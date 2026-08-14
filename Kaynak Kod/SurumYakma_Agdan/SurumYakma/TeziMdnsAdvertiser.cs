using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    /// <summary>
    /// Toradex Easy Installer'ın yerel image_list.json kaynağını bulabilmesi için
    /// yalnızca seçilen USB-NCM arayüzünde _tezi._tcp DNS-SD duyurusu yapar.
    /// </summary>
    public sealed class TeziMdnsAdvertiser : IDisposable
    {
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
        private const int MdnsPort = 5353;
        private const string ServiceType = "_tezi._tcp.local";
        // Toradex'in resmî Avahi tanımında servis örnek adı ile TXT name alanı
        // birebir aynıdır; aynı yapıyı koruyarak TEZI sürümleriyle uyumu artır.
        private const string DisplayName = "Custom Toradex Easy Installer Feed";
        private const string InstanceName = DisplayName + "._tezi._tcp.local";
        private const string HostName = "ukb-surum-yakma.local";

        private readonly IPAddress _bindAddress;
        private readonly int _httpPort;
        private readonly string _feedPath;
        private UdpClient _udp;
        private CancellationTokenSource _cancellation;
        private Task _loopTask;

        public event Action RelevantQueryReceived;

        public TeziMdnsAdvertiser(string bindIp, int httpPort, string feedPath = "/image_list.json")
        {
            if (!IPAddress.TryParse(bindIp, out _bindAddress) ||
                _bindAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("mDNS için geçerli bir IPv4 adresi gerekli.", nameof(bindIp));
            if (httpPort < 1 || httpPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(httpPort));
            _httpPort = httpPort;
            _feedPath = string.IsNullOrWhiteSpace(feedPath) ? "/image_list.json" : feedPath;
        }

        public bool IsRunning => _udp != null;

        public void Start()
        {
            if (IsRunning)
                return;

            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.ExclusiveAddressUse = false;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            udp.JoinMulticastGroup(MulticastAddress, _bindAddress);
            udp.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                _bindAddress.GetAddressBytes());

            // RFC 6762 gereği mDNS paketleri IP TTL 255 ile gönderilmelidir. Bazı
            // istemciler farklı TTL değerindeki yanıtları güvenlik nedeniyle yok sayar.
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, 255);
            udp.MulticastLoopback = false;

            _udp = udp;
            _cancellation = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunAsync(_cancellation.Token));
            Logger.Info(
                $"TEZI Zeroconf duyurusu başlatıldı: interface={_bindAddress}; " +
                $"service={ServiceType}; port={_httpPort}; path={_feedPath}");
            Logger.Checkpoint(
                "TEZI_MDNS",
                "START",
                $"interface={_bindAddress}; port={_httpPort}; path={_feedPath}; ttl=255");
        }

        private async Task RunAsync(CancellationToken ct)
        {
            byte[] response = BuildResponsePacket(_bindAddress, _httpPort, _feedPath, 120);
            var multicastEndpoint = new IPEndPoint(MulticastAddress, MdnsPort);

            Logger.Checkpoint(
                "TEZI_MDNS_PACKET",
                "READY",
                $"bytes={response.Length}; instance={DisplayName}; hex={ToHex(response)}");

            try
            {
                await TrySendAsync(response, multicastEndpoint, "startup-multicast", ct);
                Task<UdpReceiveResult> receive = _udp.ReceiveAsync();

                while (!ct.IsCancellationRequested)
                {
                    Task delay = Task.Delay(TimeSpan.FromSeconds(10), ct);
                    Task completed = await Task.WhenAny(receive, delay);
                    if (completed != receive)
                    {
                        ct.ThrowIfCancellationRequested();
                        await TrySendAsync(response, multicastEndpoint, "periodic-multicast", ct);
                        continue;
                    }

                    UdpReceiveResult query;
                    try
                    {
                        query = await receive;
                    }
                    catch (SocketException ex) when (!ct.IsCancellationRequested)
                    {
                        Logger.Diagnostic("TEZI mDNS sorgusu alınırken geçici soket hatası; dinleme sürdürülecek.", ex);
                        Logger.Checkpoint("TEZI_MDNS_RECEIVE", "RETRY", $"error={ex.SocketErrorCode}; message={ex.Message}");
                        await Task.Delay(500, ct);
                        receive = _udp.ReceiveAsync();
                        continue;
                    }

                    receive = _udp.ReceiveAsync();
                    string questions = DescribeQuestions(query.Buffer);
                    if (!IsRelevantQuery(query.Buffer))
                    {
                        Logger.Checkpoint(
                            "TEZI_MDNS_QUERY",
                            "IGNORED",
                            $"remote={query.RemoteEndPoint}; questions={questions}");
                        continue;
                    }

                    Logger.Checkpoint(
                        "TEZI_MDNS_QUERY",
                        "RECEIVED",
                        $"remote={query.RemoteEndPoint}; bytes={query.Buffer.Length}; " +
                        $"questions={questions}; hex={ToHex(query.Buffer)}");
                    try { RelevantQueryReceived?.Invoke(); }
                    catch (Exception ex)
                    {
                        Logger.Diagnostic("TEZI mDNS sorgu olayi islenemedi.", ex);
                    }

                    // QU biti isteyen veya Windows/TEZI yorum farkı gösteren istemciler için
                    // yanıtı hem doğrudan sorgu sahibine hem de standart multicast adresine gönder.
                    await TrySendAsync(response, query.RemoteEndPoint, "query-unicast", ct);
                    await TrySendAsync(response, multicastEndpoint, "query-multicast", ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
            }
            catch (SocketException ex) when (
                ct.IsCancellationRequested ||
                ex.SocketErrorCode == SocketError.OperationAborted)
            {
                Logger.Checkpoint(
                    "TEZI_MDNS",
                    "STOPPING",
                    $"socket={ex.SocketErrorCode}; normalShutdown=true");
            }
            catch (Exception ex)
            {
                Logger.Error("TEZI Zeroconf duyuru döngüsü durdu", ex);
                Logger.Checkpoint("TEZI_MDNS", "FAILED", ex.Message);
            }
        }

        private async Task<bool> TrySendAsync(
            byte[] packet,
            IPEndPoint endpoint,
            string kind,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await _udp.SendAsync(packet, packet.Length, endpoint);
                Logger.Checkpoint(
                    "TEZI_MDNS_RESPONSE",
                    "SENT",
                    $"kind={kind}; target={endpoint}; bytes={packet.Length}");
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Tek bir multicast gönderim hatası duyuru döngüsünü tamamen öldürmemelidir.
                Logger.Diagnostic($"TEZI mDNS yanıtı gönderilemedi ({kind}, {endpoint}); döngü sürecek.", ex);
                Logger.Checkpoint(
                    "TEZI_MDNS_RESPONSE",
                    "FAILED",
                    $"kind={kind}; target={endpoint}; exception={ex.GetType().Name}; message={ex.Message}");
                return false;
            }
        }

        public static byte[] BuildAnnouncementPacketForTest(string ip, int port, string feedPath)
        {
            return BuildResponsePacket(IPAddress.Parse(ip), port, feedPath, 120);
        }

        private static byte[] BuildResponsePacket(
            IPAddress address,
            int port,
            string feedPath,
            uint ttl)
        {
            using var output = new MemoryStream();
            WriteUInt16(output, 0);       // transaction id
            WriteUInt16(output, 0x8400);  // response + authoritative
            WriteUInt16(output, 0);       // questions
            WriteUInt16(output, 1);       // answers: PTR
            WriteUInt16(output, 0);       // authority
            WriteUInt16(output, 3);       // additional: SRV + TXT + A

            WriteRecord(output, ServiceType, 12, 1, ttl, EncodeName(InstanceName));

            using (var srv = new MemoryStream())
            {
                WriteUInt16(srv, 0);
                WriteUInt16(srv, 0);
                WriteUInt16(srv, (ushort)port);
                byte[] host = EncodeName(HostName);
                srv.Write(host, 0, host.Length);
                WriteRecord(output, InstanceName, 33, 0x8001, ttl, srv.ToArray());
            }

            var txt = new List<string>
            {
                "name=" + DisplayName,
                "path=" + feedPath,
                "enabled=1",
                "https=0"
            };
            using (var txtData = new MemoryStream())
            {
                foreach (string value in txt)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    if (bytes.Length > 255)
                        throw new InvalidOperationException("mDNS TXT alanı çok uzun.");
                    txtData.WriteByte((byte)bytes.Length);
                    txtData.Write(bytes, 0, bytes.Length);
                }
                WriteRecord(output, InstanceName, 16, 0x8001, ttl, txtData.ToArray());
            }

            WriteRecord(output, HostName, 1, 0x8001, ttl, address.GetAddressBytes());
            return output.ToArray();
        }

        private static bool IsRelevantQuery(byte[] packet)
        {
            return GetQuestions(packet).Any(q =>
                q.Name.Equals(ServiceType, StringComparison.OrdinalIgnoreCase) ||
                q.Name.Equals(InstanceName, StringComparison.OrdinalIgnoreCase) ||
                q.Name.Equals(HostName, StringComparison.OrdinalIgnoreCase));
        }

        private static string DescribeQuestions(byte[] packet)
        {
            try
            {
                var questions = GetQuestions(packet);
                return questions.Count == 0
                    ? "none"
                    : string.Join(",", questions.Select(q => $"{q.Name}/type={q.Type}/class=0x{q.Class:X4}"));
            }
            catch (Exception ex)
            {
                return "parse-error:" + ex.Message;
            }
        }

        private sealed class DnsQuestion
        {
            public string Name { get; set; }
            public ushort Type { get; set; }
            public ushort Class { get; set; }
        }

        private static List<DnsQuestion> GetQuestions(byte[] packet)
        {
            var result = new List<DnsQuestion>();
            if (packet == null || packet.Length < 12)
                return result;

            int questionCount = ReadUInt16(packet, 4);
            int offset = 12;
            for (int i = 0; i < questionCount; i++)
            {
                string name = ReadName(packet, ref offset);
                if (offset + 4 > packet.Length)
                    throw new InvalidDataException("DNS soru alanı eksik.");
                ushort type = ReadUInt16(packet, offset);
                ushort dnsClass = ReadUInt16(packet, offset + 2);
                offset += 4;
                result.Add(new DnsQuestion { Name = name, Type = type, Class = dnsClass });
            }
            return result;
        }

        private static string ReadName(byte[] packet, ref int offset)
        {
            var labels = new List<string>();
            int cursor = offset;
            int returnOffset = -1;
            int jumps = 0;

            while (true)
            {
                if (cursor >= packet.Length)
                    throw new InvalidDataException("DNS adı paket dışında kaldı.");
                int length = packet[cursor++];
                if (length == 0)
                {
                    if (returnOffset < 0)
                        returnOffset = cursor;
                    break;
                }
                if ((length & 0xC0) == 0xC0)
                {
                    if (cursor >= packet.Length)
                        throw new InvalidDataException("DNS sıkıştırma işaretçisi eksik.");
                    int pointer = ((length & 0x3F) << 8) | packet[cursor++];
                    if (returnOffset < 0)
                        returnOffset = cursor;
                    cursor = pointer;
                    if (++jumps > 16)
                        throw new InvalidDataException("DNS sıkıştırma döngüsü algılandı.");
                    continue;
                }
                if (length > 63 || cursor + length > packet.Length)
                    throw new InvalidDataException("Geçersiz DNS etiketi.");
                labels.Add(Encoding.ASCII.GetString(packet, cursor, length));
                cursor += length;
            }

            offset = returnOffset;
            return string.Join(".", labels);
        }

        private static ushort ReadUInt16(byte[] packet, int offset)
        {
            return (ushort)((packet[offset] << 8) | packet[offset + 1]);
        }

        private static string ToHex(byte[] packet)
        {
            if (packet == null)
                return "";
            int length = Math.Min(packet.Length, 512);
            return BitConverter.ToString(packet, 0, length).Replace("-", "");
        }

        private static void WriteRecord(
            Stream output,
            string name,
            ushort type,
            ushort dnsClass,
            uint ttl,
            byte[] data)
        {
            byte[] encodedName = EncodeName(name);
            output.Write(encodedName, 0, encodedName.Length);
            WriteUInt16(output, type);
            WriteUInt16(output, dnsClass);
            WriteUInt32(output, ttl);
            WriteUInt16(output, (ushort)data.Length);
            output.Write(data, 0, data.Length);
        }

        private static byte[] EncodeName(string name)
        {
            using var output = new MemoryStream();
            foreach (string label in name.TrimEnd('.').Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63)
                    throw new InvalidOperationException("Geçersiz DNS etiketi: " + label);
                output.WriteByte((byte)bytes.Length);
                output.Write(bytes, 0, bytes.Length);
            }
            output.WriteByte(0);
            return output.ToArray();
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            try { _udp?.DropMulticastGroup(MulticastAddress); } catch { }
            try { _udp?.Close(); } catch { }
            try { _loopTask?.Wait(1000); } catch { }
            _udp?.Dispose();
            _cancellation?.Dispose();
            _udp = null;
            _loopTask = null;
            _cancellation = null;
            Logger.Checkpoint("TEZI_MDNS", "STOP");
        }
    }
}
