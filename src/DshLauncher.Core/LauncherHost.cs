// DshLauncher.Core — 统一入口（CLI Host + 后续 WPF Host 都用它）
using DshLauncher.Logging;

namespace DshLauncher
{
    public static class LauncherHost
    {
        public sealed class Options
        {
            public bool Check { get; set; }
            public bool JsonOutput { get; set; }
            public bool Update { get; set; }
            public bool InstallOnly { get; set; }
            public bool Uninstall { get; set; }
            public bool Purge { get; set; }
            public bool AssumeYes { get; set; }
            public bool Help { get; set; }
            public bool StartWeb { get; set; } = true;   // 默认启动 web（CLI 完整启动流程）
        }

        /// <summary>
        /// 解析命令行参数（大小写不敏感）。
        /// </summary>
        public static Options ParseArgs(string[] args)
        {
            var o = new Options();
            foreach (string a in args)
            {
                string l = a.ToLowerInvariant();
                switch (l)
                {
                    case "--check": o.Check = true; break;
                    case "--json": o.JsonOutput = true; break;
                    case "--update": o.Update = true; break;
                    case "--install-only": o.InstallOnly = true; break;
                    case "--no-web": o.StartWeb = false; break;
                    case "--uninstall": o.Uninstall = true; break;
                    case "--purge": o.Purge = true; break;
                    case "--yes": case "-y": o.AssumeYes = true; break;
                    case "--help": case "-h": o.Help = true; break;
                }
            }
            return o;
        }

        public static int Run(Options opts, ILogger log,
            Action<InstallProgress> onProgress = null,
            CancellationToken ct = default)
        {
            // 1) --uninstall 最优先，独立路径（不需要 Config/Mutex）
            if (opts.Uninstall)
                return Uninstaller.Run(opts.Purge, opts.AssumeYes, log);

            // 2) --help
            if (opts.Help)
            {
                PrintHelp();
                return ExitCodes.Success;
            }

            // 3) 单实例锁
            using (var lock_ = SingleInstanceLock.TryAcquire())
            {
                if (lock_ == null)
                {
                    log.Error("[!] 已有 dsh-launcher 在运行。请先关闭原窗口。");
                    return ExitCodes.AlreadyRunning;
                }

                // 4) 加载配置 + 加载日志级别
                Config.Load();
                ApplyLogLevel(log);

                AppLogger.Banner(log, AppMain.Version, Paths.RuntimeDir);

                // 5) --check
                if (opts.Check)
                    return SelfCheck.Run(opts.JsonOutput, log);

                // 6) Node
                if (PortableNode.Ensure(log) == null)
                {
                    log.Error("[!] Node.js 环境准备失败（可能需要联网）。");
                    Shell.Pause();
                    return ExitCodes.NodeSetupFailed;
                }

                // 7) dsh install/update
                if (opts.Update)
                {
                    int r = DshInstaller.Ensure(true, log);
                    if (r == 0) PluginManager.EnsureAll(log, onProgress, ct);
                    Shell.Pause();
                    return r;
                }

                if (opts.InstallOnly)
                {
                    int r = DshInstaller.Ensure(false, log);
                    if (r == 0) r = PluginManager.EnsureAll(log, onProgress, ct);
                    Shell.Pause();
                    return r;
                }

                // 8) 默认完整流程
                int dsh = DshInstaller.Ensure(false, log);
                if (dsh != 0)
                {
                    log.Error("[!] dsh 安装/更新失败。");
                    Shell.Pause();
                    return ExitCodes.DshInstallFailed;
                }
                int pCode = PluginManager.EnsureAll(log, onProgress, ct);

                // 9) 启动 web（除非 --no-web）
                if (!opts.StartWeb) return pCode != 0 ? ExitCodes.PluginInstallFailed : ExitCodes.Success;

                if (Shell.PortInUse(3080))
                {
                    log.Info("[提示] 端口 3080 已有服务在运行（可能已有 dsh web），直接打开浏览器。");
                    Shell.OpenBrowser("http://127.0.0.1:3080");
                    Shell.Pause();
                    return 0;
                }

                Console.WriteLine();
                Console.WriteLine("[4/4] 正在启动 dsh web ...");
                Console.WriteLine("      启动完成后将自动打开浏览器（一般需要 10~60 秒），关闭本窗口即停止服务。");
                Console.WriteLine();
                return WebLauncher.Start(log, pCode, ct);
            }
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

        private static void PrintHelp()
        {
            Console.WriteLine(AppMain.AppName + " v" + AppMain.Version);
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  dsh-launcher.exe                  完整启动流程（默认）");
            Console.WriteLine("  dsh-launcher.exe --check          环境自检（人类可读）");
            Console.WriteLine("  dsh-launcher.exe --check --json   环境自检（机器可读 JSON）");
            Console.WriteLine("  dsh-launcher.exe --update         强制更新 dsh");
            Console.WriteLine("  dsh-launcher.exe --install-only   安装/更新环境，不启动 web");
            Console.WriteLine("  dsh-launcher.exe --no-web         同 install-only");
            Console.WriteLine("  dsh-launcher.exe --uninstall      删除启动器自带环境（保留 ~/.dsh）");
            Console.WriteLine("  dsh-launcher.exe --uninstall --purge  连用户数据一并清空");
            Console.WriteLine("  dsh-launcher.exe --uninstall --yes   跳过交互确认（非交互 / CI）");
        }
    }

