using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SurumYakma
{
    public static class HardwareAutoConfigurator
    {
        public static string ApplyStartupDetection(
            AppConfig config,
            string projectName,
            bool preserveConfiguredProfile = false)
        {
            var changes = new List<string>();
            bool machineOverride = !string.IsNullOrWhiteSpace(config.LoadedConfigPath) &&
                !string.Equals(
                    System.IO.Path.GetFileName(config.LoadedConfigPath),
                    "appsettings.json",
                    StringComparison.OrdinalIgnoreCase);

            string normalized = Normalize(projectName);
            if (!config.HardwareTestMode && !preserveConfiguredProfile &&
                (normalized == "YFYK" || normalized.StartsWith("YFYK ", StringComparison.Ordinal)))
            {
                // MOXA Discreate YFYK/ANKA-I resmi eslemeleri:
                // UKS gucu MOXA-1 MOD8/CH4, Recovery MOXA-2 MOD8/CH4.
                // IP adresleri makineye ozeldir; kullanicinin girdigi IP'ler korunur.
                config.PowerSlot = 8;
                config.PowerChannel1 = 4;
                config.PowerChannel1Secondary = -1;
                config.RecoverySlot = 8;
                config.RecoveryChannel1 = 4;
                changes.Add("YFYK profili: UKS Power=MOD8/CH4, Recovery=MOD8/CH4 (IP'ler korundu)");
            }

            if (!config.HardwareTestMode && !machineOverride && !preserveConfiguredProfile &&
                (normalized == "KSIMSEK" || normalized == "KS" || normalized == "SIMSEK"))
            {
                config.PowerBoxIp = "10.135.1.60";
                config.RelayBoxIp = "10.135.1.40";
                config.PowerSlot = 1;
                config.PowerChannel1 = 0;
                config.PowerChannel1Secondary = 1;
                config.RecoverySlot = 7;
                config.RecoveryChannel1 = 7;
                changes.Add("KSIMSEK profili: Power=MOD1/CH0+CH1, Recovery=MOD7/CH7");
            }

            if (preserveConfiguredProfile)
                changes.Add("Settings.json donanım profili korundu; sabit platform varsayılanları uygulanmadı");

            if (config.AutoDetectSerialPort)
            {
                string detected = DetectSerialPort(config.SerialPortName, out string detail);
                if (!string.IsNullOrWhiteSpace(detected) &&
                    !detected.Equals(config.SerialPortName, StringComparison.OrdinalIgnoreCase))
                {
                    config.SerialPortName = detected;
                    changes.Add("Seri port=" + detected + " (" + detail + ")");
                }
                else if (!string.IsNullOrWhiteSpace(detail))
                {
                    changes.Add(detail);
                }
            }

            // Birleşik tabloda otomatik donanım profili UKB1 satırına uygulanır.
            UkbTargetConfig first = config.GetTarget(1);
            first.ComPort = config.SerialPortName;
            first.BaudRate = config.SerialBaudRate;
            first.PowerSlot = config.PowerSlot;
            first.PowerChannel = config.PowerChannel1;
            first.PowerSecondaryChannel = config.PowerChannel1Secondary;
            first.RecoverySlot = config.RecoverySlot;
            first.RecoveryChannel = config.RecoveryChannel1;
            config.SwarmModeEnabled = true;

            return changes.Count == 0 ? "Otomatik değişiklik gerekmedi." : string.Join("; ", changes);
        }

        public static string[] GetAvailableSerialPorts()
        {
            return SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PortNumber)
                .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string DetectSerialPort(string configured, out string detail)
        {
            string[] ports = GetAvailableSerialPorts();
            if (ports.Any(port => port.Equals(configured, StringComparison.OrdinalIgnoreCase)))
            {
                detail = "Yapılandırılmış seri port aktif: " + configured;
                return configured;
            }

            if (ports.Length == 1)
            {
                detail = "Tek aktif seri port otomatik seçildi";
                return ports[0];
            }

            string[] usbCandidates = GetUsbSerialCandidates(ports);
            if (usbCandidates.Length == 1)
            {
                detail = "Tek USB/VCP seri port otomatik seçildi";
                return usbCandidates[0];
            }

            detail = ports.Length == 0
                ? "Aktif COM portu bulunamadı."
                : "Birden fazla COM portu bulundu; güvenli otomatik seçim yapılamadı: " + string.Join(", ", ports);
            return configured;
        }

        private static string[] GetUsbSerialCandidates(string[] activePorts)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key == null)
                    return result.ToArray();
                foreach (string name in key.GetValueNames())
                {
                    string port = Convert.ToString(key.GetValue(name));
                    if (activePorts.Contains(port, StringComparer.OrdinalIgnoreCase) &&
                        (name.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("VCP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("FTDI", StringComparison.OrdinalIgnoreCase) >= 0))
                        result.Add(port);
                }
            }
            catch
            {
            }
            return result.OrderBy(PortNumber).ToArray();
        }

        public static string[] GetLocalIpv4Addresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork &&
                               !IPAddress.IsLoopback(item.Address))
                .Select(item => item.Address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static HashSet<string> GetNetworkInterfaceIds()
        {
            return new HashSet<string>(
                NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .Select(adapter => adapter.Id),
                StringComparer.OrdinalIgnoreCase);
        }

        public static async Task<string> EnsureNetworkServerIpAsync(
            AppConfig config, ISet<string> before, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            Exception lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string resolved = ResolveNetworkServerIp(config);
                    if (await WaitForBindableIpv4Async(
                            resolved, TimeSpan.FromSeconds(2), ct))
                    {
                        Logger.Checkpoint("USB_NCM_IPV4_BIND", "SUCCESS",
                            "address=" + resolved + "; source=existing");
                        return resolved;
                    }
                    lastError = new InvalidOperationException(
                        "USB-NCM IPv4 adresi listede bulundu ancak soket bağlanabilir değil: " + resolved);
                    Logger.Checkpoint("USB_NCM_IPV4_BIND", "RETRY",
                        "address=" + resolved + "; reason=socket-bind-unavailable");
                }
                catch (InvalidOperationException ex) { lastError = ex; }
                if (!config.AutoDetectNetworkServerIp) throw lastError;
                NetworkInterface candidate = FindUsbNcmCandidate(before, out string detail);
                if (candidate != null)
                {
                    Logger.Checkpoint("USB_NCM_IPV4_AUTOCONFIG", "START", detail);
                    await AssignStaticIpv4Async(candidate.Name, config.NetworkServerIp, ct);
                    return await WaitForAssignedIpv4Async(config, candidate, ct);
                }
                await Task.Delay(500, ct);
            }
            throw new InvalidOperationException(
                (lastError?.Message ?? "USB-NCM IPv4 adresi bulunamadı.") +
                " USB-NCM bağdaştırıcısı güvenli biçimde belirlenemedi.");
        }

        public static string ResolveNetworkServerIp(AppConfig config)
        {
            string[] local = GetLocalIpv4Addresses();
            if (!config.AutoDetectNetworkServerIp &&
                local.Contains(config.NetworkServerIp, StringComparer.OrdinalIgnoreCase))
                return config.NetworkServerIp;

            if (IPAddress.TryParse(config.UkbTargetIp, out IPAddress target))
            {
                byte[] targetBytes = target.GetAddressBytes();
                string[] sameSubnet = local.Where(value =>
                {
                    byte[] bytes = IPAddress.Parse(value).GetAddressBytes();
                    return bytes.Length == 4 && targetBytes.Length == 4 &&
                           bytes[0] == targetBytes[0] && bytes[1] == targetBytes[1] && bytes[2] == targetBytes[2] &&
                           bytes[3] != targetBytes[3];
                }).ToArray();
                if (sameSubnet.Contains(config.NetworkServerIp, StringComparer.OrdinalIgnoreCase))
                    return config.NetworkServerIp;
                if (sameSubnet.Length == 1)
                    return sameSubnet[0];
                if (sameSubnet.Length > 1)
                    throw new InvalidOperationException(
                        "UKB ile aynı USB-NCM ağında birden fazla PC adresi bulundu: " + string.Join(", ", sameSubnet) +
                        ". Bağlantı Ayarları ekranından doğru PC adresini seçip otomatik IP seçimini kapatın.");
            }

            throw new InvalidOperationException(
                "UKB Easy Installer USB-NCM adresiyle aynı ağda bir PC adresi bulunamadı. " +
                "Hedef=" + config.UkbTargetIp + "; aktif PC IPv4 adresleri=" +
                (local.Length == 0 ? "yok" : string.Join(", ", local)) + ".");
        }

        private static NetworkInterface FindUsbNcmCandidate(ISet<string> before, out string detail)
        {
            NetworkInterface fallback = null;
            foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    n.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                    n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) continue;
                bool hasIpv4Gateway = n.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address?.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any));
                if (hasIpv4Gateway) continue;
                bool isNew = before == null || !before.Contains(n.Id);
                bool isUsb = IsUsbNcmName(n.Name + " " + n.Description);
                if (!isNew && !isUsb) continue;
                if (isUsb)
                {
                    detail = "adapter=" + n.Name + "; usbLike=true";
                    return n;
                }
                fallback ??= n;
            }
            detail = fallback == null ? "candidate=none" : "adapter=" + fallback.Name + "; new=true";
            return fallback;
        }

        private static bool IsUsbNcmName(string value)
        {
            string n = (value ?? "").ToUpperInvariant();
            return n.Contains("NCM") || n.Contains("RNDIS") || n.Contains("GADGET") ||
                   n.Contains("TORADEX") || n.Contains("USB ETHERNET") ||
                   n.Contains("USB 10/") || n.Contains("USB GBE") || n.Contains("USB LAN");
        }

        private static async Task<bool> WaitForBindableIpv4Async(
            string address, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int consecutiveSuccesses = 0;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var socket = new Socket(
                        AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Parse(address), 0));
                    consecutiveSuccesses++;
                    if (consecutiveSuccesses >= 3) return true;
                }
                catch (SocketException)
                {
                    consecutiveSuccesses = 0;
                }
                await Task.Delay(250, ct);
            }
            return false;
        }

        private static async Task<string> WaitForAssignedIpv4Async(
            AppConfig config, NetworkInterface candidate, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (GetLocalIpv4Addresses().Contains(
                        config.NetworkServerIp, StringComparer.OrdinalIgnoreCase))
                {
                    if (await WaitForBindableIpv4Async(
                            config.NetworkServerIp, TimeSpan.FromSeconds(2), ct))
                    {
                        Logger.Checkpoint("USB_NCM_IPV4_AUTOCONFIG", "SUCCESS",
                            "adapter=" + candidate.Name + "; address=" + config.NetworkServerIp +
                            "/24; bindStable=true");
                        return ResolveNetworkServerIp(config);
                    }
                }
                await Task.Delay(500, ct);
            }
            throw new InvalidOperationException(
                "USB-NCM IPv4 adresi atandı ancak Windows adresi etkinleştirmedi. Adapter=" +
                candidate.Name + "; address=" + config.NetworkServerIp + "/24");
        }

        private static async Task AssignStaticIpv4Async(
            string interfaceName, string address, CancellationToken ct)
        {
            if (!IPAddress.TryParse(address, out IPAddress parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçersiz USB-NCM PC IPv4 adresi: " + address);

            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException(
                        "USB-NCM IPv4 adresini otomatik atamak için uygulamayı yönetici olarak çalıştırın.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("interface");
            psi.ArgumentList.Add("ipv4");
            psi.ArgumentList.Add("set");
            psi.ArgumentList.Add("address");
            psi.ArgumentList.Add("name=" + interfaceName);
            psi.ArgumentList.Add("source=static");
            psi.ArgumentList.Add("address=" + address);
            psi.ArgumentList.Add("mask=255.255.255.0");
            psi.ArgumentList.Add("gateway=none");
            psi.ArgumentList.Add("store=active");

            using Process process = Process.Start(psi) ??
                throw new InvalidOperationException("Windows ağ yapılandırma aracı başlatılamadı.");
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            string error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            Logger.Checkpoint("USB_NCM_IPV4_NETSH",
                process.ExitCode == 0 ? "SUCCESS" : "FAILED",
                "adapter=" + interfaceName + "; exitCode=" + process.ExitCode +
                "; output=" + output.Trim() + "; error=" + error.Trim());
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "USB-NCM IPv4 adresi Windows tarafından atanamadı. " + error.Trim());
        }

        private static int PortNumber(string value)
        {
            if (value != null && value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value.Substring(3), out int number))
                return number;
            return int.MaxValue;
        }

        private static string Normalize(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace("İ", "I")
                .Replace("Ş", "S")
                .Replace("Ğ", "G")
                .Replace("Ü", "U")
                .Replace("Ö", "O")
                .Replace("Ç", "C")
                .ToUpperInvariant();
        }
    }
}
