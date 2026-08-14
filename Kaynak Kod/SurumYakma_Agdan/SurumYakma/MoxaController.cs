using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MOXA_CSharp_MXIO;

namespace SurumYakma
{
    /// <summary>
    /// Moxa power box ve relay box ile ilgili tüm işlemleri toplayan sınıf.
    /// Eskiden bu mantık Form1 içine (UI koduyla iç içe) yazılmıştı; burada
    /// UI'dan bağımsız, test edilebilir ve iptal edilebilir (CancellationToken)
    /// hale getirildi. Önemli fark: bir röle 100 denemede beklenen duruma
    /// gelmezse artık sessizce devam ETMİYOR, MoxaOperationException fırlatıyor
    /// — akışı çağıran (FlashWorkflow) bunu yakalayıp süreci güvenli şekilde
    /// durdurup kullanıcıya bildiriyor.
    /// </summary>
    public class MoxaOperationException : Exception
    {
        public MoxaOperationException(string message) : base(message) { }
    }

    public class MoxaController : IDisposable
    {
        private readonly AppConfig _cfg;
        private readonly int[] _hPower = new int[1];
        private readonly int[] _hRelay = new int[1];
        // MXIO_NET.dll yerel ve süreç-geneli durum taşıyan eski bir kütüphanedir.
        // Power ve Relay farklı handle kullansa da tüm yerel çağrıları tek sıraya al.
        private readonly SemaphoreSlim _mxioGate = new SemaphoreSlim(1, 1);
        private SemaphoreSlim PowerGate => _mxioGate;
        private SemaphoreSlim RelayGate => _mxioGate;
        private readonly object _lifecycleLock = new object();
        private CancellationTokenSource _keepAliveCancellation;
        private Task _keepAliveTask;
        private bool _initialized;
        private bool _disposed;

        public bool IsPowerConnected { get; private set; }
        public bool IsRelayConnected { get; private set; }
        public event Action<bool, bool> ConnectionStateChanged;

