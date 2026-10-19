using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media;

namespace WinTools.Search
{
    /// <summary>Un'icona del catalogo: nome MDI, tracciato SVG (24×24), categorie e alias ufficiali.</summary>
    public sealed class IconEntry
    {
        public string Name = "";
        public string Path = "";
        public string[] Categories = new string[0];
        public string[] Aliases = new string[0];
        public bool IsOutline;
        internal string[] Parts = new string[0];
        internal string[] AliasParts = new string[0];
        internal string[] Words = new string[0];
        Geometry _geometry;

        /// <summary>Il disegno pronto per WPF (congelato, condiviso).</summary>
        public Geometry Geometry
        {
            get
            {
                if (_geometry == null)
                {
                    try { var g = Geometry.Parse(Path); g.Freeze(); _geometry = g; }
                    catch { _geometry = Geometry.Empty; }
                }
                return _geometry;
            }
        }
    }

    /// <summary>
    /// Catalogo delle icone Material Design Icons 7.4.47 (Pictogrammers, licenza Apache 2.0), incluso nel programma:
    /// niente internet. Lo stesso file e la stessa ricerca in italiano di BlackHole Workspaces: le parti dei nomi inglesi
    /// hanno le loro traduzioni (Assets\mdi-it.txt), le categorie un nome italiano. Si carica alla prima richiesta.
    /// </summary>
    public static class IconCatalog
    {
        const uint Magic = 0x3149554D;   // "MUI1"
        static readonly object Gate = new object();
        static State _state;

        sealed class State
        {
            public string Version = "";
            public IconEntry[] Icons = new IconEntry[0];
            public Dictionary<string, IconEntry> Lookup = new Dictionary<string, IconEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> Suggested = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        static State Current
        {
            get
            {
                if (_state != null) return _state;
                lock (Gate)
                {
                    if (_state == null)
                    {
                        try { _state = Load(); }
                        catch (Exception ex) { Program.LogError("Catalogo icone", ex); _state = new State(); }
                    }
                }
                return _state;
            }
        }

        public static string Version => Current.Version;
        public static int Count => Current.Icons.Length;
        public static bool IsLoaded => _state != null;

        /// <summary>Carica in anticipo (su un thread di servizio), così la prima ricerca non aspetta.</summary>
        public static void Preload() { System.Threading.ThreadPool.QueueUserWorkItem(_ => { var s = Current; }); }

        /// <summary>L'icona per nome o alias ufficiale (anche con il prefisso «mdi:»); null se non c'è.</summary>
        public static IconEntry Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Trim();
            if (n.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase)) n = n.Substring(4);
            IconEntry icon;
            return Current.Lookup.TryGetValue(n, out icon) ? icon : null;
        }

