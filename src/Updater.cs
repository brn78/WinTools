using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinTools
{
    /// <summary>
    /// Aggiornamento automatico dalle release di GitHub (come WinOptima): si legge l'ultima release, se è più recente
    /// si scarica WinTools.exe in %LOCALAPPDATA%\WinTools\update, se ne verificano dimensione, intestazione e SHA-256
    /// (quando GitHub lo fornisce) e si installa alla chiusura, oppure subito con «Riavvia e aggiorna». L'exe in uso
    /// diventa «WinTools.old.exe» (rinominare un eseguibile in esecuzione è consentito) e si elimina al primo avvio.
    /// </summary>
    public sealed class Updater
    {
        public sealed class Info { public Version Version; public string Url; public long Size; public string Sha256; public string Page; public string Notes; }

        public Info Available { get; private set; }
        public string ReadyPath { get; private set; }
        public bool Checking { get; private set; }
        public DateTime LastCheck { get; private set; }

        /// <summary>Nuova versione trovata (il download parte da solo).</summary>
        public event Action<Info> UpdateAvailable;
        /// <summary>Scaricata e verificata: si può installare.</summary>
        public event Action<Info> UpdateReady;
        /// <summary>Controllo a mano senza novità (true) o fallito (false), con il messaggio.</summary>
        public event Action<bool, string> CheckFinished;

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinTools-Updater", Program.Version));
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return c;
        }

        public static Version ParseVersion(string text)
        {
            string t = Regex.Replace((text ?? "").Trim(), "^[vV]", "");
            if (!Regex.IsMatch(t, @"^\d+(\.\d+){1,3}$")) return null;
            if (!t.Contains(".")) t += ".0";
            Version v;
            return Version.TryParse(t, out v) ? v : null;
        }

        static Version Current
        {
            get
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
            }
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        /// <summary>Controlla l'ultima release; se è più recente la scarica. manual = avvisa anche se non c'è niente di nuovo.</summary>
        public async Task CheckAsync(bool manual)
        {
            if (Checking) return;
            Checking = true;
            try
            {
                LastCheck = DateTime.Now;
                string json = await Http.GetStringAsync($"https://api.github.com/repos/{Program.UpdateRepo}/releases/latest").ConfigureAwait(false);
                var rel = new JavaScriptSerializer().Deserialize<dynamic>(json);
                var latest = ParseVersion((string)rel["tag_name"]);
                if (latest == null) throw new Exception("tag della release non riconosciuto");
                latest = Normalize(latest);
                dynamic asset = null;
                foreach (var a in (System.Collections.IEnumerable)rel["assets"])
                {
                    dynamic d = a;
                    if (string.Equals((string)d["name"], "WinTools.exe", StringComparison.OrdinalIgnoreCase)) { asset = d; break; }
                }
                if (latest > Current && asset != null)
                {
                    string digest = "";
                    try { object dg; if (((System.Collections.Generic.IDictionary<string, object>)asset).TryGetValue("digest", out dg) && dg is string && ((string)dg).StartsWith("sha256:")) digest = ((string)dg).Substring(7); } catch { }
                    var info = new Info
                    {
                        Version = latest, Url = (string)asset["browser_download_url"], Size = Convert.ToInt64(asset["size"]), Sha256 = digest,
                        Page = (string)rel["html_url"], Notes = rel["body"] as string ?? ""
                    };
                    Available = info;
                    UpdateAvailable?.Invoke(info);
                    await DownloadAsync(info).ConfigureAwait(false);
                }
                else if (manual)
                {
                    CheckFinished?.Invoke(true, $"Stai usando l'ultima versione ({Program.Version}).");
                }
            }
            catch (Exception ex)
            {
                Program.LogError("Controllo aggiornamenti", ex);
                if (manual) CheckFinished?.Invoke(false, "Verifica la connessione a Internet e riprova più tardi.");
            }
            finally { Checking = false; }
        }

        async Task DownloadAsync(Info info)
        {
            try
            {
                string dir = Path.Combine(AppSettings.AppDir, "update");
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir, "*.exe")) { try { File.Delete(old); } catch { } }
                string dest = Path.Combine(dir, $"WinTools-{info.Version}.exe");
                using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                }
                var bytes = File.ReadAllBytes(dest);
                if (info.Size > 0 && bytes.Length != info.Size) throw new Exception($"dimensione inattesa ({bytes.Length} invece di {info.Size} byte)");
                if (bytes.Length < 2 || bytes[0] != 0x4D || bytes[1] != 0x5A) throw new Exception("il file scaricato non è un eseguibile valido");
                if (!string.IsNullOrEmpty(info.Sha256))
                {
                    using (var sha = SHA256.Create())
                    {
                        string h = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                        if (!string.Equals(h, info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("impronta SHA-256 non corrispondente");
                    }
                }
                ReadyPath = dest;
                UpdateReady?.Invoke(info);
            }
            catch (Exception ex)
            {
                Program.LogError("Download aggiornamento", ex);
                ReadyPath = null;
            }
        }

        /// <summary>Sostituisce l'exe con quello scaricato. relaunch = riavvia subito la nuova versione.</summary>
        public bool Install(bool relaunch)
        {
            string exe = Program.ExePath;
            string neu = ReadyPath;
            if (string.IsNullOrEmpty(neu) || !File.Exists(neu) || !File.Exists(exe)) return false;
            string old = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + ".old.exe");
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Copy(neu, exe, true); }
                catch { File.Move(old, exe); throw; }
                try { File.Delete(neu); } catch { }
                ReadyPath = null;
                // Cancellazione differita del vecchio exe: attende qualche secondo che questo processo termini
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c ping -n 6 127.0.0.1 >nul & del /f /q \"{old}\"")
                    { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = false });
                }
                catch { }
                if (relaunch)
                {
                    // Prima si lascia il mutex di istanza unica: la nuova versione, trovandolo occupato, si chiuderebbe da sola
                    Program.ReleaseInstance();
                    try { Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
                    catch (Exception ex) { Program.LogError("Riavvio dopo aggiornamento", ex); }
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.LogError("Installazione aggiornamento", ex);
                return false;
            }
        }
    }
}
