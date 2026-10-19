using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WinTools.Search;

namespace WinTools.UI
{
    /// <summary>I colori della palette di BlackHole Workspaces per le icone dei tasti (tono medio, leggibile su chiaro e scuro).</summary>
    public static class IconPalette
    {
        public sealed class Entry { public string Key, Label, Hex; }

        public static readonly Entry[] Colors =
        {
            new Entry { Key = "", Label = "Accento", Hex = "" },
            new Entry { Key = "neutral", Label = "Grigio", Hex = "#64748B" },
            new Entry { Key = "brown", Label = "Marrone", Hex = "#A0673C" },
            new Entry { Key = "wine", Label = "Bordeaux", Hex = "#BE123C" },
            new Entry { Key = "danger", Label = "Rosso", Hex = "#EF4444" },
            new Entry { Key = "orange", Label = "Arancione", Hex = "#EA580C" },
            new Entry { Key = "warning", Label = "Ambra", Hex = "#D97706" },
            new Entry { Key = "yellow", Label = "Giallo", Hex = "#CA8A04" },
            new Entry { Key = "lime", Label = "Lime", Hex = "#65A30D" },
            new Entry { Key = "success", Label = "Verde", Hex = "#16A34A" },
            new Entry { Key = "emerald", Label = "Smeraldo", Hex = "#059669" },
            new Entry { Key = "teal", Label = "Verde acqua", Hex = "#0D9488" },
            new Entry { Key = "info", Label = "Azzurro", Hex = "#0891B2" },
            new Entry { Key = "sky", Label = "Celeste", Hex = "#0284C7" },
            new Entry { Key = "primary", Label = "Blu", Hex = "#3B82F6" },
            new Entry { Key = "indigo", Label = "Indaco", Hex = "#6366F1" },
            new Entry { Key = "violet", Label = "Viola", Hex = "#8B5CF6" },
            new Entry { Key = "fuchsia", Label = "Fucsia", Hex = "#C026D3" },
            new Entry { Key = "pink", Label = "Rosa", Hex = "#DB2777" },
        };

        /// <summary>Il pennello per un colore «#RRGGBB» (vuoto o non valido = il pennello dinamico di accento).</summary>
        public static Brush BrushFor(string hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.Trim())); b.Freeze(); return b; }
                catch { }
            }
            return (Brush)Application.Current.Resources["Accent"];
        }

        public static string LabelFor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "Accento";
            var e = Colors.FirstOrDefault(c => string.Equals(c.Hex, hex.Trim(), StringComparison.OrdinalIgnoreCase));
            return e != null ? e.Label : hex.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Com'è fatta l'icona di un tasto: un'immagine della shell (file, app), un glifo Segoe, un disegno MDI, ognuno con
    /// il colore scelto. Usato dalla barra, dai menu dei contenitori, dall'elenco delle impostazioni e dall'editor.
    /// </summary>
    public sealed class IconLook
    {
        public string Glyph = "";          // glifo Segoe Fluent Icons ("" = nessuno)
        public Geometry Mdi;               // disegno MDI (null = nessuno)
        public string ImageKey = "";       // percorso o parsing name per IconCache ("" = nessuno)
        public Brush Brush;                // colore del glifo o del disegno

        public static bool IsGlyph(string s) => !string.IsNullOrEmpty(s) && s.Length == 4 && s.All(Uri.IsHexDigit);
        public static bool IsMdi(string s) => !string.IsNullOrEmpty(s) && s.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase);

        public static IconLook For(BarButton b)
        {
            var look = new IconLook { Brush = IconPalette.BrushFor(b.IconColor) };
            if (b.Separator) return look;
            if (IsMdi(b.Icon))
            {
                var e = IconCatalog.Find(b.Icon);
                if (e != null) { look.Mdi = e.Geometry; return look; }
                look.Glyph = "";
                return look;
            }
            if (IsGlyph(b.Icon)) { look.Glyph = ((char)Convert.ToInt32(b.Icon, 16)).ToString(); return look; }
            if (b.Group && string.IsNullOrEmpty(b.Icon)) { look.Glyph = ""; return look; }
            string key = string.IsNullOrEmpty(b.Icon) ? Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(b.Target)) : b.Icon;
            if (key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase)) { look.Glyph = ""; return look; }
            look.ImageKey = key;
            // Se l'immagine non arriva, un glifo di riserva
            look.Glyph = key.IndexOf(':') > 0 && !Path.IsPathRooted(key) ? "" : "";
            return look;
        }
    }
}
