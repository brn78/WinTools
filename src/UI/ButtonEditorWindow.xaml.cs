using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinTools.Search;
using Forms = System.Windows.Forms;

namespace WinTools.UI
{
    /// <summary>
    /// Crea o modifica un tasto della barra. Il risultato finisce nel BarButton passato al costruttore. L'icona può
    /// venire dal file, dal catalogo Material Design (7.000 icone, ricerca in italiano e in inglese, colore a scelta)
    /// o da un'immagine.
    /// </summary>
    public partial class ButtonEditorWindow : Window
    {
        readonly BarButton _button;
        string _iconSpec = "";      // «E8B7» (glifo Segoe) oppure «mdi:nome»
        string _color = "";         // «#RRGGBB», vuoto = accento
        bool _loading = true;
        readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        int _searchSerial;
        ToggleButton _selectedIconTile;

        public ButtonEditorWindow(BarButton button)
        {
            InitializeComponent();
            FluentWindow.Setup(this);
            _button = button;
            Heading.Text = button.Group ? (string.IsNullOrEmpty(button.Label) ? "Nuovo contenitore" : "Modifica contenitore") : string.IsNullOrEmpty(button.Target) ? "Nuovo tasto" : "Modifica tasto";

            // Strumenti di Windows raggruppati
            PresetBox.Items.Add(new ComboBoxItem { Content = "Scegli uno strumento…", IsEnabled = false, Foreground = (Brush)FindResource("TextTertiary") });
            string group = null;
            foreach (var t in Presets.Tools)
            {
                if (t.Group != group)
                {
                    group = t.Group;
                    PresetBox.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextSecondary") });
                }
                PresetBox.Items.Add(new ComboBoxItem { Content = "    " + t.Label, Tag = t });
            }
            PresetBox.SelectedIndex = 0;

            BuildColorSwatches();
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); RunIconSearch(); };

