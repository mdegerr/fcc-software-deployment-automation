using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SurumYakma
{
    public static class SettingsProfileStore
    {
        private const string ProfilesSection = "SURUM_YAKMA_PROFILES";

        public static string DefaultPath =>
            Path.Combine(AppContext.BaseDirectory, "Settings.json");

        public static void SaveCurrentProject(string projectName, AppConfig config)
        {
            Save(DefaultPath, projectName, config);
        }

        public static string[] GetProjectNames(string settingsPath = null)
        {
            string path = string.IsNullOrWhiteSpace(settingsPath) ? DefaultPath : settingsPath;
            if (!File.Exists(path))
                return new string[0];
            try
            {
                JsonObject root = JsonNode.Parse(
                    File.ReadAllText(path),
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }) as JsonObject;
                JsonObject profiles = root?[ProfilesSection] as JsonObject;
                return profiles == null
                    ? new string[0]
                    : profiles.Select(item => item.Key)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is InvalidOperationException)
            {
                Logger.Diagnostic("Settings.json platform adları okunamadı.", ex);
                return new string[0];
            }
        }

        public static bool TryApplyCurrentProject(
            string projectName,
            AppConfig config,
            out string detail)
        {
            detail = "Settings.json ortam profili bulunamadı.";
            if (config == null || string.IsNullOrWhiteSpace(projectName) || !File.Exists(DefaultPath))
                return false;

            try
            {
                JsonObject root = JsonNode.Parse(
                    File.ReadAllText(DefaultPath),
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }) as JsonObject;
                JsonObject profiles = root?[ProfilesSection] as JsonObject;
                if (profiles == null)
                    return false;

                JsonObject profile = null;
                foreach (var item in profiles)
                {
                    if (item.Key.Equals(projectName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        profile = item.Value as JsonObject;
                        break;
                    }
                }
                if (profile == null)
                    return false;

                JsonObject serial = profile["Serial"] as JsonObject;
                JsonObject power = profile["PowerBox"] as JsonObject;
                JsonObject relay = profile["RelayBox"] as JsonObject;
                JsonObject network = profile["EasyInstallerNetwork"] as JsonObject;

                config.SelectedUkb = ReadInt(profile, "SelectedUkb", config.SelectedUkb);
                config.SwarmModeEnabled = ReadBool(profile, "SwarmModeEnabled", config.SwarmModeEnabled);
                config.SerialPortName = ReadString(serial, "UKB1ComPort",
                    ReadString(serial, "ComPort", config.SerialPortName));
                config.SerialPortName2 = ReadString(serial, "UKB2ComPort", config.SerialPortName2);
                config.AutoDetectSerialPort = ReadBool(serial, "AutoDetect", config.AutoDetectSerialPort);
                config.SerialBaudRate = ReadInt(serial, "BaudRate", config.SerialBaudRate);

                config.PowerBoxIp = ReadString(power, "IPAddress", config.PowerBoxIp);
                config.PowerSlot = ReadByte(power, "UKB1Slot", ReadByte(power, "Slot", config.PowerSlot));
                config.PowerSlot2 = ReadInt(power, "UKB2Slot", config.PowerSlot2);
                config.PowerChannel1 = ReadByte(power, "UKB1PrimaryChannel", config.PowerChannel1);
                config.PowerChannel1Secondary = ReadInt(power, "UKB1SecondaryChannel", config.PowerChannel1Secondary);
                config.PowerChannel2 = ReadByte(power, "UKB2PrimaryChannel", config.PowerChannel2);
                config.PowerChannel2Secondary = ReadInt(power, "UKB2SecondaryChannel", config.PowerChannel2Secondary);

                config.RelayBoxIp = ReadString(relay, "IPAddress", config.RelayBoxIp);
                config.RecoverySlot = ReadByte(relay, "UKB1Slot", ReadByte(relay, "Slot", config.RecoverySlot));
                config.RecoverySlot2 = ReadInt(relay, "UKB2Slot", config.RecoverySlot2);
                config.RecoveryChannel1 = ReadByte(relay, "UKB1RecoveryChannel", config.RecoveryChannel1);
                config.RecoveryChannel2 = ReadByte(relay, "UKB2RecoveryChannel", config.RecoveryChannel2);

                config.UkbTargetIp = ReadString(network, "TargetIP", config.UkbTargetIp);
                config.NetworkServerIp = ReadString(network, "ServerIP", config.NetworkServerIp);
                config.NetworkServerPort = ReadInt(network, "ServerPort", config.NetworkServerPort);
                config.AutoDetectNetworkServerIp = ReadBool(
                    network,
                    "AutoDetectServerIP",
                    config.AutoDetectNetworkServerIp);
                config.ValidateTeziPackageNamePrefix = ReadBool(
                    profile,
                    "ValidateTeziPackageNamePrefix",
                    config.ValidateTeziPackageNamePrefix);
                bool hadUnifiedTargets = false;
                if (profile["SwarmTargets"] is JsonArray swarmTargets)
                {
                    var parsedTargets = new System.Collections.Generic.List<UkbTargetConfig>();
                    foreach (JsonNode node in swarmTargets)
                    {
                        if (!(node is JsonObject target))
                            continue;
                        int number = ReadInt(target, "UkbNumber", 0);
                        if (number < 1 || number > 6)
                            continue;
                        parsedTargets.Add(new UkbTargetConfig
                        {
                            UkbNumber = number,
                            ComPort = ReadString(target, "ComPort", ""),
                            BaudRate = ReadInt(target, "BaudRate", config.SerialBaudRate),
                            PowerSlot = ReadByte(target, "PowerSlot", 0),
                            PowerChannel = ReadByte(target, "PowerChannel", 0),
                            PowerSecondaryChannel = ReadInt(target, "PowerSecondaryChannel", -1),
                            RecoverySlot = ReadByte(target, "RecoverySlot", 0),
                            RecoveryChannel = ReadByte(target, "RecoveryChannel", 0)
                        });
                    }
                    if (parsedTargets.Count > 0)
                    {
                        config.SwarmUkbTargets = parsedTargets;
                        hadUnifiedTargets = true;
                    }
                }
                if (!config.SwarmModeEnabled || !hadUnifiedTargets)
                    config.MigrateLegacyTargetsToUnifiedTable();
                config.SwarmModeEnabled = true;

                detail = $"Settings.json profili uygulandı: {projectName.Trim()} " +
                    $"(Power={config.PowerBoxIp}, Relay={config.RelayBoxIp}, " +
                    $"Recovery=MOD{config.RecoverySlot}/CH{config.RecoveryChannel1}, COM={config.SerialPortName}).";
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException ||
                                       ex is InvalidOperationException || ex is FormatException ||
                                       ex is OverflowException)
            {
                detail = "Settings.json ortam profili uygulanamadı: " + ex.Message;
                Logger.Checkpoint("SETTINGS_JSON_PROFILE", "LOAD_FAILED", detail);
                return false;
            }
        }

        private static string ReadString(JsonObject source, string name, string fallback)
        {
            if (source?[name] is JsonValue value && value.TryGetValue(out string result) &&
                !string.IsNullOrWhiteSpace(result))
                return result.Trim();
            return fallback;
        }

        private static int ReadInt(JsonObject source, string name, int fallback)
        {
            if (source?[name] is JsonValue value && value.TryGetValue(out int result))
                return result;
            return fallback;
        }

        private static byte ReadByte(JsonObject source, string name, byte fallback)
        {
            int result = ReadInt(source, name, fallback);
            return result >= byte.MinValue && result <= byte.MaxValue ? (byte)result : fallback;
        }

        private static bool ReadBool(JsonObject source, string name, bool fallback)
        {
            if (source?[name] is JsonValue value && value.TryGetValue(out bool result))
                return result;
            return fallback;
        }

        public static void Save(string settingsPath, string projectName, AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            string environmentName = (projectName ?? "").Trim();
            if (environmentName.Length == 0)
                throw new InvalidOperationException(
                    "Settings.json profili kaydedilemedi: UAV_PROJECT_NAME bos.");
            if (string.IsNullOrWhiteSpace(settingsPath))
                throw new ArgumentException("Settings.json yolu bos.", nameof(settingsPath));

            string fullPath = Path.GetFullPath(settingsPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Settings.json bulunamadi.", fullPath);

            JsonObject root;
            try
            {
                root = JsonNode.Parse(
                    File.ReadAllText(fullPath),
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }) as JsonObject;
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            {
                throw new InvalidOperationException(
                    "Settings.json okunamadi; mevcut dosya degistirilmedi: " + ex.Message,
                    ex);
            }

            if (root == null)
                throw new InvalidOperationException(
                    "Settings.json kok nesnesi gecersiz; mevcut dosya degistirilmedi.");

            JsonObject profiles = root[ProfilesSection] as JsonObject;
            if (profiles == null)
            {
                profiles = new JsonObject();
                root[ProfilesSection] = profiles;
            }

            var profile = new JsonObject
            {
                ["EnvironmentName"] = environmentName,
                ["UpdatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["SelectedUkb"] = config.SelectedUkb,
                ["SwarmModeEnabled"] = config.SwarmModeEnabled,
                ["ValidateTeziPackageNamePrefix"] = config.ValidateTeziPackageNamePrefix,
                ["Serial"] = new JsonObject
                {
                    ["ComPort"] = config.SerialPortName,
                    ["UKB1ComPort"] = config.SerialPortName,
                    ["UKB2ComPort"] = config.SerialPortName2,
                    ["AutoDetect"] = config.AutoDetectSerialPort,
                    ["BaudRate"] = config.SerialBaudRate,
                    ["DataBits"] = 8,
                    ["Parity"] = "None",
                    ["StopBits"] = "One"
                },
                ["PowerBox"] = new JsonObject
                {
                    ["IPAddress"] = config.PowerBoxIp,
                    ["Port"] = 502,
                    ["Slot"] = config.PowerSlot,
                    ["UKB1Slot"] = config.PowerSlot,
                    ["UKB2Slot"] = config.GetPowerSlot(true),
                    ["UKB1PrimaryChannel"] = config.PowerChannel1,
                    ["UKB1SecondaryChannel"] = config.PowerChannel1Secondary,
                    ["UKB2PrimaryChannel"] = config.PowerChannel2,
                    ["UKB2SecondaryChannel"] = config.PowerChannel2Secondary
                },
                ["RelayBox"] = new JsonObject
                {
                    ["IPAddress"] = config.RelayBoxIp,
                    ["Port"] = 502,
                    ["Slot"] = config.RecoverySlot,
                    ["UKB1Slot"] = config.RecoverySlot,
                    ["UKB2Slot"] = config.GetRecoverySlot(true),
                    ["UKB1RecoveryChannel"] = config.RecoveryChannel1,
                    ["UKB2RecoveryChannel"] = config.RecoveryChannel2
                },
                ["EasyInstallerNetwork"] = new JsonObject
                {
                    ["TargetIP"] = config.UkbTargetIp,
                    ["ServerIP"] = config.NetworkServerIp,
                    ["ServerPort"] = config.NetworkServerPort,
                    ["AutoDetectServerIP"] = config.AutoDetectNetworkServerIp
                }
            };

            var mappings = new JsonArray();
            foreach (ProjectPackageMapping mapping in config.ProjectPackageMappings)
            {
                mappings.Add(new JsonObject
                {
                    ["EnvironmentName"] = mapping.EnvironmentName,
                    ["PackageNamePrefix"] = mapping.PackageNamePrefix
                });
            }
            profile["PackageMappings"] = mappings;
            var swarmTargets = new JsonArray();
            foreach (UkbTargetConfig target in config.SwarmUkbTargets ?? UkbTargetConfig.CreateSwarmDefaults())
            {
                swarmTargets.Add(new JsonObject
                {
                    ["UkbNumber"] = target.UkbNumber,
                    ["ComPort"] = target.ComPort,
                    ["BaudRate"] = target.BaudRate,
                    ["PowerSlot"] = target.PowerSlot,
                    ["PowerChannel"] = target.PowerChannel,
                    ["PowerSecondaryChannel"] = target.PowerSecondaryChannel,
                    ["RecoverySlot"] = target.RecoverySlot,
                    ["RecoveryChannel"] = target.RecoveryChannel
                });
            }
            profile["SwarmTargets"] = swarmTargets;
            profiles[environmentName] = profile;

            string directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
            string temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            string backupPath = fullPath + ".bak";
            string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            try
            {
                File.WriteAllText(temporaryPath, json + Environment.NewLine);
                File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
                throw;
            }
        }
    }
}
