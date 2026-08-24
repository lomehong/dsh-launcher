// DshLauncher.Core — 自检（人读 + JSON）
using DshLauncher.Logging;

namespace DshLauncher
{
    public static class SelfCheck
    {
        public static int Run(bool jsonOut, ILogger log = null)
        {
            log ??= new NullLogger();
            return jsonOut ? RunJson(log) : RunHuman(log);
        }

        private static int RunHuman(ILogger log)
        {
            Console.WriteLine("============== 环境自检 ==============");
            Console.WriteLine("操作系统:    " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (x64)" : " (x86)"));
            Console.WriteLine("运行目录:    " + Paths.RuntimeDir);
            Console.WriteLine("npm 源:      " + Config.Current.Registry);
            Console.WriteLine();

            string marker = Path.Combine(Paths.RuntimeDir, "current-node.txt");
            bool nodeOk = false;
            if (File.Exists(marker))
            {
                string dir = File.ReadAllText(marker, Encoding.UTF8).Trim();
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, "node.exe")))
                {
                    Paths.NodeDir = dir;
                    Console.WriteLine("[OK] 便携版 Node.js:  " + PortableNode.NodeVersion() + "  (" + dir + ")");
                    nodeOk = true;
                }
            }
            if (!nodeOk) Console.WriteLine("[--] 便携版 Node.js:  未安装（首次启动会自动下载）");

            string installed = nodeOk ? DshInstaller.InstalledVersion() : "";
            if (installed.Length > 0) Console.WriteLine("[OK] dsh:             " + installed + "  (" + PortableNode.DshCmdPath() + ")");
            else Console.WriteLine("[--] dsh:             未安装（首次启动会自动安装）");

            int okPlugins = 0;
            List<PluginSpec> plugins = PluginRegistry.Load().Plugins;
            foreach (PluginSpec spec in plugins)
                if (PluginManager.ProfileHasPlugin(spec.PkgName)) okPlugins++;
            if (okPlugins == plugins.Count)
                Console.WriteLine("[OK] 默认插件:        " + okPlugins + "/" + plugins.Count + " 已安装");
            else
                Console.WriteLine("[--] 默认插件:        " + okPlugins + "/" + plugins.Count + " 已安装（启动时会自动补齐）");

            Console.WriteLine("端口 3080:    " + (Shell.PortInUse(3080) ? "被占用（可能已有 dsh web 在运行）" : "空闲"));

            Console.WriteLine();
            Console.WriteLine("正在向 npm 源查询最新 dsh 版本 ...");
            string latest = nodeOk ? DshInstaller.LatestVersion() : "";
            if (latest.Length > 0)
            {
                Console.WriteLine("最新 dsh:       " + latest);
                string i = Shell.GetVersionNumber(installed);
                if (i.Length > 0 && Shell.CompareVersions(i, Shell.GetVersionNumber(latest)) < 0)
                    Console.WriteLine("[提示] 可运行 --update（或双击\"升级dsh.bat\"）升级到最新版。");
            }
            else
            {
                Console.WriteLine("最新 dsh:       (查询失败，可能离线或网络问题)");
            }

            Console.WriteLine();
            Console.WriteLine("自检完成。");
            return 0;
        }

        private static int RunJson(ILogger log)
        {
            var root = new Dictionary<string, object>();
            root["launcherVersion"] = AppMain.Version;
            root["runtimeDir"] = Paths.RuntimeDir;
            root["registry"] = Config.Current.Registry;
            root["githubProxy"] = Config.Current.GithubProxy;
            root["integrity"] = Config.Current.Integrity;
            root["protectExternal"] = Config.Current.ProtectExternal;
            root["logLevel"] = AppLoggerCompat.LevelName();
            root["os"] = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (x64)" : " (x86)");

            string marker = Path.Combine(Paths.RuntimeDir, "current-node.txt");
            bool nodeOk = false;
            var nodeObj = new Dictionary<string, object>();
            if (File.Exists(marker))
            {
                string dir = File.ReadAllText(marker, Encoding.UTF8).Trim();
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, "node.exe")))
                {
                    Paths.NodeDir = dir;
                    nodeObj["status"] = "ok";
                    nodeObj["path"] = dir;
                    nodeObj["version"] = PortableNode.NodeVersion();
                    nodeOk = true;
                }
            }
            if (!nodeOk) { nodeObj["status"] = "missing"; nodeObj["path"] = null; nodeObj["version"] = null; }
            root["node"] = nodeObj;

            var dshObj = new Dictionary<string, object>();
            string installed = nodeOk ? DshInstaller.InstalledVersion() : "";
            string latest = nodeOk ? DshInstaller.LatestVersion() : "";
            string instV = Shell.GetVersionNumber(installed);
            string latV = Shell.GetVersionNumber(latest);
            dshObj["installed"] = installed.Length > 0 ? installed : null;
            dshObj["latest"] = latest.Length > 0 ? latest : null;
            dshObj["updateAvailable"] = instV.Length > 0 && latV.Length > 0 && Shell.CompareVersions(instV, latV) < 0;
            root["dsh"] = dshObj;

            var pluginArr = new List<object>();
            int okPlugins = 0;
            List<PluginSpec> plugins = PluginRegistry.Load().Plugins;
            foreach (PluginSpec spec in plugins)
            {
                var p = new Dictionary<string, object>();
                p["id"] = spec.Id;
                p["display"] = spec.Display;
                p["pkgName"] = spec.PkgName;
                p["viaNpm"] = spec.ViaNpm;
                p["source"] = spec.Source;
                p["required"] = spec.Required;
                bool present = PluginManager.ProfileHasPlugin(spec.PkgName);
                if (present) okPlugins++;
                p["status"] = present ? "ok" : "missing";
                pluginArr.Add(p);
            }
            root["plugins"] = pluginArr;
            root["pluginsOkCount"] = okPlugins;
            root["pluginsTotalCount"] = plugins.Count;

            root["port"] = 3080;
            root["portInUse"] = Shell.PortInUse(3080);
            // 语义拆分：healthy = 环境完整（Node/dsh/插件齐全，可装可跑）；
            // webRunning = 服务是否在跑。两者独立——"装好了且正在运行"是最健康态而非病态。
            bool healthy = nodeOk && installed.Length > 0 && okPlugins == plugins.Count;
            bool webRunning = (bool)root["portInUse"];
            root["webRunning"] = webRunning;
            root["healthy"] = healthy;

            Console.WriteLine(JsonMini.Stringify(root));
            return healthy ? 0 : 1;
        }
    }

    /// <summary>从 ILogger 反射出 Config 中设置的级别字符串（M1 临时，等 WPF 改成 INotifyPropertyChanged）。</summary>
    internal static class AppLoggerCompat
    {
        public static string LevelName() => Config.Current.LogLevel;
    }
}