            LabelBox.Text = button.Label;
            TargetBox.Text = button.Target;
            ArgsBox.Text = button.Arguments;
            WorkDirBox.Text = button.WorkingDir;
            AdminBox.IsChecked = button.Admin;
            IconOnlyBox.IsChecked = button.IconOnly;
            _color = button.IconColor ?? "";
            if (IconLook.IsGlyph(button.Icon) || IconLook.IsMdi(button.Icon)) { _iconSpec = button.Icon; IconGlyph.IsChecked = true; }
            else if (!string.IsNullOrEmpty(button.Icon)) { IconFile.IsChecked = true; ImageBox.Text = button.Icon; }
            else IconAuto.IsChecked = true;
            GroupBox.IsChecked = button.Group;
            _loading = false;
            GroupBox_Changed(null, null);
            IconMode_Changed(null, null);
            MarkColorSwatch();
            LabelBox.Focus();
        }

        // ------------------------------------------------------------------ contenitore
        void GroupBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool group = GroupBox.IsChecked == true;
            var hide = group ? Visibility.Collapsed : Visibility.Visible;
            PresetCard.Visibility = hide;
            TargetLabel.Visibility = hide; TargetBox.Visibility = hide; BrowsePanel.Visibility = hide;
            ArgsLabel.Visibility = hide; ArgsBox.Visibility = hide;
            WorkDirLabel.Visibility = hide; WorkDirBox.Visibility = hide;
            AdminBox.Visibility = hide;
            IconAuto.Visibility = hide;
            if (group && IconAuto.IsChecked == true)
            {
                if (string.IsNullOrEmpty(_iconSpec)) _iconSpec = "mdi:folder";
                IconGlyph.IsChecked = true;
            }
            Heading.Text = group ? (string.IsNullOrEmpty(_button.Label) ? "Nuovo contenitore" : "Modifica contenitore") : string.IsNullOrEmpty(_button.Target) ? "Nuovo tasto" : "Modifica tasto";
            UpdatePreview();
        }

        void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var t = (PresetBox.SelectedItem as ComboBoxItem)?.Tag as Presets.Tool;
            if (t == null) return;
            LabelBox.Text = t.Label;
            TargetBox.Text = t.Target;
            ArgsBox.Text = t.Arguments ?? "";
            AdminBox.IsChecked = t.Admin;
            _iconSpec = t.Glyph;
            IconGlyph.IsChecked = true;
            UpdatePreview();
        }

        // ------------------------------------------------------------------ icona
        void IconMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            GlyphPanel.Visibility = IconGlyph.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ImagePanel.Visibility = IconFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (IconGlyph.IsChecked == true)
            {
                if (IconWrap.Children.Count == 0) RunIconSearch();
                // La griglia delle icone sta in fondo: la si porta in vista e il cursore va nella ricerca
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { GlyphPanel.BringIntoView(); if (sender != null) IconSearch.Focus(); }));
            }
            UpdatePreview();
        }

        void Any_Changed(object sender, TextChangedEventArgs e) { if (!_loading) UpdatePreview(); }

        void IconSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            IconHint.Visibility = IconSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        /// <summary>Riempie la griglia con le icone trovate (al massimo 180: oltre, si affina la ricerca).</summary>
        void RunIconSearch()
        {
            string q = IconSearch.Text;
            int serial = ++_searchSerial;
            IconCount.Text = IconCatalog.IsLoaded ? "" : "carico il catalogo…";
            Task.Run(() => { int total; var items = IconCatalog.Search(q, 180, out total); return new { items, total }; }).ContinueWith(t =>
            {
                if (serial != _searchSerial || t.IsFaulted) return;
                IconWrap.Children.Clear();
                _selectedIconTile = null;
                var brush = IconPalette.BrushFor(_color);
                foreach (var icon in t.Result.items)
                {
                    var tile = new ToggleButton
                    {
                        Content = new System.Windows.Shapes.Path { Data = icon.Geometry, Fill = brush, Stretch = Stretch.Uniform, Width = 20, Height = 20 },
                        Width = 36, Height = 36, Margin = new Thickness(2), Tag = icon.Name, ToolTip = icon.Name,
                        Style = (Style)FindResource("GlyphToggle"),
                        IsChecked = string.Equals(_iconSpec, "mdi:" + icon.Name, StringComparison.OrdinalIgnoreCase)
                    };
                    if (tile.IsChecked == true) _selectedIconTile = tile;
                    tile.Checked += (s, a) => { SelectTile((ToggleButton)s); };
                    tile.Unchecked += (s, a) => { if (ReferenceEquals(s, _selectedIconTile)) ((ToggleButton)s).IsChecked = true; };
                    IconWrap.Children.Add(tile);
                }
                int shown = t.Result.items.Count;
                IconCount.Text = t.Result.total == 0 ? "nessuna icona" : shown < t.Result.total ? $"{shown} di {t.Result.total} · affina la ricerca" : $"{shown} {(shown == 1 ? "icona" : "icone")}";
                IconScroll.ScrollToTop();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SelectTile(ToggleButton tile)
        {
            if (_selectedIconTile != null && !ReferenceEquals(_selectedIconTile, tile)) _selectedIconTile.IsChecked = false;
            _selectedIconTile = tile;
            _iconSpec = "mdi:" + (string)tile.Tag;
            UpdatePreview();
        }

        // ------------------------------------------------------------------ colore
        void BuildColorSwatches()
        {
            ColorWrap.Children.Clear();
            foreach (var c in IconPalette.Colors) ColorWrap.Children.Add(MakeSwatch(c.Hex, c.Label));
            var other = new Button { Content = "Altro…", Style = (Style)FindResource("SubtleButton"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 2, 0, 2), ToolTip = "Un colore qualsiasi" };
            other.Click += (s, e) =>
            {
                using (var d = new Forms.ColorDialog { FullOpen = true, AnyColor = true })
                {
                    try { var cur = ((SolidColorBrush)IconPalette.BrushFor(_color)).Color; d.Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B); } catch { }
                    if (d.ShowDialog() != Forms.DialogResult.OK) return;
                    _color = $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}";
                    MarkColorSwatch();
                    RecolorTiles();
                    UpdatePreview();
                }
            };
            ColorWrap.Children.Add(other);
        }

        ToggleButton MakeSwatch(string hex, string label)
        {
            var dot = new Ellipse { Width = 18, Height = 18 };
            if (string.IsNullOrEmpty(hex)) dot.SetResourceReference(Shape.FillProperty, "Accent"); else dot.Fill = IconPalette.BrushFor(hex);
            var tb = new ToggleButton { Content = dot, Width = 30, Height = 30, Margin = new Thickness(2), Tag = hex ?? "", ToolTip = label, Style = (Style)FindResource("GlyphToggle") };
            tb.Checked += (s, e) => { _color = (string)tb.Tag; MarkColorSwatch(); RecolorTiles(); UpdatePreview(); };
            tb.Unchecked += (s, e) => { if (string.Equals((string)tb.Tag, _color, StringComparison.OrdinalIgnoreCase)) tb.IsChecked = true; };
            return tb;
        }

        /// <summary>Evidenzia il campione del colore scelto; un colore fuori palette compare come campione in più.</summary>
        void MarkColorSwatch()
        {
            bool found = false;
            foreach (var child in ColorWrap.Children.OfType<ToggleButton>().ToList())
            {
                bool match = string.Equals((string)child.Tag, _color ?? "", StringComparison.OrdinalIgnoreCase);
                if (match) found = true;
                if (child.IsChecked != match) child.IsChecked = match;
                if (!match && !IconPalette.Colors.Any(c => string.Equals(c.Hex, (string)child.Tag, StringComparison.OrdinalIgnoreCase))) ColorWrap.Children.Remove(child);
            }
            if (!found && !string.IsNullOrEmpty(_color))
            {
                var sw = MakeSwatch(_color, _color);
                ColorWrap.Children.Insert(ColorWrap.Children.Count - 1, sw);
                sw.IsChecked = true;
            }
        }

        void RecolorTiles()
        {
            var brush = IconPalette.BrushFor(_color);
            foreach (var tile in IconWrap.Children.OfType<ToggleButton>()) if (tile.Content is System.Windows.Shapes.Path p) p.Fill = brush;
        }

        // ------------------------------------------------------------------ anteprima
        void UpdatePreview()
        {
            PreviewImage.Source = null;
            PreviewPath.Visibility = Visibility.Collapsed;
            PreviewGlyph.Text = "";
            var brush = IconPalette.BrushFor(_color);
            PreviewGlyph.Foreground = brush;
            if (IconGlyph.IsChecked == true)
            {
                if (IconLook.IsMdi(_iconSpec))
                {
                    var e = IconCatalog.Find(_iconSpec);
                    if (e != null) { PreviewPath.Data = e.Geometry; PreviewPath.Fill = brush; PreviewPath.Visibility = Visibility.Visible; }
                }
                else if (IconLook.IsGlyph(_iconSpec)) PreviewGlyph.Text = ((char)Convert.ToInt32(_iconSpec, 16)).ToString();
                return;
            }
            string key = IconFile.IsChecked == true ? ImageBox.Text.Trim() : Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(TargetBox.Text.Trim()));
            if (key == "") return;
            if (key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) { PreviewGlyph.Text = ""; return; }
            string wanted = key;
            var img = IconCache.Get(key, 28, loaded => { if (loaded != null && wanted == CurrentKey()) PreviewImage.Source = loaded; });
            if (img != null) PreviewImage.Source = img;
            else PreviewGlyph.Text = key.IndexOf(':') > 0 && !System.IO.Path.IsPathRooted(key) ? "" : "";
        }

        string CurrentKey() => IconFile.IsChecked == true ? ImageBox.Text.Trim() : Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(TargetBox.Text.Trim()));

        // ------------------------------------------------------------------ sfoglia
        void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Scegli un programma o un file", Filter = "Programmi e collegamenti|*.exe;*.lnk;*.bat;*.cmd;*.msc;*.cpl|Tutti i file|*.*", CheckFileExists = true };
            if (d.ShowDialog(this) == true)
            {
                TargetBox.Text = d.FileName;
                if (LabelBox.Text.Trim() == "") LabelBox.Text = System.IO.Path.GetFileNameWithoutExtension(d.FileName);
            }
        }

        void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new Forms.FolderBrowserDialog { Description = "Scegli una cartella", ShowNewFolderButton = false })
            {
                if (d.ShowDialog() == Forms.DialogResult.OK)
                {
                    TargetBox.Text = d.SelectedPath;
                    if (LabelBox.Text.Trim() == "") LabelBox.Text = System.IO.Path.GetFileName(d.SelectedPath.TrimEnd('\\'));
                }
            }
        }

        void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Scegli un'immagine o un file con icona", Filter = "Immagini e icone|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|Tutti i file|*.*", CheckFileExists = true };
            if (d.ShowDialog(this) == true) { ImageBox.Text = d.FileName; UpdatePreview(); }
        }

        // ------------------------------------------------------------------ salva
        void Ok_Click(object sender, RoutedEventArgs e)
        {
            string iconSpec = IconGlyph.IsChecked == true ? _iconSpec : IconFile.IsChecked == true ? ImageBox.Text.Trim() : "";
            if (GroupBox.IsChecked == true)
            {
                string name = LabelBox.Text.Trim();
                if (name == "")
                {
                    MessageBox.Show(this, "Dai un nome al contenitore.", Program.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    LabelBox.Focus();
                    return;
                }
                _button.Group = true;
                _button.Label = name;
                _button.Target = ""; _button.Arguments = ""; _button.WorkingDir = ""; _button.Admin = false;
                _button.IconOnly = IconOnlyBox.IsChecked == true;
                _button.Icon = IconFile.IsChecked == true ? iconSpec : (string.IsNullOrEmpty(iconSpec) ? "mdi:folder" : iconSpec);
                _button.IconColor = _color ?? "";
                DialogResult = true;
                return;
            }
            _button.Group = false;
            string target = TargetBox.Text.Trim();
            if (target == "")
            {
                MessageBox.Show(this, "Scrivi che cosa deve aprire il tasto.", Program.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                TargetBox.Focus();
                return;
            }
            _button.Label = LabelBox.Text.Trim();
            if (_button.Label == "") _button.Label = target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? target : System.IO.Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
            _button.Target = target;
            _button.Arguments = ArgsBox.Text.Trim();
            _button.WorkingDir = WorkDirBox.Text.Trim();
            _button.Admin = AdminBox.IsChecked == true;
            _button.IconOnly = IconOnlyBox.IsChecked == true;
            _button.Icon = iconSpec;
            _button.IconColor = _color ?? "";
            DialogResult = true;
        }
    }
}
