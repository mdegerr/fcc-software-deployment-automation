using System;
using System.IO;

namespace SurumYakma
{
    public sealed class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Basit dosya logger'ı. Üretim hattında bir yakma işleminin ne zaman,
    /// hangi UKB için, hangi adımda ne yaptığını/hata verdiğini geriye dönük
    /// incelemek için kullanılır. Thread-safe (lock ile korunuyor).
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logPath = "surumyakma.log";
        private static string _sessionLogPath = "surumyakma-session.log";
        public static event Action<LogEntry> MessageWritten;
        public static string CurrentSessionLogPath => _sessionLogPath;

        public static void Initialize(string logPath)
        {
            try
            {
                ConfigurePaths(logPath);
                File.AppendAllText(_sessionLogPath, "");
            }
            catch
            {
                try
                {
                    string fallback = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SurumYakma",
                        "logs",
                        "surumyakma.log");
                    ConfigurePaths(fallback);
                    File.AppendAllText(_sessionLogPath, "");
                }
                catch { /* loglama başarısız olsa da program çalışmaya devam etmeli */ }
            }
        }

        private static void ConfigurePaths(string logPath)
        {
            _logPath = logPath;
            string dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string baseName = Path.GetFileNameWithoutExtension(_logPath);
            string extension = Path.GetExtension(_logPath);
            _sessionLogPath = Path.Combine(
                string.IsNullOrEmpty(dir) ? "." : dir,
                $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}{extension}");
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message + " | " + ex.GetType().Name + ": " + ex.Message);
            Write("DIAGNOSTIC", ex.ToString(), false);
        }

        public static void Diagnostic(string message, Exception ex)
        {
            Write("DIAGNOSTIC", message + Environment.NewLine + ex, false);
        }

        public static void Checkpoint(string id, string status, string details = "")
        {
            string message = $"id={id}; status={status}";
            if (!string.IsNullOrWhiteSpace(details))
                message += "; " + details;
            Write("CHECKPOINT", message, false);
        }

        private static void Write(string level, string message, bool publishToUi = true)
        {
            message = Localization.ForCurrentLanguage(message);
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };
            string normalized = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string[] messageLines = normalized.Split('\n');
            string[] lines = new string[messageLines.Length];
            for (int i = 0; i < messageLines.Length; i++)
                lines[i] = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] {messageLines[i]}";
            string fileText = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            System.Diagnostics.Debug.WriteLine(string.Join(Environment.NewLine, lines));
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, fileText);
                    if (!string.Equals(_sessionLogPath, _logPath, StringComparison.OrdinalIgnoreCase))
                        File.AppendAllText(_sessionLogPath, fileText);
                }
                catch
                {
                    // Diske yazamıyorsak (izin, disk dolu vb.) sessizce geç;
                    // burada patlarsak asıl işi durdurmuş oluruz.
                }
            }

            if (!publishToUi)
                return;

            try { MessageWritten?.Invoke(entry); }
            catch { /* Arayüz loglaması ana iş akışını durdurmamalı. */ }
        }
    }
}