        /// <summary>
        /// Cerca per parole (italiane o inglesi, ognuna deve trovare qualcosa). Senza parole propone le icone più utili
        /// per una barra di avvio, poi le altre in ordine alfabetico. outlineToo = anche le varianti a contorno.
        /// </summary>
        public static List<IconEntry> Search(string query, int limit, out int total)
        {
            var st = Current;
            var tokens = Normalize(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            IEnumerable<IconEntry> ordered;
            if (tokens.Length == 0)
                ordered = st.Icons.OrderBy(i => SuggestedRank(st, i.Name)).ThenBy(i => i.IsOutline ? 1 : 0).ThenBy(i => i.Name, StringComparer.Ordinal);
            else
                ordered = st.Icons.Select(i => new { Icon = i, Score = Score(i, tokens) }).Where(x => x.Score < int.MaxValue)
                    .OrderBy(x => x.Score).ThenBy(x => x.Icon.Parts.Length).ThenBy(x => x.Icon.Name, StringComparer.Ordinal).Select(x => x.Icon);
            var list = ordered.ToList();
            total = list.Count;
            return list.Take(Math.Max(1, limit)).ToList();
        }

        static int SuggestedRank(State st, string name)
        {
            int r;
            return st.Suggested.TryGetValue(name, out r) ? r : int.MaxValue;
        }

        /// <summary>
        /// Punteggio di un'icona per le parole cercate (più basso = più pertinente; MaxValue = non corrisponde). Ogni parola
        /// deve corrispondere: al nome (intera o inizio di una parte), agli alias, alle traduzioni italiane o alle categorie.
        /// </summary>
        static int Score(IconEntry icon, string[] tokens)
        {
            int total = 0;
            foreach (var token in tokens)
            {
                string stem = StemOf(token);
                int best = int.MaxValue;
                if (icon.Parts.Contains(token)) best = 0;
                else if (icon.Parts.Any(p => p.StartsWith(token, StringComparison.Ordinal))) best = 6;
                else if (icon.AliasParts.Contains(token)) best = 10;
                else if (icon.Words.Contains(token)) best = 14;
                else if (token.Length >= 3 && icon.AliasParts.Any(p => p.StartsWith(token, StringComparison.Ordinal))) best = 18;
                else if (token.Length >= 3 && icon.Words.Any(w => w.StartsWith(stem, StringComparison.Ordinal))) best = 24;
                else if (token.Length >= 4 && icon.Name.IndexOf(token, StringComparison.Ordinal) >= 0) best = 40;
                if (best == int.MaxValue) return int.MaxValue;
                total += best;
            }
            return total + (icon.IsOutline ? 1 : 0);
        }

        static string StemOf(string token)
        {
            if (token.Length >= 5 && "aeio".IndexOf(token[token.Length - 1]) >= 0) return token.Substring(0, token.Length - 1);
            return token;
        }

        /// <summary>Minuscolo, senza accenti, solo lettere e cifre separate da uno spazio.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        // ------------------------------------------------------------------ caricamento
        static State Load()
        {
            var st = new State();
            var italian = ParseTerms(ReadResource("WinTools.mdi-it.txt"));
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinTools.mdi.mui.gz"))
            {
                if (stream == null) throw new InvalidDataException("Catalogo delle icone non incluso nel programma.");
                using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new BinaryReader(gz, new UTF8Encoding(false, true)))
                {
                    if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Catalogo delle icone non valido.");
                    if (reader.ReadInt32() != 1) throw new InvalidDataException("Schema del catalogo non supportato.");
                    st.Version = reader.ReadString();
                    int n = reader.ReadInt32();
                    if (n <= 0 || n > 100000) throw new InvalidDataException("Catalogo delle icone non valido.");
                    var list = new List<IconEntry>(n);
                    for (int i = 0; i < n; i++)
                    {
                        string name = reader.ReadString();
                        string path = reader.ReadString();
                        reader.ReadString();   // autore
                        reader.ReadString();   // versione in cui è comparsa
                        var cats = ReadArray(reader);
                        var aliases = ReadArray(reader);
                        ReadArray(reader);     // stili
                        list.Add(Build(name, path, cats, aliases, italian));
                    }
                    st.Icons = list.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                }
            }
            foreach (var icon in st.Icons) st.Lookup[icon.Name] = icon;
            foreach (var icon in st.Icons) foreach (var a in icon.Aliases) if (!st.Lookup.ContainsKey(a)) st.Lookup[a] = icon;
            int rank = 0;
            foreach (var s in SuggestedNames.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IconEntry icon;
                if (st.Lookup.TryGetValue(s, out icon) && !st.Suggested.ContainsKey(icon.Name)) st.Suggested[icon.Name] = rank++;
            }
            return st;
        }

        static string ReadResource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static string[] ReadArray(BinaryReader reader)
        {
            int n = reader.ReadInt32();
            if (n < 0 || n > 1000) throw new InvalidDataException("Catalogo delle icone non valido.");
            var values = new string[n];
            for (int i = 0; i < n; i++) values[i] = reader.ReadString();
            return values;
        }

