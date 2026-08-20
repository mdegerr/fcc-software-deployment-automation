using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MessageBox = SurumYakma.LocalizedMessageBox;

namespace SurumYakma
{
    public partial class Form1 : Form
    {
        private const string ApplicationDisplayName = "Sürüm Yükleme v-1.0.3";
        private const int NetworkStageCount = 12;
        private const int EasyInstallerEvidenceTimeoutSeconds = 45;
        internal const int SerialShellProbeAttempts = 3;
        internal const int SerialShellProbeRetryDelayMilliseconds = 1500;
        internal const int NetworkOnlyFeedDiscoveryTimeoutSeconds = 15;
        internal const int FinalFeedLateResponseGraceSeconds = 15;
        internal const int MaximumShutdownWaitSeconds = 20;
        private const int ConsoleFlushBatchSize = 800;
        private const int ConsolePendingEntryLimit = 5000;
        private const int ConsoleMaximumCharacters = 200000;
        private const int ConsoleTrimToCharacters = 150000;
        private static readonly Regex SerialTxDescriptorRowPattern = new Regex(
            @"^\[serial\]\s+\[\s*\d+(?:\.\d+)?\]\s+\d+\s+(?:(?:S|H)\s+){0,2}0x[0-9a-fA-F]{4}\s+0x[0-9a-fA-F]+\s+\d+\s+[0-9a-fA-F]+\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private const string DefaultPlatformSuffix = " (Varsayılan)";
        private const string EnglishDefaultPlatformSuffix = " (Default)";

        private AppConfig _cfg;
        private MoxaController _moxa;
        private VersionManager _versions;
        private FlashWorkflow _workflow;
        private UkbSerialMonitor _serial;
        private TeziHttpServer _teziHttpServer;
        private TeziMdnsAdvertiser _teziMdnsAdvertiser;
        private CancellationTokenSource _cts;
        private string _networkStagingPath;
        private long _networkSerialMark;
        private bool _networkFeedReady;
        private string _networkExpectedOfpVersion;
        private TaskCompletionSource<string> _networkFeedRequest;
        private TaskCompletionSource<string> _networkImageRequest;
        private TaskCompletionSource<string> _networkPayloadRequest;
        private TaskCompletionSource<string> _networkHealthRequest;
        private Task _activeWorkflowTask;

        private sealed class VersionChoice
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public bool IsFromPc { get; set; }

            public override string ToString()
            {
                return IsFromPc ? Name : Name + "  [FLASH'TA HAZIR]";
            }
        }

        private sealed class ConsoleSegment
        {
            public string Level { get; set; }
            public StringBuilder Text { get; } = new StringBuilder();
        }

        private static class NativeMethods
        {
            internal const int WmSetRedraw = 0x000B;

            [DllImport("user32.dll")]
            internal static extern IntPtr SendMessage(
                IntPtr hWnd,
                int msg,
                IntPtr wParam,
                IntPtr lParam);
        }

        private VersionChoice[] _versionChoices = new VersionChoice[0];

        // Tasarımcıya (resx/Designer) dokunmadan koddan eklenen kontroller:
        private Label lblStatus;
        private Label lblConsoleTitle;
        private Button btnCancel;
        private GroupBox grpHardwareTest;
        private Button btnTestPowerPulse;
        private Button btnTestRecoveryPulse;
        private Button btnRefreshDrives;
        private Label lblTestPowerState;
        private Label lblTestRecoveryState;
        private RichTextBox txtProcessConsole;
        private Button btnConnectionSettings;
        private Label lblConnectionSummary;
        private Label lblStageProgress;
        private Label lblTargetSelector;
        private ComboBox cmbTargetSelector;
        private readonly ConcurrentQueue<LogEntry> _pendingConsoleEntries = new ConcurrentQueue<LogEntry>();
        private System.Windows.Forms.Timer _consoleFlushTimer;
        private int _pendingConsoleCount;
        private Panel pnlModernHeader;
        private ModernCardPanel pnlLeftCard;
        private ModernCardPanel pnlRightCard;
        private Label lblModernTitle;
        private Label lblModernSubtitle;
        private Label lblModernMode;
        private Button btnWorkflowTab;
        private Button btnHelpTab;
        private Button btnLanguageTr;
        private Button btnLanguageEn;
        private Panel pnlHelp;
        private RichTextBox txtHelpGuide;
        private FlowLayoutPanel pnlHelpContent;
        private bool _modernUiApplied;
        private bool _compactInitialSizeApplied;
        private bool _helpViewActive;
        private string _helpContentLanguage = "";
        private TaskCompletionSource<bool> _ukbMediaReadySource;
        private bool _ukbMediaReadyForSecondUkb;
        private bool _criticalPhase;
        private bool _manualRecoveryRequired;
        private bool _hardwareTestBusy;
        private bool _moxaConnected;
        private bool _safeOutputsReady;
        private bool _shutdownStarted;
        private bool _shutdownCompleted;
        private bool _preservePowerAfterSuccessfulInstall;
        private string _projectName = "";
        private string _autoDetectionSummary = "";
        private int _currentNetworkStage;
        private string _currentNetworkStageName = "İşlem başlatılmadı";

        public Form1()
        {
            InitializeComponent();
            ApplyApplicationIcon();
            // Designer kontrollerinin ilk karede görünmesini engelle. Pencere,
            // modern düzen tamamen kurulduğunda ConfigureProductionUi içinde açılır.
            Opacity = 0;
            DoubleBuffered = true;
            FormClosing += Form1_FormClosing;
            FormClosed += (s, e) =>
            {
                _consoleFlushTimer?.Stop();
                _consoleFlushTimer?.Dispose();
                Logger.MessageWritten -= Logger_MessageWritten;
            };
            Resize += Form1_Resize;
            Shown += (s, e) => ShowInTaskbar = true;
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            ShowInTaskbar = true;
            if (WindowState != FormWindowState.Minimized)
                LayoutRuntimeControls();
        }

        private void ApplyShutdownVisualState()
        {
            UseWaitCursor = true;
            var pending = new Stack<Control>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                Control parent = pending.Pop();
                foreach (Control child in parent.Controls)
                {
                    if (child is Button button)
                        button.Enabled = false;
                    if (child.HasChildren)
                        pending.Push(child);
                }
            }

            lblStatus.Text = Localization.T(
                "Uygulama güvenli şekilde kapatılıyor...",
                "Application is closing safely...");
            if (lblStageProgress != null)
                lblStageProgress.Text = Localization.T(
                    "Bağlantılar güvenli şekilde kapatılıyor",
                    "Connections are closing safely");
            progressBar1.Style = ProgressBarStyle.Marquee;
            progressBar1.MarqueeAnimationSpeed = 24;
        }

        private void ApplyApplicationIcon()
        {
            using (Stream stream = typeof(Form1).Assembly.GetManifestResourceStream(
                "SurumYakma.Assets.surum-yukleme.ico"))
            {
                if (stream == null)
                    return;

                using (var embeddedIcon = new Icon(stream))
                    Icon = (Icon)embeddedIcon.Clone();
            }
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_shutdownCompleted)
                return;

            if (_shutdownStarted)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            _shutdownStarted = true;
            ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            ApplyShutdownVisualState();
            Activate();
            Logger.Checkpoint("APPLICATION_SHUTDOWN", "START", "Kullanıcı uygulamayı kapattı.");

            Task shutdown = ShutdownResourcesAsync();
            Task completed = await Task.WhenAny(
                shutdown,
                Task.Delay(TimeSpan.FromSeconds(MaximumShutdownWaitSeconds)));
            if (completed == shutdown)
            {
                try { await shutdown; }
                catch (Exception ex) { Logger.Diagnostic("Kapanış işlemi hata verdi.", ex); }
            }
            else
            {
                Logger.Warn("Güvenli kapanış üst süreyi aştı; pencere ve proses kapatılıyor.");
                Logger.Checkpoint(
                    "APPLICATION_SHUTDOWN_TOTAL",
                    "TIMEOUT",
                    $"maximumSeconds={MaximumShutdownWaitSeconds}");
            }

