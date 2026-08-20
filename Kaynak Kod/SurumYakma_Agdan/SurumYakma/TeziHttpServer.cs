using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    /// <summary>
    /// Seçili TEZI paketini yalnızca belirtilen USB-NCM/izole ağ adresinde sunar.
    /// HttpListener yerine TcpListener kullanıldığı için URL ACL veya yönetici
    /// yetkisi gerektirmez. Yalnızca GET/HEAD ve tek paket kökü desteklenir.
    /// </summary>
    public sealed class TeziHttpServer : IDisposable
    {
        public const string HealthResponse = "SURUMYAKMA_HTTP_OK";

        private readonly IPAddress _bindAddress;
        private readonly int _requestedPort;
        private readonly SemaphoreSlim _clientLimit = new SemaphoreSlim(4, 4);
        private readonly object _sync = new object();
        private readonly object _clientTasksSync = new object();
        private readonly HashSet<Task> _clientTasks = new HashSet<Task>();
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptTask;
        private string _packageRoot;

        public event Action<string> RequestStarted;
        public event Action<string> RequestCompleted;

        public TeziHttpServer(string bindIp, int port)
        {
            if (!IPAddress.TryParse(bindIp, out _bindAddress))
                throw new ArgumentException("Geçersiz ağ sunucusu IP adresi: " + bindIp, nameof(bindIp));
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            _requestedPort = port;
        }

        public bool IsRunning => _listener != null;

        public int Port
        {
            get
            {
                TcpListener listener = _listener;
                return listener == null ? _requestedPort : ((IPEndPoint)listener.LocalEndpoint).Port;
            }
        }

        public string BaseUrl => $"http://{_bindAddress}:{Port}";

        public static TeziHttpServer StartWithFallback(
            string bindIp,
            int configuredPort,
            string packageRoot)
        {
            var candidates = new List<int> { configuredPort };
            if (configuredPort != 8088)
                candidates.Add(8088);
            if (configuredPort != 0)
                candidates.Add(0);

            SocketException lastSocketError = null;
            foreach (int candidatePort in candidates)
            {
                var server = new TeziHttpServer(bindIp, candidatePort);
                try
                {
                    server.SetPackageRoot(packageRoot);
                    server.Start();
                    if (server.Port != configuredPort)
                    {
                        Logger.Checkpoint(
                            "TEZI_HTTP_PORT_FALLBACK",
                            "SUCCESS",
                            $"configured={configuredPort}; selected={server.Port}; bindIp={bindIp}");
                    }
                    return server;
                }
                catch (SocketException ex) when (
                    ex.SocketErrorCode == SocketError.AccessDenied ||
                    ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    lastSocketError = ex;
                    Logger.Checkpoint(
                        "TEZI_HTTP_PORT_FALLBACK",
                        "RETRY",
                        $"port={candidatePort}; socketError={ex.SocketErrorCode}; nativeError={ex.NativeErrorCode}");
                    server.Dispose();
                }
            }

            throw lastSocketError ?? new SocketException((int)SocketError.AddressNotAvailable);
        }

        public void SetPackageRoot(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
                throw new DirectoryNotFoundException("Yayınlanacak TEZI paketi bulunamadı: " + packageRoot);
            if (!File.Exists(Path.Combine(packageRoot, "image.json")))
                throw new InvalidOperationException("Yayınlanacak pakette image.json bulunamadı.");

            lock (_sync)
                _packageRoot = Path.GetFullPath(packageRoot);
        }

        public void Start()
        {
            if (IsRunning)
                return;

            var listener = new TcpListener(_bindAddress, _requestedPort);
            listener.Start();
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
            Logger.Info($"TEZI HTTP sunucusu başlatıldı: {BaseUrl}");
            Logger.Checkpoint("TEZI_HTTP_SERVER", "START", $"url={BaseUrl}");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                    try
                    {
                        await _clientLimit.WaitAsync(ct);
                    }
                    catch
                    {
                        client.Dispose();
                        throw;
                    }

                    Task clientTask = HandleClientAndReleaseAsync(client, ct);
                    lock (_clientTasksSync)
                        _clientTasks.Add(clientTask);
                    _ = clientTask.ContinueWith(
                        completed =>
                        {
                            lock (_clientTasksSync)
                                _clientTasks.Remove(completed);
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("TEZI HTTP sunucusu bağlantı kabul döngüsü durdu", ex);
            }
        }

        private async Task HandleClientAndReleaseAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                    await HandleClientAsync(client, ct);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Logger.Diagnostic("TEZI HTTP istemci isteği işlenemedi.", ex);
                Logger.Checkpoint(
                    "TEZI_HTTP_CLIENT",
                    "FAILED",
                    $"exception={ex.GetType().Name}; message={ex.Message}");
            }
            finally
            {
                _clientLimit.Release();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            client.NoDelay = true;
            client.SendBufferSize = 1024 * 1024;
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(
                stream, Encoding.ASCII, false, 4096, leaveOpen: true);

            string requestLine = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(requestLine))
                return;

            string[] requestParts = requestLine.Split(' ');
            if (requestParts.Length < 2)
            {
                await WriteTextAsync(stream, 400, "Bad Request", "Geçersiz HTTP isteği.\n", false, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", "status=400; reason=bad_request");
                return;
            }

            string method = requestParts[0].ToUpperInvariant();
            bool headOnly = method == "HEAD";
            if (method != "GET" && !headOnly)
            {
                await WriteTextAsync(stream, 405, "Method Not Allowed", "Yalnızca GET/HEAD desteklenir.\n", false, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", $"method={method}; status=405");
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
            {
                int separator = line.IndexOf(':');
                if (separator > 0)
                    headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            string path = GetRequestPath(requestParts[1]);
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            Logger.Info($"[http] istek: remote={remote}; method={method}; path={path}");
            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(stream, 200, "OK", HealthResponse + "\n", headOnly, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "OK", $"remote={remote}; method={method}; path=/health; status=200");
                PublishRequestCompleted("/health");
                return;
            }

            if (path.Equals("/image_list.json", StringComparison.OrdinalIgnoreCase))
            {
                string packageRoot;
                lock (_sync)
                    packageRoot = _packageRoot;
                if (packageRoot == null)
                {
                    await WriteTextAsync(stream, 503, "Service Unavailable", "TEZI paketi henüz hazırlanmadı.\n", headOnly, ct);
                    Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", $"remote={remote}; path={path}; status=503; reason=package_not_ready");
                    return;
                }

                string json = JsonSerializer.Serialize(
                    new { config_format = 1, images = new[] { "package/image.json" } },
                    new JsonSerializerOptions { WriteIndented = true }) + "\n";
                await WriteBytesAsync(
                    stream, 200, "OK", "application/json; charset=utf-8",
                    Encoding.UTF8.GetBytes(json), headOnly, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "OK", $"remote={remote}; method={method}; path=/image_list.json; status=200");
                PublishRequestCompleted("/image_list.json");
                return;
            }

            if (!path.StartsWith("/package/", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(stream, 404, "Not Found", "Bulunamadı.\n", headOnly, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", $"remote={remote}; path={path}; status=404");
                return;
            }

            string root;
            lock (_sync)
                root = _packageRoot;
            if (root == null)
            {
                await WriteTextAsync(stream, 503, "Service Unavailable", "TEZI paketi henüz hazırlanmadı.\n", headOnly, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", $"remote={remote}; path={path}; status=503; reason=package_not_ready");
                return;
            }

            string relative = Uri.UnescapeDataString(path.Substring("/package/".Length))
                .Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(root, relative));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                await WriteTextAsync(stream, 404, "Not Found", "Dosya bulunamadı.\n", headOnly, ct);
                Logger.Checkpoint("TEZI_HTTP_REQUEST", "FAILED", $"remote={remote}; path={path}; status=404; reason=file_not_found_or_unsafe_path");
                return;
            }

            string publishedPath = "/package/" + relative.Replace(Path.DirectorySeparatorChar, '/');
            PublishRequestStarted(publishedPath);
            Logger.Checkpoint(
                "TEZI_HTTP_FILE",
                "START",
                $"remote={remote}; method={method}; relative={relative}; bytes={new FileInfo(fullPath).Length}; range={headers.ContainsKey("Range")}");
            await WriteFileAsync(stream, fullPath, headers, headOnly, ct);
            Logger.Checkpoint(
                "TEZI_HTTP_FILE",
                "SUCCESS",
                $"remote={remote}; method={method}; relative={relative}; bytes={new FileInfo(fullPath).Length}; range={headers.ContainsKey("Range")}");
            PublishRequestCompleted(publishedPath);
        }

        private void PublishRequestStarted(string path)
        {
            try { RequestStarted?.Invoke(path); }
            catch (Exception ex) { Logger.Diagnostic("HTTP istek başlangıç olayı işlenemedi: " + path, ex); }
        }

        private void PublishRequestCompleted(string path)
        {
            try { RequestCompleted?.Invoke(path); }
            catch (Exception ex) { Logger.Diagnostic("HTTP istek olayı işlenemedi: " + path, ex); }
        }

        private static string GetRequestPath(string target)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out Uri absolute))
                return absolute.AbsolutePath;
            int query = target.IndexOf('?');
            return query >= 0 ? target.Substring(0, query) : target;
        }

        private static async Task WriteFileAsync(
            Stream stream,
            string file,
            IDictionary<string, string> requestHeaders,
            bool headOnly,
            CancellationToken ct)
        {
            var info = new FileInfo(file);
            long start = 0;
            long end = info.Length - 1;
            bool partial = false;
            if (requestHeaders.TryGetValue("Range", out string range) &&
                TryParseRange(range, info.Length, out long rangeStart, out long rangeEnd))
            {
                start = rangeStart;
                end = rangeEnd;
                partial = true;
            }

            long length = Math.Max(0, end - start + 1);
            var extraHeaders = new StringBuilder("Accept-Ranges: bytes\r\n");
            if (partial)
                extraHeaders.Append($"Content-Range: bytes {start}-{end}/{info.Length}\r\n");
            await WriteHeaderAsync(
                stream,
                partial ? 206 : 200,
                partial ? "Partial Content" : "OK",
                GetContentType(file),
                length,
                extraHeaders.ToString(),
                ct);
            if (headOnly || length == 0)
                return;

            using FileStream input = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 512, FileOptions.Asynchronous | FileOptions.SequentialScan);
            input.Position = start;
            byte[] buffer = new byte[1024 * 512];
            long remaining = length;
            while (remaining > 0)
            {
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                if (read == 0)
                    throw new EndOfStreamException("TEZI paket dosyası aktarım sırasında beklenmedik biçimde sona erdi.");
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                remaining -= read;
            }
        }

        private static bool TryParseRange(string header, long fileLength, out long start, out long end)
        {
            start = 0;
            end = fileLength - 1;
            if (fileLength <= 0 || string.IsNullOrWhiteSpace(header) ||
                !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] parts = header.Substring(6).Split('-', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out start) || start < 0 || start >= fileLength)
                return false;
            if (!string.IsNullOrWhiteSpace(parts[1]) && !long.TryParse(parts[1], out end))
                return false;
            end = Math.Min(end, fileLength - 1);
            return end >= start;
        }

        private static string GetContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".json": return "application/json";
                case ".html": return "text/html";
                case ".txt":
                case ".sh": return "text/plain";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".xz": return "application/x-xz";
                case ".gz": return "application/gzip";
                case ".tar": return "application/x-tar";
                default: return "application/octet-stream";
            }
        }

        private static Task WriteTextAsync(
            Stream stream, int status, string reason, string text, bool headOnly, CancellationToken ct)
        {
            return WriteBytesAsync(
                stream, status, reason, "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes(text), headOnly, ct);
        }

        private static async Task WriteBytesAsync(
            Stream stream,
            int status,
            string reason,
            string contentType,
            byte[] body,
            bool headOnly,
            CancellationToken ct)
        {
            await WriteHeaderAsync(stream, status, reason, contentType, body.Length, "", ct);
            if (!headOnly)
                await stream.WriteAsync(body.AsMemory(), ct);
        }

        private static async Task WriteHeaderAsync(
            Stream stream,
            int status,
            string reason,
            string contentType,
            long contentLength,
            string extraHeaders,
            CancellationToken ct)
        {
            string header =
                $"HTTP/1.1 {status.ToString(CultureInfo.InvariantCulture)} {reason}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {contentLength.ToString(CultureInfo.InvariantCulture)}\r\n" +
                "Connection: close\r\n" +
                "Cache-Control: no-store,max-age=0\r\n" +
                extraHeaders + "\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(bytes.AsMemory(), ct);
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _acceptTask?.Wait(1000); } catch { }

            Task[] activeClients;
            lock (_clientTasksSync)
                activeClients = _clientTasks.ToArray();
            bool clientsStopped = true;
            if (activeClients.Length > 0)
            {
                try
                {
                    clientsStopped = Task.WaitAll(activeClients, 2000);
                }
                catch
                {
                    clientsStopped = activeClients.All(task => task.IsCompleted);
                }
            }

            _cancellation?.Dispose();
            if (clientsStopped)
                _clientLimit.Dispose();
            _listener = null;
            _acceptTask = null;
            _cancellation = null;
            Logger.Checkpoint(
                "TEZI_HTTP_SERVER",
                "STOP",
                $"activeClients={activeClients.Length}; clientsStopped={clientsStopped}");
        }
    }
}
