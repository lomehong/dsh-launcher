using System.ComponentModel;
using System.Runtime.CompilerServices;
using DshLauncher;

namespace DshLauncher.Gui.ViewModels
{
    /// <summary>
    /// 设置 Tab 的 ViewModel：直接持有 Config.Current 引用，setter 写回。
    /// Save() 调用 Config.Save 持久化到 config.json；LoadFromConfig() 在打开 Tab 时从 Core 拉最新。
    /// </summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public string[] IntegrityLevels { get; } = new[] { "strict", "lax", "none" };
        public string[] LogLevels { get; } = new[] { "silent", "info", "verbose" };

        private string _registry;
        public string Registry
        {
            get => _registry;
            set { if (_registry != value) { _registry = value; OnChanged(); } }
        }

        private string _githubProxy;
        public string GithubProxy
        {
            get => _githubProxy;
            set { if (_githubProxy != value) { _githubProxy = value; OnChanged(); } }
        }

        private string _integrity;
        public string Integrity
        {
            get => _integrity;
            set { if (_integrity != value) { _integrity = value; OnChanged(); } }
        }

        private bool _protectExternal;
        public bool ProtectExternal
        {
            get => _protectExternal;
            set { if (_protectExternal != value) { _protectExternal = value; OnChanged(); } }
        }

        private string _logLevel;
        public string LogLevel
        {
            get => _logLevel;
            set { if (_logLevel != value) { _logLevel = value; OnChanged(); } }
        }

        private string _pinnedNodeVersion;
        public string PinnedNodeVersion
        {
            get => _pinnedNodeVersion;
            set { if (_pinnedNodeVersion != value) { _pinnedNodeVersion = value; OnChanged(); } }
        }

        public void LoadFromConfig()
        {
            var c = Config.Current;
            _registry = c.Registry;
            _githubProxy = c.GithubProxy ?? "";
            _integrity = c.Integrity;
            _protectExternal = c.ProtectExternal;
            _logLevel = c.LogLevel;
            _pinnedNodeVersion = c.PinnedNodeVersion;
            OnChanged();
            OnChanged(nameof(GithubProxy));
            OnChanged(nameof(Registry));
            OnChanged(nameof(Integrity));
            OnChanged(nameof(ProtectExternal));
            OnChanged(nameof(LogLevel));
            OnChanged(nameof(PinnedNodeVersion));
        }

        public (bool ok, string err) Save()
        {
            var c = Config.Current;
            c.Registry = Registry;
            c.GithubProxy = GithubProxy ?? "";
            c.Integrity = Integrity;
            c.ProtectExternal = ProtectExternal;
            c.LogLevel = LogLevel;
            c.PinnedNodeVersion = PinnedNodeVersion;
            try
            {
                Config.Save(c);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
