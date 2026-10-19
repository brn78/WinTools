using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WinTools.Search;
using WinTools.UI;
using Forms = System.Windows.Forms;

namespace WinTools
{
    /// <summary>
    /// L'applicazione: impostazioni, tema, icona nell'area di notifica, tasto rapido globale, rilevamento del bordo
    /// dello schermo, indice e catalogo delle app, aggiornamenti. La barra (BarWindow) e le impostazioni sono finestre
    /// a parte; qui si coordinano.
    /// </summary>
    public partial class App : Application
    {
        public AppSettings Settings { get; private set; }
        public FileIndex Index { get; private set; }
        public AppCatalog Apps { get; private set; }
        public Updater Updates { get; private set; }
        public BarWindow Bar { get; private set; }

        public static new App Current => (App)Application.Current;

        Forms.NotifyIcon _tray;
        Window _helper;                 // finestra nascosta: handle per tasto rapido e menu dell'area di notifica
        HwndSource _helperSource;
        DispatcherTimer _edgeTimer;
        DispatcherTimer _updateTimer;
        DateTime _edgeSince = DateTime.MinValue;
        DateTime _awaySince = DateTime.MinValue;
        bool _hotkeyRegistered;
        bool _exiting;
        const int HotkeyId = 0xB00B;

        public event Action SettingsChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Settings = AppSettings.Load();
            if (!Settings.FirstRunDone)
            {
                Settings.Buttons = AppSettings.DefaultButtons();
                Settings.FirstRunDone = true;
                Settings.Save();
            }
            // Dal setup: la scelta «Avvia con Windows» fatta durante l'installazione
            foreach (var a in e.Args)
            {
                if (a.Equals("--set-autostart=1", StringComparison.OrdinalIgnoreCase)) { Settings.AutoStart = true; Settings.Save(); }
                else if (a.Equals("--set-autostart=0", StringComparison.OrdinalIgnoreCase)) { Settings.AutoStart = false; Settings.Save(); }
            }
            Theme.AccentOverride = Settings.AccentColor;
            Theme.Apply(Settings.Theme);
            SystemEvents.UserPreferenceChanged += (s, a) => { Theme.Invalidate(); Dispatcher.BeginInvoke(new Action(() => Theme.Apply(Settings.Theme))); };
            SystemEvents.DisplaySettingsChanged += (s, a) => Dispatcher.BeginInvoke(new Action(() => Bar?.Reposition()));

            // Finestra di servizio (mai visibile): riceve WM_HOTKEY e dà un proprietario ai menu dell'area di notifica
            _helper = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Opacity = 0 };
            _helperSource = HwndSource.FromHwnd(new WindowInteropHelper(_helper).EnsureHandle());
            _helperSource.AddHook(HelperHook);

            Index = new FileIndex();
            Apps = new AppCatalog();
            Updates = new Updater();
            Bar = new BarWindow();
            Bar.Prepare();

            SetupTray();
            RegisterHotkey();
            Tools.SetAutoStart(Settings.AutoStart);

            bool autostart = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            bool updated = e.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
            if (Settings.Index.Enabled) Index.Start(Settings.Index, autostart ? TimeSpan.FromMinutes(Settings.Index.StartupDelayMinutes) : TimeSpan.Zero);
            if (Settings.Index.Apps) Apps.Start();

            _edgeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            _edgeTimer.Tick += EdgeTick;
            _edgeTimer.Start();

            Updates.UpdateReady += info => Dispatcher.BeginInvoke(new Action(() => Bar.ShowUpdateReady(info)));
            Updates.CheckFinished += (ok, msg) => Dispatcher.BeginInvoke(new Action(() => Bar.ShowNotice(ok ? "success" : "warning", ok ? "WinTools è aggiornato" : "Controllo aggiornamenti non riuscito", msg)));
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _updateTimer.Tick += (s, a) => { if (Settings.AutoUpdate) Task.Run(() => Updates.CheckAsync(false)); };
            _updateTimer.Start();
            if (Settings.AutoUpdate) Task.Delay(8000).ContinueWith(_ => Updates.CheckAsync(false));