    /// <summary>GUI 启动 dsh web 的占位（M3 实现）。M1 暂时用同样的 Console+Job Object 流程。</summary>
    public static class WebLauncher
    {
        public static int Start(ILogger log, int pluginExitCode, CancellationToken ct)
        {
            string dsh = PortableNode.DshCmdPath();
            if (dsh == null)
            {
                log.Error("[!] 找不到 dsh.cmd，无法启动。");
                return ExitCodes.DshInstallFailed;
            }
            // 后台 poll-and-open
            var waiter = new Thread(() =>
            {
                for (int i = 0; i < 180; i++)
                {
                    if (ct.IsCancellationRequested) return;
                    if (Shell.PortInUse(3080))
                    {
                        log.Info("[提示] 服务已就绪，正在打开浏览器: http://127.0.0.1:3080");
                        Shell.OpenBrowser("http://127.0.0.1:3080");
                        return;
                    }
                    Thread.Sleep(500);
                }
                log.Warn("[提示] 等待服务就绪超时，请手动打开浏览器访问 http://127.0.0.1:3080");
            }) { IsBackground = true };
            waiter.Start();

            // M1: 直接同步等；M3 会改成异步 + 取消
            // Job Object 真正生效：web 进程 spawn 后立即 AssignProcessToJobObject（在 Shell.RunCmd 内），
            // launcher 进程退出（含关窗/被杀）→ job 句柄关闭 → 内核终止整棵进程树（cmd/node/子进程）。
            using (JobObject job = JobObject.Create())
            {
                int code = Shell.RunCmd("\"" + dsh + "\" web", job);
                log.Info("dsh web 已退出（退出码 " + code + "）。");
                if (pluginExitCode != 0 && code == 0) code = ExitCodes.PluginInstallFailed;
                return code == 0 ? ExitCodes.Success : code;
            }
        }
    }

    /// <summary>
    /// Windows Job Object：把 dsh web 进程纳入，关闭 launcher 时自动杀掉子进程。
    /// .NET Framework 没有原生 JobObject，corefx 也没有，所以走 P/Invoke。
    /// </summary>
    public sealed class JobObject : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private bool _disposed;

        public static JobObject Create()
        {
            var job = new JobObject();
            IntPtr h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return job;
            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE };
            var ext = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = info };
            IntPtr p = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(ext));
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(ext, p, false);
                if (!SetInformationJobObject(h, JobObjectInfoType.ExtendedLimitInformation, p,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(ext)))
                {
                    CloseHandle(h);
                    return new JobObject();
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(p); }
            job.Handle = h;
            return job;
        }

        public bool AddProcess(IntPtr processHandle)
        {
            if (Handle == IntPtr.Zero) return false;
            return AssignProcessToJobObject(Handle, processHandle);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != IntPtr.Zero) { CloseHandle(Handle); Handle = IntPtr.Zero; }
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        private enum JobObjectInfoType { ExtendedLimitInformation = 9 }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
    }

    /// <summary>单实例 Mutex（按用户会话 Local 命名空间：同一用户防双开，不同 Windows 用户互不干扰——安装本就按用户隔离在 %LOCALAPPDATA%）。</summary>
    public sealed class SingleInstanceLock : IDisposable
    {
        private Mutex _mutex;
        private SingleInstanceLock(Mutex m) { _mutex = m; }

        public static SingleInstanceLock TryAcquire()
        {
            string name = @"Local\dsh-launcher-single-instance-v2";
            bool createdNew;
            Mutex m = new Mutex(true, name, out createdNew);
            if (!createdNew)
            {
                try { m.Close(); } catch { }
                return null;
            }
            return new SingleInstanceLock(m);
        }

        public void Dispose()
        {
            try { _mutex.ReleaseMutex(); } catch { }
            try { _mutex.Close(); } catch { }
            try { _mutex.Dispose(); } catch { }
        }
    }

    /// <summary>兼容旧 AppLogger.Banner 调用（过渡期间仍有些地方用到）。</summary>
    internal static class AppLogger
    {
        public static void Banner(ILogger log, string version, string runtimeDir)
        {
            Console.WriteLine("==============================================================");
            Console.WriteLine("  DeepSeek Harness (dsh) 一键启动器  v" + version);
            Console.WriteLine("  自动：便携版 Node.js -> dsh -> 默认插件(9个) -> 启动 dsh web -> 打开浏览器");
            Console.WriteLine("  运行目录: " + runtimeDir);
            Console.WriteLine("==============================================================");
            Console.WriteLine();
        }
    }
}
