using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SurumYakma;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "SurumYakmaTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            RunSessionLoggerAndCheckpointTest(testRoot);
            Console.WriteLine("PASS: tarih-saatli oturum logu ve gizli checkpoint kaydı");
            RunSettingsProfileStoreTest(testRoot);
            Console.WriteLine("PASS: Settings.json ortam profili, mevcut alan koruma ve yedekleme");
            RunVersionPreparationTest(testRoot);
            Console.WriteLine("PASS: sürüm hazırlama, kaynak koruma ve autoinstall seçimi");
            RunOfpPreparationTest(testRoot);
            Console.WriteLine("PASS: OFP keşfi, ELF doğrulaması ve flash kopyası");
            RunNaturalVersionSortTest();
            Console.WriteLine("PASS: sürümler en güncel doğal sayı sırasıyla listeleniyor");
            RunFriendlyVersionFolderSelectionTest(testRoot);
            Console.WriteLine("PASS: dış sürüm adı ve iç TEZI yükleme yolu eşlemesi");
            RunAutomaticPlatformFolderTest(testRoot);
            Console.WriteLine("PASS: otomatik masaustu kok yolu ve toleransli platform klasoru");
            RunFlatTeziRepositoryAndProjectAliasTest(testRoot);
            Console.WriteLine("PASS: düz TEZI deposu ve proje alias eşlemesi");
            RunHardwareProfileTest();
            Console.WriteLine("PASS: Şimşek otomatik MOXA slot/kanal profili");
            RunAdvancedSettingsTest();
            Console.WriteLine("PASS: gelismis ayarlar ve ortam-paket eslemesi");
            RunLocalizationAndFinalStateTest(testRoot);
            Console.WriteLine("PASS: TR/EN, OTG onayi, LINK sonucu ve basarili son durum korumalari");
            RunSwarmUavConfigurationTest(testRoot);
            Console.WriteLine("PASS: SURU IHA alti UKB hedef paneli ve secili hedef cozumleme");
            RunTeziPrefixToggleTest(testRoot);
            Console.WriteLine("PASS: istege bagli TEZI on ad dogrulamasi ve profil kaydi");
            RunOtgWaitStateParsingTest();
            Console.WriteLine("PASS: OTG bekleme ve yeniden baglanma durum algilama");
            RunUsbBulkTimeoutParsingTest();
            Console.WriteLine("PASS: UUU USB bulk timeout hatasi secici yeniden deneme algilama");
            RunRecoveryToolSelfHealTest(testRoot);
            Console.WriteLine("PASS: bundled recovery araclari atomik restore ve hash dogrulamasi");
            RunModernUiSmokeTest(true);
            Console.WriteLine("PASS: modern ağdan aktarım arayüzü yerleşim kontrolü");
            RunEnglishUiSmokeTest();
            Console.WriteLine("PASS: ana ekran dinamik Ingilizce metin ve Default platform gorunumu");
            RunSingleTargetUiSmokeTest();
            Console.WriteLine("PASS: UKB1/UKB2 tekli hedef seçimi ve seçili COM özeti");
            RunTeziHttpServerTest(testRoot);
            Console.WriteLine("PASS: TEZI HTTP health, feed, dosya ve range aktarımı");
            RunNetworkPackagePreparationTest(testRoot);
            Console.WriteLine("PASS: ağ staging kopyası, kaynak koruma ve autoinstall hazırlığı");
            RunTeziMdnsPacketTest();
            Console.WriteLine("PASS: TEZI Zeroconf DNS-SD duyuru paketi");
            RunEasyInstallerEvidenceTest();
            Console.WriteLine("PASS: Easy Installer seri veya mDNS kanıtı seçimi");
            RunTeziVncRefreshPacketTest();
            Console.WriteLine("PASS: TEZI VNC Refresh (r) tus paketi");
            RunManifestAndFirewallRuleTest();
            Console.WriteLine("PASS: manifest and safe firewall command arguments");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    private static void RunSettingsProfileStoreTest(string testRoot)
    {
        string settingsPath = Path.Combine(testRoot, "Settings.json");
        string original = "{\n  \"UKB_1\": { \"ComPort\": \"COM14\" },\n  \"MOXAPATHS\": { \"YFYK\": \"AKSUNGUR\" }\n}\n";
        File.WriteAllText(settingsPath, original);

        var config = new AppConfig
        {
            SerialPortName = "COM27",
            SerialBaudRate = 115200,
            AutoDetectSerialPort = false,
            PowerBoxIp = "10.10.1.60",
            PowerSlot = 8,
            PowerChannel1 = 4,
            PowerChannel1Secondary = -1,
            RelayBoxIp = "10.10.1.40",
            RecoverySlot = 7,
            RecoveryChannel1 = 6,
            UkbTargetIp = "192.168.11.1",
            NetworkServerIp = "192.168.11.221",
            NetworkServerPort = 8088,
            AutoDetectNetworkServerIp = true,
            ValidateTeziPackageNamePrefix = false,
            UiLanguage = "EN",
            VersionsRootPath = Path.Combine(testRoot, "UKB Versions")
        };

        SettingsProfileStore.Save(settingsPath, "YFYK 8.1", config);
        Assert(SettingsProfileStore.GetProjectNames(settingsPath).SequenceEqual(new[] { "YFYK 8.1" }),
            "Settings.json icindeki kayitli platform adlari okunamadi.");
        string updated = File.ReadAllText(settingsPath);
        string backup = File.ReadAllText(settingsPath + ".bak");
        Assert(backup == original, "Settings.json .bak yedegi kaynak icerigi korumadi.");
        Assert(updated.Contains("\"UKB_1\""), "Settings.json mevcut UKB_1 alani silindi.");
        Assert(updated.Contains("\"MOXAPATHS\""), "Settings.json mevcut MOXAPATHS alani silindi.");
        Assert(updated.Contains("\"SURUM_YAKMA_PROFILES\""), "Surum Yakma profil bolumu eklenmedi.");
        Assert(updated.Contains("\"YFYK 8.1\""), "Aktif ortam profili eklenmedi.");
        Assert(updated.Contains("\"ComPort\": \"COM27\""), "Manuel COM ayari profile yazilmadi.");
        Assert(updated.Contains("\"IPAddress\": \"10.10.1.60\""), "Power Box IP profile yazilmadi.");
        Assert(updated.Contains("\"IPAddress\": \"10.10.1.40\""), "Relay Box IP profile yazilmadi.");
        Assert(updated.Contains("\"UiLanguage\": \"EN\""), "Arayuz dili profile yazilmadi.");
        Assert(updated.Contains("\"VersionsRootPath\""), "Surum deposu yolu profile yazilmadi.");
    }

    private static void RunLocalizationAndFinalStateTest(string testRoot)
    {
        Localization.SetLanguage("EN");
        Assert(Localization.T("Türkçe", "English") == "English",
            "Ingilizce arayuz secimi uygulanmadi.");
        Assert(Localization.TranslateToEnglish("Uygulama başladı.") == "Application started.",
            "Log mesaji Ingilizceye cevrilemedi.");
        Assert(Localization.TranslateToEnglish("Moxa cihazlarına bağlanılıyor...") ==
               "Connecting to Moxa devices...",
            "Moxa durum metni tam Ingilizceye cevrilemedi.");
        Assert(!Localization.TranslateToEnglish("Moxa cihazlarına bağlanılıyor...")
                   .Contains("bnetwork", StringComparison.OrdinalIgnoreCase),
            "Kisa kelime cevirisi Turkce kelimenin icini bozdu.");
        Assert(Localization.TranslateToEnglish("1. Yüklenecek Sürüm") ==
               "1. Version to Install" &&
               Localization.TranslateToEnglish("2. Yüklenecek UKB") ==
               "2. Target UKB" &&
               Localization.TranslateToEnglish("3. Yükleme İşlemi") ==
               "3. Installation",
            "Ana ekran basliklari tamamen Ingilizce degil.");
        Assert(Localization.TranslateToEnglish("Bağlantı ve Donanım Ayarları") ==
               "Connection and Hardware Settings",
            "Baglanti penceresi basligi tamamen Ingilizce degil.");
        Assert(Localization.TranslateToEnglish(
                   "Sürüm yükleme başlatılamadı. Başarısız aşama: NETWORK_TEST") ==
               "Version installation could not be started. Failed stage: NETWORK_TEST",
            "Hata popup metni tamamen Ingilizce degil.");
        Assert(Localization.TranslateToEnglish("Uygulama Zaten Açık") ==
               "Application Already Running" &&
               Localization.TranslateToEnglish("OTG Kablosunu Takın") ==
               "Connect the OTG Cable" &&
               Localization.TranslateToEnglish("Sürüm Yükleme Başarılı") ==
               "Version Installation Successful",
            "Popup basliklarinin Ingilizce karsiliklari eksik.");

        var config = new AppConfig
        {
            UiLanguage = "EN",
            VersionsRootPath = Path.Combine(testRoot, "custom-version-root")
        };
        Assert(config.GetVersionsRootPath() == config.VersionsRootPath,
            "Manuel surum deposu yolu kullanilmadi.");

        PropertyInfo otgConfirmation = typeof(FlashWorkflow).GetProperty(
            "WaitForOtgCableConfirmationAsync",
            BindingFlags.Instance | BindingFlags.Public);
        Assert(otgConfirmation?.PropertyType == typeof(Func<CancellationToken, Task>),
            "OTG kullanici onayi bekleme noktasi bulunamadi.");

        MethodInfo linkCheck = typeof(Form1).GetMethod(
            "IsPhysicalEthernetLinkUp",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(linkCheck != null &&
               (bool)linkCheck.Invoke(null, new object[] { "eth0: Link is Up" }) &&
               !(bool)linkCheck.Invoke(null, new object[] { "usb0: Link is Up" }),
            "Fiziksel LINK IS UP ayrimi dogru calismiyor.");

        FieldInfo preservePower = typeof(Form1).GetField(
            "_preservePowerAfterSuccessfulInstall",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo resultDialog = typeof(Form1).GetMethod(
            "ShowInstallResultDialog",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(preservePower?.FieldType == typeof(bool) && resultDialog != null,
            "Basarili kurulumda Power ON veya renkli LINK sonuc korumasi eksik.");

        Type dialogType = typeof(AppConfig).Assembly.GetType(
            "SurumYakma.ConnectionSettingsForm",
            throwOnError: true);
        using (var dialog = (Form)Activator.CreateInstance(
            dialogType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { config, "KSIMSEK", false },
            culture: null))
        {
            Assert(dialog.Text == "Connection and Hardware Settings",
                "Baglanti penceresi Ingilizce baslikla acilmadi.");
            string[] texts = Descendants(dialog).Select(control => control.Text).ToArray();
            Assert(texts.Contains("Advanced Options") &&
                   texts.Contains("Automatically detect the USB-NCM PC address after Easy Installer starts") &&
                   texts.Contains("Validate the TEZI package prefix against the environment"),
                "Baglanti/Gelismis ayarlarda Ingilizceye cevrilmemis kontroller var.");
        }
        Localization.SetLanguage("TR");
    }

    private static void RunTeziPrefixToggleTest(string testRoot)
    {
        string repository = Path.Combine(testRoot, "prefix-toggle");
        string package = CreateTeziPackage(
            repository,
            "kiha-Tezi_6.6.0-devel-20260701120000+build.0",
            "KIHA 11.0.16.1.0");

        Assert(VersionManager.GetTeziPackagePaths(repository, "TUK").Length == 0,
            "Acik on ad dogrulamasi farkli platform paketini kabul etti.");
        Assert(VersionManager.GetTeziPackagePaths(repository, "TUK", null, false).Single() == package,
            "Kapali on ad dogrulamasi gecerli TEZI yapisini kabul etmedi.");

        string settingsPath = Path.Combine(testRoot, "prefix-toggle-Settings.json");
        File.WriteAllText(settingsPath, "{}");
        var source = new AppConfig { ValidateTeziPackageNamePrefix = false };
        SettingsProfileStore.Save(settingsPath, "TUK", source);
        string savedProfile = File.ReadAllText(settingsPath);
        Assert(savedProfile.Contains(nameof(AppConfig.ValidateTeziPackageNamePrefix)) &&
               savedProfile.Contains("false"),
            "TEZI on ad secenegi Settings.json profiline yazilmadi.");
    }

    private static void RunAdvancedSettingsTest()
    {
        Localization.SetLanguage("TR");
        var mappings = new List<ProjectPackageMapping>
        {
            new ProjectPackageMapping
            {
                EnvironmentName = "YENI_ORTAM*",
                PackageNamePrefix = "YENI_TEZI"
            }
        };
        Assert(
            VersionManager.NormalizeProjectFamily("YENI_ORTAM_8.1", mappings) == "YENI_TEZI",
            "Jokerli ortam -> TEZI paket eslemesi uygulanmadi.");
        Assert(
            VersionManager.PackageMatchesProject(
                "yeni_tezi-6.6.0-build.0",
                "YENI_ORTAM_8.1",
                mappings),
            "Ozel TEZI paket adi ortamla eslesmedi.");

        var priorityMappings = new List<ProjectPackageMapping>
        {
            new ProjectPackageMapping { EnvironmentName = "YENI*", PackageNamePrefix = "GENEL" },
            new ProjectPackageMapping { EnvironmentName = "YENI_ORTAM_8.1", PackageNamePrefix = "OZEL" }
        };
        Assert(
            VersionManager.NormalizeProjectFamily("YENI_ORTAM_8.1", priorityMappings) == "OZEL",
            "Tam ortam eslemesi jokerli genel kuraldan once uygulanmadi.");

        var config = new AppConfig
        {
            SelectedUkb = 2,
            SerialPortName = "COM21",
            SerialPortName2 = "COM22",
            PowerSlot = 1,
            PowerSlot2 = 8,
            RecoverySlot = 7,
            RecoverySlot2 = 9,
            ValidateTeziPackageNamePrefix = false,
            VersionsRootPath = Path.Combine(Path.GetTempPath(), "UKB-Surumleri-Test"),
            ProjectPackageMappings = mappings
        };

        Type dialogType = typeof(AppConfig).Assembly.GetType(
            "SurumYakma.ConnectionSettingsForm",
            throwOnError: true);
        using var dialog = (Form)Activator.CreateInstance(
            dialogType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { config, "YENI_ORTAM_8.1", false },
            culture: null);
        TabControl tabs = Descendants(dialog).OfType<TabControl>().Single();
        Assert(tabs.TabPages.Count == 2, "UKB ve Gelismis sekmeleri olusturulmadi.");
        Assert(tabs.TabPages.Cast<TabPage>().Any(page => page.Text.Contains("Gelişmiş")),
            "Gelismis Secenekler sekmesi bulunamadi.");
        Assert(tabs.TabPages.Cast<TabPage>().Any(page => page.Text == "UKB"),
            "UKB sekmesi bulunamadi.");
        Assert(Descendants(dialog).OfType<DataGridView>().Any(),
            "Ortam -> TEZI esleme tablosu bulunamadi.");
        DataGridView mappingGrid = Descendants(dialog).OfType<DataGridView>()
            .Single(grid => grid.Columns.Contains("EnvironmentName"));
        Assert(mappingGrid.Columns["Selected"] is DataGridViewCheckBoxColumn &&
               Descendants(dialog).OfType<Button>().Any(button => button.Text == "Satır Ekle") &&
               Descendants(dialog).OfType<Button>().Any(button => button.Text == "Seçilenleri Sil"),
            "Gelismis tablo secim, satir ekleme veya silme kontrolleri eksik.");
        Assert(!mappingGrid.AllowUserToResizeColumns && !mappingGrid.AllowUserToResizeRows &&
               mappingGrid.Columns["EnvironmentName"].FillWeight ==
               mappingGrid.Columns["PackageNamePrefix"].FillWeight,
            "Gelismis tablo satir/sutunlari esit ve sabit degil.");
        ComboBox baudRateList = Descendants(dialog).OfType<ComboBox>().Single(combo =>
            combo.Items.Cast<object>().Any(item =>
                int.TryParse(Convert.ToString(item), out int baud) && baud == 115200));
        Assert(baudRateList.FlatStyle == FlatStyle.Standard && baudRateList.DrawMode == DrawMode.Normal,
            "Baud rate listesi standart cerceve ve sol hizali metin modunda degil.");
        CheckBox prefixValidation = Descendants(dialog).OfType<CheckBox>().Single(checkBox =>
            checkBox.Text.IndexOf("TEZI paket ön adını", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert(!prefixValidation.Checked,
            "TEZI on ad dogrulama secenegi kayitli kapali durumuyla acilmadi.");
        Assert(!Descendants(dialog).OfType<CheckBox>().Any(checkBox =>
                checkBox.Text.IndexOf("otomatik giriş", StringComparison.OrdinalIgnoreCase) >= 0),
            "Agdan surumde gereksiz Easy Installer otomatik giris alani gorunuyor.");
        string[] settingLabels = Descendants(dialog).OfType<Label>().Select(label => label.Text).ToArray();
        Assert(Descendants(dialog).OfType<TextBox>().Any(textBox =>
                   string.Equals(textBox.Text, config.VersionsRootPath, StringComparison.OrdinalIgnoreCase)) &&
               settingLabels.Any(label => label == "UKB Sürümleri ana klasörü"),
            "Gelismis seceneklerde manuel surum deposu yolu gorunmuyor.");
        Assert(settingLabels.Contains("Power Box IP") && settingLabels.Contains("Relay Box IP"),
            "Birlesik hedef tablosunun Power ve Relay IP alanlari olusturulmadi.");
        Assert(!settingLabels.Any(label => label.IndexOf("Yüklenecek hedef", StringComparison.OrdinalIgnoreCase) >= 0),
            "Baglanti Ayarlarinda gereksiz ikinci yukleme hedefi secimi kaldi.");
        Assert(config.GetPowerSlot(true) == 8 && config.GetRecoverySlot(true) == 9,
            "UKB2 ayri Power/Recovery slot secimi uygulanmadi.");
        Assert(config.SelectedUkb == 2 && config.SerialPortName2 == "COM22",
            "UKB2 tekli hedef yapilandirmasi dogru COM portunu korumadi.");

        NumericUpDown number = Descendants(dialog).OfType<NumericUpDown>().First();
        decimal originalNumber = number.Value;
        number.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(number, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120) });
        Assert(number.Value == originalNumber,
            "Baglanti ayarindaki sayisal deger mouse tekerlegiyle degisti.");

        ComboBox combo = Descendants(dialog).OfType<ComboBox>()
            .First(item => item.DropDownStyle == ComboBoxStyle.DropDown);
        combo.Text = "COM99";
        combo.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(combo, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120) });
        Assert(combo.Text == "COM99",
            "Baglanti ayarindaki liste degeri mouse tekerlegiyle degisti.");
    }

    private static void RunSwarmUavConfigurationTest(string testRoot)
    {
        List<UkbTargetConfig> targets = UkbTargetConfig.CreateSwarmDefaults();
        UkbTargetConfig target5 = targets.Single(target => target.UkbNumber == 5);
        target5.ComPort = "COM55";
        target5.PowerSlot = 9;
        target5.PowerChannel = 12;
        target5.PowerSecondaryChannel = 13;
        target5.RecoverySlot = 7;
        target5.RecoveryChannel = 6;
        var config = new AppConfig
        {
            SwarmModeEnabled = true,
            SelectedUkb = 5,
            SwarmUkbTargets = targets
        };

        Assert(config.GetTargetNumber(false) == 5 && config.GetSerialPort(false) == "COM55",
            "SURU IHA secili UKB5 COM hedefi cozumlenemedi.");
        Assert(config.GetPowerSlot(false) == 9 && config.GetPowerChannel(false) == 12 &&
               config.GetPowerSecondaryChannel(false) == 13,
            "SURU IHA UKB5 Power MOD/kanallari cozumlenemedi.");
        Assert(config.GetRecoverySlot(false) == 7 && config.GetRecoveryChannel(false) == 6,
            "SURU IHA UKB5 Recovery MOD/kanali cozumlenemedi.");

        Type dialogType = typeof(AppConfig).Assembly.GetType(
            "SurumYakma.ConnectionSettingsForm",
            throwOnError: true);
        using var dialog = (Form)Activator.CreateInstance(
            dialogType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { config, "SURU_IHA", false },
            culture: null);
        TabPage swarmPage = Descendants(dialog).OfType<TabPage>().Single(page => page.Text == "UKB");
        DataGridView swarmGrid = Descendants(swarmPage).OfType<DataGridView>().Single();
        Assert(swarmGrid.Rows.Count == 6 && swarmGrid.Columns.Count == 6,
            "Birlesik hedef panelinde alti UKB ve COM/Power/Recovery alanlari bulunmuyor.");
        Assert(!swarmGrid.AllowUserToResizeColumns && !swarmGrid.AllowUserToResizeRows &&
               swarmGrid.Columns.Cast<DataGridViewColumn>().Select(column => column.FillWeight).Distinct().Count() == 1,
            "UKB tablo satir/sutunlari esit ve sabit degil.");
        Assert(swarmGrid.Columns["Com"] is DataGridViewComboBoxColumn &&
               Descendants(swarmPage).OfType<Label>().Any(label => label.Text == "Baud rate") &&
               Descendants(swarmPage).OfType<ComboBox>().Any(combo =>
                   combo.Items.Cast<object>().Any(item =>
                       int.TryParse(Convert.ToString(item), out int baud) && baud == 115200)),
            "COM listesi veya ortak baud rate secimi olusturulmadi.");

        string settingsPath = Path.Combine(testRoot, "swarm-Settings.json");
        File.WriteAllText(settingsPath, "{}");
        SettingsProfileStore.Save(settingsPath, "SURU_IHA", config);
        string saved = File.ReadAllText(settingsPath);
        Assert(saved.Contains("SwarmModeEnabled") && saved.Contains("COM55") && saved.Contains("SwarmTargets"),
            "SURU IHA alti hedef ayari Settings.json profiline yazilmadi.");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }

    private static void RunHardwareProfileTest()
    {
        var config = new AppConfig { AutoDetectSerialPort = false };
        HardwareAutoConfigurator.ApplyStartupDetection(config, "KSIMSEK");
        Assert(config.PowerSlot == 1 && config.PowerChannel1 == 0 && config.PowerChannel1Secondary == 1,
            "Şimşek Power MOD1/CH0+CH1 profili uygulanmadı.");
        Assert(config.RecoverySlot == 7 && config.RecoveryChannel1 == 7,
            "Şimşek Recovery MOD7/CH7 profili uygulanmadı.");
        Assert(config.GetTarget(1).PowerSlot == 1 && config.GetTarget(1).RecoverySlot == 7,
            "Otomatik Şimşek profili birleşik UKB1 tablosuna uygulanmadı.");

        var savedProfile = new AppConfig
        {
            AutoDetectSerialPort = false,
            PowerBoxIp = "10.137.1.60",
            RelayBoxIp = "10.137.1.40",
            PowerSlot = 6,
            PowerChannel1 = 2,
            PowerChannel1Secondary = -1,
            RecoverySlot = 9,
            RecoveryChannel1 = 6
        };
        string savedSummary = HardwareAutoConfigurator.ApplyStartupDetection(
            savedProfile,
            "KSIMSEK",
            preserveConfiguredProfile: true);
        Assert(savedProfile.PowerBoxIp == "10.137.1.60" &&
               savedProfile.RelayBoxIp == "10.137.1.40" &&
               savedProfile.PowerSlot == 6 &&
               savedProfile.PowerChannel1 == 2 &&
               savedProfile.RecoverySlot == 9 &&
               savedProfile.RecoveryChannel1 == 6,
            "Settings.json Şimşek profili sabit varsayılanlarla ezildi.");
        Assert(savedSummary.Contains("Settings.json donanım profili korundu"),
            "Kayıtlı donanım profilinin korunduğu otomatik algılama özetine yazılmadı.");

        var yfyk = new AppConfig
        {
            AutoDetectSerialPort = false,
            PowerBoxIp = "192.168.1.21",
            RelayBoxIp = "192.168.1.22",
            PowerSlot = 4,
            PowerChannel1 = 6,
            RecoverySlot = 2,
            RecoveryChannel1 = 4
        };
        string yfykSummary = HardwareAutoConfigurator.ApplyStartupDetection(yfyk, "YFYK 7.2.4.X");
        Assert(yfyk.PowerBoxIp == "192.168.1.21" && yfyk.RelayBoxIp == "192.168.1.22",
            "YFYK otomatik profilinde kullanıcının MOXA IP'leri değiştirildi.");
        Assert(yfyk.PowerSlot == 8 && yfyk.PowerChannel1 == 4 && yfyk.PowerChannel1Secondary == -1,
            "YFYK UKS Power MOD8/CH4 profili uygulanmadı.");
        Assert(yfyk.RecoverySlot == 8 && yfyk.RecoveryChannel1 == 4,
            "YFYK Recovery MOD8/CH4 profili uygulanmadı.");
        Assert(yfykSummary.Contains("IP'ler korundu"),
            "YFYK otomatik profil özeti IP korumasını bildirmedi.");
    }

    private static void RunUsbBulkTimeoutParsingTest()
    {
        MethodInfo parser = typeof(FlashWorkflow).GetMethod(
            "IsUsbBulkTimeoutLine",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(parser != null, "UUU USB bulk timeout ayrıştırıcısı bulunamadı.");

        bool exactTimeout = (bool)parser.Invoke(
            null,
            new object[] { "Fail Bulk(W): LIBUSB_ERROR_TIMEOUT (-7)(4.322s)" });
        bool readTimeout = (bool)parser.Invoke(
            null,
            new object[] { "Fail Bulk(R): LIBUSB_ERROR_TIMEOUT (-7)(20s)" });
        bool unrelatedTimeout = (bool)parser.Invoke(
            null,
            new object[] { "Wait for Known USB Device Appear..." });
        Assert(exactTimeout && readTimeout && !unrelatedTimeout,
            "USB bulk timeout yeniden deneme filtresi Bulk(W)/Bulk(R) hatalarını doğru seçmiyor.");
    }

    private static void RunModernUiSmokeTest(bool networkMode)
    {
        using var form = new Form1();
        Type type = typeof(Form1);
        int originalClientWidth = form.ClientSize.Width;
        var designerProgress = (ProgressBar)type.GetField("progressBar1", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        int originalRightWidth = originalClientWidth - designerProgress.Left - 28;
        var uiConfig = new AppConfig { NetworkInstallMode = networkMode, SelectedUkb = 2 };
        uiConfig.MigrateLegacyTargetsToUnifiedTable();
        type.GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, uiConfig);
        type.GetField("_projectName", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, "KSIMSEK");
        type.GetMethod("AddRuntimeControls", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        type.GetMethod("ConfigureProductionUi", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);

        var header = (Panel)type.GetField("pnlModernHeader", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var leftCard = (Panel)type.GetField("pnlLeftCard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var rightCard = (Panel)type.GetField("pnlRightCard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var settings = (Button)type.GetField("btnConnectionSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var helpButton = (Button)type.GetField("btnHelpTab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var workflowButton = (Button)type.GetField("btnWorkflowTab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var helpPanel = (Panel)type.GetField("pnlHelp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var helpGuide = (RichTextBox)type.GetField("txtHelpGuide", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var projectList = (ComboBox)type.GetField("projectList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var platformLabel = (Label)type.GetField("label2", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var versionList = (ComboBox)type.GetField("surumList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var versionLabel = (Label)type.GetField("label3", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var startButton = (Button)type.GetField("UKB1Yak", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var operationLabel = (Label)type.GetField("lblHello", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var targetLabel = (Label)type.GetField("lblTargetSelector", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var targetList = (ComboBox)type.GetField("cmbTargetSelector", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var relayDetails = (Label)type.GetField("RelayBoxIPLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var powerDetails = (Label)type.GetField("PowerBoxIPLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var connectionSummary = (Label)type.GetField("lblConnectionSummary", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var stageProgress = (Label)type.GetField("lblStageProgress", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var progressBar = (ProgressBar)type.GetField("progressBar1", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        Assert(header != null && leftCard != null && rightCard != null, "Modern başlık/kartlar oluşturulmadı.");
        Assert(settings.Parent == header, "Bağlantı Ayarları modern başlığa taşınmadı.");
        Assert(helpButton.Parent == header && workflowButton.Parent == header,
            "Yukleme/Yardim sekmeleri modern basliga eklenmedi.");
        Assert(helpPanel != null && helpGuide != null,
            "Yardim paneli veya kullanim kilavuzu olusturulmadi.");
        Assert(helpPanel.Dock == DockStyle.Fill,
            "Yardim sekmesi ana ekrani tamamen kaplayan katman olarak ayarlanmadi.");
        Assert(helpGuide.Text.Contains("İlk kullanımdan önce") &&
               helpGuide.Text.Contains("Bağlantı Ayarları") &&
               helpGuide.Text.Contains("Settings.json") &&
               helpGuide.Text.Contains("Sık karşılaşılan sorunlar") &&
               helpGuide.Text.Contains("Windows Defender") &&
               helpGuide.Text.Contains("OTG") &&
               helpGuide.Text.Contains("USB-NCM"),
            "Yardim kilavuzunun zorunlu bolumleri eksik.");
        type.GetMethod("ShowHelpView", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { true });
        Assert((bool)type.GetField("_helpViewActive", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form),
            "Yardim sekmesi etkinlestirilemedi.");
        type.GetMethod("ShowHelpView", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { false });
        Assert(leftCard.Right < rightCard.Right && leftCard.Top == rightCard.Top, "Modern iki kolon yerleşimi hatalı.");
        Assert(leftCard.Bottom < rightCard.Bottom,
            "Sol kart gereksiz boş alanla sağ işlem kartı kadar uzatılıyor.");
        Assert(form.ClientSize.Width < originalClientWidth &&
               Math.Abs((rightCard.Width - 32) - originalRightWidth) <= 2,
            "Pencere küçültülürken sağ işlem kartının genişliği korunmadı.");
        Assert(!form.MaximizeBox && form.FormBorderStyle == FormBorderStyle.FixedSingle &&
               form.MinimumSize == form.MaximumSize,
            "Uygulama penceresi sabit boyutta kilitlenmedi.");
        Assert(form.Opacity == 1, "Modern düzen tamamlandıktan sonra pencere görünür yapılmadı.");
        Assert(projectList.Enabled && !projectList.TabStop, "Otomatik proje alanı okunaklı salt seçim görünümünde değil.");
        Assert(platformLabel.Text == "Platform", "Otomatik proje başlığı Platform olarak güncellenmedi.");
        Assert(versionList.DrawMode == DrawMode.OwnerDrawFixed && versionList.FlatStyle == FlatStyle.Standard,
            "Sürüm listesi yüksek kontrastlı çizim modunda değil.");
        Assert(versionList.BackColor != System.Drawing.Color.White,
            "Sürüm listesi kart arka planından ayırt edilemiyor.");

        var sourceLabel = (Label)type.GetField("label1", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var sourcePath = (TextBox)type.GetField("textBox2", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var sourceButton = (Button)type.GetField("button2", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        Assert(!sourceLabel.Visible && !sourcePath.Visible && !sourceButton.Visible,
            "Ana ekrandaki manuel surum yolu secimi kaldirilmadi.");

        Assert(versionLabel.Text == "1. Yüklenecek Sürüm",
            "1. Yuklenecek Surum etiketi ayarlanmadi.");
        Assert(versionList.TabStop && versionList.DropDownStyle == ComboBoxStyle.DropDownList,
            "Surum kaynagi altindaki secilebilir surum listesi ayarlanmadi.");
        Assert(versionLabel.Bottom <= versionList.Top && versionList.Bottom <= targetLabel.Top &&
               targetLabel.Bottom <= targetList.Top && targetList.Bottom <= operationLabel.Top,
            "Surum, UKB hedefi ve yukleme islemi adimlari dogru siralanmadi.");
        Assert(targetLabel.Text == "2. Yüklenecek UKB" && targetList.Items.Count == 6 &&
               targetList.SelectedItem?.ToString() == "UKB2",
            "Ana ekrandaki UKB1-UKB6 hedef listesi dogru olusturulmadi.");
        Assert(operationLabel.Text == "3. Yükleme İşlemi" && operationLabel.Bottom <= startButton.Top,
            "3. Yukleme Islemi basligi yukleme butonunun ustunde degil.");
        type.GetMethod("UpdateMainActionButtonTexts", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        Assert(startButton.Text.Contains("UKB2"),
            "Ana yukleme butonu Baglanti Ayarlarinda secilen UKB2 hedefini gostermiyor.");
        type.GetField("_safeOutputsReady", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
        type.GetMethod("SetButtonsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { true });
        Assert(!startButton.Enabled, "Surum bulunmadigi halde ana baslatma butonu etkinlesti.");
        Assert(!relayDetails.AutoSize && !powerDetails.AutoSize,
            "MOXA IP/slot/kanal ayrinti alanlari tek satir icin sabitlenmedi.");
        Assert(platformLabel.BackColor == form.BackColor &&
               relayDetails.BackColor == form.BackColor &&
               powerDetails.BackColor == form.BackColor &&
               stageProgress.BackColor == form.BackColor,
            "Ana arayuz etiketlerinde arka plan renk patlamasi olusturan farkli renkler kaldi.");
        type.GetMethod("UpdateConnectionSummary", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        Assert(connectionSummary.Text.Contains("UKB2:") && connectionSummary.Text.Contains("COM23") &&
               !connectionSummary.Text.Contains("Power") &&
               !connectionSummary.Text.Contains("Recovery"),
            "Ust baslikta yalnizca secili COM bilgisi gosterilmiyor.");
        Assert(stageProgress.Top >= progressBar.Bottom,
            "Asama bilgisi ilerleme cubugunun altinda konumlanmadi.");
        Assert(progressBar.Left > rightCard.Left && progressBar.Right < rightCard.Right,
            "Ilerleme cubugu sag kartin disina tasiyor.");
        type.GetMethod("SetNetworkStage", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { 6, "Yerel Surum Sunucusu Baslatiliyor" });
        Assert(progressBar.Maximum == 1200 && progressBar.Value == 500,
            "Ilerleme cubugu 12 esit asamaya bolunmedi.");
        Assert(stageProgress.Text.Contains("Aşama 6/12") &&
               stageProgress.Text.Contains("Yerel Surum Sunucusu"),
            "Guncel asama adi ve X/12 bilgisi gosterilmiyor.");
        type.GetMethod("CompleteNetworkProgress", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { "Tamamlandi" });
        Assert(progressBar.Value == progressBar.Maximum && stageProgress.Text.Contains("12/12"),
            "Tum asamalar tamamlandiginda ilerleme cubugu tam dolmadi.");

        var cancellation = new CancellationTokenSource();
        type.GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, cancellation);
        type.GetField("_criticalPhase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
        type.GetMethod("CancelActiveOperation", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        Assert(cancellation.IsCancellationRequested,
            "Iptal butonu kritik aktarim asamasinda islemi durdurmadi.");
        type.GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, null);
        type.GetField("_criticalPhase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, false);
        cancellation.Dispose();

        type.GetField("_networkFeedReady", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
        type.GetMethod("ResetNetworkUiAfterStoppedRun", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        Assert(!(bool)type.GetField("_networkFeedReady", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) &&
               progressBar.Value == progressBar.Minimum &&
               stageProgress.Text == "İşlem başlatılmadı" &&
               settings.Enabled,
            "Hata/iptal sonrasında ağ hazırlığı, ilerleme ve ayar kontrolleri başlangıç durumuna dönmedi.");

        string snapshotPath = Environment.GetEnvironmentVariable("SURUM_UI_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            form.ClientSize = new Size(1500, 760);
            form.CreateControl();
            form.PerformLayout();
            form.Show();
            Application.DoEvents();
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.ClientSize));
            image.Save(snapshotPath);
            form.Hide();
        }
        SaveHelpSnapshot(form, type);
    }

    private static void RunEnglishUiSmokeTest()
    {
        Localization.SetLanguage("EN");
        using var form = new Form1();
        Type type = typeof(Form1);
        var config = new AppConfig
        {
            NetworkInstallMode = true,
            SelectedUkb = 1,
            UiLanguage = "EN"
        };
        config.MigrateLegacyTargetsToUnifiedTable();
        type.GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, config);
        type.GetField("_projectName", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, "KSIMSEK");
        type.GetMethod("AddRuntimeControls", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        type.GetMethod("ConfigureProductionUi", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);

        var versionLabel = (Label)type.GetField("label3", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);
        var targetLabel = (Label)type.GetField("lblTargetSelector", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);
        var operationLabel = (Label)type.GetField("lblHello", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);
        var status = (Label)type.GetField("lblStatus", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);
        var start = (Button)type.GetField("UKB1Yak", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);
        var projects = (ComboBox)type.GetField("projectList", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(form);

        status.Text = "Moxa cihazlarına bağlanılıyor...";
        start.Text = "UKB1 — SÜRÜM YÜKLEMEYİ BAŞLAT";
        form.Text = "Sürüm Yükleme v-1.0.2";
        projects.Items.Add("KSIMSEK (Varsayılan)");

        Assert(form.Text == "Version Installation v-1.0.2" &&
               versionLabel.Text == "1. Version to Install" &&
               targetLabel.Text == "2. Target UKB" &&
               operationLabel.Text == "3. Installation" &&
               status.Text == "Connecting to Moxa devices..." &&
               start.Text == "UKB1 — START VERSION INSTALLATION" &&
               projects.GetItemText(projects.Items[0]) == "KSIMSEK (Default)",
            "Ana ekranda Turkce veya karisik Ingilizce metin kaldi.");
        Localization.SetLanguage("TR");
    }

    private static void RunSingleTargetUiSmokeTest()
    {
        using var form = new Form1();
        Type type = typeof(Form1);
        var targetConfig = new AppConfig
        {
            NetworkInstallMode = true,
            SelectedUkb = 2,
            SerialPortName = "COM21",
            SerialPortName2 = "COM22"
        };
        targetConfig.MigrateLegacyTargetsToUnifiedTable();
        type.GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, targetConfig);
        type.GetField("_projectName", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, "KSIMSEK");
        type.GetMethod("AddRuntimeControls", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        type.GetMethod("ConfigureProductionUi", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);

        var start = (Button)type.GetField("UKB1Yak", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var summary = (Label)type.GetField("lblConnectionSummary", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        var overall = (ProgressBar)type.GetField("progressBar1", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);

        type.GetMethod("UpdateMainActionButtonTexts", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        type.GetMethod("UpdateConnectionSummary", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        Assert(start.Text.Contains("UKB2") && !start.Text.Contains("PARALEL"),
            "Ana yukleme dugmesi secili UKB2 hedefini gostermiyor.");
        Assert(summary.Text.Contains("UKB2") && summary.Text.Contains("COM22") && !summary.Text.Contains("COM21"),
            "Baslikta yalniz secili UKB2 COM bilgisi gosterilmedi.");
        MethodInfo getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
        Func<Control, bool> explicitlyVisible = control => (bool)getState.Invoke(control, new object[] { 2 });
        Assert(explicitlyVisible(overall), "Tekli yukleme ilerleme cubugu gorunur degil.");
        Assert(type.GetField("progressUkb1", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
               type.GetField("progressUkb2", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
               type.GetMethod("RunParallelNetworkConnectivityAsync", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "Paralel yukleme kodu veya arayuz kontrolleri kaynakta kaldi.");
    }

    private static void RunOtgWaitStateParsingTest()
    {
        MethodInfo method = typeof(FlashWorkflow).GetMethod(
            "UpdateKnownUsbWaitState",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(method != null, "OTG bekleme durum ayristiricisi bulunamadi.");

        object[] waitingArgs = { "Wait for Known USB Device Appear", 0, 0L };
        method.Invoke(null, waitingArgs);
        Assert((int)waitingArgs[1] == 1, "UUU OTG bekleme satiri algilanamadi.");
        Assert((long)waitingArgs[2] > 0, "OTG uyari sayaci gercek bekleme aninda baslatilmadi.");

        object[] cfgOkayArgs = { "Okay (0s)", 1, waitingArgs[2] };
        method.Invoke(null, cfgOkayArgs);
        Assert((int)cfgOkayArgs[1] == 1,
            "UUU CFG cevabi yanlislikla USB cihaz bulundu olarak kabul edildi.");
        Assert((long)cfgOkayArgs[2] == (long)waitingArgs[2],
            "UUU CFG cevabi OTG bekleme sayacini yanlislikla sifirladi.");

        object[] resumedArgs = { ">Start Cmd:SDPS: boot -f imx-boot", 1, cfgOkayArgs[2] };
        method.Invoke(null, resumedArgs);
        Assert((int)resumedArgs[1] == 0,
            "UUU gercek transfer komutundan sonra bekleme durumu temizlenmedi.");
        Assert((long)resumedArgs[2] == 0,
            "UUU gercek transferinden sonra OTG bekleme sayaci temizlenmedi.");
    }

    private static void RunRecoveryToolSelfHealTest(string testRoot)
    {
        string recoveryDirectory = Path.Combine(testRoot, "recovery-self-heal");
        MethodInfo method = typeof(FlashWorkflow).GetMethod(
            "EnsureRecoveryTools",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(string) },
            null);
        Assert(method != null, "Recovery self-heal metodu bulunamadi.");

        string firstStatus = InvokeRecoveryToolSelfHeal(method, recoveryDirectory);
        string toolPath = Path.Combine(recoveryDirectory, "uuu.exe");
        string scriptPath = Path.Combine(recoveryDirectory, "uuu.auto");
        Assert(firstStatus == "REPAIRED", "Eksik bundled recovery dosyalari onarilmadi.");
        Assert(File.Exists(toolPath) && File.Exists(scriptPath),
            "Bundled recovery dosyalari gecici hedefe cikarilmadi.");
        Assert(GetSha256(toolPath) == "F6B76A6246BEFABEADFEBDC1CBFE58F35939596CAF7B78717CEAB599B0C85027",
            "Cikarilan uuu.exe beklenen SHA256 degerinde degil.");

        byte[] originalScript = File.ReadAllBytes(scriptPath);
        DateTime toolWriteTime = File.GetLastWriteTimeUtc(toolPath);
        DateTime scriptWriteTime = File.GetLastWriteTimeUtc(scriptPath);
        string secondStatus = InvokeRecoveryToolSelfHeal(method, recoveryDirectory);
        Assert(secondStatus == "OK", "Ayni bundled recovery dosyalari gereksiz yere yenilendi.");
        Assert(File.GetLastWriteTimeUtc(toolPath) == toolWriteTime &&
               File.GetLastWriteTimeUtc(scriptPath) == scriptWriteTime,
            "Icerigi ayni recovery dosyasinin yazma zamani degisti.");

        File.WriteAllText(toolPath, "corrupt");
        File.WriteAllText(scriptPath, "corrupt");
        string repairedStatus = InvokeRecoveryToolSelfHeal(method, recoveryDirectory);
        Assert(repairedStatus == "REPAIRED", "Bozuk recovery dosyalari bundled kaynaklardan onarilmadi.");
        Assert(GetSha256(toolPath) == "F6B76A6246BEFABEADFEBDC1CBFE58F35939596CAF7B78717CEAB599B0C85027",
            "Onarilan uuu.exe beklenen SHA256 degerinde degil.");
        Assert(File.ReadAllBytes(scriptPath).SequenceEqual(originalScript),
            "uuu.auto bundled kaynak icerigine geri yuklenmedi.");
        Assert(Directory.GetFiles(recoveryDirectory, "*.tmp").Length == 0,
            "Atomik restore gecici dosya birakti.");
    }

    private static string InvokeRecoveryToolSelfHeal(MethodInfo method, string recoveryDirectory)
    {
        try
        {
            return (string)method.Invoke(null, new object[] { recoveryDirectory });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static string GetSha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void SaveHelpSnapshot(Form1 form, Type type)
    {
        string helpSnapshotPath = Environment.GetEnvironmentVariable("SURUM_UI_HELP_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(helpSnapshotPath))
            return;

        form.ClientSize = new Size(1500, 760);
        form.CreateControl();
        form.PerformLayout();
        form.Show();
        type.GetMethod("ShowHelpView", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { true });
        Application.DoEvents();
        using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.ClientSize));
        image.Save(helpSnapshotPath);
        form.Hide();
    }

    private static void RunEasyInstallerEvidenceTest()
    {
        MethodInfo method = typeof(Form1).GetMethod(
            "WaitForEasyInstallerEvidenceAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(method != null, "Easy Installer kanıt seçici bulunamadı.");

        var serialNeverCompletes = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> mdnsResult = (Task<string>)method.Invoke(
            null,
            new object[]
            {
                serialNeverCompletes.Task,
                Task.CompletedTask,
                TimeSpan.FromSeconds(1),
                CancellationToken.None
            });
        Assert(
            mdnsResult.GetAwaiter().GetResult().StartsWith("MDNS:", StringComparison.Ordinal),
            "Seri çıktı yokken hedef mDNS sorgusu Easy Installer kanıtı olarak kabul edilmedi.");

        var mdnsNeverCompletes = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> serialResult = (Task<string>)method.Invoke(
            null,
            new object[]
            {
                Task.FromResult("Toradex Easy Installer"),
                mdnsNeverCompletes.Task,
                TimeSpan.FromSeconds(1),
                CancellationToken.None
            });
        Assert(
            serialResult.GetAwaiter().GetResult().StartsWith("SERIAL:", StringComparison.Ordinal),
            "Seri konsol kanıtı Easy Installer hazır durumu olarak kabul edilmedi.");
    }

    private static void RunTeziHttpServerTest(string testRoot)
    {
        string package = Path.Combine(testRoot, "http-package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "image.json"), "{\"config_format\": 1}");
        byte[] payload = Encoding.ASCII.GetBytes("0123456789ABCDEF");
        File.WriteAllBytes(Path.Combine(package, "payload.bin"), payload);

        using var server = new TeziHttpServer("127.0.0.1", 0);
        var startedRequests = new List<string>();
        var completedRequests = new List<string>();
        server.RequestStarted += path => startedRequests.Add(path);
        server.RequestCompleted += path => completedRequests.Add(path);
        server.SetPackageRoot(package);
        server.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        string health = client.GetStringAsync(server.BaseUrl + "/health").GetAwaiter().GetResult().Trim();
        Assert(health == TeziHttpServer.HealthResponse, "HTTP health yanıtı doğrulanamadı.");

        using HttpResponseMessage feedResponse = client.GetAsync(server.BaseUrl + "/image_list.json")
            .GetAwaiter().GetResult();
        string feed = feedResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert(feed.Contains("package/image.json"), "TEZI image_list.json paket adresi hatalı.");
        Assert(feedResponse.Headers.CacheControl != null &&
               feedResponse.Headers.CacheControl.NoStore &&
               feedResponse.Headers.CacheControl.MaxAge == TimeSpan.Zero,
            "TEZI JSON yanıtı resmi no-store,max-age=0 önbellek başlığını taşımıyor.");

        byte[] downloaded = client.GetByteArrayAsync(server.BaseUrl + "/package/payload.bin")
            .GetAwaiter().GetResult();
        Assert(downloaded.SequenceEqual(payload), "HTTP paket dosyası değişmeden aktarılamadı.");
        SpinWait.SpinUntil(
            () => completedRequests.Contains("/package/payload.bin"),
            TimeSpan.FromSeconds(1));
        Assert(startedRequests.Contains("/package/payload.bin"), "Payload başlangıç olayı üretilmedi.");
        Assert(completedRequests.Contains("/image_list.json"), "Feed tamamlanma olayı üretilmedi.");
        Assert(completedRequests.Contains("/package/payload.bin"), "Payload tamamlanma olayı üretilmedi.");

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/package/payload.bin");
        request.Headers.Range = new RangeHeaderValue(4, 7);
        using HttpResponseMessage response = client.Send(request);
        byte[] range = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        Assert(response.StatusCode == HttpStatusCode.PartialContent, "HTTP range isteği 206 dönmedi.");
        Assert(Encoding.ASCII.GetString(range) == "4567", "HTTP range içeriği hatalı.");

        HttpResponseMessage traversal = client.GetAsync(server.BaseUrl + "/package/%2e%2e/image.json")
            .GetAwaiter().GetResult();
        Assert(traversal.StatusCode == HttpStatusCode.NotFound, "Dizin geçişi isteği engellenmedi.");
        traversal.Dispose();

        using var occupiedPort = new TcpListener(IPAddress.Loopback, 0);
        occupiedPort.Start();
        int blockedPort = ((IPEndPoint)occupiedPort.LocalEndpoint).Port;
        using var fallbackServer = TeziHttpServer.StartWithFallback(
            "127.0.0.1",
            blockedPort,
            package);
        Assert(fallbackServer.Port != blockedPort, "Occupied HTTP port fallback failed.");
        string fallbackHealth = client.GetStringAsync(fallbackServer.BaseUrl + "/health")
            .GetAwaiter().GetResult().Trim();
        Assert(fallbackHealth == TeziHttpServer.HealthResponse, "Fallback HTTP port health failed.");
    }

    private static void RunNetworkPackagePreparationTest(string testRoot)
    {
        string repository = Path.Combine(testRoot, "network-source");
        string source = CreateTeziPackage(
            repository,
            "tuk-Tezi_6.6.0-devel-20260729120000+build.0",
            "TUK 4.0.16.99");
        string sourceJson = Path.Combine(source, "image.json");
        File.WriteAllText(
            sourceJson,
            "{\n  \"name\": \"TUK 4.0.16.99\",\n  \"autoinstall\": false,\n" +
            "  \"license\": \"license.html\",\n  \"license_title\": \"Lisans\"\n}\n");
        File.WriteAllText(Path.Combine(source, "license.html"), "örnek lisans");

        string stagingRoot = Path.Combine(testRoot, "network-staging");
        string prepared = new VersionManager(new AppConfig())
            .PrepareVersionForNetwork(source, stagingRoot);

        string preparedJson = File.ReadAllText(Path.Combine(prepared, "image.json"));
        Assert(preparedJson.Contains("\"autoinstall\": true"), "Ağ kopyası autoinstall=true olmadı.");
        Assert(!preparedJson.Contains("\"license\""), "Ağ kopyasındaki lisans referansı kaldırılmadı.");
        Assert(File.ReadAllText(sourceJson).Contains("\"autoinstall\": false"), "Kaynak image.json değiştirildi.");
        Assert(File.ReadAllText(sourceJson).Contains("\"license\""), "Kaynak lisans referansı değiştirildi.");
        string wrapup = File.ReadAllText(Path.Combine(prepared, "wrapup.sh"));
        Assert(wrapup.IndexOf("poweroff -f", StringComparison.Ordinal) <
               wrapup.IndexOf("exit 0", StringComparison.Ordinal),
            "Ağ kopyasında poweroff -f doğru konuma eklenmedi.");
    }

    private static void RunTeziMdnsPacketTest()
    {
        byte[] packet = TeziMdnsAdvertiser.BuildAnnouncementPacketForTest(
            "192.168.11.221",
            8088,
            "/image_list.json");
        string text = Encoding.ASCII.GetString(packet);
        Assert(packet.Length > 100, "mDNS duyuru paketi beklenenden kısa.");
        Assert(text.Contains("_tezi"), "mDNS paketinde _tezi servisi yok.");
        Assert(text.Contains("Custom Toradex Easy Installer Feed"),
            "mDNS servis örnek adı Toradex resmî yayın adıyla eşleşmiyor.");
        Assert(text.Contains("name=Custom Toradex Easy Installer Feed"),
            "mDNS TXT name alanı servis örnek adıyla eşleşmiyor.");
        Assert(text.Contains("path=/image_list.json"), "mDNS paketinde feed yolu yok.");
        Assert(text.Contains("enabled=1"), "mDNS paketinde enabled TXT alanı yok.");
        Assert(packet.Contains((byte)192) && packet.Contains((byte)221), "mDNS paketinde IPv4 adresi yok.");
        Assert(packet[6] == 0 && packet[7] == 1, "mDNS PTR yanıt sayısı 1 değil.");
        Assert(packet[10] == 0 && packet[11] == 3, "mDNS SRV/TXT/A ek kayıt sayısı 3 değil.");
    }

    private static void RunTeziVncRefreshPacketTest()
    {
        byte[] sequence = TeziVncClient.BuildRefreshKeySequenceForTest();
        Assert(sequence.Length == 16, "VNC tus dizisi 16 byte degil.");
        Assert(sequence[0] == 4 && sequence[1] == 1 && sequence[7] == 0x72,
            "VNC 'r' tus basma paketi gecersiz.");
        Assert(sequence[8] == 4 && sequence[9] == 0 && sequence[15] == 0x72,
            "VNC 'r' tus birakma paketi gecersiz.");
    }

    private static void RunNaturalVersionSortTest()
    {
        string[] versions =
        {
            "ANKA_X_5.9",
            "ANKA_X_5.10",
            "ANKA_X_4.20",
            "ANKA_X_10.1"
        };

        string[] sorted = versions
            .OrderByDescending(name => name, NaturalVersionNameComparer.Instance)
            .ToArray();

        Assert(
            sorted.SequenceEqual(new[]
            {
                "ANKA_X_10.1",
                "ANKA_X_5.10",
                "ANKA_X_5.9",
                "ANKA_X_4.20"
            }),
            "Doğal sürüm sıralaması beklenen sonucu vermedi.");
    }

    private static void RunFriendlyVersionFolderSelectionTest(string testRoot)
    {
        string repository = Path.Combine(testRoot, "TUK");
        string oldWrapper = Path.Combine(repository, "TUK_7.1.12.6.0");
        string newWrapper = Path.Combine(repository, "TUK_7.1.12.26.0");
        string oldPackage = CreateTeziPackage(
            oldWrapper,
            "tuk-Tezi_6.6.0-devel-20260701120000+build.0",
            "TUK 7.1.12.6.0");
        string newPackage = CreateTeziPackage(
            newWrapper,
            "tuk-Tezi_6.6.0-devel-20260702120000+build.0",
            "TUK 7.1.12.26.0");

        VersionListResult versions = VersionManager.GetSelectableTeziVersions(repository, "TUK");

        Assert(versions.Names.SequenceEqual(new[] { "TUK_7.1.12.26.0", "TUK_7.1.12.6.0" }),
            "Dış sürüm klasörleri kullanıcı dostu adla en yeniden eskiye listelenmedi.");
        Assert(versions.Paths.SequenceEqual(new[] { newPackage, oldPackage }),
            "Görünen sürüm adı içteki gerçek TEZI paket yoluyla eşleşmedi.");
    }

    private static void RunAutomaticPlatformFolderTest(string testRoot)
    {
        string root = Path.Combine(testRoot, "automatic-platform", "UKB Sürümleri");
        string tolerantFolder = Path.Combine(root, "KŞİMŞEK");
        Directory.CreateDirectory(tolerantFolder);
        var config = new AppConfig { VersionsRootPath = root };

        Assert(config.GetPlatformVersionsPath("k-simsek") == tolerantFolder,
            "Turkce/Ingizlice karakter, ayirici veya harf boyutu toleransi uygulanmadi.");
        Assert(AppConfig.NormalizePlatformFolderKey("TUK_VTOL") ==
               AppConfig.NormalizePlatformFolderKey("tuk-vtol"),
            "Alt cizgi ve tire farki normalize edilmedi.");
        Assert(new AppConfig().GetVersionsRootPath().EndsWith(
                "UKB Sürümleri",
                StringComparison.OrdinalIgnoreCase),
            "Varsayilan UKB Surumleri ana klasoru masaustu altinda tanimlanmadi.");
    }

    private static void RunFlatTeziRepositoryAndProjectAliasTest(string testRoot)
    {
        string repository = Path.Combine(testRoot, "flat-tezi-repository");
        string older = CreateTeziPackage(repository, "tuk-Tezi_6.6.0-devel-20260721120000+build.0", "TUK 4.0.16.90");
        string newer = CreateTeziPackage(repository, "tuk-Tezi_6.6.0-devel-20260722133452+build.0", "TUK 4.0.16.91");
        CreateTeziPackage(repository, "vtuk-Tezi_6.6.0-devel-20260723133452+build.0", "VTUK 4.0.16.91");
        string ks = CreateTeziPackage(repository, "ks-Tezi_6.6.0-devel-20260724133452+build.0", "KS 4.0.16.91");
        string wcc = CreateTeziPackage(repository, "wcc-Tezi_6.6.0-devel-20260717053519+build.0", "WCC 2.0.10.0.4 (PREEMPT_RT)");

        string[] tukPackages = VersionManager.GetTeziPackagePaths(repository, "TUK");
        Assert(tukPackages.SequenceEqual(new[] { newer, older }), "Düz TUK TEZI paketleri en yeniden eskiye listelenmedi.");
        Assert(VersionManager.GetTeziPackagePaths(repository, "VTUK").Length == 1,
            "VTUK paketi TUK paketlerinden bağımsız filtrelenemedi.");
        Assert(VersionManager.GetTeziPackagePaths(repository, "KSIMSEK").Single() == ks,
            "KSIMSEK ortam değişkeni KS paket ailesine eşlenemedi.");
        Assert(VersionManager.GetTeziPackagePaths(repository, "YFYK 7.2.4.X").Single() == wcc,
            "YFYK ortam değişkeni WCC paket ailesine eşlenemedi.");
        Assert(VersionManager.GetTeziPackagePaths(repository, "YFYK 7.99.DEGISEBILIR").Single() == wcc,
            "YFYK değişken sürüm eki WCC paket eşlemesini bozdu.");
        Assert(VersionManager.IsYfykProject("YFYK 7.2.4.X") &&
               !VersionManager.IsYfykProject("TUK"),
            "YFYK'ye özel davranış diğer projelerden ayrılamadı.");
        Assert(VersionManager.PackageMatchesProject("WCC 2.0.10.0.4 (PREEMPT_RT)", "YFYK 7.2.4.X"),
            "YFYK ortam değişkeni WCC image adına eşlenemedi.");
        string prefixedWcc = CreateTeziPackage(repository,
            "yfyk_wcc-Tezi_6.6.0-devel-20260121120252+build.0",
            "WCC 2.0.7.0.3 (PREEMPT_RT)");
        Assert(VersionManager.TeziPackageMatchesProject(prefixedWcc, "YFYK 7.2.4.X"),
            "YFYK ön ekli WCC paketi image.json eşleşmesiyle doğrulanamadı.");
        Assert(VersionManager.IsTeziPackage(newer), "Geçerli TEZI paketi algılanamadı.");
        Assert(VersionManager.GetExpectedOfpVersion(newer) == "4.0.16.91",
            "Beklenen OFP sürümü image.json name alanından okunamadı.");
        Assert(UkbSerialMonitor.ExtractOfpVersion("#### OFP Version 4.0.16.91 ####") == "4.0.16.91",
            "Seri OFP Version satırı ayrıştırılamadı.");
    }

    private static string CreateTeziPackage(string root, string folderName, string imageName)
    {
        string path = Path.Combine(root, folderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "image.json"),
            "{\n  \"name\": \"" + imageName + "\",\n  \"autoinstall\": false\n}\n");
        File.WriteAllText(Path.Combine(path, "prepare.sh"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(path, "wrapup.sh"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(path, "payload.bin"), folderName);
        return path;
    }

    private static void RunSessionLoggerAndCheckpointTest(string testRoot)
    {
        string logPath = Path.Combine(testRoot, "logs", "surumyakma.log");
        int uiEventCount = 0;
        Action<LogEntry> handler = entry => uiEventCount++;
        Logger.MessageWritten += handler;
        try
        {
            Logger.Initialize(logPath);
            Logger.Info("görünür test mesajı");
            Logger.Checkpoint("TEST_STAGE", "OK", "step=1");

            Assert(File.Exists(Logger.CurrentSessionLogPath), "Oturuma özel log dosyası oluşturulmadı.");
            string sessionLog = File.ReadAllText(Logger.CurrentSessionLogPath);
            Assert(sessionLog.Contains("[INFO] görünür test mesajı"), "Görünür terminal mesajı oturum loguna yazılmadı.");
            Assert(sessionLog.Contains("[CHECKPOINT] id=TEST_STAGE; status=OK; step=1"),
                "Checkpoint oturum loguna yazılmadı.");
            Assert(uiEventCount == 1, "Checkpoint UI terminaline gönderildi.");
        }
        finally
        {
            Logger.MessageWritten -= handler;
        }
    }

    private static void RunManifestAndFirewallRuleTest()
    {
        string manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "SurumYakma",
            "app.manifest"));
        Assert(File.Exists(manifestPath), "app.manifest kaynak dosyası bulunamadı.");
        string manifest = File.ReadAllText(manifestPath);
        Assert(manifest.Contains("requireAdministrator"),
            "Manifest requireAdministrator yürütme seviyesini istemiyor.");

        string executablePath = Path.Combine("C:\\Program Files", "Sürüm Yükleme", "Sürüm Yükleme.exe");
        var successfulRunner = new RecordingFirewallCommandRunner(
            new FirewallCommandResult(1, "No rules match."),
            new FirewallCommandResult(0, "Ok."));
        FirewallRuleHelper.EnsureInboundAllowRule(executablePath, successfulRunner);

        Assert(successfulRunner.Calls.Count == 2,
            "Firewall helper silme ve ekleme komutlarını sırasıyla çalıştırmadı.");
        Assert(successfulRunner.Calls.All(call => call.FileName == "netsh.exe"),
            "Firewall helper netsh.exe dışında bir süreç çalıştırdı.");
        Assert(successfulRunner.Calls[0].Arguments.SequenceEqual(new[]
        {
            "advfirewall", "firewall", "delete", "rule",
            "name=" + FirewallRuleHelper.RuleName,
            "dir=in"
        }), "Firewall silme komut argümanları beklenen güvenli biçimde değil.");
        Assert(successfulRunner.Calls[1].Arguments.SequenceEqual(new[]
        {
            "advfirewall", "firewall", "add", "rule",
            "name=" + FirewallRuleHelper.RuleName,
            "dir=in",
            "action=allow",
            "program=" + Path.GetFullPath(executablePath),
            "enable=yes",
            "profile=any",
            "protocol=any"
        }), "Firewall ekleme komutu yalnız EXE yoluna bağlı, inbound ve port bağımsız değil.");

        LogEntry warning = null;
        Action<LogEntry> handler = entry =>
        {
            if (entry.Level == "WARN" && entry.Message.Contains("exitCode=5"))
                warning = entry;
        };
        Logger.MessageWritten += handler;
        try
        {
            var failingRunner = new RecordingFirewallCommandRunner(
                new FirewallCommandResult(0),
                new FirewallCommandResult(5, "", "Access denied"));
            FirewallRuleHelper.EnsureInboundAllowRule(executablePath, failingRunner);
        }
        finally
        {
            Logger.MessageWritten -= handler;
        }

        Assert(warning != null && warning.Message.Contains("Access denied") &&
               warning.Message.Contains(Path.GetFullPath(executablePath)),
            "Firewall ekleme hatası uygulamayı çökertmeden ayrıntılı WARN kaydı üretmedi.");
    }

    private sealed class RecordingFirewallCommandRunner : IFirewallCommandRunner
    {
        private readonly Queue<FirewallCommandResult> _results;

        public RecordingFirewallCommandRunner(params FirewallCommandResult[] results)
        {
            _results = new Queue<FirewallCommandResult>(results);
        }

        public List<FirewallCommandCall> Calls { get; } = new List<FirewallCommandCall>();

        public FirewallCommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add(new FirewallCommandCall(fileName, arguments.ToArray()));
            return _results.Dequeue();
        }
    }

    private sealed class FirewallCommandCall
    {
        public FirewallCommandCall(string fileName, IReadOnlyList<string> arguments)
        {
            FileName = fileName;
            Arguments = arguments;
        }

        public string FileName { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    private static void RunOfpPreparationTest(string testRoot)
    {
        string repository = Path.Combine(testRoot, "UKB Sürümleri");
        string sourceVersion = Path.Combine(repository, "OFP_7.1.12.26.0");
        string flashRoot = Path.Combine(testRoot, "ofp-flash");
        Directory.CreateDirectory(sourceVersion);
        Directory.CreateDirectory(flashRoot);
        File.WriteAllBytes(Path.Combine(sourceVersion, "ofp"),
            new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 2, 3, 4 });
        File.WriteAllText(Path.Combine(sourceVersion, "log.txt"), "örnek derleme günlüğü");

        Assert(VersionManager.IsOfpRepository(repository), "OFP deposu algılanamadı.");
        Assert(VersionManager.GetOfpVersionNames(repository).SequenceEqual(new[] { "OFP_7.1.12.26.0" }),
            "OFP sürümü listelenemedi.");

        var manager = new VersionManager(new AppConfig());
        manager.PrepareVersionFromPc(sourceVersion, flashRoot);

        string copiedVersion = Path.Combine(flashRoot, "OFP_7.1.12.26.0");
        Assert(File.Exists(Path.Combine(copiedVersion, "ofp")), "OFP dosyası flash hedefe kopyalanmadı.");
        Assert(File.ReadAllBytes(Path.Combine(copiedVersion, "ofp"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(sourceVersion, "ofp"))),
            "Kopyalanan OFP kaynakla aynı değil.");

        VersionListResult versions = manager.GetVersionsInFlash(flashRoot, "OFP");
        Assert(versions.Names.SequenceEqual(new[] { "OFP_7.1.12.26.0" }),
            "Flash bellekteki OFP sürümü listelenemedi.");
    }

    private static void RunVersionPreparationTest(string testRoot)
    {
        string sourceVersion = Path.Combine(testRoot, "source-version");
        string sourceBuild = Path.Combine(sourceVersion, "AH10031_01_01_1v710-build.0");
        string flashRoot = Path.Combine(testRoot, "flash");
        string oldBuild = Path.Combine(flashRoot, "OLD-build.0");
        Directory.CreateDirectory(sourceBuild);
        Directory.CreateDirectory(oldBuild);

        string sourceImage = Path.Combine(sourceBuild, "image.json");
        string sourceWrapup = Path.Combine(sourceBuild, "wrapup.sh");
        File.WriteAllText(sourceImage,
            "{\n  \"name\": \"AH10031\",\n  \"autoinstall\": false,\n  \"license\": \"license.html\",\n  \"license_title\": \"Genel Lisans\"\n}\n");
        File.WriteAllText(Path.Combine(sourceBuild, "license.html"), "örnek lisans");
        File.WriteAllText(Path.Combine(sourceBuild, "prepare.sh"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(sourceWrapup, "#!/bin/sh\nexit 0\n");
        File.WriteAllBytes(Path.Combine(sourceBuild, "payload.bin"), Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray());

        File.WriteAllText(Path.Combine(oldBuild, "image.json"), "{\n  \"name\": \"OLD\",\n  \"autoinstall\": true\n}\n");
        File.WriteAllText(Path.Combine(oldBuild, "wrapup.sh"), "#!/bin/sh\nexit 0\n");

        var manager = new VersionManager(new AppConfig());
        manager.PrepareVersionFromPc(sourceVersion, flashRoot);

        string destinationBuild = Path.Combine(flashRoot, Path.GetFileName(sourceBuild));
        Assert(Directory.Exists(destinationBuild), "Hedef sürüm klasörü oluşturulmadı.");
        Assert(File.ReadAllText(sourceImage).Contains("\"autoinstall\": false"), "PC'deki kaynak image.json değiştirildi.");
        Assert(File.ReadAllText(sourceImage).Contains("\"license\""), "PC'deki kaynak lisans referansı değiştirildi.");
        Assert(File.ReadAllText(sourceWrapup).TrimEnd().EndsWith("exit 0"), "PC'deki kaynak wrapup.sh değiştirildi.");
        Assert(File.ReadAllText(Path.Combine(destinationBuild, "image.json")).Contains("\"autoinstall\": true"), "Seçilen sürüm autoinstall=true olmadı.");
        Assert(!File.ReadAllText(Path.Combine(destinationBuild, "image.json")).Contains("\"license\""),
            "Flash kopyasındaki license referansı otomatik kaldırılmadı.");
        Assert(File.Exists(Path.Combine(destinationBuild, "license.html")),
            "Lisans dosyasının kendisi flash kopyasında korunmadı.");
        Assert(File.ReadAllText(Path.Combine(oldBuild, "image.json")).Contains("\"autoinstall\": false"), "Diğer sürüm autoinstall=false olmadı.");

        string[] wrapupLines = File.ReadAllLines(Path.Combine(destinationBuild, "wrapup.sh"));
        int powerOffIndex = Array.FindIndex(wrapupLines, line => line.Trim() == "poweroff -f");
        int exitIndex = Array.FindIndex(wrapupLines, line => line.Trim() == "exit 0");
        Assert(powerOffIndex >= 0 && exitIndex >= 0 && powerOffIndex < exitIndex, "poweroff -f, exit 0 satırından önce değil.");

        VersionListResult versions = manager.GetVersionsInFlash(flashRoot, "AH10031");
        Assert(versions.Names.Length == 1 && versions.Names[0] == "AH10031_01_01_1v710-build.0",
            "Flash bellekteki TEZI paket klasörü doğru listelenemedi.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
