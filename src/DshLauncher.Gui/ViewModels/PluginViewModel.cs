using System.ComponentModel;
using System.Runtime.CompilerServices;
using DshLauncher;

namespace DshLauncher.Gui.ViewModels
{
    /// <summary>
    /// 单个插件在 UI 层的视图模型：包装 PluginSpec，添加 IsInstalled / IsCustomized 等 UI 状态。
    /// 所有 setter 触发 PropertyChanged，DataGrid 自动刷新。
    /// </summary>
    public class PluginViewModel : INotifyPropertyChanged
    {
        public PluginSpec Spec { get; }

        /// <summary>用户勾选框（启用/禁用该插件）。</summary>
        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; Spec.Required = value; OnChanged(); OnChanged(nameof(RequiredText)); OnChanged(nameof(StatusText)); } }
        }

        public string Id => Spec.Id;
        public string Display => Spec.Display ?? Spec.Id;
        public string PkgName => Spec.PkgName ?? "";
        public string Source => Spec.Source ?? "";
        public bool ViaNpm => Spec.ViaNpm;
        public string SourceKindText => ViaNpm ? "npm" : "GitHub";
        public string RequiredText => Spec.Required ? "✓" : "—";
        public string SourceDisplay => ViaNpm ? PkgName : Source;

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set { if (_isInstalled != value) { _isInstalled = value; OnChanged(); OnChanged(nameof(StatusText)); OnChanged(nameof(StatusBrushKey)); } }
        }

        public string StatusText => IsEnabled
            ? (IsInstalled ? "✅ 已装" : "⚠ 未装")
            : "— 已禁用";

        public string StatusBrushKey => IsEnabled
            ? (IsInstalled ? "Brush.Ok" : "Brush.Warn")
            : "Brush.Muted";

        /// <summary>标记与 BuiltIn 不一致（用户改过）。</summary>
        private bool _isCustomized;
        public bool IsCustomized
        {
            get => _isCustomized;
            set { if (_isCustomized != value) { _isCustomized = value; OnChanged(); } }
        }

        public PluginViewModel(PluginSpec spec, bool isInstalled)
        {
            Spec = spec;
            _isEnabled = spec.Required;
            _isInstalled = isInstalled;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
