using System.Windows;
using System.Windows.Controls;

namespace WinTools.UI
{
    /// <summary>Piccolo dialogo con una sola casella di testo (per esempio la descrizione di un separatore).</summary>
    public sealed class TextPromptWindow : Window
    {
        readonly TextBox _box;

        /// <summary>Il testo inserito (dopo un OK).</summary>
        public string Value => _box.Text;

        public TextPromptWindow(string title, string caption, string value, int maxLength = 40)
        {
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FluentWindow.Setup(this);

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            var cap = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            cap.SetResourceReference(StyleProperty, "TxtCaption");
            _box = new TextBox { Text = value ?? "", MaxLength = maxLength };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
            ok.SetResourceReference(StyleProperty, "AccentButton");
            ok.Click += (s, e) => { DialogResult = true; };
            var cancel = new Button { Content = "Annulla", MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(cap);
            root.Children.Add(_box);
            root.Children.Add(buttons);
            Content = root;
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }
    }
}
