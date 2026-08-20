using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SurumYakma
{
    public partial class Form1 : Form
    {
        private const string ApplicationDisplayName = "Sürüm Yükleme v-1.0.0";
        private const int NetworkStageCount = 12;
        private const int EasyInstallerEvidenceTimeoutSeconds = 45;
        private const string DefaultPlatformSuffix = " (Varsayılan)";

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
        private Panel pnlModernHeader;
        private ModernCardPanel pnlLeftCard;
        private ModernCardPanel pnlRightCard;
        private Label lblModernTitle;
        private Label lblModernSubtitle;
        private Label lblModernMode;
        private Button btnWorkflowTab;
        private Button btnHelpTab;
        private Panel pnlHelp;
        private RichTextBox txtHelpGuide;
        private bool _modernUiApplied;
        private bool _compactInitialSizeApplied;
        private bool _helpViewActive;
        private TaskCompletionSource<bool> _ukbMediaReadySource;
        private bool _ukbMediaReadyForSecondUkb;
        private bool _criticalPhase;
        private bool _manualRecoveryRequired;
        private bool _hardwareTestBusy;
        private bool _moxaConnected;
        private bool _safeOutputsReady;
        private bool _shutdownStarted;
        private bool _shutdownCompleted;
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
            Resize += (s, e) => LayoutRuntimeControls();
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
            Hide();
            Logger.Checkpoint("APPLICATION_SHUTDOWN", "START", "Kullanıcı uygulamayı kapattı.");

            await ShutdownResourcesAsync();

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
                        await SetPowerAndVerifyAsync(whichUkb, 0, safeStopTimeout.Token,
                            $"APPLICATION_SHUTDOWN_POWER_UKB{_cfg.GetTargetNumber(whichUkb)}");
                        await SetRecoveryAndVerifyAsync(whichUkb, 0, safeStopTimeout.Token,
                            $"APPLICATION_SHUTDOWN_RECOVERY_UKB{_cfg.GetTargetNumber(whichUkb)}");
                    }
                    Logger.Checkpoint(
                        "APPLICATION_SHUTDOWN_SAFE_OUTPUTS",
                        "SUCCESS",
                        $"target=UKB{_cfg.SelectedUkb}; Power=OFF; Recovery=NORMAL");
                }
                catch (Exception ex)
                {
                    Logger.Error("Kapanışta seçili UKB güvenli çıkış durumuna alınamadı", ex);
                    Logger.Checkpoint("APPLICATION_SHUTDOWN_SAFE_OUTPUTS", "FAILED", ex.Message);
                }
            }

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
                SettingsProfileStore.TryApplyCurrentProject(
                    _projectName,
                    _cfg,
                    out string profileLoadSummary);
                string hardwareDetectionSummary =
                    HardwareAutoConfigurator.ApplyStartupDetection(_cfg, _projectName);
                _autoDetectionSummary = profileLoadSummary + " " + hardwareDetectionSummary;
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
            _workflow.OnWaitingForOtgConnect += () => BeginInvoke((Action)(() =>
                lblStatus.Text = "Flash belleği SIM PC'den güvenle çıkarın; UKB sürüm portuna ve OTG kablosunu UKB'ye takın..."));
            _workflow.OnWaitingForOtgDisconnect += () => BeginInvoke((Action)(() =>
                lblStatus.Text = "Lütfen OTG kablosunu ve flash belleği UKB'den çıkarın..."));
            _workflow.OnOtgCableWaitWarning += () => BeginInvoke((Action)(() =>
                MessageBox.Show(
                    this,
                    "OTG kablo bağlantısı algılanamadı. OTG kablosunu doğrudan PC ile UKB arasına takın ve bağlantıyı kontrol edin.\n\n" +
                    "Tamam'a bastıktan sonra uygulama Recovery güç çevrimi yaparak USB aygıtını otomatik olarak tekrar arayacaktır.",
                    "OTG Kablo Bağlantısı Sağlanamadı",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning)));
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

                lblStatus.Text = "Moxa cihazlarına bağlanılıyor...";
                _safeOutputsReady = false;
                bool connected = await _moxa.ConnectAsync();
                if (connected && !_cfg.HardwareTestMode)
                {
                    lblStatus.Text = "Başlangıç güvenliği: Power OFF ve Recovery NORMAL doğrulanıyor...";
                    await NormalizeMoxaOutputsAsync();
                }
                _safeOutputsReady = connected;
                _moxaConnected = connected;
                UpdateConnectionLabels();
                ConfigureHardwareTestUi(connected);
                if (_cfg.HardwareTestMode && connected)
                    await RefreshHardwareTestStatesAsync();

                lblStatus.Text = connected
                    ? (_cfg.NetworkInstallMode
                        ? "Yüklemeye hazır; sürümü seçip işlemi başlatın."
                        : "Hazır.")
                    : "Moxa bağlantısı bekleniyor; Bağlantı Ayarlarından değerleri kontrol edin.";
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
                lblStatus.Text = "Donanım bağlantısı kurulamadı; ayarları kontrol edin.";
                Logger.Error("Arka plan donanım başlatması tamamlanamadı", ex);
                Logger.Checkpoint("HARDWARE_BACKGROUND_INIT", "FAILED", ex.Message);
            }
        }

        private async Task NormalizeMoxaOutputsAsync()
        {
            bool whichUkb = _cfg.SelectedUkb == 2;
            string label = "UKB" + _cfg.GetTargetNumber(whichUkb);
            Logger.Checkpoint("STARTUP_SAFE_OUTPUTS", "START", $"target={label}; Power OFF -> Recovery NORMAL");
            await SetPowerAndVerifyAsync(whichUkb, 0, CancellationToken.None, $"STARTUP_POWER_{label}");
            await SetRecoveryAndVerifyAsync(whichUkb, 0, CancellationToken.None, $"STARTUP_RECOVERY_{label}");
            Logger.Checkpoint("STARTUP_SAFE_OUTPUTS", "SUCCESS", $"target={label}; Power=OFF; Recovery=NORMAL");
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

            if (_moxaConnected && _cts == null)
            {
                _safeOutputsReady = false;
                SetButtonsEnabled(false);
                try
                {
                    await NormalizeMoxaOutputsAsync();
                    _safeOutputsReady = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("Seçilen UKB güvenli duruma alınamadı", ex);
                }
            }
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
                    ? "Moxa bağlantıları hazır."
                    : "Moxa bağlantısı bekleniyor; uygulama arka planda yeniden deneyecek.";
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
                btnConnectionSettings.Location = new Point(pnlModernHeader.Width - 205, 16);
                lblConnectionSummary.Location = new Point(
                    Math.Max(btnHelpTab.Right + 18, btnConnectionSettings.Left - 210), 17);
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

            Controls.Remove(btnConnectionSettings);
            Controls.Remove(lblConnectionSummary);
            pnlModernHeader.Controls.Add(lblModernTitle);
            pnlModernHeader.Controls.Add(lblModernSubtitle);
            pnlModernHeader.Controls.Add(lblModernMode);
            pnlModernHeader.Controls.Add(btnWorkflowTab);
            pnlModernHeader.Controls.Add(btnHelpTab);
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
                Dock = DockStyle.Fill,
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
                pnlHelp.BringToFront();
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
        }

        private void PopulateHelpGuide()
        {
            txtHelpGuide.Clear();
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
            AppendHelpBullet("TEZI paket adı / ön eki, o ortam için sürüm klasörünün başlangıç adıdır.");
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
            AppendHelpBullet("Kurulum tamamlanınca UKB kapanışı, Recovery NORMAL durumu ve normal açılıştaki OFP sürümü doğrulanır.");
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

        private void UpdateMainActionButtonTexts()
        {
            if (_cfg == null)
                return;

            if (_cfg.NetworkInstallMode)
            {
                UKB1Yak.Text = $"UKB{_cfg.SelectedUkb} — SÜRÜM YÜKLEMEYİ BAŞLAT";
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
            _consoleFlushTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _consoleFlushTimer.Tick += (s, e) => FlushPendingConsoleEntries();
            _consoleFlushTimer.Start();
        }

        private void Logger_MessageWritten(LogEntry entry)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            // UUU ve seri port kısa sürede yüzlerce satır üretebilir. Her satır için
            // BeginInvoke kullanmak Windows mesaj kuyruğunu doldurup pencereyi
            // "Yanıt Vermiyor" durumuna düşürüyordu. UI, kuyruğu periyodik toplu boşaltır.
            _pendingConsoleEntries.Enqueue(entry);
        }

        private void FlushPendingConsoleEntries()
        {
            if (IsDisposed || txtProcessConsole == null)
                return;

            int drained = 0;
            txtProcessConsole.SuspendLayout();
            try
            {
                while (drained < 200 && _pendingConsoleEntries.TryDequeue(out LogEntry entry))
                {
                    AppendConsoleEntry(entry, scrollToEnd: false);
                    drained++;
                }

                if (drained > 0)
                {
                    txtProcessConsole.SelectionStart = txtProcessConsole.TextLength;
                    txtProcessConsole.ScrollToCaret();
                }
            }
            finally
            {
                txtProcessConsole.ResumeLayout();
            }
        }


        private void AppendConsoleEntry(LogEntry entry, bool scrollToEnd = true)
        {
            txtProcessConsole.SelectionStart = txtProcessConsole.TextLength;
            txtProcessConsole.SelectionColor =
                entry.Level == "ERROR" ? Color.LightCoral :
                entry.Level == "WARN" ? Color.Khaki : Color.Gainsboro;
            txtProcessConsole.AppendText($"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}{Environment.NewLine}");
            if (scrollToEnd)
            {
                txtProcessConsole.SelectionStart = txtProcessConsole.TextLength;
                txtProcessConsole.ScrollToCaret();
            }
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
            lblStatus.Text = "Yeni ayarlarla Moxa bağlantıları kuruluyor...";
            _safeOutputsReady = false;
            _moxaConnected = await _moxa.ConnectAsync();
            if (_moxaConnected && !_cfg.HardwareTestMode)
                await NormalizeMoxaOutputsAsync();
            _safeOutputsReady = _moxaConnected;
            UpdateConnectionLabels();
            SetButtonsEnabled(_moxaConnected);
            lblStatus.Text = _moxaConnected
                ? "Ayarlar kaydedildi ve Moxa bağlantıları kuruldu."
                : "Ayarlar kaydedildi; Moxa bağlantısı kurulamadı. IP/slot/kanal değerlerini kontrol edin.";
        }

        private void SetButtonsEnabled(bool enabled)
        {
            bool selectionsReady =
                _cfg == null ||
                _cfg.HardwareTestMode ||
                (!string.IsNullOrWhiteSpace(_projectName) &&
                 (_cfg.NetworkInstallMode || driveList.SelectedItem != null) &&
                 surumList.Items.Count > 0 &&
                 surumList.SelectedItem is VersionChoice);
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
                    ? project + DefaultPlatformSuffix
                    : project);

            string selectedDisplay = projectList.Items.Cast<string>().FirstOrDefault(project =>
                AppConfig.NormalizePlatformFolderKey(GetProjectNameFromDisplay(project)) ==
                AppConfig.NormalizePlatformFolderKey(preferredProject));
            if (string.IsNullOrWhiteSpace(selectedDisplay))
                selectedDisplay = environmentProject + DefaultPlatformSuffix;
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
                _cfg.ValidateTeziPackageNamePrefix);

            if (!_cfg.NetworkInstallMode && driveList.SelectedItem != null)
            {
                string driveRoot = driveList.Text.Substring(0, 2) + "\\";
                VersionListResult flashResult = _versions.GetVersionsInFlash(
                    driveRoot,
                    _projectName);

                var matchingFlash = flashResult.Names
                    .Select((name, index) => new { Name = name, Path = flashResult.Paths[index] })
                    .Where(item =>
                        VersionManager.PackageMatchesProject(item.Path, _projectName, _cfg.ProjectPackageMappings) ||
                        (VersionManager.IsYfykProject(_projectName) &&
                         VersionManager.TeziPackageMatchesProject(item.Path, _projectName, _cfg.ProjectPackageMappings)))
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
            return value.EndsWith(DefaultPlatformSuffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - DefaultPlatformSuffix.Length).Trim()
                : value;
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
            if (_cfg.ValidateTeziPackageNamePrefix &&
                (string.IsNullOrWhiteSpace(_projectName) ||
                !(VersionManager.PackageMatchesProject(selected.Path, _projectName, _cfg.ProjectPackageMappings) ||
                  (VersionManager.IsYfykProject(_projectName) &&
                   VersionManager.TeziPackageMatchesProject(selected.Path, _projectName, _cfg.ProjectPackageMappings)))))
            {
                Logger.Checkpoint(
                    "NETWORK_PACKAGE_PREPARE",
                    "FAILED",
                    $"reason=project_mismatch; project={_projectName ?? "not-detected"}; source={selected.Path}");
                MessageBox.Show(
                    "Seçili paket UAV_PROJECT_NAME projesiyle eşleşmiyor.",
                    "Proje Eşleşme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
            if (path.Equals("/package/image.json", StringComparison.OrdinalIgnoreCase))
            {
                _networkImageRequest?.TrySetResult(path);
                Logger.Checkpoint("TEZI_IMAGE_METADATA", "SUCCESS", "path=" + path);
                return;
            }
        }

        private void TeziHttpServer_RequestStarted(string path)
        {
            if (!path.StartsWith("/package/", StringComparison.OrdinalIgnoreCase) ||
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
                        $"candidate={candidate}; selected={selectedPort}; url={healthUrl}; timeoutSec=8");
                    _serial.SendHttpHealthProbe(healthUrl);
                    await WaitForNetworkStageAsync(
                        _networkHealthRequest.Task,
                        TimeSpan.FromSeconds(8),
                        "UKB hedefinden HTTP health isteği alınmadı.",
                        ct);

                    Logger.Checkpoint(
                        "TEZI_HTTP_TARGET_PORT_PROBE",
                        "SUCCESS",
                        $"configured={_cfg.NetworkServerPort}; selected={selectedPort}; url={healthUrl}");
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
                "USB-NCM ağı çalışıyor olabilir ancak Windows Güvenlik Duvarı uygulamanın gelen TCP bağlantısını engelliyor.",
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
                throw new TimeoutException(timeoutMessage);
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
            throw new TimeoutException(timeoutMessage);
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
            string ukbLabel = "UKB" + _cfg.GetTargetNumber(whichUkb);
            UKB1Yak.Enabled = false;
            btnCancel.Enabled = true;
            bool recoveryEnabled = false;
            bool powerOn = false;
            bool installationStarted = false;
            bool shutdownObserved = false;
            bool installationSuccessful = false;
            bool retainForManualRecovery = false;
            string observedOfpVersion = null;
            string observedNetworkUpLine = null;
            string activeServerIp = null;
            int activeServerPort = 0;
            TaskCompletionSource<bool> mdnsQuerySeen = null;
            HashSet<string> interfacesBeforeEasyInstaller = HardwareAutoConfigurator.GetNetworkInterfaceIds();
            string currentStage = "NETWORK_INSTALL_INITIALIZE";
            var progress = new Progress<FlashProgress>(p =>
            {
                lblStatus.Text = p.Status;
            });
            try
            {
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

                long recoveryMark = _serial.Mark();
                SetNetworkStage(3, "Recovery Modu ve UKB Gücü Hazırlanıyor");
                lblStatus.Text = "Recovery modu REAL yapılıyor...";
                currentStage = "NETWORK_RECOVERY_REAL";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=REAL",
                    () => SetRecoveryAndVerifyAsync(whichUkb, 1, _cts.Token, "NETWORK_RECOVERY_REAL"));
                recoveryEnabled = true;

                lblStatus.Text = "UKB gücü açılıyor...";
                currentStage = "NETWORK_POWER_ON_RECOVERY";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=ON",
                    () => SetPowerAndVerifyAsync(
                        whichUkb,
                        1,
                        _cts.Token,
                        "NETWORK_POWER_ON_RECOVERY"));
                powerOn = true;
                _criticalPhase = false;
                btnCancel.Enabled = true;

                SetNetworkStage(4, "Easy Installer USB Üzerinden Yükleniyor");
                lblStatus.Text = "Toradex Easy Installer USB üzerinden yükleniyor; OTG kablosu bekleniyor...";
                currentStage = "NETWORK_EASY_INSTALLER_LOAD";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}",
                    () => _workflow.LoadEasyInstallerAsync(progress, ukbLabel, whichUkb, _cts.Token));

                SetNetworkStage(5, "USB-NCM Sanal Ağ Bağlantısı Oluşturuluyor");
                lblStatus.Text = "Hedef yükleme ortamı bekleniyor...";
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
                lblStatus.Text = "Hedef PC adresi otomatik belirleniyor...";
                currentStage = "NETWORK_HTTP_SERVER_START";
                activeServerIp = await RunLoggedStageAsync(
                    currentStage,
                    $"target={_cfg.UkbTargetIp}; configuredPort={_cfg.NetworkServerPort}; auto={_cfg.AutoDetectNetworkServerIp}",
                    async () =>
                    {
                        string resolvedServerIp = await HardwareAutoConfigurator.EnsureNetworkServerIpAsync(
                            _cfg,
                            interfacesBeforeEasyInstaller,
                            TimeSpan.FromSeconds(15),
                            _cts.Token);
                        TeziHttpServer previousServer = _teziHttpServer;
                        _teziHttpServer = null;
                        await DisposeInBackgroundAsync(previousServer);
                        _teziHttpServer = TeziHttpServer.StartWithFallback(
                            resolvedServerIp,
                            _cfg.NetworkServerPort,
                            _networkStagingPath);
                        _teziHttpServer.RequestStarted += TeziHttpServer_RequestStarted;
                        _teziHttpServer.RequestCompleted += TeziHttpServer_RequestCompleted;
                        activeServerPort = _teziHttpServer.Port;
                        return resolvedServerIp;
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
                Logger.Checkpoint(
                    "NETWORK_EASY_INSTALLER_READY",
                    "SUCCESS",
                    "method=" + (easyInstallerVerifiedByMdns ? "mdns-query" : "serial") +
                    "; evidence=" + easyInstallerEvidence);
                Logger.Checkpoint(
                    "SERIAL_PORT_GUARD",
                    selectedSerialProducedData ? "READY" : "WARNING",
                    $"selected={_serial.PortName}; dataReceived={selectedSerialProducedData}; " +
                    "action=" + (selectedSerialProducedData ? "none" : "network-flow-continues; validate-before-serial-fallback"));
                Logger.Info("Ağ testi için Easy Installer hazır: " + easyInstallerEvidence);

                SetNetworkStage(8, "UKB ile PC Ağ Erişimi Test Ediliyor");
                if (selectedSerialProducedData)
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
                if (selectedSerialProducedData)
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
                            "expectedPath=/image_list.json; timeoutSec=30; method=mdns-vnc-refresh",
                            () => WaitForTeziFeedWithRefreshAsync(
                                _networkFeedRequest.Task,
                                mdnsQuerySeen.Task,
                                TimeSpan.FromSeconds(30),
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
                        Logger.Checkpoint("NETWORK_TEZI_FEED_RECOVERY", "SUCCESS", "method=restart; attempt=2/2");
                    }
                }
                Logger.Checkpoint("TEZI_FEED_DISCOVERY", "SUCCESS", "path=" + feedRequest);

                lblStatus.Text = "Easy Installer'ın paket bilgisini istemesi bekleniyor...";
                currentStage = "NETWORK_TEZI_METADATA_REQUEST";
                string imageRequest = await RunLoggedStageAsync(
                    currentStage,
                    "expectedPath=/package/image.json; timeoutSec=60",
                    () => WaitForNetworkStageAsync(
                        _networkImageRequest.Task,
                        TimeSpan.FromSeconds(60),
                        "Easy Installer image_list.json dosyasını aldı ancak 60 saniye içinde package/image.json istemedi. " +
                        "Sürüm listesi yolu veya TEZI paket tanımını kontrol edin.",
                        _cts.Token));
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
                await Task.Delay(500, _cts.Token);
                Logger.Checkpoint(
                    "NETWORK_POWER_OFF_SETTLE",
                    "SUCCESS",
                    "delayMs=500; Power=OFF geri okundu");

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
                    Logger.Warn("Ağ hazır/Link Up satırı 45 saniye içinde görülmedi; OFP sürümü doğrulandığı için sonuç korunuyor.");
                    Logger.Checkpoint(
                        "NETWORK_NORMAL_BOOT_NETWORK_UP",
                        "WARNING",
                        ex.Message);
                }

                lblStatus.Text = "Sürüm doğrulandı; UKB gücü kapatılıyor...";
                currentStage = "NETWORK_FINAL_POWER_OFF";
                await RunLoggedStageAsync(
                    currentStage,
                    $"ukb={ukbLabel}; requested=OFF",
                    () => SetPowerAndVerifyAsync(
                        whichUkb,
                        0,
                        _cts.Token,
                        "NETWORK_FINAL_POWER_OFF"));
                powerOn = false;

                installationSuccessful = true;
                CompleteNetworkProgress();
                Logger.Checkpoint(
                    "NETWORK_INSTALL",
                    "SUCCESS",
                    $"expectedOfp={_networkExpectedOfpVersion}; observedOfp={observedOfpVersion}; networkUp={observedNetworkUpLine}");
                string networkResult = FormatNetworkVerificationResult(observedNetworkUpLine);
                lblStatus.Text =
                    "SÜRÜM YÜKLENDİ VE DOĞRULANDI — " + networkResult +
                    "; UKB kapalı, Recovery NORMAL.";
                UKB1Yak.Text = "SÜRÜM YÜKLEME TAMAMLANDI";
                MessageBox.Show(
                    "Bağlantı testleri geçti, sürüm ağ üzerinden yüklendi ve normal açılıştaki OFP sürümü doğrulandı.\n\n" +
                    $"OFP Version: {observedOfpVersion}\n" +
                    $"Ağ durumu: {networkResult}\n\n" +
                    "UKB kapalı ve Recovery NORMAL durumda bırakıldı.",
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
                if (!payloadObserved && powerOn && _serial.IsOpen)
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
                        "Başarısız aşama: " + currentStage + "\n" + ex.Message,
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
                        "Başarısız aşama: " + currentStage + "\n" + ex.Message,
                        "Sürüm Yükleme Hatası",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                _criticalPhase = false;
                await StopNetworkServicesAsync();

                if (!retainForManualRecovery && powerOn)
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
                if (installationSuccessful && !powerOn && !recoveryEnabled)
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

        private static bool IsPhysicalEthernetLinkUp(string line)
        {
            return !string.IsNullOrWhiteSpace(line) &&
                   line.IndexOf("Link is Up", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   line.IndexOf("usb", StringComparison.OrdinalIgnoreCase) < 0;
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
            if (selectedVersion == null ||
                (_cfg.ValidateTeziPackageNamePrefix &&
                (string.IsNullOrWhiteSpace(_projectName) ||
                !(VersionManager.PackageMatchesProject(selectedVersion.Path, _projectName, _cfg.ProjectPackageMappings) ||
                  (VersionManager.IsYfykProject(_projectName) &&
                   VersionManager.TeziPackageMatchesProject(selectedVersion.Path, _projectName, _cfg.ProjectPackageMappings))))))
            {
                MessageBox.Show(
                    "Seçili sürüm bilgisayarın UAV_PROJECT_NAME projesiyle eşleşmiyor. İşlem başlatılmadı.",
                    "Proje Eşleşme Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
