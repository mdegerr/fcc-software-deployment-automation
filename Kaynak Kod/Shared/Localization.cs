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
            ["Başlat"] = "Start",
            ["Relay Box Connection"] = "Relay Box Connection",
            ["Power Box Connection"] = "Power Box Connection",
            ["Gelişmiş Seçenekler"] = "Advanced Options",
            ["Kaydet ve Bağlan"] = "Save and Connect",
            ["Vazgeç"] = "Cancel",
            ["Listeyi Yenile"] = "Refresh List",
            ["Sürüm deposu"] = "Version Repository",
            ["UKB Sürümleri ana klasörü"] = "UKB Versions Root Folder",
            ["Satır Ekle"] = "Add Row",
            ["Seçilenleri Sil"] = "Delete Selected",
            ["Algılanan Ortamı Ekle"] = "Add Detected Environment",
            ["Varsayılanları Yükle"] = "Load Defaults",
            ["Seç"] = "Select",
            ["Hedef"] = "Target",
            ["Power kanal(lar)"] = "Power Channel(s)",
            ["Recovery kanal"] = "Recovery Channel",
            ["Power Box IP"] = "Power Box IP",
            ["Relay Box IP"] = "Relay Box IP",
            ["Power MOD"] = "Power MOD",
            ["Recovery MOD"] = "Recovery MOD",
            ["COM"] = "COM",
            ["UKB hedef IP"] = "UKB Target IP",
            ["HTTP sunucu IP"] = "HTTP Server IP",
            ["HTTP portu"] = "HTTP Port",
            ["Baud rate"] = "Baud Rate",
            ["Tamam"] = "OK"
            ,["Sürüm Yükleme v-1.0.3"] = "Version Installation v-1.0.3"
            ,["Bağlantı ve Donanım Ayarları"] = "Connection and Hardware Settings"
            ,["1. Yüklenecek Sürüm"] = "1. Version to Install"
            ,["2. Yüklenecek UKB"] = "2. Target UKB"
            ,["3. Yükleme İşlemi"] = "3. Installation"
            ,["UKB1 — SÜRÜM YÜKLEMEYİ BAŞLAT"] = "UKB1 — START VERSION INSTALLATION"
            ,["USB-NCM PC adresini Easy Installer başladıktan sonra otomatik bul"] =
                "Automatically detect the USB-NCM PC address after Easy Installer starts"
            ,["Ortam / UAV_PROJECT_NAME"] = "Environment / UAV_PROJECT_NAME"
            ,["Platform listesi"] = "Platform List"
            ,["Moxa cihazlarına bağlanılıyor..."] = "Connecting to Moxa devices..."
            ,["Moxa bağlantısı bekleniyor; Bağlantı Ayarlarından değerleri kontrol edin."] =
                "Moxa connection is pending; check the values in Connection Settings."
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
            ,["Settings.json Uyarisi"] = "Settings.json Warning"
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
            Pair("Aktif COM:", "Active COM:"),
            Pair("Aktif IPv4:", "Active IPv4:"),
            Pair("Aktif platform:", "Active platform:"),
            Pair("Moxa cihazlarına bağlanılıyor...", "Connecting to Moxa devices..."),
            Pair("Başlangıç güvenliği: Power OFF ve Recovery NORMAL doğrulanıyor...", "Startup safety: verifying Power OFF and Recovery NORMAL..."),
            Pair("Yüklemeye hazır; sürümü seçip işlemi başlatın.", "Ready for installation; select a version and start the operation."),
            Pair("Hazır.", "Ready."),
            Pair("Donanım bağlantısı kurulamadı; ayarları kontrol edin.", "Hardware connection could not be established; check the settings."),
            Pair("Moxa bağlantıları hazır.", "Moxa connections are ready."),
            Pair("Moxa bağlantısı bekleniyor; uygulama arka planda yeniden deneyecek.", "Waiting for the Moxa connection; the application will retry in the background."),
            Pair("Yeni ayarlarla Moxa bağlantıları kuruluyor...", "Connecting to Moxa devices with the new settings..."),
            Pair("Ayarlar kaydedildi ve Moxa bağlantıları kuruldu.", "Settings were saved and Moxa connections were established."),
            Pair("Ayarlar kaydedildi; Moxa bağlantısı kurulamadı. IP/slot/kanal değerlerini kontrol edin.", "Settings were saved, but the Moxa connection could not be established. Check the IP, slot and channel values."),
            Pair("OTG kablo bağlantısı algılanamadı. OTG kablosunu doğrudan PC ile UKB arasına takın ve bağlantıyı kontrol edin.\n\nTamam'a bastıktan sonra uygulama Recovery güç çevrimi yaparak USB aygıtını otomatik olarak tekrar arayacaktır.", "The OTG cable connection was not detected. Connect the OTG cable directly between the PC and UKB and check the connection.\n\nAfter you press OK, the application will perform a Recovery power cycle and automatically search for the USB device again."),
            Pair("Baglanti ayarlari makine profiline kaydedildi ancak Settings.json guncellenemedi.", "Connection settings were saved to the machine profile, but Settings.json could not be updated."),
            Pair("Sürüm dosyası aktarılmaya başladıktan sonra hata oluştu. Güvenlik için UKB gücü ve Recovery durumu değiştirilmedi.", "An error occurred after the version file transfer started. For safety, UKB power and Recovery state were not changed."),
            Pair("UKB gücünü kesmeyin; seri log ile cihaz durumunu kontrol edin.", "Do not cut UKB power; check the device status in the serial log."),
            Pair("Bağlantı testleri geçti, sürüm ağ üzerinden yüklendi ve normal açılıştaki OFP sürümü doğrulandı.", "Connection tests passed, the version was installed over the network, and the OFP version was verified after normal boot."),
            Pair("Ağ durumu:", "Network status:"),
            Pair("Fiziksel Ethernet LINK UP", "Physical Ethernet LINK UP"),
            Pair("Linux ağ sistemi hazır", "Linux network system ready"),
            Pair("Ağ hazır mesajı gözlenmedi (OFP doğrulandı)", "Network ready message was not observed (OFP verified)"),
            Pair("UKS POWER açık ve Recovery NORMAL durumda bırakıldı.", "UKS POWER was left ON and Recovery was left in NORMAL mode."),
            Pair("OFP doğrulandı — Fiziksel Ethernet: LINK UP", "OFP verified — Physical Ethernet: LINK UP"),
            Pair("OFP doğrulandı — Linux ağ sistemi hazır", "OFP verified — Linux network system ready"),
            Pair("Moxa cihazlarına bağlanılamadı", "Could not connect to Moxa devices"),
            Pair("Arayüz hazır; donanım bağlantıları arka planda kuruluyor...", "Interface ready; hardware connections are being established in the background..."),
            Pair("Başlangıç güvenliği:", "Startup safety:"),
            Pair("projesine ait tam TEZI (*build.0) paketi bulunamadı.", "project's complete TEZI (*build.0) package was not found."),
            Pair("Ham OFP dosyaları kurulum paketi değildir.", "Raw OFP files are not installation packages."),
            Pair("Varsayılan bilgisayar projesi ortam değişkeninden okundu:", "Default computer platform read from environment variable:"),
            Pair("Aktif sürüm platformu:", "Active version platform:"),
            Pair("Uygulama dili Türkçe olarak değiştirildi.", "Application language changed to English."),
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
            Pair("Donanım bağlantısı kurulamadı; ayarları kontrol edin.", "Hardware connection could not be established; check the settings."),
            Pair("İptal isteği alındı; aktarım durduruluyor ve UKB güvenli duruma alınıyor...", "Cancellation requested; transfer is stopping and the UKB is being placed in a safe state..."),
            Pair("MANUEL KURTARMA GEREKLİ — UKB gücünü kesmeyin; seri port ile kontrol edin.", "MANUAL RECOVERY REQUIRED — Do not cut UKB power; inspect it through the serial port."),
            Pair("Seçilen paket doğrulanıp yüklemeye hazırlanıyor...", "The selected package is being verified and prepared for installation..."),
            Pair("Paket hazır. Hedef bağlantısı oluşunca yükleme otomatik başlatılacak.", "Package ready. Installation will start automatically when the target connection is available."),
            Pair("Ağ aktarım hazırlığı başarısız:", "Network transfer preparation failed:"),
            Pair("UKB güç durumu kontrol ediliyor...", "Checking UKB power state..."),
            Pair("Recovery başlangıç durumu NORMAL yapılıyor...", "Setting the initial Recovery state to NORMAL..."),
            Pair("UKB gücü açılıyor...", "Turning UKB power on..."),
            Pair("Toradex Easy Installer USB üzerinden yükleniyor; OTG kablosu bekleniyor...", "Loading Toradex Easy Installer over USB; waiting for the OTG cable..."),
            Pair("Seçili UKB USB-NCM adaptörü hazırlanıyor...", "Preparing the selected UKB USB-NCM adapter..."),
            Pair("Hedef yükleme ortamı doğrulanıyor...", "Verifying the target installation environment..."),
            Pair("Yerel sürüm sunucusu seçili USB-NCM adresinde başlatılıyor...", "Starting the local version server on the selected USB-NCM address..."),
            Pair("Easy Installer seri konsol veya ağ duyurusu üzerinden doğrulanıyor...", "Verifying Easy Installer through the serial console or network announcement..."),
            Pair("Easy Installer → PC için erişilebilir HTTP portu otomatik belirleniyor...", "Automatically determining an HTTP port reachable from Easy Installer to the PC..."),
            Pair("Easy Installer → PC ağ yolu doğrulandı; paket isteği bekleniyor...", "The Easy Installer-to-PC network path was verified; waiting for the package request..."),
            Pair("Testler başarılı; TEZI ağına sürüm duyuruluyor...", "Tests passed; announcing the version on the TEZI network..."),
            Pair("Easy Installer'ın sürüm listesini istemesi bekleniyor...", "Waiting for Easy Installer to request the version list..."),
            Pair("Easy Installer'ın paket bilgisini istemesi bekleniyor...", "Waiting for Easy Installer to request package metadata..."),
            Pair("Testler tamam; Easy Installer sürüm yüklemesini başlatıyor...", "Tests completed; Easy Installer is starting the version installation..."),
            Pair("Sürüm aktarılıyor; UKB kapanış doğrulaması bekleniyor...", "Transferring the version; waiting for UKB shutdown confirmation..."),
            Pair("Moxa bağlantıları yenileniyor...", "Refreshing Moxa connections..."),
            Pair("Kurulum tamamlandı; UKB gücü kapatılıyor...", "Installation completed; turning UKB power off..."),
            Pair("UKB normal açılışta sürüm doğrulaması için çalıştırılıyor...", "Starting the UKB normally to verify the installed version..."),
            Pair("Sürüm yükleme kullanıcı tarafından durduruldu; Power OFF ve Recovery NORMAL uygulanıyor.", "Version installation was stopped by the user; applying Power OFF and Recovery NORMAL."),
            Pair("Test/yükleme başarısız; neden oturum loguna kaydedildi.", "Test/installation failed; the reason was recorded in the session log."),
            Pair("Hata/iptal sonrası UKB gücü güvenli biçimde kapatılıyor...", "Safely turning UKB power off after the error/cancellation..."),
            Pair("Hata/iptal sonrası Recovery NORMAL yapılıyor...", "Setting Recovery to NORMAL after the error/cancellation..."),
            Pair("UYARI: UKB güç durumu doğrulanamadı; logu inceleyin.", "WARNING: The UKB power state could not be verified; inspect the log."),
            Pair("UYARI: Recovery durumu doğrulanamadı; logu inceleyin.", "WARNING: The Recovery state could not be verified; inspect the log."),
            Pair("SÜRÜM AĞDAN YÜKLENDİ VE DOĞRULANDI — yeni işlem başlatılabilir.", "VERSION INSTALLED AND VERIFIED OVER THE NETWORK — A new operation can be started."),
            Pair("İşlem durdu. Ayarlar ve seçimler yeniden açıldı; tekrar deneyebilirsiniz.", "The operation stopped. Settings and selections are available again; you can retry."),
            Pair("Flash bellek hazır bildirildi; otomatik yükleme başlatılıyor...", "Flash drive readiness confirmed; starting automatic installation..."),
            Pair("Yüklenebilir bir sürüm klasörü seçin.", "Select an installable version folder."),
            Pair("Aşama", "Stage"),
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
            ,Pair("Yapılandırılan USB-NCM PC adresi etkin bir Windows adaptörüne bağlı değil", "The configured USB-NCM PC address is not assigned to an active Windows adapter")
            ,Pair("Seçili UKB için USB-NCM adaptörü bulunamadı", "The USB-NCM adapter for the selected UKB was not found")
            ,Pair("adaptörü diğer etkin USB ağ adaptörlerinden güvenli biçimde ayırt edilemedi", "adapter could not be safely distinguished from the other active USB network adapters")
            ,Pair("OTG kablosunu kontrol edip yalnızca seçili UKB'nin Easy Installer ortamında olduğunu doğrulayın", "Check the OTG cable and verify that only the selected UKB is in the Easy Installer environment")
            ,Pair("USB-NCM IPv4 adresi atandı ancak seçili adaptörde etkinleşmedi", "The USB-NCM IPv4 address was assigned but did not become active on the selected adapter")
            ,Pair("Seçili USB-NCM adaptörünün IPv4 arayüz indeksi okunamadı", "The IPv4 interface index of the selected USB-NCM adapter could not be read")
            ,Pair("Seçili UKB için Windows hedef rotası oluşturulamadı", "The Windows target route for the selected UKB could not be created")
            ,Pair("Easy Installer, PC HTTP sunucusuna 80/8088/8000/8888 portlarından ulaşamadı", "Easy Installer could not reach the PC HTTP server on ports 80/8088/8000/8888")
            ,Pair("Uygulama seçili UKB'nin USB-NCM adaptörünü ve hedef rotasını doğruladı", "The application verified the selected UKB's USB-NCM adapter and target route")
            ,Pair("buna rağmen erişim yoksa OTG sürücüsünü, Windows Güvenlik Duvarı iznini ve seçili UKB'nin COM/adaptör eşleşmesini kontrol edin", "if access is still unavailable, check the OTG driver, Windows Firewall permission, and the selected UKB's COM-to-adapter mapping")
            ,Pair("Seçili UKB için yeni USB-NCM adaptörü bulunamadı", "A new USB-NCM adapter for the selected UKB was not found")
            ,Pair("Başka bir UKB'nin açık kalan adaptörü kullanılmadı; seçili UKB'nin OTG kablosunu kontrol edin", "An adapter left active by another UKB was not used; check the selected UKB's OTG cable")
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
            KeyValuePair<string, string> exactPhrase = Phrases.FirstOrDefault(item =>
                string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactPhrase.Key))
                return exactPhrase.Value;
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

        public static string TranslateToTurkish(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            KeyValuePair<string, string> exact = Ui.FirstOrDefault(item =>
                string.Equals(item.Value, value, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(exact.Key))
                return exact.Key;

            KeyValuePair<string, string> exactPhrase = Phrases.FirstOrDefault(item =>
                string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactPhrase.Key))
                return exactPhrase.Key;

            string translated = value;
            foreach (KeyValuePair<string, string> pair in Phrases
                .OrderByDescending(item => item.Value.Length))
            {
                string pattern =
                    $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(pair.Value)}(?![\p{{L}}\p{{N}}_])";
                translated = Regex.Replace(
                    translated,
                    pattern,
                    _ => pair.Key,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return translated;
        }

        public static string ForCurrentLanguage(string value) =>
            IsEnglish ? TranslateToEnglish(value) : TranslateToTurkish(value);

        public static void Apply(Control root)
        {
            if (root == null)
                return;
            if (root is not RichTextBox)
                root.Text = ForCurrentLanguage(root.Text);
            if (root is DataGridView grid)
                foreach (DataGridViewColumn column in grid.Columns)
                    column.HeaderText = ForCurrentLanguage(column.HeaderText);
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
