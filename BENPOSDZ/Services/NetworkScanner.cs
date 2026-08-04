using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BENPOSDZ.Services
{
    // نتائج التقدم أثناء فحص الشبكة
    public class ScanProgress
    {
        public int Scanned { get; set; }
        public int Total { get; set; }
        public string CurrentHost { get; set; } = "";
        public int Percent => Total > 0 ? (int)(Scanned * 100.0 / Total) : 0;
    }

    // مسح الشبكة المحلية بحثاً عن خوادم MySQL — يعتمد على واجهات الشبكة الفعلية
    // (موثوق على Android حيث لا يمكن الاعتماد على اسم الجهاز أو Dns.GetHostAddresses)
    public class NetworkScanner
    {
        private const int DefaultTimeoutMs = 300;
        private const int MaxConcurrency = 64;

        // عناوين IPv4 الخاصة للجهاز من واجهات الشبكة الفعلية (Wi-Fi / Ethernet / إلخ)
        public List<string> GetLocalIPv4Addresses()
        {
            var ips = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                        ips.Add(ip);
                    }
                }
            }
            catch { }

            // احتياط: إذا لم نجد شيئاً نحاول عبر DNS
            if (ips.Count == 0)
            {
                try
                {
                    foreach (var ip in Dns.GetHostAddresses(Environment.MachineName))
                        if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                            ips.Add(ip.ToString());
                }
                catch { }
            }
            return ips.Distinct().ToList();
        }

        // عنوان الشبكة الفرعية /24 لعنوان محلي معيّن
        private string? GetSubnet(string localIp)
        {
            var octets = localIp.Split('.');
            if (octets.Length != 4) return null;
            return $"{octets[0]}.{octets[1]}.{octets[2]}.";
        }

        // فحص منفذ على كل المرشحين: localhost + الاسم + بوابات الشبكة + كل الشبكات الفرعية /24
        public async Task<List<string>> ScanPortAsync(int port, int timeoutMs = DefaultTimeoutMs, IProgress<ScanProgress>? progress = null)
        {
            var candidates = new List<string> { "localhost", "127.0.0.1" };
            try { candidates.Add(Environment.MachineName); } catch { }

            var localIps = GetLocalIPv4Addresses();
            var subnets = new HashSet<string>();
            foreach (var ip in localIps)
            {
                var subnet = GetSubnet(ip);
                if (subnet != null) subnets.Add(subnet);

                // بوابات الشبكة (غالباً هي الخادم) — غير مدعومة على Android
                if (!OperatingSystem.IsAndroid())
                {
                    try
                    {
                        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus != OperationalStatus.Up) continue;
                            foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                            {
                                if (gw.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                                string g = gw.Address.ToString();
                                if (!g.StartsWith("127.")) candidates.Add(g);
                            }
                        }
                    }
                    catch { }
                }
            }

            foreach (var subnet in subnets)
                for (int i = 1; i <= 254; i++)
                    candidates.Add(subnet + i);

            var distinct = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            var found = new List<string>();
            int scanned = 0;
            int total = distinct.Count;
            var semaphore = new SemaphoreSlim(MaxConcurrency);

            var tasks = distinct.Select(async host =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync(host, port);
                    var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                    if (done == connectTask && tcp.Connected)
                        lock (found) found.Add(host);
                }
                catch { }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref scanned);
                    progress?.Report(new ScanProgress { Scanned = current, Total = total, CurrentHost = host });
                }
            }).ToList();

            await Task.WhenAll(tasks);
            return found.OrderBy(x => x).ToList();
        }

        public Task<List<string>> ScanMySqlServersAsync(int timeoutMs = DefaultTimeoutMs, IProgress<ScanProgress>? progress = null)
            => ScanPortAsync(3306, timeoutMs, progress);
    }
}
