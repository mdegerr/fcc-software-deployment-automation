using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        private static readonly ConcurrentQueue<string> _fileQueue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent _fileSignal = new AutoResetEvent(false);
        private static CancellationTokenSource _writerCts;
        private static Task _writerTask;
        public static event Action<LogEntry> MessageWritten;
        public static string CurrentSessionLogPath => _sessionLogPath;

        public static void Initialize(string logPath)
        {
            Shutdown();
            try
            {
                ConfigurePaths(logPath);
                File.AppendAllText(_sessionLogPath, "");
                StartWriter();
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
                    StartWriter();
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

        private static void StartWriter()
        {
            _writerCts = new CancellationTokenSource();
            CancellationToken token = _writerCts.Token;
            _writerTask = Task.Run(() => WriterLoop(token), CancellationToken.None);
        }

        private static void WriterLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _fileSignal.WaitOne(100);
                Flush();
            }
            Flush();
        }

        public static void Flush()
        {
            lock (_lock)
            {
                if (_fileQueue.IsEmpty)
                    return;

                var batch = new StringBuilder(64 * 1024);
                while (_fileQueue.TryDequeue(out string text))
                    batch.Append(text);
                string fileText = batch.ToString();
                if (fileText.Length == 0)
                    return;

                try
                {
                    File.AppendAllText(_logPath, fileText);
                    if (!string.Equals(_sessionLogPath, _logPath, StringComparison.OrdinalIgnoreCase))
                        File.AppendAllText(_sessionLogPath, fileText);
                }
                catch
                {
                    // Disk/izin hatası asıl yükleme akışını durdurmamalı.
                }
            }
        }

        public static void Shutdown()
        {
            CancellationTokenSource cts = _writerCts;
            Task writer = _writerTask;
            _writerCts = null;
            _writerTask = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                _fileSignal.Set();
                try { writer?.Wait(1500); } catch { }
                try { cts.Dispose(); } catch { }
            }
            Flush();
        }
        private static bool IsRawTechnicalMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;
            return message.StartsWith("[serial]", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("[serial-tx]", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("[uuu]", StringComparison.OrdinalIgnoreCase) ||
                   message.StartsWith("[http]", StringComparison.OrdinalIgnoreCase);
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
            if (!IsRawTechnicalMessage(message))
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
            _fileQueue.Enqueue(fileText);
            _fileSignal.Set();

            if (!publishToUi)
                return;

            try { MessageWritten?.Invoke(entry); }
            catch { /* Arayüz loglaması ana iş akışını durdurmamalı. */ }
        }
    }
}
