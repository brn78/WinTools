using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace WinTools.UI
{
    /// <summary>Crea o modifica un tasto della barra. Il risultato finisce nel BarButton passato al costruttore.</summary>
    public partial class ButtonEditorWindow : Window
    {
        readonly BarButton _button;
        string _glyph = "";
        bool _loading = true;

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

            foreach (var g in Presets.Glyphs)
            {
                var tb = new ToggleButton
                {
                    Content = new TextBlock { Text = ((char)Convert.ToInt32(g, 16)).ToString(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Width = 36, Height = 36, Margin = new Thickness(2), Tag = g, ToolTip = g,
                    Style = (Style)FindResource("GlyphToggle")
                };
                tb.Checked += (s, e) => { foreach (ToggleButton o in GlyphWrap.Children) if (o != tb) o.IsChecked = false; _glyph = g; UpdatePreview(); };
                GlyphWrap.Children.Add(tb);
            }

            LabelBox.Text = button.Label;
            TargetBox.Text = button.Target;
            ArgsBox.Text = button.Arguments;
            WorkDirBox.Text = button.WorkingDir;
            AdminBox.IsChecked = button.Admin;
            IconOnlyBox.IsChecked = button.IconOnly;
            if (ButtonVm.IsGlyph(button.Icon)) { _glyph = button.Icon; IconGlyph.IsChecked = true; foreach (ToggleButton o in GlyphWrap.Children) o.IsChecked = (string)o.Tag == button.Icon; }
            else if (!string.IsNullOrEmpty(button.Icon)) { IconFile.IsChecked = true; ImageBox.Text = button.Icon; }
            else IconAuto.IsChecked = true;
            GroupBox.IsChecked = button.Group;
            _loading = false;
            GroupBox_Changed(null, null);
            IconMode_Changed(null, null);
            LabelBox.Focus();
        }

        /// <summary>Contenitore: niente bersaglio, argomenti o amministratore; l'icona è un simbolo (o un'immagine).</summary>
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
                if (!ButtonVm.IsGlyph(_glyph)) _glyph = "E8B7";
                IconGlyph.IsChecked = true;
                foreach (ToggleButton o in GlyphWrap.Children) o.IsChecked = (string)o.Tag == _glyph;
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
            _glyph = t.Glyph;
            IconGlyph.IsChecked = true;
            foreach (ToggleButton o in GlyphWrap.Children) o.IsChecked = (string)o.Tag == t.Glyph;
            UpdatePreview();
        }

        void IconMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            GlyphPanel.Visibility = IconGlyph.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ImagePanel.Visibility = IconFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        void Any_Changed(object sender, TextChangedEventArgs e) { if (!_loading) UpdatePreview(); }

        void UpdatePreview()
        {
            PreviewImage.Source = null;
            PreviewGlyph.Text = "";
            if (IconGlyph.IsChecked == true)
            {
                if (ButtonVm.IsGlyph(_glyph)) PreviewGlyph.Text = ((char)Convert.ToInt32(_glyph, 16)).ToString();
                return;
            }
            string key = IconFile.IsChecked == true ? ImageBox.Text.Trim() : Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(TargetBox.Text.Trim()));
            if (key == "") return;
            if (key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) { PreviewGlyph.Text = ""; return; }
            string wanted = key;
            var img = IconCache.Get(key, 28, loaded => { if (loaded != null && wanted == CurrentKey()) PreviewImage.Source = loaded; });
            if (img != null) PreviewImage.Source = img;
            else PreviewGlyph.Text = key.IndexOf(':') > 0 && !Path.IsPathRooted(key) ? "" : "";
        }

        string CurrentKey() => IconFile.IsChecked == true ? ImageBox.Text.Trim() : Tools.ResolveShortcut(Environment.ExpandEnvironmentVariables(TargetBox.Text.Trim()));

        void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Scegli un programma o un file", Filter = "Programmi e collegamenti|*.exe;*.lnk;*.bat;*.cmd;*.msc;*.cpl|Tutti i file|*.*", CheckFileExists = true };
            if (d.ShowDialog(this) == true)
            {
                TargetBox.Text = d.FileName;
                if (LabelBox.Text.Trim() == "") LabelBox.Text = Path.GetFileNameWithoutExtension(d.FileName);
            }
        }

        void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new Forms.FolderBrowserDialog { Description = "Scegli una cartella", ShowNewFolderButton = false })
            {
                if (d.ShowDialog() == Forms.DialogResult.OK)
                {
                    TargetBox.Text = d.SelectedPath;
                    if (LabelBox.Text.Trim() == "") LabelBox.Text = Path.GetFileName(d.SelectedPath.TrimEnd('\\'));
                }
            }
        }

        void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Scegli un'immagine o un file con icona", Filter = "Immagini e icone|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|Tutti i file|*.*", CheckFileExists = true };
            if (d.ShowDialog(this) == true) { ImageBox.Text = d.FileName; UpdatePreview(); }
        }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
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
                _button.Icon = IconFile.IsChecked == true ? ImageBox.Text.Trim() : (ButtonVm.IsGlyph(_glyph) ? _glyph : "E8B7");
                DialogResult = true;
                return;
            }
            // Un contenitore che torna tasto normale: i tasti che aveva dentro restano con chi lo modifica (BarWindow li rimette sulla barra)
            _button.Group = false;
            string target = TargetBox.Text.Trim();
            if (target == "")
            {
                MessageBox.Show(this, "Scrivi che cosa deve aprire il tasto.", Program.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                TargetBox.Focus();
                return;
            }
            _button.Label = LabelBox.Text.Trim();
            if (_button.Label == "") _button.Label = target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? target : Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
            _button.Target = target;
            _button.Arguments = ArgsBox.Text.Trim();
            _button.WorkingDir = WorkDirBox.Text.Trim();
            _button.Admin = AdminBox.IsChecked == true;
            _button.IconOnly = IconOnlyBox.IsChecked == true;
            _button.Icon = IconGlyph.IsChecked == true ? _glyph : IconFile.IsChecked == true ? ImageBox.Text.Trim() : "";
            DialogResult = true;
        }
    }
}
