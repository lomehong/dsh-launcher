using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using DshLauncher;
using DshLauncher.Logging;

namespace DshLauncher.Gui.Services
{
    public enum ThemeMode { Auto, Light, Dark }

    /// <summary>
    /// 主题管理：浅色/深色/自动（跟随系统）。
    /// 原理——App.xaml 的 Brush.* 都是共享 SolidColorBrush 实例，运行时改其 Color，
    /// 全部 StaticResource 引用实时跟随（无需 DynamicResource 改造）。
    /// 偏好持久化在 RuntimeDir\gui-settings.json；自动模式通过注册表监听跟随系统。
    /// </summary>
    public static class ThemeManager
    {
        public static ThemeMode Mode { get; private set; } = ThemeMode.Auto;
        public static bool IsDarkEffective { get; private set; } = true;

        /// <summary>主题已应用（MainWindow 菜单勾选态等订阅刷新）。</summary>
        public static event Action Applied;

        private static readonly string SettingsFile =
            Path.Combine(Paths.RuntimeDir ?? ".", "gui-settings.json");

        // Brush key -> (dark, light)
        private static readonly (string Key, string Dark, string Light)[] Palette = new[]
        {
            ("Brush.Primary",      "#6366F1", "#5B5BD6"),
            ("Brush.PrimaryHover", "#818CF8", "#6E6BE8"),
            ("Brush.PrimaryDim",   "#312E81", "#E0E7FF"),
            ("Brush.Ok",           "#34D399", "#059669"),
            ("Brush.OkBg",         "#052E21", "#D1FAE5"),
            ("Brush.Warn",         "#FBBF24", "#B45309"),
            ("Brush.WarnBg",       "#362B08", "#FEF3C7"),
            ("Brush.Error",        "#F87171", "#DC2626"),
            ("Brush.ErrorBg",      "#3B1214", "#FEE2E2"),
            ("Brush.Info",         "#E6EDF3", "#1F2937"),
            ("Brush.Muted",        "#9BA8BD", "#4B5563"),
            ("Brush.Verbose",      "#5C6A82", "#6B7280"),
            ("Brush.Bg",           "#0D1117", "#F4F6F9"),
            ("Brush.Sidebar",      "#090C10", "#ECEFF4"),
            ("Brush.PanelBg",      "#111722", "#FFFFFF"),
            ("Brush.CardBg",       "#161D2B", "#FFFFFF"),
            ("Brush.HoverBg",      "#1D2739", "#E8EDF4"),
            ("Brush.Border",       "#232D42", "#D5DBE4"),
            ("Brush.BorderSoft",   "#1A2234", "#E4E9F0"),
            // 标题栏/日志等专用
            ("Brush.CaptionGlyph", "#CFD8E6", "#4A4F5A"),
            ("Brush.CaptionHover", "#16FFFFFF", "#14000000"),
            ("Brush.CaptionPress", "#28FFFFFF", "#22000000"),
            ("Brush.LogText",      "#C9D4E4", "#24303F"),
            // 日志级别专用色：日志面板底是 Sidebar(#090C10)，全局 Verbose(#5C6A82) 在其上
            // 对比度仅 ~2.4:1 不可读——日志用更亮一档的独立色板
            ("Brush.LogVerbose",    "#8B99B0", "#5B6472"),
            ("Brush.LogInfo",       "#D6DEEA", "#1F2937"),
            ("Brush.PrimaryText",  "#A5B4FC", "#4F46E5"),
        };

        /// <summary>启动时调用：读偏好 -> 应用 -> 注册系统主题监听。</summary>
        public static void Init()
        {
            Load();
            Apply();
            StartSystemThemeWatcher();
        }

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            Save();
            Apply();
        }

        /// <summary>系统当前是否浅色应用主题（注册表 AppsUseLightTheme）。</summary>
        public static bool SystemPrefersLight()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return v is int i ? i != 0 : true;
            }
            catch { return true; }
        }

        private static void Apply()
        {
            bool dark = Mode switch
            {
                ThemeMode.Light => false,
                ThemeMode.Dark => true,
                _ => !SystemPrefersLight(),
            };
            IsDarkEffective = dark;

            var res = System.Windows.Application.Current.Resources;
            foreach (var (key, darkHex, lightHex) in Palette)
            {
                var c = (System.Windows.Media.ColorConverter.ConvertFromString(
                    dark ? darkHex : lightHex) as System.Windows.Media.Color?).Value;
                // XAML 全部 Brush.* 引用为 DynamicResource：替换字典条目即全 UI 刷新
                res[key] = new System.Windows.Media.SolidColorBrush(c);
            }
            Applied?.Invoke();
        }

        // ---------- 持久化（gui-settings.json {"theme":"auto|light|dark"}） ----------

        private static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var obj = JsonMini.Parse(File.ReadAllText(SettingsFile, Encoding.UTF8));
                string t = obj.GetString("theme")?.Trim().ToLowerInvariant();
                if (t == "light") Mode = ThemeMode.Light;
                else if (t == "dark") Mode = ThemeMode.Dark;
                else Mode = ThemeMode.Auto;
            }
            catch { Mode = ThemeMode.Auto; }
        }

        private static void Save()
        {
            try
            {
                string t = Mode switch { ThemeMode.Light => "light", ThemeMode.Dark => "dark", _ => "auto" };
                File.WriteAllText(SettingsFile, "{\"theme\":\"" + t + "\"}", new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------- 自动模式：轻量轮询系统主题（2s；注册表读取微秒级，简单可靠） ----------

        private static void StartSystemThemeWatcher()
        {
            var t = new Thread(() =>
            {
                bool lastLight = SystemPrefersLight();
                while (true)
                {
                    Thread.Sleep(2000);
                    try
                    {
                        bool nowLight = SystemPrefersLight();
                        if (nowLight != lastLight)
                        {
                            lastLight = nowLight;
                            if (Mode == ThemeMode.Auto)
                            {
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    Apply();
                                    App.Logger?.Info("系统主题变化，已自动切换为" + (IsDarkEffective ? "深色" : "浅色") + "。");
                                }));
                            }
                        }
                    }
                    catch { }
                }
            }) { IsBackground = true, Name = "theme-watcher" };
            t.Start();
        }
    }
}
