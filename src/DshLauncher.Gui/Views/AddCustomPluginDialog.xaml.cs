using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using DshLauncher.Logging;
using System.Windows;
using DshLauncher;

namespace DshLauncher.Gui.Views
{
    public partial class AddCustomPluginDialog : Window
    {
        public string Id { get; private set; }
        public string Display { get; private set; }
        public bool ViaNpm { get; private set; }
        public string Source { get; private set; }
        public string Error { get; private set; }

        public AddCustomPluginDialog()
        {
            InitializeComponent();
        }

        private void InputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ErrorText.Text = "";
            string raw = InputBox.Text?.Trim() ?? "";
            if (raw.Length == 0)
            {
                PreviewText.Text = "";
                BtnOk.IsEnabled = false;
                return;
            }
            var r = PluginInputParser.Parse(raw);
            if (r.Success)
            {
                PreviewText.Text = $"✓ ID={r.Id}  来源={r.Kind}  Source={r.Source}";
                BtnOk.IsEnabled = true;
            }
            else
            {
                PreviewText.Text = "";
                BtnOk.IsEnabled = false;
            }
        }

        private void BtnOk_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string raw = InputBox.Text?.Trim() ?? "";
            var r = PluginInputParser.Parse(raw);
            if (!r.Success)
            {
                MessageBox.Show("无法解析：\n" + r.Error, "格式错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Id = r.Id;
            Display = r.Display;
            ViaNpm = r.Kind == PluginInputParser.Kind.Npm;
            Source = r.Source;
            DialogResult = true;
            Close();
        }
    }
}
