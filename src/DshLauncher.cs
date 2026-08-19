// DshLauncher.cs
// DeepSeek Harness (dsh) 一键启动器 —— 通用 Windows 版本
//
// 双击即可自动完成：便携版 Node.js 下载 -> 安装/更新 dsh -> 安装 9 个默认插件
// (at-file / genui / visualize / automation / better-sidebar / mnemon /
// vision-toolkit / market / yuyi(御驿)；GitHub 源直连不可达时自动切换国内代理)
// -> 启动 dsh web -> 自动打开浏览器。
// 外部/开发安装（profile 依赖 link:/file: 指向启动器目录之外）同样受保护：
// 其 node_modules/@deepseek-ai 会被自动统一为指向便携 harness 依赖集的
// junction，避免检出自带副本造成 typert 双实例 -> /api/<ns>/* 404。
// 适用于任何 Windows 10/11 电脑（64 位），无需预先安装 Node.js，无需管理员权限，
// 不修改系统已有的 Node.js 环境。所有运行数据放在 %LOCALAPPDATA%\dsh-launcher 下，
// 删除该目录即可完全卸载。
//
// 编译（.NET Framework 4.x，Windows 10/11 自带）：
//   "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /optimize+ /target:exe
//       /out:dsh一键启动.exe /r:System.IO.Compression.FileSystem.dll DshLauncher.cs
//
// 用法：
//   dsh一键启动.exe              启动（自动检查/安装/更新环境，然后启动 dsh web 并打开浏览器）
//   dsh一键启动.exe --check      环境自检（只检查并报告，不安装、不启动）
//   dsh一键启动.exe --update     强制检查并更新 dsh 到最新版
//   dsh一键启动.exe --install-only  仅安装/更新环境，不启动 dsh web
//
// 可选配置（%LOCALAPPDATA%\dsh-launcher\config.txt，每行一个配置）：
//   registry=https://registry.npmmirror.com   # npm 源（默认国内镜像）
//   githubProxy=https://ghfast.top/           # GitHub 下载代理前缀（默认直连失败后自动尝试内置代理列表）
// 环境变量 DSH_REGISTRY / DSH_GITHUB_PROXY 分别优先于配置文件。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class Program
{
    // ---------- 常量 ----------
    private const string APP_NAME = "DeepSeek Harness (dsh) 一键启动器";
    private const string LAUNCHER_VERSION = "1.3.1";
    private const int DEFAULT_PORT = 3080;
    private const string DEFAULT_REGISTRY = "https://registry.npmmirror.com";
    private const string NODE_MIRROR_NPMMIRROR = "https://registry.npmmirror.com/-/binary/node";
    private const string NODE_MIRROR_OFFICIAL = "https://nodejs.org/dist";
    private const string NODE_INDEX_NPMMIRROR = "https://registry.npmmirror.com/-/binary/node/index.json";
    private const string NODE_INDEX_OFFICIAL = "https://nodejs.org/dist/index.json";
    // 版本发现失败（离线等）时的兜底版本：2026-08 的 Node 最新 LTS
    private const string PINNED_NODE = "v24.19.0";
    // GitHub 源码包下载
    private const string CODELOAD = "https://codeload.github.com";
    // 内置的国内 GitHub 代理前缀（按顺序尝试；config.txt 的 githubProxy= 可整体替换）
    private static readonly string[] GITHUB_PROXIES = new[]
    {
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://mirror.ghproxy.com/",
        "https://ghproxy.net/",
    };

    /// <summary>一个默认插件：npm 源或 GitHub 源码。</summary>
    private sealed class PluginSpec
    {
        public readonly string Id;       // 本地目录名
        public readonly string Display;  // 显示名
        public readonly string PkgName;  // npm 包名（bundles/dependencies 检查用）
        public readonly bool ViaNpm;     // true=npm 源；false=GitHub 源码
        public readonly string Source;   // npm: 包名；github: "owner/repo/branch"
        public PluginSpec(string id, string display, string pkgName, bool viaNpm, string source)
        {
            Id = id; Display = display; PkgName = pkgName; ViaNpm = viaNpm; Source = source;
        }
    }

    /// <summary>默认安装的 9 个插件（第 9 个 yuyi 需构建，且尊重用户手动/开发安装）。</summary>
    private static readonly PluginSpec[] DEFAULT_PLUGINS = new[]
    {
        new PluginSpec("dsh-at-file", "at-file", "dsh-at-file", false, "omdsh-dev/dsh-at-file/main"),
        new PluginSpec("dsh-genui", "genui", "@omdsh-dev/dsh-genui", false, "omdsh-dev/dsh-genui/main"),
        new PluginSpec("dsh-visualize", "visualize", "@dsh-external/dsh-visualize", false, "Nagi-ovo/dsh-visualize/main"),
        new PluginSpec("dsh-automation", "automation", "@dsh-external/dsh-automation", false, "titanwings/dsh-automation/main"),
        new PluginSpec("dsh-better-sidebar", "better-sidebar", "dsh-better-sidebar", true, "dsh-better-sidebar"),
        new PluginSpec("dsh-mnemon", "mnemon", "dsh-mnemon", true, "dsh-mnemon"),
        new PluginSpec("dsh-vision-toolkit", "vision-toolkit", "@anionex/dsh-vision-toolkit", true, "@anionex/dsh-vision-toolkit"),
        new PluginSpec("dsh-market", "market", "@dsh-market/plugin", true, "@dsh-market/plugin"),
        new PluginSpec("dsh-yuyi", "yuyi(御驿)", "dsh-yuyi", false, "lomehong/dsh-yuyi/main"),
    };

    /// <summary>yuyi 会话工具 preset 的 id（基于内置 standard 追加 dsh-yuyi/tools 行）。</summary>
    private const string YUYI_PRESET_ID = "standard-yuyi";

    // ---------- 运行状态 ----------
    private static string _runtimeDir;   // %LOCALAPPDATA%\dsh-launcher
    private static string _cacheDir;     // _runtimeDir\cache
    private static string _npmCache;     // _runtimeDir\npm-cache
    private static string _pluginsDir;   // _runtimeDir\plugins
    private static string _nodeDir;      // _runtimeDir\node\node-vX-win-x64
    private static string _registry = DEFAULT_REGISTRY;
    private static string _githubProxy = ""; // 用户自定义 GitHub 代理前缀

    // =====================================================================
    // 入口
    // =====================================================================
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        try { Console.Title = APP_NAME; } catch { }
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        InitPaths();

        bool check = Has(args, "--check");
        bool update = Has(args, "--update");
        bool installOnly = Has(args, "--install-only");

        Banner();

        if (check)
        {
            int r = RunCheck();
            Pause();
            return r;
        }

        string nodeDir = EnsurePortableNode();
        if (nodeDir == null)
        {
            Fail("Node.js 环境准备失败（可能需要联网），请检查网络后重新运行。");
            return 1;
        }

        if (update)
        {
            EnsureDsh(true);
            EnsurePlugins(false);
            Pause();
            return 0;
        }

        if (installOnly)
        {
            int r = EnsureDsh(false);
            if (r == 0) r = EnsurePlugins(false);
            Pause();
            return r;
        }

        // ---------- 默认：完整启动流程 ----------
        if (EnsureDsh(false) != 0)
        {
            Fail("dsh 安装/更新失败，请检查网络后重试。");
            return 1;
        }
        EnsurePlugins(false);

        if (PortInUse(DEFAULT_PORT))
        {
            Console.WriteLine();
            Console.WriteLine("[提示] 端口 {0} 已有服务在运行（可能已有 dsh web 在运行），直接打开浏览器。", DEFAULT_PORT);
            OpenBrowser("http://127.0.0.1:" + DEFAULT_PORT);
            Pause();
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("[4/4] 正在启动 dsh web ...");
        Console.WriteLine("      启动完成后将自动打开浏览器（一般需要 10~60 秒），关闭本窗口即停止服务。");
        Console.WriteLine();
        StartWeb();
        return 0;
    }

    // =====================================================================
    // 环境初始化
    // =====================================================================
    private static void InitPaths()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _runtimeDir = Path.Combine(local, "dsh-launcher");
        _cacheDir = Path.Combine(_runtimeDir, "cache");
        _npmCache = Path.Combine(_runtimeDir, "npm-cache");
        _pluginsDir = Path.Combine(_runtimeDir, "plugins");
        try
        {
            Directory.CreateDirectory(_runtimeDir);
            Directory.CreateDirectory(_cacheDir);
            Directory.CreateDirectory(_npmCache);
            Directory.CreateDirectory(_pluginsDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] 无法创建运行目录 " + _runtimeDir + " : " + ex.Message);
        }

        // npm 源：环境变量优先，其次 config.txt 里的 registry= 行，最后默认国内镜像
        string envReg = Environment.GetEnvironmentVariable("DSH_REGISTRY");
        if (!string.IsNullOrWhiteSpace(envReg)) _registry = envReg.Trim();
        else
        {
            try
            {
                string cfg = Path.Combine(_runtimeDir, "config.txt");
                if (File.Exists(cfg))
                {
                    foreach (string line in File.ReadAllLines(cfg, Encoding.UTF8))
                    {
                        string s = line.Trim();
                        if (s.StartsWith("registry=", StringComparison.OrdinalIgnoreCase))
                        {
                            string r = s.Substring("registry=".Length).Trim();
                            if (r.Length > 0) { _registry = r; break; }
                        }
                    }
                }
            }
            catch { }
        }

        // GitHub 代理：环境变量优先，其次 config.txt 里的 githubProxy= 行
        string envProxy = Environment.GetEnvironmentVariable("DSH_GITHUB_PROXY");
        if (!string.IsNullOrWhiteSpace(envProxy)) _githubProxy = envProxy.Trim();
        else
        {
            try
            {
                string cfg = Path.Combine(_runtimeDir, "config.txt");
                if (File.Exists(cfg))
                {
                    foreach (string line in File.ReadAllLines(cfg, Encoding.UTF8))
                    {
                        string s = line.Trim();
                        if (s.StartsWith("githubProxy=", StringComparison.OrdinalIgnoreCase))
                        {
                            string r = s.Substring("githubProxy=".Length).Trim();
                            if (r.Length > 0) { _githubProxy = r; break; }
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static void Banner()
    {
        Console.WriteLine("==============================================================");
        Console.WriteLine("  " + APP_NAME + "  v" + LAUNCHER_VERSION);
        Console.WriteLine("  自动：便携版 Node.js -> dsh -> 默认插件(9个) -> 启动 dsh web -> 打开浏览器");
        Console.WriteLine("  运行目录: " + _runtimeDir);
        Console.WriteLine("==============================================================");
        Console.WriteLine();
    }

    // =====================================================================
    // 便携版 Node.js
    // =====================================================================
    private static string EnsurePortableNode()
    {
        Console.WriteLine("[1/4] 检查便携版 Node.js ...");
        string marker = Path.Combine(_runtimeDir, "current-node.txt");
        if (File.Exists(marker))
        {
            string dir = File.ReadAllText(marker, Encoding.UTF8).Trim();
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, "node.exe")))
            {
                _nodeDir = dir;
                Console.WriteLine("      已就绪: " + NodeVersion() + "  (" + dir + ")");
                return dir;
            }
        }

        Console.WriteLine("      未找到便携版 Node.js，开始下载（首次使用约需下载 35MB，请耐心等待）...");
        string ver = FindLatestNodeLts();
        string zipName = "node-" + ver + "-win-x64.zip";
        string zipPath = Path.Combine(_cacheDir, zipName);
        if (!File.Exists(zipPath))
        {
            bool ok = false;
            string[] urls = new[]
            {
                NODE_MIRROR_NPMMIRROR + "/" + ver + "/" + zipName,
                NODE_MIRROR_OFFICIAL + "/" + ver + "/" + zipName,
            };
            foreach (string u in urls)
            {
                Console.WriteLine("      下载 " + ver + ": " + u);
                if (DownloadFile(u, zipPath + ".part"))
                {
                    try { File.Move(zipPath + ".part", zipPath); } catch { }
                    ok = true;
                    break;
                }
                try { if (File.Exists(zipPath + ".part")) File.Delete(zipPath + ".part"); } catch { }
            }
            if (!ok) return null;
        }

        string extractRoot = Path.Combine(_runtimeDir, "node");
        string extractDir = Path.Combine(extractRoot, "node-" + ver + "-win-x64");
        if (!File.Exists(Path.Combine(extractDir, "node.exe")))
        {
            Console.WriteLine("      解压中 ...");
            try
            {
                Directory.CreateDirectory(extractRoot);
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractRoot);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] 解压失败: " + ex.Message);
                return null;
            }
        }

        _nodeDir = extractDir;
        try { File.WriteAllText(marker, extractDir, Encoding.UTF8); } catch { }
        AddNodeToPath();
        Console.WriteLine("      完成: " + NodeVersion());
        return extractDir;
    }

    /// <summary>从镜像索引里找出最新 LTS 版本号；全部失败时用兜底版本。</summary>
    private static string FindLatestNodeLts()
    {
        foreach (string url in new[] { NODE_INDEX_NPMMIRROR, NODE_INDEX_OFFICIAL })
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "dsh-launcher/" + LAUNCHER_VERSION);
                    string json = wc.DownloadString(url);
                    Version best = null;
                    string bestVer = null;
                    foreach (Match m in Regex.Matches(json, "\"version\":\"v([0-9]+\\.[0-9]+\\.[0-9]+)\"(?:[^{}]*?)\"lts\":\"([^\"]+)\""))
                    {
                        Version v;
                        if (!Version.TryParse(m.Groups[1].Value, out v)) continue;
                        if (best == null || v > best) { best = v; bestVer = m.Groups[1].Value; }
                    }
                    if (bestVer != null) return "v" + bestVer;
                }
            }
            catch { }
        }
        return PINNED_NODE;
    }

    private static void AddNodeToPath()
    {
        if (_nodeDir == null) return;
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string p in path.Split(';'))
        {
            if (string.Equals(p.TrimEnd('\\'), _nodeDir, StringComparison.OrdinalIgnoreCase)) return;
        }
        Environment.SetEnvironmentVariable("PATH", _nodeDir + ";" + path);
    }

    // =====================================================================
    // dsh 安装 / 更新
    // =====================================================================
    private static int EnsureDsh(bool forceUpdate)
    {
        Console.WriteLine("[2/4] 检查 dsh ...");
        if (_nodeDir == null) return 1;
        string npm = NpmCmd();
        string installed = InstalledDshVersion();

        if (installed.Length > 0)
        {
            Console.WriteLine("      已安装: " + installed);
            if (!forceUpdate && !AutoUpdateDue())
            {
                Console.WriteLine("      今日已检查过更新，跳过。");
                return 0;
            }
            Console.WriteLine("      正在查询最新版本 ...");
            string latest = LatestDshVersion();
            string instV = GetVersionNumber(installed);
            string latV = GetVersionNumber(latest);
            if (latV.Length == 0 || instV == latV)
            {
                Console.WriteLine("      已是最新版本" + (latest.Length > 0 ? " (" + latest + ")" : "") + "。");
                WriteAutoUpdateStamp();
                return 0;
            }
            Console.WriteLine("      发现新版本 " + latest + "，正在自动更新 ...");
        }
        else
        {
            Console.WriteLine("      未安装，正在安装 @deepseek-ai/dsh（首次可能需要几分钟）...");
        }

        string action = installed.Length > 0 ? "install -g @deepseek-ai/dsh@latest" : "install -g @deepseek-ai/dsh";
        string cmdLine = "\"" + npm + "\" " + action
            + " --registry=" + _registry
            + " --cache=\"" + _npmCache + "\" --no-fund --no-audit --loglevel=notice";
        int code = RunCmd(cmdLine);
        if (code != 0)
        {
            Console.WriteLine("[!] npm 命令失败（退出码 " + code + "）。");
            return code;
        }
        string after = InstalledDshVersion();
        Console.WriteLine("      完成: " + (after.Length > 0 ? after : "dsh 已安装"));
        WriteAutoUpdateStamp();
        return 0;
    }

    private static string NpmCmd()
    {
        return _nodeDir == null ? "npm" : Path.Combine(_nodeDir, "npm.cmd");
    }

    private static string InstalledDshVersion()
    {
        string dsh = DshCmdPath();
        if (dsh == null) return "";
        string outTxt = RunCapture("\"" + dsh + "\" --version");
        string[] lines = StripNpmWarns(outTxt).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
    }

    private static string LatestDshVersion()
    {
        string outTxt = RunCapture("\"" + NpmCmd() + "\" view @deepseek-ai/dsh version --registry=" + _registry + " --no-fund --no-audit");
        return StripNpmWarns(outTxt).Trim();
    }

    /// <summary>去掉 npm 输出的 "npm warn ..." 噪音行，保证版本号干净可解析。</summary>
    private static string StripNpmWarns(string text)
    {
        if (text == null) return "";
        StringBuilder sb = new StringBuilder();
        foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = line.Trim();
            if (t.StartsWith("npm warn", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static string DshCmdPath()
    {
        if (_nodeDir == null) return null;
        string p = Path.Combine(_nodeDir, "dsh.cmd");
        return File.Exists(p) ? p : null;
    }

    private static string NodeVersion()
    {
        if (_nodeDir == null) return "";
        string outTxt = RunCapture("\"" + Path.Combine(_nodeDir, "node.exe") + "\" --version");
        string[] lines = outTxt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "(未知)";
    }

    private static bool AutoUpdateDue()
    {
        string f = Path.Combine(_runtimeDir, "last-update.txt");
        try
        {
            return !File.Exists(f) || File.ReadAllText(f, Encoding.UTF8).Trim() != DateTime.Today.ToString("yyyy-MM-dd");
        }
        catch { return true; }
    }

    private static void WriteAutoUpdateStamp()
    {
        try { File.WriteAllText(Path.Combine(_runtimeDir, "last-update.txt"), DateTime.Today.ToString("yyyy-MM-dd"), Encoding.UTF8); } catch { }
    }

    // =====================================================================
    // 默认插件（9 个：GitHub 源码或 npm 源，GitHub 直连不可达时走国内代理）
    // =====================================================================
    private static string DshHome()
    {
        string env = Environment.GetEnvironmentVariable("DSH_HOME");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    }

    private static string ProfileDir()
    {
        return Path.Combine(DshHome(), "profiles", "web");
    }

    /// <summary>确保 9 个默认插件都已注册进 web profile。</summary>
    private static int EnsurePlugins(bool force)
    {
        Console.WriteLine("[3/4] 检查默认插件 ...");
        if (_nodeDir == null) return 1;
        if (InstallPnpm() != 0) return 1;
        EnsureProfile();
        EnsurePeersJunction(Path.Combine(ProfileDir(), "node_modules"));
        int installed = 0, failed = 0;
        for (int i = 0; i < DEFAULT_PLUGINS.Length; i++)
        {
            PluginSpec spec = DEFAULT_PLUGINS[i];
            string externalDir = spec.ViaNpm ? null : ExternalPluginDir(spec);
            bool ready = spec.ViaNpm
                ? ProfileHasPlugin(spec.PkgName)
                : ProfileHasPlugin(spec.PkgName) && (PluginDirReady(spec) || externalDir != null);
            if (ready && !force)
            {
                Console.WriteLine("      [" + (i + 1) + "/" + DEFAULT_PLUGINS.Length + "] " + spec.Display + " 已安装。");
                if (externalDir != null)
                    Console.WriteLine("          外部/开发安装: " + externalDir);
                installed++;
                continue;
            }
            Console.WriteLine("      [" + (i + 1) + "/" + DEFAULT_PLUGINS.Length + "] 安装 " + spec.Display + " ...");
            int code = spec.ViaNpm ? InstallPluginNpm(spec) : InstallPluginGithub(spec);
            if (code == 0 && ProfileHasPlugin(spec.PkgName))
            {
                Console.WriteLine("      " + spec.Display + " 已就绪。");
                installed++;
            }
            else
            {
                Console.WriteLine("[!] " + spec.Display + " 安装失败（不影响其他步骤）。");
                failed++;
            }
        }
        // pnpm 的 install/add 可能清理 node_modules 里的未知 junction，最后再确保一次
        EnsurePeersJunction(Path.Combine(ProfileDir(), "node_modules"));
        // 外部/开发安装（link:/file: 指向启动器目录之外）同样必须满足单实例不变量：
        // 检出自带的 @deepseek-ai 真实目录会让插件宿主半边挂载失败（/api/<ns>/* 404）。
        // 幂等：junction 已正确时秒过；外部检出跑过 pnpm install 后重启启动器即可自愈。
        UnifyExternalPluginPeers();
        // yuyi 会话工具 preset：已装 yuyi 时，确保 standard-yuyi 存在并设为默认（幂等）
        if (ProfileHasPlugin("dsh-yuyi"))
        {
            EnsureYuyiPreset();
            PatchProfileDefaultPreset();
        }
        Console.WriteLine("      插件检查完成：成功 " + installed + "，失败 " + failed + "。");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>GitHub 插件目录是否已就绪（源码 + 依赖 + lib + peers junction + 标记）。</summary>
    private static bool PluginDirReady(PluginSpec spec)
    {
        string target = Path.Combine(_pluginsDir, spec.Id);
        return File.Exists(Path.Combine(target, ".dsh-ready"))
            && Directory.Exists(Path.Combine(target, "node_modules", "@deepseek-ai"))
            && File.Exists(Path.Combine(target, "lib", "index.js"));
    }

    /// <summary>
    /// GitHub 插件是否为用户手动/开发安装，并返回其目录：profile 依赖以 link:/file:
    /// 指向启动器 plugins 目录之外且目录存在（例如 yuyi 链到开发检出）。
    /// 返回 null = 非外部安装（无依赖、版本号写法、指向启动器目录或路径失效），
    /// 此时尊重启动器自装副本或常规安装，不替换用户环境。
    /// </summary>
    private static string ExternalPluginDir(PluginSpec spec)
    {
        string pj = Path.Combine(ProfileDir(), "package.json");
        if (!File.Exists(pj)) return null;
        try
        {
            string json = File.ReadAllText(pj, Encoding.UTF8);
            Match deps = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{([^}]*)\\}");
            if (!deps.Success) return null;
            Match dep = Regex.Match(deps.Groups[1].Value, "\"" + Regex.Escape(spec.PkgName) + "\"\\s*:\\s*\"([^\"]*)\"");
            if (!dep.Success) return null;
            string path = ParseLocalDepPath(dep.Groups[1].Value);
            if (path == null) return null;
            return Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 解析 profile 依赖里的本地路径（link:/file: 前缀）。返回规范化的 Windows
    /// 绝对路径；非本地路径写法（版本号等）、指向启动器目录、或无法解析时返回 null。
    /// profile 里常见 "E://code//..." 双斜杠写法，一并归一。
    /// </summary>
    private static string ParseLocalDepPath(string depValue)
    {
        string v = depValue.Trim();
        string prefix = null;
        if (v.StartsWith("link:", StringComparison.OrdinalIgnoreCase)) prefix = "link:";
        else if (v.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) prefix = "file:";
        if (prefix == null) return null;
        string raw = v.Substring(prefix.Length).Trim();
        if (raw.Length == 0 || raw.IndexOf("dsh-launcher", StringComparison.OrdinalIgnoreCase) >= 0) return null;
        string path = raw.Replace('/', '\\');
        while (path.IndexOf("\\\\", StringComparison.Ordinal) >= 0) path = path.Replace("\\\\", "\\");
        if (!Path.IsPathRooted(path)) path = Path.Combine(ProfileDir(), path);
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    /// <summary>
    /// 扫描 web profile 的全部本地路径依赖（link:/file: 且指向启动器 plugins 目录
    /// 之外），把这些外部检出 node_modules/@deepseek-ai 统一为指向便携 harness
    /// 依赖集的 junction。动机：外部检出常自带 pnpm/npm 安装的 @deepseek-ai 真实
    /// 目录（版本随检出而异），与便携 harness 形成 typert-protocol 双实例 → 插件
    /// 宿主半边挂载失败 → 其 Remote 端点全部 404。只处理 package.json 引用了
    /// @deepseek-ai/* 的目录，避免在无关仓库里创建 node_modules。
    /// </summary>
    private static void UnifyExternalPluginPeers()
    {
        string pj = Path.Combine(ProfileDir(), "package.json");
        if (!File.Exists(pj)) return;
        try
        {
            string json = File.ReadAllText(pj, Encoding.UTF8);
            Match deps = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{([^}]*)\\}");
            if (!deps.Success) return;
            foreach (Match m in Regex.Matches(deps.Groups[1].Value,
                "\"([^\"]+)\"\\s*:\\s*\"(?:link|file):([^\"]+)\""))
            {
                string path = ParseLocalDepPath("link:" + m.Groups[2].Value);
                if (path == null || !Directory.Exists(path)) continue;
                string pkg = Path.Combine(path, "package.json");
                if (!File.Exists(pkg)) continue;
                try { if (!File.ReadAllText(pkg, Encoding.UTF8).Contains("@deepseek-ai/")) continue; }
                catch { continue; }
                EnsurePeersJunction(Path.Combine(path, "node_modules"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[提示] 外部安装依赖统一检查失败: " + ex.Message);
        }
    }

    /// <summary>profile 是否已包含该插件（dependencies 与 bundles 均命中）。</summary>
    private static bool ProfileHasPlugin(string pkgName)
    {
        string pj = Path.Combine(ProfileDir(), "package.json");
        if (!File.Exists(pj)) return false;
        try
        {
            string json = File.ReadAllText(pj, Encoding.UTF8);
            Match deps = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{([^}]*)\\}");
            if (!deps.Success || !deps.Groups[1].Value.Contains("\"" + pkgName + "\"")) return false;
            Match bundles = Regex.Match(json, "\"bundles\"\\s*:\\s*\\[([^\\]]*)\\]");
            return bundles.Success && bundles.Groups[1].Value.Contains("\"" + pkgName + "\"");
        }
        catch { return false; }
    }

    /// <summary>npm 源安装：dsh plugin --profile web add &lt;pkg&gt;（npm_config_registry 指向国内镜像）。</summary>
    private static int InstallPluginNpm(PluginSpec spec)
    {
        string old = Environment.GetEnvironmentVariable("npm_config_registry");
        try
        {
            Environment.SetEnvironmentVariable("npm_config_registry", _registry);
            string dsh = DshCmdPath();
            if (dsh == null) return 1;
            return RunCmd("\"" + dsh + "\" plugin --profile web add " + spec.PkgName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("npm_config_registry", old);
        }
    }

    /// <summary>GitHub 源安装：下载 zip（直连失败走代理）→ 解压 → 装依赖 → 缺 lib 则构建 → dsh plugin add。</summary>
    private static int InstallPluginGithub(PluginSpec spec)
    {
        string dir = EnsurePluginSource(spec, false);
        if (dir == null) return 1;
        string dsh = DshCmdPath();
        if (dsh == null) return 1;
        return RunCmd("\"" + dsh + "\" plugin --profile web add \"" + dir + "\"");
    }

    /// <summary>下载/解压 GitHub 插件源码并准备好依赖与构建产物。返回插件源码目录。</summary>
    private static string EnsurePluginSource(PluginSpec spec, bool force)
    {
        string target = Path.Combine(_pluginsDir, spec.Id);
        bool hasPkg = File.Exists(Path.Combine(target, "package.json"));
        bool hasDeps = Directory.Exists(Path.Combine(target, "node_modules"));
        bool hasLib = File.Exists(Path.Combine(target, "lib", "index.js"));
        // 已就绪判定必须带 .dsh-ready 标记：旧安装（未装 peers、缺标记）会被重新准备修复
        if (!force && hasPkg && hasDeps && hasLib && File.Exists(Path.Combine(target, ".dsh-ready"))) return target;
        if (!force && hasPkg && !hasDeps)
        {
            return PreparePluginDeps(spec, target) ? target : null;
        }

        string zipName = spec.Id + ".zip";
        string zipPath = Path.Combine(_cacheDir, zipName);
        if (!File.Exists(zipPath))
        {
            Console.WriteLine("      下载 " + spec.Display + " 源码 ...");
            string[] urls = PluginZipUrls(spec);
            bool ok = false;
            foreach (string u in urls)
            {
                if (DownloadFile(u, zipPath + ".part"))
                {
                    try { File.Move(zipPath + ".part", zipPath); } catch { }
                    ok = true;
                    break;
                }
                try { if (File.Exists(zipPath + ".part")) File.Delete(zipPath + ".part"); } catch { }
            }
            if (!ok) return null;
        }

        Console.WriteLine("      解压 " + spec.Display + " ...");
        string tmp = Path.Combine(_pluginsDir, "_" + spec.Id + "_tmp");
        bool extracted = false;
        for (int attempt = 0; attempt < 3 && !extracted; attempt++)
        {
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                ZipFile.ExtractToDirectory(zipPath, tmp);
                extracted = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("      解压尝试 " + (attempt + 1) + "/3 失败: " + ex.Message);
                Thread.Sleep(1500);
            }
        }
        if (!extracted)
        {
            Console.WriteLine("[!] " + spec.Display + " 解压失败（可能被占用或磁盘问题）。");
            return null;
        }
        try
        {
            string found = null;
            foreach (string d in Directory.GetDirectories(tmp))
            {
                string pj = Path.Combine(d, "package.json");
                if (!File.Exists(pj)) continue;
                string name = ReadPackageName(pj);
                if (name == spec.PkgName || name == spec.Id) { found = d; break; }
            }
            if (found == null)
            {
                Console.WriteLine("[!] 源码包中未找到 " + spec.Id + " 包。");
                return null;
            }
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(found, target);
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] 解压整理失败: " + ex.Message);
            return null;
        }
        return PreparePluginDeps(spec, target) ? target : null;
    }

    /// <summary>
    /// 在插件目录内装依赖；缺 lib 时执行构建。删除提交的 lockfile 避免开发期本地链接残留。
    /// lib 已提交时去掉 devDependencies（含开发期 link:../deepseek-harness 残留）。
    /// 装完后把插件 node_modules/@deepseek-ai 指向 harness 的 @deepseek-ai 依赖集
    /// （junction）：插件的 @deepseek-ai/* peer 依赖由此解析到与 harness 同一份物理包，
    /// 避免从 registry 装 peers 撞上未发布包（dsh-type-meta/dsh-compact 等 404），
    /// 也避免 Typert 标记双实例问题。
    /// </summary>
    private static bool PreparePluginDeps(PluginSpec spec, string target)
    {
        string lockFile = Path.Combine(target, "pnpm-lock.yaml");
        try { if (File.Exists(lockFile)) File.Delete(lockFile); } catch { }
        bool hasLib = File.Exists(Path.Combine(target, "lib", "index.js"));
        if (hasLib) StripDevDependencies(target);
        Console.WriteLine("      安装 " + spec.Display + " 依赖 ...");
        string pnpm = PnpmCmd();
        string installCmd = "\"" + pnpm + "\" install --ignore-scripts --no-frozen-lockfile --registry=" + _registry
            + " --cache-dir=\"" + _npmCache + "\"";
        if (RunCmdIn(installCmd, target) != 0) return false;
        if (!hasLib)
        {
            // 构建在 junction 之前：tsc/tsdown 用插件自己解析的 devDeps 类型，
            // 不被指向便携 harness 的 @deepseek-ai junction 遮蔽
            Console.WriteLine("      构建 " + spec.Display + " ...");
            if (RunCmdIn("\"" + pnpm + "\" build", target) != 0) return false;
        }
        if (!File.Exists(Path.Combine(target, "lib", "index.js")))
        {
            Console.WriteLine("[!] " + spec.Display + " 构建后仍未找到 lib/index.js。");
            return false;
        }
        EnsurePeersJunction(Path.Combine(target, "node_modules"));
        try { File.WriteAllText(Path.Combine(target, ".dsh-ready"), LAUNCHER_VERSION, new UTF8Encoding(false)); } catch { }
        return true;
    }

    /// <summary>去掉 package.json 的 devDependencies（lib 已提交时无需构建，避免拉进整套构建工具链与开发期 link 残留）。</summary>
    private static void StripDevDependencies(string target)
    {
        string pj = Path.Combine(target, "package.json");
        try
        {
            string json = File.ReadAllText(pj, Encoding.UTF8);
            string cleaned = Regex.Replace(json,
                "(,\\s*\"devDependencies\"\\s*:\\s*\\{[^{}]*\\})|(\"devDependencies\"\\s*:\\s*\\{[^{}]*\\},\\s*)", "");
            if (cleaned != json) File.WriteAllText(pj, cleaned, new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>
    /// 确保 node_modules/@deepseek-ai 是指向 harness @deepseek-ai 依赖集的 junction。
    /// harness 依赖集 = 便携 Node 里全局装的 @deepseek-ai/dsh 包的 node_modules/@deepseek-ai
    /// （194 个包，覆盖 cordis/schemastery/dsh-*/typert 全套）。这样插件的 @deepseek-ai
    /// peer 与 harness 解析到同一份物理包：无 registry 404、无版本漂移、Typert 标记共享。
    /// mklink /J 建目录联接无需管理员权限。
    /// </summary>
    private static void EnsurePeersJunction(string nodeModulesDir)
    {
        if (_nodeDir == null) return;
        string target = Path.Combine(_nodeDir, "node_modules", "@deepseek-ai", "dsh", "node_modules", "@deepseek-ai");
        if (!Directory.Exists(target)) return;
        string link = Path.Combine(nodeModulesDir, "@deepseek-ai");
        try
        {
            Directory.CreateDirectory(nodeModulesDir);
            if (Directory.Exists(link))
            {
                // 已是 junction 且能解析到 cordis（harness 依赖集必有）→ 跳过；否则重建。
                // .NET Framework 4.x 无 LinkTarget，用功能性检查代替。
                var item = new DirectoryInfo(link);
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0
                    && Directory.Exists(Path.Combine(link, "cordis"))) return;
            }
            // 真实目录（插件自装的 @deepseek-ai 副本）必须替换成 junction，否则
            // typert-protocol 双实例 → @Remote 标记对网关不可见 → /api/<ns>/* 404。
            // 删除大目录可能撞 AV/索引器短暂锁定：带重试；每步失败都要报出来，不静默。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(link)) Directory.Delete(link, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("      [提示] 删除旧 @deepseek-ai 目录失败（第 " + (attempt + 1) + " 次）: " + ex.Message);
                    Thread.Sleep(1500);
                    continue;
                }
                RunCmd("mklink /J \"" + link + "\" \"" + target + "\"");
                if (Directory.Exists(Path.Combine(link, "cordis")))
                {
                    Console.WriteLine("      [修正] @deepseek-ai 依赖已统一为 harness junction: " + nodeModulesDir);
                    return; // junction 生效
                }
                Console.WriteLine("      [提示] @deepseek-ai junction 未生效，重试（第 " + (attempt + 1) + " 次）。");
                Thread.Sleep(1500);
            }
            Console.WriteLine("[!] @deepseek-ai peers junction 创建失败，插件的 Remote 端点可能 404。");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] peers junction 异常: " + ex.Message);
        }
    }

    // =====================================================================
    // yuyi 会话工具 preset（dsh-yuyi/tools 工具行）
    // =====================================================================
    /// <summary>创建会话 preset（基于部署自带 standard 追加 yuyi 工具行）；已存在则跳过。</summary>
    private static void EnsureYuyiPreset()
    {
        string home = DshHome();
        string userPresets = Path.Combine(home, ".agent-presets");
        string targetPreset = Path.Combine(userPresets, YUYI_PRESET_ID);
        if (File.Exists(Path.Combine(targetPreset, "agent.cordis.yml"))) return;
        string shipped = FindShippedPreset("standard");
        if (shipped == null)
        {
            Console.WriteLine("[提示] 未找到内置 standard preset，跳过 yuyi 工具 preset 创建。");
            return;
        }
        Console.WriteLine("      创建会话 preset: " + YUYI_PRESET_ID + " ...");
        try
        {
            Directory.CreateDirectory(userPresets);
            CopyDirectory(shipped, targetPreset);
            string add = Environment.NewLine + "# ── yuyi (御驿通信) ─────────────────────────────────────────" + Environment.NewLine
                + "# 御驿模型工具面：yuyi_status / yuyi_register / yuyi_peers / yuyi_send /" + Environment.NewLine
                + "# yuyi_inbox 加十二个 yuyi_task_* 工具。连接接缝由 web profile 的 dsh-yuyi" + Environment.NewLine
                + "# bundle 行挂在宿主平面；本行只把工具注册进本 preset 的会话。" + Environment.NewLine
                + "- id: tool-yuyi" + Environment.NewLine
                + "  name: dsh-yuyi/tools" + Environment.NewLine;
            File.AppendAllText(Path.Combine(targetPreset, "agent.cordis.yml"), add, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(targetPreset, "preset.yml"),
                "name: 标准模式 + 御驿" + Environment.NewLine
                + "description: 标准模式，并带御驿（Yuyi）跨会话通信：十七个 yuyi_* 模型工具（唤醒投递、会话 roster、任务记忆）。" + Environment.NewLine
                + "order: 1" + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine("      preset 已创建。");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[提示] yuyi preset 创建失败: " + ex.Message);
        }
    }

    /// <summary>把 web profile 默认会话 preset 设为 standard-yuyi（cordis.patch.yml 的 agent-presets 覆盖）。</summary>
    private static void PatchProfileDefaultPreset()
    {
        string patch = Path.Combine(ProfileDir(), "cordis.patch.yml");
        if (!File.Exists(patch)) return;
        try
        {
            string content = File.ReadAllText(patch, Encoding.UTF8);
            if (content.Contains(YUYI_PRESET_ID)) return;
            string entry = "- id: agent-presets" + Environment.NewLine
                + "  config:" + Environment.NewLine
                + "    default: " + YUYI_PRESET_ID + Environment.NewLine;
            string replaced = Regex.Replace(content, @"^\s*\[\s*\]\s*$", entry, RegexOptions.Multiline);
            if (replaced == content) return; // 没有 [] 占位，不动
            File.WriteAllText(patch, replaced, new UTF8Encoding(false));
            Console.WriteLine("      已将默认会话 preset 设为 " + YUYI_PRESET_ID + "。");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[提示] 默认 preset 设置失败: " + ex.Message);
        }
    }

    /// <summary>定位部署自带 preset 目录（@deepseek-ai/dsh 包的 config/agent-presets/&lt;id&gt;）。</summary>
    private static string FindShippedPreset(string id)
    {
        if (_nodeDir == null) return null;
        string direct = Path.Combine(_nodeDir, "node_modules", "@deepseek-ai", "dsh", "config", "agent-presets", id);
        if (File.Exists(Path.Combine(direct, "agent.cordis.yml"))) return direct;
        try
        {
            string root = Path.Combine(_nodeDir, "node_modules");
            foreach (string d in Directory.GetDirectories(root, "agent-presets", SearchOption.AllDirectories))
            {
                string p = Path.Combine(d, id);
                if (File.Exists(Path.Combine(p, "agent.cordis.yml"))) return p;
            }
        }
        catch { }
        return null;
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        }
        foreach (string d in Directory.GetDirectories(src))
        {
            CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }

    /// <summary>插件 zip 下载地址序列：直连 codeload 优先，然后国内代理前缀；自定义代理时只用自定义。</summary>
    private static string[] PluginZipUrls(PluginSpec spec)
    {
        string[] parts = spec.Source.Split('/'); // owner/repo/branch
        if (parts.Length != 3) return new string[0];
        string direct = CODELOAD + "/" + parts[0] + "/" + parts[1] + "/zip/refs/heads/" + parts[2];
        if (_githubProxy.Length > 0) return new[] { _githubProxy + direct, direct };
        List<string> urls = new List<string> { direct };
        foreach (string p in GITHUB_PROXIES) urls.Add(p + direct);
        return urls.ToArray();
    }

    /// <summary>把 pnpm 装进便携 Node 目录（dsh plugin 与插件构建都需要）。</summary>
    private static int InstallPnpm()
    {
        string pnpm = PnpmCmd();
        if (File.Exists(pnpm)) return 0;
        Console.WriteLine("      安装 pnpm ...");
        string cmdLine = "\"" + NpmCmd() + "\" install -g pnpm --registry=" + _registry
            + " --cache=\"" + _npmCache + "\" --no-fund --no-audit --loglevel=notice";
        int code = RunCmd(cmdLine);
        if (code != 0 || !File.Exists(pnpm))
        {
            Console.WriteLine("[!] pnpm 安装失败。");
            return 1;
        }
        return 0;
    }

    private static string PnpmCmd()
    {
        return _nodeDir == null ? "pnpm" : Path.Combine(_nodeDir, "pnpm.cmd");
    }

    /// <summary>
    /// 确保 web profile 存在：package.json（web 模板 bundles）+ pnpm-workspace.yaml。
    /// workspace 里预置 allowBuilds（node-pty/protobufjs/esbuild），否则 pnpm 11 会把
    /// 未批准的构建脚本当错误（ERR_PNPM_IGNORED_BUILDS），npm 源插件会安装失败。
    /// 写文件必须用无 BOM 的 UTF-8：Node/pnpm 的 JSON/YAML 解析器不接受 BOM 头。
    /// </summary>
    private static void EnsureProfile()
    {
        string dir = ProfileDir();
        Encoding utf8NoBom = new UTF8Encoding(false);
        try
        {
            Directory.CreateDirectory(dir);
            string pj = Path.Combine(dir, "package.json");
            if (!File.Exists(pj))
            {
                File.WriteAllText(pj,
                    "{\n  \"name\": \"dsh-profile-web\",\n  \"private\": true,\n  \"dependencies\": {},\n"
                    + "  \"dsh\": { \"profile\": { \"bundles\": [\"@deepseek-ai/dsh-base\", \"@deepseek-ai/dsh-web-app\"] } }\n}\n",
                    utf8NoBom);
            }
            string ws = Path.Combine(dir, "pnpm-workspace.yaml");
            if (!File.Exists(ws))
            {
                File.WriteAllText(ws,
                    "packages:\n  - .\n\nnodeLinker: hoisted\nautoInstallPeers: false\n"
                    + "allowBuilds:\n  node-pty: true\n  protobufjs: true\n  esbuild: true\n",
                    utf8NoBom);
            }
            else
            {
                string wsContent = File.ReadAllText(ws, Encoding.UTF8);
                bool changed = false;
                // peers 由 @deepseek-ai junction 提供，不开 autoInstallPeers（避免 registry 404）
                if (wsContent.Contains("autoInstallPeers: true"))
                {
                    wsContent = wsContent.Replace("autoInstallPeers: true", "autoInstallPeers: false");
                    changed = true;
                }
                else if (!wsContent.Contains("autoInstallPeers:"))
                {
                    wsContent += "\nautoInstallPeers: false\n";
                    changed = true;
                }
                if (!wsContent.Contains("allowBuilds:"))
                {
                    wsContent += "\nallowBuilds:\n  node-pty: true\n  protobufjs: true\n  esbuild: true\n";
                    changed = true;
                }
                if (changed) File.WriteAllText(ws, wsContent, utf8NoBom);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[提示] profile 初始化失败: " + ex.Message);
        }
    }

    private static string ReadPackageName(string packageJsonPath)
    {
        Match m = Regex.Match(File.ReadAllText(packageJsonPath, Encoding.UTF8), "\"name\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    // =====================================================================
    // 启动 dsh web
    // =====================================================================
    private static void StartWeb()
    {
        string dsh = DshCmdPath();
        if (dsh == null)
        {
            Console.WriteLine("[!] 找不到 dsh.cmd，无法启动。");
            return;
        }
        Thread waiter = new Thread(() => PollAndOpen(DEFAULT_PORT));
        waiter.IsBackground = true;
        waiter.Start();
        int code = RunCmd("\"" + dsh + "\" web");
        Console.WriteLine();
        Console.WriteLine("dsh web 已退出（退出码 " + code + "）。");
        Pause();
    }

    private static void PollAndOpen(int port)
    {
        string url = "http://127.0.0.1:" + port;
        for (int i = 0; i < 180; i++) // 最多约 90 秒
        {
            if (PortInUse(port))
            {
                Console.WriteLine("[提示] 服务已就绪，正在打开浏览器: " + url);
                OpenBrowser(url);
                return;
            }
            Thread.Sleep(500);
        }
        Console.WriteLine("[提示] 等待服务就绪超时，请手动打开浏览器访问: " + url);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(url);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[提示] 无法自动打开浏览器，请手动访问 " + url + " (" + ex.Message + ")");
        }
    }

    // =====================================================================
    // 自检
    // =====================================================================
    private static int RunCheck()
    {
        Console.WriteLine("============== 环境自检 ==============");
        Console.WriteLine("操作系统:    " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (x64)" : " (x86)"));
        Console.WriteLine("运行目录:    " + _runtimeDir);
        Console.WriteLine("npm 源:      " + _registry);
        Console.WriteLine();

        string marker = Path.Combine(_runtimeDir, "current-node.txt");
        bool nodeOk = false;
        if (File.Exists(marker))
        {
            string dir = File.ReadAllText(marker, Encoding.UTF8).Trim();
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, "node.exe")))
            {
                _nodeDir = dir;
                Console.WriteLine("[OK] 便携版 Node.js:  " + NodeVersion() + "  (" + dir + ")");
                nodeOk = true;
            }
        }
        if (!nodeOk) Console.WriteLine("[--] 便携版 Node.js:  未安装（首次启动会自动下载）");

        string installed = nodeOk ? InstalledDshVersion() : "";
        if (installed.Length > 0) Console.WriteLine("[OK] dsh:             " + installed + "  (" + DshCmdPath() + ")");
        else Console.WriteLine("[--] dsh:             未安装（首次启动会自动安装）");

        // 默认插件状态
        int okPlugins = 0;
        foreach (PluginSpec spec in DEFAULT_PLUGINS)
        {
            if (ProfileHasPlugin(spec.PkgName)) okPlugins++;
        }
        if (okPlugins == DEFAULT_PLUGINS.Length)
            Console.WriteLine("[OK] 默认插件:       " + okPlugins + "/" + DEFAULT_PLUGINS.Length + " 已安装");
        else
            Console.WriteLine("[--] 默认插件:       " + okPlugins + "/" + DEFAULT_PLUGINS.Length + " 已安装（启动时会自动补齐）");

        Console.WriteLine("端口 " + DEFAULT_PORT + ":    " + (PortInUse(DEFAULT_PORT) ? "被占用（可能已有 dsh web 在运行）" : "空闲"));

        Console.WriteLine();
        Console.WriteLine("正在向 npm 源查询最新 dsh 版本 ...");
        string latest = nodeOk ? LatestDshVersion() : "";
        if (latest.Length > 0)
        {
            Console.WriteLine("最新 dsh:       " + latest);
            string i = GetVersionNumber(installed);
            if (i.Length > 0 && i != GetVersionNumber(latest))
            {
                Console.WriteLine("[提示] 可运行 --update（或双击“升级dsh.bat”）升级到最新版。");
            }
        }
        else
        {
            Console.WriteLine("最新 dsh:       (查询失败，可能离线或网络问题)");
        }

        Console.WriteLine();
        Console.WriteLine("自检完成。");
        return 0;
    }

    // =====================================================================
    // 工具函数
    // =====================================================================
    private static bool PortInUse(int port)
    {
        try
        {
            using (TcpClient c = new TcpClient())
            {
                IAsyncResult r = c.BeginConnect("127.0.0.1", port, null, null);
                if (!r.AsyncWaitHandle.WaitOne(800)) return false;
                c.EndConnect(r);
                return true;
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// 以 cmd /c 运行命令，继承本窗口，返回退出码。
    /// 整个命令行必须用一对引号包起来：cmd 对 /c 后首尾的引号会各剥一层，
    /// 不包的话含空格路径会被截断报 "filename, directory name, or volume label syntax is incorrect"。
    /// </summary>
    private static int RunCmd(string commandLine)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + commandLine + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = false;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] 无法执行命令: " + commandLine + "  (" + ex.Message + ")");
            return -1;
        }
    }

    /// <summary>RunCmd 的可指定工作目录版本（插件构建等需要）。</summary>
    private static int RunCmdIn(string commandLine, string cwd)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + commandLine + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = false;
            if (!string.IsNullOrEmpty(cwd)) psi.WorkingDirectory = cwd;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] 无法执行命令: " + commandLine + "  (" + ex.Message + ")");
            return -1;
        }
    }

    /// <summary>运行命令并把输出（stdout+stderr）重定向到临时文件再读回，避免管道。</summary>
    private static string RunCapture(string commandLine)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "dsh-cap-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            RunCmd(commandLine + " > \"" + tmp + "\" 2>&1");
            return File.Exists(tmp) ? File.ReadAllText(tmp, Encoding.UTF8) : "";
        }
        catch { return ""; }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static bool DownloadFile(string url, string dest)
    {
        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "dsh-launcher/" + LAUNCHER_VERSION);
                ManualResetEventSlim done = new ManualResetEventSlim(false);
                Exception err = null;
                wc.DownloadProgressChanged += (s, e) =>
                {
                    if (e.TotalBytesToReceive > 0)
                    {
                        double pct = e.BytesReceived * 100.0 / e.TotalBytesToReceive;
                        Console.Write("\r      下载中 {0,3:N0}%  ({1:N1} / {2:N1} MB)   ", pct, e.BytesReceived / 1048576.0, e.TotalBytesToReceive / 1048576.0);
                    }
                    else
                    {
                        Console.Write("\r      下载中 {0:N1} MB   ", e.BytesReceived / 1048576.0);
                    }
                };
                wc.DownloadFileCompleted += (s, e) => { err = e.Error; done.Set(); };
                wc.DownloadFileAsync(new Uri(url), dest);
                done.Wait();
                Console.WriteLine();
                if (err != null)
                {
                    Console.WriteLine("[!] 下载失败: " + err.Message);
                    return false;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] 下载失败: " + ex.Message);
            return false;
        }
    }

    private static string GetVersionNumber(string text)
    {
        if (text == null) return "";
        Match m = Regex.Match(text, "[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.]+)?");
        return m.Success ? m.Value : "";
    }

    private static void Fail(string msg)
    {
        Console.WriteLine();
        Console.WriteLine("[!] " + msg);
        Pause();
    }

    private static void Pause()
    {
        try
        {
            Console.WriteLine();
            Console.WriteLine("按任意键退出 ...");
            Console.ReadKey(true);
        }
        catch { }
    }

    private static bool Has(string[] args, string flag)
    {
        foreach (string a in args)
        {
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
