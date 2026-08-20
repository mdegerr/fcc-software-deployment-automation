using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SurumYakma
{
    internal sealed class FirewallCommand
    {
        public FirewallCommand(string stage, params string[] arguments)
        {
            Stage = stage;
            Arguments = arguments;
        }

        public string Stage { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    internal sealed class FirewallCommandResult
    {
        public FirewallCommandResult(int exitCode, string standardOutput = "", string standardError = "")
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? "";
            StandardError = standardError ?? "";
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
    }

    internal interface IFirewallCommandRunner
    {
        FirewallCommandResult Run(string fileName, IReadOnlyList<string> arguments);
    }

    internal sealed class NetshCommandRunner : IFirewallCommandRunner
    {
        public FirewallCommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(true); }
                catch { }
                throw new TimeoutException("netsh advfirewall komutu 30 saniye içinde tamamlanmadı.");
            }

            return new FirewallCommandResult(
                process.ExitCode,
                outputTask.GetAwaiter().GetResult(),
                errorTask.GetAwaiter().GetResult());
        }
    }

    internal static class FirewallRuleHelper
    {
        internal const string RuleName = "Sürüm Yükleme - Uygulama Gelen Bağlantı";
        private const string NetshFileName = "netsh.exe";

        public static void EnsureInboundAllowRule()
        {
            EnsureInboundAllowRule(Environment.ProcessPath, new NetshCommandRunner());
        }

        internal static void EnsureInboundAllowRule(
            string executablePath,
            IFirewallCommandRunner commandRunner)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(executablePath))
                    throw new InvalidOperationException("Çalışan uygulamanın EXE yolu belirlenemedi.");
                if (commandRunner == null)
                    throw new ArgumentNullException(nameof(commandRunner));

                string fullExecutablePath = Path.GetFullPath(executablePath);
                Logger.Checkpoint(
                    "FIREWALL_RULE",
                    "START",
                    $"rule={RuleName}; program={fullExecutablePath}");

                IReadOnlyList<FirewallCommand> commands = CreateCommands(fullExecutablePath);
                ExecuteDelete(commands[0], commandRunner, fullExecutablePath);
                ExecuteAdd(commands[1], commandRunner, fullExecutablePath);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Windows Defender Firewall gelen bağlantı kuralı hazırlanamadı. " +
                    "Uygulama çalışmaya devam edecek; ağdan aktarım engellenirse güvenlik duvarı ayarlarını kontrol edin. " +
                    $"Hata türü={ex.GetType().Name}; ayrıntı={ex.Message}");
                Logger.Diagnostic("Firewall kuralı hazırlama beklenmeyen bir hatayla tamamlanamadı.", ex);
                Logger.Checkpoint(
                    "FIREWALL_RULE",
                    "FAILED",
                    $"exception={ex.GetType().Name}; message={ex.Message}");
            }
        }

        internal static IReadOnlyList<FirewallCommand> CreateCommands(string executablePath)
        {
            string fullExecutablePath = Path.GetFullPath(executablePath);
            return new[]
            {
                new FirewallCommand(
                    "delete",
                    "advfirewall", "firewall", "delete", "rule",
                    "name=" + RuleName,
                    "dir=in"),
                new FirewallCommand(
                    "add",
                    "advfirewall", "firewall", "add", "rule",
                    "name=" + RuleName,
                    "dir=in",
                    "action=allow",
                    "program=" + fullExecutablePath,
                    "enable=yes",
                    "profile=any",
                    "protocol=any")
            };
        }

        private static void ExecuteDelete(
            FirewallCommand command,
            IFirewallCommandRunner commandRunner,
            string executablePath)
        {
            try
            {
                FirewallCommandResult result = commandRunner.Run(NetshFileName, command.Arguments);
                Logger.Checkpoint(
                    "FIREWALL_RULE_DELETE",
                    result.ExitCode == 0 ? "OK" : "NOT_FOUND_OR_FAILED",
                    $"exitCode={result.ExitCode}; rule={RuleName}; program={executablePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Önceki Windows Defender Firewall kuralı silinemedi; yeni kural ekleme yine de denenecek. " +
                    $"Kural={RuleName}; program={executablePath}; hata türü={ex.GetType().Name}; ayrıntı={ex.Message}");
                Logger.Diagnostic("Firewall kuralı silme komutu çalıştırılamadı.", ex);
                Logger.Checkpoint(
                    "FIREWALL_RULE_DELETE",
                    "FAILED",
                    $"exception={ex.GetType().Name}; message={ex.Message}");
            }
        }

        private static void ExecuteAdd(
            FirewallCommand command,
            IFirewallCommandRunner commandRunner,
            string executablePath)
        {
            try
            {
                FirewallCommandResult result = commandRunner.Run(NetshFileName, command.Arguments);
                if (result.ExitCode == 0)
                {
                    Logger.Checkpoint(
                        "FIREWALL_RULE_ADD",
                        "OK",
                        $"exitCode=0; rule={RuleName}; program={executablePath}; direction=in; protocol=any; profile=any");
                    return;
                }

                Logger.Warn(
                    "Windows Defender Firewall gelen bağlantı kuralı eklenemedi. " +
                    "Uygulama çalışmaya devam edecek; ağdan aktarım engellenirse güvenlik duvarı ayarlarını kontrol edin. " +
                    $"Kural={RuleName}; program={executablePath}; exitCode={result.ExitCode}; " +
                    $"stderr={FormatOutput(result.StandardError)}; stdout={FormatOutput(result.StandardOutput)}");
                Logger.Checkpoint(
                    "FIREWALL_RULE_ADD",
                    "FAILED",
                    $"exitCode={result.ExitCode}; rule={RuleName}; program={executablePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Windows Defender Firewall gelen bağlantı kuralı ekleme komutu çalıştırılamadı. " +
                    "Uygulama çalışmaya devam edecek; ağdan aktarım engellenirse güvenlik duvarı ayarlarını kontrol edin. " +
                    $"Kural={RuleName}; program={executablePath}; hata türü={ex.GetType().Name}; ayrıntı={ex.Message}");
                Logger.Diagnostic("Firewall kuralı ekleme komutu çalıştırılamadı.", ex);
                Logger.Checkpoint(
                    "FIREWALL_RULE_ADD",
                    "FAILED",
                    $"exception={ex.GetType().Name}; message={ex.Message}");
            }
        }

        private static string FormatOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return "<empty>";

            string formatted = output.Replace("\r\n", " | ").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return formatted.Length <= 2000 ? formatted : formatted.Substring(0, 2000) + "...";
        }
    }
}
