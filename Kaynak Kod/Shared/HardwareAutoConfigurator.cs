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
        private static readonly object ManagedRouteSync = new();
        private static readonly HashSet<int> ManagedRouteInterfaceIndexes = new();
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

        public static Task<string> EnsureNetworkServerIpAsync(
            AppConfig config, ISet<string> before, TimeSpan timeout, CancellationToken ct)
        {
            return EnsureNetworkServerIpAsync(config, before, 1, timeout, ct);
        }

        public static async Task<string> EnsureNetworkServerIpAsync(
            AppConfig config, ISet<string> before, int targetNumber, TimeSpan timeout, CancellationToken ct)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (targetNumber < 1 || targetNumber > 6)
                throw new ArgumentOutOfRangeException(nameof(targetNumber), "UKB hedefi 1-6 aralığında olmalıdır.");

            string desiredAddress = config.AutoDetectNetworkServerIp
                ? GetTargetNetworkServerIp(config.NetworkServerIp, targetNumber)
                : config.NetworkServerIp;
            DateTime startedAt = DateTime.UtcNow;
            DateTime deadline = startedAt + timeout;
            Exception lastError = null;
            bool waitingLogged = false;

            Logger.Checkpoint(
                "USB_NCM_TARGET_ADDRESS",
                "SELECTED",
                $"ukb=UKB{targetNumber}; base={config.NetworkServerIp}; selected={desiredAddress}; auto={config.AutoDetectNetworkServerIp}");

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (!config.AutoDetectNetworkServerIp)
                {
                    if (InterfaceOwnsAddress(null, desiredAddress, out NetworkInterface configuredAdapter) &&
                        await WaitForBindableIpv4Async(desiredAddress, TimeSpan.FromSeconds(2), ct))
                    {
                        Logger.Checkpoint(
                            "USB_NCM_IPV4_BIND",
                            "SUCCESS",
                            "address=" + desiredAddress + "; source=configured; adapter=" + configuredAdapter.Name);
                        return desiredAddress;
                    }

                    throw new InvalidOperationException(
                        "Yapılandırılan USB-NCM PC adresi etkin bir Windows adaptörüne bağlı değil: " + desiredAddress);
                }
                NetworkInterface candidate = FindUsbNcmCandidate(
                    before,
                    desiredAddress,
                    out bool candidateIsNew,
                    out string detail);
                if (candidate == null)
                {
                    if (!waitingLogged)
                    {
                        waitingLogged = true;
                        Logger.Checkpoint(
                            "USB_NCM_TARGET_ADAPTER",
                            "WAITING",
                            $"ukb=UKB{targetNumber}; graceSeconds=7; reason=selected-target-new-adapter-not-ready");
                    }
                    await Task.Delay(500, ct);
                    continue;
                }

                try
                {
                    int interfaceIndex = GetIpv4InterfaceIndex(candidate);
                    Logger.Checkpoint(
                        "USB_NCM_TARGET_ADAPTER",
                        "SELECTED",
                        $"ukb=UKB{targetNumber}; {detail}; id={candidate.Id}; ifIndex={interfaceIndex}; address={desiredAddress}");

                    if (!InterfaceOwnsAddress(candidate.Id, desiredAddress, out _))
                    {
                        Logger.Checkpoint(
                            "USB_NCM_IPV4_AUTOCONFIG",
                            "START",
                            $"ukb=UKB{targetNumber}; adapter={candidate.Name}; address={desiredAddress}/24");
                        await AssignStaticIpv4Async(candidate.Name, desiredAddress, ct);
                    }
                    else
                    {
                        Logger.Checkpoint(
                            "USB_NCM_IPV4_AUTOCONFIG",
                            "SKIPPED",
                            $"ukb=UKB{targetNumber}; adapter={candidate.Name}; address={desiredAddress}/24; reason=already-assigned");
                    }

                    string assigned = await WaitForAssignedIpv4Async(
                        desiredAddress, candidate, targetNumber, ct);
                    await EnsureSelectedTargetRouteAsync(
                        config.UkbTargetIp, candidate, targetNumber, ct);
                    return assigned;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    Logger.Checkpoint(
                        "USB_NCM_TARGET_ADAPTER",
                        "RETRY",
                        $"ukb=UKB{targetNumber}; adapter={candidate.Name}; error={ex.GetType().Name}; message={ex.Message}");
                }

                await Task.Delay(500, ct);
            }

            throw new InvalidOperationException(
                (lastError?.Message ?? "Seçili UKB için yeni USB-NCM adaptörü bulunamadı.") +
                $" UKB{targetNumber} adaptörü diğer etkin USB ağ adaptörlerinden güvenli biçimde ayırt edilemedi. " +
                "Başka bir UKB'nin açık kalan adaptörü kullanılmadı; seçili UKB'nin OTG kablosunu kontrol edin.");
        }
        public static string GetTargetNetworkServerIp(string baseAddress, int targetNumber)
        {
            if (targetNumber < 1 || targetNumber > 6)
                throw new ArgumentOutOfRangeException(nameof(targetNumber), "UKB hedefi 1-6 aralığında olmalıdır.");
            if (!IPAddress.TryParse(baseAddress, out IPAddress parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçersiz USB-NCM PC IPv4 adresi: " + baseAddress);

            byte[] bytes = parsed.GetAddressBytes();
            int host = bytes[3] + targetNumber - 1;
            if (host < 2 || host > 254)
                throw new InvalidOperationException(
                    $"UKB1-UKB6 için ayrılacak PC adresleri IPv4 aralığını aşıyor. Başlangıç adresi={baseAddress}; hedef=UKB{targetNumber}.");
            bytes[3] = (byte)host;
            return new IPAddress(bytes).ToString();
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
                        ". Seçili UKB adaptörüyle eşleşme yapılmalıdır.");
            }

            throw new InvalidOperationException(
                "UKB Easy Installer USB-NCM adresiyle aynı ağda bir PC adresi bulunamadı. " +
                "Hedef=" + config.UkbTargetIp + "; aktif PC IPv4 adresleri=" +
                (local.Length == 0 ? "yok" : string.Join(", ", local)) + ".");
        }

        private static NetworkInterface FindUsbNcmCandidate(
            ISet<string> before,
            string desiredAddress,
            out bool candidateIsNew,
            out string detail)
        {
            var eligible = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsEligibleUsbNcmCandidate)
                .ToList();
            var newAdapters = eligible
                .Where(adapter => before == null || !before.Contains(adapter.Id))
                .ToList();

            NetworkInterface candidate = newAdapters.FirstOrDefault(adapter =>
                    IsUsbNcmName(adapter.Name + " " + adapter.Description))
                ?? (newAdapters.Count == 1 ? newAdapters[0] : null);
            candidateIsNew = candidate != null;

            if (candidate != null)
            {
                bool isUsb = IsUsbNcmName(candidate.Name + " " + candidate.Description);
                detail = $"adapter={candidate.Name}; description={candidate.Description}; new=true; " +
                    $"usbLike={isUsb}; eligible={eligible.Count}; policy=new-or-same-target-owner";
                return candidate;
            }

            // Windows ayni fiziksel OTG baglantisini sonraki yuklemede yeniden kullanabilir.
            // UKB1-UKB6 icin PC adresleri benzersiz oldugundan yalnizca secili hedefin
            // adresini tasiyan etkin adaptoru geri kullanmak, baska UKB adaptoru secmez.
            NetworkInterface sameTargetOwner = eligible.FirstOrDefault(adapter =>
                InterfaceOwnsAddress(adapter.Id, desiredAddress, out _));
            if (sameTargetOwner != null)
            {
                candidateIsNew = false;
                bool isUsb = IsUsbNcmName(sameTargetOwner.Name + " " + sameTargetOwner.Description);
                detail = $"adapter={sameTargetOwner.Name}; description={sameTargetOwner.Description}; new=false; " +
                    $"usbLike={isUsb}; eligible={eligible.Count}; reuse=same-target-address-owner; " +
                    "policy=new-or-same-target-owner";
                return sameTargetOwner;
            }

            detail = $"candidate=none; eligible={eligible.Count}; new={newAdapters.Count}; " +
                "sameTargetOwner=none; policy=new-or-same-target-owner";
            return null;
        }        private static bool IsEligibleUsbNcmCandidate(NetworkInterface adapter)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) return false;
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return false;
            try
            {
                return !adapter.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address?.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any));
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        }

        private static bool InterfaceOwnsAddress(
            string interfaceId, string address, out NetworkInterface owner)
        {
            owner = null;
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (!string.IsNullOrWhiteSpace(interfaceId) &&
                    !string.Equals(adapter.Id, interfaceId, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    bool owns = adapter.GetIPProperties().UnicastAddresses.Any(item =>
                        item.Address.AddressFamily == AddressFamily.InterNetwork &&
                        string.Equals(item.Address.ToString(), address, StringComparison.OrdinalIgnoreCase));
                    if (!owns) continue;
                    owner = adapter;
                    return true;
                }
                catch (NetworkInformationException)
                {
                    // Adapter durumu sorgu sırasında değişebilir; sonraki adaptörü dene.
                }
            }
            return false;
        }

        private static int GetIpv4InterfaceIndex(NetworkInterface adapter)
        {
            try
            {
                return adapter.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
            }
            catch (NetworkInformationException)
            {
                return -1;
            }
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
            string desiredAddress,
            NetworkInterface candidate,
            int targetNumber,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (InterfaceOwnsAddress(candidate.Id, desiredAddress, out NetworkInterface refreshed) &&
                    await WaitForBindableIpv4Async(desiredAddress, TimeSpan.FromSeconds(2), ct))
                {
                    Logger.Checkpoint(
                        "USB_NCM_IPV4_AUTOCONFIG",
                        "SUCCESS",
                        $"ukb=UKB{targetNumber}; adapter={refreshed.Name}; id={refreshed.Id}; " +
                        $"ifIndex={GetIpv4InterfaceIndex(refreshed)}; address={desiredAddress}/24; bindStable=true");
                    return desiredAddress;
                }
                await Task.Delay(500, ct);
            }

            string actualOwner = InterfaceOwnsAddress(null, desiredAddress, out NetworkInterface owner)
                ? owner.Name
                : "none";
            throw new InvalidOperationException(
                "USB-NCM IPv4 adresi atandı ancak seçili adaptörde etkinleşmedi. Adapter=" +
                candidate.Name + "; address=" + desiredAddress + "/24; actualOwner=" + actualOwner);
        }

        private static async Task EnsureSelectedTargetRouteAsync(
            string targetAddress,
            NetworkInterface candidate,
            int targetNumber,
            CancellationToken ct)
        {
            if (!IPAddress.TryParse(targetAddress, out IPAddress parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçersiz UKB hedef IPv4 adresi: " + targetAddress);

            int interfaceIndex = GetIpv4InterfaceIndex(candidate);
            if (interfaceIndex < 0)
                throw new InvalidOperationException(
                    "Seçili USB-NCM adaptörünün IPv4 arayüz indeksi okunamadı: " + candidate.Name);

            // Bütün UKB'ler aynı hedef IP'yi kullandığı için önce uygulamanın daha önce
            // oluşturduğu olası /32 rotaları aday USB ağ arayüzlerinden temizle.
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(IsEligibleUsbNcmCandidate))
            {
                int staleIndex = GetIpv4InterfaceIndex(adapter);
                if (staleIndex < 0) continue;
                await RunNetworkCommandAsync(
                    "netsh.exe",
                    new[]
                    {
                        "interface", "ipv4", "delete", "route",
                        "prefix=" + targetAddress + "/32",
                        "interface=" + staleIndex,
                        "store=active"
                    },
                    ct,
                    failOnNonZeroExit: false,
                    checkpoint: "USB_NCM_TARGET_ROUTE_CLEANUP");
            }

            // /32 rota, Windows'un aynı 192.168.11.0/24 ağına sahip diğer UKB
            // adaptörlerinden birini seçmesini önler.
            await RunNetworkCommandAsync(
                "netsh.exe",
                new[]
                {
                    "interface", "ipv4", "add", "route",
                    "prefix=" + targetAddress + "/32",
                    "interface=" + interfaceIndex,
                    "nexthop=0.0.0.0",
                    "metric=1",
                    "store=active"
                },
                ct,
                failOnNonZeroExit: true,
                checkpoint: "USB_NCM_TARGET_ROUTE");

            lock (ManagedRouteSync)
                ManagedRouteInterfaceIndexes.Add(interfaceIndex);
            Logger.Checkpoint(
                "USB_NCM_TARGET_ROUTE",
                "SUCCESS",
                $"ukb=UKB{targetNumber}; target={targetAddress}/32; adapter={candidate.Name}; ifIndex={interfaceIndex}");
        }
        public static async Task CleanupManagedTargetRoutesAsync(
            string targetAddress,
            CancellationToken ct)
        {
            int[] indexes;
            lock (ManagedRouteSync)
                indexes = ManagedRouteInterfaceIndexes.ToArray();

            foreach (int interfaceIndex in indexes)
            {
                ct.ThrowIfCancellationRequested();
                await RunNetworkCommandAsync(
                    "netsh.exe",
                    new[]
                    {
                        "interface", "ipv4", "delete", "route",
                        "prefix=" + targetAddress + "/32",
                        "interface=" + interfaceIndex,
                        "store=active"
                    },
                    ct,
                    failOnNonZeroExit: false,
                    checkpoint: "USB_NCM_TARGET_ROUTE_RELEASE");
                lock (ManagedRouteSync)
                    ManagedRouteInterfaceIndexes.Remove(interfaceIndex);
                Logger.Checkpoint(
                    "USB_NCM_TARGET_ROUTE_RELEASE",
                    "SUCCESS",
                    $"target={targetAddress}/32; ifIndex={interfaceIndex}");
            }
        }
        private static async Task RunNetworkCommandAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken ct,
            bool failOnNonZeroExit,
            string checkpoint)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
                psi.ArgumentList.Add(argument);

            using Process process = Process.Start(psi) ??
                throw new InvalidOperationException("Windows ağ yapılandırma aracı başlatılamadı: " + fileName);
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            string error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            Logger.Checkpoint(
                checkpoint,
                process.ExitCode == 0 ? "SUCCESS" : (failOnNonZeroExit ? "FAILED" : "IGNORED"),
                "command=" + fileName + "; exitCode=" + process.ExitCode +
                "; output=" + output.Trim() + "; error=" + error.Trim());
            if (failOnNonZeroExit && process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Seçili UKB için Windows hedef rotası oluşturulamadı. " + error.Trim());
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
