using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    public class FlashProgress
    {
        public int Percent { get; set; }
        public string Status { get; set; }
    }

    public class FlashRequest
    {
        public bool WhichUkb { get; set; }         // false = UKB1, true = UKB2
        public bool IsVersionFromPc { get; set; }  // true: PC'deki sürümler listesinden, false: flash bellekte zaten duran bir sürümden
        public string VersionPath { get; set; }    // IsVersionFromPc=true ise PC'deki kaynak klasör, değilse flash'taki *build.0 klasörü
        public string DriveRoot { get; set; }      // Örn: "D:\"
    }

    /// <summary>
    /// Tek bir UKB'ye sürüm yakma işleminin BAŞTAN SONA async akışı.
    /// Eskiden bu akış Form1 içinde birbirini tetikleyen Timer.Tick olay
    /// zincirleriyle ve arada bloklayan while/MessageBox döngüleriyle
    /// yapılıyordu. Burada tek bir async metod olarak, adım adım okunabilir,
    /// her adımda loglanan, CancellationToken ile her an iptal edilebilen ve
    /// hata durumunda güvenli şekilde duran bir yapıya kavuşturuldu.
    ///
    /// Fiziksel müdahale gerektiren iki nokta var (kablo takma/çıkarma) —
    /// bunlar insan eliyle yapılmak zorunda, ama artık kullanıcı her
    /// kontrolde "Tamam"a basmak zorunda değil: talimat bir kere gösterilir,
    /// program arka planda cihazı algılayana kadar sessizce bekler.
    /// </summary>
    public class FlashWorkflow
    {
        private const string RecoveryToolResourceName = "SurumYakma.Tools.Tezi.Recovery.uuu.exe";
        private const string RecoveryScriptResourceName = "SurumYakma.Tools.Tezi.Recovery.uuu.auto";
        private const string ExpectedRecoveryToolSha256 =
            "F6B76A6246BEFABEADFEBDC1CBFE58F35939596CAF7B78717CEAB599B0C85027";

        private readonly AppConfig _cfg;
        private readonly MoxaController _moxa;
        private readonly VersionManager _versions;
        private readonly UkbSerialMonitor _serial;

        public event Action OnWaitingForOtgConnect;
        public event Action OnWaitingForOtgDisconnect;
        public event Action OnOtgCableWaitWarning;
        public event Action<bool> OnCriticalPhaseChanged;
        public event Action OnManualRecoveryRequired;
        public Func<string, CancellationToken, Task> WaitForUkbMediaReadyAsync { get; set; }

        public FlashWorkflow(AppConfig cfg, MoxaController moxa, VersionManager versions, UkbSerialMonitor serial)
        {
            _cfg = cfg;
            _moxa = moxa;
            _versions = versions;
            _serial = serial;
        }

        public async Task RunAsync(FlashRequest req, IProgress<FlashProgress> progress, CancellationToken ct)
        {
            string ukbLabel = "UKB" + _cfg.GetTargetNumber(req.WhichUkb);
            Logger.Info($"=== Sürüm yakma başladı: {ukbLabel}, kaynak={req.VersionPath} ===");
            Logger.Checkpoint(
                "WORKFLOW_START",
                "OK",
                $"ukb={ukbLabel}; source={req.VersionPath}; drive={req.DriveRoot}; fromPc={req.IsVersionFromPc}");
            bool criticalPhase = false;
            bool recoveryEnabled = false;
            bool powerCommandedOn = false;
            bool shutdownObserved = false;
            bool retainSerialForManualRecovery = false;
            string expectedOfpVersion = null;

            try
            {
                EnsureRecoveryTools();

                if (!VersionManager.IsTeziPackage(req.VersionPath))
                    throw new InvalidOperationException(
                        "Üretim otomasyonu yalnızca tam TEZI *build.0 paketiyle çalışır; ham OFP paketi yüklenemez.");
                if (!_cfg.SerialMonitorEnabled)
                    throw new InvalidOperationException("Üretim sürüm yüklemesi için seri izleme etkin olmalıdır.");

                expectedOfpVersion = VersionManager.GetExpectedOfpVersion(req.VersionPath);
                Report(progress, 2, $"{ukbLabel}: donanım ön kontrolü yapılıyor...");
                uint initialPowerState = await _moxa.ReadPowerAsync(req.WhichUkb, ct);
                if (initialPowerState != 0)
                    throw new InvalidOperationException(
                        $"{ukbLabel} Power kanalı işlem başında ON. SuperSonic Tester/Moxa ioAdmin uygulamalarını kapatın, " +
                        "UKB gücünü güvenli şekilde OFF konumuna getirin ve işlemi yeniden başlatın. " +
                        "Recovery modu güç verilmeden önce ayarlanmalıdır.");
                Logger.Checkpoint("PREFLIGHT_POWER", "OK", $"ukb={ukbLabel}; value={initialPowerState}");

                _serial.Open();
                long recoverySerialMark = _serial.Mark();
                Logger.Info($"Beklenen normal açılış OFP sürümü: {expectedOfpVersion}");

                Report(progress, 5, $"{ukbLabel}: sürüm hazırlanıyor...");
                if (req.IsVersionFromPc)
                    await Task.Run(() => _versions.PrepareVersionFromPc(req.VersionPath, req.DriveRoot), ct);
                else
                    await Task.Run(() => _versions.PrepareVersionAlreadyOnFlash(req.VersionPath), ct);
                ct.ThrowIfCancellationRequested();

                Report(progress, 15, $"{ukbLabel}: recovery modu açılıyor...");
                await _moxa.SetRecoveryAsync(req.WhichUkb, 1, ct);
                recoveryEnabled = true;

                Report(progress, 25, $"{ukbLabel}: flash belleğin SIM PC'den çıkarılması bekleniyor...");
                OnWaitingForOtgConnect?.Invoke();
                await NetworkUtils.WaitForDriveDisconnectAsync(
                    req.DriveRoot,
                    TimeSpan.FromSeconds(_cfg.OtgWaitTimeoutSeconds),
                    ct);

                Report(progress, 30, $"{ukbLabel}: flash belleğin UKB sürüm portuna takılması bekleniyor...");
                if (WaitForUkbMediaReadyAsync == null)
                    throw new InvalidOperationException("UKB flash bellek hazır bildirimi tanımlı değil.");
                await WaitForUkbMediaReadyAsync(ukbLabel, ct);

                Report(progress, 35, $"{ukbLabel}: güç açılıyor (recovery aktif ediliyor)...");
                await _moxa.SetPowerAsync(req.WhichUkb, 1, ct);
                powerCommandedOn = true;
                criticalPhase = true;
                OnCriticalPhaseChanged?.Invoke(true);

                Report(progress, 40, $"{ukbLabel}: Toradex recovery yükleyicisi çalıştırılıyor...");
                await RunRecoveryToolAsync(progress, ukbLabel, req.WhichUkb, ct);

                Report(progress, 70, $"{ukbLabel}: UKB ağda bekleniyor...");
                await NetworkUtils.WaitUntilReachableAsync(_cfg.UkbTargetIp, _cfg.PingTimeoutMs,
                    TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds), ct);

                Report(progress, 74, $"{ukbLabel}: Easy Installer seri konsoldan doğrulanıyor...");
                string recoveryLine = await _serial.WaitForRecoveryReadyAsync(recoverySerialMark, ct);
                Logger.Info("Easy Installer seri doğrulaması: " + recoveryLine);

                Report(progress, 78, $"{ukbLabel}: otomatik kurulum ve seri kapanış mesajı bekleniyor...");
                string shutdownLine = await _serial.WaitForShutdownAsync(recoverySerialMark, ct);
                Logger.Info("Kurulum kapanış seri doğrulaması: " + shutdownLine);
                shutdownObserved = true;

                Report(progress, 82, $"{ukbLabel}: UKB'nin ağdan ayrılması doğrulanıyor...");
                await NetworkUtils.WaitUntilUnreachableAsync(_cfg.UkbTargetIp, _cfg.PingTimeoutMs,
                    TimeSpan.FromSeconds(_cfg.InstallationTimeoutSeconds), ct);
                criticalPhase = false;
                OnCriticalPhaseChanged?.Invoke(false);

                Report(progress, 85, $"{ukbLabel}: bağlantılar toparlanıyor...");
                await _moxa.ConnectAsync(); // uzun bekleme sonrası Moxa soketleri yeniden bağlanıyor (orijinal koddaki davranış korunuyor)

                Report(progress, 90, $"{ukbLabel}: güç kapatılıyor...");
                await _moxa.SetPowerAsync(req.WhichUkb, 0, ct);
                powerCommandedOn = false;

                Report(progress, 93, $"{ukbLabel}: recovery modu kapatılıyor...");
                await _moxa.SetRecoveryAsync(req.WhichUkb, 0, ct);
                recoveryEnabled = false;

                Report(progress, 94, $"{ukbLabel}: normal açılışta OFP {expectedOfpVersion} doğrulanıyor...");
                long normalBootMark = _serial.Mark();
                await _moxa.SetPowerAsync(req.WhichUkb, 1, ct);
                powerCommandedOn = true;
                string observedOfpVersion = await _serial.WaitForOfpVersionAsync(normalBootMark, ct);
                if (!string.Equals(observedOfpVersion, expectedOfpVersion, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Kurulum sonrası OFP sürümü eşleşmedi. Beklenen: {expectedOfpVersion}, okunan: {observedOfpVersion}.");
                Logger.Info($"Kurulum sonrası OFP sürümü doğrulandı: {observedOfpVersion}");

                Report(progress, 98, $"{ukbLabel}: doğrulama tamamlandı; güç kapatılıyor...");
                await _moxa.SetPowerAsync(req.WhichUkb, 0, ct);
                powerCommandedOn = false;

                OnWaitingForOtgDisconnect?.Invoke();

                Report(progress, 100, $"{ukbLabel}: tamamlandı; UKB kapalı bırakıldı.");
                Logger.Checkpoint("WORKFLOW_END", "SUCCESS", $"ukb={ukbLabel}; expectedOfp={expectedOfpVersion}");
                Logger.Info($"=== Sürüm yakma tamamlandı: {ukbLabel} ===");
            }
            catch (OperationCanceledException)
            {
                Logger.Warn($"{ukbLabel}: işlem kullanıcı tarafından iptal edildi.");
                Logger.Checkpoint(
                    "WORKFLOW_END",
                    "CANCELLED",
                    $"ukb={ukbLabel}; critical={criticalPhase}; powerOn={powerCommandedOn}; recovery={recoveryEnabled}; shutdown={shutdownObserved}");
                if (criticalPhase)
                {
                    retainSerialForManualRecovery = true;
                    OnCriticalPhaseChanged?.Invoke(false);
                    OnManualRecoveryRequired?.Invoke();
                    throw new InvalidOperationException(
                        "İşlem UKB açıkken durduruldu. Cihaza güç kesmeyin; recovery/kurulum durumunu seri porttan kontrol edin.");
                }

                await TrySafeCleanupAsync(req.WhichUkb, powerCommandedOn, recoveryEnabled, shutdownObserved);
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"{ukbLabel}: işlem hata ile durdu", ex);
                Logger.Checkpoint(
                    "WORKFLOW_END",
                    "FAILED",
                    $"ukb={ukbLabel}; exception={ex.GetType().Name}; critical={criticalPhase}; " +
                    $"powerOn={powerCommandedOn}; recovery={recoveryEnabled}; shutdown={shutdownObserved}");
                if (criticalPhase)
                {
                    retainSerialForManualRecovery = true;
                    OnCriticalPhaseChanged?.Invoke(false);
                    OnManualRecoveryRequired?.Invoke();
                    throw new InvalidOperationException(
                        "Kritik yükleme aşamasında hata oluştu. UKB gücünü otomatik olarak kesmedim. " +
                        "Cihazın durumunu seri porttan kontrol edip onaylı kurtarma prosedürünü uygulayın.",
                        ex);
                }

                await TrySafeCleanupAsync(req.WhichUkb, powerCommandedOn, recoveryEnabled, shutdownObserved);
                throw;
            }
            finally
            {
                if (!retainSerialForManualRecovery)
                    _serial.Dispose();
            }
        }

        private async Task TrySafeCleanupAsync(
            bool whichUkb,
            bool powerCommandedOn,
            bool recoveryEnabled,
            bool shutdownObserved)
        {
            try
            {
                if (powerCommandedOn && shutdownObserved)
                    await _moxa.SetPowerAsync(whichUkb, 0, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error("Hata sonrası power kapatma denemesi başarısız", ex);
            }

            try
            {
                if (recoveryEnabled && !powerCommandedOn)
                    await _moxa.SetRecoveryAsync(whichUkb, 0, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error("Hata sonrası recovery kapatma denemesi başarısız", ex);
            }
        }

        private async Task RunRecoveryToolAsync(
            IProgress<FlashProgress> progress,
            string ukbLabel,
            bool whichUkb,
            CancellationToken ct)
        {
            EnsureRecoveryTools();
            string workingDirectory = Path.GetDirectoryName(_cfg.RecoveryBatPath);
            string uuuPath = Path.Combine(workingDirectory, "recovery", "uuu.exe");
            if (!File.Exists(uuuPath))
                throw new FileNotFoundException("Toradex UUU aracı bulunamadı.", uuuPath);
            long recoverySerialMark = _serial?.Mark() ?? 0;

            var psi = new ProcessStartInfo(uuuPath, "recovery")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var proc = new Process { StartInfo = psi })
            {
                int waitingForKnownUsb = 0;
                long knownUsbWaitStartedUtcTicks = 0;
                proc.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        Logger.Info("[uuu] " + args.Data);
                        UpdateKnownUsbWaitState(
                            args.Data,
                            ref waitingForKnownUsb,
                            ref knownUsbWaitStartedUtcTicks);
                    }
                };
                proc.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        Logger.Warn("[uuu] " + args.Data);
                        UpdateKnownUsbWaitState(
                            args.Data,
                            ref waitingForKnownUsb,
                            ref knownUsbWaitStartedUtcTicks);
                    }
                };

                if (!proc.Start())
                    throw new InvalidOperationException("Toradex UUU aracı başlatılamadı.");

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_cfg.RecoveryBatTimeoutSeconds);
                DateTime warningAt = DateTime.MaxValue;
                DateTime retryAt = DateTime.MaxValue;
                long observedWaitStartTicks = 0;
                bool warningSent = false;
                int recoveryPowerCycles = 0;

                try
                {
                    while (!proc.HasExited)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (DateTime.UtcNow > deadline)
                        {
                            throw new TimeoutException("Toradex UUU aracı beklenen sürede tamamlanmadı.");
                        }

                        if (_serial != null &&
                            _serial.TryGetNormalBootEvidenceSince(recoverySerialMark, out string normalBootEvidence))
                        {
                            byte recoverySlot = _cfg.GetRecoverySlot(whichUkb);
                            byte recoveryChannel = _cfg.GetRecoveryChannel(whichUkb);
                            Logger.Checkpoint(
                                "RECOVERY_MODE_GUARD",
                                "FAILED",
                                $"ukb={ukbLabel}; slot={recoverySlot}; channel={recoveryChannel}; evidence={normalBootEvidence}");
                            throw new InvalidOperationException(
                                $"{ukbLabel} Recovery yerine normal OFP ile açıldı ({normalBootEvidence}). " +
                                $"Recovery Moxa MOD/slot ve kanal eşlemesini kontrol edin: MOD{recoverySlot}, CH{recoveryChannel}.");
                        }

                        if (Volatile.Read(ref waitingForKnownUsb) == 1)
                        {
                            long waitStartTicks = Volatile.Read(ref knownUsbWaitStartedUtcTicks);
                            if (waitStartTicks > 0 && waitStartTicks != observedWaitStartTicks)
                            {
                                observedWaitStartTicks = waitStartTicks;
                                DateTime waitStartedAt = new DateTime(waitStartTicks, DateTimeKind.Utc);
                                warningAt = waitStartedAt + TimeSpan.FromSeconds(10);
                                retryAt = waitStartedAt + TimeSpan.FromSeconds(25);
                                warningSent = false;
                                recoveryPowerCycles = 0;
                                Logger.Checkpoint(
                                    "OTG_USB_DEVICE_WAIT",
                                    "START",
                                    $"ukb={ukbLabel}; warningAfterSeconds=10; firstRecoveryRetryAfterSeconds=25");
                            }

                            // UUU bekleme satırı işlenirken başlangıç zamanı henüz görünür
                            // değilse yanlış/erken uyarı üretme; sonraki döngüyü bekle.
                            if (waitStartTicks <= 0)
                            {
                                await Task.Delay(250, ct);
                                continue;
                            }

                            if (!warningSent && DateTime.UtcNow >= warningAt)
                            {
                                warningSent = true;
                                long elapsedMs = Math.Max(
                                    0,
                                    (DateTime.UtcNow - new DateTime(waitStartTicks, DateTimeKind.Utc)).Ticks /
                                    TimeSpan.TicksPerMillisecond);
                                Logger.Checkpoint(
                                    "OTG_USB_DEVICE_WAIT",
                                    "WARNING",
                                    $"ukb={ukbLabel}; elapsedMs={elapsedMs}; warningAfterSeconds=10");
                                OnOtgCableWaitWarning?.Invoke();
                            }

                            if (DateTime.UtcNow >= retryAt && recoveryPowerCycles < 3)
                            {
                                recoveryPowerCycles++;
                                Report(
                                    progress,
                                    42,
                                    $"{ukbLabel}: OTG aygıtı algılanmadı; Recovery güç çevrimi yapılıyor " +
                                    $"({recoveryPowerCycles}/3)...");
                                Logger.Checkpoint(
                                    "OTG_USB_RECOVERY_POWER_CYCLE",
                                    "START",
                                    $"ukb={ukbLabel}; attempt={recoveryPowerCycles}/3");
                                await _moxa.SetPowerAsync(whichUkb, 0, ct);
                                await Task.Delay(1200, ct);
                                await _moxa.SetPowerAsync(whichUkb, 1, ct);
                                Logger.Checkpoint(
                                    "OTG_USB_RECOVERY_POWER_CYCLE",
                                    "SUCCESS",
                                    $"ukb={ukbLabel}; attempt={recoveryPowerCycles}/3");
                                retryAt = DateTime.UtcNow + TimeSpan.FromSeconds(25);
                            }
                            else if (DateTime.UtcNow >= retryAt && recoveryPowerCycles >= 3)
                            {
                                Logger.Checkpoint(
                                    "OTG_USB_DEVICE_WAIT",
                                    "FAILED",
                                    $"ukb={ukbLabel}; recoveryPowerCycles={recoveryPowerCycles}; reason=device-never-appeared");
                                throw new TimeoutException(
                                    $"{ukbLabel} OTG/USB recovery aygıtı üç otomatik güç çevriminden sonra algılanamadı. " +
                                    "OTG kablosunu, bilgisayar USB portunu ve UKB Recovery MOD/kanal ayarını kontrol edin.");
                            }
                        }

                        await Task.Delay(250, ct);
                    }
                }
                catch
                {
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        try { proc.WaitForExit(2000); } catch { }
                    }
                    throw;
                }

                proc.WaitForExit();
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException("Toradex UUU aracı hata kodu döndürdü: " + proc.ExitCode);
            }

            Report(progress, 65, $"{ukbLabel}: recovery yükleyicisi başarıyla aktarıldı.");
            // Bir sonraki aşama USB-NCM hedefini aktif olarak bekliyor. Burada uzun,
            // sabit bekleme yapmak yerine yalnızca kısa bir aygıt yerleşme payı bırak.
            int settleSeconds = Math.Max(0, Math.Min(_cfg.AfterBatWaitTimeSeconds, 2));
            Logger.Checkpoint(
                "EASY_INSTALLER_USB_SETTLE",
                "START",
                $"configuredSeconds={_cfg.AfterBatWaitTimeSeconds}; effectiveSeconds={settleSeconds}; activeNetworkWaitFollows=true");
            if (settleSeconds > 0)
                await Task.Delay(settleSeconds * 1000, ct);
            Logger.Checkpoint("EASY_INSTALLER_USB_SETTLE", "SUCCESS", $"effectiveSeconds={settleSeconds}");
        }

        private static void UpdateKnownUsbWaitState(
            string line,
            ref int waitingForKnownUsb,
            ref long knownUsbWaitStartedUtcTicks)
        {
            if (line.IndexOf(
                    "Wait for Known USB Device Appear",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Interlocked.CompareExchange(
                    ref knownUsbWaitStartedUtcTicks,
                    DateTime.UtcNow.Ticks,
                    0);
                Interlocked.Exchange(ref waitingForKnownUsb, 1);
                return;
            }

            if (Volatile.Read(ref waitingForKnownUsb) != 1)
                return;

            bool realTransferCommand =
                line.IndexOf(">Start Cmd:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.IndexOf(">Start Cmd:CFG:", StringComparison.OrdinalIgnoreCase) < 0;
            if (realTransferCommand ||
                line.IndexOf("New USB Device Attached", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Interlocked.Exchange(ref waitingForKnownUsb, 0);
                long waitStartedTicks = Interlocked.Exchange(ref knownUsbWaitStartedUtcTicks, 0);
                long elapsedMs = waitStartedTicks > 0
                    ? Math.Max(0, (DateTime.UtcNow.Ticks - waitStartedTicks) / TimeSpan.TicksPerMillisecond)
                    : 0;
                Logger.Checkpoint(
                    "OTG_USB_DEVICE_WAIT",
                    "SUCCESS",
                    $"elapsedMs={elapsedMs}; evidence={line}");
            }
        }

        public async Task LoadEasyInstallerAsync(
            IProgress<FlashProgress> progress,
            string ukbLabel,
            bool whichUkb,
            CancellationToken ct)
        {
            EnsureRecoveryTools();
            Logger.Checkpoint("EASY_INSTALLER_LOAD", "START", "ukb=" + ukbLabel);
            try
            {
                await RunRecoveryToolAsync(progress, ukbLabel, whichUkb, ct);
                Logger.Checkpoint("EASY_INSTALLER_LOAD", "SUCCESS", "ukb=" + ukbLabel);
            }
            catch (Exception ex)
            {
                Logger.Checkpoint(
                    "EASY_INSTALLER_LOAD",
                    "FAILED",
                    $"ukb={ukbLabel}; exception={ex.GetType().Name}; message={ex.Message}");
                throw;
            }
        }

        public void EnsureRecoveryTools()
        {
            string toolsDirectory = Path.GetDirectoryName(_cfg.RecoveryBatPath);
            if (string.IsNullOrWhiteSpace(toolsDirectory))
                throw new InvalidOperationException(
                    "Recovery arac dizini RecoveryBatPath uzerinden belirlenemedi.");

            EnsureRecoveryTools(Path.Combine(toolsDirectory, "recovery"));
        }

        private static string EnsureRecoveryTools(string recoveryDirectory)
        {
            try
            {
                string embeddedToolHash = GetEmbeddedResourceHash(RecoveryToolResourceName);
                if (!string.Equals(
                        embeddedToolHash,
                        ExpectedRecoveryToolSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Gomulu UUU kaynaginin SHA256 degeri yanlis. Beklenen: {ExpectedRecoveryToolSha256}, " +
                        $"okunan: {embeddedToolHash}.");
                }

                string embeddedScriptHash = GetEmbeddedResourceHash(RecoveryScriptResourceName);
                Directory.CreateDirectory(recoveryDirectory);

                bool repaired = false;
                repaired |= RestoreEmbeddedResourceIfDifferent(
                    RecoveryToolResourceName,
                    embeddedToolHash,
                    Path.Combine(recoveryDirectory, "uuu.exe"));
                repaired |= RestoreEmbeddedResourceIfDifferent(
                    RecoveryScriptResourceName,
                    embeddedScriptHash,
                    Path.Combine(recoveryDirectory, "uuu.auto"));

                string status = repaired ? "REPAIRED" : "OK";
                Logger.Checkpoint(
                    "RECOVERY_TOOL_SELF_HEAL",
                    status,
                    $"directory={recoveryDirectory}; uuuSha256={embeddedToolHash}; scriptSha256={embeddedScriptHash}");
                return status;
            }
            catch (Exception ex)
            {
                Logger.Checkpoint(
                    "RECOVERY_TOOL_SELF_HEAL",
                    "FAILED",
                    $"directory={recoveryDirectory}; exception={ex.GetType().Name}; message={ex.Message}");
                if (ex is InvalidOperationException)
                    throw;
                throw new InvalidOperationException(
                    "Recovery araclari gomulu kaynaklardan dogrulanamadi veya yenilenemedi: " + ex.Message,
                    ex);
            }
        }

        private static string GetEmbeddedResourceHash(string resourceName)
        {
            using Stream resource = typeof(FlashWorkflow).Assembly.GetManifestResourceStream(resourceName);
            if (resource == null)
                throw new InvalidOperationException("Gomulu recovery kaynagi bulunamadi: " + resourceName);

            return Convert.ToHexString(SHA256.HashData(resource));
        }

        private static bool RestoreEmbeddedResourceIfDifferent(
            string resourceName,
            string embeddedHash,
            string targetPath)
        {
            if (File.Exists(targetPath) &&
                string.Equals(GetFileHash(targetPath), embeddedHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException("Recovery hedef dizini belirlenemedi: " + targetPath);

            string tempPath = Path.Combine(
                targetDirectory,
                "." + Path.GetFileName(targetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using Stream resource = typeof(FlashWorkflow).Assembly.GetManifestResourceStream(resourceName);
                if (resource == null)
                    throw new InvalidOperationException("Gomulu recovery kaynagi bulunamadi: " + resourceName);

                using (var output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.WriteThrough))
                {
                    resource.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }

                string tempHash = GetFileHash(tempPath);
                if (!string.Equals(tempHash, embeddedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Gecici recovery dosyasinin SHA256 degeri yanlis: {Path.GetFileName(targetPath)}. " +
                        $"Beklenen: {embeddedHash}, okunan: {tempHash}.");

                File.Move(tempPath, targetPath, overwrite: true);
                return true;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string GetFileHash(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(input));
        }

        private static void Report(IProgress<FlashProgress> progress, int percent, string status)
        {
            Logger.Checkpoint("WORKFLOW_STAGE", "ENTER", $"percent={percent}; stage={status}");
            Logger.Info($"[{percent}%] {status}");
            progress?.Report(new FlashProgress { Percent = percent, Status = status });
        }
    }
}