        static IconEntry Build(string name, string path, string[] cats, string[] aliases, Dictionary<string, string[]> italian)
        {
            var parts = name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var aliasParts = aliases.SelectMany(a => a.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts.Concat(aliasParts)) { string[] it; if (italian.TryGetValue(p, out it)) words.UnionWith(it); }
            for (int i = 0; i < parts.Length - 1; i++) { string[] it; if (italian.TryGetValue(parts[i] + "-" + parts[i + 1], out it)) words.UnionWith(it); }
            foreach (var c in cats)
            {
                words.UnionWith(Normalize(c).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                words.UnionWith(Normalize(CategoryLabel(c)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }
            return new IconEntry { Name = name, Path = path, Categories = cats, Aliases = aliases, IsOutline = parts.Contains("outline"), Parts = parts, AliasParts = aliasParts, Words = words.ToArray() };
        }

        static Dictionary<string, string[]> ParseTerms(string text)
        {
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var line in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var words = Normalize(line.Substring(colon + 1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string[] existing;
                map[key] = map.TryGetValue(key, out existing) ? existing.Concat(words).Distinct().ToArray() : words;
            }
            return map;
        }

        static string CategoryLabel(string key)
        {
            string label;
            return CategoryLabels.TryGetValue(key, out label) ? label : key;
        }

        static readonly Dictionary<string, string> CategoryLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Account / User", "Persone e account"}, {"Agriculture", "Agricoltura"}, {"Alert / Error", "Avvisi ed errori"},
            {"Alpha / Numeric", "Lettere e numeri"}, {"Animal", "Animali"}, {"Arrange", "Disposizione"}, {"Arrow", "Frecce"},
            {"Audio", "Audio"}, {"Automotive", "Auto e veicoli"}, {"Banking", "Banca e pagamenti"}, {"Battery", "Batterie"},
            {"Cellphone / Phone", "Telefoni"}, {"Clothing", "Abbigliamento"}, {"Cloud", "Cloud"}, {"Color", "Colori"},
            {"Currency", "Valute"}, {"Database", "Database"}, {"Date / Time", "Data e ora"}, {"Developer / Languages", "Sviluppo software"},
            {"Device / Tech", "Dispositivi e tecnologia"}, {"Drawing / Art", "Disegno e arte"}, {"Edit / Modify", "Modifica"},
            {"Emoji", "Emoji"}, {"Files / Folders", "File e cartelle"}, {"Food / Drink", "Cibo e bevande"}, {"Form", "Moduli"},
            {"Gaming / RPG", "Giochi"}, {"Geographic Information System", "Mappe e territorio"}, {"Hardware / Tools", "Attrezzi"},
            {"Health / Beauty", "Salute e bellezza"}, {"Holiday", "Feste"}, {"Home Automation", "Casa e domotica"}, {"Lock", "Lucchetti e accessi"},
            {"Math", "Matematica"}, {"Medical / Hospital", "Medicina e ospedale"}, {"Music", "Musica"}, {"Nature", "Natura"},
            {"Navigation", "Navigazione"}, {"Notification", "Notifiche"}, {"People / Family", "Persone e famiglia"},
            {"Photography", "Fotografia"}, {"Places", "Luoghi"}, {"Printer", "Stampanti"}, {"Religion", "Religione"},
            {"Science", "Scienza"}, {"Settings", "Impostazioni"}, {"Shape", "Forme"}, {"Shopping", "Acquisti"}, {"Sport", "Sport"},
            {"Text / Content / Format", "Testo e formato"}, {"Tooltip", "Fumetti"}, {"Transportation + Flying", "Trasporti aerei"},
            {"Transportation + Other", "Altri trasporti"}, {"Transportation + Road", "Trasporti su strada"},
            {"Transportation + Water", "Trasporti via acqua"}, {"Vector", "Vettoriale"}, {"Video / Movie", "Video e film"},
            {"View", "Viste"}, {"Weather", "Meteo"}
        };

        /// <summary>Le icone proposte per prime, senza ricerca: le più utili per i tasti di una barra di avvio.</summary>
        const string SuggestedNames = @"
            folder folder-open folder-outline folder-multiple folder-home folder-download folder-image folder-music folder-network folder-star folder-cog folder-zip
            download upload file-document file-document-outline file-pdf-box file-excel file-word file-image file-code file-cabinet archive clipboard-text note-text
            application application-cog cog cog-outline console console-line powershell terminal microsoft-windows monitor laptop desktop-classic keyboard mouse printer
            web earth google-chrome microsoft-edge firefox email email-outline calendar calendar-month clock-outline calculator calculator-variant
            camera image image-multiple video music play-circle youtube spotify microphone headphones volume-high
            home home-outline account account-group account-circle briefcase domain office-building store school
            lan lan-connect wifi router-wireless server server-network database cloud ip-network vpn shield shield-lock lock key
            tools wrench hammer-wrench screwdriver cog-sync update refresh power power-plug lightning-bolt flash battery
            delete trash-can-outline recycle broom content-copy content-paste content-cut magnify text-box-search
            star star-outline heart heart-outline bookmark flag pin bell information-outline help-circle alert-circle check-circle
            chart-line chart-bar finance cash currency-eur cart basket tag tag-multiple
            map-marker map car truck airplane train bus bike
            lightbulb lightbulb-on led-on thermostat fan air-conditioner cctv camera-wireless home-automation remote
            gamepad controller xbox puzzle rocket-launch robot brain palette brush pencil format-paint
            phone cellphone whatsapp send message-text chat forum
            weather-sunny weather-night weather-cloudy coffee food pizza silverware-fork-knife
            arrow-right-bold chevron-right plus minus close check dots-horizontal menu view-grid view-list
        ";
    }
}
