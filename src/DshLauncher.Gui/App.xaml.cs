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
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DshLauncher;
using DshLauncher.Gui.Services;

namespace DshLauncher.Gui
{
    public partial class App : Application
    {
        public static ILogger Logger { get; private set; }
        public static LogSink LogSink { get; private set; }
        public static System.Windows.Forms.NotifyIcon TrayIcon { get; private set; }

        /// <summary>
        /// GUI 生命周期内的 Job Object（KILL_ON_JOB_CLOSE）：由本进程 spawn 的 dsh web
        /// 进程树全部纳入。GUI 退出（正常退出或崩溃）→ 句柄关闭 → 内核杀掉 web 整树，
        /// 落实 README "关闭启动器 = 停止 dsh web" 的契约；最小化到托盘期间进程存活不受影响。
        /// </summary>
        public static JobObject ChildJob { get; private set; }

        private static MainWindow _mainWindow;
        private static System.Windows.Forms.ToolStripMenuItem _trayWebItem;

        private SingleInstanceLock _instanceLock;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            Shell.OutputSink = line => Logger.Log(LogLevel.Info, line);

            // 全局异常兜底：任何未处理异常写日志 + 弹窗，绝不静默退出
            DispatcherUnhandledException += (s, ex) =>
            {
                Logger?.Error("[XAML/Dispatcher] " + ex.Exception);
                MessageBox.Show(ex.Exception.ToString(), "dsh-launcher 内部错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // 单实例锁
            _instanceLock = SingleInstanceLock.TryAcquire();
            if (_instanceLock == null)
            {
                MessageBox.Show("已有 dsh-launcher 在运行。请先关闭原窗口。",
                    "dsh-launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(ExitCodes.AlreadyRunning);
                return;
            }

            Paths.Init();
            Config.Load();
            ChildJob = JobObject.Create();   // 子进程隔离：早于一切可能 spawn 子进程的路径
            Logger = new ConsoleLogger(logDir: Paths.LogsDir);
            ApplyLogLevel(Logger);
            Services.ThemeManager.Init();   // 主题（gui-settings.json，自动跟随系统）
            LogSink = new LogSink();
            Logger.Event += LogSink.OnLog;
            // Core 的大量进度输出走 Console.WriteLine；重定向进日志流，否则 GUI 里全部丢失
            (Logger as ConsoleLogger)?.RedirectConsoleOutput();

            Logger.Info("=== 启动 GUI v" + AppMain.Version + " ===");
            Logger.Info("运行目录: " + Paths.RuntimeDir);

            _mainWindow = new MainWindow();
            InitTrayIcon();
            _mainWindow.Show();
        }

        private static void ApplyLogLevel(ILogger log)
        {
            switch (Config.Current.LogLevel?.ToLowerInvariant())
            {
                case "silent": log.Level = LogLevel.Silent; break;
                case "verbose": log.Level = LogLevel.Verbose; break;
                case "warn": log.Level = LogLevel.Warn; break;
                case "error": log.Level = LogLevel.Error; break;
                default: log.Level = LogLevel.Info; break;
            }
        }

        /// <summary>加载 app.ico 作为托盘图标；优先 exe 同名 .ico，失败回退系统默认。</summary>
        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var icoPath = System.IO.Path.ChangeExtension(asm.Location, ".ico");
                if (System.IO.File.Exists(icoPath)) return new System.Drawing.Icon(icoPath);
            }
            catch { }
            return SystemIcons.Application;
        }

        /// <summary>按 web 运行状态更新托盘菜单文本与图标（running 不传则自查端口，阻塞至多 800ms）。</summary>
        public static void UpdateTrayWebUI(bool? running = null, bool starting = false)
        {
            if (_trayWebItem == null) return;
            bool r = running ?? Shell.PortInUse(3080);
            if (starting && !r)
            {
                // 启动中：禁点，防止并发 spawn 多个 dsh web 抢 3080 端口
                _trayWebItem.Text = "dsh web 启动中…";
                _trayWebItem.Image = MenuIconPlay();
                _trayWebItem.Enabled = false;
            }
            else
            {
                _trayWebItem.Text = r ? "停止 dsh web" : "启动 dsh web";
                _trayWebItem.Image = r ? MenuIconStop() : MenuIconPlay();
                _trayWebItem.Enabled = true;
            }
            Logger.Info("托盘菜单: " + _trayWebItem.Text);
        }

