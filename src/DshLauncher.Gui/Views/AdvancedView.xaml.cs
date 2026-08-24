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
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using DshLauncher;

namespace DshLauncher.Gui.Views
{
    public partial class AdvancedView : UserControl
    {
        public AdvancedView()
        {
            InitializeComponent();
            Loaded += (_, __) => Refresh();
        }

        private void Refresh()
        {
            UninstallSummary.Text =
                "将删除 %LOCALAPPDATA%\\dsh-launcher\n" +
                "  - 便携 Node.js、dsh、安装缓存、启动器日志\n" +
                "勾选 Purge 时还会删除 ~/.dsh 用户 profile 与插件配置。\n" +
                "卸载需要关闭所有 dsh web 窗口，否则部分文件锁会失败。";
        }

        private void BtnUninstall_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool purge = PurgeCheck.IsChecked == true;
            string what = purge
                ? "%LOCALAPPDATA%\\dsh-launcher\n~/.dsh 用户数据"
                : "%LOCALAPPDATA%\\dsh-launcher";

            var r = MessageBox.Show(
                "确认卸载？\n将删除：\n" + what,
                "卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            // 调用 Core 的 Uninstaller.Run
            App.Logger.Info("[卸载] 开始 (purge=" + purge + ")");
            int code = Uninstaller.Run(purge, assumeYes: true, log: App.Logger);
            if (code == ExitCodes.Success)
            {
                // 关闭 GUI
                MessageBox.Show("卸载完成。\n\n本窗口即将关闭。下次启动器双击即重新下载 Node 和插件。",
                    "卸载", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown(code);
            }
            else
            {
                MessageBox.Show("卸载部分完成（退出码 " + code + "）。\n可能 dsh web 进程仍占用文件。",
                    "卸载", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpenRuntime_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", Paths.RuntimeDir); } catch { }
        }

        private void BtnOpenLogs_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", Paths.LogsDir); } catch { }
        }

        private void BtnCopyDiag_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dsh-launcher 诊断信息 ===");
            sb.AppendLine("Version: " + AppMain.Version);
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString + " " + (Environment.Is64BitOperatingSystem ? "x64" : "x86"));
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine("Runtime Dir: " + Paths.RuntimeDir);
            sb.AppendLine("Home: " + Paths.DshHome());
            sb.AppendLine();
            sb.AppendLine("--- Config ---");
            sb.AppendLine(JsonMini.Stringify(new Dictionary<string, object>
            {
                ["registry"] = Config.Current.Registry,
                ["githubProxy"] = Config.Current.GithubProxy,
                ["integrity"] = Config.Current.Integrity,
                ["protectExternal"] = Config.Current.ProtectExternal,
                ["logLevel"] = Config.Current.LogLevel,
                ["pinnedNodeVersion"] = Config.Current.PinnedNodeVersion,
            }));
            sb.AppendLine();
            sb.AppendLine("--- Node ---");
            try
            {
                string marker = Path.Combine(Paths.RuntimeDir, "current-node.txt");
                if (File.Exists(marker))
                {
                    string dir = File.ReadAllText(marker).Trim();
                    sb.AppendLine("Path: " + dir);
                    sb.AppendLine("Exists: " + Directory.Exists(dir));
                    if (Directory.Exists(dir))
                    {
                        Paths.NodeDir = dir;
                        sb.AppendLine("Version: " + PortableNode.NodeVersion());
                    }
                }
                else sb.AppendLine("Node: not installed");
            }
            catch (Exception ex) { sb.AppendLine("Node error: " + ex.Message); }
            sb.AppendLine();
            sb.AppendLine("--- dsh ---");
            try
            {
                sb.AppendLine("Installed: " + DshInstaller.InstalledVersion());
            }
            catch (Exception ex) { sb.AppendLine("dsh error: " + ex.Message); }
            sb.AppendLine();
            sb.AppendLine("--- Plugins ---");
            try
            {
                var reg = PluginRegistry.Load();
                int ok = 0;
                foreach (var p in reg.Plugins)
                    if (PluginManager.ProfileHasPlugin(p.PkgName)) ok++;
                sb.AppendLine("Installed: " + ok + "/" + reg.Plugins.Count);
                foreach (var p in reg.Plugins)
                    sb.AppendLine("  " + (PluginManager.ProfileHasPlugin(p.PkgName) ? "[x]" : "[ ]") + " " + p.Id + " (" + p.PkgName + ")");
            }
            catch (Exception ex) { sb.AppendLine("Plugins error: " + ex.Message); }
            sb.AppendLine();
            sb.AppendLine("--- Port 3080 ---");
            sb.AppendLine("In use: " + Shell.PortInUse(3080));
            sb.AppendLine();
            sb.AppendLine("--- Recent log (last 50 lines) ---");
            try
            {
                string logFile = Path.Combine(Paths.LogsDir, "launcher-" + DateTime.Today.ToString("yyyy-MM-dd") + ".log");
                if (File.Exists(logFile))
                {
                    var lines = File.ReadAllLines(logFile);
                    int start = Math.Max(0, lines.Length - 50);
                    for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                }
                else sb.AppendLine("(no log file)");
            }
            catch (Exception ex) { sb.AppendLine("Log error: " + ex.Message); }

            string diag = sb.ToString();
            DiagText.Text = diag.Substring(0, Math.Min(diag.Length, 4000));
            try
            {
                Clipboard.SetText(diag);
                App.Logger.Info("[诊断] 已复制到剪贴板 (" + diag.Length + " 字节)");
            }
            catch (Exception ex)
            {
                App.Logger.Warn("[诊断] 复制到剪贴板失败: " + ex.Message);
            }
        }
    }
}
