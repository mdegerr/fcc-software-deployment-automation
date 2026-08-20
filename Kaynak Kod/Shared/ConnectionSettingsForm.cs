using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using MessageBox = SurumYakma.LocalizedMessageBox;

namespace SurumYakma
{
    internal sealed class ConnectionSettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly string _projectName;
        private readonly bool _showEasyInstallerCredentials;
        private readonly TextBox _powerIp = new TextBox();
        private readonly NumericUpDown _powerSlot1 = NumberBox(0, 32);
        private readonly NumericUpDown _powerSlot2 = NumberBox(0, 32);
        private readonly TextBox _powerChannels1 = new TextBox();
        private readonly TextBox _powerChannels2 = new TextBox();
        private readonly TextBox _relayIp = new TextBox();
        private readonly NumericUpDown _recoverySlot1 = NumberBox(0, 32);
        private readonly NumericUpDown _recoverySlot2 = NumberBox(0, 32);
        private readonly NumericUpDown _recoveryChannel1 = NumberBox(0, 31);
        private readonly NumericUpDown _recoveryChannel2 = NumberBox(0, 31);
        private readonly ComboBox _serialPort = new WheelSafeComboBox();
        private readonly ComboBox _serialPort2 = new WheelSafeComboBox();
        private readonly ComboBox _baudRate = new WheelSafeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150
        };
        private readonly CheckBox _autoSerial = new CheckBox { Text = "COM portunu güvenli olduğunda otomatik seç", AutoSize = true };
        private readonly TextBox _targetIp = new TextBox();
        private readonly ComboBox _serverIp = new WheelSafeComboBox();
        private readonly CheckBox _autoServerIp = new CheckBox { Text = "USB-NCM PC adresini Easy Installer başladıktan sonra otomatik bul", AutoSize = true };
        private readonly NumericUpDown _serverPort = NumberBox(80, 65535);
        private readonly Label _discoveryInfo = new Label { AutoSize = false, Height = 42, ForeColor = Color.DarkSlateBlue };
        private readonly TextBox _versionsRootPath = new TextBox();
        private readonly CheckBox _easyAutoLogin = new CheckBox { Text = "Gerekirse seri konsoldan otomatik giriş yap", AutoSize = true };
        private readonly TextBox _easyUserName = new TextBox();
        private readonly TextBox _easyPassword = new TextBox { UseSystemPasswordChar = true };
        private readonly DataGridView _projectMappings = new DataGridView();

        private readonly Label _mappingInfo = new Label { AutoSize = true, ForeColor = Color.DarkSlateBlue };
        private readonly RadioButton _targetUkb1 = new RadioButton { Text = "UKB1", AutoSize = true, Checked = true };
        private readonly RadioButton _targetUkb2 = new RadioButton { Text = "UKB2", AutoSize = true };
        private readonly CheckBox _swarmMode = new CheckBox
        {
            Text = "SÜRÜ İHA modunu kullan",
            AutoSize = true
        };
        private readonly ComboBox _swarmSelectedTarget = new WheelSafeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130
        };
        private readonly DataGridView _swarmTargets = new DataGridView();

        public ConnectionSettingsForm(
            AppConfig config,
            string projectName,
            bool showEasyInstallerCredentials = true)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (!_config.SwarmModeEnabled)
                _config.MigrateLegacyTargetsToUnifiedTable();
            _projectName = projectName ?? "";
            _showEasyInstallerCredentials = showEasyInstallerCredentials;
            _baudRate.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 });
            Text = "Bağlantı ve Donanım Ayarları";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(1080, 650);
            Font = new Font("Microsoft Sans Serif", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var connectionTab = new TabPage("UKB");
            var advancedTab = new TabPage("Gelişmiş Seçenekler");
            tabs.TabPages.Add(connectionTab);
            tabs.TabPages.Add(advancedTab);
            root.Controls.Add(tabs, 0, 0);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 20,
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddHeader(table, "Algılanan proje", _projectName);
            var targetPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            targetPanel.Controls.Add(_targetUkb1);
            targetPanel.Controls.Add(_targetUkb2);
            AddRow(table, "Yükleme hedefi", targetPanel);
            AddHeader(table, "Power Box", "Birden fazla kanal için 0,1 biçimini kullanın.");
            AddRow(table, "Power Box IP", _powerIp);
            AddRow(table, "UKB1 Power MOD/slot", _powerSlot1);
            AddRow(table, "UKB1 Power kanal(lar)", _powerChannels1);
            AddRow(table, "UKB2 Power MOD/slot", _powerSlot2);
            AddRow(table, "UKB2 Power kanal(lar)", _powerChannels2);
            AddHeader(table, "Relay Box / Recovery", "Yanlış kanal başka çıkışları sürebilir; görsel/şema ile doğrulayın.");
            AddRow(table, "Relay Box IP", _relayIp);
            AddRow(table, "UKB1 Recovery MOD/slot", _recoverySlot1);
            AddRow(table, "UKB1 Recovery kanalı", _recoveryChannel1);
            AddRow(table, "UKB2 Recovery MOD/slot", _recoverySlot2);
            AddRow(table, "UKB2 Recovery kanalı", _recoveryChannel2);
            AddHeader(table, "Seri konsol", "Açılır liste yalnızca Windows'ta o anda görünen COM portlarını gösterir.");

            var serialPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            _serialPort.Width = 180;
            _serialPort.DropDownStyle = ComboBoxStyle.DropDown;
            var refreshSerial = new Button { Text = "Listeyi Yenile", AutoSize = true };
            refreshSerial.Click += (s, e) => RefreshSerialPorts();
            serialPanel.Controls.Add(_serialPort);
            serialPanel.Controls.Add(refreshSerial);
            AddRow(table, "UKB1 COM portu", serialPanel);
            var serialPanel2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            _serialPort2.Width = 180;
            _serialPort2.DropDownStyle = ComboBoxStyle.DropDown;
            var refreshSerial2 = new Button { Text = "Listeyi Yenile", AutoSize = true };
            refreshSerial2.Click += (s, e) => RefreshSerialPorts();
            serialPanel2.Controls.Add(_serialPort2);
            serialPanel2.Controls.Add(refreshSerial2);
            AddRow(table, "UKB2 COM portu", serialPanel2);
            AddRow(table, "Baud rate", _baudRate);
            AddRow(table, "", _autoSerial);

            AddHeader(table, "Easy Installer ağı", "Ağdan sürümde sunucu, USB-NCM oluştuktan sonra açılır.");
            AddRow(table, "UKB hedef IP", _targetIp);
            _serverIp.DropDownStyle = ComboBoxStyle.DropDown;
            AddRow(table, "PC / HTTP sunucu IP", _serverIp);
            AddRow(table, "HTTP portu", _serverPort);
            AddRow(table, "", _autoServerIp);
            AddRow(table, "Otomatik algılama", _discoveryInfo);

            AddHeader(table, "Sürüm deposu", "Platform klasörü bu ana klasörün altında otomatik seçilir.");
            var versionsPathPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            _versionsRootPath.Width = 360;
            var browseVersionsPath = new Button { Text = "Klasör Seç", AutoSize = true };
            browseVersionsPath.Click += (s, e) => SelectVersionsRootPath();
            versionsPathPanel.Controls.Add(_versionsRootPath);
            versionsPathPanel.Controls.Add(browseVersionsPath);
            AddRow(table, "UKB Sürümleri ana klasörü", versionsPathPanel);

            connectionTab.Controls.Add(BuildSwarmPanel());
            advancedTab.Controls.Add(BuildAdvancedPanel());

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            var save = new Button { Text = "Kaydet ve Bağlan", AutoSize = true, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Vazgeç", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += Save_Click;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 1);
            CancelButton = cancel;

            LoadValues();
            ModernUi.StyleDialog(this);
            // Baud rate diğer standart açılır listelerle aynı Windows çerçevesini kullanır.
            // Owner-draw metin girintisi uygulanmaz; değer doğal olarak sola hizalanır.
            _baudRate.FlatStyle = FlatStyle.Standard;
            _baudRate.DrawMode = DrawMode.Normal;
            _baudRate.Margin = new Padding(3);
            Localization.Apply(this);
        }

        private void LoadValues()
        {
            _powerIp.Text = _config.PowerBoxIp;
            _powerSlot1.Value = _config.PowerSlot;
            _powerSlot2.Value = _config.GetPowerSlot(true);
            _powerChannels1.Text = FormatChannels(_config.PowerChannel1, _config.PowerChannel1Secondary);
            _powerChannels2.Text = FormatChannels(_config.PowerChannel2, _config.PowerChannel2Secondary);
            _relayIp.Text = _config.RelayBoxIp;
            _recoverySlot1.Value = _config.RecoverySlot;
            _recoverySlot2.Value = _config.GetRecoverySlot(true);
            _recoveryChannel1.Value = _config.RecoveryChannel1;
            _recoveryChannel2.Value = _config.RecoveryChannel2;
            if (!_baudRate.Items.Contains(_config.SerialBaudRate))
                _baudRate.Items.Add(_config.SerialBaudRate);
            _baudRate.SelectedItem = _config.SerialBaudRate;
            _autoSerial.Checked = _config.AutoDetectSerialPort;
            _targetIp.Text = _config.UkbTargetIp;
            _serverPort.Value = _config.NetworkServerPort;
            _autoServerIp.Checked = _config.AutoDetectNetworkServerIp;
            _versionsRootPath.Text = _config.GetVersionsRootPath();
            _easyAutoLogin.Checked = _config.EasyInstallerAutoLoginEnabled;
            _easyUserName.Text = _config.EasyInstallerUserName;
            _easyPassword.Text = _config.EasyInstallerPassword;
            _targetUkb1.Checked = _config.SelectedUkb != 2;
            _targetUkb2.Checked = _config.SelectedUkb == 2;
            LoadSwarmTargets();
            LoadProjectMappings();
            RefreshSerialPorts();
            RefreshNetworkAddresses();
        }

        private void RefreshSerialPorts()
        {
            string current = string.IsNullOrWhiteSpace(_serialPort.Text) ? _config.SerialPortName : _serialPort.Text;
            string[] ports = HardwareAutoConfigurator.GetAvailableSerialPorts();
            _serialPort.Items.Clear();
            _serialPort.Items.AddRange(ports.Cast<object>().ToArray());
            _serialPort.Text = current;
            string current2 = string.IsNullOrWhiteSpace(_serialPort2.Text) ? _config.SerialPortName2 : _serialPort2.Text;
            _serialPort2.Items.Clear();
            _serialPort2.Items.AddRange(ports.Cast<object>().ToArray());
            _serialPort2.Text = current2;
            if (_swarmTargets.Columns["Com"] is DataGridViewComboBoxColumn comColumn)
            {
                comColumn.Items.Clear();
                comColumn.Items.AddRange(ports.Cast<object>().ToArray());
                foreach (DataGridViewRow row in _swarmTargets.Rows)
                {
                    string selected = Convert.ToString(row.Cells["Com"].Value);
                    if (!ports.Contains(selected, StringComparer.OrdinalIgnoreCase))
                        row.Cells["Com"].Value = null;
                }
            }
            _discoveryInfo.Text = "Aktif COM: " + (ports.Length == 0 ? "bulunamadı" : string.Join(", ", ports));
        }

        private void RefreshNetworkAddresses()
        {
            string[] addresses = HardwareAutoConfigurator.GetLocalIpv4Addresses();
            _serverIp.Items.Clear();
            _serverIp.Items.AddRange(addresses.Cast<object>().ToArray());
            _serverIp.Text = _config.NetworkServerIp;
            _discoveryInfo.Text += Environment.NewLine +
                "Aktif IPv4: " + (addresses.Length == 0 ? "bulunamadı" : string.Join(", ", addresses));
        }

        private void SelectVersionsRootPath()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "UKB Sürümleri ana klasörünü seçin",
                SelectedPath = Directory.Exists(_versionsRootPath.Text)
                    ? _versionsRootPath.Text
                    : _config.GetVersionsRootPath()
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _versionsRootPath.Text = dialog.SelectedPath;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            try
            {
                ValidateIp(_powerIp.Text, "Power Box IP");
                ValidateIp(_relayIp.Text, "Relay Box IP");
                ValidateIp(_targetIp.Text, "UKB hedef IP");
                ValidateIp(_serverIp.Text, "PC / HTTP sunucu IP");
                ParseChannels(_powerChannels1.Text, out byte power1, out int power1Secondary);
                ParseChannels(_powerChannels2.Text, out byte power2, out int power2Secondary);
                string serial = _serialPort.Text.Trim().ToUpperInvariant();
                string serial2 = _serialPort2.Text.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(serial))
                    throw new InvalidOperationException("UKB1 COM portu boş bırakılamaz.");
                if (!_targetUkb1.Checked && !_targetUkb2.Checked)
                    throw new InvalidOperationException("En az bir yükleme hedefi seçilmelidir.");
                if (_targetUkb2.Checked && string.IsNullOrWhiteSpace(serial2))
                    throw new InvalidOperationException("UKB2 COM portu boş bırakılamaz.");

                _config.PowerBoxIp = _powerIp.Text.Trim();
                _config.PowerSlot = (byte)_powerSlot1.Value;
                _config.PowerSlot2 = (int)_powerSlot2.Value;
                _config.PowerChannel1 = power1;
                _config.PowerChannel1Secondary = power1Secondary;
                _config.PowerChannel2 = power2;
                _config.PowerChannel2Secondary = power2Secondary;
                _config.RelayBoxIp = _relayIp.Text.Trim();
                _config.RecoverySlot = (byte)_recoverySlot1.Value;
                _config.RecoverySlot2 = (int)_recoverySlot2.Value;
                _config.RecoveryChannel1 = (byte)_recoveryChannel1.Value;
                _config.RecoveryChannel2 = (byte)_recoveryChannel2.Value;
                _config.SelectedUkb = _targetUkb1.Checked ? 1 : 2;
                _config.SerialPortName = serial;
                _config.SerialPortName2 = serial2;
                _config.SerialBaudRate = Convert.ToInt32(_baudRate.SelectedItem);
                _config.AutoDetectSerialPort = _autoSerial.Checked;
                _config.UkbTargetIp = _targetIp.Text.Trim();
                _config.NetworkServerIp = _serverIp.Text.Trim();
                _config.NetworkServerPort = (int)_serverPort.Value;
                _config.AutoDetectNetworkServerIp = _autoServerIp.Checked;
                if (string.IsNullOrWhiteSpace(_versionsRootPath.Text))
                    throw new InvalidOperationException("UKB Sürümleri ana klasörü boş bırakılamaz.");
                _config.VersionsRootPath = Path.GetFullPath(_versionsRootPath.Text.Trim());
                if (_showEasyInstallerCredentials)
                {
                    _config.EasyInstallerAutoLoginEnabled = _easyAutoLogin.Checked;
                    _config.EasyInstallerUserName = _easyUserName.Text.Trim();
                    _config.EasyInstallerPassword = _easyPassword.Text;
                }
                _config.ProjectPackageMappings = ReadProjectMappings();
                _config.ValidateTeziPackageNamePrefix = false;
                SaveSwarmTargets();
                _config.ValidateForSave();
                _config.SaveMachineSpecific();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ayar Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Control BuildAdvancedPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = _showEasyInstallerCredentials ? 9 : 5
            };
            int gridRow = panel.RowCount - 2;
            for (int row = 0; row < panel.RowCount; row++)
                panel.RowStyles.Add(new RowStyle(
                    row == gridRow ? SizeType.Percent : SizeType.AutoSize,
                    row == gridRow ? 100 : 0));

            panel.Controls.Add(new Label
            {
                Text = "Sürüm deposu",
                Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 5)
            });
            var versionsPathPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(3, 0, 3, 12)
            };
            _versionsRootPath.Width = 700;
            var browseVersionsPath = new Button { Text = "Klasör Seç", AutoSize = true };
            browseVersionsPath.Click += (s, e) => SelectVersionsRootPath();
            versionsPathPanel.Controls.Add(new Label
            {
                Text = "UKB Sürümleri ana klasörü",
                AutoSize = true,
                Margin = new Padding(0, 8, 10, 0)
            });
            versionsPathPanel.Controls.Add(_versionsRootPath);
            versionsPathPanel.Controls.Add(browseVersionsPath);
            panel.Controls.Add(versionsPathPanel);

            if (_showEasyInstallerCredentials)
            {
                panel.Controls.Add(new Label
                {
                    Text = "Easy Installer seri konsol girişi",
                    Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold),
                    AutoSize = true,
                    Margin = new Padding(3, 4, 3, 8)
                });
                panel.Controls.Add(_easyAutoLogin);

                var credentials = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ColumnCount = 2
                };
                credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
                credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                AddRow(credentials, "Kullanıcı adı", _easyUserName);

                var passwordPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
                _easyPassword.Width = 280;
                var showPassword = new CheckBox { Text = "Göster", AutoSize = true };
                showPassword.CheckedChanged += (s, e) => _easyPassword.UseSystemPasswordChar = !showPassword.Checked;
                passwordPanel.Controls.Add(_easyPassword);
                passwordPanel.Controls.Add(showPassword);
                AddRow(credentials, "Şifre", passwordPanel);
                panel.Controls.Add(credentials);

                panel.Controls.Add(new Label
                {
                    Text = "Şifre appsettings dosyasına düz metin yazılmaz; bu Windows kullanıcı hesabına bağlı olarak şifrelenir.",
                    AutoSize = true,
                    ForeColor = Color.DimGray,
                    Margin = new Padding(3, 2, 3, 14)
                });
            }
            panel.Controls.Add(new Label
            {
                Text = "Platform listesi",
                Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 4)
            });

            _projectMappings.Dock = DockStyle.Fill;
            _projectMappings.AutoGenerateColumns = false;
            _projectMappings.AllowUserToAddRows = false;
            _projectMappings.AllowUserToDeleteRows = false;
            _projectMappings.RowHeadersVisible = false;
            _projectMappings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _projectMappings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _projectMappings.AllowUserToResizeColumns = false;
            _projectMappings.AllowUserToResizeRows = false;
            _projectMappings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _projectMappings.RowTemplate.Resizable = DataGridViewTriState.False;
            _projectMappings.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "Seç",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 55,
                MinimumWidth = 55,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _projectMappings.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "EnvironmentName",
                HeaderText = "Ortam / UAV_PROJECT_NAME",
                FillWeight = 100,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            panel.Controls.Add(_projectMappings);

            var mappingButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var addRow = new Button { Text = "Satır Ekle", AutoSize = true };
            addRow.Click += (s, e) => AddMappingRow();
            var deleteRows = new Button { Text = "Seçilenleri Sil", AutoSize = true };
            deleteRows.Click += (s, e) => DeleteSelectedMappingRows();
            var addCurrent = new Button { Text = "Algılanan Ortamı Ekle", AutoSize = true };
            addCurrent.Click += (s, e) => AddCurrentEnvironment();
            var defaults = new Button { Text = "Varsayılanları Yükle", AutoSize = true };
            defaults.Click += (s, e) => FillMappingGrid(ProjectPackageMapping.CreateDefaults());
            mappingButtons.Controls.Add(addRow);
            mappingButtons.Controls.Add(deleteRows);
            mappingButtons.Controls.Add(addCurrent);
            mappingButtons.Controls.Add(defaults);
            mappingButtons.Controls.Add(_mappingInfo);
            panel.Controls.Add(mappingButtons);
            return panel;
        }

        private Control BuildSwarmPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 3
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var boxAddresses = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
            boxAddresses.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            boxAddresses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            boxAddresses.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            boxAddresses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            boxAddresses.Controls.Add(new Label { Text = "Power Box IP", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 0);
            boxAddresses.Controls.Add(_powerIp, 1, 0);
            boxAddresses.Controls.Add(new Label { Text = "Relay Box IP", AutoSize = true, Margin = new Padding(12, 8, 3, 3) }, 2, 0);
            boxAddresses.Controls.Add(_relayIp, 3, 0);
            boxAddresses.Controls.Add(new Label { Text = "Baud rate", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 1);
            _baudRate.Dock = DockStyle.Fill;
            boxAddresses.Controls.Add(_baudRate, 1, 1);
            panel.Controls.Add(boxAddresses);

            _swarmTargets.Dock = DockStyle.Fill;
            _swarmTargets.BackgroundColor = Color.White;
            _swarmTargets.ColumnHeadersHeight = 28;
            _swarmTargets.RowTemplate.Height = 26;
            _swarmTargets.ScrollBars = ScrollBars.None;
            _swarmTargets.AutoGenerateColumns = false;
            _swarmTargets.AllowUserToAddRows = false;
            _swarmTargets.AllowUserToDeleteRows = false;
            _swarmTargets.RowHeadersVisible = false;
            _swarmTargets.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _swarmTargets.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _swarmTargets.AllowUserToResizeColumns = false;
            _swarmTargets.AllowUserToResizeRows = false;
            _swarmTargets.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _swarmTargets.RowTemplate.Resizable = DataGridViewTriState.False;
            _swarmTargets.KeyDown += SwarmTargets_KeyDown;
            _swarmTargets.EditingControlShowing += SwarmTargets_EditingControlShowing;
            AddSwarmColumn("Ukb", "Hedef", 42, readOnly: true);
            _swarmTargets.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Com",
                HeaderText = "COM",
                FillWeight = 100,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.False
            });
            AddSwarmColumn("PowerSlot", "Power MOD", 65);
            AddSwarmColumn("PowerChannels", "Power kanal(lar)", 92);
            AddSwarmColumn("RecoverySlot", "Recovery MOD", 72);
            AddSwarmColumn("RecoveryChannel", "Recovery kanal", 72);
            panel.Controls.Add(_swarmTargets);
            var network = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
            network.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            network.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            network.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            network.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            network.Controls.Add(new Label { Text = "UKB hedef IP", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 0);
            network.Controls.Add(_targetIp, 1, 0);
            network.Controls.Add(new Label { Text = "HTTP sunucu IP", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(12, 3, 3, 3) }, 2, 0);
            network.Controls.Add(_serverIp, 3, 0);
            network.Controls.Add(new Label { Text = "HTTP portu", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 1);
            network.Controls.Add(_serverPort, 1, 1);
            network.Controls.Add(_autoServerIp, 0, 2);
            network.SetColumnSpan(_autoServerIp, 4);
            panel.Controls.Add(network);
            return panel;
        }

        private void SwarmTargets_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || _swarmTargets.CurrentCell?.OwningColumn?.Name != "Com")
                return;
            _swarmTargets.CurrentCell.Value = null;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void SwarmTargets_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (!(e.Control is ComboBox combo))
                return;
            combo.KeyDown -= ComEditingControl_KeyDown;
            combo.KeyDown += ComEditingControl_KeyDown;
        }

        private void ComEditingControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || _swarmTargets.CurrentCell?.OwningColumn?.Name != "Com")
                return;
            _swarmTargets.EndEdit();
            _swarmTargets.CurrentCell.Value = null;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void AddSwarmColumn(string name, string title, float weight, bool readOnly = false)
        {
            _swarmTargets.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = title,
                FillWeight = 100,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.False
            });
        }

        private void LoadSwarmTargets()
        {
            _swarmMode.Checked = true;
            UkbTargetConfig[] configuredTargets = Enumerable.Range(1, 6).Select(_config.GetTarget).ToArray();
            string[] activePorts = HardwareAutoConfigurator.GetAvailableSerialPorts();
            if (_swarmTargets.Columns["Com"] is DataGridViewComboBoxColumn comColumn)
            {
                comColumn.Items.Clear();
                comColumn.Items.AddRange(activePorts.Cast<object>().ToArray());
            }
            _swarmTargets.Rows.Clear();
            foreach (UkbTargetConfig target in configuredTargets)
            {
                _swarmTargets.Rows.Add(
                    "UKB" + target.UkbNumber,
                    activePorts.Contains(target.ComPort, StringComparer.OrdinalIgnoreCase)
                        ? target.ComPort
                        : null,
                    target.PowerSlot,
                    FormatChannels(target.PowerChannel, target.PowerSecondaryChannel),
                    target.RecoverySlot,
                    target.RecoveryChannel);
            }
        }

        private void SaveSwarmTargets()
        {
            var targets = new List<UkbTargetConfig>();
            for (int index = 0; index < 6; index++)
            {
                DataGridViewRow row = _swarmTargets.Rows[index];
                string com = Convert.ToString(row.Cells["Com"].Value)?.Trim().ToUpperInvariant() ?? "";
                if (index == _config.SelectedUkb - 1 && string.IsNullOrWhiteSpace(com))
                    throw new InvalidOperationException($"Seçili UKB{index + 1} COM portu boş bırakılamaz.");
                ParseChannels(Convert.ToString(row.Cells["PowerChannels"].Value),
                    out byte powerChannel, out int powerSecondary);
                targets.Add(new UkbTargetConfig
                {
                    UkbNumber = index + 1,
                    ComPort = com,
                    BaudRate = Convert.ToInt32(_baudRate.SelectedItem),
                    PowerSlot = ParseByteCell(row, "PowerSlot", $"UKB{index + 1} Power MOD", 32),
                    PowerChannel = powerChannel,
                    PowerSecondaryChannel = powerSecondary,
                    RecoverySlot = ParseByteCell(row, "RecoverySlot", $"UKB{index + 1} Recovery MOD", 32),
                    RecoveryChannel = ParseByteCell(row, "RecoveryChannel", $"UKB{index + 1} Recovery kanal", 31)
                });
            }
            _config.SwarmModeEnabled = true;
            _config.SwarmUkbTargets = targets;
            UkbTargetConfig first = targets[0];
            UkbTargetConfig second = targets[1];
            _config.SerialPortName = first.ComPort;
            _config.SerialPortName2 = second.ComPort;
            _config.SerialBaudRate = Convert.ToInt32(_baudRate.SelectedItem);
            _config.PowerSlot = first.PowerSlot;
            _config.PowerSlot2 = second.PowerSlot;
            _config.PowerChannel1 = first.PowerChannel;
            _config.PowerChannel1Secondary = first.PowerSecondaryChannel;
            _config.PowerChannel2 = second.PowerChannel;
            _config.PowerChannel2Secondary = second.PowerSecondaryChannel;
            _config.RecoverySlot = first.RecoverySlot;
            _config.RecoverySlot2 = second.RecoverySlot;
            _config.RecoveryChannel1 = first.RecoveryChannel;
            _config.RecoveryChannel2 = second.RecoveryChannel;
        }

        private static int ParseIntCell(DataGridViewRow row, string column, string label, int minimum, int maximum)
        {
            string text = Convert.ToString(row.Cells[column].Value)?.Trim() ?? "";
            if (!int.TryParse(text, out int value) || value < minimum || value > maximum)
                throw new InvalidOperationException($"{label} {minimum}-{maximum} arasında olmalıdır.");
            return value;
        }

        private void UpdateTargetModeAvailability()
        {
            bool normalMode = !_swarmMode.Checked;
            foreach (Control control in new Control[]
            {
                _targetUkb1, _targetUkb2,
                _powerSlot1, _powerSlot2, _powerChannels1, _powerChannels2,
                _recoverySlot1, _recoverySlot2, _recoveryChannel1, _recoveryChannel2,
                _serialPort, _serialPort2, _autoSerial
            })
                control.Enabled = normalMode;
        }

        private static byte ParseByteCell(DataGridViewRow row, string column, string label, int maximum)
        {
            string text = Convert.ToString(row.Cells[column].Value)?.Trim() ?? "";
            if (!byte.TryParse(text, out byte value) || value > maximum)
                throw new InvalidOperationException($"{label} 0-{maximum} arasında olmalıdır.");
            return value;
        }

        private void LoadProjectMappings()
        {
            List<ProjectPackageMapping> mappings = _config.ProjectPackageMappings == null ||
                                                   _config.ProjectPackageMappings.Count == 0
                ? ProjectPackageMapping.CreateDefaults()
                : _config.ProjectPackageMappings;
            FillMappingGrid(mappings);
            AddCurrentEnvironment();
        }

        private void FillMappingGrid(IEnumerable<ProjectPackageMapping> mappings)
        {
            _projectMappings.Rows.Clear();
            foreach (ProjectPackageMapping mapping in mappings)
                _projectMappings.Rows.Add(false, mapping.EnvironmentName);
            UpdateMappingInfo();
        }

        private void AddMappingRow()
        {
            int index = _projectMappings.Rows.Add(false, "");
            _projectMappings.CurrentCell = _projectMappings.Rows[index].Cells["EnvironmentName"];
            _projectMappings.BeginEdit(true);
        }

        private void DeleteSelectedMappingRows()
        {
            DataGridViewRow[] selected = _projectMappings.Rows.Cast<DataGridViewRow>()
                .Where(row => Convert.ToBoolean(row.Cells["Selected"].Value ?? false))
                .ToArray();
            foreach (DataGridViewRow row in selected)
                _projectMappings.Rows.Remove(row);
            UpdateMappingInfo();
        }

        private void AddCurrentEnvironment()
        {
            if (string.IsNullOrWhiteSpace(_projectName))
                return;
            foreach (DataGridViewRow row in _projectMappings.Rows)
            {
                string pattern = Convert.ToString(row.Cells["EnvironmentName"].Value);
                if (!row.IsNewRow && EnvironmentPatternMatches(pattern, _projectName))
                {
                    UpdateMappingInfo();
                    return;
                }
            }
            _projectMappings.Rows.Add(false, _projectName);
            UpdateMappingInfo();
        }

        private List<ProjectPackageMapping> ReadProjectMappings()
        {
            var result = new List<ProjectPackageMapping>();
            foreach (DataGridViewRow row in _projectMappings.Rows)
            {
                if (row.IsNewRow)
                    continue;
                string environmentName = Convert.ToString(row.Cells["EnvironmentName"].Value)?.Trim() ?? "";
                if (environmentName.Length == 0)
                    continue;
                result.Add(new ProjectPackageMapping
                {
                    EnvironmentName = environmentName,
                    PackageNamePrefix = environmentName
                });
            }
            return result;
        }

        private void UpdateMappingInfo()
        {
            if (string.IsNullOrWhiteSpace(_projectName))
            {
                _mappingInfo.Text = "Aktif ortam algılanmadı.";
                return;
            }

            _mappingInfo.Text = "Aktif platform: " + _projectName;

        }

        private static bool EnvironmentPatternMatches(string pattern, string environmentName)
        {
            string normalizedPattern = (pattern ?? "").Trim().Replace('-', '_');
            string normalizedEnvironment = (environmentName ?? "").Trim().Replace('-', '_');
            if (normalizedPattern.EndsWith("*", StringComparison.Ordinal))
                return normalizedEnvironment.StartsWith(
                    normalizedPattern.Substring(0, normalizedPattern.Length - 1),
                    StringComparison.OrdinalIgnoreCase);
            return normalizedEnvironment.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatChannels(byte primary, int secondary)
        {
            return secondary >= 0 ? primary + "," + secondary : primary.ToString();
        }

        private static void ParseChannels(string text, out byte primary, out int secondary)
        {
            string[] parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2 ||
                !byte.TryParse(parts[0], out primary) || primary > 31)
                throw new InvalidOperationException("Power kanalı tek kanal (0) veya kanal grubu (0,1) biçiminde olmalıdır.");
            secondary = -1;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out secondary) || secondary < 0 || secondary > 31 || secondary == primary))
                throw new InvalidOperationException("İkinci Power kanalı 0-31 arasında ve ilk kanaldan farklı olmalıdır.");
        }

        private static void ValidateIp(string text, string label)
        {
            if (!IPAddress.TryParse((text ?? "").Trim(), out _))
                throw new InvalidOperationException(label + " geçerli bir IP adresi olmalıdır.");
        }

        private static NumericUpDown NumberBox(decimal value, decimal maximum)
        {
            return new WheelSafeNumericUpDown { Minimum = 0, Maximum = maximum, Value = Math.Min(value, maximum), Width = 180 };
        }

        private sealed class WheelSafeNumericUpDown : NumericUpDown
        {
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                // Ayar değerleri yalnızca klavye veya ok düğmeleriyle değiştirilsin.
            }
        }

        private sealed class WheelSafeComboBox : ComboBox
        {
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                // Kapalı açılır listede mouse tekerleği seçimi değiştirmesin.
            }
        }

        private static void AddHeader(TableLayoutPanel table, string title, string detail)
        {
            var label = new Label
            {
                Text = title,
                Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(3, 12, 3, 3)
            };
            var info = new Label { Text = detail, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 14, 3, 3) };
            table.Controls.Add(label);
            table.Controls.Add(info);
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            var name = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 4, 3, 3);
            table.Controls.Add(name);
            table.Controls.Add(control);
        }
    }
}
