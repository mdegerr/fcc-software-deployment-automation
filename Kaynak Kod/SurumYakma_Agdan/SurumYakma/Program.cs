using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MessageBox = SurumYakma.LocalizedMessageBox;

namespace SurumYakma
{
    static class Program
    {
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
                MessageBox.Show(
                    "Ağdan sürüm yükleme uygulaması zaten açık. Açık olan pencereyi kullanın.",
                    "Uygulama Zaten Açık",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Logger.Initialize(Path.Combine(Application.StartupPath, "logs", "surumyakma.log"));
            FirewallRuleHelper.EnsureInboundAllowRule();
            Application.Run(new Form1());
        }
    }
}
