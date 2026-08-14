using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SurumYakma
{
    public static class Localization
    {
        private static readonly Dictionary<string, string> Ui = new(StringComparer.Ordinal)
        {
            ["Yükleme"] = "Installation",
            ["Yardım"] = "Help",
            ["Bağlantı Ayarları..."] = "Connection Settings...",
            ["Platform"] = "Platform",
            ["1. Sürüm Kaynağı"] = "1. Version Source",
            ["2. Yükleme İşlemi"] = "2. Installation",
            ["Klasör Seç"] = "Browse",
            ["İlerleme"] = "Progress",
            ["İşlem Konsolu"] = "Process Console",
            ["İptal"] = "Cancel",
            ["Relay Box Bağlantısı"] = "Relay Box Connection",
            ["Power Box Bağlantısı"] = "Power Box Connection",
            ["Gelişmiş Seçenekler"] = "Advanced Options",
            ["Kaydet ve Bağlan"] = "Save and Connect",
            ["Vazgeç"] = "Cancel",
            ["Listeyi Yenile"] = "Refresh List",
            ["Sürüm deposu"] = "Version Repository",
            ["UKB Sürümleri ana klasörü"] = "UKB Versions Root Folder",
            ["Ortam adı → TEZI paket adı eşlemesi"] = "Environment → TEZI Package Name Mapping",
            ["Satır Ekle"] = "Add Row",
            ["Seçilenleri Sil"] = "Delete Selected",
            ["Algılanan Ortamı Ekle"] = "Add Detected Environment",
            ["Varsayılanları Yükle"] = "Load Defaults",
            ["Seç"] = "Select",
            ["Hedef"] = "Target",
            ["Power kanal(lar)"] = "Power Channel(s)",
            ["Recovery kanal"] = "Recovery Channel",
            ["UKB hedef IP"] = "UKB Target IP",
            ["HTTP sunucu IP"] = "HTTP Server IP",
            ["HTTP portu"] = "HTTP Port",
            ["Baud rate"] = "Baud Rate",
            ["Tamam"] = "OK"
            ,["Sürüm Yükleme v-1.0.2"] = "Version Installation v-1.0.2"
            ,["Bağlantı ve Donanım Ayarları"] = "Connection and Hardware Settings"
            ,["1. Yüklenecek Sürüm"] = "1. Version to Install"
            ,["2. Yüklenecek UKB"] = "2. Target UKB"
            ,["3. Yükleme İşlemi"] = "3. Installation"
            ,["UKB1 — SÜRÜM YÜKLEMEYİ BAŞLAT"] = "UKB1 — START VERSION INSTALLATION"
            ,["USB-NCM PC adresini Easy Installer başladıktan sonra otomatik bul"] =
                "Automatically detect the USB-NCM PC address after Easy Installer starts"
            ,["TEZI paket ön adını ortam değişkenine göre doğrula"] =
                "Validate the TEZI package prefix against the environment"
            ,["Açıksa paket ön adı UAV_PROJECT_NAME ile karşılaştırılır. Kapalıysa ön ad kontrol edilmez; geçerli tam TEZI yapısı bulunması yeterlidir."] =
                "When enabled, the package prefix is compared with UAV_PROJECT_NAME. When disabled, only a valid complete TEZI structure is required."
            ,["Ortam / UAV_PROJECT_NAME"] = "Environment / UAV_PROJECT_NAME"
            ,["TEZI paket adı / ön eki"] = "TEZI Package Name / Prefix"
            ,["Moxa cihazlarına bağlanılıyor..."] = "Connecting to Moxa devices..."
            ,["Ayar Hatası"] = "Settings Error"
            ,["Ayarlar Kilitli"] = "Settings Locked"
            ,["Yükleme Devam Ediyor"] = "Installation in Progress"
            ,["Donanım Testi Hatası"] = "Hardware Test Error"
            ,["Geçersiz Sürüm"] = "Invalid Version"
            ,["Geçersiz Sürüm Paketi"] = "Invalid Version Package"
            ,["Proje Eşleşme Hatası"] = "Project Match Error"
            ,["Ağ Aktarım Hatası"] = "Network Transfer Error"
            ,["Kritik Yükleme Hatası"] = "Critical Installation Error"
            ,["Sürüm Yükleme Hatası"] = "Version Installation Error"
            ,["Sürüm Yükleme Başarılı"] = "Version Installation Successful"
            ,["OTG Kablo Bağlantısı Sağlanamadı"] = "OTG Cable Connection Failed"
            ,["OTG Kablosunu Takın"] = "Connect the OTG Cable"
            ,["Eksik Seçim"] = "Missing Selection"
            ,["Geçersiz Hedef Disk"] = "Invalid Target Drive"
            ,["Başarılı"] = "Successful"
            ,["Hata"] = "Error"
            ,["Uygulama Zaten Açık"] = "Application Already Running"
        };

        private static readonly KeyValuePair<string, string>[] Phrases =
        {
            Pair("Uygulama başladı.", "Application started."),
            Pair("Yüklenen yapılandırma:", "Loaded configuration:"),
            Pair("Bu oturumun ayrıntılı log dosyası:", "Detailed session log file:"),
            Pair("Otomatik donanım algılama:", "Automatic hardware detection:"),
            Pair("Sürüm paketi hazırlanıyor", "Preparing version package"),
            Pair("Donanım ve Güvenli Çıkışlar Kontrol Ediliyor", "Checking hardware and safe outputs"),
            Pair("Recovery Modu ve UKB Gücü Hazırlanıyor", "Preparing Recovery mode and UKB power"),
            Pair("Easy Installer USB Üzerinden Yükleniyor", "Loading Easy Installer over USB"),
            Pair("USB-NCM Sanal Ağ Bağlantısı Oluşturuluyor", "Creating USB-NCM virtual network"),
            Pair("Yerel Sürüm Sunucusu Başlatılıyor", "Starting local version server"),
            Pair("Easy Installer Doğrulanıyor", "Verifying Easy Installer"),
            Pair("UKB ile PC Ağ Erişimi Test Ediliyor", "Testing UKB-to-PC network access"),
            Pair("Sürüm Paketi Easy Installer'a Tanıtılıyor", "Publishing version package to Easy Installer"),
            Pair("Sürüm Dosyaları UKB'ye Aktarılıyor ve Kuruluyor", "Transferring and installing version files"),
            Pair("OFP Sürümü ve Network Up Doğrulanıyor", "Verifying OFP version and Network Up"),
            Pair("Sürüm yüklendi ve doğrulandı", "Version installed and verified"),
            Pair("Sürüm Yükleme", "Version Installation"),
            Pair("SÜRÜM YÜKLENDİ VE DOĞRULANDI", "VERSION INSTALLED AND VERIFIED"),
            Pair("SÜRÜM YÜKLEMEYİ BAŞLAT", "START VERSION INSTALLATION"),
            Pair("SÜRÜM YÜKLEME TAMAMLANDI", "VERSION INSTALLATION COMPLETED"),
            Pair("İşlem başlatılmadı", "Operation not started"),
            Pair("Varsayılan", "Default"),
            Pair("Aktif ortam algılanmadı.", "No active environment was detected."),
            Pair("Aktif:", "Active:"),
            Pair("Moxa cihazlarına bağlanılıyor...", "Connecting to Moxa devices..."),
            Pair("Moxa cihazlarına bağlanılamadı", "Could not connect to Moxa devices"),
            Pair("Arayüz hazır; donanım bağlantıları arka planda kuruluyor...", "Interface ready; hardware connections are being established in the background..."),
            Pair("Başlangıç güvenliği:", "Startup safety:"),
            Pair("projesine ait tam TEZI (*build.0) paketi bulunamadı.", "project's complete TEZI (*build.0) package was not found."),
            Pair("Ham OFP dosyaları kurulum paketi değildir.", "Raw OFP files are not installation packages."),
            Pair("Varsayılan bilgisayar projesi ortam değişkeninden okundu:", "Default computer platform read from environment variable:"),
            Pair("Aktif sürüm platformu:", "Active version platform:"),
            Pair("Bağlantı ayarları makine profiline kaydedildi ancak Settings.json güncellenemedi.", "Connection settings were saved to the machine profile, but Settings.json could not be updated."),
            Pair("Kritik yükleme aşamasında işlem ekranından ayrılamazsınız.", "You cannot leave the operation screen during the critical installation stage."),
            Pair("Devam eden/hazırlanmış ağ işlemi veya manuel kontrol varken bağlantı ayarları değiştirilemez.", "Connection settings cannot be changed while a network operation or manual inspection is active."),
            Pair("Sürüm yükleme için PC'deki tam TEZI *build.0 paketini seçin.", "Select the complete TEZI *build.0 package on the PC for version installation."),
            Pair("Seçili paket UAV_PROJECT_NAME projesiyle eşleşmiyor.", "The selected package does not match the UAV_PROJECT_NAME project."),
            Pair("NetworkServerIp değerinin Windows USB-NCM bağdaştırıcısına ait olduğunu ipconfig ile doğrulayın.", "Use ipconfig to verify that NetworkServerIp belongs to the Windows USB-NCM adapter."),
            Pair("Sürüm yükleme başlatılamadı.", "Version installation could not be started."),
            Pair("Başarısız aşama:", "Failed stage:"),
            Pair("Başarısız aşama ve nedeni ayrıntılı oturum loguna kaydedildi.", "The failed stage and its reason were recorded in the detailed session log."),
            Pair("Lütfen disk ve sürüm seçin.", "Please select a drive and version."),
            Pair("Güvenlik nedeniyle sürüm yalnızca çıkarılabilir bir flash belleğe hazırlanabilir.", "For safety, the version can only be prepared on a removable flash drive."),
            Pair("Seçili sürüm bilgisayarın UAV_PROJECT_NAME projesiyle eşleşmiyor.", "The selected version does not match the computer's UAV_PROJECT_NAME project."),
            Pair("Üretim yüklemesi yalnızca image.json, prepare.sh ve wrapup.sh içeren tam TEZI *build.0 paketiyle başlatılabilir. Ham OFP dosyası seçilemez.", "Production installation can only start with a complete TEZI *build.0 package containing image.json, prepare.sh and wrapup.sh. A raw OFP file cannot be selected."),
            Pair("Sürüm yakma işlemi tamamlandı.", "Version installation completed."),
            Pair("İşlem iptal edildi.", "Operation cancelled."),
            Pair("İşlem hata ile durdu:", "Operation stopped with an error:"),
            Pair("Ağdan sürüm yükleme uygulaması zaten açık. Açık olan pencereyi kullanın.", "The network version installation application is already running. Use the open window."),
            Pair("bağlandı", "connected"),
            Pair("bağlanamadı", "could not connect"),
            Pair("bağlantısı bekleniyor", "connection is pending"),
            Pair("kanalı okundu", "channel read"),
            Pair("kanalı açıldı", "channel enabled"),
            Pair("kanalı kapatıldı", "channel disabled"),
            Pair("güç açılıyor", "power is being enabled"),
            Pair("güç kapatılıyor", "power is being disabled"),
            Pair("Recovery modu REAL yapılıyor", "Setting Recovery mode to REAL"),
            Pair("Recovery modu NORMAL yapılıyor", "Setting Recovery mode to NORMAL"),
            Pair("zaman aşımına uğradı", "timed out"),
            Pair("yeniden deneniyor", "retrying"),
            Pair("başarılı", "successful"),
            Pair("başarısız", "failed"),
            Pair("doğrulandı", "verified"),
            Pair("bekleniyor", "waiting"),
            Pair("bulunamadı", "not found"),
            Pair("hata ile durdu", "stopped with an error"),
            Pair("işlem", "operation"),
            Pair("İşlem", "Operation"),
            Pair("sürüm", "version"),
            Pair("Sürüm", "Version"),
            Pair("Lütfen", "Please"),
            Pair("kapatılıyor", "is being closed"),
            Pair("açılıyor", "is being opened")
            ,Pair("kullanıcı", "user")
            ,Pair("Kullanıcı", "User")
            ,Pair("ayarları", "settings")
            ,Pair("Ayarları", "Settings")
            ,Pair("ayar", "setting")
            ,Pair("dosyası", "file")
            ,Pair("dosya", "file")
            ,Pair("klasörü", "folder")
            ,Pair("klasör", "folder")
            ,Pair("bağlantı", "connection")
            ,Pair("Bağlantı", "Connection")
            ,Pair("ağ", "network")
            ,Pair("Ağ", "Network")
            ,Pair("güç", "power")
            ,Pair("Güç", "Power")
            ,Pair("yükleme", "installation")
            ,Pair("Yükleme", "Installation")
            ,Pair("hazırlanıyor", "is being prepared")
            ,Pair("kontrol ediliyor", "is being checked")
            ,Pair("algılanamadı", "was not detected")
            ,Pair("algılandı", "detected")
            ,Pair("seçili", "selected")
            ,Pair("Seçili", "Selected")
            ,Pair("hedef", "target")
            ,Pair("Hedef", "Target")
            ,Pair("kapatıldı", "disabled")
            ,Pair("açıldı", "enabled")
            ,Pair("tamamlandı", "completed")
            ,Pair("başlatıldı", "started")
            ,Pair("iptal edildi", "cancelled")
            ,Pair("doğru", "correct")
            ,Pair("yanlış", "incorrect")
            ,Pair("değiştirildi", "changed")
            ,Pair("kaydedildi", "saved")
            ,Pair("bulundu", "found")
            ,Pair("okundu", "read")
            ,Pair("yazıldı", "written")
            ,Pair("deneme", "attempt")
            ,Pair("hata", "error")
            ,Pair("Hata", "Error")
            ,Pair("uyarı", "warning")
            ,Pair("Uyarı", "Warning")
        };

        public static bool IsEnglish { get; private set; }
        public static string LanguageCode => IsEnglish ? "EN" : "TR";
        public static void SetLanguage(string code) =>
            IsEnglish = string.Equals(code, "EN", StringComparison.OrdinalIgnoreCase);
        public static string T(string tr, string en) => IsEnglish ? en : tr;

        public static string TranslateToEnglish(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            if (Ui.TryGetValue(value, out string exact))
                return exact;
            string translated = value;
            foreach (KeyValuePair<string, string> pair in Phrases.OrderByDescending(item => item.Key.Length))
            {
                string pattern =
                    $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(pair.Key)}(?![\p{{L}}\p{{N}}_])";
                translated = Regex.Replace(
                    translated,
                    pattern,
                    _ => pair.Value,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return translated;
        }

        public static string ForCurrentLanguage(string value) =>
            IsEnglish ? TranslateToEnglish(value) : value;

        public static void Apply(Control root)
        {
            if (root == null)
                return;
            if (IsEnglish)
                root.Text = TranslateToEnglish(root.Text);
            else
            {
                KeyValuePair<string, string> exact = Ui.FirstOrDefault(item =>
                    string.Equals(item.Value, root.Text, StringComparison.Ordinal));
                if (!string.IsNullOrEmpty(exact.Key))
                    root.Text = exact.Key;
            }
            if (root is DataGridView grid && IsEnglish)
                foreach (DataGridViewColumn column in grid.Columns)
                    column.HeaderText = TranslateToEnglish(column.HeaderText);
            foreach (Control child in root.Controls)
                Apply(child);
        }

        private static KeyValuePair<string, string> Pair(string tr, string en) => new(tr, en);
    }

    public static class LocalizedMessageBox
    {
        public static DialogResult Show(
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon) =>
            MessageBox.Show(
                Localization.ForCurrentLanguage(text),
                Localization.ForCurrentLanguage(caption),
                buttons,
                icon);

        public static DialogResult Show(
            IWin32Window owner,
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon) =>
            MessageBox.Show(
                owner,
                Localization.ForCurrentLanguage(text),
                Localization.ForCurrentLanguage(caption),
                buttons,
                icon);
    }
}