        // ---------- 托盘菜单小图标（16x16 GDI+ 绘制，无外部资源依赖） ----------

        private static System.Drawing.Image MenuIconHome()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(70, 120, 220)))
                {
                    g.FillPolygon(b, new System.Drawing.PointF[] {
                        new System.Drawing.PointF(2, 9), new System.Drawing.PointF(8, 3), new System.Drawing.PointF(14, 9) });
                    g.FillRectangle(b, 4, 9, 8, 5);
                }
            }
            return bmp;
        }

        private static System.Drawing.Image MenuIconPlay()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(46, 160, 90)))
                {
                    g.FillPolygon(b, new System.Drawing.PointF[] {
                        new System.Drawing.PointF(4, 3), new System.Drawing.PointF(13, 8), new System.Drawing.PointF(4, 13) });
                }
            }
            return bmp;
        }

        private static System.Drawing.Image MenuIconStop()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(214, 72, 72)))
                {
                    g.FillRectangle(b, 4, 4, 8, 8);
                }
            }
            return bmp;
        }

        private static System.Drawing.Image MenuIconGlobe()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 120, 220), 1.4f))
                {
                    g.DrawEllipse(p, 2, 2, 12, 12);
                    g.DrawEllipse(p, 5, 2, 6, 12);
                    g.DrawLine(p, 2.5f, 8, 13.5f, 8);
                }
            }
            return bmp;
        }

        private static System.Drawing.Image MenuIconExit()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = new System.Drawing.Pen(System.Drawing.Color.FromArgb(150, 150, 158), 2f))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(p, 4, 4, 12, 12);
                    g.DrawLine(p, 12, 4, 4, 12);
                }
            }
            return bmp;
        }

        private static void InitTrayIcon()
        {
            TrayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Visible = true,
                Text = "dsh-launcher v" + AppMain.Version,
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("打开主界面", MenuIconHome(), (_, __) => _mainWindow?.ShowFromTray()));
            _trayWebItem = new System.Windows.Forms.ToolStripMenuItem("启动 dsh web", MenuIconPlay(), (_, __) => _mainWindow?.Dispatcher.Invoke(() =>
            {
                if (Shell.PortInUse(3080)) _mainWindow?.StopWebFromTray();
                else _mainWindow?.StartWebFromTray();
            }));
            menu.Items.Add(_trayWebItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("打开浏览器", MenuIconGlobe(), (_, __) => Shell.OpenBrowser("http://127.0.0.1:3080")));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("退出", MenuIconExit(), (_, __) => _mainWindow?.RealExit()));
            TrayIcon.ContextMenuStrip = menu;

            // 双击托盘：恢复窗口
            TrayIcon.MouseDoubleClick += (_, __) => _mainWindow?.ShowFromTray();

            UpdateTrayWebUI();   // 初始按端口实际状态显示 启动/停止
            Logger.Info("托盘图标已就绪。");
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            Logger?.Info("=== GUI 退出 (code=" + e.ApplicationExitCode + ") ===");
            if (TrayIcon != null)
            {
                TrayIcon.Visible = false;
                TrayIcon.Dispose();
            }
            if (Logger != null && LogSink != null) Logger.Event -= LogSink.OnLog;
            LogSink?.Detach();
            // 先于日志器失效记录：释放 job 即终止 dsh web 进程树（若有）
            ChildJob?.Dispose();
            ChildJob = null;
            _instanceLock?.Dispose();
            base.OnExit(e);
        }
    }
}
