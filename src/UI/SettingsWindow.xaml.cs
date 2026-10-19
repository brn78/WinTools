using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinTools.UI
{
    /// <summary>Riga dell'elenco dei tasti nelle impostazioni: un tasto della barra o uno dentro un contenitore (rientrato).</summary>
    public sealed class ButtonRow : ButtonVmBase
    {
        public List<BarButton> Owner { get; }
        public bool Nested { get; }
        public ButtonRow(BarButton b, List<BarButton> owner, bool nested) : base(b) { Owner = owner; Nested = nested; }
        public new string Label => (Nested ? "      ↳ " : "") + (Button.Separator ? "────────  separatore  ────────" : base.Label);
        public string Flags => string.Join(" · ", new[]
        {
            Button.Separator ? "separatore" : "",
            Button.Group ? $"contenitore · {Button.Children.Count} {(Button.Children.Count == 1 ? "tasto" : "tasti")}" : "",
            Button.Admin ? "amministratore" : "", Button.IconOnly ? "solo icona" : ""
        }.Where(s => s != ""));
    }

    /// <summary>Base comune per mostrare un tasto (icona o glifo) fuori dalla barra.</summary>
    public class ButtonVmBase : System.ComponentModel.INotifyPropertyChanged
    {
        public BarButton Button { get; }
        System.Windows.Media.ImageSource _icon;
        public ButtonVmBase(BarButton b)
        {
            Button = b;
            var look = IconLook.For(b);
            Glyph = b.Separator ? "" : look.Glyph;
            Mdi = look.Mdi;
            IconBrush = look.Brush;
            if (!string.IsNullOrEmpty(look.ImageKey))
                _icon = IconCache.Get(look.ImageKey, 24, img => { if (img != null) { _icon = img; Raise("Icon"); Raise("ImageVisibility"); Raise("GlyphVisibility"); Raise("MdiVisibility"); } });
        }
        public string Label => string.IsNullOrEmpty(Button.Label) ? Button.Target : Button.Label;
        public string Glyph { get; }
        public Geometry Mdi { get; }
        public Brush IconBrush { get; }
        public System.Windows.Media.ImageSource Icon => _icon;
        public Visibility ImageVisibility => _icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MdiVisibility => Mdi != null && _icon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => _icon == null && Mdi == null ? Visibility.Visible : Visibility.Collapsed;
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void Raise(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
    }

    /// <summary>Finestra delle impostazioni, nello stile di WinOptima: navigazione a sinistra, card a destra. Lavora su una copia e applica con Salva.</summary>
    public partial class SettingsWindow : Window
    {
        AppSettings _work;
        readonly ObservableCollection<ButtonRow> _rows = new ObservableCollection<ButtonRow>();
        readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        string _hotkey;

        public SettingsWindow()
        {
            InitializeComponent();
            FluentWindow.Setup(this);
            NavLogo.Source = App.WindowIcon();
            AboutLogo.Source = NavLogo.Source;
            NavVersion.Text = "v" + Program.Version;
            AboutVersion.Text = "v" + Program.Version;
            ButtonsBox.ItemsSource = _rows;
            Load();
            GoTo("general");
            _statusTimer.Tick += (s, e) => UpdateIndexStatus();
            _statusTimer.Start();
            Closed += (s, e) => _statusTimer.Stop();
            UpdateIndexStatus();
            UpdateUpdateState();
            App.Current.Updates.UpdateReady += OnUpdateReady;
            Closed += (s, e) => App.Current.Updates.UpdateReady -= OnUpdateReady;
        }

        void OnUpdateReady(Updater.Info i) { Dispatcher.BeginInvoke(new Action(UpdateUpdateState)); }

        void Load()
        {
            // Copia profonda: le modifiche non toccano l'app finché non si salva
            var js = new JavaScriptSerializer();
            _work = js.Deserialize<AppSettings>(js.Serialize(App.Current.Settings));
            Select(ThemeBox, _work.Theme);
            _accent = _work.AccentColor ?? "";
            UpdateAccentSwatch();
            Select(EdgeBox, _work.Edge);
            Select(MonitorBox, _work.Monitor);
            FullWidthBox.IsChecked = _work.FullWidth;
            LabelsBox.IsChecked = _work.ShowLabels;
            EdgeTriggerBox.IsChecked = _work.EdgeTrigger;
            FocusEdgeBox.IsChecked = _work.FocusOnEdge;
            EdgeDelayBox.Text = _work.EdgeDelayMs.ToString();
            HideDelayBox.Text = _work.HideDelayMs.ToString();
            _hotkey = _work.Hotkey;
            HotkeyBox.Text = string.IsNullOrEmpty(_hotkey) ? "Nessuno" : _hotkey;
            PinnedBox.IsChecked = _work.Pinned;
            AutoStartBox.IsChecked = _work.AutoStart;
            AutoUpdateBox.IsChecked = _work.AutoUpdate;
            IndexEnabledBox.IsChecked = _work.Index.Enabled;
            RootsBox.Text = string.Join(Environment.NewLine, _work.Index.Roots);
            ExcludeBox.Text = string.Join(Environment.NewLine, _work.Index.Exclude);
            HiddenBox.IsChecked = _work.Index.IncludeHidden;
            AppsBox.IsChecked = _work.Index.Apps;
            RescanHoursBox.Text = _work.Index.RescanHours.ToString();
            StartupDelayBox.Text = _work.Index.StartupDelayMinutes.ToString();
            RecentText.Text = App.Current.Settings.Recent.Count == 0 ? "Recenti: nessuno" : $"Recenti: {App.Current.Settings.Recent.Count}";
            ClearRecentButton.IsEnabled = App.Current.Settings.Recent.Count > 0;
            RefreshRows();
        }

        void ClearRecent_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Settings.ClearRecents();
            App.Current.SaveSettings();
            RecentText.Text = "Recenti: nessuno";
            ClearRecentButton.IsEnabled = false;
        }

        static void Select(ComboBox box, string tag)
        {
            foreach (ComboBoxItem i in box.Items) if ((string)i.Tag == tag) { box.SelectedItem = i; return; }
            box.SelectedIndex = 0;
        }

        static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

        void RefreshRows()
        {
            int sel = ButtonsBox.SelectedIndex;
            _rows.Clear();
            foreach (var b in _work.Buttons)
            {
                _rows.Add(new ButtonRow(b, _work.Buttons, false));
                if (b.Group) foreach (var c in b.Children) _rows.Add(new ButtonRow(c, b.Children, true));
            }
            if (sel >= 0 && sel < _rows.Count) ButtonsBox.SelectedIndex = sel;
        }

        void SelectButton(BarButton b)
        {
            var row = _rows.FirstOrDefault(r => r.Button == b);
            if (row != null) { ButtonsBox.SelectedItem = row; ButtonsBox.ScrollIntoView(row); }
        }

        // ------------------------------------------------------------------ navigazione
        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageGeneral == null) return;
            GoTo((string)((RadioButton)sender).Tag);
        }

        public void GoTo(string page)
        {
            foreach (RadioButton r in Nav.Children) if ((string)r.Tag == page && r.IsChecked != true) { r.IsChecked = true; return; }
            PageGeneral.Visibility = page == "general" ? Visibility.Visible : Visibility.Collapsed;
            PageButtons.Visibility = page == "buttons" ? Visibility.Visible : Visibility.Collapsed;
            PageSearch.Visibility = page == "search" ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
            switch (page)
            {
                case "buttons": PageTitle.Text = "Tasti della barra"; PageSubtitle.Text = "I tasti nell'ordine in cui compaiono. Trascinali per riordinarli o per metterli dentro (o fuori da) un contenitore; doppio clic per modificarli."; break;
                case "search": PageTitle.Text = "Ricerca e indice"; PageSubtitle.Text = "Che cosa trova la ricerca e dove legge. L'indice si aggiorna da solo in sottofondo."; break;
                case "about": PageTitle.Text = "Informazioni e aggiornamenti"; PageSubtitle.Text = "Versione, aggiornamenti automatici e cartella dei dati."; break;
                default: PageTitle.Text = "Generale"; PageSubtitle.Text = "Aspetto della barra, quando compare, tasto rapido e avvio con Windows."; break;
            }
        }

        // ------------------------------------------------------------------ generale
        void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
            if (key == Key.Escape) { return; }
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None && !(key >= Key.F1 && key <= Key.F24))
            {
                HotkeyBox.ToolTip = "Serve almeno un modificatore (Ctrl, Alt, Shift o Win), oppure un tasto funzione.";
                return;
            }
            _hotkey = Tools.FormatHotkey(mods, key);
            HotkeyBox.Text = _hotkey;
        }

        void HotkeyClear_Click(object sender, RoutedEventArgs e) { _hotkey = ""; HotkeyBox.Text = "Nessuno"; }

        // ------------------------------------------------------------------ colore di accento
        string _accent = "";

        void UpdateAccentSwatch()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_accent)) { AccentSwatch.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Accent"); AccentSwatch.ToolTip = "Colore di Windows"; }
                else { AccentSwatch.Background = new System.Windows.Media.SolidColorBrush(Theme.ColorFromHex(_accent)); AccentSwatch.ToolTip = _accent; }
            }
            catch { _accent = ""; AccentSwatch.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Accent"); }
            AccentResetButton.Visibility = string.IsNullOrWhiteSpace(_accent) ? Visibility.Collapsed : Visibility.Visible;
        }

        void PickAccent_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                try
                {
                    var c = string.IsNullOrWhiteSpace(_accent) ? Theme.Accent : Theme.ColorFromHex(_accent);
                    d.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                }
                catch { }
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _accent = $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}";
                UpdateAccentSwatch();
            }
        }

        void ResetAccent_Click(object sender, RoutedEventArgs e) { _accent = ""; UpdateAccentSwatch(); }

        // ------------------------------------------------------------------ tasti
        void AddButton_Click(object sender, RoutedEventArgs e) { AddNew(false); }
        void AddGroup_Click(object sender, RoutedEventArgs e) { AddNew(true); }

        /// <summary>Un separatore dopo la riga selezionata (nello stesso elenco), oppure in fondo alla barra.</summary>
        void AddSeparator_Click(object sender, RoutedEventArgs e)
        {
            var sep = new BarButton { Separator = true };
            var sel = ButtonsBox.SelectedItem as ButtonRow;
            if (sel != null && sel.Button.Group) sel.Button.Children.Add(sep);
            else if (sel != null) sel.Owner.Insert(sel.Owner.IndexOf(sel.Button) + 1, sep);
            else _work.Buttons.Add(sep);
            RefreshRows();
            SelectButton(sep);
        }

        void AddNew(bool group)
        {
            var b = new BarButton { Group = group, Icon = group ? "E8B7" : "" };
            var w = new ButtonEditorWindow(b) { Owner = this };
            if (w.ShowDialog() != true) return;
            // Un tasto nuovo dentro il contenitore selezionato, se ce n'è uno; altrimenti in fondo alla barra
            var sel = ButtonsBox.SelectedItem as ButtonRow;
            if (!group && sel != null && sel.Button.Group) sel.Button.Children.Add(b);
            else if (!group && sel != null && sel.Nested) sel.Owner.Add(b);
            else _work.Buttons.Add(b);
            RefreshRows();
            SelectButton(b);
        }

        void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var row = ButtonsBox.SelectedItem as ButtonRow;
            if (row == null) return;
            var copy = row.Button.Clone();
            var w = new ButtonEditorWindow(copy) { Owner = this };
            if (w.ShowDialog() == true)
            {
                int i = row.Owner.IndexOf(row.Button);
                if (i < 0) return;
                row.Owner[i] = copy;
                if (row.Nested && copy.Group)
                {
                    // Un tasto dentro un contenitore non può diventare a sua volta contenitore: sale sulla barra
                    row.Owner.RemoveAt(i);
                    _work.Buttons.Add(copy);
                }
                else if (!row.Nested && !copy.Group && copy.Children.Count > 0)
                {
                    _work.Buttons.InsertRange(Math.Min(_work.Buttons.Count, i + 1), copy.Children);
                    copy.Children = new List<BarButton>();
                }
                RefreshRows();
                SelectButton(copy);
            }
        }

        void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var row = ButtonsBox.SelectedItem as ButtonRow;
            if (row == null) return;
            int sel = ButtonsBox.SelectedIndex;
            int i = row.Owner.IndexOf(row.Button);
            row.Owner.Remove(row.Button);
            // Via un contenitore: i suoi tasti tornano sulla barra al suo posto
            if (row.Button.Group && row.Button.Children.Count > 0) _work.Buttons.InsertRange(Math.Min(_work.Buttons.Count, Math.Max(0, i)), row.Button.Children);
            RefreshRows();
            if (_rows.Count > 0) ButtonsBox.SelectedIndex = Math.Min(sel, _rows.Count - 1);
        }

        // ------------------------------------------------------------------ trascinamento delle righe
        // Come sulla barra: il mouse resta catturato dall'elenco, un fantasma segue il cursore, una linea mostra dove
        // finirà la riga (nella barra o dentro il contenitore in cui sta cadendo), una cornice evidenzia il contenitore
        // quando ci si lascia sopra. Al rilascio si sposta davvero; fuori dall'elenco non cambia niente.
        sealed class RowDrag
        {
            public ButtonRow Row;
            public Point Start;
            public bool Active;
            public List<BarButton> TargetList;
            public int TargetIndex = -1;
            public BarButton IntoGroup;
        }
        RowDrag _rowDrag;

        ButtonRow RowAt(DependencyObject d)
        {
            while (d != null && !(d is ListBoxItem)) d = VisualTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext as ButtonRow;
        }

        void Rows_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var row = RowAt(e.OriginalSource as DependencyObject);
            _rowDrag = row == null ? null : new RowDrag { Row = row, Start = e.GetPosition(ButtonsBox) };
        }

        void Rows_MouseMove(object sender, MouseEventArgs e)
        {
            var d = _rowDrag;
            if (d == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { if (d.Active) EndRowDrag(false); else _rowDrag = null; return; }
            var p = e.GetPosition(ButtonsBox);
            if (!d.Active)
            {
                if (Math.Abs(p.X - d.Start.X) < 6 && Math.Abs(p.Y - d.Start.Y) < 6) return;
                d.Active = true;
                ButtonsBox.CaptureMouse();
                RowGhostText.Text = d.Row.Label.Trim();
                RowGhost.IsOpen = true;
                Mouse.OverrideCursor = Cursors.Hand;
            }
            UpdateRowDrag(d, p);
        }

        void Rows_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var d = _rowDrag;
            if (d == null) return;
            if (!d.Active) { _rowDrag = null; return; }
            UpdateRowDrag(d, e.GetPosition(ButtonsBox));
            e.Handled = true;
            EndRowDrag(true);
        }

        void Rows_LostCapture(object sender, MouseEventArgs e) { if (_rowDrag != null && _rowDrag.Active) EndRowDrag(false); }

        void UpdateRowDrag(RowDrag d, Point p)
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var sp = ButtonsBox.PointToScreen(p);
            RowGhost.HorizontalOffset = sp.X / scale + 14;
            RowGhost.VerticalOffset = sp.Y / scale + 10;

            d.TargetList = null; d.TargetIndex = -1; d.IntoGroup = null;
            RowDropLine.Visibility = Visibility.Collapsed;
            RowDropBox.Visibility = Visibility.Collapsed;
            if (p.X < 0 || p.X > ButtonsBox.ActualWidth || p.Y < -20 || p.Y > ButtonsBox.ActualHeight + 20) return;

            var moving = d.Row.Button;
            var host = (FrameworkElement)RowDropLine.Parent;
            // La riga sotto il mouse (o l'ultima, se si è più in basso)
            for (int i = 0; i < _rows.Count; i++)
            {
                var el = ButtonsBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (el == null) continue;
                var top = el.TranslatePoint(new Point(0, 0), ButtonsBox).Y;
                double h = el.ActualHeight;
                bool last = i == _rows.Count - 1;
                if (p.Y >= top + h && !last) continue;
                var row = _rows[i];
                double rel = (p.Y - top) / Math.Max(1, h);
                if (p.Y >= top + h) rel = 1;   // sotto l'ultima riga
                // Dentro un contenitore: la parte centrale della sua riga (i contenitori non entrano in altri contenitori)
                if (row.Button.Group && !moving.Group && row.Button != moving && rel > 0.25 && rel < 0.75)
                {
                    d.IntoGroup = row.Button;
                    var y0 = el.TranslatePoint(new Point(0, 0), host).Y;
                    RowDropBox.Margin = new Thickness(4, y0, 4, 0);
                    RowDropBox.Height = h;
                    RowDropBox.Visibility = Visibility.Visible;
                    return;
                }
                bool before = rel < 0.5;
                var list = row.Owner;
                int idx = list.IndexOf(row.Button) + (before ? 0 : 1);
                // Un contenitore (o un tasto lasciato subito sotto l'intestazione di un contenitore aperto) va a livello della barra
                if (moving.Group && list != _work.Buttons)
                {
                    var parent = _work.Buttons.First(g => g.Children == list);
                    list = _work.Buttons; idx = _work.Buttons.IndexOf(parent) + 1;
                }
                else if (!before && row.Button.Group && list == _work.Buttons && !moving.Group)
                {
                    // Subito sotto l'intestazione di un contenitore: dentro, in prima posizione
                    list = row.Button.Children; idx = 0;
                }
                d.TargetList = list; d.TargetIndex = idx;
                double ly = el.TranslatePoint(new Point(0, before ? 0 : h), host).Y - 1;
                bool nested = list != _work.Buttons;
                RowDropLine.Margin = new Thickness(nested ? 48 : 8, Math.Max(0, ly), 8, 0);
                RowDropLine.Visibility = Visibility.Visible;
                return;
            }
            // Elenco vuoto: in fondo alla barra
            d.TargetList = _work.Buttons; d.TargetIndex = _work.Buttons.Count;
        }

        void EndRowDrag(bool commit)
        {
            var d = _rowDrag;
            _rowDrag = null;
            if (d == null) return;
            RowGhost.IsOpen = false;
            RowDropLine.Visibility = Visibility.Collapsed;
            RowDropBox.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            if (ButtonsBox.IsMouseCaptured) ButtonsBox.ReleaseMouseCapture();
            if (!commit) return;
            var b = d.Row.Button;
            if (d.IntoGroup != null)
            {
                if (d.IntoGroup.Children.Contains(b)) return;
                d.Row.Owner.Remove(b);
                d.IntoGroup.Children.Add(b);
            }
            else if (d.TargetList != null && d.TargetIndex >= 0)
            {
                var list = d.TargetList;
                int from = d.Row.Owner.IndexOf(b);
                int to = d.TargetIndex;
                if (ReferenceEquals(list, d.Row.Owner) && (to == from || to == from + 1)) return;   // lasciata dov'era
                d.Row.Owner.Remove(b);
                if (ReferenceEquals(list, d.Row.Owner) && to > from) to--;
                list.Insert(Math.Max(0, Math.Min(list.Count, to)), b);
            }
            else return;
            RefreshRows();
            SelectButton(b);
        }

        void UpButton_Click(object sender, RoutedEventArgs e) { Move(-1); }
        void DownButton_Click(object sender, RoutedEventArgs e) { Move(1); }

        /// <summary>Sposta il tasto selezionato di un posto, dentro l'elenco in cui sta (barra o contenitore).</summary>
        void Move(int delta)
        {
            var row = ButtonsBox.SelectedItem as ButtonRow;
            if (row == null) return;
            var list = row.Owner;
            int i = list.IndexOf(row.Button);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list.RemoveAt(i);
            list.Insert(j, row.Button);
            RefreshRows();
            SelectButton(row.Button);
        }

        void ResetButtons_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Sostituire i tasti attuali con quelli predefiniti?", Program.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _work.Buttons = AppSettings.DefaultButtons();
            RefreshRows();
        }

        // ------------------------------------------------------------------ indice
        void UpdateIndexStatus()
        {
            var st = App.Current.Index.Status;
            if (st.Scanning)
            {
                IndexStatusText.Text = $"Lettura in corso: {Tools.FormatCount(st.Seen)} elementi";
                IndexDetailText.Text = st.Current;
                IndexProgress.Visibility = Visibility.Visible;
                StopScanButton.IsEnabled = true;
                RescanButton.IsEnabled = false;
            }
            else
            {
                IndexStatusText.Text = st.Entries > 0 ? $"{Tools.FormatCount(st.Entries)} file e cartelle nell'indice" : "Indice vuoto";
                var parts = new List<string>();
                if (st.LastScan.Year > 2000) parts.Add("ultima lettura completa " + Tools.FormatDate(st.LastScan) + (st.LastScanSeconds > 0 ? $" in {st.LastScanSeconds:0} s" : ""));
                if (st.MemoryBytes > 0) parts.Add("circa " + Tools.FormatSize(st.MemoryBytes) + " in memoria");
                if (App.Current.Apps.Count > 0) parts.Add($"{App.Current.Apps.Count} app del menu Start");
                if (!string.IsNullOrEmpty(st.Message)) parts.Add(st.Message);
                IndexDetailText.Text = string.Join(" · ", parts);
                IndexProgress.Visibility = Visibility.Collapsed;
                StopScanButton.IsEnabled = false;
                RescanButton.IsEnabled = App.Current.Settings.Index.Enabled;
            }
        }

        void Rescan_Click(object sender, RoutedEventArgs e) { App.Current.Index.Rescan(); UpdateIndexStatus(); }
        void StopScan_Click(object sender, RoutedEventArgs e) { App.Current.Index.CancelScan(); }
        void ResetExcludes_Click(object sender, RoutedEventArgs e) { ExcludeBox.Text = string.Join(Environment.NewLine, IndexSettings.DefaultExcludes()); }

        void BrowseRoot_Click(object sender, RoutedEventArgs e) { AppendFolder(RootsBox, "Scegli una cartella da indicizzare"); }
        void BrowseExclude_Click(object sender, RoutedEventArgs e) { AppendFolder(ExcludeBox, "Scegli una cartella da saltare"); }

        /// <summary>Finestra di scelta della cartella: il percorso scelto si aggiunge in fondo all'elenco (una riga per cartella).</summary>
        void AppendFolder(TextBox box, string title)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = title, ShowNewFolderButton = false })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string path = d.SelectedPath.TrimEnd('\\');
                if (path.Length == 2 && path[1] == ':') path += "\\";   // la radice di un'unità resta «C:\»
                var lines = Lines(box.Text);
                if (lines.Any(l => string.Equals(l.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return;
                lines.Add(path);
                box.Text = string.Join(Environment.NewLine, lines);
                box.ScrollToEnd();
            }
        }

        // ------------------------------------------------------------------ informazioni
        void GitHub_Click(object sender, RoutedEventArgs e) { Tools.Open("https://github.com/brn78"); }
        void Repo_Click(object sender, RoutedEventArgs e) { Tools.Open(Program.RepoUrl); }
        void Releases_Click(object sender, RoutedEventArgs e) { Tools.Open(Program.RepoUrl + "/releases"); }
        void DataFolder_Click(object sender, RoutedEventArgs e) { Tools.Open(AppSettings.AppDir); }
        void Log_Click(object sender, RoutedEventArgs e)
        {
            if (System.IO.File.Exists(AppSettings.ErrorLog)) Tools.Open(AppSettings.ErrorLog);
            else MessageBox.Show(this, "Nessun errore registrato.", Program.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void UpdateUpdateState()
        {
            var u = App.Current.Updates;
            if (!string.IsNullOrEmpty(u.ReadyPath) && u.Available != null)
            {
                UpdateStateText.Text = $"WinTools {u.Available.Version} è pronto.";
                UpdateStateDesc.Text = "Verrà installato alla chiusura, oppure subito con un riavvio.";
                InstallUpdateButton.Visibility = Visibility.Visible;
            }
            else if (u.Available != null)
            {
                UpdateStateText.Text = $"Disponibile WinTools {u.Available.Version}: download in corso…";
                UpdateStateDesc.Text = "";
            }
            else
            {
                UpdateStateText.Text = "Controlla adesso se c'è una versione più recente.";
                UpdateStateDesc.Text = u.LastCheck.Year > 2000 ? "Ultimo controllo: " + Tools.FormatDate(u.LastCheck) : "";
            }
        }

        async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStateText.Text = "Controllo in corso…";
            Action<bool, string> done = null;
            var tcs = new TaskCompletionSource<Tuple<bool, string>>();
            done = (ok, msg) => tcs.TrySetResult(Tuple.Create(ok, msg));
            App.Current.Updates.CheckFinished += done;
            try
            {
                await App.Current.Updates.CheckAsync(true);
                if (App.Current.Updates.Available == null)
                {
                    var r = await Task.WhenAny(tcs.Task, Task.Delay(500));
                    if (r == tcs.Task) { UpdateStateText.Text = tcs.Task.Result.Item1 ? "WinTools è aggiornato." : "Controllo non riuscito."; UpdateStateDesc.Text = tcs.Task.Result.Item2; }
                    else UpdateUpdateState();
                }
                else UpdateUpdateState();
            }
            finally
            {
                App.Current.Updates.CheckFinished -= done;
                CheckUpdateButton.IsEnabled = true;
            }
        }

        void InstallUpdate_Click(object sender, RoutedEventArgs e) { App.Current.RestartAndUpdate(); }

        // ------------------------------------------------------------------ salva
        void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Current.Settings;
            var oldIndex = new JavaScriptSerializer().Serialize(s.Index);
            bool oldApps = s.Index.Apps;

            s.Theme = Selected(ThemeBox) ?? "system";
            s.AccentColor = _accent ?? "";
            s.Edge = Selected(EdgeBox) ?? "top";
            s.Monitor = Selected(MonitorBox) ?? "cursor";
            s.FullWidth = FullWidthBox.IsChecked == true;
            s.ShowLabels = LabelsBox.IsChecked == true;
            s.EdgeTrigger = EdgeTriggerBox.IsChecked == true;
            s.FocusOnEdge = FocusEdgeBox.IsChecked == true;
            int n;
            if (int.TryParse(EdgeDelayBox.Text.Trim(), out n)) s.EdgeDelayMs = Math.Max(0, Math.Min(2000, n));
            if (int.TryParse(HideDelayBox.Text.Trim(), out n)) s.HideDelayMs = Math.Max(100, Math.Min(5000, n));
            s.Hotkey = _hotkey ?? "";
            s.Pinned = PinnedBox.IsChecked == true;
            s.AutoStart = AutoStartBox.IsChecked == true;
            s.AutoUpdate = AutoUpdateBox.IsChecked == true;
            s.Buttons = _work.Buttons;

            s.Index.Enabled = IndexEnabledBox.IsChecked == true;
            s.Index.Roots = Lines(RootsBox.Text);
            s.Index.Exclude = Lines(ExcludeBox.Text);
            s.Index.IncludeHidden = HiddenBox.IsChecked == true;
            s.Index.Apps = AppsBox.IsChecked == true;
            if (int.TryParse(RescanHoursBox.Text.Trim(), out n)) s.Index.RescanHours = Math.Max(0, Math.Min(720, n));
            if (int.TryParse(StartupDelayBox.Text.Trim(), out n)) s.Index.StartupDelayMinutes = Math.Max(0, Math.Min(120, n));

            var newIndex = new JavaScriptSerializer().Serialize(s.Index);
            bool indexChanged = newIndex != oldIndex;
            // Il periodo di rilettura e le app non richiedono di rifare l'indice
            if (indexChanged)
            {
                var a = new JavaScriptSerializer().Deserialize<IndexSettings>(oldIndex);
                var b = new JavaScriptSerializer().Deserialize<IndexSettings>(newIndex);
                a.RescanHours = b.RescanHours; a.Apps = b.Apps; a.StartupDelayMinutes = b.StartupDelayMinutes;
                indexChanged = new JavaScriptSerializer().Serialize(a) != new JavaScriptSerializer().Serialize(b);
            }
            App.Current.ApplySettings(indexChanged, s.Index.Apps != oldApps);
            if (s.Pinned) App.Current.ShowBar(false);
            FooterNote.Text = "Salvato.";
            Load();
        }

        static List<string> Lines(string text) => (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l != "").ToList();

        void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
