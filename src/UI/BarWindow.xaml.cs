using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinTools.Search;
using Forms = System.Windows.Forms;

namespace WinTools.UI
{
    /// <summary>Un tasto della barra (o di un contenitore) per l'interfaccia: etichetta, icona o glifo.</summary>
    public sealed class ButtonVm : INotifyPropertyChanged
    {
        public BarButton Button { get; }
        ImageSource _icon;
        public ButtonVm(BarButton b, bool showLabels)
        {
            Button = b;
            ShowLabels = showLabels;
            if (ButtonVm.IsGlyph(b.Icon)) Glyph = ((char)Convert.ToInt32(b.Icon, 16)).ToString();
            else if (b.Group && string.IsNullOrEmpty(b.Icon)) Glyph = "";
            else
            {
                string key = string.IsNullOrEmpty(b.Icon) ? Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(b.Target)) : b.Icon;
                if (key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) Glyph = "";
                else
                {
                    _icon = IconCache.Get(key, 24, img => { if (img != null) { _icon = img; Raise("Icon"); Raise("ImageVisibility"); Raise("GlyphVisibility"); } });
                    if (_icon == null) Glyph = key.IndexOf(':') > 0 && !Path.IsPathRooted(key) ? "" : "";
                }
            }
        }
        public static bool IsGlyph(string s) => !string.IsNullOrEmpty(s) && s.Length == 4 && s.All(Uri.IsHexDigit);
        public bool ShowLabels { get; }
        public bool IsGroup => Button.Group;
        public string Label => Button.Label;
        public string Tooltip
        {
            get
            {
                if (IsGroup) return $"{Button.Label}\n{Button.Children.Count} {(Button.Children.Count == 1 ? "tasto" : "tasti")} · clic per aprire, trascina qui un tasto per aggiungerlo";
                if (string.IsNullOrEmpty(Button.Label)) return Button.Target;
                return (Button.IconOnly || !ShowLabels ? Button.Label + "\n" : "") + Button.Target + (Button.Admin ? "  (amministratore)" : "");
            }
        }
        public string Glyph { get; private set; }
        public ImageSource Icon => _icon;
        public Visibility ImageVisibility => _icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LabelVisibility => ShowLabels && !Button.IconOnly && !string.IsNullOrEmpty(Button.Label) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GroupBadgeVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;
        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Un risultato per l'elenco.</summary>
    public sealed class ResultVm : INotifyPropertyChanged
    {
        public SearchHit Hit { get; }
        ImageSource _icon;
        public ResultVm(SearchHit h)
        {
            Hit = h;
            if (h.IconKey != null && h.IconKey.StartsWith("glyph:")) Glyph = ((char)Convert.ToInt32(h.IconKey.Substring(6), 16)).ToString();
            else if (!string.IsNullOrEmpty(h.IconKey))
            {
                _icon = IconCache.Get(h.IconKey, 26, img => { if (img != null) { _icon = img; Raise("Icon"); Raise("ImageVisibility"); Raise("GlyphVisibility"); } });
                if (_icon == null) Glyph = h.Kind == HitKind.Folder ? "" : h.Kind == HitKind.App ? "" : "";
            }
            var meta = new List<string>();
            if (h.Kind == HitKind.File) { meta.Add(h.KindLabel); if (h.Size > 0) meta.Add(Tools.FormatSize(h.Size)); }
            else if (h.Kind == HitKind.Folder) meta.Add("Cartella");
            if (h.Modified.Year > 1980) meta.Add(Tools.FormatDate(h.Modified));
            Meta = string.Join(" · ", meta);
        }
        public string Title => Hit.Title;
        public string Subtitle => Hit.Subtitle;
        public string Meta { get; }
        public string Glyph { get; }
        public ImageSource Icon => _icon;
        public Visibility ImageVisibility => _icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;
        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// La barra a comparsa: ricerca, tasti personalizzabili (trascinabili, anche dentro contenitori), risultati, avvisi.
    /// Compare dal bordo dello schermo con una breve animazione e si nasconde da sola, a meno che non sia bloccata.
    /// </summary>
    public partial class BarWindow : Window
    {
        readonly ObservableCollection<ButtonVm> _buttons = new ObservableCollection<ButtonVm>();
        readonly ObservableCollection<ButtonVm> _groupItems = new ObservableCollection<ButtonVm>();
        readonly ObservableCollection<ResultVm> _results = new ObservableCollection<ResultVm>();
        readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        readonly DispatcherTimer _noticeTimer = new DispatcherTimer();
        Forms.Screen _screen;
        IntPtr _hwnd;
        bool _hiding;
        bool _dialogOpen;
        bool _noActivate;
        bool _showingRecents;
        int _searchSerial;
        Action _noticeAction;
        Point _dragStart;
        ButtonVm _dragging;
        BarButton _openGroup;

        AppSettings S => App.Current.Settings;

