using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using DshLauncher.Logging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DshLauncher;
using DshLauncher.Logging;

namespace DshLauncher.Gui.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            Loaded += async (_, __) => await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            await Task.Run(() =>
            {
                var nodeMarker = System.IO.Path.Combine(Paths.RuntimeDir, "current-node.txt");
                bool nodeOk = false;
                string nodeDir = null;
                if (System.IO.File.Exists(nodeMarker))
                {
                    nodeDir = System.IO.File.ReadAllText(nodeMarker).Trim();
                    if (nodeDir.Length > 0 && System.IO.File.Exists(System.IO.Path.Combine(nodeDir, "node.exe")))
                    {
                        nodeOk = true;
                        Paths.NodeDir = nodeDir;
                    }
                }

                string nodeVer = nodeOk ? PortableNode.NodeVersion() : "";
                string dshVer = nodeOk ? DshInstaller.InstalledVersion() : "";
                string dshLatest = nodeOk ? DshInstaller.LatestVersion() : "";
                int okPlugins = 0, totalPlugins = 0;
                if (nodeOk)
                {
                    var plugins = PluginRegistry.Load().Plugins;
                    totalPlugins = plugins.Count;
                    foreach (var p in plugins)
                        if (PluginManager.ProfileHasPlugin(p.PkgName)) okPlugins++;
                }
                bool webRunning = Shell.PortInUse(3080);

                Dispatcher.Invoke(() => RenderStatus(nodeOk, nodeVer, nodeDir,
                    dshVer, dshLatest, okPlugins, totalPlugins, webRunning));
            });
        }

        /// <summary>轻量刷新 web 徽章（不查 npm 版本，供启动/停止联动调用；starting=启动中）。</summary>
        public void RefreshWebBadge(bool running, bool starting = false)
        {
            if (starting) { SetBadge(WebBadge, WebBadgeText, false, "", "启动中 …", ""); return; }
            SetBadge(WebBadge, WebBadgeText, running, "运行中 · 3080", "未启动", "");
        }

        private void RenderStatus(bool nodeOk, string nodeVer, string nodeDir,
            string dshVer, string dshLatest, int okPlugins, int totalPlugins, bool webRunning)
        {
            // ① Node
            SetBadge(NodeBadge, NodeBadgeText, nodeOk, "已就绪 " + nodeVer, "未安装", "https://registry.npmmirror.com");
            NodePathText.Text = nodeOk ? nodeDir : "首次启动会自动下载便携 Node（约 35MB）";
            BtnRedownload.Visibility = nodeOk ? Visibility.Visible : Visibility.Collapsed;

            // ② dsh
            SetBadge(DshBadge, DshBadgeText, dshVer.Length > 0, "已安装 " + dshVer, "未安装", "");
            DshLatestText.Text = "最新版本: " + (dshLatest.Length > 0 ? dshLatest : "查询失败（可能离线）");
            BtnUpdateDsh.Visibility = dshVer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            // ③ 插件
            if (totalPlugins == 0)
            {
                SetBadge(PluginsBadge, PluginsBadgeText, false, "", "未检测", "");
                PluginsProgress.Visibility = Visibility.Collapsed;
                BtnReinstallPlugins.Visibility = Visibility.Collapsed;
            }
            else
            {
                bool all = okPlugins == totalPlugins;
                SetBadge(PluginsBadge, PluginsBadgeText, all,
                    okPlugins + " / " + totalPlugins + " 已装",
                    okPlugins + " / " + totalPlugins + " 已装（缺装）", "");
                PluginsProgress.Visibility = Visibility.Visible;
                PluginsProgress.Value = 100.0 * okPlugins / totalPlugins;
                BtnReinstallPlugins.Visibility = Visibility.Visible;
            }

            // ④ web
            SetBadge(WebBadge, WebBadgeText, webRunning, "运行中 · 3080", "未启动", "");
        }

        private static void SetBadge(Border badge, TextBlock text, bool ok,
            string okText, string badText, string unused)
        {
            var app = System.Windows.Application.Current;
            badge.Background = ok
                ? (Brush)app.Resources["Brush.OkBg"]
                : (Brush)app.Resources["Brush.WarnBg"];
            text.Foreground = ok
                ? (Brush)app.Resources["Brush.Ok"]
                : (Brush)app.Resources["Brush.Warn"];
            text.Text = ok ? okText : badText;
        }

        private async void BtnRedownload_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[重下载 Node] 开始...");
            BtnRedownload.IsEnabled = false;
            await Task.Run(() => PortableNode.Ensure(App.Logger));
            BtnRedownload.IsEnabled = true;
            await RefreshAsync();
        }

        private async void BtnUpdateDsh_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[更新 dsh] 检查最新版本...");
            BtnUpdateDsh.IsEnabled = false;
            await Task.Run(() => DshInstaller.Ensure(forceUpdate: true, log: App.Logger));
            BtnUpdateDsh.IsEnabled = true;
            await RefreshAsync();
        }

        private async void BtnReinstallPlugins_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[重检插件] 开始...");
            BtnReinstallPlugins.IsEnabled = false;
            await Task.Run(() => PluginManager.EnsureAll(App.Logger));
            BtnReinstallPlugins.IsEnabled = true;
            await RefreshAsync();
        }

        private async void BtnFullSetup_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[完整安装] 开始...");
            BtnFullSetup.IsEnabled = false;
            BtnInstallOnly.IsEnabled = false;
            FullSetupStatus.Text = "正在执行完整安装（日志见下方）…";
            await Task.Run(() =>
            {
                if (PortableNode.Ensure(App.Logger) == null) return;
                if (DshInstaller.Ensure(false, App.Logger) != 0) return;
                PluginManager.EnsureAll(App.Logger);
            });
            BtnFullSetup.IsEnabled = true;
            BtnInstallOnly.IsEnabled = true;
            await RefreshAsync();
            FullSetupStatus.Text = "完整安装完成。";
        }

        private async void BtnInstallOnly_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[仅安装] 开始...");
            BtnInstallOnly.IsEnabled = false;
            FullSetupStatus.Text = "正在安装（日志见下方）…";
            await Task.Run(() =>
            {
                if (PortableNode.Ensure(App.Logger) == null) return;
                if (DshInstaller.Ensure(false, App.Logger) != 0) return;
                PluginManager.EnsureAll(App.Logger);
            });
            BtnInstallOnly.IsEnabled = true;
            await RefreshAsync();
            FullSetupStatus.Text = "安装完成（未启动 dsh web）。";
        }
    }
}
