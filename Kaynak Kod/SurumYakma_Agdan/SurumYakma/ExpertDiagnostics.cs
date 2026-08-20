using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    internal sealed class ExpertDiagnosticContext
    {
        public string Stage { get; set; }
        public string Ukb { get; set; }
        public string TargetIp { get; set; }
        public string ServerIp { get; set; }
        public int HttpPort { get; set; }
        public string SerialPort { get; set; }
        public int BaudRate { get; set; }
        public string RelayBoxIp { get; set; }
        public string PowerBoxIp { get; set; }
        public bool PayloadStarted { get; set; }
        public bool ShutdownObserved { get; set; }
        public bool PowerOn { get; set; }
        public bool RecoveryEnabled { get; set; }
    }

    internal static class ExpertDiagnostics
    {
        private const int CommandTimeoutMilliseconds = 8000;
        private const int SessionTailLineCount = 350;

        public static async Task<string> CaptureFailureAsync(
            ExpertDiagnosticContext context,
            Exception exception,
            CancellationToken cancellationToken)
        {
            string sessionLog = Logger.CurrentSessionLogPath;
            string logDirectory = Path.GetDirectoryName(sessionLog);
            if (string.IsNullOrWhiteSpace(logDirectory))
                logDirectory = AppContext.BaseDirectory;
            string diagnosticDirectory = Path.Combine(logDirectory, "diagnostics");
            Directory.CreateDirectory(diagnosticDirectory);
            string path = Path.Combine(
                diagnosticDirectory,
                $"expertiz_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}.txt");

            var sections = new List<Task<DiagnosticSection>>
            {
                CaptureCommandAsync("IPCONFIG_ALL", "ipconfig.exe", "/all", cancellationToken),
                CaptureCommandAsync("IPV4_ROUTES", "route.exe", "print -4", cancellationToken),
                CaptureCommandAsync("IPV4_INTERFACES", "netsh.exe", "interface ipv4 show interfaces", cancellationToken),
                CaptureCommandAsync("IPV4_ADDRESSES", "netsh.exe", "interface ipv4 show addresses", cancellationToken),
                CaptureCommandAsync("TCP_LISTENERS", "netstat.exe", "-ano -p tcp", cancellationToken),
                CaptureCommandAsync(
                    "FIREWALL_RULE",
                    "netsh.exe",
                    "advfirewall firewall show rule name=\"Sürüm Yükleme - Uygulama Gelen Bağlantı\" verbose",
                    cancellationToken),
                CaptureCommandAsync(
                    "CONNECTED_NET_DEVICES",
                    ResolveWindowsSystemTool("pnputil.exe"),
                    "/enum-devices /connected /class Net",
                    cancellationToken)
            };

            DiagnosticSection[] commandSections = await Task.WhenAll(sections);
            var report = new StringBuilder(128 * 1024);
            report.AppendLine("SÜRÜM YÜKLEME - UZMAN HATA TEŞHİS RAPORU");
            report.AppendLine(new string('=', 72));
            report.AppendLine("Oluşturma zamanı : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            report.AppendLine("Oturum logu     : " + sessionLog);
            report.AppendLine("Gizlilik         : Şifreler ve sürüm payload içerikleri bu rapora alınmaz.");
            report.AppendLine();

            AppendSection(report, "HATA ÖZETİ", BuildFailureSummary(context, exception));
            AppendSection(report, "OLASI NEDEN / ÖNERİLEN KONTROL", ClassifyFailure(context?.Stage, exception));
            AppendSection(report, "UYGULAMA VE WINDOWS", BuildHostSummary());
            AppendSection(report, "SEÇİLİ YÜKLEME BAĞLAMI", BuildContextSummary(context));
            AppendSection(report, "COM PORTLARI", BuildSerialSummary());
            AppendSection(report, "YÖNETİLEN AĞ BAĞDAŞTIRICILARI", BuildNetworkSummary(context));
            AppendSection(report, "İLGİLİ ÇALIŞAN SÜREÇLER", BuildProcessSummary());

            foreach (DiagnosticSection section in commandSections)
                AppendSection(report, section.Name, section.Content);

            Logger.Flush();
            AppendSection(report, "OTURUM LOGU - SON CHECKPOINT VE MESAJLAR", ReadSessionTail(sessionLog));
            await File.WriteAllTextAsync(path, report.ToString(), new UTF8Encoding(true), cancellationToken);
            return path;
        }

        internal static string ClassifyFailure(string stage, Exception exception)
        {
            string value = (stage ?? "").ToUpperInvariant();
            if (value.Contains("OTG") || value.Contains("EASY_INSTALLER_LOAD"))
                return "USB recovery aygıtı zamanında oluşmadı. OTG kablosu, USB güç geri beslemesi, UUU sürücüsü, doğru Recovery/Power kanalı ve Aygıt Yöneticisi kayıtlarını kontrol edin.";
            if (value.Contains("USB_NCM"))
                return "USB-NCM bağdaştırıcısı oluşmadı veya doğru IPv4/route atanamadı. UsbNcm sürücüsü, yeni oluşan adaptör, hedefe özel route ve çakışan eski adaptörler kontrol edilmelidir.";
            if (value.Contains("HTTP_SERVER") || value.Contains("REVERSE_HTTP"))
                return "Yerel HTTP sunucusu seçilen IPv4 adresi/port üzerinde yayın yapamadı. IP sahipliği, port çakışması, yönetici yetkisi ve Windows Defender Firewall sonucu aşağıdaki bölümlerde karşılaştırılmalıdır.";
            if (value.Contains("METADATA"))
                return "UKB image_list.json dosyasını gördü fakat image.json isteğine geçmedi. Feed biçimi, Easy Installer liste yenilemesi, paket uyumluluğu ve HTTP istek sırası son checkpoint bölümünden incelenmelidir.";
            if (value.Contains("TEZI_FEED") || value.Contains("MDNS"))
                return "Easy Installer özel feed duyurusunu alamadı veya taramadı. mDNS 5353 erişimi, doğru USB-NCM arayüzü, firewall ve VNC/TEZI yenileme kayıtları kontrol edilmelidir.";
            if (value.Contains("PAYLOAD") || value.Contains("SHUTDOWN"))
                return "Gerçek aktarım başlamış olabilir. Gücü kesmeden payload HTTP istekleri, seri kapanış kanıtı ve hedef durumunu kontrol edin.";
            if (value.Contains("MOXA") || value.Contains("POWER") || value.Contains("RECOVERY"))
                return "Moxa bağlantısı veya fiziksel kanal doğrulaması başarısız. IP, slot/MOD, kanal, mevcut kanal değeri ve ağ erişimi kontrol edilmelidir.";
            if (value.Contains("OFP") || value.Contains("NETWORK_UP"))
                return "Kurulum sonrası normal açılış doğrulaması tamamlanmadı. Recovery NORMAL, Power ON, doğru COM portu ve beklenen OFP/ethernet seri mesajları kontrol edilmelidir.";
            return "Hata tek bir kategoriye indirgenemedi. HATA ÖZETİ, son checkpoint’ler ve Windows ağ/USB sorguları birlikte değerlendirilmelidir.";
        }

        private static string BuildFailureSummary(ExpertDiagnosticContext context, Exception exception)
        {
            var text = new StringBuilder();
            text.AppendLine("Başarısız aşama : " + (context?.Stage ?? "bilinmiyor"));
            for (Exception current = exception; current != null; current = current.InnerException)
                text.AppendLine($"{current.GetType().FullName}: {current.Message}");
            text.AppendLine();
            text.AppendLine(exception?.ToString() ?? "İstisna bilgisi yok.");
            return text.ToString();
        }

        private static string BuildHostSummary()
        {
            string executable = Environment.ProcessPath ?? "bilinmiyor";
            string version = File.Exists(executable)
                ? FileVersionInfo.GetVersionInfo(executable).FileVersion
                : "bilinmiyor";
            bool elevated = false;
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            return string.Join(Environment.NewLine, new[]
            {
                "OS                 : " + Environment.OSVersion,
                "OS 64-bit          : " + Environment.Is64BitOperatingSystem,
                "Process 64-bit     : " + Environment.Is64BitProcess,
                ".NET               : " + Environment.Version,
                "Yönetici           : " + elevated,
                "Makine             : " + Environment.MachineName,
                "Uygulama           : " + executable,
                "Dosya sürümü       : " + version,
                "Process ID         : " + Environment.ProcessId,
                "Çalışma süresi     : " + TimeSpan.FromMilliseconds(Environment.TickCount64)
            });
        }

        private static string BuildContextSummary(ExpertDiagnosticContext c)
        {
            if (c == null) return "Bağlam bilgisi yok.";
            return string.Join(Environment.NewLine, new[]
            {
                "Aşama              : " + c.Stage,
                "UKB                : " + c.Ukb,
                "Hedef IP           : " + c.TargetIp,
                "PC/HTTP IP         : " + c.ServerIp,
                "HTTP portu         : " + c.HttpPort,
                "Seri port          : " + c.SerialPort + " / " + c.BaudRate,
                "Relay Box IP       : " + c.RelayBoxIp,
                "Power Box IP       : " + c.PowerBoxIp,
                "Payload başladı    : " + c.PayloadStarted,
                "Shutdown görüldü   : " + c.ShutdownObserved,
                "Power ON           : " + c.PowerOn,
                "Recovery REAL      : " + c.RecoveryEnabled
            });
        }

        private static string BuildSerialSummary()
        {
            try { return string.Join(Environment.NewLine, SerialPort.GetPortNames().OrderBy(x => x)); }
            catch (Exception ex) { return "COM portları okunamadı: " + ex.Message; }
        }

        private static string BuildNetworkSummary(ExpertDiagnosticContext context)
        {
            var text = new StringBuilder();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces().OrderBy(x => x.Name))
                {
                    IPInterfaceProperties properties;
                    try { properties = nic.GetIPProperties(); }
                    catch { continue; }
                    string[] ipv4 = properties.UnicastAddresses
                        .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(x => x.Address + "/" + x.PrefixLength)
                        .ToArray();
                    bool relevant = ipv4.Length > 0 ||
                        nic.Description.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        nic.Description.IndexOf("NCM", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!relevant) continue;
                    text.AppendLine($"[{nic.Name}] id={nic.Id}");
                    text.AppendLine($"  Açıklama={nic.Description}; durum={nic.OperationalStatus}; tür={nic.NetworkInterfaceType}; hız={nic.Speed}");
                    text.AppendLine("  IPv4=" + (ipv4.Length == 0 ? "yok" : string.Join(", ", ipv4)));
                    text.AppendLine("  Gateway=" + string.Join(", ", properties.GatewayAddresses.Select(x => x.Address.ToString())));
                }

                int port = context?.HttpPort ?? 0;
                if (port > 0)
                {
                    string listeners = string.Join(", ", IPGlobalProperties.GetIPGlobalProperties()
                        .GetActiveTcpListeners().Where(x => x.Port == port));
                    text.AppendLine($"HTTP port {port} dinleyicileri: " + (listeners.Length == 0 ? "yok" : listeners));
                }
            }
            catch (Exception ex) { text.AppendLine("Ağ özeti alınamadı: " + ex); }
            return text.ToString();
        }

        private static string BuildProcessSummary()
        {
            string[] names = { "uuu", "ttermpro", "vncviewer", "ioAdmin", "Sonic", "Surum", "Sürüm" };
            var lines = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (names.Any(x => process.ProcessName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                        lines.Add($"pid={process.Id}; name={process.ProcessName}; started={TryGetStartTime(process)}");
                }
                catch { }
                finally { process.Dispose(); }
            }
            return lines.Count == 0 ? "İlgili süreç bulunamadı." : string.Join(Environment.NewLine, lines);
        }

        private static string TryGetStartTime(Process process)
        {
            try { return process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return "erişilemedi"; }
        }

        private static async Task<DiagnosticSection> CaptureCommandAsync(
            string name,
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                Task exited = process.WaitForExitAsync(cancellationToken);
                Task finished = await Task.WhenAny(exited, Task.Delay(CommandTimeoutMilliseconds, cancellationToken));
                if (finished != exited)
                {
                    try { process.Kill(true); } catch { }
                    return new DiagnosticSection(name, $"TIMEOUT after {CommandTimeoutMilliseconds} ms: {fileName} {arguments}");
                }
                return new DiagnosticSection(
                    name,
                    $"Command: {fileName} {arguments}{Environment.NewLine}ExitCode: {process.ExitCode}{Environment.NewLine}" +
                    await stdout + Environment.NewLine + await stderr);
            }
            catch (Exception ex)
            {
                return new DiagnosticSection(name, "Sorgu çalıştırılamadı: " + ex);
            }
        }

        private static string ResolveWindowsSystemTool(string fileName)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                string sysnative = Path.Combine(windows, "Sysnative", fileName);
                if (File.Exists(sysnative)) return sysnative;
            }
            string system32 = Path.Combine(windows, "System32", fileName);
            return File.Exists(system32) ? system32 : fileName;
        }

        private static string ReadSessionTail(string path)
        {
            try
            {
                if (!File.Exists(path)) return "Oturum logu bulunamadı.";
                string[] lines = File.ReadAllLines(path);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - SessionTailLineCount)));
            }
            catch (Exception ex) { return "Oturum logu okunamadı: " + ex.Message; }
        }

        private static void AppendSection(StringBuilder report, string title, string content)
        {
            report.AppendLine();
            report.AppendLine("[" + title + "]");
            report.AppendLine(new string('-', 72));
            report.AppendLine(string.IsNullOrWhiteSpace(content) ? "Bilgi yok." : content.TrimEnd());
        }

        private readonly struct DiagnosticSection
        {
            public DiagnosticSection(string name, string content) { Name = name; Content = content; }
            public string Name { get; }
            public string Content { get; }
        }
    }
}