            if (Settings.Pinned) ShowBar(false);
            else if (!autostart) ShowBar(true);
            if (updated) Bar.ShowNotice("success", $"WinTools aggiornato alla versione {Program.Version}", "La versione precedente è stata eliminata.");
        }

        // ------------------------------------------------------------------ impostazioni
        public void SaveSettings()
        {
            Settings.Save();
            try { SettingsChanged?.Invoke(); } catch (Exception ex) { Program.LogError("SettingsChanged", ex); }
        }

        /// <summary>Dopo un cambiamento nelle impostazioni: tema, tasto rapido, avvio automatico, barra.</summary>
        public void ApplySettings(bool indexChanged, bool appsChanged)
        {
            Theme.Invalidate();
            Theme.AccentOverride = Settings.AccentColor;
            Theme.Apply(Settings.Theme);
            RegisterHotkey();
            Tools.SetAutoStart(Settings.AutoStart);
            if (indexChanged) Index.Reconfigure(Settings.Index);
            if (appsChanged) { if (Settings.Index.Apps) { Apps.Start(); } }
            Bar.ApplySettings();
            UpdateTrayMenu();
            SaveSettings();
        }

        // ------------------------------------------------------------------ barra
        public void ShowBar(bool activate)
        {
            if (_exiting) return;
            Bar.ShowBar(activate, ScreenForBar());
        }

        public void HideBar() { Bar.HideBar(); }

        public void ToggleBar()
        {
            if (Bar.IsBarVisible && Bar.IsActive) HideBar(); else ShowBar(true);
        }

        public void SetPinned(bool on)
        {
            Settings.Pinned = on;
            SaveSettings();
            Bar.ApplySettings();
            UpdateTrayMenu();
            if (on) ShowBar(false);
        }

        Forms.Screen ScreenForBar()
        {
            if (Settings.Monitor == "primary") return Forms.Screen.PrimaryScreen;
            Native.POINT p;
            if (Native.GetCursorPos(out p)) return Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
            return Forms.Screen.PrimaryScreen;
        }

        /// <summary>Ogni 50 ms: il mouse sul bordo fa comparire la barra; lontano dalla barra la fa sparire.</summary>
        void EdgeTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            if (Bar.IsBarVisible)
            {
                // Bloccata, impegnata (si scrive, c'è un menu) o con il fuoco della tastiera: resta; si ritira con Esc o quando il fuoco va altrove
                if (Settings.Pinned || Bar.KeepOpen || Bar.IsActive) { _awaySince = DateTime.MinValue; return; }
                if (Bar.ContainsScreenPoint(p.X, p.Y, 8)) { _awaySince = DateTime.MinValue; return; }
                if (_awaySince == DateTime.MinValue) _awaySince = DateTime.Now;
                else if ((DateTime.Now - _awaySince).TotalMilliseconds >= Settings.HideDelayMs) { _awaySince = DateTime.MinValue; Bar.HideBar(); }
                return;
            }

            if (!Settings.EdgeTrigger) return;
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
            if (Settings.Monitor == "primary" && !screen.Primary) { _edgeSince = DateTime.MinValue; return; }
            var b = screen.Bounds;
            bool onEdge = Settings.Edge == "bottom" ? p.Y >= b.Bottom - 1 : p.Y <= b.Top;
            if (!onEdge) { _edgeSince = DateTime.MinValue; return; }
            if (_edgeSince == DateTime.MinValue) _edgeSince = DateTime.Now;
            else if ((DateTime.Now - _edgeSince).TotalMilliseconds >= Settings.EdgeDelayMs)
            {
                _edgeSince = DateTime.MinValue;
                // Non durante un trascinamento o con un tasto premuto (si sta lavorando sul bordo)
                if (Forms.Control.MouseButtons != Forms.MouseButtons.None) return;
                Bar.ShowFromEdge(screen);
            }
        }

        // ------------------------------------------------------------------ tasto rapido
        DispatcherTimer _hotkeyRetry;
        string _hotkeyWarned;

        /// <summary>
        /// Registra il tasto rapido. Se un altro programma lo tiene (Windows risponde «già registrato»), lo si dice una
        /// volta sola e si riprova ogni minuto: quando l'altro programma lo lascia, il tasto torna a funzionare da solo.
        /// </summary>
        void RegisterHotkey()
        {
            IntPtr h = _helperSource.Handle;
            if (_hotkeyRegistered) { Native.UnregisterHotKey(h, HotkeyId); _hotkeyRegistered = false; }
            uint mods, vk; System.Windows.Input.Key key;
            if (!Tools.ParseHotkey(Settings.Hotkey, out mods, out vk, out key)) { _hotkeyRetry?.Stop(); return; }
            _hotkeyRegistered = Native.RegisterHotKey(h, HotkeyId, mods | Native.MOD_NOREPEAT, vk);
            if (!_hotkeyRegistered) _hotkeyRegistered = Native.RegisterHotKey(h, HotkeyId, mods, vk);
            if (_hotkeyRegistered) { _hotkeyRetry?.Stop(); _hotkeyWarned = null; return; }

            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (_hotkeyWarned != Settings.Hotkey)
            {
                _hotkeyWarned = Settings.Hotkey;
                Program.LogError("Tasto rapido " + Settings.Hotkey, new System.ComponentModel.Win32Exception(err));
                Dispatcher.BeginInvoke(new Action(() => Bar.ShowNotice("warning", "Tasto rapido non disponibile",
                    $"«{Settings.Hotkey}» è già usato da un altro programma: scegline un altro nelle impostazioni. Intanto la barra compare dal bordo dello schermo e dall'icona nell'area di notifica.")));
            }
            if (_hotkeyRetry == null)
            {
                _hotkeyRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                _hotkeyRetry.Tick += (s, a) => { if (!_hotkeyRegistered) RegisterHotkey(); else _hotkeyRetry.Stop(); };
            }
            _hotkeyRetry.Start();
        }

        IntPtr HelperHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleBar();
                handled = true;
            }
            else if (msg == Native.WM_DWMCOLORIZATIONCOLORCHANGED || msg == Native.WM_SETTINGCHANGE)
            {
                Theme.Invalidate();
                Dispatcher.BeginInvoke(new Action(() => Theme.Apply(Settings.Theme)));
            }
            return IntPtr.Zero;
        }

        // ------------------------------------------------------------------ area di notifica
        void SetupTray()
        {
            _tray = new Forms.NotifyIcon { Text = $"{Program.Title} {Program.Version}", Visible = true, Icon = LoadIcon() };
            _tray.MouseUp += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) ToggleBar();
                else if (e.Button == Forms.MouseButtons.Right) ShowTrayMenu();
            };
            _tray.DoubleClick += (s, e) => OpenSettings();
        }

        ContextMenu _trayMenu;

        void UpdateTrayMenu() { _trayMenu = null; }

        void ShowTrayMenu()
        {
            var m = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, StaysOpen = false };
            m.Items.Add(new MenuItem { Header = $"{Program.Title} {Program.Version}", IsEnabled = false });
            m.Items.Add(new Separator());
            m.Items.Add(Item("Mostra la barra", "", () => ShowBar(true)));
            var pin = Item(Settings.Pinned ? "Sblocca la barra (si nasconde da sola)" : "Blocca la barra (sempre visibile)", "", () => SetPinned(!Settings.Pinned));
            m.Items.Add(pin);
            m.Items.Add(Item("Impostazioni…", "", OpenSettings));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Aggiorna l'indice dei file", "", () => { if (Settings.Index.Enabled) Index.Rescan(); }));
            var auto = Item(Settings.AutoStart ? "Avvio con Windows: attivo" : "Avvio con Windows: disattivato", Settings.AutoStart ? "" : "", () => { Settings.AutoStart = !Settings.AutoStart; Tools.SetAutoStart(Settings.AutoStart); SaveSettings(); });
            m.Items.Add(auto);
            m.Items.Add(Item("Controlla aggiornamenti", "", () => Task.Run(() => Updates.CheckAsync(true))));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Esci", "", Exit));
            // Il menu si chiude cliccando altrove solo se una nostra finestra è in primo piano
            Native.SetForegroundWindow(_helperSource.Handle);
            _trayMenu = m;
            m.IsOpen = true;
        }

        static MenuItem Item(string text, string glyph, Action action)
        {
            var mi = new MenuItem { Header = text };
            mi.Icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 };
            mi.Click += (s, e) => action();
            return mi;
        }

        public static System.Drawing.Icon LoadIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinTools.ico"))
                    if (s != null) return new System.Drawing.Icon(s);
            }
            catch { }
            try { return System.Drawing.Icon.ExtractAssociatedIcon(Program.ExePath); } catch { return SystemIcons.Application; }
        }

        /// <summary>L'icona dell'app per le finestre WPF.</summary>
        public static System.Windows.Media.ImageSource WindowIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinTools.ico"))
                {
                    if (s == null) return null;
                    var d = System.Windows.Media.Imaging.BitmapDecoder.Create(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    return d.Frames.OrderByDescending(f => f.PixelWidth).First();
                }
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ impostazioni e uscita
        SettingsWindow _settingsWindow;

        public void OpenSettings()
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded) { _settingsWindow.Activate(); return; }
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        public void OpenSettingsPage(string page)
        {
            OpenSettings();
            _settingsWindow?.GoTo(page);
        }

        public new void Exit()
        {
            if (_exiting) return;
            _exiting = true;
            try { _edgeTimer?.Stop(); _updateTimer?.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            try { if (_hotkeyRegistered) Native.UnregisterHotKey(_helperSource.Handle, HotkeyId); } catch { }
            Settings.Save();
            try { Index?.Dispose(); Apps?.Dispose(); } catch { }
            // Un aggiornamento scaricato si installa adesso, alla chiusura
            try { if (!string.IsNullOrEmpty(Updates?.ReadyPath)) Updates.Install(false); } catch { }
            Shutdown();
        }

        /// <summary>«Riavvia e aggiorna»: installa la versione scaricata e la avvia.</summary>
        public void RestartAndUpdate()
        {
            if (string.IsNullOrEmpty(Updates?.ReadyPath)) return;
            _exiting = true;
            try { _edgeTimer?.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            try { if (_hotkeyRegistered) Native.UnregisterHotKey(_helperSource.Handle, HotkeyId); } catch { }
            Settings.Save();
            try { Index?.Dispose(); Apps?.Dispose(); } catch { }
            if (!Updates.Install(true))
            {
                _exiting = false;
                Bar.ShowNotice("error", "Aggiornamento non riuscito", "La cartella dell'exe potrebbe essere in sola lettura. Scarica la nuova versione dalla pagina della release.");
                return;
            }
            Shutdown();
        }
    }
}
