using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MessageBox = SurumYakma.LocalizedMessageBox;

namespace SurumYakma
{
    static class Program
    {
        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        private static bool TryActivateRunningWindow()
        {
            int currentProcessId = Environment.ProcessId;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentProcessId || process.MainWindowHandle == IntPtr.Zero)
                        continue;
                    string title = process.MainWindowTitle ?? "";
                    if (title.IndexOf("Sürüm Yükleme", StringComparison.OrdinalIgnoreCase) < 0 &&
                        title.IndexOf("Version Installation", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    ShowWindowAsync(process.MainWindowHandle, SwRestore);
                    SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
                catch
                {
                    // Süreç kapanıyor veya erişilemiyorsa sonraki pencereyi dene.
                }
                finally
                {
                    process.Dispose();
                }
            }
            return false;
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                AppConfig startupConfig = AppConfig.Load();
                string project = Environment.GetEnvironmentVariable("UAV_PROJECT_NAME") ?? "";
                SettingsProfileStore.TryApplyCurrentProject(project, startupConfig, out _);
                Localization.SetLanguage(startupConfig.UiLanguage);
            }
            catch
            {
                Localization.SetLanguage("TR");
            }

            using var singleInstance = new Mutex(true, @"Local\UKB-SurumYakma-Agdan", out bool firstInstance);
            if (!firstInstance)
            {
                TryActivateRunningWindow();
                return;
            }

            ApplicationConfiguration.Initialize();
            Logger.Initialize(Path.Combine(Application.StartupPath, "logs", "surumyakma.log"));
            FirewallRuleHelper.EnsureInboundAllowRule();
            try
            {
                Application.Run(new Form1());
            }
            finally
            {
                Logger.Shutdown();
            }
            Environment.Exit(0);
        }
    }
}
