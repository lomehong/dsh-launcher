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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DshLauncher;
using DshLauncher.Gui.ViewModels;

namespace DshLauncher.Gui.Views
{
    public partial class PluginsView : UserControl
    {
        /// <summary>DataGrid 的 ItemsSource，UI 自动响应 Add/Remove。</summary>
        public ObservableCollection<PluginViewModel> Plugins { get; } = new();

        public PluginsView()
        {
            InitializeComponent();
            PluginGrid.ItemsSource = Plugins;
            Loaded += async (_, __) => await RefreshAsync();
        }

        /// <summary>从 Core 的 PluginRegistry 读 + 检测安装状态。</summary>
        public async Task RefreshAsync()
        {
            await Task.Run(() =>
            {
                var reg = PluginRegistry.Load();
                var installed = new HashSet<string>(
                    reg.Plugins.Where(p => PluginManager.ProfileHasPlugin(p.PkgName))
                              .Select(p => p.PkgName));

                Dispatcher.Invoke(() =>
                {
                    Plugins.Clear();
                    foreach (var spec in reg.Plugins)
                        Plugins.Add(new PluginViewModel(spec, installed.Contains(spec.PkgName)));
                });
            });
        }

        /// <summary>把当前 Plugins 列表写入 plugins.json（GUI 修改持久化）。</summary>
        private void SaveChanges()
        {
            try
            {
                // 直接写 plugins.json（绕过 PluginRegistry.Save 的 BuiltIn 过滤逻辑，
                // 确保用户禁用/启用的状态被忠实记录）
                var arr = new List<object>();
                foreach (var vm in Plugins)
                {
                    arr.Add(new Dictionary<string, object>
                    {
                        ["id"] = vm.Spec.Id,
                        ["display"] = vm.Display,
                        ["pkgName"] = vm.Spec.PkgName ?? "",
                        ["viaNpm"] = vm.ViaNpm,
                        ["source"] = vm.Spec.Source ?? "",
                        ["required"] = vm.Spec.Required,
                    });
                }
                var root = new Dictionary<string, object> { ["plugins"] = arr };
                string path = Path.Combine(Paths.RuntimeDir, PluginRegistry.UserPluginsFile);
                File.WriteAllText(path, JsonMini.Stringify(root), new System.Text.UTF8Encoding(false));
                StatusText.Text = "已保存 " + Plugins.Count + " 个插件到 plugins.json";
                App.Logger.Info("[插件] 保存 " + Plugins.Count + " 个插件到 plugins.json");
            }
            catch (Exception ex)
            {
                StatusText.Text = "保存失败: " + ex.Message;
                App.Logger.Error("[!] 保存 plugins.json 失败: " + ex.Message);
                MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, System.Windows.RoutedEventArgs e) => SaveChanges();

        private async void BtnRecheck_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            App.Logger.Info("[插件] 重新检测（含缺失补装）...");
            BtnRecheck.IsEnabled = false;
            StatusText.Text = "正在检查 / 补装，详见日志...";
            // EnsureAll：缺的插件自动补装，全程 Core 输出（[1/9] xxx 已安装...）进日志面板
            await Task.Run(() => PluginManager.EnsureAll(App.Logger));
            await RefreshAsync();
            BtnRecheck.IsEnabled = true;
            StatusText.Text = "检查完成";
        }

        private void BtnReset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "确认重置为内置默认 9 个插件？\n自定义插件会被移除，启用状态恢复默认。",
                "重置", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string path = Path.Combine(Paths.RuntimeDir, PluginRegistry.UserPluginsFile);
                if (File.Exists(path)) File.Delete(path);
                App.Logger.Info("[插件] 已删除 plugins.json，下次启动恢复默认。");
                _ = RefreshAsync();
            }
            catch (Exception ex)
            {
                App.Logger.Error("[!] 重置失败: " + ex.Message);
            }
        }

        private void BtnAddCustom_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new AddCustomPluginDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            var parsed = PluginInputParser.Parse(dlg.InputBox.Text?.Trim() ?? "");
            if (!parsed.Success)
            {
                MessageBox.Show("无法解析输入：\n" + parsed.Error, "格式错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string id = parsed.Id, display = parsed.Display, source = parsed.Source;
            bool viaNpm = parsed.Kind == PluginInputParser.Kind.Npm;

            // 重复检查
            if (Plugins.Any(p => p.Id == id))
            {
                MessageBox.Show("已存在同名插件 ID: " + id, "重复", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var spec = new PluginSpec(id, display, id, viaNpm, source);
            var vm = new PluginViewModel(spec, isInstalled: false) { IsCustomized = true };
            Plugins.Add(vm);
            StatusText.Text = "已添加自定义插件: " + id;
            App.Logger.Info("[插件] 添加自定义: " + id + " (" + (viaNpm ? "npm" : "git") + ")");
        }

        private void BtnRemoveRow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PluginViewModel vm) return;
            var r = MessageBox.Show(
                "确认删除插件 " + vm.Display + "？\n（需要点\"保存更改\"才真正写入 plugins.json）",
                "删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            Plugins.Remove(vm);
            StatusText.Text = "已移除 " + vm.Display;
            App.Logger.Info("[插件] 移除 " + vm.Id + "（待保存）");
        }
    }
}
