using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinTools
{
    /// <summary>
    /// File .rdp di Connessione Desktop remoto: mstsc.exe non accetta l'utente dalla riga di comando, quindi un tasto
    /// con host e utente diventa un file .rdp generato nell'archivio di WinTools. Si parte dal Default.rdp dell'utente
    /// (in Documenti, con le sue preferenze di schermo e periferiche) e si sovrascrivono le voci che contano.
    /// </summary>
    public static class RdpFile
    {
        public sealed class Settings
        {
            public string Host = "";
            public string User = "";
            public bool FullScreen = true;
            public bool MultiMon;
            public bool Admin;
        }

        static readonly string[] Managed = { "full address", "username", "screen mode id", "use multimon", "administrative session", "domain" };

        /// <summary>Legge le voci di un file .rdp (chiave → valore, senza il tipo); null se non si legge.</summary>
        public static Dictionary<string, string> Read(string path)
        {
            try
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path))
                {
                    int a = line.IndexOf(':');
                    if (a <= 0) continue;
                    int b = line.IndexOf(':', a + 1);
                    if (b < 0) continue;
                    d[line.Substring(0, a).Trim()] = line.Substring(b + 1).Trim();
                }
                return d;
            }
            catch { return null; }
        }

        public static Settings ReadSettings(string path)
        {
            var d = Read(path);
            if (d == null) return null;
            string v;
            var s = new Settings();
            if (d.TryGetValue("full address", out v)) s.Host = v;
            if (d.TryGetValue("username", out v)) s.User = v;
            if (d.TryGetValue("domain", out v) && v != "" && !s.User.Contains("\\") && !s.User.Contains("@")) s.User = v + "\\" + s.User;
            if (d.TryGetValue("screen mode id", out v)) s.FullScreen = v.Trim() == "2";
            if (d.TryGetValue("use multimon", out v)) s.MultiMon = v.Trim() == "1";
            if (d.TryGetValue("administrative session", out v)) s.Admin = v.Trim() == "1";
            return s;
        }

        /// <summary>Scrive il file .rdp: le righe del Default.rdp dell'utente (se c'è) tranne quelle gestite qui, più host, utente e opzioni.</summary>
        public static void Write(string path, Settings s)
        {
            var lines = new List<string>();
            string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");
            try
            {
                if (File.Exists(template))
                    lines.AddRange(File.ReadAllLines(template).Where(l => { int a = l.IndexOf(':'); return a > 0 && !Managed.Contains(l.Substring(0, a).Trim(), StringComparer.OrdinalIgnoreCase); }));
            }
            catch { lines.Clear(); }
            if (lines.Count == 0)
            {
                lines.AddRange(new[]
                {
                    "session bpp:i:32", "compression:i:1", "keyboardhook:i:2", "audiocapturemode:i:0", "videoplaybackmode:i:1",
                    "connection type:i:7", "networkautodetect:i:1", "bandwidthautodetect:i:1", "displayconnectionbar:i:1",
                    "autoreconnection enabled:i:1", "authentication level:i:2", "prompt for credentials:i:0", "negotiate security layer:i:1",
                    "redirectclipboard:i:1", "redirectprinters:i:1", "redirectsmartcards:i:1", "remoteapplicationmode:i:0",
                    "gatewayusagemethod:i:4", "gatewaycredentialssource:i:4", "gatewayprofileusagemethod:i:0", "promptcredentialonce:i:0"
                });
            }
            lines.Add("full address:s:" + s.Host.Trim());
            if (!string.IsNullOrWhiteSpace(s.User)) lines.Add("username:s:" + s.User.Trim());
            lines.Add("screen mode id:i:" + (s.FullScreen ? "2" : "1"));
            lines.Add("use multimon:i:" + (s.MultiMon ? "1" : "0"));
            lines.Add("administrative session:i:" + (s.Admin ? "1" : "0"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // mstsc vuole UTF-16 LE con BOM, come i file che salva lui
            File.WriteAllLines(path, lines, new UnicodeEncoding(false, true));
        }

        /// <summary>Nome file sicuro per il .rdp a partire dall'etichetta del tasto (o dall'host).</summary>
        public static string FileNameFor(string label, string host)
        {
            string name = string.IsNullOrWhiteSpace(label) ? host : label;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            if (name == "") name = "Desktop remoto";
            return name + ".rdp";
        }

        /// <summary>Vero se il bersaglio è mstsc.exe (con o senza percorso) o un file .rdp.</summary>
        public static bool IsRdpTarget(string target)
        {
            string t = Environment.ExpandEnvironmentVariables((target ?? "").Trim().Trim('"'));
            if (t == "") return false;
            if (t.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase)) return true;
            string name = Path.GetFileName(t);
            return name.Equals("mstsc.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("mstsc", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Host, /admin, /f e /multimon dagli argomenti di mstsc.exe (per prendere ciò che si era già scritto a mano).</summary>
        public static Settings FromArguments(string args)
        {
            var s = new Settings { FullScreen = true };   // come mstsc senza /w e /h
            foreach (var raw in (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string a = raw.Trim('"');
                if (a.StartsWith("/w:", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/h:", StringComparison.OrdinalIgnoreCase)) s.FullScreen = false;
                else if (a.StartsWith("/v:", StringComparison.OrdinalIgnoreCase)) s.Host = a.Substring(3);
                else if (a.Equals("/admin", StringComparison.OrdinalIgnoreCase) || a.Equals("/console", StringComparison.OrdinalIgnoreCase)) s.Admin = true;
                else if (a.Equals("/f", StringComparison.OrdinalIgnoreCase)) s.FullScreen = true;
                else if (a.Equals("/multimon", StringComparison.OrdinalIgnoreCase)) s.MultiMon = true;
            }
            return s;
        }
    }
}