        public MoxaController(AppConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Moxa Ethernet I/O kütüphanesini başlatır.</summary>
        public Task<int> InitAsync()
        {
            return Task.Run(() =>
            {
                lock (_lifecycleLock)
                {
                    if (_initialized)
                        return MXIO_CS.MXIO_OK;

                    int ret = MXIO_CS.MXEIO_Init();
                    if (ret != MXIO_CS.MXIO_OK)
                        Logger.Warn($"MXEIO_Init beklenmeyen kod döndürdü: {ret}");
                    else
                        _initialized = true;
                    return ret;
                }
            });
        }

        /// <summary>Power box ve relay box'a bağlanır. Sonuç: her ikisi de bağlandıysa true.</summary>
        public async Task<bool> ConnectAsync()
        {
            ThrowIfDisposed();
            await InitAsync();

            IsRelayConnected = await ConnectUnderGateAsync(
                RelayGate, _cfg.RelayBoxIp, _hRelay, "Relay Box");
            IsPowerConnected = await ConnectUnderGateAsync(
                PowerGate, _cfg.PowerBoxIp, _hPower, "Power Box");

            StartKeepAlive();

            return IsPowerConnected && IsRelayConnected;
        }

        private async Task<bool> ConnectUnderGateAsync(
            SemaphoreSlim gate,
            string ip,
            int[] handle,
            string label)
        {
            await gate.WaitAsync();
            try
            {
                return await ConnectOneAsync(ip, handle, label);
            }
            finally
            {
                gate.Release();
            }
        }

        private void StartKeepAlive()
        {
            lock (_lifecycleLock)
            {
                if (_disposed || (_keepAliveTask != null && !_keepAliveTask.IsCompleted))
                    return;

                _keepAliveCancellation?.Dispose();
                _keepAliveCancellation = new CancellationTokenSource();
                CancellationToken token = _keepAliveCancellation.Token;
                _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(token), token);
                Logger.Checkpoint(
                    "MOXA_KEEPALIVE",
                    "START",
                    $"intervalSeconds={_cfg.MoxaKeepAliveIntervalSeconds}");
            }
        }

        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_cfg.MoxaKeepAliveIntervalSeconds), ct);
                    try
                    {
                        await KeepAliveOneAsync(
                            _hRelay, _cfg.RelayBoxIp, RelayGate, "Relay Box", ct);
                        await KeepAliveOneAsync(
                            _hPower, _cfg.PowerBoxIp, PowerGate, "Power Box", ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.Checkpoint("MOXA_KEEPALIVE", "FAIL", ex.Message);
                        Logger.Diagnostic("MOXA keep-alive turunda hata; sonraki turda tekrar denenecek.", ex);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Logger.Checkpoint("MOXA_KEEPALIVE", "STOP", "Uygulama kapaniyor.");
            }
        }

        private async Task KeepAliveOneAsync(
            int[] handle,
            string ip,
            SemaphoreSlim gate,
            string label,
            CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (handle[0] == 0 || !GetConnectionState(label))
                    {
                        Logger.Checkpoint("MOXA_KEEPALIVE", "RECONNECT", $"box={label}; ip={ip}");
                        if (!ConnectOne(ip, handle, label))
                            return;
                    }

                    byte[] connectionStatus = { 2 };
                    int result = ExecuteWithReconnect(
                        handle,
                        ip,
                        label,
                        () =>
                        {
                            connectionStatus[0] = 2;
                            int checkResult = MXIO_CS.MXEIO_CheckConnection(
                                handle[0],
                                1000,
                                connectionStatus);
                            return checkResult == MXIO_CS.MXIO_OK && connectionStatus[0] != 0
                                ? MXIO_CS.EIO_SOCKET_DISCONNECT
                                : checkResult;
                        },
                        ct);
                    bool ok = result == MXIO_CS.MXIO_OK;
                    if (!ok)
                        SetConnectionState(label, false);
                    Logger.Checkpoint(
                        "MOXA_KEEPALIVE",
                        ok ? "OK" : "FAIL",
                        $"box={label}; connectionStatus={connectionStatus[0]}; error={result}");
                }, ct);
            }
            finally
            {
                gate.Release();
            }
        }

        private Task<bool> ConnectOneAsync(string ip, int[] handle, string label)
        {
            return Task.Run(() => ConnectOne(ip, handle, label));
        }

        private bool ConnectOne(string ip, int[] handle, string label, int? timeoutOverrideMs = null)
        {
            if (handle[0] != 0 && GetConnectionState(label))
            {
                byte[] status = { 2 };
                int checkResult = MXIO_CS.MXEIO_CheckConnection(handle[0], 750, status);
                if (checkResult == MXIO_CS.MXIO_OK && status[0] == 0)
                {
                    Logger.Checkpoint(
                        "MOXA_CONNECT",
                        "REUSED",
                        $"box={label}; ip={ip}; handle={handle[0]}; connectionStatus={status[0]}");
                    return true;
                }
            }

            if (handle[0] != 0)
            {
                try { MXIO_CS.MXEIO_Disconnect(handle[0]); } catch { }
                handle[0] = 0;
            }

            int ret = MXIO_CS.MXEIO_Connect(
                Encoding.UTF8.GetBytes(ip),
                502,
                (uint)(timeoutOverrideMs ?? _cfg.MoxaConnectTimeoutMs),
                handle);
            bool connected = ret == MXIO_CS.MXIO_OK;
            SetConnectionState(label, connected);
            if (connected)
            {
                Logger.Info($"{label} bağlandı ({ip}, timeout={timeoutOverrideMs ?? _cfg.MoxaConnectTimeoutMs} ms, handle={handle[0]}).");
                Logger.Checkpoint("MOXA_CONNECT", "OK", $"box={label}; ip={ip}; handle={handle[0]}");
                return true;
            }

            Logger.Warn($"{label} bağlanamadı ({ip}). Hata kodu: {ret}");
            Logger.Checkpoint("MOXA_CONNECT", "FAIL", $"box={label}; ip={ip}; error={ret}");
            return false;
        }

        private void SetConnectionState(string label, bool connected)
        {
            bool oldPower = IsPowerConnected;
            bool oldRelay = IsRelayConnected;
            if (label.Equals("Power Box", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("Power", StringComparison.OrdinalIgnoreCase))
                IsPowerConnected = connected;
            else
                IsRelayConnected = connected;

            if (oldPower != IsPowerConnected || oldRelay != IsRelayConnected)
                ConnectionStateChanged?.Invoke(IsPowerConnected, IsRelayConnected);
        }

        private bool GetConnectionState(string label)
        {
            return label.Equals("Power Box", StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("Power", StringComparison.OrdinalIgnoreCase)
                ? IsPowerConnected
                : IsRelayConnected;
        }

        /// <summary>
        /// Power kanalını açar/kapatır. acKapa: 1 = aç, 0 = kapat.
        /// whichUkb: false = UKB1 (channel1), true = UKB2 (channel2).
        /// </summary>
        public async Task SetPowerAsync(bool whichUkb, uint acKapa, CancellationToken ct)
        {
            byte slot = _cfg.GetPowerSlot(whichUkb);
            byte primary = _cfg.GetPowerChannel(whichUkb);
            int secondary = _cfg.GetPowerSecondaryChannel(whichUkb);
            await SetChannelAsync(_hPower, _cfg.PowerBoxIp, PowerGate, slot,
                primary, acKapa, "Power", ct);
            if (secondary >= 0)
            {
                try
                {
                    await SetChannelAsync(_hPower, _cfg.PowerBoxIp, PowerGate, slot,
                        (byte)secondary, acKapa, "Power-Secondary", ct);
                }
                catch
                {
                    if (acKapa == 1)
                    {
                        try
                        {
                            await SetChannelAsync(_hPower, _cfg.PowerBoxIp, PowerGate, slot,
                                primary, 0, "Power-Rollback", CancellationToken.None);
                        }
                        catch { }
                    }
                    throw;
                }
                Logger.Checkpoint("MOXA_POWER_GROUP", "OK",
                    $"slot={slot}; channels={primary},{secondary}; value={acKapa}");
            }
        }

        /// <summary>Recovery röle kanalını açar/kapatır. acKapa: 1 = aç, 0 = kapat.</summary>
        public Task SetRecoveryAsync(bool whichUkb, uint acKapa, CancellationToken ct)
            => SetChannelAsync(_hRelay, _cfg.RelayBoxIp, RelayGate, _cfg.GetRecoverySlot(whichUkb),
                _cfg.GetRecoveryChannel(whichUkb), acKapa, "Recovery", ct);

        public async Task<uint> ReadPowerAsync(bool whichUkb, CancellationToken ct)
        {
            byte slot = _cfg.GetPowerSlot(whichUkb);
            byte primary = _cfg.GetPowerChannel(whichUkb);
            int secondary = _cfg.GetPowerSecondaryChannel(whichUkb);
            uint value = await ReadChannelAsync(
                _hPower, _cfg.PowerBoxIp, PowerGate, slot, primary, "Power", ct);
            if (secondary < 0)
                return value;
            uint secondValue = await ReadChannelAsync(
                _hPower, _cfg.PowerBoxIp, PowerGate, slot, (byte)secondary, "Power-Secondary", ct);
            if (value != secondValue)
                throw new MoxaOperationException(
                    $"Power grup kanalları tutarsız: slot={slot}, CH{primary}={value}, CH{secondary}={secondValue}.");
            return value;
        }

        public Task<uint> ReadRecoveryAsync(bool whichUkb, CancellationToken ct)
            => ReadChannelAsync(
                _hRelay,
                _cfg.RelayBoxIp,
                RelayGate,
                _cfg.GetRecoverySlot(whichUkb),
                _cfg.GetRecoveryChannel(whichUkb),
                "Recovery",
                ct);

        private async Task<uint> ReadChannelAsync(
            int[] handle,
            string ip,
            SemaphoreSlim gate,
            byte slot,
            byte channel,
            string label,
            CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                return await Task.Run(() => ReadChannelWithReconnect(
                    handle, ip, slot, channel, label, ct), ct);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task SetChannelAsync(
            int[] handle,
            string ip,
            SemaphoreSlim gate,
            byte slot,
            byte channel,
            uint acKapa,
            string label,
            CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                await Task.Run(() => SetChannelWithReconnect(
                    handle, ip, slot, channel, acKapa, label, ct), ct);
            }
            finally
            {
                gate.Release();
            }
        }

        private uint ReadChannelWithReconnect(
            int[] handle,
            string ip,
            byte slot,
            byte channel,
            string label,
            CancellationToken ct)
        {
            uint[] value = { 0u };
            int result = ExecuteWithReconnect(
                handle,
                ip,
                label,
                () => MXIO_CS.DO_Reads(handle[0], slot, channel, 1, value),
                ct);
            if (result != MXIO_CS.MXIO_OK)
                throw new MoxaOperationException(
                    $"{label} kanalı okunamadı (hata={result}, slot={slot}, channel={channel}).");

            Logger.Info($"{label} kanalı okundu: {(value[0] == 1 ? "ON" : "OFF")} " +
                        $"(slot={slot}, channel={channel}).");
            Logger.Checkpoint("MOXA_READ", "OK", $"box={label}; slot={slot}; channel={channel}; value={value[0]}");
            return value[0];
        }

        private void SetChannelWithReconnect(
            int[] handle,
            string ip,
            byte slot,
            byte channel,
            uint acKapa,
            string label,
            CancellationToken ct)
        {
            uint current = ReadChannelWithReconnect(handle, ip, slot, channel, label, ct);

            int attempt = 0;
            while (current != acKapa)
            {
                ct.ThrowIfCancellationRequested();

                int writeResult = ExecuteWithReconnect(
                    handle,
                    ip,
                    label,
                    () => MXIO_CS.DO_Writes(handle[0], slot, channel, 1, acKapa),
                    ct);
                attempt++;
                Logger.Checkpoint(
                    "MOXA_WRITE",
                    writeResult == MXIO_CS.MXIO_OK ? "OK" : "FAIL",
                    $"box={label}; slot={slot}; channel={channel}; target={acKapa}; attempt={attempt}; error={writeResult}");
                if (writeResult != MXIO_CS.MXIO_OK)
                    Logger.Warn($"{label} kanal yazma denemesi başarısız (hata={writeResult}, deneme={attempt}).");

                if (attempt >= _cfg.MaxRelayRetries)
                {
                    string msg = $"{label} kanalı {(acKapa == 1 ? "AÇILAMADI" : "KAPATILAMADI")} " +
                                 $"({_cfg.MaxRelayRetries} denemede sonuç alınamadı, slot={slot}, channel={channel}).";
                    Logger.Error(msg);
                    throw new MoxaOperationException(msg);
                }

                Thread.Sleep(100);
                current = ReadChannelWithReconnect(handle, ip, slot, channel, label, ct);
            }
            Logger.Info($"{label} kanalı {(acKapa == 1 ? "açıldı" : "kapatıldı")} (slot={slot}, channel={channel}, yazma={attempt}).");
        }

        private int ExecuteWithReconnect(
            int[] handle,
            string ip,
            string label,
            Func<int> operation,
            CancellationToken ct)
        {
            int result = operation();
            for (int reconnectAttempt = 1;
                 IsTransientConnectionError(result) && reconnectAttempt <= _cfg.MoxaReconnectAttempts;
                 reconnectAttempt++)
            {
                ct.ThrowIfCancellationRequested();
                Logger.Checkpoint(
                    "MOXA_RECONNECT",
                    "START",
                    $"box={label}; ip={ip}; reason={result}; attempt={reconnectAttempt}/{_cfg.MoxaReconnectAttempts}");
                Logger.Warn($"{label} soket bağlantısı kopmuş (hata={result}); yeniden bağlanılıyor " +
                            $"({reconnectAttempt}/{_cfg.MoxaReconnectAttempts})...");
                int reconnectTimeoutMs = Math.Min(_cfg.MoxaConnectTimeoutMs, 2000);
                if (!ConnectOne(ip, handle, label, reconnectTimeoutMs))
                {
                    result = MXIO_CS.EIO_SOCKET_DISCONNECT;
                    Thread.Sleep(200);
                    continue;
                }

                result = operation();
                Logger.Checkpoint(
                    "MOXA_RECONNECT",
                    result == MXIO_CS.MXIO_OK ? "OK" : "FAIL",
                    $"box={label}; operationResult={result}; attempt={reconnectAttempt}");
            }
            return result;
        }

        private static bool IsTransientConnectionError(int result)
        {
            // Moxa API: 2001 açma/gönderme/yanıt zaman aşımı, 2005 ağ bağlantısı koptu.
            return result == MXIO_CS.EIO_TIME_OUT ||
                   result == MXIO_CS.EIO_SOCKET_DISCONNECT;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MoxaController));
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _keepAliveCancellation?.Cancel();
            }

            try { _keepAliveTask?.Wait(750); } catch { }

            bool relayClosed = DisconnectOne(RelayGate, _hRelay, "Relay Box");
            bool powerClosed = DisconnectOne(PowerGate, _hPower, "Power Box");

            lock (_lifecycleLock)
            {
                if (_initialized && relayClosed && powerClosed)
                {
                    try
                    {
                        MXIO_CS.MXEIO_Exit();
                    }
                    catch (Exception ex)
                    {
                        Logger.Diagnostic("MXEIO_Exit hatasi.", ex);
                    }
                    _initialized = false;
                }
                else if (_initialized)
                {
                    Logger.Warn(
                        "MOXA işlemi kapanış süresini aştığı için MXEIO_Exit çağrısı atlandı; proses kapanışı bağlantıyı sonlandıracak.");
                    _initialized = false;
                }
            }

            _keepAliveCancellation?.Dispose();
            Logger.Checkpoint("MOXA_DISCONNECT", "OK", "Tum MOXA oturumlari kapatildi.");
        }

        private bool DisconnectOne(SemaphoreSlim gate, int[] handle, string label)
        {
            if (!gate.Wait(750))
            {
                Logger.Checkpoint(
                    "MOXA_DISCONNECT",
                    "TIMEOUT",
                    $"box={label}; gateTimeoutMs=750");
                SetConnectionState(label, false);
                return false;
            }

            try
            {
                if (handle[0] != 0)
                {
                    int oldHandle = handle[0];
                    try
                    {
                        int result = MXIO_CS.MXEIO_Disconnect(oldHandle);
                        Logger.Checkpoint(
                            "MOXA_DISCONNECT",
                            result == MXIO_CS.MXIO_OK ? "OK" : "FAIL",
                            $"box={label}; handle={oldHandle}; result={result}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Diagnostic($"{label} baglantisi kapatilirken hata.", ex);
                    }
                    finally
                    {
                        handle[0] = 0;
                    }
                }

                SetConnectionState(label, false);
                return true;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
