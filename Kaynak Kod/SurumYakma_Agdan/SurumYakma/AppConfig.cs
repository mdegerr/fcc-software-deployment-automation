using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurumYakma
{
    public sealed class AppConfigException : Exception
    {
        public AppConfigException(string message) : base(message) { }
        public AppConfigException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class UkbTargetConfig
    {
        public int UkbNumber { get; set; }
        public string ComPort { get; set; } = "";
        public int BaudRate { get; set; } = 115200;
        public byte PowerSlot { get; set; }
        public byte PowerChannel { get; set; }
        public int PowerSecondaryChannel { get; set; } = -1;
        public byte RecoverySlot { get; set; }
        public byte RecoveryChannel { get; set; }

        public static List<UkbTargetConfig> CreateSwarmDefaults()
        {
            return Enumerable.Range(1, 6)
                .Select(number => new UkbTargetConfig
                {
                    UkbNumber = number,
                    ComPort = number == 1 ? "COM22" : "",
                    PowerSlot = 0,
                    PowerChannel = (byte)(number - 1),
                    RecoverySlot = 0,
                    RecoveryChannel = (byte)(number - 1)
                })
                .ToList();
        }
    }

    public sealed class AppConfig
    {
        public bool HardwareTestMode { get; set; }
        public string ProfileName { get; set; } = "Üretim";
        public bool TwoUkb { get; set; }
        public int SelectedUkb { get; set; } = 1;
        public bool SwarmModeEnabled { get; set; }
        public List<UkbTargetConfig> SwarmUkbTargets { get; set; } =
            UkbTargetConfig.CreateSwarmDefaults();
        public bool OnlyFlash { get; set; } = true;
        public string SurumlerPath { get; set; } = "";
        public string VersionsRootPath { get; set; } = "";

        public string PowerBoxIp { get; set; } = "";
        public string RelayBoxIp { get; set; } = "";
        public byte PowerSlot { get; set; }
        public int PowerSlot2 { get; set; } = -1;
        public byte PowerChannel1 { get; set; }
        public int PowerChannel1Secondary { get; set; } = -1;
        public byte PowerChannel2 { get; set; } = 1;
        public int PowerChannel2Secondary { get; set; } = -1;
        public byte RecoverySlot { get; set; }
        public int RecoverySlot2 { get; set; } = -1;
        public byte RecoveryChannel1 { get; set; }
        public byte RecoveryChannel2 { get; set; } = 1;

        public string TeraName { get; set; } = "ttermpro";
        public string TesterName { get; set; } = "Moxa";
        public string RecoveryBatPath { get; set; } = @"tools\tezi\recovery-windows.bat";

        public bool SerialMonitorEnabled { get; set; } = true;
        public string SerialPortName { get; set; } = "COM22";
        public string SerialPortName2 { get; set; } = "COM23";
        public bool AutoDetectSerialPort { get; set; } = true;
        public int SerialBaudRate { get; set; } = 115200;
        public int SerialRecoveryTimeoutSeconds { get; set; } = 180;
        public int SerialBootTimeoutSeconds { get; set; } = 300;
        public bool EasyInstallerAutoLoginEnabled { get; set; }
        public string EasyInstallerUserName { get; set; } = "";
        public string EasyInstallerPasswordProtected { get; set; } = "";
        public bool ValidateTeziPackageNamePrefix { get; set; } = true;
        public List<ProjectPackageMapping> ProjectPackageMappings { get; set; } =
            ProjectPackageMapping.CreateDefaults();

        [JsonIgnore]
        public string EasyInstallerPassword
        {
            get => ProtectedSecret.Unprotect(EasyInstallerPasswordProtected);
            set => EasyInstallerPasswordProtected = ProtectedSecret.Protect(value);
        }

        public int AfterBatWaitTimeSeconds { get; set; } = 10;
        public string UkbTargetIp { get; set; } = "192.168.11.1";
        public int PingTimeoutMs { get; set; } = 1000;
        public int OtgWaitTimeoutSeconds { get; set; } = 600;
        public int RecoveryBatTimeoutSeconds { get; set; } = 300;
        public int InstallationTimeoutSeconds { get; set; } = 1800;
        public int RecoveryNetworkTimeoutSeconds { get; set; } = 180;
        public int MaxRelayRetries { get; set; } = 100;
        public int MoxaConnectTimeoutMs { get; set; } = 10000;
        public int MoxaReconnectAttempts { get; set; } = 3;
        public int MoxaKeepAliveIntervalSeconds { get; set; } = 15;
        [JsonIgnore]
        public string LoadedConfigPath { get; private set; } = "";

        public bool NetworkInstallMode { get; set; } = true;
        public string NetworkServerIp { get; set; } = "192.168.11.221";
        // Toradex'in resmi "images serve" ve Avahi _tezi._tcp servisi port 80 kullanir.
        public int NetworkServerPort { get; set; } = 80;
        public bool AutoDetectNetworkServerIp { get; set; } = true;

        public int GetTargetNumber(bool whichUkb) => SelectedUkb;

        public UkbTargetConfig GetTarget(int targetNumber)
        {
            EnsureSwarmTargets();
            return SwarmUkbTargets.First(target => target.UkbNumber == targetNumber);
        }

        public string GetSerialPort(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).ComPort;
        }

        public int GetSerialBaudRate(bool whichUkb)
            => GetTarget(GetTargetNumber(whichUkb)).BaudRate;

        public byte GetPowerSlot(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).PowerSlot;
        }

        public byte GetPowerChannel(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).PowerChannel;
        }

        public int GetPowerSecondaryChannel(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).PowerSecondaryChannel;
        }

        public byte GetRecoverySlot(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).RecoverySlot;
        }

        public byte GetRecoveryChannel(bool whichUkb)
        {
            int target = GetTargetNumber(whichUkb);
            return GetTarget(target).RecoveryChannel;
        }

        private void EnsureSwarmTargets()
        {
            var existing = (SwarmUkbTargets ?? new List<UkbTargetConfig>())
                .Where(target => target != null && target.UkbNumber >= 1 && target.UkbNumber <= 6)
                .GroupBy(target => target.UkbNumber)
                .ToDictionary(group => group.Key, group => group.First());
            SwarmUkbTargets = Enumerable.Range(1, 6)
                .Select(number => existing.TryGetValue(number, out UkbTargetConfig target)
                    ? target
                    : UkbTargetConfig.CreateSwarmDefaults().First(item => item.UkbNumber == number))
                .OrderBy(target => target.UkbNumber)
                .ToList();
        }

        public void MigrateLegacyTargetsToUnifiedTable()
        {
            EnsureSwarmTargets();
            UkbTargetConfig first = GetTarget(1);
            first.ComPort = SerialPortName;
            first.BaudRate = SerialBaudRate;
            first.PowerSlot = PowerSlot;
            first.PowerChannel = PowerChannel1;
            first.PowerSecondaryChannel = PowerChannel1Secondary;
            first.RecoverySlot = RecoverySlot;
            first.RecoveryChannel = RecoveryChannel1;

            UkbTargetConfig second = GetTarget(2);
            second.ComPort = SerialPortName2;
            second.BaudRate = SerialBaudRate;
            second.PowerSlot = PowerSlot2 >= 0 ? (byte)PowerSlot2 : PowerSlot;
            second.PowerChannel = PowerChannel2;
            second.PowerSecondaryChannel = PowerChannel2Secondary;
            second.RecoverySlot = RecoverySlot2 >= 0 ? (byte)RecoverySlot2 : RecoverySlot;
            second.RecoveryChannel = RecoveryChannel2;
            SwarmModeEnabled = true;
        }

        public void OverrideSurumlerPath(string path)
        {
            SurumlerPath = path;
        }

        public string GetVersionsRootPath()
        {
            if (!string.IsNullOrWhiteSpace(VersionsRootPath))
                return VersionsRootPath;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                desktop = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Desktop");
            return Path.Combine(desktop, "UKB Sürümleri");
        }

        public string GetPlatformVersionsPath(string projectName)
        {
            string platform = (projectName ?? "").Trim();
            if (platform.Length == 0 || platform.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "";

            string root = GetVersionsRootPath();
            string expected = Path.Combine(root, platform);
            if (!Directory.Exists(root))
                return expected;

            string exact = Directory.GetDirectories(root)
                .FirstOrDefault(path => Path.GetFileName(path).Equals(
                    platform,
                    StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
                return exact;

            string platformKey = NormalizePlatformFolderKey(platform);
            return Directory.GetDirectories(root)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => NormalizePlatformFolderKey(Path.GetFileName(path)) == platformKey)
                ?? expected;
        }

        public static string NormalizePlatformFolderKey(string value)
        {
            string expanded = (value ?? "")
                .Replace('ı', 'i')
                .Replace('İ', 'I')
                .Normalize(NormalizationForm.FormD);
            var result = new StringBuilder(expanded.Length);
            foreach (char character in expanded)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(character))
                    result.Append(char.ToUpperInvariant(character));
            }
            return result.ToString();
        }

        public void ValidateForSave()
        {
            Validate();
        }

        public string SaveMachineSpecific()
        {
            string path = Path.Combine(AppContext.BaseDirectory, $"appsettings.{Environment.MachineName}.json");
            string recoveryPath = RecoveryBatPath;
            string versionsPath = SurumlerPath;
            string versionsRootPath = VersionsRootPath;
            try
            {
                RecoveryBatPath = MakePortablePath(RecoveryBatPath);
                SurumlerPath = MakePortablePath(SurumlerPath);
                VersionsRootPath = MakePortablePath(VersionsRootPath);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            finally
            {
                RecoveryBatPath = recoveryPath;
                SurumlerPath = versionsPath;
                VersionsRootPath = versionsRootPath;
            }
            LoadedConfigPath = path;
            return path;
        }

        private static string MakePortablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                return path;
            string relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
            return relative.Equals("..", StringComparison.Ordinal) ||
                   relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? path
                : relative;
        }

        public static AppConfig Load()
        {
            bool testProfile = Array.Exists(
                Environment.GetCommandLineArgs(),
                arg => arg.Equals("--test-hardware", StringComparison.OrdinalIgnoreCase));
            string configPath;
            if (testProfile)
            {
                configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.test.json");
            }
            else
            {
                string requestedConfig =
                    (Environment.GetEnvironmentVariable("SURUM_YAKMA_CONFIG") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(requestedConfig))
                {
                    configPath = Path.IsPathRooted(requestedConfig)
                        ? requestedConfig
                        : Path.Combine(AppContext.BaseDirectory, requestedConfig);
                }
                else
                {
                    string machineConfig = Path.Combine(
                        AppContext.BaseDirectory,
                        $"appsettings.{Environment.MachineName}.json");
                    configPath = File.Exists(machineConfig)
                        ? machineConfig
                        : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                }
            }
            if (!File.Exists(configPath))
                throw new AppConfigException("Yapılandırma dosyası bulunamadı: " + configPath);

            AppConfig config;
            try
            {
                string json = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<AppConfig>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            {
                throw new AppConfigException("appsettings.json okunamadı: " + ex.Message, ex);
            }

            if (config == null)
                throw new AppConfigException("appsettings.json boş veya geçersiz.");

            if (config.ProjectPackageMappings == null || config.ProjectPackageMappings.Count == 0)
                config.ProjectPackageMappings = ProjectPackageMapping.CreateDefaults();
            config.EnsureSwarmTargets();
            if (!config.SwarmModeEnabled)
                config.MigrateLegacyTargetsToUnifiedTable();

            config.LoadedConfigPath = Path.GetFullPath(configPath);
            config.ResolvePaths();
            config.Validate();
            return config;
        }

        private void ResolvePaths()
        {
            RecoveryBatPath = ResolveRelativePath(RecoveryBatPath);
            if (!string.IsNullOrWhiteSpace(SurumlerPath))
                SurumlerPath = ResolveRelativePath(SurumlerPath);
            if (!string.IsNullOrWhiteSpace(VersionsRootPath))
                VersionsRootPath = ResolveRelativePath(VersionsRootPath);
        }

        private static string ResolveRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                return path;

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }

        private void Validate()
        {
            var errors = new List<string>();

            RequireText(PowerBoxIp, "PowerBoxIp", errors);
            RequireText(RelayBoxIp, "RelayBoxIp", errors);
            RequireText(UkbTargetIp, "UkbTargetIp", errors);
            RequireText(RecoveryBatPath, "RecoveryBatPath", errors);

            if (!File.Exists(RecoveryBatPath))
                errors.Add("Recovery BAT dosyası bulunamadı: " + RecoveryBatPath);

            if (typeof(AppConfig).Assembly.GetManifestResourceInfo(
                    "SurumYakma.Tools.Tezi.Recovery.uuu.exe") == null &&
                !File.Exists(Path.Combine(
                    Path.GetDirectoryName(RecoveryBatPath) ?? "",
                    "recovery",
                    "uuu.exe")))
                errors.Add("Toradex UUU aracı recovery\\uuu.exe konumunda bulunamadı.");

            RequirePositive(PingTimeoutMs, "PingTimeoutMs", errors);
            RequirePositive(OtgWaitTimeoutSeconds, "OtgWaitTimeoutSeconds", errors);
            RequirePositive(RecoveryBatTimeoutSeconds, "RecoveryBatTimeoutSeconds", errors);
            RequirePositive(InstallationTimeoutSeconds, "InstallationTimeoutSeconds", errors);
            RequirePositive(RecoveryNetworkTimeoutSeconds, "RecoveryNetworkTimeoutSeconds", errors);
            RequirePositive(MaxRelayRetries, "MaxRelayRetries", errors);
            RequirePositive(MoxaConnectTimeoutMs, "MoxaConnectTimeoutMs", errors);
            RequirePositive(MoxaReconnectAttempts, "MoxaReconnectAttempts", errors);
            RequirePositive(MoxaKeepAliveIntervalSeconds, "MoxaKeepAliveIntervalSeconds", errors);
            ValidateSecondaryChannel(PowerChannel1Secondary, PowerChannel1, "PowerChannel1Secondary", errors);
            ValidateSecondaryChannel(PowerChannel2Secondary, PowerChannel2, "PowerChannel2Secondary", errors);
            if (SelectedUkb < 1 || SelectedUkb > 6)
                errors.Add("SelectedUkb 1-6 aralığında olmalıdır.");
            if (PowerSlot2 < -1 || PowerSlot2 > 32)
                errors.Add("PowerSlot2 -1 veya 0-32 aralığında olmalıdır.");
            if (RecoverySlot2 < -1 || RecoverySlot2 > 32)
                errors.Add("RecoverySlot2 -1 veya 0-32 aralığında olmalıdır.");

            if (NetworkInstallMode)
            {
                if (!IPAddress.TryParse(NetworkServerIp, out _))
                    errors.Add("NetworkServerIp geçerli bir IPv4/IPv6 adresi olmalıdır.");
                if (NetworkServerPort < 1 || NetworkServerPort > 65535)
                    errors.Add("NetworkServerPort 1-65535 aralığında olmalıdır.");
            }

            if (SerialMonitorEnabled)
            {
                EnsureSwarmTargets();
                UkbTargetConfig selectedTarget = GetTarget(SelectedUkb);
                RequireText(selectedTarget.ComPort, $"UKB{SelectedUkb} COM", errors);
                RequirePositive(selectedTarget.BaudRate, $"UKB{SelectedUkb} BaudRate", errors);
                foreach (UkbTargetConfig target in SwarmUkbTargets)
                    ValidateSecondaryChannel(target.PowerSecondaryChannel, target.PowerChannel,
                        $"UKB{target.UkbNumber} ikinci Power kanalı", errors);
                RequirePositive(SerialRecoveryTimeoutSeconds, "SerialRecoveryTimeoutSeconds", errors);
                RequirePositive(SerialBootTimeoutSeconds, "SerialBootTimeoutSeconds", errors);
            }

            if (EasyInstallerAutoLoginEnabled)
                RequireText(EasyInstallerUserName, "EasyInstallerUserName", errors);
            ValidateProjectMappings(ProjectPackageMappings, errors);

            if (TwoUkb && PowerChannel1 == PowerChannel2)
                errors.Add("İki UKB etkin olduğunda power kanalları farklı olmalıdır.");
            if (TwoUkb && RecoveryChannel1 == RecoveryChannel2)
                errors.Add("İki UKB etkin olduğunda recovery kanalları farklı olmalıdır.");

            if (errors.Count > 0)
                throw new AppConfigException(
                    "appsettings.json içinde eksik/hatalı ayarlar bulundu:\n - " +
                    string.Join("\n - ", errors));
        }

        private static void RequireText(string value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(name + " boş bırakılamaz.");
        }

        private static void RequirePositive(int value, string name, List<string> errors)
        {
            if (value <= 0)
                errors.Add(name + " sıfırdan büyük olmalıdır.");
        }

        private static void ValidateProjectMappings(
            List<ProjectPackageMapping> mappings,
            List<string> errors)
        {
            if (mappings == null || mappings.Count == 0)
            {
                errors.Add("En az bir ortam / TEZI paket eslemesi bulunmalidir.");
                return;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectPackageMapping mapping in mappings)
            {
                string environmentName = (mapping?.EnvironmentName ?? "").Trim();
                string packagePrefix = (mapping?.PackageNamePrefix ?? "").Trim();
                if (environmentName.Length == 0 || packagePrefix.Length == 0)
                {
                    errors.Add("Ortam ve TEZI paket adi bos birakilamaz.");
                    continue;
                }
                if (!names.Add(environmentName))
                    errors.Add("Ortam eslemesi birden fazla tanimlanmis: " + environmentName);
            }
        }

        private static void ValidateSecondaryChannel(int value, byte primary, string name, List<string> errors)
        {
            if (value < -1 || value > 31)
                errors.Add(name + " -1 (kapalı) veya 0-31 arasında olmalıdır.");
            if (value == primary)
                errors.Add(name + " birincil Power kanalıyla aynı olamaz.");
        }
    }
}
