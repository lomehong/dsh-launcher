using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using DshLauncher.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using DshLauncher;
using DshLauncher.Gui.ViewModels;

namespace DshLauncher.Gui.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsViewModel ViewModel { get; } = new();

        public SettingsView()
        {
            InitializeComponent();
            DataContext = ViewModel;
            // LoadFromConfig 后同步单选框选中态（RadioButton 无双向绑定，需手动对齐）
            Loaded += (_, __) =>
            {
                ViewModel.LoadFromConfig();
                SyncRadios();
            };
        }

        /// <summary>根据 VM 当前值选中对应的单选框。</summary>
        private void SyncRadios()
        {
            IntegrityStrict.IsChecked = ViewModel.Integrity == "strict";
            IntegrityLax.IsChecked    = ViewModel.Integrity == "lax";
            IntegrityNone.IsChecked   = ViewModel.Integrity == "none";
            LogSilent.IsChecked       = ViewModel.LogLevel == "silent";
            LogInfo.IsChecked         = ViewModel.LogLevel == "info";
            LogVerbose.IsChecked      = ViewModel.LogLevel == "verbose";
        }
        private void PresetRegistry_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string val) ViewModel.Registry = val;
        }
        private void PresetProxy_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string val) ViewModel.GithubProxy = val;
        }

        private void IntegrityStrict_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.Integrity = "strict";
        private void IntegrityLax_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.Integrity = "lax";
        private void IntegrityNone_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.Integrity = "none";
        private void LogSilent_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.LogLevel = "silent";
        private void LogInfo_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.LogLevel = "info";
        private void LogVerbose_Checked(object sender, System.Windows.RoutedEventArgs e) => ViewModel.LogLevel = "verbose";

        private void BtnSave_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var (ok, err) = ViewModel.Save();
            if (ok)
            {
                StatusText.Text = "已保存到 " + Paths.ConfigJsonPath;
                App.Logger.Info("[设置] 已保存 config.json");
            }
            else
            {
                StatusText.Text = "保存失败: " + err;
                App.Logger.Error("[!] 保存 config.json 失败: " + err);
                MessageBox.Show(err, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReload_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 重新从 config.json 加载（覆盖当前 VM 值）
            Config.Load();
            ViewModel.LoadFromConfig();
            StatusText.Text = "已重新加载当前配置";
            App.Logger.Info("[设置] 已重新加载 config.json");
        }

        private void BtnOpenFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", Paths.RuntimeDir);
            }
            catch (Exception ex)
            {
                App.Logger.Error("[!] 打开目录失败: " + ex.Message);
            }
        }
    }
}
