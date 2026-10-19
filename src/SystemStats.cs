using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinTools
{
    /// <summary>
    /// Le informazioni della riga di stato della barra: processore, memoria, rete (traffico e connessione a Internet),
    /// indirizzi IPv4 e porte COM. Letture leggere (API di sistema, niente contatori delle prestazioni), pensate per
    /// essere ripetute ogni secondo solo mentre la barra è visibile; porte e indirizzi si rileggono ogni pochi secondi.
    /// </summary>
    public sealed class SystemStats
    {
        public sealed class Snapshot
        {
            public double CpuPercent;
            public ulong RamUsed, RamTotal;
            public double RxBytesPerSec, TxBytesPerSec;
            public bool Online;
            public List<Tuple<string, string>> Ipv4 = new List<Tuple<string, string>>();   // indirizzo, scheda
            public List<Tuple<string, string>> ComPorts = new List<Tuple<string, string>>();   // COMx, nome
        }

        [StructLayout(LayoutKind.Sequential)] struct FILETIME64 { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out FILETIME64 idle, out FILETIME64 kernel, out FILETIME64 user);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX s);

        [DllImport("wininet.dll")] static extern bool InternetGetConnectedState(out int flags, int reserved);

        ulong _lastIdle, _lastKernel, _lastUser;
        long _lastRx, _lastTx;
        DateTime _lastNet = DateTime.MinValue;
        DateTime _lastSlow = DateTime.MinValue;
        List<Tuple<string, string>> _ipv4 = new List<Tuple<string, string>>();
        List<Tuple<string, string>> _com = new List<Tuple<string, string>>();
        int _slowBusy;

        public Snapshot Sample()
        {
            var s = new Snapshot();
            // ---- processore: differenza dei tempi di sistema dall'ultima lettura
            try
            {
                FILETIME64 idle, kernel, user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    ulong i = idle.Value, k = kernel.Value, u = user.Value;
                    if (_lastKernel > 0)
                    {
                        double total = (k - _lastKernel) + (u - _lastUser);
                        double busy = total - (i - _lastIdle);
                        s.CpuPercent = total > 0 ? Math.Max(0, Math.Min(100, busy * 100 / total)) : 0;
                    }
                    _lastIdle = i; _lastKernel = k; _lastUser = u;
                }
            }
            catch { }
            // ---- memoria
            try
            {
                var m = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(ref m)) { s.RamTotal = m.TotalPhys; s.RamUsed = m.TotalPhys - m.AvailPhys; }
            }
            catch { }
            // ---- rete: traffico di tutte le schede attive (non loopback, non tunnel) e stato di Internet
            try
            {
                long rx = 0, tx = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPv4Statistics();
                    rx += st.BytesReceived; tx += st.BytesSent;
                }
                var now = DateTime.UtcNow;
                if (_lastNet != DateTime.MinValue)
                {
                    double sec = Math.Max(0.2, (now - _lastNet).TotalSeconds);
                    s.RxBytesPerSec = Math.Max(0, (rx - _lastRx) / sec);
                    s.TxBytesPerSec = Math.Max(0, (tx - _lastTx) / sec);
                }
                _lastRx = rx; _lastTx = tx; _lastNet = now;
                int flags;
                s.Online = InternetGetConnectedState(out flags, 0) && NetworkInterface.GetIsNetworkAvailable();
            }
            catch { }
            // ---- indirizzi e porte: ogni 5 secondi, in sottofondo (le porte passano da WMI)
            if ((DateTime.UtcNow - _lastSlow).TotalSeconds >= 5 && Interlocked.CompareExchange(ref _slowBusy, 1, 0) == 0)
            {
                _lastSlow = DateTime.UtcNow;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { _ipv4 = ReadIpv4(); _com = ReadComPorts(); }
                    catch (Exception ex) { Program.LogError("Info di sistema", ex); }
                    finally { Interlocked.Exchange(ref _slowBusy, 0); }
                });
            }
            s.Ipv4 = _ipv4;
            s.ComPorts = _com;
            return s;
        }

        static List<Tuple<string, string>> ReadIpv4()
        {
            var list = new List<Tuple<string, string>>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = a.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;   // indirizzo automatico senza DHCP: non serve a nessuno
                    list.Add(Tuple.Create(ip, ni.Name));
                }
            }
            // Prima le schede fisiche (Ethernet, Wi-Fi), poi le virtuali
            return list.OrderBy(t => t.Item2.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || t.Item2.IndexOf("vEthernet", StringComparison.OrdinalIgnoreCase) >= 0 || t.Item2.IndexOf("WSL", StringComparison.OrdinalIgnoreCase) >= 0 || t.Item2.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0 || t.Item2.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0).ThenBy(t => t.Item2).ToList();
        }

        static List<Tuple<string, string>> ReadComPorts()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (var o in searcher.Get())
                    {
                        string name = o["Name"] as string ?? "";
                        int i = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                        int j = i >= 0 ? name.IndexOf(')', i) : -1;
                        if (i >= 0 && j > i) names[name.Substring(i + 1, j - i - 1)] = name.Substring(0, i).Trim();
                    }
                }
            }
            catch { }
            var ports = new List<Tuple<string, string>>();
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames().Distinct().OrderBy(PortNumber))
            {
                string n;
                ports.Add(Tuple.Create(p, names.TryGetValue(p, out n) ? n : ""));
            }
            return ports;
        }

        static int PortNumber(string port)
        {
            int n;
            return int.TryParse(port.Substring(3), out n) ? n : 999;
        }

        public static string FormatRate(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return "0 KB/s";
            if (bytesPerSec < 1024 * 1024) return (bytesPerSec / 1024).ToString("0", Tools.Italian) + " KB/s";
            return (bytesPerSec / 1024 / 1024).ToString("0.0", Tools.Italian) + " MB/s";
        }

        public static string FormatGb(ulong bytes) => (bytes / 1024.0 / 1024 / 1024).ToString("0.0", Tools.Italian);
    }
}
