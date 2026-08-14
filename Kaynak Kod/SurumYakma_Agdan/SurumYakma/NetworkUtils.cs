using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SurumYakma
{
    public static class NetworkUtils
    {
        public static async Task WaitForDriveDisconnectAsync(string driveRoot, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int consecutiveMissingChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                consecutiveMissingChecks = Directory.Exists(driveRoot) ? 0 : consecutiveMissingChecks + 1;
                if (consecutiveMissingChecks >= 3)
                    return;

                await Task.Delay(500, ct);
            }

            throw new TimeoutException("Flash belleğin SIM PC'den çıkarıldığı beklenen sürede algılanmadı.");
        }

        public static Task WaitForOtgAsync(string interfaceNameContains, TimeSpan timeout, CancellationToken ct)
        {
            return WaitForInterfaceStateAsync(interfaceNameContains, true, timeout, ct);
        }

        public static Task WaitForOtgDisconnectAsync(string interfaceNameContains, TimeSpan timeout, CancellationToken ct)
        {
            return WaitForInterfaceStateAsync(interfaceNameContains, false, timeout, ct);
        }

        private static async Task WaitForInterfaceStateAsync(
            string interfaceNameContains,
            bool shouldExist,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(interfaceNameContains))
                throw new InvalidOperationException("OTG ağ arayüzü adı ayarlanmamış.");

            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                bool exists = NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                    n.Name.IndexOf(interfaceNameContains, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.Description.IndexOf(interfaceNameContains, StringComparison.OrdinalIgnoreCase) >= 0);

                if (exists == shouldExist)
                    return;

                await Task.Delay(500, ct);
            }

            throw new TimeoutException(
                shouldExist
                    ? "OTG bağlantısı beklenen sürede algılanmadı."
                    : "OTG bağlantısının çıkarıldığı beklenen sürede algılanmadı.");
        }

        public static Task WaitUntilReachableAsync(
            string ip,
            int pingTimeoutMs,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return WaitForPingStateAsync(ip, pingTimeoutMs, timeout, true, ct);
        }

        public static Task WaitUntilUnreachableAsync(
            string ip,
            int pingTimeoutMs,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return WaitForPingStateAsync(ip, pingTimeoutMs, timeout, false, ct);
        }

        private static async Task WaitForPingStateAsync(
            string ip,
            int pingTimeoutMs,
            TimeSpan timeout,
            bool shouldBeReachable,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int consecutiveMatches = 0;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                bool reachable = false;

                try
                {
                    using (var ping = new Ping())
                    {
                        PingReply reply = await ping.SendPingAsync(ip, pingTimeoutMs);
                        reachable = reply.Status == IPStatus.Success;
                    }
                }
                catch (PingException)
                {
                    reachable = false;
                }

                consecutiveMatches = reachable == shouldBeReachable ? consecutiveMatches + 1 : 0;
                if (consecutiveMatches >= 3)
                    return;

                await Task.Delay(1000, ct);
            }

            throw new TimeoutException(
                shouldBeReachable
                    ? "UKB beklenen sürede ağda erişilebilir olmadı."
                    : "UKB'nin otomatik kurulum sonrası kapandığı doğrulanamadı.");
        }
    }
}
