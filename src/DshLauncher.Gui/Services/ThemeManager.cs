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
            // VSCode Modern 色板（紫蓝强调色保留）
            ("Brush.Primary",      "#6366F1", "#5B5BD6"),
            ("Brush.PrimaryHover", "#818CF8", "#6E6BE8"),
            ("Brush.PrimaryDim",   "#312E81", "#E0E7FF"),
            ("Brush.Ok",           "#34D399", "#059669"),
            ("Brush.OkBg",         "#052E21", "#D1FAE5"),
            ("Brush.Warn",         "#FBBF24", "#B45309"),
            ("Brush.WarnBg",       "#362B08", "#FEF3C7"),
            ("Brush.Error",        "#F87171", "#DC2626"),
            ("Brush.ErrorBg",      "#3B1214", "#FEE2E2"),
            ("Brush.Info",         "#CCCCCC", "#3B3B3B"),     // VSCode foreground
            ("Brush.Muted",        "#9D9D9D", "#616161"),     // descriptionForeground
            ("Brush.Verbose",      "#6F6F6F", "#8B8B8B"),
            ("Brush.Bg",           "#1F1F1E", "#F8F8F8"),     // editor.background
            ("Brush.Sidebar",      "#181818", "#F9F8F8"),     // sideBar/titleBar/panel
            ("Brush.PanelBg",      "#313131", "#FFFFFF"),     // input.background
            ("Brush.CardBg",       "#212121", "#FFFFFF"),
            ("Brush.HoverBg",      "#2A2D2E", "#E8E8E8"),     // list.hoverBackground
            ("Brush.Border",       "#333333", "#D0D0D0"),
            ("Brush.BorderSoft",   "#2B2B2B", "#E5E5E5"),     // VSCode 边框色
            ("Brush.CaptionGlyph", "#CCCCCC", "#5F5F5F"),
            ("Brush.CaptionHover", "#16FFFFFF", "#14000000"),
            ("Brush.CaptionPress", "#28FFFFFF", "#22000000"),
            ("Brush.LogText",      "#E6E6E6", "#3B3B3B"),
            ("Brush.LogVerbose",   "#B4B4B4", "#5A5A5A"),
            ("Brush.LogInfo",      "#E6E6E6", "#3B3B3B"),
            ("Brush.PrimaryText",  "#A5B4FC", "#4F46E5"),
            ("Brush.ScrollThumb",  "#3F3F3F", "#C9C9C9"),
            ("Brush.ListSelectedBg","#343257","#E4E1FA"),
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