        /// <summary>La barra è visibile (anche durante l'animazione di uscita no).</summary>
        public bool IsBarVisible => IsVisible && !_hiding;
        /// <summary>Non nascondersi quando il mouse si allontana: aperta con il tasto rapido, si sta scrivendo, c'è una finestra o un menu aperto, un avviso.</summary>
        public bool KeepOpen { get; private set; }

        public BarWindow()
        {
            InitializeComponent();
            ButtonsList.ItemsSource = _buttons;
            GroupList.ItemsSource = _groupItems;
            ResultsList.ItemsSource = _results;
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); RunSearch(); };
            _noticeTimer.Tick += (s, e) => { _noticeTimer.Stop(); HideNotice(); };
            Deactivated += (s, e) =>
            {
                // Il fuoco va altrove: la barra si ritira, a meno che non sia bloccata o ci sia un dialogo nostro aperto
                if (_dialogOpen || S.Pinned) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!IsActive && !_dialogOpen && !S.Pinned && IsBarVisible && !GroupPopup.IsOpen) HideBar(); }));
            };
            DpiChanged += (s, e) => Reposition();
            // Comparsa dal bordo senza rubare il fuoco: al primo clic sulla barra la finestra si attiva davvero,
            // altrimenti si potrebbe scrivere nella casella ma i tasti finirebbero nell'app di prima
            PreviewMouseDown += (s, e) => EnsureActive(e.OriginalSource as DependencyObject);
            SizeChanged += (s, e) => { if (S.Edge == "bottom") Reposition(); };
            App.Current.Index.StatusChanged += () => Dispatcher.BeginInvoke(new Action(UpdateStatus));
        }

        /// <summary>Crea l'handle senza mostrare la finestra: stile «tool window» e posizione.</summary>
        public void Prepare()
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            Native.MakeToolWindow(_hwnd);
            ApplySettings();
            // Prima comparsa senza attese: modelli e albero visuale si costruiscono subito, senza mostrare la finestra
            // (Show e Hide in WPF sono differiti: chiamarli uno dopo l'altro lascerebbe la finestra visibile)
            try
            {
                Root.Measure(new Size(1200, 600));
                Root.Arrange(new Rect(0, 0, 1200, 600));
                Root.UpdateLayout();
            }
            catch { }
        }

        // ------------------------------------------------------------------ impostazioni → aspetto
        public void ApplySettings()
        {
            _buttons.Clear();
            foreach (var b in S.Buttons) _buttons.Add(new ButtonVm(b, S.ShowLabels));
            if (_openGroup != null) RefreshGroupItems();
            BarRow.Height = S.ShowLabels && S.Buttons.Any(b => !b.IconOnly && !string.IsNullOrEmpty(b.Label)) ? 64 : 52;
            PinGlyph.Text = S.Pinned ? "" : "";
            PinButton.ToolTip = S.Pinned ? "Sblocca la barra (si nasconde da sola)" : "Blocca la barra (sempre visibile)";
            Panel.HorizontalAlignment = S.FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            bool bottom = S.Edge == "bottom";
            Panel.CornerRadius = bottom ? new CornerRadius(10, 10, 0, 0) : new CornerRadius(0, 0, 10, 10);
            Panel.BorderThickness = bottom ? new Thickness(1, 1, 1, 0) : new Thickness(1, 0, 1, 1);
            Panel.Margin = bottom ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 14);
            ((DropShadowEffect)Panel.Effect).Direction = bottom ? 90 : 270;
            DockPanel.SetDock(BarRow, bottom ? Dock.Bottom : Dock.Top);
            DockPanel.SetDock(Notice, bottom ? Dock.Bottom : Dock.Top);
            GroupPopup.Placement = bottom ? PlacementMode.Top : PlacementMode.Bottom;
            if (IsVisible) Reposition();
        }

        /// <summary>Salva e ridisegna i tasti dopo una modifica.</summary>
        void CommitButtons()
        {
            App.Current.SaveSettings();
            ApplySettings();
        }

        // ------------------------------------------------------------------ mostra e nascondi
        public void ShowBar(bool activate, Forms.Screen screen)
        {
            _screen = screen ?? Forms.Screen.PrimaryScreen;
            bool wasVisible = IsVisible && !_hiding;
            _hiding = false;
            Panel.BeginAnimation(OpacityProperty, null);
            Slide.BeginAnimation(TranslateTransform.YProperty, null);
            if (!wasVisible)
            {
                _noActivate = !activate;
                Native.SetNoActivate(_hwnd, _noActivate);
                Reposition();
                Show();
                Reposition();
                double from = (S.Edge == "bottom" ? 1 : -1) * Math.Max(40, Panel.ActualHeight);
                Slide.Y = from;
                Panel.Opacity = 0;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
            else
            {
                Slide.Y = 0;
                Panel.Opacity = 1;
            }
            if (activate)
            {
                KeepOpen = true;
                _noActivate = false;
                Native.SetNoActivate(_hwnd, false);
                Activate();
                Native.ForceForeground(_hwnd);
                SearchBox.Focus();
                SearchBox.SelectAll();
                if (SearchBox.Text.Length == 0) ShowRecents();
            }
            else KeepOpen = Notice.Visibility == Visibility.Visible;
            UpdateStatus();
        }

        /// <summary>Toglie il «non attivabile» e porta la finestra in primo piano; con un clic nella ricerca, fuoco alla casella.</summary>
        void EnsureActive(DependencyObject source)
        {
            bool inSearch = false;
            for (var d = source; d != null; d = VisualTreeHelper.GetParent(d)) { if (d == SearchHost) { inSearch = true; break; } }
            if (_noActivate || !IsActive)
            {
                _noActivate = false;
                Native.SetNoActivate(_hwnd, false);
                Activate();
                Native.ForceForeground(_hwnd);
                KeepOpen = true;
            }
            if (inSearch) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { SearchBox.Focus(); Keyboard.Focus(SearchBox); }));
        }

        public void HideBar()
        {
            if (!IsVisible || _hiding) return;
            GroupPopup.IsOpen = false;
            _hiding = true;
            KeepOpen = false;
            double to = (S.Edge == "bottom" ? 1 : -1) * Math.Max(40, Panel.ActualHeight);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            slide.Completed += (s, e) =>
            {
                if (!_hiding) return;
                _hiding = false;
                Hide();
                Slide.BeginAnimation(TranslateTransform.YProperty, null);
                Slide.Y = 0;
                Panel.Opacity = 1;
                SearchBox.Text = "";
                _results.Clear();
                Results.Visibility = Visibility.Collapsed;
            };
            Slide.BeginAnimation(TranslateTransform.YProperty, slide);
            Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)));
        }

        /// <summary>Posizione e larghezza sullo schermo scelto, in pixel reali (SetWindowPos), così va bene anche con più schermi a DPI diversi.</summary>
        public void Reposition()
        {
            if (_screen == null || _hwnd == IntPtr.Zero) return;
            var full = _screen.Bounds;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Width = full.Width / scale;
            int h = (int)Math.Round(ActualHeight * scale);
            int y = S.Edge == "bottom" ? full.Bottom - h : full.Top;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, full.Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>Vero se il punto (pixel dello schermo) sta sul pannello della barra (o sul menu di un contenitore), con un margine.</summary>
        public bool ContainsScreenPoint(int x, int y, int margin)
        {
            try
            {
                if (!IsVisible) return false;
                var tl = Panel.PointToScreen(new Point(0, 0));
                var br = Panel.PointToScreen(new Point(Panel.ActualWidth, Panel.ActualHeight));
                if (x >= tl.X - margin && x <= br.X + margin && y >= tl.Y - margin && y <= br.Y + margin) return true;
                if (GroupPopup.IsOpen && GroupPopup.Child is FrameworkElement c)
                {
                    var a = c.PointToScreen(new Point(0, 0));
                    var b = c.PointToScreen(new Point(c.ActualWidth, c.ActualHeight));
                    return x >= a.X - margin && x <= b.X + margin && y >= a.Y - margin && y <= b.Y + margin;
                }
                return false;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ avvisi
        public void ShowNotice(string kind, string title, string message, string actionText = null, Action action = null, bool sticky = false)
        {
            string fill = "InfoFill", text = "InfoText", glyph = "";
            switch (kind)
            {
                case "success": fill = "SuccessFill"; text = "SuccessText"; glyph = ""; break;
                case "warning": fill = "WarnFill"; text = "WarnText"; glyph = ""; break;
                case "error": fill = "ErrorFill"; text = "ErrorText"; glyph = ""; break;
            }
            Notice.SetResourceReference(Border.BackgroundProperty, fill);
            NoticeIcon.SetResourceReference(TextBlock.ForegroundProperty, text);
            NoticeIcon.Text = glyph;
            NoticeTitle.Text = title;
            NoticeText.Text = " " + message;
            _noticeAction = action;
            NoticeAction.Content = actionText;
            NoticeAction.Visibility = string.IsNullOrEmpty(actionText) ? Visibility.Collapsed : Visibility.Visible;
            Notice.Visibility = Visibility.Visible;
            _noticeTimer.Stop();
            if (!sticky) { _noticeTimer.Interval = TimeSpan.FromSeconds(10); _noticeTimer.Start(); }
            if (!IsBarVisible) { ShowBar(false, _screen ?? Forms.Screen.PrimaryScreen); KeepOpen = true; if (!sticky) { _noticeTimer.Interval = TimeSpan.FromSeconds(8); } }
        }

        public void ShowUpdateReady(Updater.Info info)
        {
            ShowNotice("success", $"WinTools {info.Version} è pronto.", "Verrà installato alla chiusura, oppure subito con un riavvio.", "Riavvia e aggiorna", () => App.Current.RestartAndUpdate(), sticky: true);
        }

        void HideNotice()
        {
            Notice.Visibility = Visibility.Collapsed;
            if (KeepOpen && !IsActive && !S.Pinned) KeepOpen = false;
        }

        void NoticeAction_Click(object sender, RoutedEventArgs e) { var a = _noticeAction; HideNotice(); a?.Invoke(); }
        void NoticeClose_Click(object sender, RoutedEventArgs e) { _noticeTimer.Stop(); HideNotice(); }

        // ------------------------------------------------------------------ pulsanti di servizio
        void Pin_Click(object sender, RoutedEventArgs e) { App.Current.SetPinned(!S.Pinned); }
        void Settings_Click(object sender, RoutedEventArgs e) { App.Current.OpenSettings(); if (!S.Pinned) HideBar(); }
        void Close_Click(object sender, RoutedEventArgs e) { HideBar(); }

        void ButtonsScroll_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ButtonsScroll.ScrollToHorizontalOffset(ButtonsScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ tasti della barra
        void BarButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ButtonVm;
            if (vm == null || _dragging != null) return;
            if (vm.IsGroup) { OpenGroup(vm.Button, btn); return; }
            Tools.Launch(vm.Button);
            if (!S.Pinned) HideBar();
        }

        void BarButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            _dragging = null;
        }

        void BarButton_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _dragging = null; return; }
            var btn = sender as Button;
            var vm = btn?.Tag as ButtonVm;
            if (vm == null) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < 8 && Math.Abs(p.Y - _dragStart.Y) < 8) return;
            _dragging = vm;
            KeepOpen = true;
            try
            {
                var data = new DataObject("WinTools.Button", vm.Button.Id);
                DragDrop.DoDragDrop(btn, data, DragDropEffects.Move);
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _dragging = null; KeepOpen = IsActive || GroupPopup.IsOpen; }));
            }
        }

        void Buttons_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("WinTools.Button")) e.Effects = DragDropEffects.Move;
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        void Buttons_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var targetVm = (sender as Button)?.Tag as ButtonVm;
            if (e.Data.GetDataPresent("WinTools.Button"))
            {
                string id = e.Data.GetData("WinTools.Button") as string;
                // Rilasciato su un contenitore: ci finisce dentro
                if (targetVm != null && targetVm.IsGroup && targetVm.Button.Id != id)
                {
                    List<BarButton> owner;
                    var moving = S.FindButton(id, out owner);
                    if (moving == null || moving.Group) return;
                    owner.Remove(moving);
                    targetVm.Button.Children.Add(moving);
                    CommitButtons();
                    return;
                }
                int target = DropIndex(e.GetPosition(ButtonsList));
                int from = S.Buttons.FindIndex(b => b.Id == id);
                var item = S.DetachButton(id);
                if (item == null) return;
                if (from >= 0 && target > from) target--;
                target = Math.Max(0, Math.Min(S.Buttons.Count, target));
                S.Buttons.Insert(target, item);
                CommitButtons();
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                var list = targetVm != null && targetVm.IsGroup ? targetVm.Button.Children : S.Buttons;
                int at = targetVm != null && targetVm.IsGroup ? list.Count : Math.Max(0, Math.Min(S.Buttons.Count, DropIndex(e.GetPosition(ButtonsList))));
                foreach (var f in files)
                {
                    var b = new BarButton { Label = Path.GetFileNameWithoutExtension(f), Target = f };
                    if (Directory.Exists(f)) b.Label = Path.GetFileName(f.TrimEnd('\\'));
                    list.Insert(at++, b);
                }
                CommitButtons();
            }
        }

        /// <summary>In quale posizione cade un tasto rilasciato a quella X.</summary>
        int DropIndex(Point p)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var el = ButtonsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (el == null) continue;
                var pos = el.TranslatePoint(new Point(0, 0), ButtonsList);
                if (p.X < pos.X + el.ActualWidth / 2) return i;
            }
            return _buttons.Count;
        }

        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); Buttons_DragOver(this, e); }
        protected override void OnDrop(DragEventArgs e) { base.OnDrop(e); if (!e.Handled) Buttons_Drop(this, e); }

        // ------------------------------------------------------------------ menu contestuali dei tasti
        void BarButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ButtonVm;
            if (vm == null || btn.ContextMenu == null) return;
            KeepOpen = true;
            var m = btn.ContextMenu;
            m.Items.Clear();
            var b = vm.Button;
            if (b.Group)
            {
                m.Items.Add(Menu("Apri", () => OpenGroup(b, btn)));
                m.Items.Add(new Separator());
                m.Items.Add(Menu("Modifica…", () => EditButton(b)));
                m.Items.Add(Menu(b.Children.Count > 0 ? "Rimuovi il contenitore (i tasti tornano sulla barra)" : "Rimuovi il contenitore", () => RemoveGroup(b)));
            }
            else
            {
                m.Items.Add(Menu("Apri", () => Tools.Launch(b)));
                m.Items.Add(Menu("Apri come amministratore", () => Tools.Open(b.Target, b.Arguments, b.WorkingDir, true)));
                m.Items.Add(Menu("Apri il percorso del file", () => Reveal(b)));
                m.Items.Add(new Separator());
                m.Items.Add(MoveToMenu(b));
                m.Items.Add(Menu("Modifica…", () => EditButton(b)));
                m.Items.Add(Menu("Rimuovi", () => { S.DetachButton(b.Id); CommitButtons(); }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Aggiungi un tasto…", () => AddButton(false)));
            m.Items.Add(Menu("Aggiungi un contenitore…", () => AddButton(true)));
        }

        void AddButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.ContextMenu == null) return;
            KeepOpen = true;
            btn.ContextMenu.Items.Clear();
            btn.ContextMenu.Items.Add(Menu("Aggiungi un tasto…", () => AddButton(false)));
            btn.ContextMenu.Items.Add(Menu("Aggiungi un contenitore…", () => AddButton(true)));
        }

        /// <summary>«Sposta in»: i contenitori esistenti, oppure uno nuovo creato al volo con dentro questo tasto.</summary>
        MenuItem MoveToMenu(BarButton b)
        {
            var mi = new MenuItem { Header = "Sposta in" };
            foreach (var g in S.Buttons.Where(x => x.Group))
            {
                var target = g;
                mi.Items.Add(Menu(string.IsNullOrEmpty(target.Label) ? "(contenitore)" : target.Label, () =>
                {
                    S.DetachButton(b.Id);
                    target.Children.Add(b);
                    CommitButtons();
                }));
            }
            List<BarButton> owner;
            S.FindButton(b.Id, out owner);
            if (owner != null && owner != S.Buttons)
            {
                if (mi.Items.Count > 0) mi.Items.Add(new Separator());
                mi.Items.Add(Menu("Sulla barra", () => { S.DetachButton(b.Id); S.Buttons.Add(b); CommitButtons(); }));
            }
            if (mi.Items.Count > 0) mi.Items.Add(new Separator());
            mi.Items.Add(Menu("Nuovo contenitore…", () =>
            {
                var g = new BarButton { Group = true, Icon = "E8B7" };
                if (RunDialog(() => new ButtonEditorWindow(g)))
                {
                    S.DetachButton(b.Id);
                    g.Children.Add(b);
                    S.Buttons.Add(g);
                    CommitButtons();
                }
            }));
            return mi;
        }

        void Reveal(BarButton b)
        {
            string t = Environment.ExpandEnvironmentVariables(b.Target);
            if (!Path.IsPathRooted(t)) t = IconCache.FindOnPath(t) ?? t;
            Tools.RevealInExplorer(t);
        }

        void EditButton(BarButton b)
        {
            var copy = b.Clone();
            if (RunDialog(() => new ButtonEditorWindow(copy)))
            {
                List<BarButton> owner;
                var cur = S.FindButton(b.Id, out owner);
                if (cur != null)
                {
                    int at = owner.IndexOf(cur);
                    owner[at] = copy;
                    // Un contenitore diventato tasto normale: i tasti che aveva dentro tornano sulla barra, subito dopo
                    if (!copy.Group && copy.Children.Count > 0) { S.Buttons.InsertRange(Math.Min(S.Buttons.Count, at + 1), copy.Children); copy.Children = new List<BarButton>(); }
                }
                if (_openGroup != null && _openGroup.Id == copy.Id) { _openGroup = copy; if (!copy.Group) GroupPopup.IsOpen = false; }
                CommitButtons();
            }
        }

        void RemoveGroup(BarButton g)
        {
            int i = S.Buttons.IndexOf(g);
            if (i < 0) return;
            S.Buttons.RemoveAt(i);
            S.Buttons.InsertRange(i, g.Children);
            g.Children.Clear();
            if (_openGroup == g) GroupPopup.IsOpen = false;
            CommitButtons();
        }

        void AddButton_Click(object sender, RoutedEventArgs e) { AddButton(false); }

        void AddButton(bool group)
        {
            var b = new BarButton { Group = group, Icon = group ? "E8B7" : "" };
            if (RunDialog(() => new ButtonEditorWindow(b)))
            {
                S.Buttons.Add(b);
                CommitButtons();
            }
        }

        static MenuItem Menu(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (s, e) => a(); return mi; }

        /// <summary>Mostra un dialogo senza che la barra si ritiri nel frattempo.</summary>
        public bool RunDialog(Func<Window> create)
        {
            _dialogOpen = true;
            KeepOpen = true;
            GroupPopup.IsOpen = false;
            try
            {
                var w = create();
                bool? r = w.ShowDialog();
                return r == true;
            }
            finally
            {
                _dialogOpen = false;
                if (IsBarVisible) { Activate(); SearchBox.Focus(); }
            }
        }

        // ------------------------------------------------------------------ contenitori
        void OpenGroup(BarButton g, UIElement anchor)
        {
            if (GroupPopup.IsOpen && _openGroup == g) { GroupPopup.IsOpen = false; return; }
            _openGroup = g;
            RefreshGroupItems();
            GroupPopup.PlacementTarget = anchor;
            GroupPopup.IsOpen = true;
        }

        void RefreshGroupItems()
        {
            _groupItems.Clear();
            if (_openGroup == null) return;
            List<BarButton> owner;
            var g = S.FindButton(_openGroup.Id, out owner);
            if (g == null) { GroupPopup.IsOpen = false; return; }
            _openGroup = g;
            GroupTitle.Text = g.Label;
            foreach (var c in g.Children) _groupItems.Add(new ButtonVm(c, true));
            GroupEmpty.Visibility = g.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void GroupPopup_Opened(object sender, EventArgs e) { KeepOpen = true; }
        void GroupPopup_Closed(object sender, EventArgs e) { KeepOpen = IsActive && (SearchBox.IsKeyboardFocusWithin || SearchBox.Text.Length > 0); }

        void GroupItem_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as Button)?.Tag as ButtonVm;
            if (vm == null || _dragging != null) return;
            GroupPopup.IsOpen = false;
            Tools.Launch(vm.Button);
            if (!S.Pinned) HideBar();
        }

        void GroupItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ButtonVm;
            if (vm == null || btn.ContextMenu == null) return;
            var b = vm.Button;
            var m = btn.ContextMenu;
            m.Items.Clear();
            m.Items.Add(Menu("Apri", () => { GroupPopup.IsOpen = false; Tools.Launch(b); if (!S.Pinned) HideBar(); }));
            m.Items.Add(Menu("Apri come amministratore", () => Tools.Open(b.Target, b.Arguments, b.WorkingDir, true)));
            m.Items.Add(new Separator());
            m.Items.Add(MoveToMenu(b));
            m.Items.Add(Menu("Modifica…", () => EditButton(b)));
            m.Items.Add(Menu("Rimuovi", () => { S.DetachButton(b.Id); CommitButtons(); }));
        }

        void GroupPopup_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("WinTools.Button") || e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>Un tasto rilasciato nel menu aperto finisce in quel contenitore (in fondo).</summary>
        void GroupPopup_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_openGroup == null) return;
            if (e.Data.GetDataPresent("WinTools.Button"))
            {
                string id = e.Data.GetData("WinTools.Button") as string;
                List<BarButton> owner;
                var moving = S.FindButton(id, out owner);
                if (moving == null || moving.Group) return;
                owner.Remove(moving);
                _openGroup.Children.Add(moving);
                CommitButtons();
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                foreach (var f in (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? new string[0])
                    _openGroup.Children.Add(new BarButton { Label = Directory.Exists(f) ? Path.GetFileName(f.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(f), Target = f });
                CommitButtons();
            }
        }

        // ------------------------------------------------------------------ ricerca
        void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            KeepOpen = true;
            if (SearchBox.Text.Length == 0) ShowRecents();
        }

        void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = SearchBox.Text.Length == 0;
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearSearch.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            KeepOpen = true;
            _searchTimer.Stop();
            if (empty) { ShowRecents(); return; }
            _searchTimer.Start();
        }

        void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Text = ""; SearchBox.Focus(); }

        void ShowRecents()
        {
            var list = new List<SearchHit>();
            foreach (var p in S.Recent)
            {
                if (p.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) list.Add(new SearchHit { Kind = HitKind.App, Title = AppName(p), Path = p, Subtitle = "App · recente", IconKey = p });
                else if (Directory.Exists(p)) list.Add(new SearchHit { Kind = HitKind.Folder, Title = Path.GetFileName(p.TrimEnd('\\')), Path = p, Subtitle = Path.GetDirectoryName(p) ?? p, IconKey = p });
                else if (File.Exists(p)) { var fi = new FileInfo(p); list.Add(new SearchHit { Kind = HitKind.File, Title = fi.Name, Path = p, Subtitle = fi.DirectoryName, Size = fi.Length, Modified = fi.LastWriteTime, IconKey = p }); }
                if (list.Count >= 8) break;
            }
            _showingRecents = true;
            SetResults(list, false);
            HintText.Text = list.Count > 0 ? "Recenti · Canc rimuove · tasto destro per svuotare" : "";
            UpdateStatus();
        }

        string AppName(string launch)
        {
            // Il nome dall'elenco delle app, se c'è; altrimenti l'ultimo pezzo del parsing name
            var hit = App.Current.Apps.Frequent(new Dictionary<string, int> { { launch, 1 } }, 1).FirstOrDefault();
            if (hit != null) return hit.Title;
            string s = launch.Substring("shell:AppsFolder\\".Length);
            int bang = s.IndexOf('!');
            return bang > 0 ? s.Substring(0, bang) : s;
        }

        void RemoveRecent(ResultVm vm)
        {
            if (vm == null) return;
            int i = _results.IndexOf(vm);
            S.RemoveRecent(vm.Hit.Path);
            App.Current.SaveSettings();
            ShowRecents();
            if (_results.Count > 0) ResultsList.SelectedIndex = Math.Min(Math.Max(0, i), _results.Count - 1);
        }

        void ClearRecents()
        {
            S.ClearRecents();
            App.Current.SaveSettings();
            ShowRecents();
        }

        void RunSearch()
        {
            string text = SearchBox.Text.Trim();
            var words = SearchText.Words(text);
            int serial = ++_searchSerial;
            if (words.Count == 0) { ShowRecents(); return; }
            var usage = new Dictionary<string, int>(S.Usage, StringComparer.OrdinalIgnoreCase);
            var index = App.Current.Index;
            var apps = App.Current.Apps;
            bool searchApps = S.Index.Apps;
            Task.Run(() =>
            {
                var hits = new List<SearchHit>();
                try
                {
                    if (searchApps) hits.AddRange(apps.Search(words, usage));
                    hits.AddRange(index.Search(words, 40, usage));
                }
                catch (Exception ex) { Program.LogError("Ricerca", ex); }
                var top = hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Modified).Take(12).ToList();
                string suggestion = null;
                if (top.Count < 2)
                {
                    // «Forse cercavi»: la prima parola di almeno 4 lettere scritta male
                    foreach (var w in words.Where(w => w.Length >= 4 && w.All(char.IsLetter)))
                    {
                        string s = index.SuggestWord(w);
                        if (s != null && s != w) { suggestion = string.Join(" ", words.Select(x => x == w ? s : x)); break; }
                    }
                }
                return new { top, suggestion };
            }).ContinueWith(t =>
            {
                if (serial != _searchSerial || t.IsFaulted) return;
                var list = t.Result.top;
                // Un percorso scritto per intero si apre com'è
                if (text.Length > 2 && (text.IndexOf(":\\", StringComparison.Ordinal) == 1 || text.StartsWith("\\\\")) && (Directory.Exists(text) || File.Exists(text)) && !list.Any(h => string.Equals(h.Path, text, StringComparison.OrdinalIgnoreCase)))
                    list.Insert(0, new SearchHit { Kind = Directory.Exists(text) ? HitKind.Folder : HitKind.File, Title = Path.GetFileName(text.TrimEnd('\\')), Path = text, Subtitle = text, IconKey = text });
                if (t.Result.suggestion != null)
                    list.Add(new SearchHit { Kind = HitKind.Command, Title = "Forse cercavi: " + t.Result.suggestion, Path = "suggest:" + t.Result.suggestion, Subtitle = "Cerca con le parole corrette", IconKey = "glyph:E8FD" });
                list.Add(new SearchHit { Kind = HitKind.Command, Title = "Esegui: " + text, Path = "run:" + text, Subtitle = "Come nella finestra Esegui (Win+R)", IconKey = "glyph:E756" });
                list.Add(new SearchHit { Kind = HitKind.Command, Title = "Cerca sul web: " + text, Path = "web:" + text, Subtitle = "Apre il browser", IconKey = "glyph:E774" });
                _showingRecents = false;
                SetResults(list, true);
                HintText.Text = "↑↓ scegli · Invio apri · Ctrl+Invio apri la cartella · Esc chiudi";
                UpdateStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetResults(List<SearchHit> hits, bool selectFirst)
        {
            _results.Clear();
            foreach (var h in hits) _results.Add(new ResultVm(h));
            Results.Visibility = _results.Count > 0 || App.Current.Index.Status.Scanning ? Visibility.Visible : Visibility.Collapsed;
            if (selectFirst && _results.Count > 0) { ResultsList.SelectedIndex = 0; ResultsList.ScrollIntoView(_results[0]); }
            HintText.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateStatus()
        {
            var st = App.Current.Index.Status;
            if (!S.Index.Enabled) StatusText.Text = "Indice dei file disattivato: solo app e comandi.";
            else if (st.Scanning) StatusText.Text = $"Lettura dei file in corso… {Tools.FormatCount(st.Seen)} elementi" + (string.IsNullOrEmpty(st.Current) ? "" : "  ·  " + Shorten(st.Current, 60));
            else if (st.Entries == 0) StatusText.Text = string.IsNullOrEmpty(st.Message) ? "L'indice è vuoto." : st.Message;
            else StatusText.Text = $"{Tools.FormatCount(st.Entries)} file e cartelle" + (st.LastScan.Year > 2000 ? $"  ·  aggiornato {Tools.FormatDate(st.LastScan)}" : "") + (App.Current.Apps.Count > 0 ? $"  ·  {App.Current.Apps.Count} app" : "") + (string.IsNullOrEmpty(st.Message) ? "" : "  ·  " + st.Message);
            if (SearchBox.IsKeyboardFocusWithin && Results.Visibility != Visibility.Visible && (st.Scanning || SearchBox.Text.Length > 0)) Results.Visibility = Visibility.Visible;
        }

        static string Shorten(string s, int max) => s.Length <= max ? s : "…" + s.Substring(s.Length - max + 1);

        void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: MoveSelection(1); e.Handled = true; break;
                case Key.Up: MoveSelection(-1); e.Handled = true; break;
                case Key.PageDown: MoveSelection(5); e.Handled = true; break;
                case Key.PageUp: MoveSelection(-5); e.Handled = true; break;
                case Key.Enter:
                    OpenSelected((Keyboard.Modifiers & ModifierKeys.Control) != 0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    e.Handled = true; break;
                case Key.Delete:
                    if (_showingRecents && SearchBox.Text.Length == 0) { RemoveRecent(ResultsList.SelectedItem as ResultVm ?? _results.FirstOrDefault()); e.Handled = true; }
                    break;
                case Key.Escape:
                    if (SearchBox.Text.Length > 0) SearchBox.Text = ""; else HideBar();
                    e.Handled = true; break;
            }
        }

        void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected((Keyboard.Modifiers & ModifierKeys.Control) != 0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; }
            else if (e.Key == Key.Delete && _showingRecents) { RemoveRecent(ResultsList.SelectedItem as ResultVm); e.Handled = true; }
            else if (e.Key == Key.Escape) { HideBar(); e.Handled = true; }
        }

        void MoveSelection(int delta)
        {
            if (_results.Count == 0) return;
            int i = Math.Max(0, Math.Min(_results.Count - 1, ResultsList.SelectedIndex + delta));
            ResultsList.SelectedIndex = i;
            ResultsList.ScrollIntoView(_results[i]);
        }

        void ResultsList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var item = ItemAt(e.OriginalSource as DependencyObject);
            if (item == null) return;
            ResultsList.SelectedItem = item;
            OpenSelected((Keyboard.Modifiers & ModifierKeys.Control) != 0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }

        void ResultsList_RightClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemAt(e.OriginalSource as DependencyObject);
            if (item == null || item.Hit.Kind == HitKind.Command) return;
            ResultsList.SelectedItem = item;
            KeepOpen = true;
            var m = new ContextMenu();
            m.Items.Add(Menu("Apri", () => OpenHit(item.Hit, false, false)));
            if (item.Hit.Kind != HitKind.App)
            {
                m.Items.Add(Menu("Apri la cartella", () => OpenHit(item.Hit, true, false)));
                if (item.Hit.Kind == HitKind.File && item.Hit.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) m.Items.Add(Menu("Apri come amministratore", () => OpenHit(item.Hit, false, true)));
                m.Items.Add(Menu("Copia il percorso", () => Tools.CopyToClipboard(item.Hit.Path)));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Aggiungi alla barra", () =>
            {
                S.Buttons.Add(new BarButton { Label = item.Hit.Kind == HitKind.File ? Path.GetFileNameWithoutExtension(item.Hit.Title) : item.Hit.Title, Target = item.Hit.Path });
                CommitButtons();
            }));
            if (_showingRecents)
            {
                m.Items.Add(new Separator());
                m.Items.Add(Menu("Rimuovi dai recenti", () => RemoveRecent(item)));
                m.Items.Add(Menu("Svuota i recenti", ClearRecents));
            }
            m.Closed += (s, a) => { KeepOpen = IsActive; };
            m.IsOpen = true;
            e.Handled = true;
        }

        ResultVm ItemAt(DependencyObject d)
        {
            while (d != null && !(d is ListBoxItem)) d = VisualTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext as ResultVm;
        }

        void OpenSelected(bool reveal, bool admin)
        {
            var vm = ResultsList.SelectedItem as ResultVm ?? _results.FirstOrDefault();
            if (vm == null)
            {
                string text = SearchBox.Text.Trim();
                if (text.Length > 0) { Tools.Open(text); Done(); }
                return;
            }
            OpenHit(vm.Hit, reveal, admin);
        }

        void OpenHit(SearchHit h, bool reveal, bool admin)
        {
            switch (h.Kind)
            {
                case HitKind.Command:
                    if (h.Path.StartsWith("suggest:")) { SearchBox.Text = h.Path.Substring(8); SearchBox.CaretIndex = SearchBox.Text.Length; return; }
                    if (h.Path.StartsWith("run:")) Tools.Open(h.Path.Substring(4), "", "", admin);
                    else if (h.Path.StartsWith("web:")) Tools.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(h.Path.Substring(4)));
                    break;
                case HitKind.App:
                    Tools.Open(h.Path);
                    S.NoteOpened(h.Path);
                    break;
                default:
                    if (reveal) Tools.RevealInExplorer(h.Path);
                    else Tools.Open(h.Path, "", "", admin);
                    S.NoteOpened(h.Path);
                    break;
            }
            App.Current.SaveSettings();
            Done();
        }

        /// <summary>Dopo un'apertura: la barra si ritira (o, se bloccata, si svuota la ricerca).</summary>
        void Done()
        {
            if (S.Pinned) { SearchBox.Text = ""; Results.Visibility = Visibility.Collapsed; }
            else HideBar();
        }
    }
}
