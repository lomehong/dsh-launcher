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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DshLauncher;

namespace DshLauncher.Gui
{
    public partial class MainWindow : Window
    {
        private readonly LogSink _logSink;
        private bool _reallyExit;   // 区分 "最小化到托盘" 与 "真正退出"
        private volatile bool _webStarting;   // dsh web 启动中（防重入：阻止重复 spawn 多个进程抢 3080）

        public MainWindow()
        {
            InitializeComponent();
            _logSink = App.LogSink;
            _logSink.Attach(LogBox);
            HeaderVersion.Text = "v" + AppMain.Version;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Services.ThemeManager.Applied += ApplyTitleIcon;   // 主题切换 -> 标题栏鲸鱼随之反色
        }

        /// <summary>侧边导航切换：切换 ContentArea 里四个视图的可见性。</summary>
        private void NavList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DashboardView == null || PluginsView == null || SettingsView == null || AdvancedView == null) return;
            int idx = NavList.SelectedIndex;
            DashboardView.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            PluginsView.Visibility  = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility  = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            AdvancedView.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyTitleIcon();
            RefreshPortStatus();
            RefreshDashboard();
            RefreshWebStatus();
        }

        /// <summary>标题栏鲸鱼跟随主题：深色主题用浅色鲸鱼（纯黑在 #0D1117 底上不可见），浅色主题用黑鲸鱼。</summary>
        private void ApplyTitleIcon()
        {
            if (TitleIcon == null) return;
            try
            {
                // 相对 pack URI：解析到本程序集（程序集名是 dsh-launcher-gui，不能写死命名空间）
                var uri = new Uri("whale_" + (Services.ThemeManager.IsDarkEffective ? "light" : "dark") + ".png", UriKind.Relative);
                TitleIcon.Source = new System.Windows.Media.Imaging.BitmapImage(uri);
            }
            catch (Exception ex)
            {
                App.Logger.Warn("[提示] 标题栏图标加载失败: " + ex.Message);
            }
        }

        private void RefreshPortStatus()
        {
            bool busy = Shell.PortInUse(3080);
            PortText.Text = busy ? "端口 3080 · 占用" : "端口 3080 · 空闲";
        }

        private void RefreshWebStatus()
        {
            bool running = Shell.PortInUse(3080);
            if (running) _webStarting = false;   // 已监听 => 启动完成
            bool starting = _webStarting && !running;
            WebStatusText.Text = starting ? "dsh web: 启动中…" : (running ? "dsh web: 运行中" : "dsh web: 未启动");
            WebStatusDot.Fill = running
                ? (System.Windows.Media.Brush)Resources["Brush.Ok"]
                : (System.Windows.Media.Brush)Resources["Brush.Muted"];
            BtnStopWeb.IsEnabled = running;
            BtnOpenBrowser.IsEnabled = running;
            BtnStartWeb.IsEnabled = !running && !starting;
            DashboardView?.RefreshWebBadge(running, starting);   // 总览页徽章联动
            App.UpdateTrayWebUI(running, starting);              // 托盘菜单联动
        }

        private async void RefreshDashboard()
        {
            await DashboardView.RefreshAsync();
        }

        private void BtnStartWeb_Click(object sender, System.Windows.RoutedEventArgs e) => StartWebWithGuard();

        /// <summary>托盘菜单入口：与主界面按钮同一防重入入口。</summary>
        internal void StartWebFromTray() => StartWebWithGuard();

        /// <summary>
        /// 统一启动入口（防重入）：启动中重复点击只记日志，不再 spawn 进程——
        /// 多个 dsh web 并发会互相抢 3080 端口（EADDRINUSE 崩溃）。
        /// </summary>
        private void StartWebWithGuard()
        {
            if (Shell.PortInUse(3080))
            {
                App.Logger.Info("端口 3080 已被占用，可能 dsh web 已在运行。");
                RefreshWebStatus();
                return;
            }
            if (_webStarting)
            {
                App.Logger.Info("[提示] dsh web 正在启动中，请勿重复点击（冷启动可能需要 1-2 分钟）。");
                return;
            }
            _webStarting = true;
            App.Logger.Info("正在启动 dsh web ...（冷启动可能需要 1-2 分钟，请勿重复点击）");
            StartDshWebDetached();
            RefreshPortStatus();
            RefreshWebStatus();   // 显示 "启动中…" 并禁用启动按钮
            _ = PollWebUntilIdleAsync();
        }

        /// <summary>非阻塞启动 dsh web：独立进程后台运行，GUI 不等其退出，避免主线程被 WaitForExit 卡住。</summary>
        private void StartDshWebDetached()
        {
            string dsh = PortableNode.DshCmdPath();
            if (dsh == null)
            {
                App.Logger.Error("[!] 找不到 dsh.cmd。");
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + dsh + "\" web")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
                if (p == null)
                {
                    _webStarting = false;
                    App.Logger.Error("[!] dsh web 进程启动失败。");
                    return;
                }
                // 纳入 GUI 生命周期的 Job Object：GUI 真正退出（含崩溃）时内核自动终止 web 进程树
                if (App.ChildJob != null && App.ChildJob.Handle != IntPtr.Zero)
                {
                    try
                    {
                        if (!App.ChildJob.AddProcess(p.Handle))
                            App.Logger.Warn("[提示] dsh web 未纳入 Job Object（GUI 退出后可能残留进程）。");
                    }
                    catch (Exception jex)
                    {
                        App.Logger.Warn("[提示] Job Object 加入失败: " + jex.Message);
                    }
                }
                p.OutputDataReceived += (_, e) => { if (e.Data != null) App.Logger.Info(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) App.Logger.Warn(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                // 进程退出但端口从未监听 => 启动失败（如 EADDRINUSE/依赖缺失），立即结束"启动中"状态
                p.EnableRaisingEvents = true;
                p.Exited += (_, __) => Dispatcher.BeginInvoke(() =>
                {
                    // 进程退出：无论此前是"启动中"还是"运行中"，都要同步 UI
                    // （轮询循环在就绪后已 return，运行中退出的场景只能靠这里刷新）
                    bool running = Shell.PortInUse(3080);
                    if (!running)
                    {
                        bool wasStarting = _webStarting;
                        _webStarting = false;
                        if (wasStarting)
                            App.Logger.Error("[!] dsh web 进程已退出且端口未监听——启动失败，请查看上方日志。");
                        else
                            App.Logger.Info("dsh web 进程已退出。");
                    }
                    RefreshWebStatus();
                });
            }
            catch (Exception ex)
            {
                App.Logger.Error("[!] 启动 dsh web 异常: " + ex.Message);
            }
        }

        /// <summary>后台等待 dsh web 就绪/停止，状态变化时刷新 UI（最多约 3 分钟，冷启动较慢）。</summary>
        private async Task PollWebUntilIdleAsync()
        {
            bool last = Shell.PortInUse(3080);
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(1500);
                bool now = Shell.PortInUse(3080);
                if (now != last)
                {
                    last = now;
                    RefreshPortStatus();
                    RefreshWebStatus();
                    if (now) return;   // 已就绪
                }
            }
            if (_webStarting)
            {
                // 3 分钟仍未监听且未收到 Exited（进程还在慢启动）：恢复按钮，避免永久锁死
                _webStarting = false;
                App.Logger.Warn("[提示] 等待 dsh web 就绪超时（3 分钟）。若浏览器无法访问 127.0.0.1:3080，请查看日志排查。");
                RefreshWebStatus();
            }
        }

        private void BtnStopWeb_Click(object sender, System.Windows.RoutedEventArgs e) => StopDshWeb();

        /// <summary>托盘菜单停止入口。</summary>
        internal void StopWebFromTray() => StopDshWeb();

        /// <summary>停止 dsh web（主界面按钮与托盘菜单共用）。</summary>
        private void StopDshWeb()
        {
            // 通过端口找 PID，杀真正的监听者（WINDOWTITLE 过滤不可靠：dsh web 的 node 无窗口标题）
            App.Logger.Warn("[!] 停止 dsh web：定位 3080 端口占用进程");
            BtnStopWeb.IsEnabled = false;
            try
            {
                foreach (int pid in Shell.GetPortOwnerPids(3080))
                {
                    // 先杀进程树（node 会 spawn 子进程）
                    Shell.RunCmd("taskkill /F /T /PID " + pid);
                    App.Logger.Info("taskkill /T /PID " + pid);
                }
                // 等端口释放（最多 5s）
                for (int i = 0; i < 10 && Shell.PortInUse(3080); i++) Thread.Sleep(500);
                bool stopped = !Shell.PortInUse(3080);
                App.Logger.Info(stopped ? "dsh web 已停止。" : "[!] 端口 3080 仍被占用（可能有外部进程）。");
            }
            catch (Exception ex) { App.Logger.Warn("[!] 停止失败: " + ex.Message); }
            RefreshPortStatus();
            RefreshWebStatus();
        }

        private void BtnOpenBrowser_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Shell.OpenBrowser("http://127.0.0.1:3080");
        }

        private async void BtnRecheck_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BtnRecheck.IsEnabled = false;
            try
            {
                App.Logger.Info("[自检] 刷新状态...");
                await DashboardView.RefreshAsync();
                RefreshPortStatus();
                RefreshWebStatus();
                App.Logger.Info("[自检] 完成。");
            }
            finally { BtnRecheck.IsEnabled = true; }
        }

        private void BtnClearLog_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _logSink.Clear();
            App.Logger.Verbose("日志面板已清空（文件日志不受影响）。");
        }

        /// <summary>
        /// 窗口关闭事件：默认最小化到托盘（保持后台运行），只有用户明确"退出"才真正 Shutdown。
        /// 托盘右键菜单提供"退出"选项。
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            Services.ThemeManager.Applied -= ApplyTitleIcon;
            if (!_reallyExit && App.TrayIcon != null)
            {
                e.Cancel = true;
                Hide();
                App.TrayIcon.ShowBalloonTip(3000, "dsh-launcher",
                    "已最小化到托盘。右键托盘图标可恢复窗口或退出。",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                _logSink.Detach();
            }
        }

        /// <summary>由 App 托盘菜单触发：显示窗口。</summary>
        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        /// <summary>由 App 托盘菜单触发：真正退出。</summary>
        public void RealExit()
        {
            _reallyExit = true;
            Close();
        }

        // ==================== 无边框窗口支持 ====================

        // ---------- 主题切换 ----------

        private void BtnTheme_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SyncThemeMenuChecks();
            ThemePopup.IsOpen = true;
        }

        private void ThemeItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string tag = (sender as System.Windows.Controls.Button)?.Tag as string;
            var mode = tag == "light" ? Services.ThemeMode.Light
                     : tag == "dark" ? Services.ThemeMode.Dark
                     : Services.ThemeMode.Auto;
            ThemePopup.IsOpen = false;
            Services.ThemeManager.SetMode(mode);
        }

        /// <summary>按当前模式显示行尾勾选。</summary>
        private void SyncThemeMenuChecks()
        {
            var m = Services.ThemeManager.Mode;
            RowLightCheck.Visibility = m == Services.ThemeMode.Light ? Visibility.Visible : Visibility.Collapsed;
            RowDarkCheck.Visibility = m == Services.ThemeMode.Dark ? Visibility.Visible : Visibility.Collapsed;
            RowAutoCheck.Visibility = m == Services.ThemeMode.Auto ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnMin_Click(object sender, System.Windows.RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMax_Click(object sender, System.Windows.RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void BtnClose_Click(object sender, System.Windows.RoutedEventArgs e)
            => Close();   // 走 Closing：非 _reallyExit 时最小化到托盘

        private void Window_StateChanged(object sender, EventArgs e)
        {
            bool max = WindowState == WindowState.Maximized;
            MaxGlyph.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            RestoreGlyph.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
            RootBorder.BorderThickness = max
                ? new Thickness(0)
                : new Thickness(1);
        }

    }
}