using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    /// <summary>
    /// UKB seri konsolunu yalnızca okur. Kullanıcı adı, parola veya shell komutu
    /// göndermez; Easy Installer, kapanış ve OFP sürüm mesajlarını doğrular.
    /// </summary>
    public sealed class UkbSerialMonitor : IDisposable
    {
        private sealed class SerialLine
        {
            public long Sequence { get; set; }
            public string Text { get; set; }
        }

        private sealed class LineWaiter
        {
            public long AfterSequence { get; set; }
            public Func<string, string> Match { get; set; }
            public TaskCompletionSource<string> Source { get; set; }
        }

        private readonly AppConfig _cfg;
        private readonly string _logPrefix;
        private string ActiveSerialPortName => _cfg.GetSerialPort(_cfg.SelectedUkb == 2);
        private int ActiveBaudRate => _cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2);
        private readonly object _sync = new object();
        private readonly object _writeSync = new object();
        public bool LastShellProbeRejectedByCommandParser { get; private set; }
        private readonly StringBuilder _pendingLine = new StringBuilder();
        private readonly List<SerialLine> _history = new List<SerialLine>();
        private readonly List<LineWaiter> _waiters = new List<LineWaiter>();
        private SerialPort _port;
        private CancellationTokenSource _readerCancellation;
        private Task _readerTask;
        private long _sequence;

        public event Action<string> LineReceived;

        public UkbSerialMonitor(AppConfig cfg, string logTag = null)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _logPrefix = string.IsNullOrWhiteSpace(logTag) ? "" : "[" + logTag.Trim() + "] ";
        }

        public bool IsOpen => _port != null && _port.IsOpen;

        public string PortName => IsOpen ? _port.PortName : ActiveSerialPortName;

        public bool HasReceivedDataSince(long afterSequence)
        {
            lock (_sync)
                return _sequence > afterSequence;
        }

        public async Task VerifySelectedPortRespondsAsync(
            long afterSequence,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme açık değil.");
            if (HasReceivedDataSince(afterSequence))
                return;

            lock (_writeSync)
                _port.Write("\r\n");

            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (HasReceivedDataSince(afterSequence))
                {
                    Logger.Checkpoint(
                        "SERIAL_PORT_GUARD",
                        "SUCCESS",
                        $"port={PortName}; baud={ActiveBaudRate}; method=newline-response");
                    return;
                }
                await Task.Delay(50, ct);
            }

            string[] activePorts = HardwareAutoConfigurator.GetAvailableSerialPorts();
            Logger.Checkpoint(
                "SERIAL_PORT_GUARD",
                "FAILED",
                $"selected={PortName}; active={string.Join(",", activePorts)}; reason=no-ukb-console-data");
            throw new InvalidOperationException(
                $"Seçili {PortName} açıldı ancak UKB seri konsolundan veri alınamadı. " +
                "Windows Aygıt Yöneticisi'nde UKB/NPort numarasının karşılığı olan COM'u bulun ve " +
                "Bağlantı Ayarları ekranından seçin. Aktif portlar: " +
                (activePorts.Length == 0 ? "yok" : string.Join(", ", activePorts)) + ".");
        }

        public void Open()
        {
            if (!_cfg.SerialMonitorEnabled || IsOpen)
                return;

            _port = new SerialPort(
                ActiveSerialPortName,
                ActiveBaudRate,
                Parity.None,
                8,
                StopBits.One)
            {
                Handshake = Handshake.None,
                Encoding = Encoding.ASCII,
                ReadTimeout = 250,
                WriteTimeout = 250,
                DtrEnable = false,
                RtsEnable = false
            };

            try
            {
                _port.Open();
            }
            catch (Exception ex)
            {
                _port.Dispose();
                _port = null;
                throw new InvalidOperationException(
                    $"UKB seri portu açılamadı ({ActiveSerialPortName}, {ActiveBaudRate}, 8N1). " +
                    "Tera Term veya başka bir uygulamanın portu kullanmadığını ve COM numarasını kontrol edin.",
                    ex);
            }

            _readerCancellation = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoopAsync(_readerCancellation.Token));
            Logger.Info($"{_logPrefix}UKB seri izleme açıldı: {ActiveSerialPortName}, {ActiveBaudRate}, 8N1.");
        }

        public long Mark()
        {
            lock (_sync)
                return _sequence;
        }

        public bool TryGetNormalBootEvidenceSince(long afterSequence, out string evidence)
        {
            lock (_sync)
            {
                foreach (SerialLine line in _history.Where(item => item.Sequence > afterSequence))
                {
                    string version = ExtractOfpVersion(line.Text);
                    if (version != null)
                    {
                        evidence = "OFP Version " + version;
                        return true;
                    }

                    if (line.Text.IndexOf("Started ofp application", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        evidence = line.Text.Trim();
                        return true;
                    }
                }
            }

            evidence = null;
            return false;
        }

        public Task<string> WaitForNormalBootEvidenceAsync(
            long afterSequence,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return WaitForMatchAsync(
                afterSequence,
                line =>
                {
                    string version = ExtractOfpVersion(line);
                    if (version != null)
                        return "OFP Version " + version;
                    return line.IndexOf(
                        "Started ofp application",
                        StringComparison.OrdinalIgnoreCase) >= 0
                        ? line.Trim()
                        : null;
                },
                timeout,
                "OTG'siz normal UKB açılışı",
                ct);
        }

        public Task<string> WaitForRecoveryReadyAsync(long afterSequence, CancellationToken ct)
        {
            return WaitForMatchAsync(
                afterSequence,
                line => IsEasyInstallerBootEvidence(line) ? line : null,
                TimeSpan.FromSeconds(_cfg.SerialRecoveryTimeoutSeconds),
                "Toradex Easy Installer başlangıç mesajı veya recovery promptu",
                ct);
        }

        internal static bool IsEasyInstallerBootEvidence(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string value = line.Trim();
            // U-Boot FIT metadata (for example "Description: tezi-initramfs") only
            // proves that the ramdisk is loading. It is not a Linux-ready signal.
            return value.IndexOf("Welcome to the Toradex Easy Installer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.StartsWith("Toradex Easy Installer ", StringComparison.OrdinalIgnoreCase) ||
                   IsRootShellPrompt(value);
        }

        private static bool IsRootShellPrompt(string line)
        {
            string value = (line ?? string.Empty).Trim();
            return value.Equals("/ #", StringComparison.Ordinal) ||
                   value.Equals("#", StringComparison.Ordinal) ||
                   value.EndsWith(":~#", StringComparison.Ordinal) ||
                   value.EndsWith(":/#", StringComparison.Ordinal);
        }

        public async Task<bool> ProbeInteractiveShellAsync(CancellationToken ct)
        {
            if (!IsOpen)
                return false;

            LastShellProbeRejectedByCommandParser = false;
            string token = Guid.NewGuid().ToString("N");
            string marker = "__SURUMYAKMA_SHELL_OK_" + token + "__";
            long mark = Mark();
            lock (_writeSync)
                _port.Write("\n" + "echo " + marker + "\n");

            Logger.Checkpoint("TEZI_SERIAL_SHELL_PROBE", "COMMAND_SENT", "timeoutSec=3");
            try
            {
                string result = await WaitForMatchAsync(
                    mark,
                    line => line.Trim().Equals(marker, StringComparison.Ordinal) ? marker :
                            line.IndexOf("ERR FORMAT", StringComparison.OrdinalIgnoreCase) >= 0 ? "ERR_FORMAT" : null,
                    TimeSpan.FromSeconds(3),
                    "Easy Installer etkileşimli seri kabuk doğrulaması",
                    ct);
                if (result == "ERR_FORMAT")
                {
                    LastShellProbeRejectedByCommandParser = true;
                    Logger.Checkpoint(
                        "TEZI_SERIAL_SHELL_PROBE",
                        "UNSUPPORTED",
                        "response=ERR_FORMAT; action=stop-probe-retries-and-use-official-zeroconf");
                    return false;
                }
                Logger.Checkpoint("TEZI_SERIAL_SHELL_PROBE", "SUCCESS");
                return true;
            }
            catch (TimeoutException)
            {
                Logger.Checkpoint(
                    "TEZI_SERIAL_SHELL_PROBE",
                    "UNAVAILABLE",
                    "action=official-zeroconf-flow; serial-cli-disabled");
                return false;
            }
        }
        public Task<string> WaitForShutdownAsync(long afterSequence, CancellationToken ct)
        {
            return WaitForMatchAsync(
                afterSequence,
                line => ContainsAny(
                    line,
                    "reboot: Power down",
                    "Power down",
                    "Powering off",
                    "System halted",
                    "Reached target Power-Off") ? line : null,
                TimeSpan.FromSeconds(_cfg.InstallationTimeoutSeconds),
                "kurulum sonu kapanış mesajı",
                ct);
        }

        public Task<string> WaitForOfpVersionAsync(long afterSequence, CancellationToken ct)
        {
            return WaitForMatchAsync(
                afterSequence,
                ExtractOfpVersion,
                TimeSpan.FromSeconds(_cfg.SerialBootTimeoutSeconds),
                "normal açılıştaki OFP Version mesajı",
                ct);
        }

        public Task<string> WaitForNetworkUpAsync(
            long afterSequence,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return WaitForMatchAsync(
                afterSequence,
                line => IsPhysicalEthernetLinkUpLine(line) ? line : null,
                timeout,
                "normal açılıştaki fiziksel Ethernet Link Up mesajı",
                ct);
        }

        private static bool IsPhysicalEthernetLinkUpLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.IndexOf("Link is Up", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (line.IndexOf("usb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("can", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            // Nihai Link Up sonucu yalnızca UKB'nin fiziksel eth0 portundan gelen
            // kernel satırıyla doğrulanır. eth1 veya "link becomes ready" kabul edilmez.
            return line.IndexOf("eth0:", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public Task<string> VerifyHttpHealthAsync(string healthUrl, CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme açık değil.");
            if (!Uri.TryCreate(healthUrl, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                healthUrl.IndexOfAny(new[] { '\'', '"', ';', '&', '|', '`', '$', '\r', '\n' }) >= 0)
                throw new ArgumentException("Güvenli olmayan veya geçersiz HTTP health adresi.", nameof(healthUrl));

            long mark = Mark();
            SendHttpHealthProbe(healthUrl);

            return WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(TeziHttpServer.HealthResponse, StringComparison.Ordinal)
                    ? line.Trim()
                    : null,
                TimeSpan.FromSeconds(10),
                "Easy Installer → PC HTTP health yanıtı",
                ct);
        }

        public void SendHttpHealthProbe(string healthUrl)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme açık değil.");
            if (!Uri.TryCreate(healthUrl, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                healthUrl.IndexOfAny(new[] { '\'', '"', ';', '&', '|', '`', '$', '\r', '\n' }) >= 0)
                throw new ArgumentException("Güvenli olmayan veya geçersiz HTTP health adresi.", nameof(healthUrl));

            string command =
                "if wget -q -T 5 -t 1 -O /dev/null '" + healthUrl +
                "'; then echo " + TeziHttpServer.HealthResponse +
                "; else echo SURUMYAKMA_HTTP_FAIL; fi";
            lock (_writeSync)
                _port.Write(command + "\n");

            Logger.Info("[serial-tx] Easy Installer → PC HTTP health testi gönderildi: " + healthUrl);
            Logger.Checkpoint(
                "SERIAL_HTTP_TEST",
                "COMMAND_SENT",
                "url=" + healthUrl + "; connectTimeoutSec=5; attempts=1");
        }

        /// <summary>
        /// Bir TEZI HTTP kaynagini hedef UKB uzerinden indirip /dev/null'a atar.
        /// Dosya icerigi seri porta veya loga yazilmaz; yalnizca sonuc isareti doner.
        /// </summary>
        public async Task VerifyHttpResourceAsync(string resourceUrl, CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme acik degil.");
            if (!Uri.TryCreate(resourceUrl, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                resourceUrl.IndexOfAny(new[] { '\'', '"', ';', '&', '|', '`', '$', '\r', '\n' }) >= 0)
                throw new ArgumentException("Guvenli olmayan veya gecersiz HTTP kaynak adresi.", nameof(resourceUrl));

            string token = Guid.NewGuid().ToString("N");
            string okMarker = "__SURUMYAKMA_HTTP_OK_" + token + "__";
            string failMarker = "__SURUMYAKMA_HTTP_FAIL_" + token + "__";
            long mark = Mark();
            string command =
                "attempt=1; ok=0; while [ \"$attempt\" -le 3 ]; do " +
                "if wget -q -T 8 -O /dev/null '" + resourceUrl + "'; then ok=1; break; fi; " +
                "attempt=$((attempt+1)); sleep 2; done; " +
                "if [ \"$ok\" = 1 ]; then echo " + okMarker +
                "; else echo " + failMarker + "; fi";

            lock (_writeSync)
            {
                _port.Write(command + "\n");
            }

            Logger.Checkpoint("SERIAL_HTTP_RESOURCE", "COMMAND_SENT", "url=" + resourceUrl);
            string result = await WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(okMarker, StringComparison.Ordinal) ? "OK" :
                        line.Trim().Equals(failMarker, StringComparison.Ordinal) ? "FAIL" : null,
                TimeSpan.FromSeconds(30),
                "Easy Installer -> PC HTTP kaynak kontrol sonucu",
                ct);

            if (result != "OK")
                throw new InvalidOperationException(
                    "Easy Installer HTTP kaynagini indiremedi: " + resourceUrl);
            Logger.Checkpoint("SERIAL_HTTP_RESOURCE", "SUCCESS", "url=" + resourceUrl);
        }

        /// <summary>
        /// Zeroconf taramasi gec kalirsa ayni HTTP listesini TEZI'nin kendi CLI
        /// arayuzune dogrudan ekler. Bu ayar RAM'deki Easy Installer oturumuna aittir.
        /// </summary>
        public async Task AddTeziFeedAsync(string feedUrl, CancellationToken ct)
        {
            ValidateSafeHttpUrl(feedUrl, nameof(feedUrl));
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme acik degil.");

            string token = Guid.NewGuid().ToString("N");
            string okMarker = "__SURUMYAKMA_FEED_ADD_OK_" + token + "__";
            string failMarker = "__SURUMYAKMA_FEED_ADD_FAIL_" + token + "__";
            long mark = Mark();
            string command =
                "tool=/usr/bin/tezictl; [ -x \"$tool\" ] || tool=$(command -v tezictl 2>/dev/null); " +
                "if [ -n \"$tool\" ] && [ -x \"$tool\" ] && \"$tool\" feed-add '" + feedUrl +
                "' >/var/volatile/surumyakma-feed-add.log 2>&1; then echo " + okMarker +
                "; else echo " + failMarker + "; fi";
            lock (_writeSync)
                _port.Write(command + "\n");

            Logger.Checkpoint("TEZICTL_FEED_ADD", "COMMAND_SENT", "url=" + feedUrl);
            string result = await WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(okMarker, StringComparison.Ordinal) ? "OK" :
                        line.Trim().Equals(failMarker, StringComparison.Ordinal) ? "FAIL" : null,
                TimeSpan.FromSeconds(20),
                "tezictl feed-add sonucu",
                ct);
            if (result != "OK")
                throw new InvalidOperationException("TEZI CLI feed-add komutunu kabul etmedi.");
            Logger.Checkpoint("TEZICTL_FEED_ADD", "SUCCESS", "url=" + feedUrl);
        }

        /// <summary>
        /// autoinstall baslamazsa yalnizca verilen sunucu/paket URI'siyle eslesen
        /// imaji TEZI CLI uzerinden arka planda baslatir.
        /// </summary>
        public async Task StartTeziInstallFromCliAsync(string packageBaseUrl, CancellationToken ct)
        {
            ValidateSafeHttpUrl(packageBaseUrl, nameof(packageBaseUrl));
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme acik degil.");

            string token = Guid.NewGuid().ToString("N");
            string okMarker = "__SURUMYAKMA_INSTALL_CLI_OK_" + token + "__";
            string failMarker = "__SURUMYAKMA_INSTALL_CLI_FAIL_" + token + "__";
            long mark = Mark();
            string command =
                "tool=/usr/bin/tezictl; [ -x \"$tool\" ] || tool=$(command -v tezictl 2>/dev/null); " +
                "if [ -z \"$tool\" ] || [ ! -x \"$tool\" ]; then echo " + failMarker + "; else " +
                "state=$(\"$tool\" status 2>/dev/null); " +
                "if [ \"$state\" = installing ] || [ \"$state\" = installed ]; then echo " + okMarker + "; else " +
                "idx=$(\"$tool\" image-list 2>/dev/null | awk -v needle='" + packageBaseUrl +
                "' '/^\\[[0-9]+\\]/{i=$1; gsub(/\\[|\\]/,\"\",i)} index($0,needle){print i; exit}'); " +
                "if [ -n \"$idx\" ]; then \"$tool\" image-install \"$idx\" --accept-all-licenses " +
                ">/var/volatile/surumyakma-image-install.log 2>&1 & echo " + okMarker +
                "; else echo " + failMarker + "; fi; fi; fi";
            lock (_writeSync)
                _port.Write(command + "\n");

            Logger.Checkpoint("TEZICTL_IMAGE_INSTALL", "COMMAND_SENT", "packageBaseUrl=" + packageBaseUrl);
            string result = await WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(okMarker, StringComparison.Ordinal) ? "OK" :
                        line.Trim().Equals(failMarker, StringComparison.Ordinal) ? "FAIL" : null,
                TimeSpan.FromSeconds(30),
                "tezictl image-install baslatma sonucu",
                ct);
            if (result != "OK")
                throw new InvalidOperationException("TEZI CLI listesinde secili ag paketi bulunamadi veya kurulum baslatilamadi.");
            Logger.Checkpoint("TEZICTL_IMAGE_INSTALL", "SUCCESS", "packageBaseUrl=" + packageBaseUrl);
        }

        private static void ValidateSafeHttpUrl(string value, string parameterName)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                value.IndexOfAny(new[] { '\'', '"', ';', '&', '|', '`', '$', '\r', '\n' }) >= 0)
                throw new ArgumentException("Guvenli olmayan veya gecersiz HTTP adresi.", parameterName);
        }

        /// <summary>
        /// Easy Installer feed kesfini tamamlayamazsa hedefteki TEZI gunlugunu ve
        /// calisan TEZI sureclerini salt-okunur komutlarla oturum loguna aktarir.
        /// Komut herhangi bir dosyayi veya cihaz durumunu degistirmez.
        /// </summary>
        public async Task CaptureTeziDiagnosticsAsync(CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme acik degil.");

            const string endMarker = "__SURUMYAKMA_TEZI_DIAG_END__";
            long mark = Mark();
            string command =
                "echo __SURUMYAKMA_TEZI_DIAG_BEGIN__; " +
                "echo __SURUMYAKMA_SYSTEM__; uname -a 2>&1; date 2>&1; " +
                "echo __SURUMYAKMA_NETWORK__; ip -4 addr show 2>&1; ip route 2>&1; " +
                "echo __SURUMYAKMA_PROCESSES__; " +
                "ps w 2>&1 | grep '[t]ezi'; " +
                "echo __SURUMYAKMA_TEZI_LOGS__; found=0; " +
                "for f in /var/volatile/tezi.log /var/log/tezi.log /tmp/tezi.log; do " +
                "if [ -f \"$f\" ]; then found=1; echo __TEZI_LOG_FILE__\"$f\"; tail -n 200 \"$f\" 2>&1; fi; done; " +
                "if [ \"$found\" = 0 ]; then echo __SURUMYAKMA_TEZI_LOG_MISSING__; fi; " +
                "echo " + endMarker;

            lock (_writeSync)
            {
                _port.Write(command + "\n");
            }

            Logger.Checkpoint("TEZI_TARGET_DIAGNOSTICS", "COMMAND_SENT");
            await WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(endMarker, StringComparison.Ordinal) ? line.Trim() : null,
                TimeSpan.FromSeconds(20),
                "Easy Installer teshis komutu bitis isareti",
                ct);
            Logger.Checkpoint("TEZI_TARGET_DIAGNOSTICS", "CAPTURED");
        }

        /// <summary>
        /// Yalnizca payload aktarimi baslamadan once TEZI Qt arayuzunu yeniden
        /// baslatir. Easy Installer RAM'de calistigi icin dahili flasha dokunmaz.
        /// Ilk zeroconf taramasi feed ilani hazirken tekrar gerceklesir.
        /// </summary>
        public async Task RestartTeziUiAsync(CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme acik degil.");

            string token = Guid.NewGuid().ToString("N");
            string okMarker = "__SURUMYAKMA_TEZI_RESTART_OK_" + token + "__";
            string failMarker = "__SURUMYAKMA_TEZI_RESTART_FAIL_" + token + "__";
            long mark = Mark();
            string command =
                "oldpid=$(pidof tezi); if [ -n \"$oldpid\" ]; then kill $oldpid; fi; " +
                "sleep 2; if ! pidof tezi >/dev/null 2>&1; then " +
                "/usr/bin/tezi -autoinstall -platform wayland >>/var/volatile/tezi.log 2>&1 & fi; " +
                "sleep 3; if pidof tezi >/dev/null 2>&1; then echo " + okMarker +
                "; else echo " + failMarker + "; fi";

            lock (_writeSync)
            {
                _port.Write(command + "\n");
            }

            Logger.Checkpoint("TEZI_UI_RESTART", "COMMAND_SENT");
            string result = await WaitForMatchAsync(
                mark,
                line => line.Trim().Equals(okMarker, StringComparison.Ordinal) ? "OK" :
                        line.Trim().Equals(failMarker, StringComparison.Ordinal) ? "FAIL" : null,
                TimeSpan.FromSeconds(15),
                "TEZI arayuz yeniden baslatma sonucu",
                ct);
            if (result != "OK")
                throw new InvalidOperationException("TEZI arayuz sureci yeniden baslatilamadi.");
            Logger.Checkpoint("TEZI_UI_RESTART", "SUCCESS");
        }

        private async Task<string> WaitForMatchAsync(
            long afterSequence,
            Func<string, string> matcher,
            TimeSpan timeout,
            string description,
            CancellationToken ct)
        {
            if (!IsOpen)
                throw new InvalidOperationException("UKB seri izleme açık değil.");

            var waiter = new LineWaiter
            {
                AfterSequence = afterSequence,
                Match = matcher,
                Source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            lock (_sync)
            {
                foreach (SerialLine line in _history.Where(item => item.Sequence > afterSequence))
                {
                    string existing = matcher(line.Text);
                    if (existing != null)
                        return existing;
                }
                _waiters.Add(waiter);
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task delay = Task.Delay(timeout, linked.Token);
                Task completed = await Task.WhenAny(waiter.Source.Task, delay);
                if (completed == waiter.Source.Task)
                {
                    linked.Cancel();
                    return await waiter.Source.Task;
                }
            }

            lock (_sync)
                _waiters.Remove(waiter);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Seri portta {description} {timeout.TotalSeconds:0} saniye içinde görülmedi.");
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && IsOpen)
                {
                    string chunk = _port.ReadExisting();
                    if (chunk.Length == 0)
                    {
                        await Task.Delay(40, ct);
                        continue;
                    }
                    Consume(chunk);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("UKB seri port okuma işlemi durdu", ex);
                FailWaiters(ex);
            }
        }

        private void Consume(string chunk)
        {
            foreach (char character in chunk)
            {
                if (character == '\n')
                {
                    string line = _pendingLine.ToString().TrimEnd('\r');
                    _pendingLine.Clear();
                    if (!string.IsNullOrWhiteSpace(line))
                        PublishLine(line);
                }
                else if (character != '\0')
                {
                    _pendingLine.Append(character);
                    if (_pendingLine.Length > 16384)
                    {
                        PublishLine(_pendingLine.ToString());
                        _pendingLine.Clear();
                    }
                }
            }
        }

        private void PublishLine(string line)
        {
            List<Tuple<LineWaiter, string>> matches = new List<Tuple<LineWaiter, string>>();
            lock (_sync)
            {
                long sequence = ++_sequence;
                _history.Add(new SerialLine { Sequence = sequence, Text = line });
                if (_history.Count > 4000)
                    _history.RemoveRange(0, _history.Count - 4000);

                foreach (LineWaiter waiter in _waiters.ToArray())
                {
                    if (sequence <= waiter.AfterSequence)
                        continue;
                    string result = waiter.Match(line);
                    if (result == null)
                        continue;
                    _waiters.Remove(waiter);
                    matches.Add(Tuple.Create(waiter, result));
                }
            }

            Logger.Info(_logPrefix + "[serial] " + line);
            LineReceived?.Invoke(line);
            foreach (Tuple<LineWaiter, string> match in matches)
                match.Item1.Source.TrySetResult(match.Item2);
        }

        private void FailWaiters(Exception ex)
        {
            LineWaiter[] waiters;
            lock (_sync)
            {
                waiters = _waiters.ToArray();
                _waiters.Clear();
            }
            foreach (LineWaiter waiter in waiters)
                waiter.Source.TrySetException(ex);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string ExtractOfpVersion(string line)
        {
            Match match = Regex.Match(
                line ?? "",
                @"OFP\s+Version\s*[:#-]?\s*(?<version>\d+(?:\.\d+){2,})",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["version"].Value : null;
        }

        public void Dispose()
        {
            _readerCancellation?.Cancel();
            try { _port?.Close(); } catch { }
            try { _readerTask?.Wait(1000); } catch { }
            _port?.Dispose();
            _readerCancellation?.Dispose();
            _port = null;
            _readerTask = null;
            _readerCancellation = null;
            FailWaiters(new ObjectDisposedException(nameof(UkbSerialMonitor)));
        }
    }
}
