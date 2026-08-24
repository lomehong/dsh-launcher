// DshLauncher.Core — 卸载
using DshLauncher.Logging;

namespace DshLauncher
{
    public static class Uninstaller
    {
        public static int Run(bool purge, bool assumeYes, ILogger log = null)
        {
            log ??= new NullLogger();
            Console.WriteLine("============== 卸载 dsh-launcher ==============");
            Console.WriteLine();
            Console.WriteLine("以下目录将被删除：");
            Console.WriteLine("  [1] " + Paths.RuntimeDir + "    (便携 Node + dsh + 启动器数据)");

            string dshHome = Paths.DshHome();
            bool dshHomeExists = Directory.Exists(dshHome);
            bool dshHomeUnderRuntime = dshHome.StartsWith(Paths.RuntimeDir, StringComparison.OrdinalIgnoreCase);

            if (purge)
            {
                Console.WriteLine("  [2] " + dshHome + "    (用户 profile + plugins)  ← --purge");
            }
            else if (dshHomeExists && !dshHomeUnderRuntime)
            {
                Console.WriteLine();
                Console.WriteLine("  [保留] " + dshHome + "    (用户数据，默认保留；用 --purge 一并删除)");
            }

            Console.WriteLine();
            Console.WriteLine("提示：便携 Node.js 也将一并删除（约 100MB）。下次启动器双击即重新下载。");
            Console.WriteLine();

            if (!assumeYes)
            {
                Console.Write("确认删除？输入 y 继续，其它键取消: ");
                string answer;
                try { answer = Console.ReadLine(); }
                catch { answer = null; }
                if (answer == null || answer.Trim().ToLowerInvariant() != "y")
                {
                    Console.WriteLine("已取消。");
                    return ExitCodes.Success;
                }
            }

            int code = ExitCodes.Success;

            if (Directory.Exists(Paths.RuntimeDir))
            {
                Console.WriteLine("[1/2] 正在删除 " + Paths.RuntimeDir + " ...");
                string err;
                if (TryDeleteDirectory(Paths.RuntimeDir, out err))
                {
                    Console.WriteLine("      完成。");
                }
                else
                {
                    log.Error("[!] 删除失败: " + err);
                    log.Error("    提示：请先关闭所有 dsh web 窗口和正在运行的后台进程。");
                    code = ExitCodes.InternalError;
                }
            }
            else
            {
                Console.WriteLine("[1/2] 目录不存在，跳过: " + Paths.RuntimeDir);
            }

            if (purge && Directory.Exists(dshHome) && !dshHomeUnderRuntime)
            {
                Console.WriteLine("[2/2] 正在删除 " + dshHome + " ...");
                string err2;
                if (TryDeleteDirectory(dshHome, out err2))
                {
                    Console.WriteLine("      完成。");
                }
                else
                {
                    log.Error("[!] 删除失败: " + err2);
                    code = ExitCodes.InternalError;
                }
            }

            Console.WriteLine();
            if (code == ExitCodes.Success)
            {
                Console.WriteLine("卸载完成。");
                Console.WriteLine("如需完全重装，请双击 " + AppMain.AppName + ".exe；首次启动会重新下载 Node 和插件。");
            }
            else
            {
                Console.WriteLine("卸载部分完成（部分目录保留）。再次运行 --uninstall --yes 重试。");
            }
            return code;
        }

        private static bool TryDeleteDirectory(string dir, out string error)
        {
            error = null;
            // 优先用 cmd rd /s /q：处理长路径和文件锁比 .NET 更稳健
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int code = Shell.RunCmd("rd /s /q \"" + dir + "\"");
                if (code == 0 && !Directory.Exists(dir))
                {
                    error = null;
                    return true;
                }
                error = code == 0 ? "目录仍存在" : "rd 退出码 " + code;
                if (attempt < 2)
                {
                    Console.WriteLine("      重试 " + (attempt + 1) + "/3: " + error);
                    Thread.Sleep(2000);
                }
            }
            // fallback：先删 reparse point
            try { RemoveReparsePointsDeep(dir); } catch { }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!Directory.Exists(dir)) return true;
                    Directory.Delete(dir, true);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (attempt < 2)
                    {
                        Console.WriteLine("      备用重试 " + (attempt + 1) + "/3: " + error);
                        Thread.Sleep(2000);
                    }
                }
            }
            return false;
        }

        private static void RemoveReparsePointsDeep(string dir)
        {
            if (!Directory.Exists(dir)) return;
            DirectoryInfo root;
            try { root = new DirectoryInfo(dir); } catch { return; }
            foreach (DirectoryInfo sub in SafeEnumerateDirectories(root))
            {
                try
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                        sub.Delete(false);
                    else
                        RemoveReparsePointsDeep(sub.FullName);
                }
                catch { }
            }
        }

        private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo dir)
        {
            var result = new List<DirectoryInfo>();
            try
            {
                foreach (var d in dir.EnumerateDirectories())
                    result.Add(d);
            }
            catch { }
            return result;
        }
    }
}
