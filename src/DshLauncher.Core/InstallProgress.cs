// DshLauncher.Core — 安装进度事件
namespace DshLauncher
{
    public enum InstallPhase
    {
        Idle,
        NodeDownload,
        NodeExtract,
        DshInstall,
        DshUpdate,
        PnpmInstall,
        PluginScan,
        PluginInstall,
        PluginBuild,
        DshWebStart,
        Complete,
    }

    public sealed class InstallProgress
    {
        public InstallPhase Phase { get; init; }
        public string CurrentItem { get; init; } = "";
        public int CurrentIndex { get; init; }
        public int TotalItems { get; init; }
        public int Percent { get; init; }   // 0..100
        public string Message { get; init; } = "";
        public bool Indeterminate { get; init; }

        public override string ToString() =>
            $"[{Phase}] {CurrentIndex}/{TotalItems} {CurrentItem} ({Percent}%) {Message}";
    }
}
