using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinTools
{
    /// <summary>
    /// Tema Fluent (Windows 11) come in WinOptima: i colori sono pennelli «dinamici» nelle risorse dell'app
    /// (DynamicResource). Cambiando tema o colore di accento vengono sostituiti e tutta l'interfaccia si aggiorna.
    /// </summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; } = true;
        public static Color Accent { get; private set; } = Color.FromRgb(0x60, 0xCD, 0xFF);
        static string _lastKey = "";

        public static event Action Changed;

        public static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k?.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return true; }
        }

        /// <summary>La tavolozza di accento di Windows: 8 colori RGBA da 4 byte; [1] = Light2 (tema scuro), [4] = Dark1 (tema chiaro).</summary>
        static Color SystemAccent(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var pal = k?.GetValue("AccentPalette") as byte[];
                    if (pal != null && pal.Length >= 32)
                    {
                        int i = dark ? 4 : 16;
                        return Color.FromRgb(pal[i], pal[i + 1], pal[i + 2]);
                    }
                }
            }
            catch { }
            return dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
        }

        /// <summary>Colore di accento scelto dall'utente («#RRGGBB»); vuoto o non valido = quello di Windows.</summary>
        public static string AccentOverride { get; set; } = "";

        public static void Apply(string setting)
        {
            bool dark = setting == "dark" ? true : setting == "light" ? false : SystemDark();
            Color accent = SystemAccent(dark);
            if (!string.IsNullOrWhiteSpace(AccentOverride))
            {
                try { accent = ColorFromHex(AccentOverride.Trim()); } catch { }
            }
            string key = dark + "|" + accent;
            if (key == _lastKey) return;
            _lastKey = key;
            IsDark = dark;
            Accent = accent;

            var t = new Dictionary<string, string>();
            if (dark)
            {
                t["WindowBase"] = "#202020"; t["BarFill"] = "#F2202020"; t["LayerFill"] = "#992E2E2E"; t["LogFill"] = "#26000000";
                t["TextPrimary"] = "#FFFFFF"; t["TextSecondary"] = "#C5FFFFFF"; t["TextTertiary"] = "#87FFFFFF"; t["TextDisabled"] = "#5DFFFFFF"; t["TextOnAccent"] = "#000000";
                t["CardFill"] = "#0DFFFFFF"; t["CardFillHover"] = "#14FFFFFF"; t["CardStroke"] = "#14FFFFFF";
                t["ControlFill"] = "#0FFFFFFF"; t["ControlFillHover"] = "#15FFFFFF"; t["ControlFillPressed"] = "#08FFFFFF"; t["ControlFillDisabled"] = "#0BFFFFFF"; t["ControlStroke"] = "#18FFFFFF";
                t["SubtleHover"] = "#0FFFFFFF"; t["SubtlePressed"] = "#0AFFFFFF"; t["SubtleSelected"] = "#17FFFFFF";
                t["AccentDisabled"] = "#28FFFFFF";
                t["ToggleOffStroke"] = "#9BFFFFFF"; t["ToggleOffKnob"] = "#C5FFFFFF"; t["ToggleOnKnob"] = "#000000";
                t["ProgressTrack"] = "#2EFFFFFF"; t["ScrollThumb"] = "#7DFFFFFF"; t["ScrollThumbHover"] = "#C5FFFFFF";
                t["DialogFill"] = "#2C2C2C"; t["DialogFooterFill"] = "#212121"; t["Scrim"] = "#66000000"; t["Divider"] = "#14FFFFFF";
                t["SuccessText"] = "#6CCB5F"; t["SuccessFill"] = "#393D1B"; t["WarnText"] = "#FCE100"; t["WarnFill"] = "#433519";
                t["ErrorText"] = "#FF99A4"; t["ErrorFill"] = "#442726"; t["InfoText"] = "#99DDFF"; t["InfoFill"] = "#26323B";
                t["TextBoxFill"] = "#0FFFFFFF"; t["TextBoxFillFocused"] = "#1E1E1E"; t["TextBoxStroke"] = "#8AFFFFFF";
            }
            else
            {
                t["WindowBase"] = "#F3F3F3"; t["BarFill"] = "#F5F3F3F3"; t["LayerFill"] = "#B3FFFFFF"; t["LogFill"] = "#80FFFFFF";
                t["TextPrimary"] = "#E4000000"; t["TextSecondary"] = "#9E000000"; t["TextTertiary"] = "#72000000"; t["TextDisabled"] = "#5C000000"; t["TextOnAccent"] = "#FFFFFF";
                t["CardFill"] = "#B3FFFFFF"; t["CardFillHover"] = "#80F9F9F9"; t["CardStroke"] = "#14000000";
                t["ControlFill"] = "#B3FFFFFF"; t["ControlFillHover"] = "#80F9F9F9"; t["ControlFillPressed"] = "#4DF9F9F9"; t["ControlFillDisabled"] = "#4DF9F9F9"; t["ControlStroke"] = "#1F000000";
                t["SubtleHover"] = "#09000000"; t["SubtlePressed"] = "#06000000"; t["SubtleSelected"] = "#0E000000";
                t["AccentDisabled"] = "#37000000";
                t["ToggleOffStroke"] = "#72000000"; t["ToggleOffKnob"] = "#99000000"; t["ToggleOnKnob"] = "#FFFFFF";
                t["ProgressTrack"] = "#1A000000"; t["ScrollThumb"] = "#72000000"; t["ScrollThumbHover"] = "#9E000000";
                t["DialogFill"] = "#FFFFFF"; t["DialogFooterFill"] = "#F3F3F3"; t["Scrim"] = "#4D000000"; t["Divider"] = "#0F000000";
                t["SuccessText"] = "#0F7B0F"; t["SuccessFill"] = "#DFF6DD"; t["WarnText"] = "#9D5D00"; t["WarnFill"] = "#FFF4CE";
                t["ErrorText"] = "#C42B1C"; t["ErrorFill"] = "#FDE7E9"; t["InfoText"] = "#005FB8"; t["InfoFill"] = "#E5F1FB";
                t["TextBoxFill"] = "#B3FFFFFF"; t["TextBoxFillFocused"] = "#FFFFFF"; t["TextBoxStroke"] = "#72000000";
            }

            var res = Application.Current.Resources;
            foreach (var kv in t) res[kv.Key] = Brush(ColorFromHex(kv.Value));
            res["Accent"] = Brush(accent);
            res["AccentHover"] = Brush(Color.FromArgb(230, accent.R, accent.G, accent.B));
            res["AccentPressed"] = Brush(Color.FromArgb(204, accent.R, accent.G, accent.B));
            res["AccentSoft"] = Brush(Color.FromArgb(dark ? (byte)0x33 : (byte)0x26, accent.R, accent.G, accent.B));
            res["AccentColor"] = accent;
            Changed?.Invoke();
        }

        /// <summary>Forza il prossimo Apply a riapplicare tutto (dopo un cambio di tema di sistema).</summary>
        public static void Invalidate() { _lastKey = ""; }

        static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
