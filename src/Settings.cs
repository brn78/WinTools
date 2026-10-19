using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace WinTools
{
    /// <summary>Un tasto della barra: che cosa apre e come appare.</summary>
    public class BarButton
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = "";
        /// <summary>Percorso di un programma, file o cartella, indirizzo web, «ms-settings:», «shell:…» o comando.</summary>
        public string Target { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDir { get; set; } = "";
        /// <summary>Vuoto = icona del file; «E8B7» = glifo Segoe Fluent Icons; «mdi:folder» = icona Material Design; altrimenti il percorso di un .ico/.png/.exe.</summary>
        public string Icon { get; set; } = "";
        /// <summary>Colore dell'icona (simbolo o MDI) come «#RRGGBB»; vuoto = colore di accento.</summary>
        public string IconColor { get; set; } = "";
        /// <summary>Avvia come amministratore (chiede l'elevazione UAC).</summary>
        public bool Admin { get; set; }
        /// <summary>Mostra solo l'icona, senza etichetta.</summary>
        public bool IconOnly { get; set; }
        /// <summary>Contenitore: non apre nulla, mostra un menu con i tasti in Children.</summary>
        public bool Group { get; set; }
        public List<BarButton> Children { get; set; } = new List<BarButton>();
        /// <summary>Separatore: una linea sottile tra i tasti, senza funzione. Si trascina e si toglie come un tasto.</summary>
        public bool Separator { get; set; }

        public BarButton Clone()
        {
            return new BarButton
            {
                Id = Id, Label = Label, Target = Target, Arguments = Arguments, WorkingDir = WorkingDir, Icon = Icon, IconColor = IconColor, Admin = Admin, IconOnly = IconOnly, Group = Group, Separator = Separator,
                Children = (Children ?? new List<BarButton>()).Select(c => c.Clone()).ToList()
            };
        }
    }

    /// <summary>Che cosa indicizzare per la ricerca.</summary>
    public class IndexSettings
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Cartelle di partenza; vuoto = tutte le unità fisse.</summary>
        public List<string> Roots { get; set; } = new List<string>();
        /// <summary>Un nome di cartella (saltate tutte quelle con quel nome) o un percorso completo (saltato solo quello).</summary>
        public List<string> Exclude { get; set; } = DefaultExcludes();
        public bool IncludeHidden { get; set; }
        /// <summary>Anche le app del menu Start (desktop e Store) tra i risultati.</summary>
        public bool Apps { get; set; } = true;
        /// <summary>Ogni quante ore rileggere tutto da capo (0 = solo all'avvio e a mano).</summary>
        public int RescanHours { get; set; } = 12;
        /// <summary>All'avvio di Windows, quanti minuti aspettare prima di rileggere i file (il PC appena acceso ha già da fare). 0 = subito.</summary>
        public int StartupDelayMinutes { get; set; } = 3;

        public static List<string> DefaultExcludes()
        {
            return new List<string>
            {
                "$Recycle.Bin", "System Volume Information", "$WinREAgent", "$WINDOWS.~BT", "$WINDOWS.~WS", "Windows.old", "Windows10Upgrade", "Recovery", "Config.Msi", "PerfLogs",
                "node_modules", ".git", ".vs", ".nuget", "__pycache__", ".cache", "WinSxS", "Temp", "INetCache", "Cache", "Code Cache", "GPUCache",
                "%SystemRoot%", "%ProgramData%\\Microsoft", "%LocalAppData%\\Microsoft\\Windows", "%LocalAppData%\\Packages", "%LocalAppData%\\Temp"
            };
        }
    }

    /// <summary>Tutte le preferenze dell'app, salvate in %LOCALAPPDATA%\WinTools\WinTools.settings.json.</summary>
    public class AppSettings
    {
        public int SchemaVersion { get; set; } = 1;
        /// <summary>system, dark o light.</summary>
        public string Theme { get; set; } = "system";
        /// <summary>Colore di accento scelto a mano («#RRGGBB»); vuoto = quello di Windows.</summary>
        public string AccentColor { get; set; } = "";
        /// <summary>top o bottom: lato dello schermo da cui compare la barra.</summary>
        public string Edge { get; set; } = "top";
        /// <summary>La barra compare quando il mouse tocca il bordo.</summary>
        public bool EdgeTrigger { get; set; } = true;
        /// <summary>Quanto il mouse deve restare sul bordo prima che la barra compaia.</summary>
        public int EdgeDelayMs { get; set; } = 150;
        /// <summary>Dopo quanto la barra si nasconde quando il mouse se ne va.</summary>
        public int HideDelayMs { get; set; } = 600;
        /// <summary>Tasto rapido globale, es. «Ctrl+Alt+W»; vuoto = nessuno. (Ctrl+Alt+Spazio è spesso preso da altre app, come Gemini.)</summary>
        public string Hotkey { get; set; } = "Ctrl+Alt+W";
        /// <summary>primary oppure cursor: su quale schermo compare la barra.</summary>
        public string Monitor { get; set; } = "cursor";
        public bool AutoStart { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        /// <summary>Barra sempre visibile (non si nasconde da sola).</summary>
        public bool Pinned { get; set; }
        /// <summary>Barra larga quanto lo schermo, altrimenti centrata e larga quanto serve.</summary>
        public bool FullWidth { get; set; }
        public bool ShowLabels { get; set; } = true;
        /// <summary>Riga di stato sotto i tasti: processore, memoria, rete, indirizzi IPv4 e porte COM.</summary>
        public bool ShowInfoStrip { get; set; } = true;
        /// <summary>Quando la barra compare dal bordo, il cursore va subito nella ricerca (toglie il fuoco all'app in uso).</summary>
        public bool FocusOnEdge { get; set; } = true;
        public bool FirstRunDone { get; set; }
        public List<BarButton> Buttons { get; set; } = new List<BarButton>();
        public IndexSettings Index { get; set; } = new IndexSettings();
        /// <summary>Ultime ricerche aperte (percorsi), per i suggerimenti con la casella vuota.</summary>
        public List<string> Recent { get; set; } = new List<string>();
        /// <summary>Quante volte ogni percorso è stato aperto dai risultati: i preferiti salgono.</summary>
        public Dictionary<string, int> Usage { get; set; } = new Dictionary<string, int>();

        // ---------------------------------------------------------------- percorsi
        public static string AppDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTools");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }
        public static string SettingsPath => Path.Combine(AppDir, "WinTools.settings.json");
        public static string IndexPath => Path.Combine(AppDir, "index.bin");
        public static string ErrorLog => Path.Combine(AppDir, "WinTools-error.log");

        // ---------------------------------------------------------------- lettura e scrittura
        static readonly object Gate = new object();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var s = new JavaScriptSerializer { MaxJsonLength = 50 * 1024 * 1024 }.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        s.Normalize();
                        return s;
                    }
                }
            }
            catch (Exception ex) { Program.LogError("Lettura impostazioni", ex); }
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        public void Save()
        {
            lock (Gate)
            {
                try
                {
                    string json = new JavaScriptSerializer { MaxJsonLength = 50 * 1024 * 1024 }.Serialize(this);
                    string tmp = SettingsPath + ".tmp";
                    File.WriteAllText(tmp, Pretty(json), new UTF8Encoding(false));
                    if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, null); else File.Move(tmp, SettingsPath);
                }
                catch (Exception ex) { Program.LogError("Salvataggio impostazioni", ex); }
            }
        }

        void Normalize()
        {
            if (Buttons == null) Buttons = new List<BarButton>();
            if (Index == null) Index = new IndexSettings();
            if (Index.Roots == null) Index.Roots = new List<string>();
            if (Index.Exclude == null) Index.Exclude = IndexSettings.DefaultExcludes();
            if (Recent == null) Recent = new List<string>();
            if (Usage == null) Usage = new Dictionary<string, int>();
            if (Theme != "dark" && Theme != "light") Theme = "system";
            if (Edge != "bottom") Edge = "top";
            if (Monitor != "primary") Monitor = "cursor";
            EdgeDelayMs = Math.Max(0, Math.Min(2000, EdgeDelayMs));
            HideDelayMs = Math.Max(100, Math.Min(5000, HideDelayMs));
            NormalizeButtons(Buttons);
            if (Index.RescanHours < 0) Index.RescanHours = 0;
            if (Index.StartupDelayMinutes < 0) Index.StartupDelayMinutes = 0;
        }

        static void NormalizeButtons(List<BarButton> list)
        {
            foreach (var b in list)
            {
                if (string.IsNullOrEmpty(b.Id)) b.Id = Guid.NewGuid().ToString("N");
                b.Label = b.Label ?? ""; b.Target = b.Target ?? ""; b.Arguments = b.Arguments ?? ""; b.WorkingDir = b.WorkingDir ?? ""; b.Icon = b.Icon ?? ""; b.IconColor = b.IconColor ?? "";
                if (b.Children == null) b.Children = new List<BarButton>();
                // Un contenitore dentro un contenitore no: i suoi tasti salgono di un livello
                foreach (var c in b.Children.Where(c => c.Group).ToList()) { b.Children.Remove(c); b.Children.AddRange(c.Children ?? new List<BarButton>()); }
                NormalizeButtons(b.Children);
            }
        }

        /// <summary>Il tasto con quell'id, sulla barra o dentro un contenitore; owner = l'elenco che lo contiene.</summary>
        public BarButton FindButton(string id, out List<BarButton> owner)
        {
            foreach (var b in Buttons)
            {
                if (b.Id == id) { owner = Buttons; return b; }
                foreach (var c in b.Children) if (c.Id == id) { owner = b.Children; return c; }
            }
            owner = null;
            return null;
        }

        /// <summary>Toglie il tasto da dove sta (barra o contenitore) e lo restituisce; null se non c'è.</summary>
        public BarButton DetachButton(string id)
        {
            List<BarButton> owner;
            var b = FindButton(id, out owner);
            if (b != null) owner.Remove(b);
            return b;
        }

        public void RemoveRecent(string path)
        {
            Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }

        public void ClearRecents()
        {
            Recent.Clear();
        }

        /// <summary>Segna un percorso come aperto: in cima ai recenti e un punto in più per la pertinenza.</summary>
        public void NoteOpened(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            if (Recent.Count > 12) Recent.RemoveRange(12, Recent.Count - 12);
            int n;
            Usage.TryGetValue(path, out n);
            Usage[path] = Math.Min(n + 1, 1000);
            if (Usage.Count > 500)
            {
                foreach (var k in Usage.OrderBy(kv => kv.Value).Take(Usage.Count - 400).Select(kv => kv.Key).ToList()) Usage.Remove(k);
            }
        }

        /// <summary>I tasti del primo avvio: gli strumenti che si usano ogni giorno.</summary>
        public static List<BarButton> DefaultButtons()
        {
            return new List<BarButton>
            {
                new BarButton { Label = "Esplora file", Target = "explorer.exe", Icon = "EC50" },
                new BarButton { Label = "Download", Target = "shell:Downloads", Icon = "E896" },
                new BarButton { Label = "Terminale", Target = Tools.TerminalCommand(), Icon = "E756" },
                new BarButton { Label = "Impostazioni", Target = "ms-settings:", Icon = "E713" },
                new BarButton { Label = "Gestione attività", Target = "taskmgr.exe", Icon = "E9D9" },
                new BarButton { Label = "Calcolatrice", Target = "calc.exe", Icon = "E8EF" },
                new BarButton { Label = "Blocco note", Target = "notepad.exe", Icon = "E70B" },
                new BarButton { Label = "Cattura schermo", Target = "ms-screenclip:", Icon = "E7A8" },
                new BarButton { Label = "Cestino", Target = "shell:RecycleBinFolder", Icon = "E74D" },
            };
        }

        /// <summary>JSON con rientri e una riga per proprietà: si legge e si modifica anche a mano.</summary>
        static string Pretty(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0; bool inStr = false; bool esc = false;
            foreach (char c in json)
            {
                if (inStr)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{': case '[': sb.Append(c); sb.Append("\r\n"); indent++; sb.Append(' ', indent * 2); break;
                    case '}': case ']': sb.Append("\r\n"); indent--; sb.Append(' ', indent * 2); sb.Append(c); break;
                    case ',': sb.Append(c); sb.Append("\r\n"); sb.Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
