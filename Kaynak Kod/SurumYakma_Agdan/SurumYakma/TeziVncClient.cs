using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    /// <summary>
    /// RealVNC Viewer acmadan TEZI'nin standart VNC sunucusuna yalnizca bir
    /// klavye tusu gonderir. Ekran goruntusu veya pano verisi okumaz.
    /// </summary>
    public static class TeziVncClient
    {
        public static async Task SendRefreshKeyAsync(
            string host,
            int port,
            CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            CancellationToken token = timeout.Token;

            using var client = new TcpClient(AddressFamily.InterNetwork);
            Logger.Checkpoint("TEZI_VNC_HANDSHAKE", "CONNECTING", $"target={host}:{port}");
            await client.ConnectAsync(host, port, token);
            client.NoDelay = true;
            Logger.Checkpoint("TEZI_VNC_HANDSHAKE", "CONNECTED", $"target={host}:{port}");
            using NetworkStream stream = client.GetStream();

            byte[] serverVersionBytes = await ReadExactAsync(stream, 12, token);
            string serverVersion = Encoding.ASCII.GetString(serverVersionBytes);
            if (!serverVersion.StartsWith("RFB 003.", StringComparison.Ordinal) ||
                serverVersion.Length != 12)
                throw new InvalidDataException("Gecersiz VNC/RFB sunucu surumu: " + serverVersion.Trim());

            int serverMinor = int.Parse(serverVersion.Substring(8, 3));
            int negotiatedMinor = serverMinor >= 8 ? 8 : serverMinor >= 7 ? 7 : 3;
            byte[] clientVersion = Encoding.ASCII.GetBytes(
                "RFB 003." + negotiatedMinor.ToString("000") + "\n");
            await stream.WriteAsync(clientVersion, token);
            Logger.Checkpoint("TEZI_VNC_HANDSHAKE", "PROTOCOL_OK", $"serverVersion={serverVersion.Trim()}; negotiatedMinor={negotiatedMinor}");

            if (negotiatedMinor == 3)
            {
                uint securityType = ReadUInt32(await ReadExactAsync(stream, 4, token), 0);
                if (securityType != 1)
                    throw new InvalidOperationException(
                        "TEZI VNC sunucusu sifresiz RFB guvenlik tipini desteklemiyor (type=" + securityType + ").");
            }
            else
            {
                int count = (await ReadExactAsync(stream, 1, token))[0];
                if (count == 0)
                    throw new InvalidOperationException("TEZI VNC sunucusu kullanilabilir guvenlik tipi bildirmedi.");
                byte[] securityTypes = await ReadExactAsync(stream, count, token);
                if (Array.IndexOf(securityTypes, (byte)1) < 0)
                    throw new InvalidOperationException("TEZI VNC sunucusunda sifresiz RFB guvenlik tipi yok.");
                await stream.WriteAsync(new byte[] { 1 }, token);
                uint securityResult = ReadUInt32(await ReadExactAsync(stream, 4, token), 0);
                if (securityResult != 0)
                    throw new InvalidOperationException("TEZI VNC guvenlik anlasmasi basarisiz (result=" + securityResult + ").");
            }
            Logger.Checkpoint("TEZI_VNC_HANDSHAKE", "SECURITY_OK", "type=None");

            // Shared ClientInit: mevcut bir VNC istemcisi varsa baglantisini kesme.
            await stream.WriteAsync(new byte[] { 1 }, token);
            byte[] serverInit = await ReadExactAsync(stream, 24, token);
            uint nameLength = ReadUInt32(serverInit, 20);
            if (nameLength > 1024 * 1024)
                throw new InvalidDataException("VNC masaustu adi beklenenden uzun.");
            if (nameLength > 0)
                await ReadExactAsync(stream, checked((int)nameLength), token);
            Logger.Checkpoint("TEZI_VNC_HANDSHAKE", "SERVER_INIT_OK", "nameLength=" + nameLength);

            // RFB KeyEvent: type=4, down flag, padding, X11 keysym 'r' (0x72).
            byte[] keySequence = BuildRefreshKeySequenceForTest();
            await Task.Delay(250, token);
            await stream.WriteAsync(keySequence.AsMemory(0, 8), token);
            await stream.FlushAsync(token);
            Logger.Checkpoint("TEZI_VNC_REFRESH", "KEY_DOWN_SENT", $"target={host}:{port}; key=r");
            await Task.Delay(120, token);
            await stream.WriteAsync(keySequence.AsMemory(8, 8), token);
            await stream.FlushAsync(token);
            Logger.Checkpoint("TEZI_VNC_REFRESH", "KEY_UP_SENT", $"target={host}:{port}; key=r");
            await Task.Delay(500, token);
            try { client.Client.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            Logger.Checkpoint("TEZI_VNC_REFRESH", "KEY_SENT", $"target={host}:{port}; key=r");
        }

        public static byte[] BuildRefreshKeySequenceForTest()
        {
            byte[] keyDown = { 4, 1, 0, 0, 0, 0, 0, 0x72 };
            byte[] keyUp = { 4, 0, 0, 0, 0, 0, 0, 0x72 };
            byte[] keySequence = new byte[keyDown.Length + keyUp.Length];
            Buffer.BlockCopy(keyDown, 0, keySequence, 0, keyDown.Length);
            Buffer.BlockCopy(keyUp, 0, keySequence, keyDown.Length, keyUp.Length);
            return keySequence;
        }

        private static async Task<byte[]> ReadExactAsync(
            Stream stream,
            int count,
            CancellationToken ct)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
                if (read == 0)
                    throw new EndOfStreamException("VNC baglantisi beklenen veri alinmadan kapandi.");
                offset += read;
            }
            return buffer;
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }
    }
}