            _shutdownCompleted = true;
            Close();
        }

        private async Task ShutdownResourcesAsync()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

            Task activeWorkflow = _activeWorkflowTask;
            if (activeWorkflow != null && !activeWorkflow.IsCompleted)
            {
                Logger.Checkpoint(
                    "APPLICATION_SHUTDOWN_WORKFLOW",
                    "WAIT",
                    "Aktif işlem iptal edildi; güvenli MOXA geri alması bekleniyor.");
                Task workflowStopped = await Task.WhenAny(
                    activeWorkflow,
                    Task.Delay(TimeSpan.FromSeconds(6)));
                if (workflowStopped != activeWorkflow)
                    Logger.Warn(
                        "Aktif işlem 6 saniyede kapanmadı; kaynak kapatma aşamasına geçiliyor.");
                else
                {
                    try { await activeWorkflow; }
                    catch { }
                    Logger.Checkpoint("APPLICATION_SHUTDOWN_WORKFLOW", "STOPPED");
                }
            }

            if (_moxa != null && _moxaConnected && _cfg != null)
            {
                using var safeStopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                try
                {
                    bool[] targets = new[] { _cfg.SelectedUkb == 2 };
                    foreach (bool whichUkb in targets)
                    {
                        if (!_preservePowerAfterSuccessfulInstall)
                            await SetPowerAndVerifyAsync(whichUkb, 0, safeStopTimeout.Token,
                                $"APPLICATION_SHUTDOWN_POWER_UKB{_cfg.GetTargetNumber(whichUkb)}");
                        await SetRecoveryAndVerifyAsync(whichUkb, 0, safeStopTimeout.Token,
                            $"APPLICATION_SHUTDOWN_RECOVERY_UKB{_cfg.GetTargetNumber(whichUkb)}");
                    }
                    Logger.Checkpoint(
                        "APPLICATION_SHUTDOWN_SAFE_OUTPUTS",
                        "SUCCESS",
                        $"target=UKB{_cfg.SelectedUkb}; Power={(_preservePowerAfterSuccessfulInstall ? "ON_PRESERVED" : "OFF")}; Recovery=NORMAL");
                }
                catch (Exception ex)
                {
                    Logger.Error("Kapanışta seçili UKB güvenli çıkış durumuna alınamadı", ex);
                    Logger.Checkpoint("APPLICATION_SHUTDOWN_SAFE_OUTPUTS", "FAILED", ex.Message);
                }
            }

            await CleanupManagedTargetRouteAsync("APPLICATION_SHUTDOWN_ROUTE");

            TeziMdnsAdvertiser mdns = _teziMdnsAdvertiser;
            TeziHttpServer http = _teziHttpServer;
            UkbSerialMonitor serial = _serial;
            MoxaController moxa = _moxa;
            _teziMdnsAdvertiser = null;
            _teziHttpServer = null;
            _serial = null;
            _moxa = null;

            Task cleanup = Task.Run(() =>
            {
                try { mdns?.Dispose(); }
                catch (Exception ex) { Logger.Diagnostic("Kapanışta mDNS durdurulamadı.", ex); }
                try { http?.Dispose(); }
                catch (Exception ex) { Logger.Diagnostic("Kapanışta HTTP sunucusu durdurulamadı.", ex); }
                try { serial?.Dispose(); }
                catch (Exception ex) { Logger.Diagnostic("Kapanışta seri port kapatılamadı.", ex); }
                try { moxa?.Dispose(); }
                catch (Exception ex) { Logger.Diagnostic("Kapanışta MOXA bağlantıları kapatılamadı.", ex); }
                try { CleanupNetworkStaging(); }
                catch (Exception ex) { Logger.Diagnostic("Kapanışta staging temizlenemedi.", ex); }
            });

            Task completed = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(3)));
            if (completed == cleanup)
            {
                try
                {
                    await cleanup;
                    Logger.Checkpoint("APPLICATION_SHUTDOWN", "SUCCESS", "Kaynaklar kapatıldı.");
                }
                catch (Exception ex)
                {
                    Logger.Diagnostic("Kapanış temizliği hata verdi.", ex);
                    Logger.Checkpoint("APPLICATION_SHUTDOWN", "FAILED", ex.Message);
                }
            }
            else
            {
                Logger.Warn("Kapanış temizliği 3 saniyeyi aştı; prosesin beklememesi için uygulama sonlandırılıyor.");
                Logger.Checkpoint(
                    "APPLICATION_SHUTDOWN",
                    "TIMEOUT",
                    "cleanupTimeoutSeconds=3; pencere ve proses kapatılacak");
            }

            Logger.MessageWritten -= Logger_MessageWritten;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            AddRuntimeControls();
            InitializeConsoleFlushTimer();
            SetButtonsEnabled(false);

            try
            {
                _cfg = AppConfig.Load();
                _projectName = (Environment.GetEnvironmentVariable("UAV_PROJECT_NAME") ?? "").Trim();
                bool settingsProfileApplied = SettingsProfileStore.TryApplyCurrentProject(
                    _projectName,
                    _cfg,
                    out string profileLoadSummary);
                string hardwareDetectionSummary =
                    HardwareAutoConfigurator.ApplyStartupDetection(
                        _cfg,
                        _projectName,
                        preserveConfiguredProfile: settingsProfileApplied);
                _autoDetectionSummary = profileLoadSummary + " " + hardwareDetectionSummary;
                // Hedef secimi oturumluktur; uygulama her acilista guvenli varsayilan UKB1 ile baslar.
                ApplyStartupDefaults(_cfg);
                Localization.SetLanguage(_cfg.UiLanguage);
            }
            catch (AppConfigException ex)
            {
                MessageBox.Show(ex.Message, "Ayar Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
                return;
            }

            // İlk pencere çizilmeden doğru ürün arayüzünü hazırla. Donanım
            // bağlantılarını beklerken eski Designer düzeni görünmemelidir.
            if (!_cfg.HardwareTestMode)
                ConfigureProductionUi();

            Logger.MessageWritten += Logger_MessageWritten;
            Logger.Info("Uygulama başladı.");
            Logger.Info("Yüklenen yapılandırma: " + _cfg.LoadedConfigPath);
            Logger.Info("Bu oturumun ayrıntılı log dosyası: " + Logger.CurrentSessionLogPath);
            Logger.Info("Otomatik donanım algılama: " + _autoDetectionSummary);
            Logger.Checkpoint(
                "SESSION_START",
                "OK",
                $"version={Application.ProductVersion}; profile={_cfg.ProfileName}; " +
                $"selectedUkb=UKB{_cfg.SelectedUkb}; swarm={_cfg.SwarmModeEnabled}; power={_cfg.PowerBoxIp}/{_cfg.GetPowerSlot(_cfg.SelectedUkb == 2)}/" +
                $"{_cfg.GetPowerChannel(_cfg.SelectedUkb == 2)}; " +
                $"recovery={_cfg.RelayBoxIp}/{_cfg.GetRecoverySlot(_cfg.SelectedUkb == 2)}/" +
                $"{_cfg.GetRecoveryChannel(_cfg.SelectedUkb == 2)}; " +
                $"serial={_cfg.GetSerialPort(_cfg.SelectedUkb == 2)}/{_cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2)}; target={_cfg.UkbTargetIp}; " +
                $"moxaKeepAlive={_cfg.MoxaKeepAliveIntervalSeconds}s");

            _moxa = new MoxaController(_cfg);
            _moxa.ConnectionStateChanged += Moxa_ConnectionStateChanged;
            _versions = new VersionManager(_cfg);
            _serial = new UkbSerialMonitor(_cfg);
            _workflow = new FlashWorkflow(_cfg, _moxa, _versions, _serial);
            _workflow.WaitForUkbMediaReadyAsync = WaitForUkbMediaReadyAsync;
            _workflow.WaitForOtgCableConfirmationAsync = WaitForOtgCableConfirmationAsync;
            _workflow.WaitForOtgDisconnectConfirmationAsync = WaitForOtgDisconnectConfirmationAsync;
            _workflow.WaitForOtgReconnectConfirmationAsync = WaitForOtgReconnectConfirmationAsync;
            _workflow.OnWaitingForOtgConnect += () => BeginInvoke((Action)(() =>
                lblStatus.Text = "Flash belleği SIM PC'den güvenle çıkarın; UKB sürüm portuna ve OTG kablosunu UKB'ye takın..."));
            _workflow.OnWaitingForOtgDisconnect += () => BeginInvoke((Action)(() =>
                lblStatus.Text = "Lütfen OTG kablosunu ve flash belleği UKB'den çıkarın..."));
            _workflow.OnCriticalPhaseChanged += isCritical => BeginInvoke((Action)(() =>
            {
                _criticalPhase = isCritical;
                btnCancel.Enabled = _cts != null;
            }));
            _workflow.OnManualRecoveryRequired += () => BeginInvoke((Action)(() =>
            {
                _manualRecoveryRequired = true;
                _criticalPhase = false;
                SetButtonsEnabled(false);
                lblStatus.Text = "MANUEL KURTARMA GEREKLİ — UKB gücünü kesmeyin; seri port ile kontrol edin.";
            }));

            if (!_cfg.NetworkInstallMode)
                FillDriveList();

            RefreshProjectList();

            progressBar1.Value = 0;
            UpdateMainActionButtonTexts();
            lblStatus.Text = "Arayüz hazır; donanım bağlantıları arka planda kuruluyor...";
            Logger.Checkpoint("UI_READY", "SUCCESS", "hardwareConnection=background");
            SetButtonsEnabled(false);
            BeginInvoke((Action)(async () => await InitializeHardwareAsync()));
        }

        private static void ApplyStartupDefaults(AppConfig config)
        {
            if (config == null)
                return;
            config.SelectedUkb = 1;
            config.ValidateTeziPackageNamePrefix = false;
        }
        private async Task InitializeHardwareAsync()
        {
            Logger.Checkpoint("HARDWARE_BACKGROUND_INIT", "START");
            try
            {
                await Task.Run(() =>
                {
                    ProcessCleanup(_cfg.TeraName);
                    ProcessCleanup(_cfg.TesterName);
                });

                lblStatus.Text = Localization.T("Moxa cihazlarına bağlanılıyor...", "Connecting to Moxa devices...");
                _safeOutputsReady = false;
                bool connected = await _moxa.ConnectAsync();
                _safeOutputsReady = connected;
                _moxaConnected = connected;
                if (connected)
                    Logger.Checkpoint(
                        "STARTUP_OUTPUTS_UNCHANGED",
                        "SUCCESS",
                        $"selectedUkb=UKB{_cfg.SelectedUkb}; policy=connect-only; Power/Recovery channels were not read or written");
                UpdateConnectionLabels();
                ConfigureHardwareTestUi(connected);
                if (_cfg.HardwareTestMode && connected)
                    await RefreshHardwareTestStatesAsync();

                lblStatus.Text = connected
                    ? (_cfg.NetworkInstallMode
                        ? Localization.T("Yüklemeye hazır; sürümü seçip işlemi başlatın.", "Ready for installation; select a version and start the operation.")
                        : Localization.T("Hazır.", "Ready."))
                    : Localization.T(
                        "Moxa bağlantısı bekleniyor; Bağlantı Ayarlarından değerleri kontrol edin.",
                        "Moxa connection is pending; check the values in Connection Settings.");
                if (_cfg.HardwareTestMode)
                    lblStatus.Text = connected
                        ? "TEST DONANIMI HAZIR — UKB sürüm yükleme akışı devre dışı."
                        : "TEST DONANIMINA BAĞLANILAMADI — IP ve ağ bağlantısını kontrol edin.";

                SetButtonsEnabled(connected);
                Logger.Checkpoint("HARDWARE_BACKGROUND_INIT", connected ? "SUCCESS" : "FAILED");
            }
            catch (Exception ex)
            {
                _moxaConnected = false;
                _safeOutputsReady = false;
                UpdateConnectionLabels();
                SetButtonsEnabled(false);
                lblStatus.Text = Localization.T("Donanım bağlantısı kurulamadı; ayarları kontrol edin.", "Hardware connection could not be established; check the settings.");
                Logger.Error("Arka plan donanım başlatması tamamlanamadı", ex);
                Logger.Checkpoint("HARDWARE_BACKGROUND_INIT", "FAILED", ex.Message);
            }
        }

        private async Task ChangeSelectedTargetAsync()
        {
            if (_cfg == null || cmbTargetSelector == null || cmbTargetSelector.SelectedIndex < 0)
                return;
            int selected = cmbTargetSelector.SelectedIndex + 1;
            if (_cfg.SelectedUkb == selected)
                return;

            _cfg.SelectedUkb = selected;
            _serial?.Dispose();
            _serial = new UkbSerialMonitor(_cfg);
            UpdateMainActionButtonTexts();
            UpdateConnectionLabels();
            Logger.Info("Yükleme hedefi seçildi: UKB" + selected);

            Logger.Checkpoint(
                "TARGET_SELECTION_OUTPUTS_UNCHANGED",
                "SUCCESS",
                $"selectedUkb=UKB{selected}; policy=selection-only; Power/Recovery channels were not read or written");
            await Task.CompletedTask;
            SetButtonsEnabled(_moxaConnected);
        }

        private async Task SetPowerAndVerifyAsync(
            bool whichUkb,
            uint expected,
            CancellationToken ct,
            string checkpoint)
        {
            await _moxa.SetPowerAsync(whichUkb, expected, ct);
            uint observed = await _moxa.ReadPowerAsync(whichUkb, ct);
            if (observed != expected)
                throw new InvalidOperationException(
                    $"Power geri okuma doğrulaması başarısız. Beklenen={expected}, okunan={observed}.");
            Logger.Checkpoint(checkpoint, "SUCCESS", $"expected={expected}; observed={observed}");
        }

        private async Task SetRecoveryAndVerifyAsync(
            bool whichUkb,
            uint expected,
            CancellationToken ct,
            string checkpoint)
        {
            await _moxa.SetRecoveryAsync(whichUkb, expected, ct);
            uint observed = await _moxa.ReadRecoveryAsync(whichUkb, ct);
            if (observed != expected)
                throw new InvalidOperationException(
                    $"Recovery geri okuma doğrulaması başarısız. Beklenen={expected}, okunan={observed}.");
            Logger.Checkpoint(checkpoint, "SUCCESS", $"expected={expected}; observed={observed}");
        }

        private void Moxa_ConnectionStateChanged(bool powerConnected, bool relayConnected)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() =>
                        Moxa_ConnectionStateChanged(powerConnected, relayConnected)));
                }
                catch { }
                return;
            }

            _moxaConnected = powerConnected && relayConnected;
            UpdateConnectionLabels();
            if (_cts == null && !_criticalPhase && !_manualRecoveryRequired)
            {
                SetButtonsEnabled(_moxaConnected && _safeOutputsReady);
                lblStatus.Text = _moxaConnected
                    ? Localization.T("Moxa bağlantıları hazır.", "Moxa connections are ready.")
                    : Localization.T(
                        "Moxa bağlantısı bekleniyor; uygulama arka planda yeniden deneyecek.",
                        "Waiting for the Moxa connection; the application will retry in the background.");
            }
        }

        private void AddRuntimeControls()
        {
            // Eski ortam değişkenini gösteren ANKA_X etiketi artık arayüzde kullanılmıyor.
            Sel.Visible = false;
            RelayBoxIPLabel.Font = new Font("Microsoft Sans Serif", 10F);
            PowerBoxIPLabel.Font = new Font("Microsoft Sans Serif", 10F);
            RelayBoxIPLabel.AutoSize = false;
            RelayBoxIPLabel.TextAlign = ContentAlignment.MiddleLeft;
            PowerBoxIPLabel.AutoSize = false;
            PowerBoxIPLabel.TextAlign = ContentAlignment.MiddleLeft;

            lblStatus = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft Sans Serif", 10F),
                Text = "Başlatılıyor...",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle
            };
            btnCancel = new Button
            {
                Size = new Size(120, 34),
                Text = "İptal",
                Enabled = false
            };
            btnCancel.Click += (s, e) => CancelActiveOperation();

            btnConnectionSettings = new Button
            {
                Size = new Size(165, 34),
                Text = "Bağlantı Ayarları...",
                Enabled = false
            };
            btnConnectionSettings.Click += async (s, e) => await OpenConnectionSettingsAsync();
            lblConnectionSummary = new Label
            {
                AutoSize = false,
                Height = 34,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };
            lblStageProgress = new Label
            {
                AutoSize = false,
                Height = 24,
                Text = "İşlem başlatılmadı",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };
            lblTargetSelector = new Label
            {
                AutoSize = true,
                Text = "2. Yüklenecek UKB",
                Font = new Font("Microsoft Sans Serif", 14F, FontStyle.Bold)
            };
            cmbTargetSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(615, 28)
            };
            for (int number = 1; number <= 6; number++)
                cmbTargetSelector.Items.Add("UKB" + number);
            cmbTargetSelector.SelectedIndexChanged += async (s, e) => await ChangeSelectedTargetAsync();
            btnRefreshDrives = new Button
            {
                Size = new Size(105, 29),
                Text = "Yenile",
                Visible = false
            };
            btnRefreshDrives.Click += (s, e) =>
            {
                FillDriveList();
                RefreshVersionList();
            };
            surumList.SelectedIndexChanged += (s, e) =>
            {
                UpdateMainActionButtonTexts();
                SetButtonsEnabled(_moxaConnected);
            };

            grpHardwareTest = new GroupBox
            {
                Text = "TEST DONANIMI — fiziksel çıkış darbe kontrolü",
                Height = 100,
                Visible = false,
                BackColor = Color.MistyRose
            };
            btnTestPowerPulse = new Button
            {
                Location = new Point(12, 24),
                Size = new Size(165, 34),
                Text = "Power: 1 sn ON → OFF"
            };
            btnTestRecoveryPulse = new Button
            {
                Location = new Point(190, 24),
                Size = new Size(175, 34),
                Text = "Recovery: 1 sn ON → OFF"
            };
            lblTestPowerState = new Label
            {
                Location = new Point(12, 65),
                Size = new Size(165, 22),
                Text = "Kanal 0: okunmadı"
            };
            lblTestRecoveryState = new Label
            {
                Location = new Point(190, 65),
                Size = new Size(175, 22),
                Text = "Kanal 1: okunmadı"
            };
            btnTestPowerPulse.Click += async (s, e) => await RunHardwarePulseTestAsync(true);
            btnTestRecoveryPulse.Click += async (s, e) => await RunHardwarePulseTestAsync(false);
            grpHardwareTest.Controls.Add(btnTestPowerPulse);
            grpHardwareTest.Controls.Add(btnTestRecoveryPulse);
            grpHardwareTest.Controls.Add(lblTestPowerState);
            grpHardwareTest.Controls.Add(lblTestRecoveryState);

            lblConsoleTitle = new Label
            {
                AutoSize = false,
                Height = 25,
                Font = new Font("Microsoft Sans Serif", 11F, FontStyle.Bold),
                Text = "İşlem Konsolu",
                TextAlign = ContentAlignment.MiddleLeft
            };

            txtProcessConsole = new RichTextBox
            {
                ReadOnly = true,
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9F),
                WordWrap = false
            };

            Controls.Add(lblStatus);
            Controls.Add(btnCancel);
            Controls.Add(btnConnectionSettings);
            Controls.Add(lblConnectionSummary);
            Controls.Add(lblStageProgress);
            Controls.Add(lblTargetSelector);
            Controls.Add(cmbTargetSelector);
            Controls.Add(btnRefreshDrives);
            Controls.Add(grpHardwareTest);
            Controls.Add(lblConsoleTitle);
            Controls.Add(txtProcessConsole);
            LayoutRuntimeControls();
        }

        private void LayoutRuntimeControls()
        {
            if (lblStatus == null || txtProcessConsole == null)
                return;

            // Tasarımcı kontrolleri DPI ile ölçeklenir. Sağ kolonun başlangıcını
            // sabit piksel yerine ölçeklenmiş ilerleme çubuğundan almak,
            // terminalin soldaki "Dosyayı Seç" düğmesinin altına kaymasını önler.
            // Sol kartta butondan sonra kalan geniş boşluğu kaldır; sağ işlem kartını
            // gerçek sol içerik genişliğinin hemen sonundan başlat.
            int compactLeftBoundary = UKB1Yak.Right + 42;
            int rightLeft = Math.Min(progressBar1.Left, Math.Max(680, compactLeftBoundary));
            int rightMargin = 28;
            int rightWidth = Math.Max(360, ClientSize.Width - rightLeft - rightMargin);

            if (pnlModernHeader != null)
            {
                pnlModernHeader.Size = new Size(ClientSize.Width, 68);
                lblModernTitle.Location = new Point(24, 19);
                lblModernSubtitle.Location = new Point(26, 38);
                lblModernMode.Location = new Point(305, 20);
                btnWorkflowTab.Location = new Point(
                    Math.Max(305, lblModernTitle.Right + 28), 16);
                btnHelpTab.Location = new Point(btnWorkflowTab.Right + 6, 16);
                btnLanguageTr.Location = new Point(btnHelpTab.Right + 12, 19);
                btnLanguageEn.Location = new Point(btnLanguageTr.Right + 4, 19);
                btnConnectionSettings.Location = new Point(pnlModernHeader.Width - 205, 16);
                lblConnectionSummary.Location = new Point(
                    Math.Max(btnLanguageEn.Right + 12, btnConnectionSettings.Left - 210), 17);
                lblConnectionSummary.Size = new Size(
                    Math.Max(120, btnConnectionSettings.Left - lblConnectionSummary.Left - 14), 34);
                lblConnectionSummary.TextAlign = ContentAlignment.MiddleRight;

                pnlLeftCard.Location = new Point(14, 80);
                int leftContentBottom = Math.Max(UKB1Yak.Bottom, lblHello.Bottom);
                pnlLeftCard.Size = new Size(
                    Math.Max(660, rightLeft - 34),
                    Math.Max(300, leftContentBottom - pnlLeftCard.Top + 32));
                pnlRightCard.Location = new Point(rightLeft - 18, 80);
                pnlRightCard.Size = new Size(
                    Math.Max(390, rightWidth + 32),
                    Math.Max(300, ClientSize.Height - 96));

                // Kartlar yalnızca arka plan görevi görür. DPI/yeniden boyutlandırma
                // sonrasında WinForms z-order değişse bile girişleri örtmemelidir.
                pnlLeftCard.SendToBack();
                pnlRightCard.SendToBack();
                foreach (Control content in new Control[]
                {
                    label2, projectList, label1, textBox2, button2, label3, surumList,
                    lblTargetSelector, cmbTargetSelector,
                    lblHello, driveList, btnRefreshDrives, UKB1Yak, UKB2Yak,
                    label4, progressBar1, lblStageProgress, RelayBoxBaglantisiLabel, RelayBoxIPLabel,
                    PowerBoxBaglantisiLabel, PowerBoxIPLabel, lblStatus, btnCancel,
                    lblConsoleTitle, txtProcessConsole, grpHardwareTest
                })
                    content.BringToFront();
                if (_helpViewActive && pnlHelp != null)
                    pnlHelp.BringToFront();
                pnlModernHeader.BringToFront();

                // Designer'daki eski sabit konumlar kompakt pencere genişliğinde
                // sağ karta taşabiliyor. Başlık ve progress bar daima sağ kartın
                // içerik sınırlarına bağlanır.
                label4.Location = new Point(rightLeft, label4.Top);
                label4.Size = new Size(rightWidth, label4.Height);
                label4.TextAlign = ContentAlignment.MiddleCenter;
                progressBar1.Location = new Point(rightLeft, label4.Bottom + 12);
                progressBar1.Size = new Size(rightWidth, progressBar1.Height);
            }

            lblStageProgress.Location = new Point(rightLeft, progressBar1.Bottom + 5);
            lblStageProgress.Size = new Size(rightWidth, 24);
            int progressBottom = lblStageProgress.Bottom;

            RelayBoxBaglantisiLabel.Location = new Point(rightLeft, progressBottom + 8);
            RelayBoxIPLabel.Location = new Point(
                Math.Min(rightLeft + RelayBoxBaglantisiLabel.Width + 12, rightLeft + rightWidth - 130),
                RelayBoxBaglantisiLabel.Top);
            RelayBoxIPLabel.Size = new Size(
                Math.Max(120, rightLeft + rightWidth - RelayBoxIPLabel.Left),
                RelayBoxBaglantisiLabel.Height);

            PowerBoxBaglantisiLabel.Location = new Point(rightLeft, RelayBoxBaglantisiLabel.Bottom + 12);
            PowerBoxIPLabel.Location = new Point(
                Math.Min(rightLeft + PowerBoxBaglantisiLabel.Width + 12, rightLeft + rightWidth - 130),
                PowerBoxBaglantisiLabel.Top);
            PowerBoxIPLabel.Size = new Size(
                Math.Max(120, rightLeft + rightWidth - PowerBoxIPLabel.Left),
                PowerBoxBaglantisiLabel.Height);

            lblStatus.Location = new Point(rightLeft, PowerBoxBaglantisiLabel.Bottom + 16);
            lblStatus.Size = new Size(rightWidth, 48);

            btnCancel.Location = new Point(rightLeft, lblStatus.Bottom + 10);
            if (pnlModernHeader == null)
            {
                btnConnectionSettings.Location = new Point(btnCancel.Right + 10, btnCancel.Top);
                lblConnectionSummary.Location = new Point(btnConnectionSettings.Right + 10, btnCancel.Top);
                lblConnectionSummary.Size = new Size(
                    Math.Max(80, rightLeft + rightWidth - lblConnectionSummary.Left),
                    btnCancel.Height);
            }
            int consoleTop = btnCancel.Bottom + 18;
            lblConsoleTitle.Location = new Point(rightLeft, consoleTop);
            lblConsoleTitle.Width = rightWidth;
            txtProcessConsole.Location = new Point(rightLeft, lblConsoleTitle.Bottom + 4);
            txtProcessConsole.Size = new Size(
                rightWidth,
                Math.Max(120, ClientSize.Height - txtProcessConsole.Top - 24));

            grpHardwareTest.Location = new Point(projectList.Left, projectList.Bottom + 10);
            grpHardwareTest.Width = driveList.Width;
        }

        private void CancelActiveOperation()
        {
            CancellationTokenSource cancellation = _cts;
            if (cancellation == null || cancellation.IsCancellationRequested)
                return;

            btnCancel.Enabled = false;
            lblStatus.Text = "İptal isteği alındı; aktarım durduruluyor ve UKB güvenli duruma alınıyor...";
            Logger.Warn("Kullanıcı mevcut işlemi İptal butonuyla durdurdu.");
            Logger.Checkpoint("USER_CANCEL", "REQUESTED", $"critical={_criticalPhase}");

            // Aktif payload isteğini de kesmek için HTTP aktarımının iptal token'ını tetikler.
            // Asıl Power OFF / Recovery NORMAL geri alması ağ akışının finally bloğunda yapılır.
            TeziHttpServer server = _teziHttpServer;
            _teziHttpServer = null;
            Task cancellationTask;
            try
            {
                // Cancel(), kayıtlı geri çağrıları UI iş parçacığında çalıştırabilir.
                // CancelAsync() iptal durumunu hemen işaretleyip geri çağrıları UI dışında tamamlar.
                cancellationTask = cancellation.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                cancellationTask = Task.CompletedTask;
            }
            _ = CompleteCancellationInBackgroundAsync(cancellationTask, server, "USER_CANCEL");
        }


        private static async Task CompleteCancellationInBackgroundAsync(
            Task cancellationTask,
            IDisposable resource,
            string checkpoint)
        {
            try
            {
                await cancellationTask.ConfigureAwait(false);
                if (resource != null)
                    await Task.Run(resource.Dispose).ConfigureAwait(false);
                Logger.Checkpoint(checkpoint, "SIGNALLED");
            }
            catch (Exception ex)
            {
                Logger.Diagnostic("İptal sinyali/kaynak kapatma tamamlanamadı.", ex);
                Logger.Checkpoint(checkpoint, "SIGNAL_FAILED", ex.Message);
            }
        }


        private void SetNetworkStage(int stage, string stageName)
        {
            stage = Math.Max(1, Math.Min(NetworkStageCount, stage));
            stageName = string.IsNullOrWhiteSpace(stageName) ? "İşlem sürdürülüyor" : stageName.Trim();
            bool changed = stage != _currentNetworkStage ||
                !string.Equals(stageName, _currentNetworkStageName, StringComparison.Ordinal);

            _currentNetworkStage = stage;
            _currentNetworkStageName = stageName;
            progressBar1.Maximum = NetworkStageCount * 100;
            progressBar1.Value = Math.Max(
                progressBar1.Minimum,
                Math.Min(progressBar1.Maximum, (stage - 1) * 100));
            lblStageProgress.Text = $"{stageName}   •   Aşama {stage}/{NetworkStageCount}";

            if (changed)
                Logger.Checkpoint("WORKFLOW_STAGE", "START", $"stage={stage}/{NetworkStageCount}; name={stageName}");
        }

        private void CompleteNetworkProgress(string text = "Sürüm yükleme tamamlandı")
        {
            _currentNetworkStage = NetworkStageCount;
            _currentNetworkStageName = text;
            progressBar1.Value = progressBar1.Maximum;
            lblStageProgress.Text = $"{text}   •   {NetworkStageCount}/{NetworkStageCount}";
            Logger.Checkpoint("WORKFLOW_STAGE", "COMPLETE", $"stages={NetworkStageCount}");
        }

        private void SetNetworkProgressStopped(string result)
        {
            string prefix = string.IsNullOrWhiteSpace(result) ? "İşlem durdu" : result.Trim();
            int stage = Math.Max(1, _currentNetworkStage);
            lblStageProgress.Text = $"{prefix}: {_currentNetworkStageName}   •   Aşama {stage}/{NetworkStageCount}";
        }

        private void ConfigureProductionUi()
        {
            int modernOffset = _modernUiApplied ? 58 : 0;
            var stepHeadingFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            Text = ApplicationDisplayName;
            textBox1.Visible = false;
            textBox3.Visible = false;
            Sel.Visible = false;
            UKB2Yak.Visible = _cfg.TwoUkb;

            label2.Text = "Platform";
            label2.Font = new Font("Microsoft Sans Serif", 14F, FontStyle.Bold);
            label2.Location = new Point(27, 32 + modernOffset);
            projectList.Location = new Point(27, 64 + modernOffset);
            projectList.Size = new Size(615, 28);
            projectList.DropDownStyle = ComboBoxStyle.DropDownList;
            projectList.Enabled = true;
            projectList.TabStop = false;

            label1.Visible = false;
            textBox2.Visible = false;
            textBox2.TabStop = false;
            button2.Visible = false;
            button2.TabStop = false;

            label3.Text = "1. Yüklenecek Sürüm";
            label3.Font = stepHeadingFont;
            label3.Location = new Point(27, 112 + modernOffset);
            label3.Visible = true;
            surumList.Location = new Point(27, 144 + modernOffset);
            surumList.Size = new Size(615, 28);
            surumList.DropDownStyle = ComboBoxStyle.DropDownList;
            surumList.Visible = true;
            surumList.TabStop = true;

            lblHello.Text = "3. Flash Bellek (hedef)";
            lblHello.Font = stepHeadingFont;
            lblHello.Location = new Point(27, 272 + modernOffset);
            driveList.Location = new Point(27, 304 + modernOffset);
            driveList.Size = new Size(500, 28);
            driveList.DropDownStyle = ComboBoxStyle.DropDownList;
            btnRefreshDrives.Location = new Point(537, 301 + modernOffset);
            btnRefreshDrives.Visible = true;

            UKB1Yak.Location = new Point(27, 360 + modernOffset);
            UKB1Yak.Size = new Size(615, 58);
            UKB1Yak.Font = new Font("Microsoft Sans Serif", 14F, FontStyle.Bold);
            if (_cfg.TwoUkb)
            {
                UKB1Yak.Size = new Size(300, 58);
                UKB2Yak.Location = new Point(342, 360 + modernOffset);
                UKB2Yak.Size = new Size(300, 58);
                UKB2Yak.Font = UKB1Yak.Font;
                UKB2Yak.Text = "UKB2 — YÜKLEMEYİ BAŞLAT";
            }

            if (_cfg.NetworkInstallMode)
            {
                lblTargetSelector.Location = new Point(27, 198 + modernOffset);
                lblTargetSelector.Font = stepHeadingFont;
                cmbTargetSelector.Location = new Point(27, 230 + modernOffset);
                cmbTargetSelector.SelectedIndex = Math.Max(0, Math.Min(5, _cfg.SelectedUkb - 1));
                lblHello.Text = "3. Yükleme İşlemi";
                lblHello.Location = new Point(27, 278 + modernOffset);
                driveList.Visible = false;
                btnRefreshDrives.Visible = false;
                UKB1Yak.Location = new Point(27, 330 + modernOffset);
                UKB1Yak.Size = new Size(615, 64);
                UKB2Yak.Visible = false;
            }

            ApplyCompactInitialWindowSize();
            ApplyModernUi();
            LayoutRuntimeControls();
            SetButtonsEnabled(_moxaConnected);
            Opacity = 1;
        }

        private void ApplyCompactInitialWindowSize()
        {
            if (_compactInitialSizeApplied || WindowState != FormWindowState.Normal)
                return;

            _compactInitialSizeApplied = true;
            const int rightMargin = 28;
            int originalRightWidth = Math.Max(490, ClientSize.Width - progressBar1.Left - rightMargin);
            int compactRightLeft = Math.Min(
                progressBar1.Left,
                Math.Max(680, UKB1Yak.Right + 42));
            int desiredClientWidth = compactRightLeft + originalRightWidth + rightMargin;
            if (desiredClientWidth < ClientSize.Width)
                ClientSize = new Size(desiredClientWidth, ClientSize.Height);
        }

        private void ApplyModernUi()
        {
            if (_modernUiApplied)
                return;
            _modernUiApplied = true;

            SuspendLayout();
            BackColor = ModernUi.AppBackground;
            Font = new Font("Segoe UI", 10F);
            MinimumSize = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            SizeGripStyle = SizeGripStyle.Hide;
            MaximumSize = Size;
            MinimumSize = Size;

            pnlModernHeader = new Panel { BackColor = ModernUi.HeaderBackground };
            lblModernTitle = new Label
            {
                AutoSize = true,
                Text = ApplicationDisplayName,
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            lblModernSubtitle = new Label
            {
                AutoSize = true,
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent
            };
            lblModernMode = new Label
            {
                AutoSize = false,
                Size = new Size(150, 28),
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(187, 247, 208),
                BackColor = Color.FromArgb(22, 101, 52)
            };
            lblModernSubtitle.Visible = false;
            lblModernMode.Visible = false;
            ModernUi.ApplyRoundedRegion(lblModernMode, 12);
            btnWorkflowTab = new Button
            {
                Text = "Yükleme",
                Size = new Size(92, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnHelpTab = new Button
            {
                Text = "Yardım",
                Size = new Size(82, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnWorkflowTab.Click += (s, e) => ShowHelpView(false);
            btnHelpTab.Click += (s, e) => ShowHelpView(true);
            btnLanguageTr = new Button
            {
                Text = "TR",
                Size = new Size(42, 30),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnLanguageEn = new Button
            {
                Text = "EN",
                Size = new Size(42, 30),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnLanguageTr.Click += (s, e) => ChangeLanguage("TR");
            btnLanguageEn.Click += (s, e) => ChangeLanguage("EN");

            Controls.Remove(btnConnectionSettings);
            Controls.Remove(lblConnectionSummary);
            pnlModernHeader.Controls.Add(lblModernTitle);
            pnlModernHeader.Controls.Add(lblModernSubtitle);
            pnlModernHeader.Controls.Add(lblModernMode);
            pnlModernHeader.Controls.Add(btnWorkflowTab);
            pnlModernHeader.Controls.Add(btnHelpTab);
            pnlModernHeader.Controls.Add(btnLanguageTr);
            pnlModernHeader.Controls.Add(btnLanguageEn);
            pnlModernHeader.Controls.Add(lblConnectionSummary);
            pnlModernHeader.Controls.Add(btnConnectionSettings);
            Controls.Add(pnlModernHeader);
            pnlHelp = BuildHelpPanel();
            Controls.Add(pnlHelp);

            pnlLeftCard = new ModernCardPanel();
            pnlRightCard = new ModernCardPanel();
            Controls.Add(pnlLeftCard);
            Controls.Add(pnlRightCard);
            // Kartlar yalnızca yerleşim sınırıdır. Form üzerine ayrıca beyaz kart
            // çizmek bazı ekran kartı/DPI birleşimlerinde yeniden boyama izleri
            // oluşturduğu için arka plan tek renkte bırakılır.
            pnlLeftCard.Visible = false;
            pnlRightCard.Visible = false;
            pnlModernHeader.BringToFront();

            foreach (Control control in new Control[]
            {
                label2, projectList, label1, textBox2, button2, label3, surumList,
                lblTargetSelector, cmbTargetSelector,
                lblHello, driveList, btnRefreshDrives, UKB1Yak, UKB2Yak, label4, progressBar1,
                lblStageProgress
            })
                control.Top += 58;

            foreach (Control control in new Control[]
            {
                lblStatus, lblStageProgress, UKB1Yak, UKB2Yak
            })
                control.TextChanged += LocalizeDynamicControlText;
            TextChanged += LocalizeDynamicControlText;
            projectList.FormattingEnabled = true;
            projectList.Format += FormatProjectListItem;

            foreach (Label label in new[] { label2, label1, label3, lblHello })
                ModernUi.StyleSectionLabel(label);
            foreach (Control input in new Control[] { projectList, textBox2, surumList, driveList })
                ModernUi.StyleInput(input);

            ModernUi.StylePrimaryButton(UKB1Yak);
            ModernUi.StylePrimaryButton(UKB2Yak);
            ModernUi.StyleSecondaryButton(button2);
            ModernUi.StyleSecondaryButton(btnRefreshDrives);
            ModernUi.StyleDangerButton(btnCancel);
            ModernUi.StyleHeaderButton(btnConnectionSettings);
            ModernUi.StyleHeaderButton(btnWorkflowTab);
            ModernUi.StyleHeaderButton(btnHelpTab);
            ModernUi.StyleHeaderButton(btnLanguageTr);
            ModernUi.StyleHeaderButton(btnLanguageEn);
            ApplyLanguage();
            UpdateHeaderTabs();
            ModernUi.StyleConnectionBadge(RelayBoxBaglantisiLabel);
            ModernUi.StyleConnectionBadge(PowerBoxBaglantisiLabel);

            RelayBoxIPLabel.Font = new Font("Segoe UI Semibold", 9.5F);
            RelayBoxIPLabel.ForeColor = ModernUi.TextSecondary;
            RelayBoxIPLabel.BackColor = ModernUi.AppBackground;
            PowerBoxIPLabel.Font = RelayBoxIPLabel.Font;
            PowerBoxIPLabel.ForeColor = ModernUi.TextSecondary;
            PowerBoxIPLabel.BackColor = ModernUi.AppBackground;
            lblConnectionSummary.ForeColor = Color.FromArgb(203, 213, 225);
            lblConnectionSummary.BackColor = Color.Transparent;
            lblConnectionSummary.Font = new Font("Segoe UI", 9F);
            label4.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            label4.ForeColor = ModernUi.TextPrimary;
            label4.BackColor = ModernUi.AppBackground;
            lblStatus.BackColor = Color.FromArgb(248, 250, 252);
            lblStatus.ForeColor = ModernUi.TextPrimary;
            lblStatus.BorderStyle = BorderStyle.None;
            lblStatus.Font = new Font("Segoe UI", 9.5F);
            ModernUi.ApplyRoundedRegion(lblStatus, 8);
            lblConsoleTitle.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            lblConsoleTitle.ForeColor = ModernUi.TextPrimary;
            lblConsoleTitle.BackColor = ModernUi.AppBackground;
            txtProcessConsole.BackColor = Color.FromArgb(11, 18, 32);
            txtProcessConsole.ForeColor = Color.FromArgb(226, 232, 240);
            txtProcessConsole.BorderStyle = BorderStyle.None;
            txtProcessConsole.Font = new Font("Cascadia Mono", 9F);
            ModernUi.ApplyRoundedRegion(txtProcessConsole, 10);
            progressBar1.Height = 12;
            progressBar1.Minimum = 0;
            progressBar1.Maximum = NetworkStageCount * 100;
            progressBar1.Style = ProgressBarStyle.Continuous;
            lblStageProgress.Font = new Font("Segoe UI Semibold", 9.5F);
            lblStageProgress.ForeColor = ModernUi.TextSecondary;
            lblStageProgress.BackColor = ModernUi.AppBackground;
            ResumeLayout(true);
        }


        private Panel BuildHelpPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ModernUi.AppBackground,
                Visible = false,
                Padding = new Padding(28, 90, 28, 22)
            };

            txtHelpGuide = new RichTextBox
            {
                Visible = false,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = ModernUi.TextPrimary,
                Font = new Font("Segoe UI", 10.5F),
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                TabStop = false
            };
            pnlHelpContent = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.White,
                Padding = new Padding(18, 14, 18, 24)
            };
            panel.Controls.Add(pnlHelpContent);
            panel.Controls.Add(txtHelpGuide);
            PopulateHelpGuide();
            return panel;
        }

        private void ShowHelpView(bool showHelp)
        {
            if (showHelp && _criticalPhase)
            {
                MessageBox.Show(
                    this,
                    "Kritik yükleme aşamasında işlem ekranından ayrılamazsınız.",
                    "Yükleme Devam Ediyor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _helpViewActive = showHelp;
            pnlHelp.Visible = showHelp;
            if (showHelp)
            {
                if (!string.Equals(_helpContentLanguage, Localization.LanguageCode, StringComparison.Ordinal))
                    PopulateHelpGuide();
                pnlHelp.BringToFront();
            }
            pnlModernHeader.BringToFront();
            UpdateHeaderTabs();
        }

        private void UpdateHeaderTabs()
        {
            if (btnWorkflowTab == null || btnHelpTab == null)
                return;

            Color selected = Color.FromArgb(37, 99, 235);
            Color normal = ModernUi.HeaderBackground;
            btnWorkflowTab.BackColor = _helpViewActive ? normal : selected;
            btnHelpTab.BackColor = _helpViewActive ? selected : normal;
            btnWorkflowTab.ForeColor = Color.White;
            btnHelpTab.ForeColor = Color.White;
            btnWorkflowTab.FlatAppearance.BorderColor =
                _helpViewActive ? Color.FromArgb(71, 85, 105) : selected;
            btnHelpTab.FlatAppearance.BorderColor =
                _helpViewActive ? selected : Color.FromArgb(71, 85, 105);
            if (btnLanguageTr != null && btnLanguageEn != null)
            {
                btnLanguageTr.BackColor = Localization.IsEnglish ? normal : selected;
                btnLanguageEn.BackColor = Localization.IsEnglish ? selected : normal;
                btnLanguageTr.ForeColor = Color.White;
                btnLanguageEn.ForeColor = Color.White;
            }
        }

        private void ChangeLanguage(string language)
        {
            if (_cfg == null || string.Equals(_cfg.UiLanguage, language, StringComparison.OrdinalIgnoreCase))
                return;
            _cfg.UiLanguage = language;
            Localization.SetLanguage(language);
            try
            {
                SettingsProfileStore.SaveCurrentProject(_projectName, _cfg);
            }
            catch (Exception ex)
            {
                Logger.Diagnostic("Dil tercihi Settings.json dosyasına kaydedilemedi.", ex);
            }
            ApplyLanguage();
            RefreshProjectListLanguageSuffixes();
            Logger.Info(Localization.T(
                "Uygulama dili Türkçe olarak değiştirildi.",
                "Application language changed to English."));
        }

        private void ApplyLanguage()
        {
            SetControlRedraw(this, false);
            SuspendLayout();
            try
            {
                Localization.Apply(this);
                RelocalizeProcessConsole();
                if (btnWorkflowTab != null)
                    btnWorkflowTab.Text = Localization.T("Yükleme", "Installation");
                if (btnHelpTab != null)
                    btnHelpTab.Text = Localization.T("Yardım", "Help");
                if (btnConnectionSettings != null)
                    btnConnectionSettings.Text = Localization.T("Bağlantı Ayarları...", "Connection Settings...");
                if (lblConsoleTitle != null)
                    lblConsoleTitle.Text = Localization.T("İşlem Konsolu", "Process Console");
                if (label4 != null)
                    label4.Text = Localization.T("İlerleme", "Progress");
                if (btnCancel != null)
                    btnCancel.Text = Localization.T("İptal", "Cancel");
                if (_helpViewActive && pnlHelp != null)
                    PopulateHelpGuide();
                UpdateHeaderTabs();
                LayoutRuntimeControls();
            }
            finally
            {
                ResumeLayout(true);
                SetControlRedraw(this, true);
                Invalidate(true);
                Update();
            }
        }

        private void RelocalizeProcessConsole()
        {
            if (txtProcessConsole == null)
                return;

            FlushPendingConsoleEntries();
            if (txtProcessConsole.TextLength == 0)
                return;

            string[] lines = txtProcessConsole.Lines;
            var segments = new List<ConsoleSegment>();
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;
                string level = line.Contains("[ERROR]", StringComparison.Ordinal) ? "ERROR" :
                    line.Contains("[WARN]", StringComparison.Ordinal) ? "WARN" : "INFO";
                AddConsoleSegment(
                    segments,
                    level,
                    LocalizeConsoleLine(line) + Environment.NewLine);
            }

            SetControlRedraw(txtProcessConsole, false);
            try
            {
                txtProcessConsole.Clear();
                AppendConsoleSegments(segments, scrollToEnd: true);
            }
            finally
            {
                SetControlRedraw(txtProcessConsole, true);
                txtProcessConsole.Invalidate();
            }
        }

        private static string LocalizeConsoleLine(string line)
        {
            if (line.IndexOf("] [serial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("] [uuu]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("] [http]", StringComparison.OrdinalIgnoreCase) >= 0)
                return line;
            return Localization.ForCurrentLanguage(line);
        }
        private void LocalizeDynamicControlText(object sender, EventArgs e)
        {
            if (!Localization.IsEnglish || sender is not Control control)
                return;
            string translated = Localization.TranslateToEnglish(control.Text);
            if (!string.Equals(control.Text, translated, StringComparison.Ordinal))
                control.Text = translated;
        }

        private void FormatProjectListItem(object sender, ListControlConvertEventArgs e)
        {
            string value = Convert.ToString(e.ListItem) ?? "";
            bool isDefault =
                value.EndsWith(DefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(EnglishDefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase);
            if (!isDefault)
                return;
            e.Value = GetProjectNameFromDisplay(value) +
                Localization.T(DefaultPlatformSuffix, EnglishDefaultPlatformSuffix);
        }

        private void RefreshProjectListLanguageSuffixes()
        {
            if (projectList == null)
                return;

            int selectedIndex = projectList.SelectedIndex;
            for (int index = 0; index < projectList.Items.Count; index++)
            {
                string value = Convert.ToString(projectList.Items[index]) ?? "";
                bool isDefault =
                    value.EndsWith(DefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase) ||
                    value.EndsWith(EnglishDefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase);
                if (!isDefault)
                    continue;
                projectList.Items[index] = GetProjectNameFromDisplay(value) +
                    Localization.T(DefaultPlatformSuffix, EnglishDefaultPlatformSuffix);
            }
            if (selectedIndex >= 0 && selectedIndex < projectList.Items.Count)
                projectList.SelectedIndex = selectedIndex;
            projectList.Refresh();
        }

        private void PopulateHelpGuide()
        {
            _helpContentLanguage = Localization.LanguageCode;
            txtHelpGuide.Clear();
            if (Localization.IsEnglish)
            {
                AppendHelpTitle(ApplicationDisplayName + " — User Guide");
                AppendHelpParagraph("This screen safely automates UKB version installation over USB-NCM.");
                AppendHelpSection("1. Checks before use");
                AppendHelpBullet("Verify UAV_PROJECT_NAME, MOXA IP/MOD/channel values and the selected UKB COM port.");
                AppendHelpBullet("Connect the OTG cable directly; do not use a USB hub or charge-only cable.");
                AppendHelpBullet("Allow the application through Windows Defender Firewall and ensure no serial terminal owns the COM port.");
                AppendHelpSection("2. Connection settings");
                AppendHelpBullet("The UKB tab contains COM, Power and Recovery mappings for UKB1–UKB6.");
                AppendHelpBullet("The Advanced Options tab contains the editable UKB Versions root folder and environment-to-package mappings.");
                AppendHelpSection("3. Installation");
                AppendHelpBullet("Select the platform, version and target UKB, then start installation.");
                AppendHelpBullet("If the OTG warning appears, connect the cable and press OK; the next recovery attempt waits for this confirmation.");
                AppendHelpBullet("After a successful installation, Recovery remains NORMAL and UKS Power remains ON.");
                AppendHelpBullet("LINK IS UP is shown in green when observed. If not observed, it is shown in red as a warning; the verified version remains successful.");
                PopulateVisualHelpContent();
                return;
            }
            AppendHelpTitle(ApplicationDisplayName + " — Kullanım Kılavuzu");
            AppendHelpParagraph(
                "Bu ekran, uygulamayı ilk kez kullanan personelin ağdan sürüm yükleme işlemini " +
                "güvenli ve doğru sırayla tamamlaması için hazırlanmıştır.");

            AppendHelpSection("1. İlk kullanımdan önce neleri kontrol etmeliyim?");
            AppendHelpBullet("Bilgisayarın UAV_PROJECT_NAME ortam değişkeni doğru projeyi göstermelidir.");
            AppendHelpCode("CMD kontrolü:  echo %UAV_PROJECT_NAME%");
            AppendHelpBullet("Power Box ve Relay Box Ethernet bağlantılarını kontrol edin; Bağlantı Ayarlarındaki IP, MOD ve kanal değerleri hedef sisteme ait olmalıdır.");
            AppendHelpBullet("OTG kablosunu doğrudan PC ile seçilen UKB arasına bağlayın. USB hub veya yalnızca şarj destekleyen kablo kullanmayın.");
            AppendHelpBullet("Windows Aygıt Yöneticisinde UKB seri bağlantısının COM portunu ve USB-NCM ağ bağdaştırıcısını doğrulayın.");
            AppendHelpBullet("Tera Term veya başka bir seri terminal COM portunu kullanıyor olmamalıdır.");
            AppendHelpBullet("Windows Defender Güvenlik Duvarında uygulamaya Özel ve Genel ağ izni verildiğini kontrol edin. İlk çalıştırma izin penceresi çıkarsa erişime izin verin.");
            AppendHelpBullet("Kurumsal antivirüs/Defender uygulamayı, yerel HTTP sunucusunu veya tools\\tezi\\recovery\\uuu.exe aracını engelliyorsa yetkili BT biriminden izin talep edin.");
            AppendHelpBullet("Uygulamanın yanında Settings.json, appsettings.json, MXIO_NET.dll ve tools klasörü bulunmalıdır.");
            AppendHelpBullet("Yalnızca yetkili ve tam TEZI sürüm paketlerini kullanın.");

            AppendHelpSection("2. Bağlantı Ayarları bölümündeki alanlar ne işe yarar?");
            AppendHelpBullet("UKB tablosunda UKB1-UKB6 için yalnızca Windows'ta görünen COM portları seçilebilir. COM hücresini seçip Delete tuşuna basarak seçim temizlenebilir.");
            AppendHelpBullet("Baud rate tüm UKB'ler için ortaktır. Normal değer 115200'dür; yalnızca yetkili sistem bilgisine göre değiştirin.");
            AppendHelpBullet("Power Box IP / slot / kanallar: UKB gücünü açıp kapatan Moxa çıkışlarını belirtir.");
            AppendHelpBullet("Relay Box IP / Recovery slot ve kanalı: UKB'yi REAL Recovery moduna alan Moxa çıkışını belirtir.");
            AppendHelpBullet("UKB Hedefleri tablosunda UKB1-UKB6 sırası sabittir; her satırda COM, Power MOD/kanalları ve Recovery MOD/kanalı birlikte tutulur. Ortak baud rate tablonun üstünden bir kez seçilir.");
            AppendHelpBullet("Power Box ve Relay Box IP adresleri tablonun üstünde ortaktır. Her çalıştırmada yalnızca seçilen UKB'ye sürüm yüklenir.");
            AppendHelpBullet("Ana ekrandaki Yüklenecek UKB listesinden hedef seçilebilir. Seçim değiştiğinde uygulama o hedef için Power OFF ve Recovery NORMAL güvenlik kontrolü uygular.");
            AppendHelpBullet("UKB hedef IP: Easy Installer açıldığında UKB'nin USB-NCM adresidir.");
            AppendHelpBullet("PC / HTTP sunucu IP: Sürüm paketinin UKB'ye sunulduğu bilgisayar adresidir. Otomatik bulma açık bırakılmalıdır.");
            AppendHelpBullet("HTTP portu: Önce ayarlı port denenir; kullanılamazsa uygulama 8088 ve ardından boş bir port seçer.");
            AppendHelpBullet("UKB Sürümleri ana klasörü: Varsayılan olarak Windows Masaüstü\\UKB Sürümleri yoludur. Gerekirse buradan değiştirilebilir.");
            AppendHelpBullet("Kaydet ve Bağlan: Ayarları makine profiline ve Settings.json içindeki aktif ortam profiline kaydeder, sonra Moxa'ya yeniden bağlanır.");

            AppendHelpSection("3. Gelişmiş Seçenekler nasıl kullanılmalıdır?");
            AppendHelpBullet("Ortam adı, UAV_PROJECT_NAME değeridir. Sonunda * jokeri kullanılabilir.");
            AppendHelpBullet("Seçilen platform klasörünün doğrudan altındaki sürüm klasörleri listelenir; klasör adları değiştirilmeden gösterilir.");
            AppendHelpCode("Örnek:  YFYK*  →  WCC     |     KSIMSEK  →  KS");
            AppendHelpBullet("Tam ortam eşlemesi, jokerli genel eşlemeden önceliklidir.");

            AppendHelpSection("4. Yüklenecek sürümü nasıl seçerim?");
            AppendHelpBullet("Platform listesinde UAV_PROJECT_NAME varsayılan seçilir; Bağlantı Ayarlarında kayıtlı diğer platformlar da listelenir.");
            AppendHelpBullet("Listeden başka bir platform seçildiğinde Windows ortam değişkeni değiştirilmez; yalnızca UKB Sürümleri altındaki sürüm kaynağı ve paket eşlemesi seçilen platforma göre yenilenir.");
            AppendHelpBullet("Platform seçimi Moxa IP, COM, MOD veya kanal ayarlarını kendiliğinden değiştirmez.");
            AppendHelpBullet("Platform klasörü eşlemesinde büyük/küçük harf, Türkçe karakter ve boşluk/tire/alt çizgi farklılıklarına tolerans gösterilir.");
            AppendHelpBullet("Uygulama yalnızca aktif ortamla eşleşen tam TEZI paketlerini arka planda değerlendirir.");
            AppendHelpBullet("Geçerli paket; image.json, prepare.sh, wrapup.sh dosyalarını içermeli ve klasör adı build.0 ile bitmelidir.");
            AppendHelpBullet("Bulunan sürümler en yeniden eskiye sıralanır ve en güncel sürüm otomatik seçilir; istenirse listeden eski sürüm seçilebilir.");
            AppendHelpBullet("Uygun paket bulunamazsa ana yükleme butonu etkinleşmez.");

            AppendHelpSection("5. Sürüm yüklemeyi nasıl başlatırım?");
            AppendHelpBullet("Relay Box ve Power Box bağlantı göstergelerinin yeşil olduğunu doğrulayın.");
            AppendHelpBullet("Doğru sürüm seçiliyken SÜRÜM YÜKLEMEYİ BAŞLAT düğmesine basın.");
            AppendHelpBullet("Ana ekrandaki Yüklenecek UKB listesinden UKB1-UKB6 arasında yalnızca bir hedef seçilir; işlem bu hedef üzerinde yürütülür.");
            AppendHelpBullet("Uygulama Recovery modunu REAL yapar, UKB gücünü açar ve Easy Installer'ı USB üzerinden yükler.");
            AppendHelpBullet("USB-NCM ağı kurulduktan sonra sürüm paketi yerel HTTP sunucusundan TEZI'ye aktarılır.");
            AppendHelpBullet("Kurulum tamamlanınca Recovery NORMAL yapılır, normal açılıştaki OFP sürümü doğrulanır ve UKS POWER açık bırakılır.");
            AppendHelpBullet("Sonuç penceresinde LINK IS UP görülürse yeşil, görülmezse kırmızı gösterilir. Kırmızı LINK uyarısı doğrulanmış sürüm yüklemesini başarısız saymaz.");
            AppendHelpBullet("OTG uyarısı açılırsa kabloyu bağlayıp Tamam'a basın; kullanıcı onayı verilene kadar yeni Recovery denemesi başlamaz.");
            AppendHelpBullet("İptal düğmesi veya pencereyi kapatma, aktarımı durdurur; ardından seçili UKB Power OFF ve Recovery NORMAL yapılır.");
            AppendHelpBullet("Aktarım başladıktan sonra iptal edilen sürüm eksik kalabilir ve sonraki denemede yeniden yüklenmelidir.");

            AppendHelpSection("6. Ekrandaki göstergeler ne anlama gelir?");
            AppendHelpBullet("Yeşil bağlantı etiketi: İlgili Moxa cihazına bağlantı kuruldu.");
            AppendHelpBullet("Kırmızı bağlantı etiketi: IP, ağ, Moxa uygulaması veya fiziksel bağlantı kontrol edilmelidir.");
            AppendHelpBullet("İlerleme çubuğu: Seçili tek UKB hedefinin 12 aşamalı genel durumunu gösterir.");
            AppendHelpBullet("Durum alanı: Kullanıcının o anda yapması veya beklemesi gereken işlemi bildirir.");
            AppendHelpBullet("İşlem Konsolu: Seçili UKB'nin önemli canlı mesajlarını gösterir; ayrıntılı teşhis log dosyasındadır.");

            AppendHelpSection("7. Settings.json ve log kayıtları nerede tutulur?");
            AppendHelpBullet("Manuel bağlantı değişiklikleri, Kaydet ve Bağlan ile aktif ortamın SURUM_YAKMA_PROFILES kaydına yazılır.");
            AppendHelpBullet("Her değişiklikten önce Settings.json.bak yedeği oluşturulur.");
            AppendHelpBullet("Ayrıntılı oturum logları uygulamanın yanındaki logs klasöründe tarih-saatli olarak tutulur.");
            AppendHelpBullet("Hata bildirirken en son tarihli oturum logunu paylaşın; loglarda başarılı ve başarısız checkpoint'ler bulunur.");

            AppendHelpSection("8. Sık karşılaşılan sorunlarda neyi kontrol etmeliyim?");
            AppendHelpBullet("COM açılamıyor: Tera Term'i kapatın, kabloyu kontrol edin ve COM listesini yenileyin.");
            AppendHelpBullet("OTG algılanmıyor: Kabloyu çıkarıp doğrudan yeniden takın, farklı USB portu deneyin ve Aygıt Yöneticisinde USB aygıtının oluştuğunu kontrol edin.");
            AppendHelpBullet("USB-NCM adresi oluşmuyor: Windows ağ bağdaştırıcılarında USB-NCM sürücüsünü ve 192.168.11.x ağını kontrol edin.");
            AppendHelpBullet("Moxa bağlantısı kırmızı: IP adresini, PC ağ kartını, ping erişimini, slot ve kanal değerlerini kontrol edin.");
            AppendHelpBullet("Sürüm listesi boş: UAV_PROJECT_NAME, ortam–paket eşlemesi, kaynak klasörü ve tam TEZI paket yapısını kontrol edin.");
            AppendHelpBullet("HTTP sunucusu açılamıyor: Windows Defender Güvenlik Duvarında uygulamanın Özel/Genel izinlerini kontrol edin; uygulama alternatif portları otomatik dener.");
            AppendHelpBullet("Easy Installer bekleniyor: Recovery/Power kanallarını, USB kablosunu ve UUU çıktısını logdan kontrol edin.");
            AppendHelpBullet("Manuel kurtarma uyarısı: UKB gücünü kesmeyin; seri konsol ve en son log ile yetkili kişiye başvurun.");

            AppendHelpSection("9. Güvenli kullanım için hangi kurallara uymalıyım?");
            AppendHelpBullet("Moxa slot ve kanalını şema veya doğrulanmış profil olmadan değiştirmeyin.");
            AppendHelpBullet("Yanlış projeye ait sürümü yüklemeye çalışmayın.");
            AppendHelpBullet("Durdurmanız gerekiyorsa İptal düğmesini veya normal pencere kapatma düğmesini kullanın; Görev Yöneticisi ile zorla sonlandırmayın.");
            AppendHelpBullet("Sorun olduğunda tahminle işlem yapmak yerine log ve checkpoint sonuçlarını inceleyin.");

            txtHelpGuide.SelectionStart = 0;
            txtHelpGuide.ScrollToCaret();
            PopulateVisualHelpContent();
        }

        private void AppendHelpTitle(string text)
        {
            txtHelpGuide.SelectionFont = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
            txtHelpGuide.SelectionColor = Color.FromArgb(15, 23, 42);
            txtHelpGuide.AppendText(text + Environment.NewLine);
            txtHelpGuide.AppendText(Environment.NewLine);
        }

        private void AppendHelpSection(string text)
        {
            if (txtHelpGuide.TextLength > 0)
                txtHelpGuide.AppendText(Environment.NewLine);

            txtHelpGuide.SelectionIndent = 8;
            txtHelpGuide.SelectionFont = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            txtHelpGuide.SelectionColor = Color.FromArgb(20, 83, 45);
            txtHelpGuide.SelectionBackColor = Color.FromArgb(236, 253, 245);
            txtHelpGuide.AppendText("  " + text + "  " + Environment.NewLine);

            txtHelpGuide.SelectionBackColor = Color.White;
            txtHelpGuide.SelectionIndent = 0;
            txtHelpGuide.AppendText(Environment.NewLine);
        }

        private void AppendHelpParagraph(string text)
        {
            txtHelpGuide.SelectionFont = new Font("Segoe UI", 10.5F);
            txtHelpGuide.SelectionColor = ModernUi.TextPrimary;
            txtHelpGuide.AppendText(text + Environment.NewLine + Environment.NewLine);
        }

        private void AppendHelpBullet(string text)
        {
            txtHelpGuide.SelectionIndent = 28;
            txtHelpGuide.SelectionHangingIndent = 14;
            txtHelpGuide.SelectionFont = new Font("Segoe UI", 10.5F);
            txtHelpGuide.SelectionColor = ModernUi.TextPrimary;
            txtHelpGuide.AppendText("\u2022  " + text + Environment.NewLine + Environment.NewLine);
            txtHelpGuide.SelectionIndent = 0;
            txtHelpGuide.SelectionHangingIndent = 0;
        }

        private void AppendHelpCode(string text)
        {
            txtHelpGuide.SelectionIndent = 42;
            txtHelpGuide.SelectionFont = new Font("Consolas", 10F, FontStyle.Bold);
            txtHelpGuide.SelectionColor = Color.FromArgb(22, 101, 52);
            txtHelpGuide.SelectionBackColor = Color.FromArgb(240, 253, 244);
            txtHelpGuide.AppendText(text + Environment.NewLine + Environment.NewLine);
            txtHelpGuide.SelectionBackColor = Color.White;
            txtHelpGuide.SelectionIndent = 0;
        }

        private void PopulateVisualHelpContent()
        {
            if (pnlHelpContent == null)
                return;

            pnlHelpContent.SuspendLayout();
            try
            {
                while (pnlHelpContent.Controls.Count > 0)
                {
                    Control control = pnlHelpContent.Controls[0];
                    pnlHelpContent.Controls.RemoveAt(0);
                    if (control is PictureBox picture && picture.Image != null)
                        picture.Image.Dispose();
                    control.Dispose();
                }

                bool english = Localization.IsEnglish;
                AddVisualHelpTitle(
                    english ? "Version Installation v-1.0.3 — Visual User Guide" :
                        "Sürüm Yükleme v-1.0.3 — Görsel Kullanım Kılavuzu",
                    english ?
                        "Follow the checklist first. Then use the numbered screenshots to identify each field before starting installation." :
                        "Önce kısa kontrol listesini tamamlayın. Ardından yüklemeyi başlatmadan önce numaralı görsellerden her alanın görevini kontrol edin.");

                AddVisualHelpCard(
                    english ? "Before starting — quick checklist" : "Başlamadan önce — hızlı kontrol listesi",
                    english ? new[]
                    {
                        "The Platform field must show the intended UAV_PROJECT_NAME; the computer's value is marked as Default.",
                        "Connect the OTG cable directly to the selected UKB. Do not use a USB hub or a charge-only cable.",
                        "Verify the UKB COM port in Windows Device Manager and close Tera Term or any other serial terminal.",
                        "Power Box and Relay Box must be reachable over Ethernet; verify their IP, MOD and channel mappings.",
                        "Allow the application through Windows Defender Firewall for Private and Public networks.",
                        "Verify that Desktop\\UKB Versions\\<Platform> contains the authorized complete TEZI package."
                    } : new[]
                    {
                        "Platform alanında yükleme yapılacak UAV_PROJECT_NAME görülmelidir; bilgisayarın kendi değeri Varsayılan olarak işaretlenir.",
                        "OTG kablosunu doğrudan seçilen UKB'ye bağlayın. USB hub veya yalnızca şarj kablosu kullanmayın.",
                        "Windows Aygıt Yöneticisinde UKB COM portunu doğrulayın; Tera Term ve diğer seri terminal uygulamalarını kapatın.",
                        "Power Box ve Relay Box Ethernet üzerinden erişilebilir olmalıdır; IP, MOD ve kanal eşlemelerini kontrol edin.",
                        "Windows Defender Güvenlik Duvarında uygulamanın Özel ve Genel ağ izinlerini kontrol edin.",
                        "Masaüstü\\UKB Sürümleri\\<Platform> altında yetkili ve eksiksiz TEZI paketinin bulunduğunu doğrulayın."
                    },
                    Color.FromArgb(236, 253, 245));

                AddVisualHelpHeading(english ? "1. Main installation screen" : "1. Ana yükleme ekranı");
                AddVisualHelpImage("SurumYakma.Assets.Help.help-main.png");
                AddVisualHelpNumberedCard(english ? new[]
                {
                    "Platform: The computer environment is selected by default; change it only when installing for another registered platform.",
                    "Version: Valid packages are sorted newest first; choose an older version only when required.",
                    "Target UKB: Select the physical UKB whose COM, Power and Recovery mappings will be used.",
                    "Start: Enabled only when a valid package exists and the selected hardware is ready.",
                    "Progress: Shows the current step of the 12-stage installation workflow.",
                    "Connections: Both Power Box Connection and Relay Box Connection must be green before starting.",
                    "Process Console: Shows important live messages; detailed checkpoints are saved under the logs folder."
                } : new[]
                {
                    "Platform: Bilgisayarın ortamı varsayılan seçilir; yalnızca kayıtlı başka bir platforma yükleme yapacaksanız değiştirin.",
                    "Yüklenecek sürüm: Geçerli paketler en yeniden eskiye sıralanır; gerekirse eski bir sürüm seçebilirsiniz.",
                    "Yüklenecek UKB: COM, Power ve Recovery eşlemeleri kullanılacak fiziksel UKB'yi seçin.",
                    "Başlat düğmesi: Yalnızca geçerli paket bulunduğunda ve seçili donanım hazır olduğunda etkinleşir.",
                    "İlerleme: 12 aşamalı yükleme akışında hangi adımda olduğunuzu gösterir.",
                    "Bağlantılar: Başlatmadan önce Power Box Connection ve Relay Box Connection göstergeleri yeşil olmalıdır.",
                    "İşlem Konsolu: Önemli canlı mesajları gösterir; ayrıntılı checkpoint kayıtları logs klasörüne yazılır."
                });

                AddVisualHelpHeading(english ? "2. UKB connection settings" : "2. UKB bağlantı ayarları");
                AddVisualHelpImage("SurumYakma.Assets.Help.help-ukb-settings.png");
                AddVisualHelpNumberedCard(english ? new[]
                {
                    "Power Box / Relay Box IP: Common Moxa addresses used by all UKB rows.",
                    "Baud Rate: Common serial speed; normally 115200 unless the verified platform configuration says otherwise.",
                    "UKB table: Each row keeps that UKB's COM, Power MOD/channel and Recovery MOD/channel together.",
                    "USB-NCM network: UKB Target IP, HTTP Server IP and HTTP Port used by Easy Installer.",
                    "Automatic address detection: Keep selected so the application can detect the PC USB-NCM address after Easy Installer starts.",
                    "Save and Connect: Saves the profile and reconnects Moxa devices with the entered values."
                } : new[]
                {
                    "Power Box / Relay Box IP: Tüm UKB satırları için kullanılan ortak Moxa adresleridir.",
                    "Baud Rate: Ortak seri haberleşme hızıdır; doğrulanmış platform bilgisi farklı değilse 115200 kullanılır.",
                    "UKB tablosu: Her UKB'nin COM, Power MOD/kanal ve Recovery MOD/kanal bilgilerini aynı satırda tutar.",
                    "USB-NCM ağı: Easy Installer'ın kullandığı UKB hedef IP, HTTP sunucu IP ve HTTP portudur.",
                    "Otomatik adres bulma: Easy Installer açıldıktan sonra PC USB-NCM adresinin bulunması için işaretli bırakın.",
                    "Kaydet ve Bağlan: Profili kaydeder ve girilen değerlerle Moxa bağlantısını yeniden kurar."
                });

                AddVisualHelpHeading(english ? "3. Advanced options" : "3. Gelişmiş seçenekler");
                AddVisualHelpImage("SurumYakma.Assets.Help.help-advanced-options.png");
                AddVisualHelpNumberedCard(english ? new[]
                {
                    "UKB Versions Root Folder: Normally Desktop\\UKB Versions; platform folders are searched below it.",
                    "Platform list: Manages the platform folders that can be selected in the application.",
                    "Version folder: Must contain a valid TEZI package under the selected platform; package prefixes are not compared.",
                    "Table actions: Add a row, delete checked rows, add the detected environment or load defaults.",
                    "Save and Connect: Stores advanced options together with the active platform profile."
                } : new[]
                {
                    "UKB Sürümleri ana klasörü: Normalde Masaüstü\\UKB Sürümleri yoludur; platform klasörleri bunun altında aranır.",
                    "Platform listesi: Uygulamada seçilebilecek platform klasörlerini yönetir.",
                    "Sürüm klasörü: Seçilen platform altında geçerli bir TEZI paketi içermelidir; paket ön adı karşılaştırılmaz.",
                    "Tablo işlemleri: Satır ekler, işaretli satırları siler, algılanan ortamı ekler veya varsayılanları yükler.",
                    "Kaydet ve Bağlan: Gelişmiş seçenekleri aktif platform profiliyle birlikte kaydeder."
                });

                AddVisualHelpCard(
                    english ? "Normal installation order" : "Normal yükleme sırası",
                    english ? new[]
                    {
                        "Complete the quick checklist and open Connection Settings if any value is uncertain.",
                        "Select Platform, Version and Target UKB.",
                        "Wait until both Moxa connection indicators are green.",
                        "Press START VERSION INSTALLATION and follow only the popup instructions.",
                        "If the OTG warning appears, connect the cable and press OK; the application retries automatically.",
                        "At completion, confirm the installed OFP version and LINK result in the result window."
                    } : new[]
                    {
                        "Hızlı kontrol listesini tamamlayın; emin olmadığınız değer varsa Bağlantı Ayarlarını açın.",
                        "Platform, Yüklenecek Sürüm ve Yüklenecek UKB seçimlerini yapın.",
                        "İki Moxa bağlantı göstergesi de yeşil olana kadar bekleyin.",
                        "SÜRÜM YÜKLEMEYİ BAŞLAT düğmesine basın ve yalnızca açılan yönlendirmeleri uygulayın.",
                        "OTG uyarısı çıkarsa kabloyu bağlayıp Tamam'a basın; uygulama otomatik yeniden dener.",
                        "İşlem sonunda sonuç penceresindeki OFP sürümünü ve LINK sonucunu kontrol edin."
                    },
                    Color.FromArgb(239, 246, 255));

                AddVisualHelpCard(
                    english ? "Important safety notes" : "Önemli güvenlik notları",
                    english ? new[]
                    {
                        "Do not change MOD/channel values without a verified platform diagram.",
                        "Do not force-close the process from Task Manager; use Cancel or the normal close button.",
                        "If installation fails, share the newest dated session log from the logs folder."
                    } : new[]
                    {
                        "Doğrulanmış platform şeması olmadan MOD/kanal değerlerini değiştirmeyin.",
                        "İşlemi Görev Yöneticisinden zorla kapatmayın; İptal veya normal pencere kapatma düğmesini kullanın.",
                        "Yükleme başarısız olursa logs klasöründeki en yeni tarihli oturum logunu paylaşın."
                    },
                    Color.FromArgb(254, 242, 242));
            }
            finally
            {
                pnlHelpContent.ResumeLayout(true);
                pnlHelpContent.AutoScrollPosition = Point.Empty;
            }
        }

        private void AddVisualHelpTitle(string title, string description)
        {
            pnlHelpContent.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                MaximumSize = new Size(1120, 0),
                Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Margin = new Padding(4, 2, 4, 8)
            });
            pnlHelpContent.Controls.Add(new Label
            {
                Text = description,
                AutoSize = true,
                MaximumSize = new Size(1120, 0),
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = ModernUi.TextSecondary,
                Margin = new Padding(4, 0, 4, 16)
            });
        }

        private void AddVisualHelpHeading(string text)
        {
            pnlHelpContent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(1120, 0),
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 83, 45),
                BackColor = Color.FromArgb(236, 253, 245),
                Padding = new Padding(10, 7, 10, 7),
                Margin = new Padding(4, 16, 4, 10)
            });
        }

        private void AddVisualHelpCard(string title, string[] items, Color backColor)
        {
            var card = new TableLayoutPanel
            {
                Width = 1120,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = backColor,
                Padding = new Padding(16, 13, 16, 11),
                Margin = new Padding(4, 4, 4, 12),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            card.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                MaximumSize = new Size(1060, 0),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = ModernUi.TextPrimary,
                Margin = new Padding(2, 0, 2, 8)
            });
            foreach (string item in items)
                card.Controls.Add(new Label
                {
                    Text = "✓  " + item,
                    AutoSize = true,
                    MaximumSize = new Size(1060, 0),
                    Font = new Font("Segoe UI", 10.2F),
                    ForeColor = ModernUi.TextPrimary,
                    Margin = new Padding(8, 2, 2, 7)
                });
            pnlHelpContent.Controls.Add(card);
        }

        private void AddVisualHelpNumberedCard(string[] items)
        {
            var card = new TableLayoutPanel
            {
                Width = 1120,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(14, 12, 14, 10),
                Margin = new Padding(4, 8, 4, 12)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < items.Length; index++)
            {
                var badge = new Label
                {
                    Text = (index + 1).ToString(),
                    Size = new Size(30, 30),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(37, 99, 235),
                    Margin = new Padding(3, 3, 8, 6)
                };
                ModernUi.ApplyRoundedRegion(badge, 15);
                card.Controls.Add(badge, 0, index);
                card.Controls.Add(new Label
                {
                    Text = items[index],
                    AutoSize = true,
                    MaximumSize = new Size(1025, 0),
                    Font = new Font("Segoe UI", 10.2F),
                    ForeColor = ModernUi.TextPrimary,
                    Margin = new Padding(2, 6, 2, 9)
                }, 1, index);
            }
            pnlHelpContent.Controls.Add(card);
        }

        private void AddVisualHelpImage(string resourceName)
        {
            using Stream stream = typeof(Form1).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return;
            using var source = Image.FromStream(stream);
            var image = new Bitmap(source);
            int width = Math.Min(1120, image.Width);
            int height = (int)Math.Round(image.Height * (width / (double)image.Width));
            pnlHelpContent.Controls.Add(new PictureBox
            {
                Image = image,
                Size = new Size(width, height),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(4 + Math.Max(0, (1120 - width) / 2), 2, 4, 4)
            });
        }

        private void UpdateMainActionButtonTexts()
        {
            if (_cfg == null)
                return;

            if (_cfg.NetworkInstallMode)
            {
                UKB1Yak.Text = Localization.T(
                    $"UKB{_cfg.SelectedUkb} — SÜRÜM YÜKLEMEYİ BAŞLAT",
                    $"UKB{_cfg.SelectedUkb} — START VERSION INSTALLATION");
                UKB2Yak.Text = "";
                return;
            }

            bool selectedVersionAlreadyOnFlash =
                surumList.SelectedItem is VersionChoice choice && !choice.IsFromPc;
            string actionText = selectedVersionAlreadyOnFlash
                ? "FLASH'TAKİ SÜRÜMLE UKB YÜKLEMESİNİ BAŞLAT"
                : "FLASH'I HAZIRLA VE YÜKLEMEYİ BAŞLAT";

            UKB1Yak.Text = _cfg.TwoUkb ? "UKB1 — " + actionText : actionText;
            UKB2Yak.Text = _cfg.TwoUkb
                ? "UKB2 — " + actionText
                : "";
        }

        private void ConfigureHardwareTestUi(bool connected)
        {
            bool testMode = _cfg != null && _cfg.HardwareTestMode;
            grpHardwareTest.Visible = testMode;
            btnTestPowerPulse.Enabled = testMode && connected;
            btnTestRecoveryPulse.Enabled = testMode && connected;

            if (!testMode)
                return;

            Text = ApplicationDisplayName + " — " + _cfg.ProfileName;
            UKB1Yak.Visible = false;
            UKB2Yak.Visible = false;
            driveList.Enabled = false;
            projectList.Enabled = false;
            surumList.Enabled = false;
            button2.Enabled = false;
            Logger.Warn(
                $"TEST DONANIMI profili aktif: IP={_cfg.PowerBoxIp}, " +
                $"Power={_cfg.PowerSlot}/{_cfg.PowerChannel1}, " +
                $"Recovery={_cfg.RecoverySlot}/{_cfg.RecoveryChannel1}. Sürüm yükleme devre dışı.");
        }

        private async Task RefreshHardwareTestStatesAsync()
        {
            try
            {
                uint power = await _moxa.ReadPowerAsync(false, CancellationToken.None);
                uint recovery = await _moxa.ReadRecoveryAsync(false, CancellationToken.None);
                lblTestPowerState.Text =
                    $"Kanal {_cfg.PowerChannel1}: {(power == 1 ? "ON" : "OFF")}";
                lblTestRecoveryState.Text =
                    $"Kanal {_cfg.RecoveryChannel1}: {(recovery == 1 ? "ON" : "OFF")}";
            }
            catch (Exception ex)
            {
                Logger.Error("Test kanallarının başlangıç durumu okunamadı", ex);
                lblTestPowerState.Text = "Power: okuma hatası";
                lblTestRecoveryState.Text = "Recovery: okuma hatası";
            }
        }

        private async Task RunHardwarePulseTestAsync(bool powerChannel)
        {
            if (_cfg == null || !_cfg.HardwareTestMode)
                return;
            if (_hardwareTestBusy)
            {
                Logger.Warn("Donanım testi zaten devam ediyor.");
                return;
            }

            string label = powerChannel ? "Power" : "Recovery";
            byte slot = powerChannel ? _cfg.PowerSlot : _cfg.RecoverySlot;
            byte channel = powerChannel ? _cfg.PowerChannel1 : _cfg.RecoveryChannel1;
            Label stateLabel = powerChannel ? lblTestPowerState : lblTestRecoveryState;

            _hardwareTestBusy = true;
            btnTestPowerPulse.Enabled = false;
            btnTestRecoveryPulse.Enabled = false;
            bool commandedOn = false;
            try
            {
                uint initial = powerChannel
                    ? await _moxa.ReadPowerAsync(false, CancellationToken.None)
                    : await _moxa.ReadRecoveryAsync(false, CancellationToken.None);
                stateLabel.Text = $"Kanal {channel}: {(initial == 1 ? "ON" : "OFF")}";
                if (initial != 0)
                    throw new InvalidOperationException(
                        $"{label} test kanalı başlangıçta OFF değil. Güvenlik nedeniyle darbe testi başlatılmadı.");

                lblStatus.Text = $"{label}: kanal ON yapılıyor...";
                if (powerChannel)
                    await _moxa.SetPowerAsync(false, 1, CancellationToken.None);
                else
                    await _moxa.SetRecoveryAsync(false, 1, CancellationToken.None);
                commandedOn = true;

                uint onValue = powerChannel
                    ? await _moxa.ReadPowerAsync(false, CancellationToken.None)
                    : await _moxa.ReadRecoveryAsync(false, CancellationToken.None);
                if (onValue != 1)
                    throw new InvalidOperationException($"{label} kanalı ON geri okuması doğrulanamadı.");
                stateLabel.Text = $"Kanal {channel}: ON";

                await Task.Delay(1000);

                lblStatus.Text = $"{label}: kanal OFF yapılıyor...";
                if (powerChannel)
                    await _moxa.SetPowerAsync(false, 0, CancellationToken.None);
                else
                    await _moxa.SetRecoveryAsync(false, 0, CancellationToken.None);
                commandedOn = false;

                uint offValue = powerChannel
                    ? await _moxa.ReadPowerAsync(false, CancellationToken.None)
                    : await _moxa.ReadRecoveryAsync(false, CancellationToken.None);
                if (offValue != 0)
                    throw new InvalidOperationException($"{label} kanalı OFF geri okuması doğrulanamadı.");

                stateLabel.Text = $"Kanal {channel}: OFF ✓";
                lblStatus.Text = $"{label} darbe testi başarılı.";
                Logger.Info($"{label} test darbesi başarıyla tamamlandı (slot={slot}, channel={channel}).");
            }
            catch (Exception ex)
            {
                Logger.Error($"{label} test darbesi başarısız", ex);
                lblStatus.Text = $"{label} testi başarısız: {ex.Message}";
                stateLabel.Text = $"Kanal {channel}: HATA";
                MessageBox.Show(ex.Message, "Donanım Testi Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (commandedOn)
                {
                    try
                    {
                        if (powerChannel)
                            await _moxa.SetPowerAsync(false, 0, CancellationToken.None);
                        else
                            await _moxa.SetRecoveryAsync(false, 0, CancellationToken.None);
                        stateLabel.Text = $"Kanal {channel}: OFF (kurtarma)";
                        Logger.Warn($"{label} test kanalı hata sonrası OFF durumuna döndürüldü.");
                    }
                    catch (Exception cleanupEx)
                    {
                        stateLabel.Text = $"Kanal {channel}: DURUM BELİRSİZ";
                        Logger.Error($"{label} test kanalı OFF durumuna döndürülemedi", cleanupEx);
                    }
                }

                bool connected = _moxa.IsPowerConnected && _moxa.IsRelayConnected;
                _hardwareTestBusy = false;
                btnTestPowerPulse.Enabled = connected;
                btnTestRecoveryPulse.Enabled = connected;
            }
        }

        private void InitializeConsoleFlushTimer()
        {
            _consoleFlushTimer = new System.Windows.Forms.Timer { Interval = 125 };
            _consoleFlushTimer.Tick += (s, e) => FlushPendingConsoleEntries();
            _consoleFlushTimer.Start();
        }

        private void Logger_MessageWritten(LogEntry entry)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            // Keep FEC TX descriptor dumps in the detailed file log, but do not
            // flood the operator console with hundreds of diagnostic rows.
            if (IsSerialTxRingDiagnostic(entry))
                return;

            _pendingConsoleEntries.Enqueue(entry);
            int pending = Interlocked.Increment(ref _pendingConsoleCount);
            while (pending > ConsolePendingEntryLimit &&
                   _pendingConsoleEntries.TryDequeue(out _))
            {
                pending = Interlocked.Decrement(ref _pendingConsoleCount);
            }
        }

        private static bool IsSerialTxRingDiagnostic(LogEntry entry)
        {
            string message = entry?.Message;
            if (string.IsNullOrWhiteSpace(message) ||
                !message.StartsWith("[serial]", StringComparison.OrdinalIgnoreCase))
                return false;

            if (message.IndexOf("TX ring dump", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (message.IndexOf("Nr", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("SC", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("addr", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("len", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("SKB", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return SerialTxDescriptorRowPattern.IsMatch(message);
        }

        private void FlushPendingConsoleEntries()
        {
            if (IsDisposed || txtProcessConsole == null)
                return;

            int drained = 0;
            var segments = new List<ConsoleSegment>();
            while (drained < ConsoleFlushBatchSize &&
                   _pendingConsoleEntries.TryDequeue(out LogEntry entry))
            {
                Interlocked.Decrement(ref _pendingConsoleCount);
                string text = $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}" +
                    Environment.NewLine;
                AddConsoleSegment(segments, entry.Level, text);
                drained++;
            }

            if (segments.Count == 0)
                return;

            SetControlRedraw(txtProcessConsole, false);
            try
            {
                AppendConsoleSegments(segments, scrollToEnd: true);
                TrimProcessConsole();
            }
            finally
            {
                SetControlRedraw(txtProcessConsole, true);
                txtProcessConsole.Invalidate();
            }
        }

        private static void AddConsoleSegment(
            List<ConsoleSegment> segments,
            string level,
            string text)
        {
            ConsoleSegment segment = segments.Count > 0 ? segments[segments.Count - 1] : null;
            if (segment == null || !string.Equals(segment.Level, level, StringComparison.Ordinal))
            {
                segment = new ConsoleSegment { Level = level };
                segments.Add(segment);
            }
            segment.Text.Append(text);
        }

        private void AppendConsoleSegments(
            IEnumerable<ConsoleSegment> segments,
            bool scrollToEnd)
        {
            foreach (ConsoleSegment segment in segments)
            {
                txtProcessConsole.SelectionStart = txtProcessConsole.TextLength;
                txtProcessConsole.SelectionColor =
                    segment.Level == "ERROR" ? Color.LightCoral :
                    segment.Level == "WARN" ? Color.Khaki : Color.Gainsboro;
                txtProcessConsole.AppendText(segment.Text.ToString());
            }
            if (scrollToEnd)
            {
                txtProcessConsole.SelectionStart = txtProcessConsole.TextLength;
                txtProcessConsole.ScrollToCaret();
            }
        }

        private void TrimProcessConsole()
        {
            if (txtProcessConsole.TextLength <= ConsoleMaximumCharacters)
                return;

            int requestedCut = txtProcessConsole.TextLength - ConsoleTrimToCharacters;
            int lineEnd = txtProcessConsole.Find(
                "\n",
                Math.Max(0, requestedCut),
                RichTextBoxFinds.None);
            int cut = lineEnd >= 0 ? lineEnd + 1 : requestedCut;
            bool wasReadOnly = txtProcessConsole.ReadOnly;
            try
            {
                txtProcessConsole.ReadOnly = false;
                txtProcessConsole.Select(0, cut);
                txtProcessConsole.SelectedText = "";
            }
            finally
            {
                txtProcessConsole.ReadOnly = wasReadOnly;
            }
        }

        private static void SetControlRedraw(Control control, bool enabled)
        {
            if (control == null || control.IsDisposed || !control.IsHandleCreated)
                return;
            NativeMethods.SendMessage(
                control.Handle,
                NativeMethods.WmSetRedraw,
                enabled ? new IntPtr(1) : IntPtr.Zero,
                IntPtr.Zero);
        }

        private void UpdateConnectionLabels()
        {
            bool selectedUkb2 = _cfg.SelectedUkb == 2;
            string ukbLabel = "UKB" + _cfg.SelectedUkb;
            RelayBoxBaglantisiLabel.BackColor = _moxa.IsRelayConnected ? ModernUi.Success : ModernUi.Danger;
            RelayBoxIPLabel.Text = _moxa.IsRelayConnected
                ? $"{ukbLabel} | IP: {_cfg.RelayBoxIp} | Slot: {_cfg.GetRecoverySlot(selectedUkb2)} | Recovery CH: {_cfg.GetRecoveryChannel(selectedUkb2)}"
                : "";
            PowerBoxBaglantisiLabel.BackColor = _moxa.IsPowerConnected ? ModernUi.Success : ModernUi.Danger;
            byte powerPrimary = _cfg.GetPowerChannel(selectedUkb2);
            int powerSecondary = _cfg.GetPowerSecondaryChannel(selectedUkb2);
            string powerChannels = powerSecondary >= 0
                ? powerPrimary + "+" + powerSecondary
                : powerPrimary.ToString();
            PowerBoxIPLabel.Text = _moxa.IsPowerConnected
                ? $"{ukbLabel} | IP: {_cfg.PowerBoxIp} | Slot: {_cfg.GetPowerSlot(selectedUkb2)} | Power CH: {powerChannels}"
                : "";
            UpdateConnectionSummary();
        }

        private void UpdateConnectionSummary()
        {
            if (_cfg == null || lblConnectionSummary == null)
                return;
            lblConnectionSummary.Text = $"UKB{_cfg.SelectedUkb}: " +
                $"{_cfg.GetSerialPort(_cfg.SelectedUkb == 2)} ({_cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2)})";
            btnConnectionSettings.Enabled = _cts == null && !_criticalPhase && !_manualRecoveryRequired;
        }

        private async Task OpenConnectionSettingsAsync()
        {
            if (_cts != null || _criticalPhase || _manualRecoveryRequired || _networkFeedReady)
            {
                MessageBox.Show("Devam eden/hazırlanmış ağ işlemi veya manuel kontrol varken bağlantı ayarları değiştirilemez.",
                    "Ayarlar Kilitli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new ConnectionSettingsForm(
                _cfg,
                _projectName,
                showEasyInstallerCredentials: false);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            _serial.Dispose();
            _serial = new UkbSerialMonitor(_cfg);
            _workflow = new FlashWorkflow(_cfg, _moxa, _versions, _serial)
            {
                WaitForUkbMediaReadyAsync = WaitForUkbMediaReadyAsync
            };
            try
            {
                SettingsProfileStore.SaveCurrentProject(_projectName, _cfg);
                Logger.Checkpoint(
                    "SETTINGS_JSON_PROFILE",
                    "SAVED",
                    $"file={SettingsProfileStore.DefaultPath}; environment={_projectName}; " +
                    $"serial={_cfg.SerialPortName}; power={_cfg.PowerBoxIp}; relay={_cfg.RelayBoxIp}");
            }
            catch (Exception settingsEx)
            {
                Logger.Error("Settings.json ortam profili kaydedilemedi", settingsEx);
                Logger.Checkpoint(
                    "SETTINGS_JSON_PROFILE",
                    "FAILED",
                    $"file={SettingsProfileStore.DefaultPath}; environment={_projectName}; " +
                    $"exception={settingsEx.GetType().Name}; message={settingsEx.Message}");
                MessageBox.Show(
                    this,
                    "Baglanti ayarlari makine profiline kaydedildi ancak Settings.json guncellenemedi.\n\n" +
                    settingsEx.Message,
                    "Settings.json Uyarisi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            Logger.Checkpoint("CONNECTION_SETTINGS", "SAVED",
                $"file={_cfg.LoadedConfigPath}; target=UKB{_cfg.SelectedUkb}; " +
                $"serial1={_cfg.SerialPortName}; serial2={_cfg.SerialPortName2}; " +
                $"power1={_cfg.PowerBoxIp}/{_cfg.PowerSlot}/{_cfg.PowerChannel1}+{_cfg.PowerChannel1Secondary}; " +
                $"power2={_cfg.PowerBoxIp}/{_cfg.GetPowerSlot(true)}/{_cfg.PowerChannel2}+{_cfg.PowerChannel2Secondary}; " +
                $"recovery1={_cfg.RelayBoxIp}/{_cfg.RecoverySlot}/{_cfg.RecoveryChannel1}; " +
                $"recovery2={_cfg.RelayBoxIp}/{_cfg.GetRecoverySlot(true)}/{_cfg.RecoveryChannel2}; " +
                $"target={_cfg.UkbTargetIp}; server={_cfg.NetworkServerIp}:{_cfg.NetworkServerPort}; autoServer={_cfg.AutoDetectNetworkServerIp}; " +
                $"projectMappings={_cfg.ProjectPackageMappings.Count}");

            // Hedef/COM seçimini MOXA bağlantı denemesinin timeout süresini bekletmeden
            // kullanıcıya yansıt. Butonun etkinlik şartı değişmez.
            UpdateConnectionSummary();
            if (cmbTargetSelector != null)
                cmbTargetSelector.SelectedIndex = Math.Max(0, Math.Min(5, _cfg.SelectedUkb - 1));
            UpdateMainActionButtonTexts();
            RefreshVersionList();
            LayoutRuntimeControls();
            lblStatus.Text = Localization.T("Yeni ayarlarla Moxa bağlantıları kuruluyor...", "Connecting to Moxa devices with the new settings...");
            _safeOutputsReady = false;
            _moxaConnected = await _moxa.ConnectAsync();
            _safeOutputsReady = _moxaConnected;
            if (_moxaConnected)
                Logger.Checkpoint(
                    "SETTINGS_RECONNECT_OUTPUTS_UNCHANGED",
                    "SUCCESS",
                    $"selectedUkb=UKB{_cfg.SelectedUkb}; policy=connect-only; Power/Recovery channels were not read or written");
            UpdateConnectionLabels();
            SetButtonsEnabled(_moxaConnected);
            lblStatus.Text = _moxaConnected
                ? Localization.T("Ayarlar kaydedildi ve Moxa bağlantıları kuruldu.", "Settings were saved and Moxa connections were established.")
                : Localization.T(
                    "Ayarlar kaydedildi; Moxa bağlantısı kurulamadı. IP/slot/kanal değerlerini kontrol edin.",
                    "Settings were saved, but the Moxa connection could not be established. Check the IP, slot and channel values.");
        }

        private void SetButtonsEnabled(bool enabled)
        {
            bool selectionsReady =
                _cfg == null ||
                _cfg.HardwareTestMode ||
                (!string.IsNullOrWhiteSpace(_projectName) &&
                 (_cfg.NetworkInstallMode || driveList.SelectedItem != null) &&
                 surumList.Items.Count > 0 &&
                 surumList.SelectedItem is VersionChoice selectedVersion &&
                 VersionManager.IsTeziPackage(selectedVersion.Path));
            selectionsReady = selectionsReady && (_cfg == null || _cfg.HardwareTestMode ||
                !string.IsNullOrWhiteSpace(_cfg.GetSerialPort(_cfg.SelectedUkb == 2)));
            bool flashEnabled =
                enabled &&
                (_cfg == null || _cfg.HardwareTestMode || _safeOutputsReady) &&
                selectionsReady &&
                (_cfg == null || !_cfg.HardwareTestMode);
            ModernUi.SetPrimaryButtonEnabled(UKB1Yak, flashEnabled);
            ModernUi.SetPrimaryButtonEnabled(UKB2Yak, flashEnabled && _cfg != null && _cfg.TwoUkb);
            if (btnConnectionSettings != null)
                btnConnectionSettings.Enabled = _cfg != null && _cts == null && !_criticalPhase && !_manualRecoveryRequired && !_networkFeedReady;
            if (cmbTargetSelector != null)
                cmbTargetSelector.Enabled = _cfg != null && _cts == null && !_criticalPhase && !_manualRecoveryRequired && !_networkFeedReady;
        }

        private static void ProcessCleanup(string processName)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try { p.Kill(); } catch { }
            }
        }

        private void FillDriveList()
        {
            driveList.Items.Clear();
            foreach (DriveInfo info in DriveInfo.GetDrives())
            {
                if (_cfg.OnlyFlash && info.DriveType != DriveType.Removable) continue;
                if (!info.IsReady) continue;
                string label = string.IsNullOrEmpty(info.VolumeLabel) ? "NO NAME" : info.VolumeLabel;
                driveList.Items.Add(info.Name + " (" + label + ")");
            }
            if (driveList.Items.Count > 0) driveList.SelectedIndex = 0;
        }

        private void RefreshProjectList()
        {
            string preferredProject = (_projectName ?? "").Trim();
            projectList.Items.Clear();
            string environmentProject = (Environment.GetEnvironmentVariable("UAV_PROJECT_NAME") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(environmentProject))
            {
                projectList.Items.Add("TANIMSIZ — UAV_PROJECT_NAME bulunamadı");
                projectList.SelectedIndex = 0;
                _projectName = "";
                Logger.Error("UAV_PROJECT_NAME ortam değişkeni bulunamadı. Yanlış proje sürümünün yüklenmesini önlemek için işlem engellendi.");
                RefreshVersionList();
                return;
            }

            var projects = new List<string> { environmentProject };
            projects.AddRange(SettingsProfileStore.GetProjectNames());
            projects.AddRange((_cfg.ProjectPackageMappings ?? new List<ProjectPackageMapping>())
                .Select(mapping => (mapping?.EnvironmentName ?? "").Trim())
                .Where(name => name.Length > 0 && !name.Contains("*")));

            foreach (string project in projects
                .GroupBy(AppConfig.NormalizePlatformFolderKey)
                .Select(group => group.First())
                .OrderBy(project => project.Equals(environmentProject, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(project => project, StringComparer.OrdinalIgnoreCase))
                projectList.Items.Add(AppConfig.NormalizePlatformFolderKey(project) ==
                    AppConfig.NormalizePlatformFolderKey(environmentProject)
                    ? project + Localization.T(DefaultPlatformSuffix, EnglishDefaultPlatformSuffix)
                    : project);

            string selectedDisplay = projectList.Items.Cast<string>().FirstOrDefault(project =>
                AppConfig.NormalizePlatformFolderKey(GetProjectNameFromDisplay(project)) ==
                AppConfig.NormalizePlatformFolderKey(preferredProject));
            if (string.IsNullOrWhiteSpace(selectedDisplay))
                selectedDisplay = environmentProject +
                    Localization.T(DefaultPlatformSuffix, EnglishDefaultPlatformSuffix);
            projectList.SelectedItem = selectedDisplay;
            _projectName = GetProjectNameFromDisplay(selectedDisplay);
            Logger.Info("Varsayılan bilgisayar platformu ortam değişkeninden okundu: " + environmentProject);
            Logger.Info("Aktif sürüm platformu: " + _projectName);
        }

        private void RefreshVersionList()
        {
            surumList.Items.Clear();
            _versionChoices = new VersionChoice[0];

            if (_cfg == null || string.IsNullOrWhiteSpace(_projectName))
            {
                SetButtonsEnabled(_moxaConnected);
                return;
            }

            string platformVersionsPath = _cfg.GetPlatformVersionsPath(_projectName);
            VersionListResult sourceVersions = VersionManager.GetSelectableTeziVersions(
                platformVersionsPath,
                _projectName,
                _cfg.ProjectPackageMappings,
                false);

            if (!_cfg.NetworkInstallMode && driveList.SelectedItem != null)
            {
                string driveRoot = driveList.Text.Substring(0, 2) + "\\";
                VersionListResult flashResult = _versions.GetVersionsInFlash(
                    driveRoot,
                    _projectName);

                var matchingFlash = flashResult.Names
                    .Select((name, index) => new { Name = name, Path = flashResult.Paths[index] })
                    .ToArray();

                _versionChoices = matchingFlash
                    .Select(item => new VersionChoice
                    {
                        Name = item.Name,
                        Path = item.Path,
                        IsFromPc = false
                    })
                    .ToArray();
            }

            var flashNames = new HashSet<string>(
                _versionChoices.Select(choice => choice.Name),
                StringComparer.OrdinalIgnoreCase);
            VersionChoice[] sourceChoices = sourceVersions.Paths
                .Select((path, index) => new { Name = sourceVersions.Names[index], Path = path })
                .Where(item => !flashNames.Contains(item.Name))
                .Select(item => new VersionChoice
                {
                    Name = item.Name,
                    Path = item.Path,
                    IsFromPc = true
                })
                .ToArray();

            _versionChoices = sourceChoices
                .Concat(_versionChoices)
                .OrderByDescending(choice => choice.Name, NaturalVersionNameComparer.Instance)
                .ToArray();

            foreach (VersionChoice version in _versionChoices)
                surumList.Items.Add(version);

            if (surumList.Items.Count > 0)
            {
                surumList.SelectedIndex = 0;
                var selected = (VersionChoice)surumList.SelectedItem;
                Logger.Info(
                    $"{_projectName} projesi için {surumList.Items.Count} uygun sürüm bulundu; " +
                    $"en güncel sürüm otomatik seçildi: {selected.Name}" +
                    (selected.IsFromPc ? "" : " [flash'ta hazır]"));
            }
            else if (Directory.Exists(platformVersionsPath))
            {
                Logger.Warn($"{_projectName} projesine ait tam TEZI (*build.0) paketi bulunamadı. Ham OFP paketleri üretim yüklemesinde kullanılamaz.");
            }
            else if (!string.IsNullOrWhiteSpace(platformVersionsPath))
            {
                Logger.Warn("Platform sürüm klasörü bulunamadı: " + platformVersionsPath);
            }

            SetButtonsEnabled(_moxaConnected);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _cfg.OverrideSurumlerPath(dlg.SelectedPath);
                    textBox2.Text = dlg.SelectedPath;
                    RefreshProjectList();
                }
            }
        }

        private void projectList_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selected = GetProjectNameFromDisplay(projectList.SelectedItem as string);
            if (string.IsNullOrWhiteSpace(selected) || selected.StartsWith("TANIMSIZ", StringComparison.OrdinalIgnoreCase))
                return;
            bool changed = !selected.Equals(_projectName, StringComparison.OrdinalIgnoreCase);
            _projectName = selected;
            if (changed)
                Logger.Info("Kullanıcı aktif sürüm platformunu seçti: " + _projectName);
            RefreshVersionList();
        }

        private static string GetProjectNameFromDisplay(string display)
        {
            string value = (display ?? "").Trim();
            if (value.EndsWith(DefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - DefaultPlatformSuffix.Length).Trim();
            if (value.EndsWith(EnglishDefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - EnglishDefaultPlatformSuffix.Length).Trim();
            return value;
        }
        private void driveList_SelectedIndexChanged(object sender, EventArgs e) => RefreshVersionList();

        private async void UKB1Yak_Click(object sender, EventArgs e)
        {
            if (_cfg != null && _cfg.NetworkInstallMode)
            {
                bool whichUkb = _cfg.SelectedUkb == 2;
                Task operation = RunNetworkStartAsync(whichUkb);
                _activeWorkflowTask = operation;
                try
                {
                    await operation;
                }
                finally
                {
                    if (ReferenceEquals(_activeWorkflowTask, operation))
                        _activeWorkflowTask = null;
                }
                return;
            }
            if (CompleteUkbMediaReady(whichUkb: false))
                return;
            await StartFlashAsync(whichUkb: false);
        }

        private async Task RunNetworkStartAsync(bool whichUkb)
        {
            SetNetworkStage(1, "Sürüm Paketi Hazırlanıyor");
            if (_networkFeedReady || await PrepareNetworkFeedAsync())
            {
                await RunNetworkConnectivityTestAsync(whichUkb);
            }
            else
                SetNetworkProgressStopped("Paket hazırlığı tamamlanamadı");
        }


        private async Task<bool> PrepareNetworkFeedAsync()
        {
            VersionChoice selected = surumList.SelectedItem as VersionChoice;
            Logger.Checkpoint(
                "NETWORK_PACKAGE_PREPARE",
                "START",
                $"project={_projectName ?? "not-detected"}; source={selected?.Path ?? "not-selected"}");
            if (selected == null || !selected.IsFromPc || !VersionManager.IsTeziPackage(selected.Path))
            {
                Logger.Checkpoint(
                    "NETWORK_PACKAGE_PREPARE",
                    "FAILED",
                    "reason=invalid_or_missing_full_tezi_package");
                MessageBox.Show(
                    "Sürüm yükleme için PC'deki tam TEZI *build.0 paketini seçin.",
                    "Geçersiz Sürüm",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            UKB1Yak.Enabled = false;
            button2.Enabled = false;
            try
            {
                lblStatus.Text = "Seçilen paket doğrulanıp yüklemeye hazırlanıyor...";
                await StopNetworkServicesAsync();
                _networkFeedReady = false;
                await CleanupNetworkStagingAsync();

                string stagingRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SurumYakmaAgdan",
                    "staging");
                _networkStagingPath = await RunLoggedStageAsync(
                    "NETWORK_PACKAGE_STAGE_COPY",
                    $"source={selected.Path}; stagingRoot={stagingRoot}",
                    () => Task.Run(() => _versions.PrepareVersionForNetwork(selected.Path, stagingRoot)));
                _networkExpectedOfpVersion = await RunLoggedStageAsync(
                    "NETWORK_EXPECTED_OFP_PARSE",
                    "source=" + selected.Path,
                    () => Task.FromResult(VersionManager.GetExpectedOfpVersion(selected.Path)));
                _networkFeedRequest = NewNetworkRequestSource();
                _networkImageRequest = NewNetworkRequestSource();
                _networkPayloadRequest = NewNetworkRequestSource();

                string selectedCom = _cfg.GetSerialPort(_cfg.SelectedUkb == 2);
                await RunLoggedStageAsync(
                    "NETWORK_SERIAL_OPEN",
                    $"port={selectedCom}; baud={_cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2)}",
                    () =>
                    {
                        _serial.Open();
                        return Task.CompletedTask;
                    });
                _networkSerialMark = _serial.Mark();
                _networkFeedReady = true;
                surumList.Enabled = false;
                button2.Enabled = false;

                lblStatus.Text = "Paket hazır. Hedef bağlantısı oluşunca yükleme otomatik başlatılacak.";
                Logger.Info("TEZI paketi hazır; sunucu IP'si Easy Installer başladıktan sonra seçilecek.");
                Logger.Checkpoint(
                    "NETWORK_PACKAGE_PREPARE",
                    "SUCCESS",
                    $"staging={_networkStagingPath}; expectedOfp={_networkExpectedOfpVersion}; serial={_cfg.GetSerialPort(_cfg.SelectedUkb == 2)}");
                UpdateMainActionButtonTexts();
                return true;
            }
            catch (Exception ex)
            {
                _networkFeedReady = false;
                await StopNetworkServicesAsync();
                await CleanupNetworkStagingAsync();
                Logger.Error("Ağ aktarım paketi/sunucusu hazırlanamadı", ex);
                Logger.Checkpoint(
                    "NETWORK_PACKAGE_PREPARE",
                    "FAILED",
                    $"exception={ex.GetType().Name}; message={ex.Message}");
                lblStatus.Text = "Ağ aktarım hazırlığı başarısız: " + ex.Message;
                MessageBox.Show(
                    ex.Message + "\n\nNetworkServerIp değerinin Windows USB-NCM bağdaştırıcısına ait olduğunu ipconfig ile doğrulayın.",
                    "Ağ Aktarım Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                button2.Enabled = !_networkFeedReady;
                SetButtonsEnabled(_moxaConnected);
            }
        }

        private static TaskCompletionSource<string> NewNetworkRequestSource()
        {
            return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void TeziHttpServer_RequestCompleted(string path)
        {
            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                _networkHealthRequest?.TrySetResult(path);
                Logger.Checkpoint("TEZI_HTTP_HEALTH_REQUEST", "SUCCESS", "path=" + path);
                return;
            }
            if (path.Equals("/image_list.json", StringComparison.OrdinalIgnoreCase))
            {
                _networkFeedRequest?.TrySetResult(path);
                Logger.Checkpoint("TEZI_FEED_DISCOVERY", "SUCCESS", "path=" + path);
                return;
            }
            string metadataPath = _teziHttpServer?.ImageMetadataPath;
            if ((!string.IsNullOrWhiteSpace(metadataPath) &&
                 path.Equals(metadataPath, StringComparison.OrdinalIgnoreCase)) ||
                path.Equals("/package/image.json", StringComparison.OrdinalIgnoreCase))
            {
                _networkImageRequest?.TrySetResult(path);
                Logger.Checkpoint("TEZI_IMAGE_METADATA", "SUCCESS", "path=" + path);
                return;
            }
        }

        private void TeziHttpServer_RequestStarted(string path)
        {
            string packagePrefix = _teziHttpServer?.PackagePathPrefix;
            bool packagePath = (!string.IsNullOrWhiteSpace(packagePrefix) &&
                                path.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)) ||
                               path.StartsWith("/package/", StringComparison.OrdinalIgnoreCase);
            if (!packagePath ||
                !IsInstallationPayloadPath(path))
                return;

            _networkPayloadRequest?.TrySetResult(path);
            Logger.Checkpoint("TEZI_INSTALL_PAYLOAD", "STARTED", "path=" + path);
        }

        private static bool IsInstallationPayloadPath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension != ".json" &&
                   extension != ".png" &&
                   extension != ".jpg" &&
                   extension != ".jpeg" &&
                   extension != ".html" &&
                   extension != ".txt" &&
                   extension != ".sh";
        }

        private static async Task<string> WaitForNetworkStageAsync(
            Task<string> stageTask,
            TimeSpan timeout,
            string timeoutMessage,
            CancellationToken ct)
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task delay = Task.Delay(timeout, timeoutCancellation.Token);
            Task completed = await Task.WhenAny(stageTask, delay);
            if (completed == stageTask)
            {
                timeoutCancellation.Cancel();
                return await stageTask;
            }

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(timeoutMessage);
        }

        private async Task<int> SelectReachableHttpPortAsync(
            string serverIp,
            TaskCompletionSource<bool> mdnsQuerySeen,
            CancellationToken ct)
        {
            var candidates = new List<int>();
            void AddCandidate(int port)
            {
                if (!candidates.Contains(port))
                    candidates.Add(port);
            }

            AddCandidate(_teziHttpServer?.Port ?? _cfg.NetworkServerPort);
            AddCandidate(8088);
            AddCandidate(8000);
            AddCandidate(8888);
            AddCandidate(0);

            Exception lastError = null;
            bool useCurrentServer = _teziHttpServer?.IsRunning == true;
            foreach (int candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!useCurrentServer)
                    {
                        TeziHttpServer previousServer = _teziHttpServer;
                        _teziHttpServer = null;
                        await DisposeInBackgroundAsync(previousServer);
                        var replacement = new TeziHttpServer(serverIp, candidate);
                        replacement.SetPackageRoot(_networkStagingPath);
                        replacement.RequestStarted += TeziHttpServer_RequestStarted;
                        replacement.RequestCompleted += TeziHttpServer_RequestCompleted;
                        replacement.Start();
                        _teziHttpServer = replacement;
                    }
                    useCurrentServer = false;

                    int selectedPort = _teziHttpServer.Port;
                    TeziMdnsAdvertiser previousMdns = _teziMdnsAdvertiser;
                    _teziMdnsAdvertiser = null;
                    await DisposeInBackgroundAsync(previousMdns);
                    _teziMdnsAdvertiser = new TeziMdnsAdvertiser(serverIp, selectedPort);
                    _teziMdnsAdvertiser.RelevantQueryReceived += () =>
                        mdnsQuerySeen.TrySetResult(true);
                    _teziMdnsAdvertiser.Start();

                    string healthUrl = _teziHttpServer.BaseUrl + "/health";
                    _networkHealthRequest = NewNetworkRequestSource();
                    Logger.Checkpoint(
                        "TEZI_HTTP_TARGET_PORT_PROBE",
                        "START",
                        $"candidate={candidate}; selected={selectedPort}; url={healthUrl}; timeoutSec=8; accepted=http-or-mdns-query");
                    _serial.SendHttpHealthProbe(healthUrl);

                    Task<string> healthRequestTask = _networkHealthRequest.Task;
                    Task mdnsEvidenceTask = mdnsQuerySeen.Task;
                    Task probeDeadline = Task.Delay(TimeSpan.FromSeconds(8), ct);
                    Task probeResult = await Task.WhenAny(
                        healthRequestTask,
                        mdnsEvidenceTask,
                        probeDeadline);
                    string verificationMethod;
                    if (probeResult == healthRequestTask)
                    {
                        await healthRequestTask;
                        verificationMethod = "target-http-request";
                    }
                    else if (probeResult == mdnsEvidenceTask)
                    {
                        await mdnsEvidenceTask;
                        verificationMethod = "target-mdns-query";
                        Logger.Checkpoint(
                            "TEZI_HTTP_TARGET_PORT_PROBE",
                            "FALLBACK",
                            $"configured={_cfg.NetworkServerPort}; selected={selectedPort}; " +
                            "reason=serial-command-not-confirmed; target-mdns-query-received; " +
                            "HTTP content requests remain mandatory before installation");
                    }
                    else
                    {
                        ct.ThrowIfCancellationRequested();
                        throw new TimeoutException(
                            "UKB hedefinden HTTP health isteği veya TEZI mDNS sorgusu alınmadı.");
                    }

                    Logger.Checkpoint(
                        "TEZI_HTTP_TARGET_PORT_PROBE",
                        "SUCCESS",
                        $"configured={_cfg.NetworkServerPort}; selected={selectedPort}; url={healthUrl}; method={verificationMethod}");
                    return selectedPort;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.Checkpoint(
                        "TEZI_HTTP_TARGET_PORT_PROBE",
                        "RETRY",
                        $"candidate={candidate}; error={ex.GetType().Name}; message={ex.Message}");
                    await Task.Delay(500, ct);
                }
            }

            throw new InvalidOperationException(
                "Easy Installer, PC HTTP sunucusuna 80/8088/8000/8888 portlarından ulaşamadı. " +
                "Uygulama seçili UKB'nin USB-NCM adaptörünü ve hedef rotasını doğruladı; buna rağmen erişim yoksa " +
                "OTG sürücüsünü, Windows Güvenlik Duvarı iznini ve seçili UKB'nin COM/adaptör eşleşmesini kontrol edin.",
                lastError);
        }
        private static async Task<string> WaitForEasyInstallerEvidenceAsync(
            Task<string> serialReadyTask,
            Task mdnsQueryTask,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task deadline = Task.Delay(timeout, timeoutCancellation.Token);
            Task completed = await Task.WhenAny(serialReadyTask, mdnsQueryTask, deadline);
            if (completed == serialReadyTask)
            {
                timeoutCancellation.Cancel();
                return "SERIAL: " + await serialReadyTask;
            }
            if (completed == mdnsQueryTask)
            {
                await mdnsQueryTask;
                timeoutCancellation.Cancel();
                return "MDNS: Easy Installer hedefinden _tezi._tcp sorgusu alındı";
            }

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Easy Installer ne seri konsoldan ne de ağdaki _tezi._tcp sorgusundan doğrulanabildi.");
        }

        private static async Task RunLoggedStageAsync(
            string id,
            string details,
            Func<Task> action)
        {
            await RunLoggedStageAsync<object>(
                id,
                details,
                async () =>
                {
                    await action();
                    return null;
                });
        }

        private static async Task<T> RunLoggedStageAsync<T>(
            string id,
            string details,
            Func<Task<T>> action)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Checkpoint(id, "START", details);
            try
            {
                T result = await action();
                Logger.Checkpoint(id, "SUCCESS", $"elapsedMs={timer.ElapsedMilliseconds}; {details}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Logger.Checkpoint(id, "CANCELLED", $"elapsedMs={timer.ElapsedMilliseconds}; {details}");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Checkpoint(
                    id,
                    "FAILED",
                    $"elapsedMs={timer.ElapsedMilliseconds}; exception={ex.GetType().Name}; message={ex.Message}; {details}");
                throw;
            }
        }

        private async Task<bool> ProbeInteractiveShellWithRetryAsync(CancellationToken ct)
        {
            for (int attempt = 1; attempt <= SerialShellProbeAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                Logger.Checkpoint(
                    "TEZI_SERIAL_SHELL_RETRY",
                    "START",
                    $"attempt={attempt}/{SerialShellProbeAttempts}; port={_serial.PortName}");
                if (await _serial.ProbeInteractiveShellAsync(ct))
                {
                    Logger.Checkpoint(
                        "TEZI_SERIAL_SHELL_RETRY",
                        "SUCCESS",
                        $"attempt={attempt}/{SerialShellProbeAttempts}; port={_serial.PortName}");
                    return true;
                }

                if (_serial.LastShellProbeRejectedByCommandParser)
                {
                    Logger.Checkpoint(
                        "TEZI_SERIAL_SHELL_RETRY",
                        "STOPPED",
                        $"attempt={attempt}/{SerialShellProbeAttempts}; port={_serial.PortName}; " +
                        "reason=ERR_FORMAT; action=official-zeroconf-flow");
                    break;
                }

                if (attempt < SerialShellProbeAttempts)
                {
                    Logger.Checkpoint(
                        "TEZI_SERIAL_SHELL_RETRY",
                        "WAITING",
                        $"attempt={attempt}/{SerialShellProbeAttempts}; retryDelayMs={SerialShellProbeRetryDelayMilliseconds}");
                    await Task.Delay(SerialShellProbeRetryDelayMilliseconds, ct);
                }
            }

            Logger.Checkpoint(
                "TEZI_SERIAL_SHELL_RETRY",
                "UNAVAILABLE",
                $"attempts={SerialShellProbeAttempts}; port={_serial.PortName}; action=preannounced-feed-reload");
            return false;
        }

        private async Task<bool> TryUseExistingTeziDuringReloadAsync(
            Task<string> feed, IProgress<FlashProgress> progress, string ukb,
            bool whichUkb, string serverIp, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task reload = _workflow.LoadEasyInstallerAsync(
                progress, ukb, whichUkb, linked.Token);
            if (await Task.WhenAny(reload, feed) == reload)
            {
                await reload;
                return false;
            }

            await feed;
            linked.Cancel();
            try { await reload; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            await NetworkUtils.WaitUntilReachableAsync(
                _cfg.UkbTargetIp, _cfg.PingTimeoutMs,
                TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds), ct);
            return true;
        }

        private async Task RebindTeziServicesAsync(
            string serverIp, TaskCompletionSource<bool> mdnsQuerySeen)
        {
            int port = _teziHttpServer?.Port ?? _cfg.NetworkServerPort;
            var oldMdns = _teziMdnsAdvertiser;
            _teziMdnsAdvertiser = null;
            await DisposeInBackgroundAsync(oldMdns);
            var oldHttp = _teziHttpServer;
            _teziHttpServer = null;
            await DisposeInBackgroundAsync(oldHttp);
            _networkFeedRequest = NewNetworkRequestSource();
            _networkImageRequest = NewNetworkRequestSource();
            _networkPayloadRequest = NewNetworkRequestSource();
            _teziHttpServer = TeziHttpServer.StartWithFallback(
                serverIp, port, _networkStagingPath);
            _teziHttpServer.RequestStarted += TeziHttpServer_RequestStarted;
            _teziHttpServer.RequestCompleted += TeziHttpServer_RequestCompleted;
            _teziMdnsAdvertiser = new TeziMdnsAdvertiser(
                serverIp, _teziHttpServer.Port);
            _teziMdnsAdvertiser.RelevantQueryReceived += () =>
                mdnsQuerySeen.TrySetResult(true);
            _teziMdnsAdvertiser.Start();
        }

        private async Task ReloadEasyInstallerWithPublishedFeedAsync(
            bool whichUkb,
            string ukbLabel,
            int selectedTargetNumber,
            string expectedServerIp,
            Task<string> observedFeedRequest,
            TaskCompletionSource<bool> mdnsQuerySeen,
            IProgress<FlashProgress> progress,
            CancellationToken ct)
        {
            Logger.Checkpoint(
                "NETWORK_TEZI_PREANNOUNCED_RELOAD",
                "START",
                $"ukb={ukbLabel}; server={expectedServerIp}; policy=http-mdns-active-before-full-easy-installer-reload");
            lblStatus.Text = "Easy Installer ağ kaynağı hazır tutularak yeniden başlatılıyor...";

            await SetPowerAndVerifyAsync(
                whichUkb,
                0,
                ct,
                "NETWORK_TEZI_RELOAD_POWER_OFF");
            await Task.Delay(FlashWorkflow.RecoveryPowerOffDwellMilliseconds, ct);

            await SetRecoveryAndVerifyAsync(
                whichUkb,
                0,
                ct,
                "NETWORK_TEZI_RELOAD_RECOVERY_NORMAL");
            await Task.Delay(FlashWorkflow.RecoveryNormalSettleMilliseconds, ct);

            HashSet<string> beforeReload = HardwareAutoConfigurator.GetNetworkInterfaceIds();

            await SetRecoveryAndVerifyAsync(
                whichUkb,
                1,
                ct,
                "NETWORK_TEZI_RELOAD_RECOVERY_REAL");
            await Task.Delay(FlashWorkflow.RecoveryRealSetupMilliseconds, ct);

            await SetPowerAndVerifyAsync(
                whichUkb,
                1,
                ct,
                "NETWORK_TEZI_RELOAD_POWER_ON");

            if (await TryUseExistingTeziDuringReloadAsync(
                    observedFeedRequest, progress, ukbLabel, whichUkb, expectedServerIp, ct))
                return;

            string reboundServerIp = await HardwareAutoConfigurator.EnsureNetworkServerIpAsync(
                _cfg,
                beforeReload,
                selectedTargetNumber,
                TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds),
                ct);
            if (!string.Equals(reboundServerIp, expectedServerIp, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Easy Installer yeniden yüklemesinden sonra USB-NCM PC adresi değişti. " +
                    $"Beklenen={expectedServerIp}; bulunan={reboundServerIp}.");
            }

            await NetworkUtils.WaitUntilReachableAsync(
                _cfg.UkbTargetIp,
                _cfg.PingTimeoutMs,
                TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds),
                ct);

            await RebindTeziServicesAsync(reboundServerIp, mdnsQuerySeen);

            Logger.Checkpoint(
                "NETWORK_TEZI_PREANNOUNCED_RELOAD",
                "SUCCESS",
                $"ukb={ukbLabel}; target={_cfg.UkbTargetIp}; server={reboundServerIp}; " +
                "httpAndMdnsRemainedActive=true");
        }

        private async Task<string> WaitForTeziFeedWithRefreshAsync(
            Task<string> feedTask,
            Task mdnsQueryTask,
            TimeSpan timeout,
            string timeoutMessage,
            string attempt,
            CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task deadline = Task.Delay(timeout, linked.Token);
            Task first = await Task.WhenAny(feedTask, mdnsQueryTask, deadline);
            if (first == feedTask)
            {
                linked.Cancel();
                return await feedTask;
            }
            if (first == deadline)
            {
                ct.ThrowIfCancellationRequested();
                return await WaitForFinalFeedLateResponseAsync(
                    feedTask, mdnsQueryTask, timeoutMessage, attempt, ct);
            }

            // TEZI 6.6, ilk feed taramasini servis ilani gelmeden tamamlayabiliyor.
            // mDNS sorgusundan hemen sonra VNC penceresi acmadan belgelenmis 'r'
            // kisayolunu gondererek ag feed listesini yeniden okut.
            await Task.Delay(750, ct);
            try
            {
                await RunLoggedStageAsync(
                    "NETWORK_TEZI_VNC_REFRESH_" + attempt,
                    $"target={_cfg.UkbTargetIp}:5900; key=r",
                    () => TeziVncClient.SendRefreshKeyAsync(
                        _cfg.UkbTargetIp,
                        5900,
                        ct));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Logger.Checkpoint(
                    "NETWORK_TEZI_VNC_REFRESH_FALLBACK",
                    "CONTINUE",
                    $"attempt={attempt}; exception={ex.GetType().Name}; message={ex.Message}");
            }

            Task completed = await Task.WhenAny(feedTask, deadline);
            if (completed == feedTask)
            {
                linked.Cancel();
                return await feedTask;
            }

            ct.ThrowIfCancellationRequested();
            return await WaitForFinalFeedLateResponseAsync(
                feedTask, mdnsQueryTask, timeoutMessage, attempt, ct);
        }

        private static async Task<string> WaitForFinalFeedLateResponseAsync(
            Task<string> feedTask,
            Task mdnsQueryTask,
            string timeoutMessage,
            string attempt,
            CancellationToken ct,
            TimeSpan? graceOverride = null)
        {
            bool finalAttempt = string.Equals(attempt, "ATTEMPT_2", StringComparison.OrdinalIgnoreCase) ||
                attempt.IndexOf("METADATA_RECOVERY", StringComparison.OrdinalIgnoreCase) >= 0;
            bool targetIsQuerying = mdnsQueryTask != null && mdnsQueryTask.Status == TaskStatus.RanToCompletion;
            if (!finalAttempt || !targetIsQuerying)
                throw new TimeoutException(timeoutMessage);

            TimeSpan grace = graceOverride ?? TimeSpan.FromSeconds(FinalFeedLateResponseGraceSeconds);
            Logger.Checkpoint(
                "NETWORK_TEZI_FEED_LATE_RESPONSE_GRACE",
                "START",
                $"attempt={attempt}; graceMs={(long)grace.TotalMilliseconds}; reason=target-mdns-evidence-present");

            if (feedTask.IsCompleted)
                return await feedTask.ConfigureAwait(false);
            Task graceDeadline = Task.Delay(grace, ct);
            Task completed = await Task.WhenAny(feedTask, graceDeadline).ConfigureAwait(false);
            if (completed == feedTask || feedTask.IsCompleted)
            {
                string result = await feedTask.ConfigureAwait(false);
                Logger.Checkpoint(
                    "NETWORK_TEZI_FEED_LATE_RESPONSE_GRACE",
                    "SUCCESS",
                    $"attempt={attempt}; path={result}");
                return result;
            }

            // Zamanlayici ile HTTP callback ayni anda kosabilir. Hata kararindan once
            // callback continuation'ina son bir scheduler turu ver ve durumu atomik oku.
            await Task.Delay(1, ct).ConfigureAwait(false);
            if (feedTask.IsCompleted)
            {
                string result = await feedTask.ConfigureAwait(false);
                Logger.Checkpoint(
                    "NETWORK_TEZI_FEED_LATE_RESPONSE_GRACE",
                    "SUCCESS",
                    $"attempt={attempt}; path={result}; boundaryRaceRecovered=true");
                return result;
            }

            ct.ThrowIfCancellationRequested();
            Logger.Checkpoint(
                "NETWORK_TEZI_FEED_LATE_RESPONSE_GRACE",
                "FAILED",
                $"attempt={attempt}; graceMs={(long)grace.TotalMilliseconds}");
            throw new TimeoutException(timeoutMessage);
        }

        private async Task<string> WaitForTeziMetadataWithRefreshAsync(
            Task<string> metadataTask,
            CancellationToken ct)
        {
            int[] refreshAfterSeconds = { 12, 18, 25 };
            for (int attempt = 0; attempt < refreshAfterSeconds.Length; attempt++)
            {
                Task delay = Task.Delay(
                    TimeSpan.FromSeconds(refreshAfterSeconds[attempt]), ct);
                Task completed = await Task.WhenAny(metadataTask, delay);
                if (completed == metadataTask)
                    return await metadataTask;

                ct.ThrowIfCancellationRequested();
                Logger.Checkpoint(
                    "NETWORK_TEZI_METADATA_REFRESH",
                    "RETRY",
                    $"attempt={attempt + 1}/{refreshAfterSeconds.Length}; " +
                    $"waitedSec={refreshAfterSeconds[attempt]}; action=vnc-refresh-key-r");
                try
                {
                    await TeziVncClient.SendRefreshKeyAsync(
                        _cfg.UkbTargetIp,
                        5900,
                        ct);
                    Logger.Checkpoint(
                        "NETWORK_TEZI_METADATA_REFRESH",
                        "SUCCESS",
                        $"attempt={attempt + 1}/{refreshAfterSeconds.Length}; key=r");
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                {
                    Logger.Checkpoint(
                        "NETWORK_TEZI_METADATA_REFRESH",
                        "CONTINUE",
                        $"attempt={attempt + 1}/{refreshAfterSeconds.Length}; " +
                        $"exception={ex.GetType().Name}; message={ex.Message}");
                }
            }

            if (metadataTask.IsCompleted)
                return await metadataTask;

            throw new TimeoutException(
                "Easy Installer image_list.json dosyasını aldı ancak mutlak ve oturuma özel " +
                "image.json adresini kontrollü yeniden taramalara rağmen istemedi. " +
                "HTTP bağlantısı çalışıyor; hedef Easy Installer oturumu paket listesini işlemedi.");
        }

        private async Task ValidateTeziHttpPublicationAsync(CancellationToken ct)
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            string feedUrl = _teziHttpServer.BaseUrl + "/image_list.json";
            string feedText = await client.GetStringAsync(feedUrl, ct);
            using JsonDocument feed = JsonDocument.Parse(feedText);
            JsonElement images = feed.RootElement.GetProperty("images");
            if (feed.RootElement.GetProperty("config_format").GetInt32() != 1 ||
                images.ValueKind != JsonValueKind.Array || images.GetArrayLength() != 1)
                throw new InvalidOperationException("Yerel TEZI image_list.json biçimi geçersiz.");
            string metadataReference = images[0].GetString();
            if (!string.Equals(metadataReference, _teziHttpServer.ImageMetadataReference, StringComparison.Ordinal))
                throw new InvalidOperationException("Yerel TEZI metadata yolu resmî göreli biçimde değil: " + metadataReference);

            string metadataText = await client.GetStringAsync(_teziHttpServer.ImageMetadataUrl, ct);
            using JsonDocument metadata = JsonDocument.Parse(metadataText);
            JsonElement root = metadata.RootElement;
            if (!root.TryGetProperty("autoinstall", out JsonElement autoInstall) || autoInstall.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("HTTP üzerinden sunulan image.json autoinstall=true değil.");
            if (root.TryGetProperty("license", out _) || root.TryGetProperty("license_title", out _))
                throw new InvalidOperationException("HTTP üzerinden sunulan image.json etkileşimli lisans alanı içeriyor.");
            Logger.Checkpoint("TEZI_HTTP_PUBLICATION_SELF_TEST", "SUCCESS",
                $"feed={feedUrl}; metadata={metadataReference}; autoinstall=true; interactiveLicense=false");
        }

        private async Task RunNetworkConnectivityTestAsync(bool whichUkb)
        {
            if (!_networkFeedReady || string.IsNullOrWhiteSpace(_networkStagingPath) || !Directory.Exists(_networkStagingPath))
            {
                Logger.Checkpoint(
                    "NETWORK_INSTALL_INITIALIZE",
                    "FAILED",
                    $"feedReady={_networkFeedReady}; staging={_networkStagingPath ?? "not-set"}; stagingExists={Directory.Exists(_networkStagingPath ?? "")}");
                throw new InvalidOperationException("Ağ paketi hazır değil.");
            }

            Logger.Checkpoint(
                "NETWORK_INSTALL_INITIALIZE",
                "SUCCESS",
                $"staging={_networkStagingPath}; expectedOfp={_networkExpectedOfpVersion}; project={_projectName}");

            _cts = new CancellationTokenSource();
            _preservePowerAfterSuccessfulInstall = false;
            string ukbLabel = "UKB" + _cfg.GetTargetNumber(whichUkb);
            UKB1Yak.Enabled = false;
            btnCancel.Enabled = true;
            bool recoveryEnabled = false;
            bool powerOn = false;
            bool installationStarted = false;
            bool shutdownObserved = false;
            bool installationSuccessful = false;
            bool retainForManualRecovery = false;
            bool serialShellAvailable = false;
            string observedOfpVersion = null;
            string observedNetworkUpLine = null;
            string activeServerIp = null;
            int activeServerPort = 0;
            TaskCompletionSource<bool> mdnsQuerySeen = null;
            HashSet<string> interfacesBeforeEasyInstaller = null;
            string currentStage = "NETWORK_INSTALL_INITIALIZE";
            var progress = new Progress<FlashProgress>(p =>
            {
                lblStatus.Text = p.Status;
            });
            try
            {
                currentStage = "NETWORK_HOST_PREFLIGHT";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}",
                    () =>
                    {
                        HardwareAutoConfigurator.ValidateHostPreflight(_cfg, whichUkb);
                        return Task.CompletedTask;
                    });
                SetNetworkStage(2, "Donanım ve Güvenli Çıkışlar Kontrol Ediliyor");
                _workflow.EnsureRecoveryTools();
                Logger.Checkpoint("NETWORK_INSTALL", "START", $"ukb={ukbLabel}; source=" + _networkStagingPath);
                Logger.Checkpoint("NETWORK_RECOVERY_TEST", "START", $"ukb={ukbLabel}");
                lblStatus.Text = "UKB güç durumu kontrol ediliyor...";
                currentStage = "NETWORK_PREFLIGHT_POWER_OFF";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; expected=OFF",
                    async () =>
                    {
                        uint initialPower = await _moxa.ReadPowerAsync(whichUkb, _cts.Token);
                        if (initialPower != 0)
                        {
                            Logger.Checkpoint(
                                "NETWORK_PREFLIGHT_POWER_AUTOCORRECT",
                                "START",
                                $"ukb={ukbLabel}; observed=ON; requested=OFF");
                            await SetPowerAndVerifyAsync(
                                whichUkb,
                                0,
                                _cts.Token,
                                "NETWORK_PREFLIGHT_POWER_AUTOCORRECT");
                            Logger.Checkpoint(
                                "NETWORK_PREFLIGHT_POWER_AUTOCORRECT",
                                "SUCCESS",
                                $"ukb={ukbLabel}; observed=OFF");
                        }
                    });

                Logger.Checkpoint(
                    "NETWORK_RECOVERY_POWER_OFF_DWELL",
                    "START",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryPowerOffDwellMilliseconds}");
                await Task.Delay(FlashWorkflow.RecoveryPowerOffDwellMilliseconds, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_RECOVERY_POWER_OFF_DWELL",
                    "SUCCESS",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryPowerOffDwellMilliseconds}");

                lblStatus.Text = "Recovery başlangıç durumu NORMAL yapılıyor...";
                currentStage = "NETWORK_PREFLIGHT_RECOVERY_NORMALIZE";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=NORMAL",
                    () => SetRecoveryAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_PREFLIGHT_RECOVERY_NORMALIZE"));

                // Seçili UKB Power OFF ve Recovery NORMAL olduktan sonra başlangıç
                // listesini al. Diğer UKB'lerden açık kalan adaptörler böylece eski,
                // seçili UKB açıldığında etkinleşen adaptör ise yeni olarak ayrılır.
                Logger.Checkpoint(
                    "NETWORK_RECOVERY_NORMAL_SETUP",
                    "START",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryNormalSettleMilliseconds}");
                await Task.Delay(FlashWorkflow.RecoveryNormalSettleMilliseconds, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_RECOVERY_NORMAL_SETUP",
                    "SUCCESS",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryNormalSettleMilliseconds}");
                interfacesBeforeEasyInstaller = HardwareAutoConfigurator.GetNetworkInterfaceIds();
                Logger.Checkpoint(
                    "USB_NCM_BASELINE",
                    "CAPTURED",
                    $"ukb={ukbLabel}; activeCount={interfacesBeforeEasyInstaller.Count}; " +
                    "policy=after-selected-power-off-before-recovery-power-on");
                long recoveryMark = _serial.Mark();
                SetNetworkStage(3, "Recovery Modu ve UKB Gücü Hazırlanıyor");
                lblStatus.Text = "Recovery modu REAL yapılıyor...";
                currentStage = "NETWORK_RECOVERY_REAL";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=REAL",
                    () => SetRecoveryAndVerifyAsync(whichUkb, 1, _cts.Token, "NETWORK_RECOVERY_REAL"));
                recoveryEnabled = true;

                Logger.Checkpoint(
                    "NETWORK_RECOVERY_REAL_SETUP",
                    "START",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryRealSetupMilliseconds}");
                await Task.Delay(FlashWorkflow.RecoveryRealSetupMilliseconds, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_RECOVERY_REAL_SETUP",
                    "SUCCESS",
                    $"ukb={ukbLabel}; durationMs={FlashWorkflow.RecoveryRealSetupMilliseconds}");

                lblStatus.Text = "UKB gücü açılıyor...";
                currentStage = "NETWORK_POWER_ON_RECOVERY";
                powerOn = true;
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=ON; startupOrder=power-on-and-uuu-overlap",
                    () => SetPowerAndVerifyAsync(
                        whichUkb,
                        1,
                        _cts.Token,
                        "NETWORK_POWER_ON_RECOVERY"));

                _criticalPhase = false;
                btnCancel.Enabled = true;

                SetNetworkStage(4, "Easy Installer USB Üzerinden Yükleniyor");
                lblStatus.Text = "Toradex Easy Installer USB üzerinden yükleniyor; OTG aygıtı bekleniyor...";
                currentStage = "NETWORK_EASY_INSTALLER_LOAD";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; startupOrder=power-on-and-uuu-overlap",
                    () => _workflow.LoadEasyInstallerAsync(
                        progress,
                        ukbLabel,
                        whichUkb,
                        _cts.Token));

                SetNetworkStage(5, "USB-NCM Sanal Ağ Bağlantısı Oluşturuluyor");
                lblStatus.Text = "Seçili UKB USB-NCM adaptörü hazırlanıyor...";
                currentStage = "NETWORK_USB_NCM_CONFIGURE";
                int selectedTargetNumber = _cfg.GetTargetNumber(whichUkb);
                activeServerIp = await RunLoggedStageAsync(
                    currentStage,
                    $"target={_cfg.UkbTargetIp}; ukb={ukbLabel}; auto={_cfg.AutoDetectNetworkServerIp}",
                    () => HardwareAutoConfigurator.EnsureNetworkServerIpAsync(
                        _cfg,
                        interfacesBeforeEasyInstaller,
                        selectedTargetNumber,
                        TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds),
                        _cts.Token));

                lblStatus.Text = "Hedef yükleme ortamı doğrulanıyor...";
                currentStage = "NETWORK_USB_NCM_TARGET_REACHABLE";
                await RunLoggedStageAsync(
                    currentStage,
                    $"target={_cfg.UkbTargetIp}; timeoutSec={_cfg.RecoveryNetworkTimeoutSeconds}",
                    () => NetworkUtils.WaitUntilReachableAsync(
                        _cfg.UkbTargetIp,
                        _cfg.PingTimeoutMs,
                        TimeSpan.FromSeconds(_cfg.RecoveryNetworkTimeoutSeconds),
                        _cts.Token));

                SetNetworkStage(6, "Yerel Sürüm Sunucusu Başlatılıyor");
                lblStatus.Text = "Yerel sürüm sunucusu seçili USB-NCM adresinde başlatılıyor...";
                currentStage = "NETWORK_HTTP_SERVER_START";
                await RunLoggedStageAsync(
                    currentStage,
                    $"target={_cfg.UkbTargetIp}; server={activeServerIp}; configuredPort={_cfg.NetworkServerPort}; auto={_cfg.AutoDetectNetworkServerIp}",
                    async () =>
                    {
                        TeziHttpServer previousServer = _teziHttpServer;
                        _teziHttpServer = null;
                        await DisposeInBackgroundAsync(previousServer);
                        _teziHttpServer = TeziHttpServer.StartWithFallback(
                            activeServerIp,
                            _cfg.NetworkServerPort,
                            _networkStagingPath);
                        _teziHttpServer.RequestStarted += TeziHttpServer_RequestStarted;
                        _teziHttpServer.RequestCompleted += TeziHttpServer_RequestCompleted;
                        activeServerPort = _teziHttpServer.Port;
                        await ValidateTeziHttpPublicationAsync(_cts.Token);
                    });
                Logger.Checkpoint(
                    "USB_NCM_SERVER_ADDRESS",
                    "SUCCESS",
                    $"target={_cfg.UkbTargetIp}; server={activeServerIp}:{activeServerPort}; configuredPort={_cfg.NetworkServerPort}; auto={_cfg.AutoDetectNetworkServerIp}");

                // TEZI ag taramasini acilis sirasinda yaptigi icin Zeroconf servisini
                // diger tanilama adimlarini bekletmeden hemen duyur.
                _networkFeedRequest = NewNetworkRequestSource();
                _networkImageRequest = NewNetworkRequestSource();
                _networkPayloadRequest = NewNetworkRequestSource();
                TeziMdnsAdvertiser previousMdns = _teziMdnsAdvertiser;
                _teziMdnsAdvertiser = null;
                await DisposeInBackgroundAsync(previousMdns);
                mdnsQuerySeen = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var earlyMdnsQuerySeen = mdnsQuerySeen;
                currentStage = "NETWORK_MDNS_ADVERTISEMENT_START";
                await RunLoggedStageAsync(
                    currentStage,
                    $"service=_tezi._tcp.local; ip={activeServerIp}; port={activeServerPort}",
                    () =>
                    {
                        _teziMdnsAdvertiser = new TeziMdnsAdvertiser(
                            activeServerIp,
                            activeServerPort);
                        _teziMdnsAdvertiser.RelevantQueryReceived += () =>
                            earlyMdnsQuerySeen.TrySetResult(true);
                        _teziMdnsAdvertiser.Start();
                        return Task.CompletedTask;
                    });
                Logger.Checkpoint(
                    "TEZI_ZEROCONF",
                    "STARTED",
                    $"service=_tezi._tcp.local; ip={activeServerIp}; port={activeServerPort}");

                SetNetworkStage(7, "Easy Installer Doğrulanıyor");
                lblStatus.Text = "Easy Installer seri konsol veya ağ duyurusu üzerinden doğrulanıyor...";
                currentStage = "NETWORK_EASY_INSTALLER_EVIDENCE";
                string easyInstallerEvidence;
                using (var serialEvidenceCancellation =
                       CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    Task<string> serialReadyTask = _serial.WaitForRecoveryReadyAsync(
                        recoveryMark,
                        serialEvidenceCancellation.Token);
                    easyInstallerEvidence = await RunLoggedStageAsync(
                        currentStage,
                        $"port={_cfg.GetSerialPort(_cfg.SelectedUkb == 2)}; baud={_cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2)}; accepted=serial-or-mdns",
                        () => WaitForEasyInstallerEvidenceAsync(
                            serialReadyTask,
                            mdnsQuerySeen.Task,
                            TimeSpan.FromSeconds(EasyInstallerEvidenceTimeoutSeconds),
                            _cts.Token));
                    serialEvidenceCancellation.Cancel();
                    try
                    {
                        await serialReadyTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // mDNS kanıtı önce geldiyse seri bekleyicisini sessizce sonlandır.
                    }
                }

                bool easyInstallerVerifiedByMdns =
                    easyInstallerEvidence.StartsWith("MDNS:", StringComparison.Ordinal);
                bool selectedSerialProducedData = _serial.HasReceivedDataSince(recoveryMark);
                if (selectedSerialProducedData)
                    serialShellAvailable = await ProbeInteractiveShellWithRetryAsync(_cts.Token);

                if (!serialShellAvailable && !mdnsQuerySeen.Task.IsCompleted)
                {
                    currentStage = "NETWORK_EASY_INSTALLER_MDNS_READY";
                    await RunLoggedStageAsync(
                        currentStage,
                        "service=_tezi._tcp.local; timeoutSec=20; reason=serial-shell-unavailable",
                        async () =>
                        {
                            Task deadline = Task.Delay(TimeSpan.FromSeconds(20), _cts.Token);
                            Task completed = await Task.WhenAny(mdnsQuerySeen.Task, deadline);
                            if (completed != mdnsQuerySeen.Task)
                            {
                                _cts.Token.ThrowIfCancellationRequested();
                                throw new TimeoutException(
                                    "Easy Installer açıldı ancak 20 saniye içinde _tezi._tcp Zeroconf sorgusu göndermedi. " +
                                    "USB-NCM adaptörünü, Windows güvenlik duvarındaki UDP 5353 iznini ve seçili UKB ağ eşlemesini kontrol edin.");
                            }
                            await mdnsQuerySeen.Task;
                        });
                    easyInstallerVerifiedByMdns = true;
                }
                // The mDNS query can arrive while the three-second shell probe is running.
                // Re-read the task state so that a valid Zeroconf signal is never lost.
                easyInstallerVerifiedByMdns =
                    easyInstallerVerifiedByMdns || mdnsQuerySeen.Task.IsCompleted;
                Logger.Checkpoint(
                    "NETWORK_EASY_INSTALLER_READY",
                    "SUCCESS",
                    "method=" + (serialShellAvailable ? "serial-shell" : "official-zeroconf") +
                    "; bootEvidence=" + easyInstallerEvidence +
                    "; mdnsQuery=" + mdnsQuerySeen.Task.IsCompleted +
                    "; serialShell=" + serialShellAvailable);
                Logger.Checkpoint(
                    "SERIAL_PORT_GUARD",
                    serialShellAvailable ? "READY" : "NETWORK_ONLY",
                    $"selected={_serial.PortName}; dataReceived={selectedSerialProducedData}; " +
                    "shell=" + serialShellAvailable + "; action=" +
                    (serialShellAvailable ? "serial-cli-fast-path-enabled" : "serial-cli-disabled; official-zeroconf-flow"));
                Logger.Info("Ağ testi için Easy Installer hazır: " + easyInstallerEvidence);

                SetNetworkStage(8, "UKB ile PC Ağ Erişimi Test Ediliyor");
                if (serialShellAvailable)
                {
                    lblStatus.Text = "Easy Installer → PC için erişilebilir HTTP portu otomatik belirleniyor...";
                    currentStage = "NETWORK_REVERSE_HTTP_PORT_SELECTION";
                    activeServerPort = await RunLoggedStageAsync(
                        currentStage,
                        $"server={activeServerIp}; candidates=current,8088,8000,8888,dynamic",
                        () => SelectReachableHttpPortAsync(
                            activeServerIp,
                            mdnsQuerySeen,
                            _cts.Token));
                    string healthUrl = _teziHttpServer.BaseUrl + "/health";
                    Logger.Checkpoint(
                        "NETWORK_REVERSE_CONNECTIVITY",
                        "SUCCESS",
                        $"method=target-http-request; url={healthUrl}; selectedPort={activeServerPort}");
                    Logger.Info($"Easy Installer → PC USB-NCM HTTP erişimi doğrulandı (port {activeServerPort}).");
                }
                else if (easyInstallerVerifiedByMdns)
                {
                    lblStatus.Text = "Easy Installer → PC ağ yolu doğrulandı; paket isteği bekleniyor...";
                    currentStage = "NETWORK_REVERSE_MDNS_CONNECTIVITY";
                    Logger.Checkpoint(
                        currentStage,
                        "SUCCESS",
                        $"method=mdns-query; target={_cfg.UkbTargetIp}; server={activeServerIp}:{activeServerPort}");
                    Logger.Checkpoint(
                        "NETWORK_REVERSE_CONNECTIVITY",
                        "SUCCESS",
                        "method=mdns-query; HTTP aktarımı image_list.json isteğinde doğrulanacak");
                    Logger.Info("Easy Installer → PC USB-NCM ağ yolu mDNS sorgusuyla doğrulandı.");
                }
                else
                {
                    throw new InvalidOperationException(
                        "Easy Installer doğrulandı ancak seçili seri porttan veri alınamadığı için " +
                        "UKB → PC HTTP port testi güvenli biçimde başlatılamadı. COM portunu kontrol edin.");
                }
                Logger.Checkpoint("NETWORK_RECOVERY_TEST", "SUCCESS", $"ukb={ukbLabel}");

                lblStatus.Text = "Testler başarılı; TEZI ağına sürüm duyuruluyor...";

                SetNetworkStage(9, "Sürüm Paketi Easy Installer'a Tanıtılıyor");
                lblStatus.Text = "Easy Installer'ın sürüm listesini istemesi bekleniyor...";
                currentStage = "NETWORK_TEZI_FEED_REQUEST_ATTEMPT_1";
                string feedRequest = null;
                if (serialShellAvailable)
                {
                    try
                    {
                        string directFeedUrl = _teziHttpServer.BaseUrl + "/image_list.json";
                        currentStage = "NETWORK_TEZICTL_FEED_ADD_FAST_PATH";
                        await RunLoggedStageAsync(
                            currentStage,
                            "url=" + directFeedUrl,
                            () => _serial.AddTeziFeedAsync(directFeedUrl, _cts.Token));
                        currentStage = "NETWORK_TEZICTL_FEED_REQUEST_FAST_PATH";
                        feedRequest = await RunLoggedStageAsync(
                            currentStage,
                            "expectedPath=/image_list.json; timeoutSec=15",
                            () => WaitForNetworkStageAsync(
                                _networkFeedRequest.Task,
                                TimeSpan.FromSeconds(15),
                                "TEZI CLI hızlı feed-add sonrasında image_list.json istemedi.",
                                _cts.Token));
                        Logger.Checkpoint(
                            "NETWORK_TEZI_FEED_FAST_PATH",
                            "SUCCESS",
                            "method=tezictl-feed-add; avoidedMdnsWaitSeconds=60");
                    }
                    catch (Exception fastPathEx) when (!(fastPathEx is OperationCanceledException && _cts.IsCancellationRequested))
                    {
                        Logger.Checkpoint(
                            "NETWORK_TEZI_FEED_FAST_PATH",
                            "FALLBACK",
                            $"exception={fastPathEx.GetType().Name}; message={fastPathEx.Message}");
                    }
                }

                if (feedRequest == null)
                {
                    try
                    {
                        feedRequest = await RunLoggedStageAsync(
                            currentStage,
                            $"expectedPath=/image_list.json; timeoutSec={(serialShellAvailable ? 30 : NetworkOnlyFeedDiscoveryTimeoutSeconds)}; method=mdns-vnc-refresh",
                            () => WaitForTeziFeedWithRefreshAsync(
                                _networkFeedRequest.Task,
                                mdnsQuerySeen.Task,
                                TimeSpan.FromSeconds(
                                    serialShellAvailable ? 30 : NetworkOnlyFeedDiscoveryTimeoutSeconds),
                                "Easy Installer CLI ve mDNS/VNC denemelerinden sonra image_list.json istemedi.",
                                "FALLBACK",
                                _cts.Token));
                    }
                    catch (TimeoutException firstDiscoveryEx)
                    {
                        Logger.Checkpoint(
                            "NETWORK_TEZI_FEED_RECOVERY",
                            "RETRY",
                            $"method=restart-mdns-and-tezi; exception={firstDiscoveryEx.GetType().Name}; message={firstDiscoveryEx.Message}");
                        TeziMdnsAdvertiser retryPreviousMdns = _teziMdnsAdvertiser;
                        _teziMdnsAdvertiser = null;
                        await DisposeInBackgroundAsync(retryPreviousMdns);
                        mdnsQuerySeen = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        var retryMdnsQuerySeen = mdnsQuerySeen;
                        _teziMdnsAdvertiser = new TeziMdnsAdvertiser(
                            activeServerIp,
                            activeServerPort);
                        _teziMdnsAdvertiser.RelevantQueryReceived += () =>
                            retryMdnsQuerySeen.TrySetResult(true);
                        _teziMdnsAdvertiser.Start();

                        if (serialShellAvailable)
                        {
                            currentStage = "NETWORK_TEZI_UI_RESTART_FOR_RESCAN";
                            try
                            {
                                await RunLoggedStageAsync(
                                    currentStage,
                                    "reason=late_zeroconf_feed; payloadStarted=false",
                                    () => _serial.RestartTeziUiAsync(_cts.Token));
                            }
                            catch (Exception restartEx) when (!(restartEx is OperationCanceledException && _cts.IsCancellationRequested))
                            {
                                Logger.Checkpoint(
                                    "NETWORK_TEZI_UI_RESTART_FALLBACK",
                                    "CONTINUE",
                                    $"exception={restartEx.GetType().Name}; message={restartEx.Message}");
                            }
                        }
                        else
                        {
                            currentStage = "NETWORK_TEZI_PREANNOUNCED_RELOAD";
                            await RunLoggedStageAsync(
                                currentStage,
                                $"ukb={ukbLabel}; server={activeServerIp}; reason=serial-shell-unavailable-and-refresh-timeout",
                                () => ReloadEasyInstallerWithPublishedFeedAsync(
                                    whichUkb,
                                    ukbLabel,
                                    selectedTargetNumber,
                                    activeServerIp,
                                    _networkFeedRequest.Task,
                                    mdnsQuerySeen,
                                    progress,
                                    _cts.Token));
                            Logger.Checkpoint(
                                "NETWORK_TEZI_UI_RESTART_FOR_RESCAN",
                                "SUCCESS",
                                "reason=serial-shell-unavailable; action=full-easy-installer-reload-with-http-mdns-active");
                        }

                        if (feedRequest == null && _networkFeedRequest.Task.IsCompleted)
                            feedRequest = await _networkFeedRequest.Task;

                        if (feedRequest == null && !serialShellAvailable)
                            serialShellAvailable = await ProbeInteractiveShellWithRetryAsync(_cts.Token);

                        if (feedRequest == null && serialShellAvailable)
                        {
                            string recoveredFeedUrl = _teziHttpServer.BaseUrl + "/image_list.json";
                            currentStage = "NETWORK_TEZICTL_FEED_ADD_AFTER_RELOAD";
                            await RunLoggedStageAsync(
                                currentStage,
                                "url=" + recoveredFeedUrl,
                                () => _serial.AddTeziFeedAsync(recoveredFeedUrl, _cts.Token));
                            currentStage = "NETWORK_TEZICTL_FEED_REQUEST_AFTER_RELOAD";
                            feedRequest = await RunLoggedStageAsync(
                                currentStage,
                                "expectedPath=/image_list.json; timeoutSec=15",
                                () => WaitForNetworkStageAsync(
                                    _networkFeedRequest.Task,
                                    TimeSpan.FromSeconds(15),
                                    "Easy Installer yeniden yüklemesinden sonra TEZI CLI image_list.json istemedi.",
                                    _cts.Token));
                        }

                        if (feedRequest == null)
                        {
                            currentStage = "NETWORK_TEZI_FEED_REQUEST_ATTEMPT_2";
                            feedRequest = await RunLoggedStageAsync(
                                currentStage,
                                "expectedPath=/image_list.json; timeoutSec=30; attempt=2/2",
                                () => WaitForTeziFeedWithRefreshAsync(
                                    _networkFeedRequest.Task,
                                    mdnsQuerySeen.Task,
                                    TimeSpan.FromSeconds(30),
                                    "Easy Installer tüm otomatik feed yöntemlerinden sonra image_list.json istemedi. " +
                                    "Hedef TEZI teşhisi otomatik olarak loga alınacak.",
                                    "ATTEMPT_2",
                                    _cts.Token));
                        }
                        Logger.Checkpoint(
                            "NETWORK_TEZI_FEED_RECOVERY",
                            "SUCCESS",
                            "method=restart; attempt=2/2; serialShellAfterReload=" + serialShellAvailable);
                    }
                }
                Logger.Checkpoint("TEZI_FEED_DISCOVERY", "SUCCESS", "path=" + feedRequest);

                lblStatus.Text = "Easy Installer'ın paket bilgisini istemesi bekleniyor...";
                currentStage = "NETWORK_TEZI_METADATA_REQUEST";
                string imageRequest;
                try
                {
                    imageRequest = await RunLoggedStageAsync(
                        currentStage,
                        $"expectedPath={_teziHttpServer.ImageMetadataPath}; timeoutSec=55; recovery=vnc-refresh-3x; metadataUrl=relative-session-directory",
                        () => WaitForTeziMetadataWithRefreshAsync(_networkImageRequest.Task, _cts.Token));
                }
                catch (TimeoutException firstMetadataFailure)
                {
                    Logger.Checkpoint("NETWORK_TEZI_METADATA_RECOVERY", "RETRY",
                        $"method=single-preannounced-full-reload; reason={firstMetadataFailure.Message}");
                    _networkFeedRequest = NewNetworkRequestSource();
                    _networkImageRequest = NewNetworkRequestSource();
                    _networkPayloadRequest = NewNetworkRequestSource();
                    await ReloadEasyInstallerWithPublishedFeedAsync(
                        whichUkb, ukbLabel, selectedTargetNumber, activeServerIp,
                        _networkFeedRequest.Task, mdnsQuerySeen, progress, _cts.Token);
                    await RunLoggedStageAsync(
                        "NETWORK_TEZI_FEED_REQUEST_AFTER_METADATA_RECOVERY",
                        "expectedPath=/image_list.json; timeoutSec=30; method=single-full-reload",
                        () => WaitForTeziFeedWithRefreshAsync(
                            _networkFeedRequest.Task, mdnsQuerySeen.Task, TimeSpan.FromSeconds(30),
                            "Easy Installer metadata toparlanmasından sonra image_list.json istemedi.",
                            "METADATA_RECOVERY", _cts.Token));
                    imageRequest = await RunLoggedStageAsync(
                        "NETWORK_TEZI_METADATA_REQUEST_AFTER_RECOVERY",
                        $"expectedPath={_teziHttpServer.ImageMetadataPath}; timeoutSec=55; recovery=vnc-refresh-3x; retry=final",
                        () => WaitForTeziMetadataWithRefreshAsync(_networkImageRequest.Task, _cts.Token));
                    Logger.Checkpoint("NETWORK_TEZI_METADATA_RECOVERY", "SUCCESS",
                        "method=single-preannounced-full-reload");
                }
                Logger.Checkpoint("TEZI_IMAGE_METADATA", "SUCCESS", "path=" + imageRequest);

                SetNetworkStage(10, "Sürüm Dosyaları UKB'ye Aktarılıyor ve Kuruluyor");
                lblStatus.Text = "Testler tamam; Easy Installer sürüm yüklemesini başlatıyor...";
                currentStage = "NETWORK_TEZI_PAYLOAD_REQUEST";
                string payloadRequest;
                try
                {
                    payloadRequest = await RunLoggedStageAsync(
                        currentStage,
                        "expected=/package/<payload>; timeoutSec=20; method=autoinstall",
                        () => WaitForNetworkStageAsync(
                            _networkPayloadRequest.Task,
                            TimeSpan.FromSeconds(20),
                            "TEZI autoinstall 20 saniye içinde payload aktarımını başlatmadı.",
                            _cts.Token));
                }
                catch (TimeoutException)
                {
                    if (!serialShellAvailable)
                    {
                        throw new TimeoutException(
                            "Easy Installer paketi ve image.json dosyasını aldı ancak autoinstall başlamadı. " +
                            "Bu Easy Installer oturumunda etkileşimli seri kabuk bulunmadığı için desteklenmeyen CLI komutu gönderilmedi. " +
                            "Paketin autoinstall=true olduğunu ve lisans alanının ağ staging kopyasında kaldırıldığını kontrol edin.");
                    }
                    currentStage = "NETWORK_TEZICTL_IMAGE_INSTALL";
                    await RunLoggedStageAsync(
                        currentStage,
                        "packageBaseUrl=" + _teziHttpServer.BaseUrl + "/package",
                        () => _serial.StartTeziInstallFromCliAsync(
                            _teziHttpServer.BaseUrl + "/package",
                            _cts.Token));
                    currentStage = "NETWORK_TEZI_PAYLOAD_REQUEST_AFTER_CLI";
                    payloadRequest = await RunLoggedStageAsync(
                        currentStage,
                        "expected=/package/<payload>; timeoutSec=180; method=tezictl",
                        () => WaitForNetworkStageAsync(
                            _networkPayloadRequest.Task,
                            TimeSpan.FromSeconds(180),
                            "TEZI CLI kurulumu başlatıldı ancak 180 saniye içinde sürüm dosyası istenmedi.",
                            _cts.Token));
                }
                installationStarted = true;
                _criticalPhase = true;
                btnCancel.Enabled = true;
                Logger.Checkpoint("NETWORK_INSTALL_PAYLOAD", "CONFIRMED", "path=" + payloadRequest);

                lblStatus.Text = "Sürüm aktarılıyor; UKB kapanış doğrulaması bekleniyor...";
                currentStage = "NETWORK_INSTALL_SHUTDOWN_SERIAL";
                string shutdownLine = await RunLoggedStageAsync(
                    currentStage,
                    $"timeoutSec={_cfg.InstallationTimeoutSeconds}",
                    () => _serial.WaitForShutdownAsync(recoveryMark, _cts.Token));
                shutdownObserved = true;
                _criticalPhase = false;
                btnCancel.Enabled = true;
                Logger.Info("Ağdan kurulum kapanış seri doğrulaması: " + shutdownLine);
                Logger.Checkpoint("NETWORK_INSTALL_SHUTDOWN", "SUCCESS", "line=" + shutdownLine);

                SetNetworkStage(11, "Recovery NORMAL Yapılıyor ve UKB Normal Açılıyor");
                // Seri konsoldaki "reboot: Power down" kurulumu ve hedef kapanışını
                // doğrudan doğrular. Recovery REAL iken USB-NCM adresinin ping ile
                // kaybolmasını beklemek güç döngüsünü kilitleyebilir. Bu nedenle
                // güvenli fiziksel sıra gecikmeden uygulanır:
                // Power OFF -> Recovery NORMAL -> Power ON.
                Logger.Checkpoint(
                    "NETWORK_POST_INSTALL_SEQUENCE",
                    "START",
                    "shutdownEvidence=serial; sequence=PowerOFF-RecoveryNORMAL-PowerON; usbPingWait=skipped");
                await DisposeInBackgroundAsync(_teziMdnsAdvertiser);
                _teziMdnsAdvertiser = null;

                lblStatus.Text = "Moxa bağlantıları yenileniyor...";
                currentStage = "NETWORK_MOXA_RECONNECT_AFTER_INSTALL";
                await RunLoggedStageAsync(
                    currentStage,
                    $"relay={_cfg.RelayBoxIp}; power={_cfg.PowerBoxIp}",
                    async () =>
                    {
                        if (!await _moxa.ConnectAsync())
                            throw new InvalidOperationException(
                                "Kurulum kapanışı görüldü ancak Moxa bağlantıları yeniden kurulamadı. " +
                                "Power/Relay Box Ethernet bağlantısını kontrol edin.");
                    });

                lblStatus.Text = "Kurulum tamamlandı; UKB gücü kapatılıyor...";
                currentStage = "NETWORK_POWER_OFF_AFTER_INSTALL";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=OFF",
                    () => SetPowerAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_POWER_OFF_AFTER_INSTALL"));
                powerOn = false;
                // Moxa geri okumasinin OFF olmasi UKB giris kapasitelerinin tamamen
                // bosaldigini garanti etmez. Ozellikle arka arkaya yuklemelerde kisa
                // kesinti hedefin yeniden acilmamasina yol acabiliyor.
                await Task.Delay(2000, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_POWER_OFF_SETTLE",
                    "SUCCESS",
                    "delayMs=2000; Power=OFF geri okundu; target discharge wait completed");

                lblStatus.Text = "Recovery modu NORMAL yapılıyor...";
                currentStage = "NETWORK_RECOVERY_NORMAL";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=NORMAL",
                    () => SetRecoveryAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_RECOVERY_NORMAL"));
                recoveryEnabled = false;
                await Task.Delay(1500, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_RECOVERY_NORMAL_SETTLE",
                    "SUCCESS",
                    "delayMs=1500; Recovery=NORMAL geri okundu");

                long normalBootMark = _serial.Mark();
                lblStatus.Text = "UKB normal açılışta sürüm doğrulaması için çalıştırılıyor...";
                currentStage = "NETWORK_POWER_ON_NORMAL_VERIFY";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=ON",
                    () => SetPowerAndVerifyAsync(
                        whichUkb,
                        1,
                        _cts.Token,
                        "NETWORK_POWER_ON_NORMAL_VERIFY"));
                powerOn = true;
                Logger.Checkpoint(
                    "NETWORK_POST_INSTALL_SEQUENCE",
                    "SUCCESS",
                    "Power=ON; Recovery=NORMAL; normalBootVerification=started");
                SetNetworkStage(12, "OFP Sürümü ve Network Up Doğrulanıyor");
                // Seri port acik gorunse bile art arda guc dongulerinden sonra Windows
                // surucusu veri iletmeyebilir veya UKB gercekte yeniden baslamamis olabilir.
                // Bos yere 300 saniye beklemek yerine once aktiviteyi denetle; veri yoksa
                // portu yenile ve Recovery NORMAL durumunda tek kontrollu acilis tekrari yap.
                try
                {
                    await _serial.WaitForActivityAsync(
                        normalBootMark,
                        TimeSpan.FromSeconds(25),
                        _cts.Token);
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_SERIAL_ACTIVITY",
                        "SUCCESS",
                        $"ukb={ukbLabel}; attempt=1; port={_serial.PortName}");
                }
                catch (TimeoutException firstBootTimeout)
                {
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_SERIAL_ACTIVITY",
                        "RETRY",
                        $"ukb={ukbLabel}; attempt=1; reason=no-serial-data; message={firstBootTimeout.Message}");
                    Logger.Warn("Normal acilista seri veri gelmedi; seri port yenilenip UKB bir kez kontrollu yeniden baslatiliyor.");
                    lblStatus.Text = "Normal acilis seri baglantisi yenileniyor...";

                    _serial.Dispose();
                    await Task.Delay(300, _cts.Token);
                    _serial.Open();
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_SERIAL_REOPEN",
                        "SUCCESS",
                        $"ukb={ukbLabel}; port={_serial.PortName}");

                    await SetPowerAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_NORMAL_BOOT_RETRY_POWER_OFF");
                    powerOn = false;
                    await Task.Delay(2000, _cts.Token);
                    await SetRecoveryAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_NORMAL_BOOT_RETRY_RECOVERY_NORMAL");
                    recoveryEnabled = false;
                    await Task.Delay(1000, _cts.Token);

                    normalBootMark = _serial.Mark();
                    await SetPowerAndVerifyAsync(
                        whichUkb,
                        1,
                        _cts.Token,
                        "NETWORK_NORMAL_BOOT_RETRY_POWER_ON");
                    powerOn = true;
                    await _serial.WaitForActivityAsync(
                        normalBootMark,
                        TimeSpan.FromSeconds(30),
                        _cts.Token);
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_SERIAL_ACTIVITY",
                        "RECOVERED",
                        $"ukb={ukbLabel}; attempt=2; port={_serial.PortName}; sequence=serial-reopen-power-cycle");
                }

                currentStage = "NETWORK_OFP_VERSION_VERIFY";
                observedOfpVersion = await RunLoggedStageAsync(
                    currentStage,
                    $"expected={_networkExpectedOfpVersion}; timeoutSec={_cfg.SerialBootTimeoutSeconds}",
                    async () =>
                    {
                        string observed = await _serial.WaitForOfpVersionAsync(normalBootMark, _cts.Token);
                        if (!string.Equals(
                                observed,
                                _networkExpectedOfpVersion,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Kurulum sonrası OFP sürümü eşleşmedi. Beklenen: {_networkExpectedOfpVersion}, " +
                                $"okunan: {observed}.");
                        }
                        return observed;
                    });
                Logger.Checkpoint(
                    "NETWORK_OFP_VERIFY",
                    "SUCCESS",
                    $"expected={_networkExpectedOfpVersion}; observed={observedOfpVersion}");
                Logger.Info(
                    $"Normal açılış sürüm bilgisi: OFP Version {observedOfpVersion}");

                lblStatus.Text = $"OFP {observedOfpVersion} doğrulandı; Network Up bilgisi bekleniyor...";
                currentStage = "NETWORK_NORMAL_BOOT_NETWORK_UP";
                try
                {
                    observedNetworkUpLine = await RunLoggedStageAsync(
                        currentStage,
                        "timeoutSec=45",
                        () => _serial.WaitForNetworkUpAsync(
                            normalBootMark,
                            TimeSpan.FromSeconds(45),
                            _cts.Token));
                    Logger.Info("Normal açılış ağ bilgisi: " + observedNetworkUpLine);
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_NETWORK_UP",
                        "SUCCESS",
                        "line=" + observedNetworkUpLine);
                    lblStatus.Text = IsPhysicalEthernetLinkUp(observedNetworkUpLine)
                        ? "OFP doğrulandı — Fiziksel Ethernet: LINK UP"
                        : "OFP doğrulandı — Linux ağ sistemi hazır";
                }
                catch (TimeoutException ex)
                {
                    observedNetworkUpLine = null;
                    string ethernetFailure;
                    bool physicalFailureSeen = _serial.TryGetPhysicalEthernetFailureSince(
                        normalBootMark,
                        out ethernetFailure);
                    string linkDetails = physicalFailureSeen
                        ? " UKB eth0 physical error: " + ethernetFailure
                        : "";
                    Logger.Warn("Ağ hazır/Link Up satırı 45 saniye içinde görülmedi; OFP sürümü doğrulandığı için sonuç korunuyor.");
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_NETWORK_UP",
                        "WARNING",
                        ex.Message + linkDetails);
                }

                installationSuccessful = true;
                _preservePowerAfterSuccessfulInstall = true;
                currentStage = "NETWORK_FINAL_POWER_PRESERVED";
                Logger.Checkpoint(
                    currentStage,
                    "SUCCESS",
                    $"ukb={ukbLabel}; Power=ON; Recovery=NORMAL; reason=post-install-operational-state");
                CompleteNetworkProgress();
                Logger.Checkpoint(
                    "NETWORK_INSTALL",
                    "SUCCESS",
                    $"expectedOfp={_networkExpectedOfpVersion}; observedOfp={observedOfpVersion}; networkUp={observedNetworkUpLine}");
                // OFP ve fiziksel Link Up doğrulandıktan sonra UKB normal çalışmasına
                // devam eder. Seri portu burada kapat; aksi halde hedefin sürekli
                // konsol çıktısı hem arayüzü hem de oturum logunu gereksiz yere doldurur.
                _serial.Dispose();
                Logger.Checkpoint(
                    "NETWORK_SERIAL_MONITOR_AFTER_SUCCESS",
                    "STOPPED",
                    $"ukb={ukbLabel}; reason=final-verification-complete; port-reopens-on-next-install");
                string networkResult = FormatNetworkVerificationResult(observedNetworkUpLine);
                lblStatus.Text =
                    "SÜRÜM YÜKLENDİ VE DOĞRULANDI — " + networkResult +
                    "; UKS POWER açık, Recovery NORMAL.";
                UKB1Yak.Text = "SÜRÜM YÜKLEME TAMAMLANDI";
                ShowInstallResultDialog(
                    "Bağlantı testleri geçti, sürüm ağ üzerinden yüklendi ve normal açılıştaki OFP sürümü doğrulandı.\n\n" +
                    $"OFP Version: {observedOfpVersion}\n" +
                    $"Ağ durumu: {networkResult}\n\n" +
                    "UKS POWER açık ve Recovery NORMAL durumda bırakıldı.",
                    "Sürüm Yükleme Başarılı",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                SetNetworkProgressStopped("İptal edildi");
                bool payloadObserved = installationStarted ||
                    (_networkPayloadRequest?.Task.IsCompleted ?? false);
                Logger.Checkpoint(
                    "NETWORK_INSTALL",
                    "CANCELLED",
                    $"stage={currentStage}; payloadStarted={payloadObserved}; shutdown={shutdownObserved}; powerOn={powerOn}; recovery={recoveryEnabled}");
                retainForManualRecovery = false;
                _manualRecoveryRequired = false;
                _criticalPhase = false;
                lblStatus.Text = "Sürüm yükleme kullanıcı tarafından durduruldu; Power OFF ve Recovery NORMAL uygulanıyor.";
                UKB1Yak.Text = $"{ukbLabel} — SÜRÜM YÜKLEMEYİ TEKRAR BAŞLAT";
                Logger.Checkpoint(
                    "NETWORK_INSTALL_CANCEL_SAFE_ROLLBACK",
                    "START",
                    $"ukb={ukbLabel}; payloadStarted={payloadObserved}; forceStopAuthorized=true");
            }
            catch (Exception ex)
            {
                SetNetworkProgressStopped("Hata oluştu");
                bool payloadObserved = installationStarted ||
                    (_networkPayloadRequest?.Task.IsCompleted ?? false);

                // Paket aktarimi baslamadan onceki Easy Installer kesif hatalarinda,
                // hedefteki TEZI gunlugunu seri porttan otomatik olarak oturum loguna al.
                // Bu salt-okunur teshis, guvenli geri alma islemlerinden once yapilmalidir.
                if (!payloadObserved && powerOn && _serial.IsOpen && serialShellAvailable)
                {
                    try
                    {
                        await _serial.CaptureTeziDiagnosticsAsync(CancellationToken.None);
                    }
                    catch (Exception diagnosticEx)
                    {
                        Logger.Diagnostic("Easy Installer hedef teshisi alinamadi", diagnosticEx);
                        Logger.Checkpoint(
                            "TEZI_TARGET_DIAGNOSTICS",
                            "FAILED",
                            $"exception={diagnosticEx.GetType().Name}; message={diagnosticEx.Message}");
                    }
                }

                Logger.Error("Ağ testi/yükleme akışı başarısız", ex);
                Logger.Checkpoint(
                    "NETWORK_FAILURE_CONTEXT",
                    "CAPTURED",
                    $"stage={currentStage}; target={_cfg.UkbTargetIp}; server={activeServerIp ?? "not-resolved"}; " +
                    $"httpPort={activeServerPort}; configuredHttpPort={_cfg.NetworkServerPort}; serial={_cfg.GetSerialPort(_cfg.SelectedUkb == 2)}/{_cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2)}; " +
                    $"relayIp={_cfg.RelayBoxIp}; powerIp={_cfg.PowerBoxIp}; " +
                    $"payloadStarted={payloadObserved}; shutdown={shutdownObserved}; powerOn={powerOn}; recovery={recoveryEnabled}; " +
                    $"exception={ex.GetType().Name}; message={ex.Message}");
                Logger.Checkpoint(
                    "NETWORK_INSTALL",
                    "FAILED",
                    $"stage={currentStage}; exception={ex.GetType().Name}; message={ex.Message}; payloadStarted={payloadObserved}; " +
                    $"shutdown={shutdownObserved}; powerOn={powerOn}; recovery={recoveryEnabled}");

                string expertDiagnosticPath = null;
                try
                {
                    lblStatus.Text = "Hata oluştu; uzman tanı paketi hazırlanıyor...";
                    expertDiagnosticPath = await ExpertDiagnostics.CaptureFailureAsync(
                        new ExpertDiagnosticContext
                        {
                            Stage = currentStage,
                            Ukb = ukbLabel,
                            TargetIp = _cfg.UkbTargetIp,
                            ServerIp = activeServerIp ?? "not-resolved",
                            HttpPort = activeServerPort,
                            SerialPort = _cfg.GetSerialPort(_cfg.SelectedUkb == 2),
                            BaudRate = _cfg.GetSerialBaudRate(_cfg.SelectedUkb == 2),
                            RelayBoxIp = _cfg.RelayBoxIp,
                            PowerBoxIp = _cfg.PowerBoxIp,
                            PayloadStarted = payloadObserved,
                            ShutdownObserved = shutdownObserved,
                            PowerOn = powerOn,
                            RecoveryEnabled = recoveryEnabled
                        },
                        ex,
                        CancellationToken.None);
                    Logger.Checkpoint(
                        "EXPERT_DIAGNOSTIC_BUNDLE",
                        "SUCCESS",
                        "path=" + expertDiagnosticPath);
                }
                catch (Exception diagnosticEx)
                {
                    Logger.Diagnostic("Uzman tanı paketi oluşturulamadı", diagnosticEx);
                    Logger.Checkpoint(
                        "EXPERT_DIAGNOSTIC_BUNDLE",
                        "FAILED",
                        $"exception={diagnosticEx.GetType().Name}; message={diagnosticEx.Message}");
                }
                string diagnosticNote = string.IsNullOrWhiteSpace(expertDiagnosticPath)
                    ? "\n\nUzman tanı paketi oluşturulamadı; oturum logunu paylaşın."
                    : "\n\nUzman tanı dosyası:\n" + expertDiagnosticPath;

                if (payloadObserved && !shutdownObserved)
                {
                    retainForManualRecovery = true;
                    _manualRecoveryRequired = true;
                    _criticalPhase = false;
                    Logger.Checkpoint(
                        "NETWORK_INSTALL_MANUAL_RECOVERY",
                        "REQUIRED",
                        $"exception={ex.GetType().Name}; message={ex.Message}");
                    lblStatus.Text = "MANUEL KONTROL GEREKLİ — aktarım başladı; UKB gücünü kesmeyin.";
                    UKB1Yak.Text = "MANUEL KONTROL GEREKLİ";
                    MessageBox.Show(
                        "Sürüm dosyası aktarılmaya başladıktan sonra hata oluştu. Güvenlik için UKB gücü ve Recovery durumu değiştirilmedi. " +
                        "UKB gücünü kesmeyin; seri log ile cihaz durumunu kontrol edin.\n\n" +
                        "Başarısız aşama: " + currentStage + "\n" + ex.Message + diagnosticNote,
                        "Kritik Yükleme Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                else
                {
                    lblStatus.Text = "Test/yükleme başarısız; neden oturum loguna kaydedildi.";
                    UKB1Yak.Text = "SÜRÜM YÜKLEMEYİ TEKRAR BAŞLAT";
                    MessageBox.Show(
                        "Sürüm yükleme başlatılamadı. Başarısız aşama ve nedeni ayrıntılı oturum loguna kaydedildi.\n\n" +
                        "Başarısız aşama: " + currentStage + "\n" + ex.Message + diagnosticNote,
                        "Sürüm Yükleme Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                _criticalPhase = false;
                await StopNetworkServicesAsync();
                await CleanupManagedTargetRouteAsync("NETWORK_INSTALL_ROUTE_CLEANUP");

                if (!retainForManualRecovery && powerOn && !installationSuccessful)
                {
                    try
                    {
                        lblStatus.Text = "Hata/iptal sonrası UKB gücü güvenli biçimde kapatılıyor...";
                        await SetPowerAndVerifyAsync(
                            whichUkb,
                            0,
                            CancellationToken.None,
                            "NETWORK_INSTALL_CLEANUP_POWER");
                        powerOn = false;
                        Logger.Checkpoint("NETWORK_INSTALL_CLEANUP_POWER", "OK");
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Error("Ağdan yükleme sonrası UKB gücü kapatılamadı", cleanupEx);
                        Logger.Checkpoint(
                            "NETWORK_INSTALL_CLEANUP_POWER",
                            "FAILED",
                            $"exception={cleanupEx.GetType().Name}; message={cleanupEx.Message}");
                        lblStatus.Text = "UYARI: UKB güç durumu doğrulanamadı; logu inceleyin.";
                    }
                }
                if (!retainForManualRecovery && recoveryEnabled && !powerOn)
                {
                    try
                    {
                        lblStatus.Text = "Hata/iptal sonrası Recovery NORMAL yapılıyor...";
                        await SetRecoveryAndVerifyAsync(
                            whichUkb,
                            0,
                            CancellationToken.None,
                            "NETWORK_INSTALL_CLEANUP_RECOVERY");
                        recoveryEnabled = false;
                        Logger.Checkpoint("NETWORK_INSTALL_CLEANUP_RECOVERY", "OK");
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Error("Ağdan yükleme sonrası Recovery NORMAL yapılamadı", cleanupEx);
                        Logger.Checkpoint(
                            "NETWORK_INSTALL_CLEANUP_RECOVERY",
                            "FAILED",
                            $"exception={cleanupEx.GetType().Name}; message={cleanupEx.Message}");
                        lblStatus.Text = "UYARI: Recovery durumu doğrulanamadı; logu inceleyin.";
                    }
                }
                if (retainForManualRecovery)
                {
                    bool payloadObserved = installationStarted ||
                        (_networkPayloadRequest?.Task.IsCompleted ?? false);
                    Logger.Checkpoint(
                        "NETWORK_INSTALL_CLEANUP",
                        "SKIPPED_FOR_SAFETY",
                        $"powerOn={powerOn}; recovery={recoveryEnabled}; payloadStarted={payloadObserved}; shutdown={shutdownObserved}");
                }
                btnCancel.Enabled = false;
                _cts?.Dispose();
                _cts = null;
                if (installationSuccessful && powerOn && !recoveryEnabled)
                {
                    UnlockNetworkUiAfterSuccessfulRun();
                }
                else if (!retainForManualRecovery)
                {
                    ResetNetworkUiAfterStoppedRun();
                }
                else
                {
                    UKB1Yak.Enabled = false;
                }
                Logger.Checkpoint(
                    "NETWORK_INSTALL_SESSION_END",
                    installationSuccessful ? "SUCCESS" :
                        retainForManualRecovery ? "MANUAL_RECOVERY_REQUIRED" : "FAILED_OR_CANCELLED",
                    $"lastStage={currentStage}; powerOn={powerOn}; recovery={recoveryEnabled}; " +
                    $"payloadStarted={installationStarted}; shutdown={shutdownObserved}; manualRecovery={retainForManualRecovery}");
            }
        }

        private void ShowInstallResultDialog(
            string message,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon)
        {
            bool linkUp = message.IndexOf("LINK UP", StringComparison.OrdinalIgnoreCase) >= 0;
            using var dialog = new Form
            {
                Text = Localization.ForCurrentLanguage(caption),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(600, 285),
                BackColor = Color.White,
                Font = new Font("Segoe UI", 10F)
            };
            var body = new Label
            {
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(552, 150),
                Text = Localization.ForCurrentLanguage(message),
                ForeColor = ModernUi.TextPrimary
            };
            var linkBadge = new Label
            {
                AutoSize = false,
                Location = new Point(24, 180),
                Size = new Size(552, 42),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                Text = linkUp
                    ? "LINK IS UP"
                    : Localization.T("LINK IS UP GÖZLENMEDİ", "LINK IS UP NOT OBSERVED"),
                ForeColor = linkUp ? Color.FromArgb(22, 101, 52) : Color.FromArgb(185, 28, 28),
                BackColor = linkUp ? Color.FromArgb(220, 252, 231) : Color.FromArgb(254, 226, 226)
            };
            var ok = new Button
            {
                Text = Localization.T("Tamam", "OK"),
                DialogResult = DialogResult.OK,
                Size = new Size(105, 36),
                Location = new Point(471, 237)
            };
            ModernUi.StylePrimaryButton(ok);
            dialog.Controls.Add(body);
            dialog.Controls.Add(linkBadge);
            dialog.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.CancelButton = ok;
            dialog.ShowDialog(this);
        }

        private static bool IsPhysicalEthernetLinkUp(string line)
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.IndexOf("Link is Up", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (line.IndexOf("usb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("can", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return line.IndexOf("eth0:", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static string FormatNetworkVerificationResult(string line)
        {
            if (IsPhysicalEthernetLinkUp(line))
                return "Fiziksel Ethernet LINK UP";
            if (!string.IsNullOrWhiteSpace(line))
                return "Linux ağ sistemi hazır";
            return "Ağ hazır mesajı gözlenmedi (OFP doğrulandı)";
        }

        private void UnlockNetworkUiAfterSuccessfulRun()
        {
            _networkFeedReady = false;
            _networkFeedRequest = null;
            _networkImageRequest = null;
            _networkPayloadRequest = null;
            _networkHealthRequest = null;
            QueueNetworkStagingCleanup();

            button2.Enabled = true;
            surumList.Enabled = true;
            UpdateMainActionButtonTexts();
            SetButtonsEnabled(_moxaConnected && _safeOutputsReady);
            UpdateConnectionSummary();
            lblStatus.Text = "SÜRÜM AĞDAN YÜKLENDİ VE DOĞRULANDI — yeni işlem başlatılabilir.";
            Logger.Checkpoint(
                "NETWORK_UI_UNLOCK_AFTER_SUCCESS",
                "SUCCESS",
                "feedReady=false; settingsEnabled=true; selectionsEnabled=true; progress=completed");
        }

        private void ResetNetworkUiAfterStoppedRun()
        {
            _networkFeedReady = false;
            _networkFeedRequest = null;
            _networkImageRequest = null;
            _networkPayloadRequest = null;
            _networkHealthRequest = null;
            QueueNetworkStagingCleanup();

            _currentNetworkStage = 0;
            _currentNetworkStageName = "İşlem başlatılmadı";
            progressBar1.Value = progressBar1.Minimum;
            lblStageProgress.Text = _currentNetworkStageName;
            button2.Enabled = true;
            surumList.Enabled = true;
            UpdateMainActionButtonTexts();
            SetButtonsEnabled(_moxaConnected && _safeOutputsReady);
            UpdateConnectionSummary();
            lblStatus.Text = "İşlem durdu. Ayarlar ve seçimler yeniden açıldı; tekrar deneyebilirsiniz.";
            Logger.Checkpoint(
                "NETWORK_UI_RESET",
                "SUCCESS",
                "feedReady=false; progress=0; controls=initial-state");
        }

        private async Task StopNetworkServicesAsync()
        {
            TeziMdnsAdvertiser mdns = _teziMdnsAdvertiser;
            TeziHttpServer http = _teziHttpServer;
            _teziMdnsAdvertiser = null;
            _teziHttpServer = null;
            await Task.WhenAll(
                DisposeInBackgroundAsync(mdns),
                DisposeInBackgroundAsync(http));
        }

        private async Task CleanupManagedTargetRouteAsync(string checkpoint)
        {
            if (_cfg == null || string.IsNullOrWhiteSpace(_cfg.UkbTargetIp))
                return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await HardwareAutoConfigurator.CleanupManagedTargetRoutesAsync(
                    _cfg.UkbTargetIp,
                    timeout.Token);
                Logger.Checkpoint(checkpoint, "SUCCESS", "target=" + _cfg.UkbTargetIp);
            }
            catch (Exception ex)
            {
                Logger.Diagnostic("Geçici USB-NCM hedef rotası temizlenemedi.", ex);
                Logger.Checkpoint(
                    checkpoint,
                    "FAILED",
                    $"target={_cfg.UkbTargetIp}; exception={ex.GetType().Name}; message={ex.Message}");
            }
        }
        private static Task DisposeInBackgroundAsync(IDisposable resource)
        {
            if (resource == null)
                return Task.CompletedTask;
            return Task.Run(() =>
            {
                try { resource.Dispose(); }
                catch (Exception ex) { Logger.Diagnostic("Arka plan kaynak kapatma işlemi başarısız.", ex); }
            });
        }

        private Task CleanupNetworkStagingAsync()
        {
            string path = DetachNetworkStagingPath();
            return string.IsNullOrWhiteSpace(path)
                ? Task.CompletedTask
                : Task.Run(() => DeleteNetworkStagingPath(path));
        }

        private void QueueNetworkStagingCleanup()
        {
            string path = DetachNetworkStagingPath();
            if (!string.IsNullOrWhiteSpace(path))
                _ = Task.Run(() => DeleteNetworkStagingPath(path));
        }

        private string DetachNetworkStagingPath()
        {
            string path = _networkStagingPath;
            _networkStagingPath = null;
            return path;
        }

        private void CleanupNetworkStaging()
        {
            string path = DetachNetworkStagingPath();
            DeleteNetworkStagingPath(path);
        }

        private static void DeleteNetworkStagingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            string allowedRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SurumYakmaAgdan",
                "staging"));
            string resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(
                    allowedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn("Güvenli staging kökü dışında olduğu için klasör silinmedi: " + resolved);
                return;
            }

            try
            {
                Directory.Delete(resolved, true);
                Logger.Checkpoint("NETWORK_STAGING_CLEANUP", "OK", "path=" + resolved);
            }
            catch (Exception ex)
            {
                Logger.Diagnostic("Ağ staging klasörü temizlenemedi: " + resolved, ex);
            }
        }

        private async void UKB2Yak_Click(object sender, EventArgs e)
        {
            if (CompleteUkbMediaReady(whichUkb: true))
                return;
            await StartFlashAsync(whichUkb: true);
        }

        private bool CompleteUkbMediaReady(bool whichUkb)
        {
            if (_ukbMediaReadySource == null || _ukbMediaReadyForSecondUkb != whichUkb)
                return false;

            UKB1Yak.Enabled = false;
            UKB2Yak.Enabled = false;
            lblStatus.Text = "Flash bellek hazır bildirildi; otomatik yükleme başlatılıyor...";
            _ukbMediaReadySource.TrySetResult(true);
            return true;
        }

        private Task WaitForOtgCableConfirmationAsync(CancellationToken ct)
        {
            return ShowOtgConfirmationAsync(
                "OTG aygıtı algılanamadı. OTG kablosunu UKB tarafından çıkarın, en az 5 saniye bekleyin, yeniden takın ve Tamam'a basın.",
                "The OTG device was not detected. Disconnect the OTG cable from the UKB, wait at least 5 seconds, reconnect it, and press OK.",
                "OTG Kablosunu Yeniden Takın",
                "Reconnect the OTG Cable",
                ct);
        }

        private Task WaitForOtgDisconnectConfirmationAsync(CancellationToken ct)
        {
            return ShowOtgConfirmationAsync(
                "OTG aygıtı algılanamadı. UUU oturumu kapatıldı; UKB gücü OFF ve Recovery NORMAL durumundadır.\n\n" +
                "1. OTG kablosunu UKB tarafından tamamen çıkarın.\n" +
                "2. Kabloyu henüz geri TAKMAYIN.\n" +
                "3. En az 5 saniye bekleyin ve yalnızca kablo çıkarılmış durumdayken Tamam'a basın.\n\n" +
                "Uygulama UKB'yi OTG olmadan NORMAL açacak ve seri konsoldan doğrulayacaktır.",
                "The OTG device was not detected. The UUU session has been closed; UKB power is OFF and Recovery is NORMAL.\n\n" +
                "1. Completely disconnect the OTG cable from the UKB.\n" +
                "2. Do NOT reconnect the cable yet.\n" +
                "3. Wait at least 5 seconds and press OK only while the cable remains disconnected.\n\n" +
                "The application will boot the UKB normally without OTG and verify it through the serial console.",
                "OTG Kablosunu Çıkarın",
                "Disconnect the OTG Cable",
                ct);
        }

        private Task WaitForOtgReconnectConfirmationAsync(CancellationToken ct)
        {
            return ShowOtgConfirmationAsync(
                "UKB'nin OTG olmadan NORMAL açıldığı doğrulandı. UKB yeniden kapatıldı ve Recovery REAL hazırlandı.\n\n" +
                "1. OTG kablosunu doğrudan PC ile UKB arasına yeniden takın.\n" +
                "2. USB hub veya uzatma kullanmayın.\n" +
                "3. Kabloyu taktıktan sonra Tamam'a basın.\n\n" +
                "Uygulama Windows USB aygıtlarını yeniden tarayacak ve tamamen yeni bir UUU oturumu başlatacaktır.",
                "Normal UKB boot without OTG was verified. The UKB has been powered off again and Recovery REAL is prepared.\n\n" +
                "1. Reconnect the OTG cable directly between the PC and UKB.\n" +
                "2. Do not use a USB hub or extension.\n" +
                "3. Press OK after reconnecting the cable.\n\n" +
                "The application will rescan Windows USB devices and start a completely new UUU session.",
                "OTG Kablosunu Takın",
                "Connect the OTG Cable",
                ct);
        }

        private Task ShowOtgConfirmationAsync(
            string turkishMessage,
            string englishMessage,
            string turkishTitle,
            string englishTitle,
            CancellationToken ct)
        {
            var source = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void ShowConfirmation()
            {
                if (ct.IsCancellationRequested)
                {
                    source.TrySetCanceled(ct);
                    return;
                }
                MessageBox.Show(
                    this,
                    Localization.T(turkishMessage, englishMessage),
                    Localization.T(turkishTitle, englishTitle),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                source.TrySetResult(true);
            }

            if (InvokeRequired)
                BeginInvoke((Action)ShowConfirmation);
            else
                ShowConfirmation();
            ct.Register(() => source.TrySetCanceled(ct));
            return source.Task;
        }

        private async Task WaitForUkbMediaReadyAsync(string ukbLabel, CancellationToken ct)
        {
            if (InvokeRequired)
            {
                var outer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        await WaitForUkbMediaReadyAsync(ukbLabel, ct);
                        outer.TrySetResult(true);
                    }
                    catch (OperationCanceledException) { outer.TrySetCanceled(ct); }
                    catch (Exception ex) { outer.TrySetException(ex); }
                }));
                await outer.Task;
                return;
            }

            if (_ukbMediaReadySource != null)
                throw new InvalidOperationException("Flash bellek hazır bildirimi zaten bekleniyor.");

            _ukbMediaReadyForSecondUkb = ukbLabel.Equals("UKB2", StringComparison.OrdinalIgnoreCase);
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ukbMediaReadySource = source;
            Button targetButton = _ukbMediaReadyForSecondUkb ? UKB2Yak : UKB1Yak;
            targetButton.Text = "FLASH UKB'YE TAKILDI — DEVAM ET";
            targetButton.Enabled = true;
            lblStatus.Text =
                $"Flash belleği {ukbLabel} sürüm portuna ve OTG kablosunu UKB'ye takın; ardından büyük düğmeye basın.";
            Logger.Warn($"{ukbLabel}: flash belleğin UKB portuna takıldığına dair kullanıcı bildirimi bekleniyor.");

            using (ct.Register(() => source.TrySetCanceled(ct)))
            {
                try
                {
                    await source.Task;
                }
                finally
                {
                    if (ReferenceEquals(_ukbMediaReadySource, source))
                        _ukbMediaReadySource = null;
                }
            }
        }

        private async Task StartFlashAsync(bool whichUkb)
        {
            if (driveList.SelectedItem == null || surumList.SelectedItem == null)
            {
                MessageBox.Show("Lütfen disk ve sürüm seçin.", "Eksik Seçim", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selectedDriveRoot = driveList.Text.Substring(0, 2) + "\\";
            var selectedDrive = new DriveInfo(selectedDriveRoot);
            if (selectedDrive.DriveType != DriveType.Removable)
            {
                MessageBox.Show(
                    "Güvenlik nedeniyle sürüm yalnızca çıkarılabilir bir flash belleğe hazırlanabilir.",
                    "Geçersiz Hedef Disk",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            VersionChoice selectedVersion = surumList.SelectedItem as VersionChoice;
            if (selectedVersion == null)
            {
                MessageBox.Show(
                    "Yüklenebilir bir sürüm klasörü seçin.",
                    "Geçersiz Sürüm",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            if (!VersionManager.IsTeziPackage(selectedVersion.Path))
            {
                MessageBox.Show(
                    "Üretim yüklemesi yalnızca image.json, prepare.sh ve wrapup.sh içeren tam TEZI *build.0 paketiyle başlatılabilir. Ham OFP dosyası seçilemez.",
                    "Geçersiz Sürüm Paketi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var req = new FlashRequest
            {
                WhichUkb = whichUkb,
                DriveRoot = selectedDriveRoot,
                IsVersionFromPc = selectedVersion.IsFromPc,
                VersionPath = selectedVersion.Path
            };

            _cts = new CancellationTokenSource();
            var progress = new Progress<FlashProgress>(p =>
            {
                progressBar1.Value = Math.Max(
                    progressBar1.Minimum,
                    Math.Min(progressBar1.Maximum, p.Percent * NetworkStageCount));
                lblStatus.Text = p.Status;
                if (lblStageProgress != null)
                    lblStageProgress.Text = $"{p.Status}   •   %{Math.Max(0, Math.Min(100, p.Percent))}";
            });

            SetButtonsEnabled(false);
            button2.Enabled = false;
            btnCancel.Enabled = true;
            try
            {
                await _workflow.RunAsync(req, progress, _cts.Token);
                MessageBox.Show("Sürüm yakma işlemi tamamlandı.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "İşlem iptal edildi.";
                MessageBox.Show("İşlem iptal edildi.", "İptal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Hata: " + ex.Message;
                MessageBox.Show("İşlem hata ile durdu:\n\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnCancel.Enabled = false;
                button2.Enabled = true;
                UpdateMainActionButtonTexts();
                SetButtonsEnabled(_moxaConnected && !_criticalPhase && !_manualRecoveryRequired);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void lblHello_Click(object sender, EventArgs e) { }
        private void textBox1_TextChanged(object sender, EventArgs e) { }
        private void textBox3_TextChanged(object sender, EventArgs e) { }
    }
}
