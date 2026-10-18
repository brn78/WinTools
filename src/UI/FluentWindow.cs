using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinTools.UI
{
    /// <summary>
    /// Aspetto Windows 11 per le finestre normali (impostazioni, editor): barra del titolo scura con il tema scuro,
    /// Mica dove c'è (22H2+) altrimenti tinta unita, angoli arrotondati, icona dell'app.
    /// </summary>
    public static class FluentWindow
    {
        public static void Setup(Window w)
        {
            w.Icon = App.WindowIcon();
            w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            w.FontSize = 14;
            w.UseLayoutRounding = true;
            w.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
            w.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
            w.SetResourceReference(Window.BackgroundProperty, "WindowBase");
            bool mica = false;
            Action apply = () =>
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                Native.SetDarkTitleBar(hwnd, Theme.IsDark);
                if (mica) w.Background = Brushes.Transparent; else w.SetResourceReference(Window.BackgroundProperty, "WindowBase");
            };
            w.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                mica = Native.EnableMica(hwnd);
                if (mica)
                {
                    var src = HwndSource.FromHwnd(hwnd);
                    if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                }
                apply();
            };
            Action onTheme = () => w.Dispatcher.BeginInvoke(apply);
            Theme.Changed += onTheme;
            w.Closed += (s, e) => Theme.Changed -= onTheme;
        }
    }
}
