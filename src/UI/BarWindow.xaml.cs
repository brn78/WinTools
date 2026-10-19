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
            var look = IconLook.For(b);
            Glyph = look.Glyph;
            Mdi = look.Mdi;
            IconBrush = look.Brush;
            if (!string.IsNullOrEmpty(look.ImageKey))
                _icon = IconCache.Get(look.ImageKey, 24, img => { if (img != null) { _icon = img; Raise("Icon"); Raise("ImageVisibility"); Raise("GlyphVisibility"); Raise("MdiVisibility"); } });
        }
        public static bool IsGlyph(string s) => IconLook.IsGlyph(s);
        public Geometry Mdi { get; }
        public Brush IconBrush { get; }
        public Visibility MdiVisibility => Mdi != null && _icon == null ? Visibility.Visible : Visibility.Collapsed;
        public bool ShowLabels { get; }
        public bool IsGroup => Button.Group;
        public bool IsSeparator => Button.Separator;
        public Visibility SeparatorVisibility => IsSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContentVisibility => IsSeparator ? Visibility.Collapsed : Visibility.Visible;
        public double MinWidth => IsSeparator ? 0 : 56;
        public Thickness Pad => IsSeparator ? new Thickness(5, 4, 5, 4) : new Thickness(10, 4, 10, 4);
        public Thickness GroupPad => IsSeparator ? new Thickness(10, 2, 10, 2) : new Thickness(10, 6, 10, 6);
        /// <summary>Nel menu del contenitore il separatore è una voce bassa, non alta quanto un tasto.</summary>
        public double GroupMinHeight => IsSeparator ? 0 : 32;
        /// <summary>Separatore con una breve descrizione (solo nel menu dei contenitori): il testo a sinistra della linea.</summary>
        public Visibility SeparatorLabelVisibility => IsSeparator && !string.IsNullOrEmpty(Button.Label) ? Visibility.Visible : Visibility.Collapsed;
        public string Label => Button.Label;
        public string Tooltip
        {
            get
            {
                if (IsSeparator) return (string.IsNullOrEmpty(Button.Label) ? "" : Button.Label + "\n") + "Separatore · trascinalo per spostarlo, tasto destro per la descrizione o per toglierlo";
                if (IsGroup) return $"{Button.Label}\n{Button.Children.Count} {(Button.Children.Count == 1 ? "tasto" : "tasti")} · clic per aprire, trascina qui un tasto per aggiungerlo";
                if (string.IsNullOrEmpty(Button.Label)) return Button.Target;
                return (Button.IconOnly || !ShowLabels ? Button.Label + "\n" : "") + Button.Target + (Button.Admin ? "  (amministratore)" : "");
            }
        }
        public string Glyph { get; private set; }
        public ImageSource Icon => _icon;
        public Visibility ImageVisibility => _icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => _icon == null && Mdi == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LabelVisibility => ShowLabels && !Button.IconOnly && !string.IsNullOrEmpty(Button.Label) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GroupBadgeVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;

        // Stato durante un trascinamento: il tasto che si muove si attenua, il contenitore sotto il mouse si evidenzia
        double _dragOpacity = 1;
        Brush _dropBrush = Brushes.Transparent;
        public double DragOpacity { get => _dragOpacity; set { if (_dragOpacity != value) { _dragOpacity = value; Raise("DragOpacity"); } } }
        public Brush DropBrush { get => _dropBrush; set { if (!ReferenceEquals(_dropBrush, value)) { _dropBrush = value; Raise("DropBrush"); } } }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Un risultato per l'elenco. Per le cartelle la dimensione si calcola in sottofondo, finché l'elenco resta quello.</summary>
    public sealed class ResultVm : INotifyPropertyChanged
    {
        public SearchHit Hit { get; }
        ImageSource _icon;
        string _meta;
        public ResultVm(SearchHit h, System.Threading.CancellationToken token)
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
            _meta = string.Join(" · ", meta);
            if (h.Kind == HitKind.Folder) StartFolderSize(h, token);
        }

        void StartFolderSize(SearchHit h, System.Threading.CancellationToken token)
        {
            FolderSize.Result cached;
            if (FolderSize.TryCached(h.Path, out cached)) { ApplyFolderSize(h, cached); return; }
            _meta = "Cartella · calcolo…" + (h.Modified.Year > 1980 ? " · " + Tools.FormatDate(h.Modified) : "");
            var disp = Dispatcher.CurrentDispatcher;
            Task.Run(() =>
            {
                try
                {
                    var r = FolderSize.Compute(h.Path, token, TimeSpan.FromSeconds(6));
                    if (token.IsCancellationRequested) return;
                    disp.BeginInvoke(new Action(() => ApplyFolderSize(h, r)));
                }
                catch { }
            }, token);
        }

        void ApplyFolderSize(SearchHit h, FolderSize.Result r)
        {
            string size = (r.Partial ? "almeno " : "") + Tools.FormatSize(r.Bytes);
            Meta = "Cartella · " + size + (h.Modified.Year > 1980 ? " · " + Tools.FormatDate(h.Modified) : "");
        }

        public string Title => Hit.Title;
        public string Subtitle => Hit.Subtitle;
        public string Meta { get => _meta; private set { _meta = value; Raise("Meta"); } }
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
        BarButton _openGroup;

        /// <summary>Un trascinamento in corso: da quale tasto è partito, dov'è il mouse, che cosa c'è sotto.</summary>
        sealed class DragState
        {
            public ButtonVm Vm;
            public Button Source;
            public bool FromPopup;
            public Point StartScreen;
            public bool Active;
            public ButtonVm HoverGroup;
            public int BarIndex = -1;      // posizione di inserimento sulla barra (-1 = fuori)
            public int GroupIndex = -1;    // posizione nel menu del contenitore aperto (-1 = fuori)
        }
        DragState _drag;

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
            SetupInfoStrip();
            SetupFilters();
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); RunSearch(); };
            _noticeTimer.Tick += (s, e) => { _noticeTimer.Stop(); HideNotice(); };
            Deactivated += (s, e) =>
            {
                // Il fuoco va altrove: la barra si ritira, a meno che non sia bloccata o ci sia un dialogo nostro aperto
                FilterPopup.IsOpen = false;
                if (_dialogOpen || S.Pinned) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!IsActive && !_dialogOpen && !S.Pinned && IsBarVisible && !GroupPopup.IsOpen) HideBar(); }));
            };
            DpiChanged += (s, e) => Reposition();
            // Comparsa dal bordo senza rubare il fuoco: al primo clic sulla barra la finestra si attiva davvero,
            // altrimenti si potrebbe scrivere nella casella ma i tasti finirebbero nell'app di prima
            PreviewMouseDown += (s, e) =>
            {
                AutoShown = false;   // si sta usando la barra: resta finché è attiva
                // Un clic nella barra fuori dal pannello dei filtri lo chiude (sul suo pulsante ci pensa il Click a chiuderlo)
                if (FilterPopup.IsOpen && !FilterButton.IsMouseOver) FilterPopup.IsOpen = false;
                EnsureActive(e.OriginalSource as DependencyObject);
            };
            PreviewKeyDown += (s, e) => { AutoShown = false; };
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
            // I tasti sono stati ricreati: il menu del contenitore aperto va riancorato al nuovo tasto, altrimenti
            // il popup resta legato a un elemento staccato dall'albero e si sposta all'angolo della finestra
            if (GroupPopup.IsOpen && _openGroup != null) ReanchorGroupPopup();
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
            DockPanel.SetDock(InfoStrip, bottom ? Dock.Bottom : Dock.Top);
            InfoStrip.BorderThickness = bottom ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
            InfoStrip.Visibility = S.ShowInfoStrip ? Visibility.Visible : Visibility.Collapsed;
            GroupPopup.Placement = bottom ? PlacementMode.Top : PlacementMode.Bottom;
            if (IsVisible) { StopInfo(); StartInfo(); Reposition(); }
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
            AutoShown = false;
            if (activate)
            {
                KeepOpen = true;
                _noActivate = false;
                Native.SetNoActivate(_hwnd, false);
                Activate();
                Native.ForceForeground(_hwnd);
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else KeepOpen = Notice.Visibility == Visibility.Visible;
            UpdateRecentsChevron();
            UpdateStatus();
            StartInfo();
        }

        bool _autoFocus;

        /// <summary>Vero dalla comparsa dal bordo (con attivazione automatica) finché non si scrive o si fa clic: in questo stato la
        /// barra si ritira quando il mouse si allontana, anche se ha il fuoco. Prima restava finché era attiva, e sembrava non chiudersi.</summary>
        public bool AutoShown { get; private set; }

        /// <summary>
        /// Comparsa dal bordo: se l'opzione è attiva, la finestra si attiva e il cursore va nella ricerca (si può scrivere
        /// subito); la barra resta finché si scrive o si clicca, altrimenti si ritira quando il mouse si allontana, con Esc o
        /// quando il fuoco va altrove.
        /// </summary>
        public void ShowFromEdge(Forms.Screen screen)
        {
            ShowBar(false, screen);
            if (!S.FocusOnEdge) return;
            _noActivate = false;
            Native.SetNoActivate(_hwnd, false);
            Activate();
            Native.ForceForeground(_hwnd);
            _autoFocus = true;
            try { SearchBox.Focus(); Keyboard.Focus(SearchBox); }
            finally { _autoFocus = false; }
            KeepOpen = Notice.Visibility == Visibility.Visible;
            AutoShown = true;
        }

        /// <summary>Toglie il «non attivabile» e porta la finestra in primo piano; con un clic nella ricerca, fuoco alla casella.</summary>
        void EnsureActive(DependencyObject source)
        {
            // Il fuoco va nella casella solo se il clic è sulla casella stessa: su un pulsante (filtri, recenti, cancella)
            // spostare il fuoco farebbe perdere al Button la cattura del mouse e il clic non arriverebbe
            bool inSearch = false, inButton = false;
            for (var d = source; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) inButton = true;
                if (d == SearchHost) { inSearch = !inButton; break; }
            }
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
            FilterPopup.IsOpen = false;
            _hiding = true;
            KeepOpen = false;
            AutoShown = false;
            double to = (S.Edge == "bottom" ? 1 : -1) * Math.Max(40, Panel.ActualHeight);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            slide.Completed += (s, e) =>
            {
                if (!_hiding) return;
                _hiding = false;
                StopInfo();
                Hide();
                Slide.BeginAnimation(TranslateTransform.YProperty, null);
                Slide.Y = 0;
                Panel.Opacity = 1;
                SearchBox.Text = "";
                _results.Clear();
                Results.Visibility = Visibility.Collapsed;
                SetSearchBorder("ControlStroke");
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

        // ------------------------------------------------------------------ filtri della ricerca
        bool _filterLoading;
        readonly DispatcherTimer _filterExtTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        void SetupFilters()
        {
            _filterExtTimer.Tick += (s, e) => { _filterExtTimer.Stop(); Filter_Changed(null, null); };
            UpdateFilterGlyph();
        }

        /// <summary>L'icona dei filtri prende il colore di accento quando ce n'è almeno uno attivo.</summary>
        void UpdateFilterGlyph()
        {
            bool active = S.Filter.IsActive;
            FilterGlyph.SetResourceReference(TextBlock.ForegroundProperty, active ? "Accent" : "TextSecondary");
            FilterGlyph.Text = active ? "" : "";   // filtro pieno / filtro vuoto
            FilterButton.ToolTip = active ? "Filtri attivi: " + S.Filter.Describe() + "\nClic: modifica o azzera" : "Filtri: tipo, estensioni, data";
        }

        void LoadFilterControls()
        {
            _filterLoading = true;
            var f = S.Filter;
            FilterKindAll.IsChecked = f.Kind == "all";
            FilterKindFolders.IsChecked = f.Kind == "folders";
            FilterKindFiles.IsChecked = f.Kind == "files";
            FilterExt.Text = f.Ext ?? "";
            foreach (ComboBoxItem i in FilterSince.Items) if ((string)i.Tag == f.SinceDays.ToString()) FilterSince.SelectedItem = i;
            if (FilterSince.SelectedItem == null) FilterSince.SelectedIndex = 0;
            FilterNameOnly.IsChecked = f.NameOnly;
            _filterLoading = false;
        }

        /// <summary>Il pannello resta aperto (StaysOpen) e si chiude solo in modo esplicito: clic sul pulsante, clic altrove nella barra,
        /// Esc, finestra disattivata o barra nascosta. Così il clic sul pulsante è un interruttore affidabile.</summary>
        void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilterPopup.IsOpen) { FilterPopup.IsOpen = false; return; }
            LoadFilterControls();
            FilterPopup.PlacementTarget = FilterButton;
            FilterPopup.IsOpen = true;
        }

        void FilterPopup_Opened(object sender, EventArgs e) { KeepOpen = true; }
        void FilterPopup_Closed(object sender, EventArgs e)
        {
            KeepOpen = IsActive && (SearchBox.IsKeyboardFocusWithin || SearchBox.Text.Length > 0);
            if (IsActive) SearchBox.Focus();
        }

        void FilterPanel_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { FilterPopup.IsOpen = false; e.Handled = true; } }

        void FilterExt_Changed(object sender, TextChangedEventArgs e) { if (_filterLoading) return; _filterExtTimer.Stop(); _filterExtTimer.Start(); }

        /// <summary>Un filtro è cambiato: si salva, l'icona si aggiorna e la ricerca riparte.</summary>
        void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (_filterLoading || FilterKindAll == null) return;
            var f = S.Filter;
            f.Kind = FilterKindFolders.IsChecked == true ? "folders" : FilterKindFiles.IsChecked == true ? "files" : "all";
            f.Ext = FilterExt.Text.Trim();
            int days; int.TryParse((FilterSince.SelectedItem as ComboBoxItem)?.Tag as string ?? "0", out days);
            f.SinceDays = days;
            f.NameOnly = FilterNameOnly.IsChecked == true;
            App.Current.SaveSettings();
            UpdateFilterGlyph();
            if (SearchBox.Text.Trim().Length > 0) RunSearch();
        }

        void FilterClear_Click(object sender, RoutedEventArgs e)
        {
            S.Filter.Clear();
            App.Current.SaveSettings();
            LoadFilterControls();
            UpdateFilterGlyph();
            if (SearchBox.Text.Trim().Length > 0) RunSearch();
        }

        void FilterClose_Click(object sender, RoutedEventArgs e) { FilterPopup.IsOpen = false; }

        /// <summary>Applica i filtri attivi ai risultati (le app restano solo senza filtri su tipo, estensione o data).</summary>
        static List<SearchHit> ApplyFilter(List<SearchHit> hits, SearchFilter f, List<string> words)
        {
            if (!f.IsActive) return hits;
            var ext = f.Extensions();
            bool kindOrExt = f.Kind != "all" || ext.Count > 0;
            DateTime since = f.SinceDays > 0 ? DateTime.Now.Date.AddDays(-(f.SinceDays - 1)) : DateTime.MinValue;
            var result = new List<SearchHit>();
            foreach (var h in hits)
            {
                if (h.Kind == HitKind.App) { if (kindOrExt || f.SinceDays > 0) continue; }
                if (f.Kind == "folders" && h.Kind != HitKind.Folder) continue;
                if (f.Kind == "files" && h.Kind != HitKind.File) continue;
                if (ext.Count > 0)
                {
                    if (h.Kind != HitKind.File) continue;
                    string e = Path.GetExtension(h.Title ?? "").TrimStart('.').ToLowerInvariant();
                    if (!ext.Contains(e)) continue;
                }
                if (f.SinceDays > 0 && h.Kind != HitKind.App && h.Modified < since) continue;
                if (f.NameOnly && h.Kind != HitKind.App)
                {
                    string name = SearchText.Fold(h.Title ?? "");
                    if (words.Any(w => name.IndexOf(w, StringComparison.Ordinal) < 0)) continue;
                }
                result.Add(h);
            }
            return result;
        }

        // ------------------------------------------------------------------ riga informazioni (CPU, RAM, rete, IPv4, COM)
        readonly SystemStats _stats = new SystemStats();
        readonly DispatcherTimer _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool _infoIconsSet;
        string _lastIps = "";

        void SetupInfoStrip()
        {
            _infoTimer.Tick += (s, e) => UpdateInfo();
            if (_infoIconsSet) return;
            _infoIconsSet = true;
            // Icone dal catalogo Material Design (caricato in anticipo all'avvio)
            Task.Run(() => new[] { "cpu-64-bit", "memory", "web", "ip-network", "serial-port" }.Select(n => IconCatalog.Find(n)?.Geometry).ToArray()).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                var g = t.Result;
                CpuIcon.Data = g[0]; RamIcon.Data = g[1]; NetIcon.Data = g[2]; IpIcon.Data = g[3]; ComIcon.Data = g[4];
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void StartInfo() { if (S.ShowInfoStrip) { UpdateInfo(); _infoTimer.Start(); } }
        void StopInfo() { _infoTimer.Stop(); }

        void UpdateInfo()
        {
            if (!S.ShowInfoStrip || !IsVisible) return;
            SystemStats.Snapshot s;
            try { s = _stats.Sample(); } catch { return; }
            CpuText.Text = $"CPU {s.CpuPercent:0}%";
            RamText.Text = s.RamTotal > 0 ? $"RAM {SystemStats.FormatGb(s.RamUsed)}/{SystemStats.FormatGb(s.RamTotal)} GB" : "RAM —";
            if (s.Online)
            {
                NetText.Text = $"↓ {SystemStats.FormatRate(s.RxBytesPerSec),9}  ↑ {SystemStats.FormatRate(s.TxBytesPerSec),9}";
                NetText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                NetIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
            }
            else
            {
                NetText.Text = "Offline";
                NetText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");
                NetIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ErrorText");
            }
            // Più indirizzi o più porte: si vede il primo con «+N», il clic apre l'elenco completo
            _ips = s.Ipv4; _coms = s.ComPorts;
            // Accanto all'indirizzo si legge se arriva dal DHCP o è statico (l'origine è in coda al nome della scheda)
            string ips = s.Ipv4.Count == 0 ? "Nessun IPv4" : $"{s.Ipv4[0].Item1} {IpOrigin(s.Ipv4[0])}" + (s.Ipv4.Count > 1 ? $" +{s.Ipv4.Count - 1}" : "");
            if (ips != _lastIps)
            {
                _lastIps = ips;
                IpText.Text = ips;
                IpButton.ToolTip = s.Ipv4.Count == 0 ? "Nessun indirizzo IPv4 attivo" : string.Join("\n", s.Ipv4.Select(t => $"{t.Item1}  ·  {t.Item2}")) + (s.Ipv4.Count == 1 ? "\n\nClic: copia l'indirizzo" : "\n\nClic: elenco e copia");
            }
            string com = s.ComPorts.Count == 0 ? "Nessuna COM" : s.ComPorts.Count <= 2 ? string.Join(" · ", s.ComPorts.Select(t => t.Item1)) : $"{s.ComPorts[0].Item1} +{s.ComPorts.Count - 1}";
            if (ComText.Text != com)
            {
                ComText.Text = com;
                ComButton.ToolTip = s.ComPorts.Count == 0 ? "Nessuna porta seriale · clic: Gestione dispositivi" : string.Join("\n", s.ComPorts.Select(t => t.Item2 == "" ? t.Item1 : $"{t.Item1}  ·  {t.Item2}")) + (s.ComPorts.Count > 2 ? "\n\nClic: elenco" : "\n\nClic: Gestione dispositivi");
            }
        }

        List<Tuple<string, string>> _ips = new List<Tuple<string, string>>();
        List<Tuple<string, string>> _coms = new List<Tuple<string, string>>();

        static string IpOrigin(Tuple<string, string> ip)
        {
            int i = ip.Item2.LastIndexOf("  ·  ", StringComparison.Ordinal);
            return i >= 0 ? ip.Item2.Substring(i + 5) : "";
        }

        void InfoCpu_Click(object sender, RoutedEventArgs e) { Tools.Open("taskmgr.exe"); if (!S.Pinned) HideBar(); }
        void Signature_Click(object sender, RoutedEventArgs e) { Tools.Open("https://github.com/brn78"); if (!S.Pinned) HideBar(); }
        void InfoNet_Click(object sender, RoutedEventArgs e) { Tools.Open("ms-settings:network"); if (!S.Pinned) HideBar(); }

        void InfoCom_Click(object sender, RoutedEventArgs e)
        {
            if (_coms.Count <= 2) { Tools.Open("devmgmt.msc"); if (!S.Pinned) HideBar(); return; }
            var m = new ContextMenu { PlacementTarget = ComButton, Placement = PlacementMode.Bottom };
            foreach (var p in _coms)
            {
                var port = p;
                var mi = Menu(port.Item2 == "" ? port.Item1 : $"{port.Item1}  ·  {port.Item2}", "", () => Tools.CopyToClipboard(port.Item1));
                mi.ToolTip = "Clic: copia il nome della porta";
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Gestione dispositivi", "", () => { Tools.Open("devmgmt.msc"); if (!S.Pinned) HideBar(); }));
            OpenInfoMenu(m);
        }

        void InfoIp_Click(object sender, RoutedEventArgs e)
        {
            if (_ips.Count == 0) return;
            if (_ips.Count == 1)
            {
                Tools.CopyToClipboard(_ips[0].Item1);
                ShowNotice("success", "Indirizzo copiato", _ips[0].Item1);
                return;
            }
            var m = new ContextMenu { PlacementTarget = IpButton, Placement = PlacementMode.Bottom };
            foreach (var a in _ips)
            {
                var addr = a;
                m.Items.Add(Menu($"{addr.Item1}  ·  {addr.Item2}", "", () => { Tools.CopyToClipboard(addr.Item1); ShowNotice("success", "Indirizzo copiato", addr.Item1); }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Copia tutti", "", () => { Tools.CopyToClipboard(string.Join(Environment.NewLine, _ips.Select(t => t.Item1))); ShowNotice("success", "Indirizzi copiati", string.Join(" · ", _ips.Select(t => t.Item1))); }));
            m.Items.Add(Menu("Impostazioni di rete", "", () => { Tools.Open("ms-settings:network"); if (!S.Pinned) HideBar(); }));
            OpenInfoMenu(m);
        }

        void OpenInfoMenu(ContextMenu m)
        {
            KeepOpen = true;
            m.Closed += (s, a) => { KeepOpen = IsActive; };
            m.IsOpen = true;
        }

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
            if (vm == null || vm.IsSeparator || (_drag != null && _drag.Active)) return;
            if (vm.IsGroup) { OpenGroup(vm.Button, btn); return; }
            Tools.Launch(vm.Button);
            if (!S.Pinned) HideBar();
        }

        // ------------------------------------------------------------------ trascinamento dei tasti
        // Fatto in casa, senza il drag&drop di Windows: il pulsante premuto tiene già il mouse catturato, quindi
        // riceve i movimenti anche fuori dalla barra. Un «fantasma» segue il mouse, una linea mostra dove finirà il
        // tasto, il contenitore sotto il mouse si evidenzia; al rilascio si sposta davvero. Tutto in pixel dello
        // schermo, così funziona anche tra la barra e il menu di un contenitore (che è una finestra a parte).

        void BarButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ButtonVm;
            if (vm == null) return;
            _drag = new DragState { Vm = vm, Source = btn, FromPopup = IsInPopup(btn), StartScreen = btn.PointToScreen(e.GetPosition(btn)) };
        }

        void BarButton_MouseMove(object sender, MouseEventArgs e)
        {
            var d = _drag;
            if (d == null || !ReferenceEquals(d.Source, sender)) return;
            if (e.LeftButton != MouseButtonState.Pressed) { if (!d.Active) _drag = null; else EndDrag(false); return; }
            Point sp;
            try { sp = d.Source.PointToScreen(e.GetPosition(d.Source)); } catch { return; }
            if (!d.Active)
            {
                if (Math.Abs(sp.X - d.StartScreen.X) < 8 && Math.Abs(sp.Y - d.StartScreen.Y) < 8) return;
                BeginDrag(d);
            }
            UpdateDrag(d, sp);
        }

        void BarButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var d = _drag;
            if (d == null || !ReferenceEquals(d.Source, sender)) return;
            if (!d.Active) { _drag = null; return; }
            e.Handled = true;   // niente clic dopo un trascinamento
            try { UpdateDrag(d, d.Source.PointToScreen(e.GetPosition(d.Source))); } catch { }
            EndDrag(true);
        }

        bool IsInPopup(DependencyObject el)
        {
            for (var c = el; c != null; c = VisualTreeHelper.GetParent(c)) if (ReferenceEquals(c, GroupPopup.Child)) return true;
            return false;
        }

        void BeginDrag(DragState d)
        {
            d.Active = true;
            KeepOpen = true;
            if (GroupPopup.IsOpen) GroupPopup.StaysOpen = true;   // mentre si trascina il menu non deve chiudersi
            d.Vm.DragOpacity = 0.35;
            GhostLabel.Text = d.Vm.IsSeparator ? "Separatore" : d.Vm.Label;
            GhostImage.Source = d.Vm.Icon;
            GhostImage.Visibility = d.Vm.ImageVisibility;
            GhostGlyph.Text = d.Vm.Glyph;
            GhostGlyph.Foreground = d.Vm.IconBrush;
            GhostGlyph.Visibility = d.Vm.GlyphVisibility;
            GhostPath.Data = d.Vm.Mdi;
            GhostPath.Fill = d.Vm.IconBrush;
            GhostPath.Visibility = d.Vm.MdiVisibility;
            Ghost.IsOpen = true;
            Mouse.OverrideCursor = Cursors.Hand;
        }

        void UpdateDrag(DragState d, Point sp)
        {
            // Il fantasma segue il mouse (le posizioni del Popup assoluto sono in unità indipendenti dal DPI)
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Ghost.HorizontalOffset = sp.X / scale + 14;
            Ghost.VerticalOffset = sp.Y / scale + 10;

            // Sopra il menu del contenitore aperto?
            d.GroupIndex = -1;
            if (!d.Vm.IsGroup && GroupPopup.IsOpen && GroupPopup.Child is FrameworkElement pc && Contains(pc, sp, 0))
            {
                d.GroupIndex = IndexIn(GroupList, sp, vertical: true);
                ShowGroupDropLine(d.GroupIndex);
            }
            else GroupDropLine.Visibility = Visibility.Collapsed;

            // Sopra un contenitore della barra? (non sé stesso, e un contenitore non entra in un altro)
            ButtonVm hover = null;
            if (d.GroupIndex < 0 && !d.Vm.IsGroup)
            {
                for (int i = 0; i < _buttons.Count; i++)
                {
                    var vm = _buttons[i];
                    if (!vm.IsGroup || ReferenceEquals(vm, d.Vm)) continue;
                    var el = ButtonsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (el != null && Contains(el, sp, 2)) { hover = vm; break; }
                }
            }
            if (!ReferenceEquals(hover, d.HoverGroup))
            {
                if (d.HoverGroup != null) d.HoverGroup.DropBrush = Brushes.Transparent;
                d.HoverGroup = hover;
                if (hover != null) hover.DropBrush = (Brush)FindResource("Accent");
            }

            // Sulla barra (con un po' di margine sopra e sotto): la linea di inserimento
            d.BarIndex = -1;
            if (hover == null && d.GroupIndex < 0 && Contains(Panel, sp, 40 * scale))
            {
                d.BarIndex = IndexIn(ButtonsList, sp, vertical: false);
                ShowDropLine(d.BarIndex);
            }
            else DropLine.Visibility = Visibility.Collapsed;
        }

        void EndDrag(bool commit)
        {
            var d = _drag;
            _drag = null;
            if (d == null) return;
            Ghost.IsOpen = false;
            DropLine.Visibility = Visibility.Collapsed;
            GroupDropLine.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            d.Vm.DragOpacity = 1;
            if (d.HoverGroup != null) d.HoverGroup.DropBrush = Brushes.Transparent;
            GroupPopup.StaysOpen = false;
            KeepOpen = IsActive || GroupPopup.IsOpen;
            if (!commit) return;

            var b = d.Vm.Button;
            if (d.GroupIndex >= 0 && _openGroup != null && !b.Group)
            {
                // Nel menu del contenitore aperto, nella posizione indicata
                var list = _openGroup.Children;
                int from = list.IndexOf(b);
                int to = d.GroupIndex;
                S.DetachButton(b.Id);
                if (from >= 0 && to > from) to--;
                list.Insert(Math.Max(0, Math.Min(list.Count, to)), b);
                CommitButtons();
            }
            else if (d.HoverGroup != null && !b.Group)
            {
                S.DetachButton(b.Id);
                d.HoverGroup.Button.Children.Add(b);
                CommitButtons();
                if (GroupPopup.IsOpen && _openGroup != null && _openGroup.Id == d.HoverGroup.Button.Id) RefreshGroupItems();
            }
            else if (d.BarIndex >= 0)
            {
                int from = S.Buttons.IndexOf(b);
                int to = d.BarIndex;
                if (from == to || from == to - 1) { if (from >= 0) return; }   // lasciato dov'era
                S.DetachButton(b.Id);
                if (from >= 0 && to > from) to--;
                S.Buttons.Insert(Math.Max(0, Math.Min(S.Buttons.Count, to)), b);
                CommitButtons();
                if (d.FromPopup) RefreshGroupItems();
            }
            // Altrimenti: rilasciato lontano, non cambia niente
        }

        /// <summary>Il punto (pixel dello schermo) sta sull'elemento, con un margine (in pixel).</summary>
        static bool Contains(FrameworkElement el, Point sp, double margin)
        {
            try
            {
                var a = el.PointToScreen(new Point(0, 0));
                var b = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
                return sp.X >= a.X - margin && sp.X <= b.X + margin && sp.Y >= a.Y - margin && sp.Y <= b.Y + margin;
            }
            catch { return false; }
        }

        /// <summary>Posizione di inserimento in un elenco: prima dell'elemento la cui metà è oltre il mouse.</summary>
        static int IndexIn(ItemsControl list, Point sp, bool vertical)
        {
            int n = list.Items.Count;
            for (int i = 0; i < n; i++)
            {
                var el = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (el == null) continue;
                try
                {
                    var mid = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
                    if (vertical ? sp.Y < mid.Y : sp.X < mid.X) return i;
                }
                catch { }
            }
            return n;
        }

        void ShowDropLine(int index)
        {
            double x;
            var host = (FrameworkElement)DropLine.Parent;
            if (_buttons.Count == 0) x = 0;
            else if (index < _buttons.Count)
            {
                var el = ButtonsList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                if (el == null) return;
                x = el.TranslatePoint(new Point(0, 0), host).X - 2;
            }
            else
            {
                var el = ButtonsList.ItemContainerGenerator.ContainerFromIndex(_buttons.Count - 1) as FrameworkElement;
                if (el == null) return;
                x = el.TranslatePoint(new Point(el.ActualWidth, 0), host).X - 1;
            }
            DropLine.Margin = new Thickness(Math.Max(0, x), 0, 0, 0);
            DropLine.Visibility = Visibility.Visible;
        }

        void ShowGroupDropLine(int index)
        {
            double y;
            var host = (FrameworkElement)GroupDropLine.Parent;
            int n = _groupItems.Count;
            if (n == 0) y = 0;
            else if (index < n)
            {
                var el = GroupList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                if (el == null) return;
                y = el.TranslatePoint(new Point(0, 0), host).Y - 2;
            }
            else
            {
                var el = GroupList.ItemContainerGenerator.ContainerFromIndex(n - 1) as FrameworkElement;
                if (el == null) return;
                y = el.TranslatePoint(new Point(0, el.ActualHeight), host).Y - 1;
            }
            GroupDropLine.Margin = new Thickness(6, Math.Max(0, y), 6, 0);
            GroupDropLine.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ file trascinati da Esplora file (drag&drop di Windows)
        const string ShellIdListFormat = "Shell IDList Array";

        static bool CanDrop(IDataObject d) => d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(ShellIdListFormat);

        /// <summary>
        /// L'effetto da rispondere alla sorgente: dev'essere tra quelli che consente, altrimenti OLE mostra il divieto.
        /// Per Questo PC, Rete, Cestino… Esplora file permette solo «collegamento»; per i file anche copia. Qui si crea
        /// comunque un tasto, quindi «collegamento» è il più fedele; mai «sposta», che farebbe eliminare gli originali.
        /// </summary>
        static DragDropEffects DropEffectFor(DragEventArgs e)
        {
            if (!CanDrop(e.Data)) return DragDropEffects.None;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        /// <summary>
        /// I tasti da creare per ciò che è stato trascinato: file e cartelle (FileDrop) oppure, quando non hanno un percorso
        /// (Questo PC, Rete, Cestino, Pannello di controllo…), gli elementi della shell con il loro nome di analisi.
        /// </summary>
        static List<BarButton> DroppedButtons(IDataObject d)
        {
            var list = new List<BarButton>();
            try
            {
                if (d.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = d.GetData(DataFormats.FileDrop) as string[];
                    if (files != null) list.AddRange(files.Where(f => !string.IsNullOrEmpty(f)).Select(ButtonForPath));
                }
                if (list.Count == 0 && d.GetDataPresent(ShellIdListFormat))
                {
                    var obj = d.GetData(ShellIdListFormat);
                    byte[] bytes = obj as byte[];
                    if (bytes == null && obj is MemoryStream ms) bytes = ms.ToArray();
                    foreach (var it in Native.ShellItemsFromIdList(bytes))
                    {
                        string parsing = it.Item1;
                        if (Path.IsPathRooted(parsing) && !parsing.StartsWith("::")) list.Add(ButtonForPath(parsing));
                        else list.Add(new BarButton { Label = it.Item2, Target = parsing.StartsWith("::") ? "shell:" + parsing : parsing });
                    }
                }
            }
            catch (Exception ex) { Program.LogError("Elementi trascinati", ex); }
            return list;
        }

        void Buttons_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DropEffectFor(e);
            e.Handled = true;
        }

        void Buttons_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DropEffectFor(e);
            if (e.Effects == DragDropEffects.None) return;
            var buttons = DroppedButtons(e.Data);
            if (buttons.Count == 0) return;
            var targetVm = (sender as Button)?.Tag as ButtonVm;
            Point sp;
            try { sp = PointToScreen(e.GetPosition(this)); } catch { sp = new Point(0, 0); }
            bool ontoGroup = targetVm != null && targetVm.IsGroup;
            var list = ontoGroup ? targetVm.Button.Children : S.Buttons;
            int at = ontoGroup ? list.Count : Math.Max(0, Math.Min(S.Buttons.Count, IndexIn(ButtonsList, sp, false)));
            // Più elementi insieme sulla barra: diventano un contenitore (con Maiusc premuto, tasti separati come prima)
            bool shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
            if (buttons.Count >= 2 && !ontoGroup && !shift)
            {
                var g = new BarButton { Group = true, Icon = "E8B7", Label = GroupNameFor(buttons.Select(b => b.Target).ToArray()), Children = buttons };
                S.Buttons.Insert(at, g);
                CommitButtons();
                ShowNotice("success", $"Contenitore «{g.Label}» con {buttons.Count} elementi.", "Clic destro sul tasto per modificarlo.", "Rinomina…", () =>
                {
                    if (RunDialog(() => new ButtonEditorWindow(g))) CommitButtons();
                });
                return;
            }
            foreach (var b in buttons) list.Insert(at++, b);
            CommitButtons();
        }

        static BarButton ButtonForPath(string f)
        {
            return new BarButton { Label = Directory.Exists(f) ? Path.GetFileName(f.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(f), Target = f };
        }

        /// <summary>Nome per un contenitore creato da più file: la cartella che li contiene tutti, altrimenti «Gruppo».</summary>
        static string GroupNameFor(string[] files)
        {
            try
            {
                if (files.Any(f => !Path.IsPathRooted(f))) return "Gruppo";   // elementi della shell senza percorso
                var dirs = files.Select(f => Path.GetDirectoryName(f.TrimEnd('\\')) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (dirs.Count == 1 && dirs[0] != "")
                {
                    string name = Path.GetFileName(dirs[0].TrimEnd('\\'));
                    if (name == "") name = dirs[0].TrimEnd('\\');   // radice di un disco: «C:»
                    return name;
                }
            }
            catch { }
            return "Gruppo";
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
            if (b.Separator)
            {
                m.Items.Add(MoveToMenu(b));
                m.Items.Add(Menu("Rimuovi il separatore", "", () => { S.DetachButton(b.Id); CommitButtons(); }));
            }
            else if (b.Group)
            {
                m.Items.Add(Menu("Apri", "", () => OpenGroup(b, btn)));
                m.Items.Add(new Separator());
                m.Items.Add(Menu("Modifica…", "", () => EditButton(b)));
                m.Items.Add(Menu(b.Children.Count > 0 ? "Rimuovi il contenitore (i tasti tornano sulla barra)" : "Rimuovi il contenitore", "", () => RemoveGroup(b)));
            }
            else
            {
                m.Items.Add(Menu("Apri", "", () => Tools.Launch(b)));
                if (!IsShellTarget(b))
                {
                    m.Items.Add(Menu("Apri come amministratore", "", () => Tools.Open(b.Target, b.Arguments, b.WorkingDir, true)));
                    m.Items.Add(Menu("Apri il percorso del file", "", () => Reveal(b)));
                }
                AddTargetItems(m.Items, b);
                m.Items.Add(new Separator());
                m.Items.Add(MoveToMenu(b));
                m.Items.Add(Menu("Modifica…", "", () => EditButton(b)));
                m.Items.Add(Menu("Rimuovi", "", () => { S.DetachButton(b.Id); CommitButtons(); }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Aggiungi un tasto…", "", () => AddButton(false)));
            m.Items.Add(Menu("Aggiungi un contenitore…", "", () => AddButton(true)));
            m.Items.Add(Menu("Aggiungi un separatore qui", "", () => AddSeparatorAfter(b)));
        }

        void AddButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.ContextMenu == null) return;
            KeepOpen = true;
            btn.ContextMenu.Items.Clear();
            btn.ContextMenu.Items.Add(Menu("Aggiungi un tasto…", "", () => AddButton(false)));
            btn.ContextMenu.Items.Add(Menu("Aggiungi un contenitore…", "", () => AddButton(true)));
            btn.ContextMenu.Items.Add(Menu("Aggiungi un separatore", "", () => AddSeparatorAfter(null)));
        }

        const string ClsidThisPc = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", ClsidNetwork = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", ClsidRecycleBin = "{645FF040-5081-101B-9F08-00AA002F954E}";

        static bool IsShellTarget(BarButton b) => (b.Target ?? "").TrimStart().StartsWith("shell:::", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Voci «Gestione» e «Proprietà» come nel menu di Esplora file: per Questo PC (Gestione computer, Sistema), Rete
        /// (Connessioni di rete, Centro connessioni), Cestino (Svuota, Proprietà) e in genere per ogni percorso.
        /// </summary>
        void AddTargetItems(ItemCollection items, BarButton b)
        {
            string t = (b.Target ?? "").Trim();
            if (t == "") return;
            Action after = () => { GroupPopup.IsOpen = false; if (!S.Pinned) HideBar(); };
            if (IsShellTarget(b))
            {
                string clsid = t.Substring("shell:::".Length).ToUpperInvariant();
                if (clsid.StartsWith(ClsidThisPc))
                    items.Add(Menu("Gestione", "", () => { if (!Native.ShellVerb(t, "manage")) Tools.Open("compmgmt.msc"); after(); }));
                else if (clsid.StartsWith(ClsidNetwork))
                    items.Add(Menu("Connessioni di rete", "", () => { Tools.Open("ncpa.cpl"); after(); }));
                else if (clsid.StartsWith(ClsidRecycleBin))
                    items.Add(Menu("Svuota il Cestino", "", () => { after(); Tools.EmptyRecycleBin(); }));
                items.Add(Menu("Proprietà", "", () => { Tools.ShellVerb(t, "properties"); after(); }));
                return;
            }
            string path = Environment.ExpandEnvironmentVariables(t);
            if (Path.IsPathRooted(path) && (File.Exists(path) || Directory.Exists(path)))
                items.Add(Menu("Proprietà", "", () => { Tools.ShowProperties(path); after(); }));
        }

        /// <summary>Un separatore subito dopo il tasto indicato (nello stesso elenco), oppure in fondo alla barra.</summary>
        void AddSeparatorAfter(BarButton after)
        {
            var sep = new BarButton { Separator = true };
            List<BarButton> owner = null;
            var cur = after == null ? null : S.FindButton(after.Id, out owner);
            if (cur != null) owner.Insert(owner.IndexOf(cur) + 1, sep); else S.Buttons.Add(sep);
            CommitButtons();
        }

        /// <summary>«Sposta in»: i contenitori esistenti, oppure uno nuovo creato al volo con dentro questo tasto.</summary>
        MenuItem MoveToMenu(BarButton b)
        {
            var mi = new MenuItem { Header = "Sposta in", Icon = GlyphIcon("") };
            foreach (var g in S.Buttons.Where(x => x.Group))
            {
                var target = g;
                mi.Items.Add(Menu(string.IsNullOrEmpty(target.Label) ? "(contenitore)" : target.Label, "", () =>
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
                mi.Items.Add(Menu("Sulla barra", "", () => { S.DetachButton(b.Id); S.Buttons.Add(b); CommitButtons(); }));
            }
            if (mi.Items.Count > 0) mi.Items.Add(new Separator());
            mi.Items.Add(Menu("Nuovo contenitore…", "", () =>
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

        /// <summary>Breve descrizione del separatore (vuota = linea semplice).</summary>
        void EditSeparator(BarButton b)
        {
            GroupPopup.IsOpen = false;
            var w = new TextPromptWindow("Separatore", "Breve descrizione mostrata accanto alla linea (lascia vuoto per una linea semplice).", b.Label ?? "");
            if (RunDialog(() => w))
            {
                b.Label = (w.Value ?? "").Trim();
                CommitButtons();
            }
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

        static MenuItem Menu(string text, string glyph, Action a) { var mi = new MenuItem { Header = text, Icon = GlyphIcon(glyph) }; mi.Click += (s, e) => a(); return mi; }
        static TextBlock GlyphIcon(string glyph) => new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };

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

        /// <summary>Dopo la ricostruzione dei tasti, lega il popup del contenitore aperto al suo nuovo elemento visivo.</summary>
        void ReanchorGroupPopup()
        {
            var btn = FindBarButtonElement(_openGroup.Id);
            if (btn == null) { GroupPopup.IsOpen = false; return; }
            if (!ReferenceEquals(GroupPopup.PlacementTarget, btn))
            {
                GroupPopup.PlacementTarget = btn;
                // Un cambio di offset forza il popup aperto a ricalcolare la posizione
                GroupPopup.HorizontalOffset += 1; GroupPopup.HorizontalOffset -= 1;
            }
        }

        /// <summary>L'elemento Button della barra che mostra il tasto con quell'Id (null se non c'è).</summary>
        Button FindBarButtonElement(string id)
        {
            ButtonsList.UpdateLayout();
            foreach (var item in ButtonsList.Items)
            {
                var vm = item as ButtonVm;
                if (vm == null || vm.Button.Id != id) continue;
                var container = ButtonsList.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
                return container == null ? null : FirstDescendant<Button>(container);
            }
            return null;
        }

        static T FirstDescendant<T>(DependencyObject d) where T : DependencyObject
        {
            if (d is T t) return t;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var r = FirstDescendant<T>(VisualTreeHelper.GetChild(d, i));
                if (r != null) return r;
            }
            return null;
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
            if (vm == null || vm.IsSeparator || (_drag != null && _drag.Active)) return;
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
            if (b.Separator)
            {
                m.Items.Add(Menu("Descrizione…", "", () => EditSeparator(b)));
                m.Items.Add(MoveToMenu(b));
                m.Items.Add(Menu("Rimuovi il separatore", "", () => { S.DetachButton(b.Id); CommitButtons(); }));
                return;
            }
            m.Items.Add(Menu("Apri", "", () => { GroupPopup.IsOpen = false; Tools.Launch(b); if (!S.Pinned) HideBar(); }));
            if (!IsShellTarget(b)) m.Items.Add(Menu("Apri come amministratore", "", () => Tools.Open(b.Target, b.Arguments, b.WorkingDir, true)));
            AddTargetItems(m.Items, b);
            m.Items.Add(new Separator());
            m.Items.Add(MoveToMenu(b));
            m.Items.Add(Menu("Modifica…", "", () => EditButton(b)));
            m.Items.Add(Menu("Rimuovi", "", () => { S.DetachButton(b.Id); CommitButtons(); }));
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Aggiungi un separatore qui", "", () => AddSeparatorAfter(b)));
        }

        void GroupPopup_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DropEffectFor(e);
            e.Handled = true;
        }

        /// <summary>File (o elementi della shell) rilasciati nel menu aperto: diventano tasti di quel contenitore.</summary>
        void GroupPopup_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DropEffectFor(e);
            if (_openGroup == null || e.Effects == DragDropEffects.None) return;
            var buttons = DroppedButtons(e.Data);
            if (buttons.Count == 0) return;
            _openGroup.Children.AddRange(buttons);
            CommitButtons();
        }

        // ------------------------------------------------------------------ ricerca
        void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!_autoFocus) KeepOpen = true;
            if (SearchBox.Text.Length == 0) SetSearchBorder("Accent");   // con il fuoco il bordo prende il colore di accento
        }

        void SearchBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (SearchBox.Text.Length == 0) SetSearchBorder("ControlStroke");
        }

        void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = SearchBox.Text.Length == 0;
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearSearch.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            UpdateRecentsChevron();
            KeepOpen = true;
            _searchTimer.Stop();
            if (empty) { HideResults(); return; }
            _searchTimer.Start();
        }

        void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Text = ""; SearchBox.Focus(); }

        /// <summary>Il chevron in fondo alla casella: c'è solo con la casella vuota e qualche recente; apre e chiude l'elenco.</summary>
        void UpdateRecentsChevron()
        {
            bool show = SearchBox.Text.Length == 0 && S.Recent.Count > 0;
            RecentsButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RecentsGlyph.Text = _showingRecents && _results.Count > 0 ? "" : "";
        }

        void RecentsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_showingRecents && _results.Count > 0) HideResults();
            else { ShowRecents(); if (_results.Count > 0) ResultsList.SelectedIndex = 0; }
            SearchBox.Focus();
            UpdateRecentsChevron();
        }

        void ShowRecents()
        {
            var list = new List<SearchHit>();
            bool pruned = false;
            foreach (var p in S.Recent.ToList())
            {
                if (p.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) list.Add(new SearchHit { Kind = HitKind.App, Title = AppName(p), Path = p, Subtitle = "App · recente", IconKey = p });
                else if (Directory.Exists(p)) list.Add(new SearchHit { Kind = HitKind.Folder, Title = Path.GetFileName(p.TrimEnd('\\')), Path = p, Subtitle = Path.GetDirectoryName(p) ?? p, IconKey = p });
                else if (File.Exists(p)) { var fi = new FileInfo(p); list.Add(new SearchHit { Kind = HitKind.File, Title = fi.Name, Path = p, Subtitle = fi.DirectoryName, Size = fi.Length, Modified = fi.LastWriteTime, IconKey = p }); }
                else { S.RemoveRecent(p); pruned = true; }   // spostato o eliminato: via dai recenti da solo
                if (list.Count >= 8) break;
            }
            if (pruned) App.Current.SaveSettings();
            _showingRecents = true;
            UpdateRecentsChevron();
            SetSearchBorder(SearchBox.IsKeyboardFocusWithin ? "Accent" : "ControlStroke");
            SetResults(list, false);
            HintText.Text = list.Count > 0 ? "Recenti · Canc rimuove · tasto destro per svuotare" : "";
            if (list.Count == 0) HideResults();
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
            HideResults();
        }

        void SetSearchBorder(string brushKey)
        {
            SearchHost.SetResourceReference(Border.BorderBrushProperty, brushKey);
            SearchHost.BorderThickness = new Thickness(brushKey == "ControlStroke" ? 1 : 1.5);
        }

        /// <summary>Elenco chiuso: niente risultati né recenti (la barra torna alla sola riga dei tasti).</summary>
        void HideResults()
        {
            SetSearchBorder(SearchBox.IsKeyboardFocusWithin ? "Accent" : "ControlStroke");
            _showingRecents = false;
            _resultsCts.Cancel();
            _resultsCts = new System.Threading.CancellationTokenSource();
            _results.Clear();
            Results.Visibility = Visibility.Collapsed;
            UpdateRecentsChevron();
        }

        void RunSearch()
        {
            string text = SearchBox.Text.Trim();
            var words = SearchText.Words(text);
            int serial = ++_searchSerial;
            if (words.Count == 0) { HideResults(); return; }
            var usage = new Dictionary<string, int>(S.Usage, StringComparer.OrdinalIgnoreCase);
            var index = App.Current.Index;
            var apps = App.Current.Apps;
            bool searchApps = S.Index.Apps;
            // Con i filtri attivi si chiede di più all'indice: dopo il filtro deve restare abbastanza
            var filter = new SearchFilter { Kind = S.Filter.Kind, Ext = S.Filter.Ext, SinceDays = S.Filter.SinceDays, NameOnly = S.Filter.NameOnly };
            int want = filter.IsActive ? 400 : 40;
            Task.Run(() =>
            {
                var hits = new List<SearchHit>();
                try
                {
                    if (searchApps) hits.AddRange(apps.Search(words, usage));
                    hits.AddRange(index.Search(words, want, usage));
                }
                catch (Exception ex) { Program.LogError("Ricerca", ex); }
                hits = ApplyFilter(hits, filter, words);
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
                // Il bordo della casella dice com'è andata: verde se c'è qualcosa, rosso se non c'è niente (restano solo «Esegui» e «Cerca sul web»)
                SetSearchBorder(list.Any(h => h.Kind != HitKind.Command) ? "SuccessText" : "ErrorText");
                HintText.Text = filter.IsActive ? "Filtri: " + filter.Describe() + " · Invio apri · Esc chiudi" : "↑↓ scegli · Invio apri · Ctrl+Invio apri la cartella · Esc chiudi";
                UpdateStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        System.Threading.CancellationTokenSource _resultsCts = new System.Threading.CancellationTokenSource();

        void SetResults(List<SearchHit> hits, bool selectFirst)
        {
            // I calcoli in sottofondo dell'elenco precedente (dimensioni delle cartelle) si fermano
            _resultsCts.Cancel();
            _resultsCts = new System.Threading.CancellationTokenSource();
            _results.Clear();
            foreach (var h in hits) _results.Add(new ResultVm(h, _resultsCts.Token));
            Results.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            if (SearchBox.IsKeyboardFocusWithin && Results.Visibility != Visibility.Visible && SearchBox.Text.Length > 0) Results.Visibility = Visibility.Visible;
        }

        static string Shorten(string s, int max) => s.Length <= max ? s : "…" + s.Substring(s.Length - max + 1);

        void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    // Casella vuota: la freccia giù mostra i recenti (non compaiono da soli all'apertura)
                    if (SearchBox.Text.Length == 0 && _results.Count == 0) { ShowRecents(); if (_results.Count > 0) ResultsList.SelectedIndex = 0; }
                    else MoveSelection(1);
                    e.Handled = true; break;
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
            m.Items.Add(Menu("Apri", "", () => OpenHit(item.Hit, false, false)));
            if (item.Hit.Kind != HitKind.App)
            {
                string path = item.Hit.Path;
                bool isFolder = item.Hit.Kind == HitKind.Folder;
                string folder = isFolder ? path : (Path.GetDirectoryName(path) ?? path);
                m.Items.Add(Menu("Apri la cartella", "", () => OpenHit(item.Hit, true, false)));
                if (item.Hit.Kind == HitKind.File && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) m.Items.Add(Menu("Apri come amministratore", "", () => OpenHit(item.Hit, false, true)));
                m.Items.Add(new Separator());
                // Come in Esplora file: negli appunti come file, da incollare ovunque
                m.Items.Add(Menu("Copia", "", () => Tools.CopyFileToClipboard(path, false)));
                m.Items.Add(Menu("Taglia", "", () => Tools.CopyFileToClipboard(path, true)));
                var paste = Menu(isFolder ? "Incolla qui" : "Incolla nella cartella", "", () => { Tools.PasteInto(folder); if (!S.Pinned) HideBar(); });
                paste.IsEnabled = Tools.ClipboardFiles().Count > 0;
                m.Items.Add(paste);
                m.Items.Add(Menu("Copia il percorso", "", () => Tools.CopyToClipboard(path)));
                m.Items.Add(new Separator());
                m.Items.Add(Menu("Proprietà", "", () => { Tools.ShowProperties(path); if (!S.Pinned) HideBar(); }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(Menu("Aggiungi alla barra", "", () =>
            {
                S.Buttons.Add(new BarButton { Label = item.Hit.Kind == HitKind.File ? Path.GetFileNameWithoutExtension(item.Hit.Title) : item.Hit.Title, Target = item.Hit.Path });
                CommitButtons();
            }));
            if (_showingRecents)
            {
                m.Items.Add(new Separator());
                m.Items.Add(Menu("Rimuovi dai recenti", "", () => RemoveRecent(item)));
                m.Items.Add(Menu("Svuota i recenti", "", ClearRecents));
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
