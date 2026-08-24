// DshLauncher.Core — Node 下载 + dsh 安装 + Junction + Yuyi
using DshLauncher.Logging;

namespace DshLauncher
{
    using System.IO.Compression;
    using System.Net;

    public static class PortableNode
    {
        public const string NPMMIRROR_BASE = "https://registry.npmmirror.com/-/binary/node";
        public const string OFFICIAL_BASE = "https://nodejs.org/dist";
        public const string NPMMIRROR_INDEX = "https://registry.npmmirror.com/-/binary/node/index.json";
        public const string OFFICIAL_INDEX = "https://nodejs.org/dist/index.json";

        public static string Ensure(ILogger log = null)
        {
            log ??= new NullLogger();
            Console.WriteLine("[1/4] 检查便携版 Node.js ...");
            string marker = Path.Combine(Paths.RuntimeDir, "current-node.txt");
            if (File.Exists(marker))
            {
                string dir = File.ReadAllText(marker, Encoding.UTF8).Trim();
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, "node.exe")))
                {
                    Paths.NodeDir = dir;
                    Console.WriteLine("      已就绪: " + NodeVersion() + "  (" + dir + ")");
                    return dir;
                }
            }

            Console.WriteLine("      未找到便携版 Node.js，开始下载（首次使用约需下载 35MB，请耐心等待）...");
            string ver = FindLatestNodeLts();
            string zipName = "node-" + ver + "-win-x64.zip";
            string zipPath = Path.Combine(Paths.CacheDir, zipName);

            if (!File.Exists(zipPath))
            {
                bool ok = false;
                string[] urls = new[]
                {
                    NPMMIRROR_BASE + "/" + ver + "/" + zipName,
                    OFFICIAL_BASE + "/" + ver + "/" + zipName,
                };
                foreach (string u in urls)
                {
                    Console.WriteLine("      下载 " + ver + ": " + u);
                    if (Shell.DownloadFile(u, zipPath))
                    {
                        if (Config.Current.Integrity != "none" && TryVerifyNodeSha(zipPath))
                        {
                            ok = true; break;
                        }
                        else if (Config.Current.Integrity == "none")
                        {
                            ok = true; break;
                        }
                        else if (Config.Current.Integrity == "strict")
                        {
                            log.Error("[!] SHA256 校验失败（strict 模式），删除并继续下一个镜像。");
                            try { File.Delete(zipPath); } catch { }
                            continue;
                        }
                        else // lax
                        {
                            log.Warn("[提示] SHA256 校验失败，但 lax 模式接受下载继续。");
                            ok = true; break;
                        }
                    }
                }
                if (!ok) return null;
            }

            string extractRoot = Path.Combine(Paths.RuntimeDir, "node");
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
                    log.Error("[!] 解压失败: " + ex.Message);
                    return null;
                }
            }

            Paths.NodeDir = extractDir;
            try { File.WriteAllText(marker, extractDir, Encoding.UTF8); } catch { }
            AddToPath();
            Console.WriteLine("      完成: " + NodeVersion());
            return extractDir;
        }

        private static string FindLatestNodeLts()
        {
            foreach (string url in new[] { NPMMIRROR_INDEX, OFFICIAL_INDEX })
            {
                string json = Http.GetString(url, timeoutSeconds: 15);
                if (json == null) continue;
                try
                {
                    Version best = null;
                    string bestVer = null;
                    foreach (Match m in Regex.Matches(json, "\"version\":\"v([0-9]+\\.[0-9]+\\.[0-9]+)\"[^}]*?\"lts\":\"([^\"]+)\""))
                    {
                        Version v;
                        if (!Version.TryParse(m.Groups[1].Value, out v)) continue;
                        if (best == null || v > best)
                        {
                            best = v;
                            bestVer = m.Groups[1].Value;
                        }
                    }
                    if (bestVer != null) return "v" + bestVer;
                }
                catch { }
            }
            return Config.Current.PinnedNodeVersion;
        }

        /// <summary>
        /// 校验已下载的 Node zip 的 SHA256：优先 npmmirror（中国用户），失败 fallback 到 nodejs.org。
        /// </summary>
        private static bool TryVerifyNodeSha(string zipPath)
        {
            try
            {
                string zipName = Path.GetFileName(zipPath);
                Match vm = Regex.Match(zipName, @"^node-v([0-9]+\.[0-9]+\.[0-9]+)-");
                if (!vm.Success) return false;
                string ver = "v" + vm.Groups[1].Value;

                string[] urls = new[]
                {
                    "https://registry.npmmirror.com/-/binary/node/" + ver + "/SHASUMS256.txt",
                    "https://nodejs.org/dist/" + ver + "/SHASUMS256.txt",
                };
                string sums = null, sumsTried = null;
                foreach (string sumsUrl in urls)
                {
                    string body = Http.GetString(sumsUrl, timeoutSeconds: 15);
                    if (!string.IsNullOrEmpty(body) && body.Contains(" "))
                    {
                        sums = body;
                        sumsTried = sumsUrl;
                        break;
                    }
                }
                if (sums == null)
                {
                    Console.Error.WriteLine("[提示] 无法获取 SHASUMS256.txt，跳过 SHA 校验。");
                    return false;
                }
                string expected = ParseShaLine(sums, zipName);
                if (expected == null)
                {
                    Console.Error.WriteLine("[提示] SHASUMS256.txt 中未找到 " + zipName + "，跳过 SHA 校验。");
                    return false;
                }
                string actual = Shell.ComputeSha256Hex(zipPath);
                bool ok = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                if (ok) Console.WriteLine("      [OK] SHA256 校验通过（来源: " + sumsTried + "）。");
                else Console.WriteLine("      [!] SHA256 不一致: 期望 " + expected.Substring(0, 12) + "..., 实际 " + actual.Substring(0, 12) + "...");
                return ok;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[提示] SHA256 校验跳过: " + ex.Message);
                return false;
            }
        }

        public static string ParseShaLine(string sumsText, string fileName)
        {
            if (sumsText == null || fileName == null) return null;
            foreach (string raw in sumsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                int sp = line.IndexOf(' ');
                if (sp != 64) continue;
                string sha = line.Substring(0, 64);
                string name = line.Substring(sp + 1).TrimStart('*', ' ');
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    return sha;
            }
            return null;
        }

        private static void AddToPath()
        {
            if (Paths.NodeDir == null) return;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string p in path.Split(';'))
                if (string.Equals(p.TrimEnd('\\'), Paths.NodeDir, StringComparison.OrdinalIgnoreCase)) return;
            Environment.SetEnvironmentVariable("PATH", Paths.NodeDir + ";" + path);
        }

        public static string NodeVersion()
        {
            if (Paths.NodeDir == null) return "";
            string outTxt = Shell.RunCapture("\"" + Path.Combine(Paths.NodeDir, "node.exe") + "\" --version", out int code);
            if (code != 0) return "(未知)";
            string[] lines = outTxt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "(未知)";
        }

        public static string NpmCmd() => Paths.NodeDir == null ? "npm" : Path.Combine(Paths.NodeDir, "npm.cmd");
        public static string PnpmCmd() => Paths.NodeDir == null ? "pnpm" : Path.Combine(Paths.NodeDir, "pnpm.cmd");
        public static string DshCmdPath()
        {
            if (Paths.NodeDir == null) return null;
            string p = Path.Combine(Paths.NodeDir, "dsh.cmd");
            return File.Exists(p) ? p : null;
        }
    }

    public static class DshInstaller
    {
        public static int Ensure(bool forceUpdate, ILogger log = null)
        {
            log ??= new NullLogger();
            Console.WriteLine("[2/4] 检查 dsh ...");
            PatchDshSubprocessSpillDir();
            if (Paths.NodeDir == null) return ExitCodes.DshInstallFailed;
            string installed = InstalledVersion();
            if (installed.Length > 0)
            {
                Console.WriteLine("      已安装: " + installed);
                if (!forceUpdate && !AutoUpdateDue())
                {
                    Console.WriteLine("      今日已检查过更新，跳过。");
                    return ExitCodes.Success;
                }
                Console.WriteLine("      正在查询最新版本 ...");
                string latest = LatestVersion();
                string instV = Shell.GetVersionNumber(installed);
                string latV = Shell.GetVersionNumber(latest);
                if (latV.Length == 0 || instV == latV)
                {
                    Console.WriteLine("      已是最新版本" + (latest.Length > 0 ? " (" + latest + ")" : "") + "。");
                    WriteAutoUpdateStamp();
                    return ExitCodes.Success;
                }
                Console.WriteLine("      发现新版本 " + latest + "，正在自动更新 ...");
            }
            else
            {
                Console.WriteLine("      未安装，正在安装 @deepseek-ai/dsh（首次可能需要几分钟）...");
            }
            string action = installed.Length > 0 ? "install -g @deepseek-ai/dsh@latest" : "install -g @deepseek-ai/dsh";
            string cmdLine = "\"" + PortableNode.NpmCmd() + "\" " + action
                + " --registry=" + Config.Current.Registry
                + " --cache=\"" + Paths.NpmCacheDir + "\" --no-fund --no-audit --loglevel=notice";
            int code = Shell.RunCmd(cmdLine);
            if (code != 0)
            {
                log.Error("[!] npm 命令失败（退出码 " + code + "）。");
                return ExitCodes.DshInstallFailed;
            }
            string after = InstalledVersion();
            Console.WriteLine("      完成: " + (after.Length > 0 ? after : "dsh 已安装"));
            WriteAutoUpdateStamp();
            return ExitCodes.Success;
        }

        public static string InstalledVersion()
        {
            string dsh = PortableNode.DshCmdPath();
            if (dsh == null) return "";
            // 退出码非 0 = 探测失败（与"未安装"同表现为空串，但可区分于正常输出）
            string outTxt = Shell.RunCapture("\"" + dsh + "\" --version", out int code);
            if (code != 0) return "";
            string[] lines = Shell.StripNpmWarns(outTxt).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
        }

        public static string LatestVersion()
        {
            string outTxt = Shell.RunCapture("\"" + PortableNode.NpmCmd() + "\" view @deepseek-ai/dsh version --registry=" + Config.Current.Registry + " --no-fund --no-audit", out int code);
            if (code != 0) return "";
            return Shell.StripNpmWarns(outTxt).Trim();
        }

        public static bool AutoUpdateDue()
        {
            string f = Path.Combine(Paths.RuntimeDir, "last-update.txt");
            try
            {
                return !File.Exists(f) || File.ReadAllText(f, Encoding.UTF8).Trim() != DateTime.Today.ToString("yyyy-MM-dd");
            }
            catch { return true; }
        }

        public static void WriteAutoUpdateStamp()
        {
            try { File.WriteAllText(Path.Combine(Paths.RuntimeDir, "last-update.txt"), DateTime.Today.ToString("yyyy-MM-dd"), Encoding.UTF8); } catch { }
        }

        public static void PatchDshSubprocessSpillDir()
        {
            if (Paths.NodeDir == null) return;
            string file = Path.Combine(Paths.NodeDir, "node_modules", "@deepseek-ai", "dsh",
                "node_modules", "@deepseek-ai", "dsh-subprocess-local", "lib", "index.js");
            if (!File.Exists(file)) return;
            try
            {
                string src = File.ReadAllText(file, Encoding.UTF8);
                if (src.Contains("this.spillFd = openSync(this.spillFile, \"wx\", 384)"))
                {
                    if (src.Contains("// launcher-patch: mkdirSync spill dir"))
                    {
                        Console.WriteLine("      [提示] dsh-subprocess-local 补丁已应用，跳过。");
                        return;
                    }
                    string patched;
                    if (src.Contains("import { closeSync, constants, mkdtempSync, openSync"))
                    {
                        patched = src.Replace(
                            "import { closeSync, constants, mkdtempSync, openSync, readFileSync, readSync, readdirSync, unlinkSync, writeSync } from \"node:fs\";",
                            "import { closeSync, constants, mkdirSync, mkdtempSync, openSync, readFileSync, readSync, readdirSync, unlinkSync, writeSync } from \"node:fs\";");
                    }
                    else
                    {
                        Console.Error.WriteLine("[!] dsh-subprocess-local 导入格式变更，跳过补丁。可能导致临时目录 ENOENT。");
                        return;
                    }
                    patched = patched.Replace(
                        "if (this.spillFd === void 0) {",
                        "if (this.spillFd === void 0) {\n\t\t\t\ttry { mkdirSync(this.spillDir, { recursive: true }); } catch {}\n\t\t\t\t// launcher-patch: mkdirSync spill dir");
                    File.WriteAllText(file, patched, new UTF8Encoding(false));
                    Console.WriteLine("      [修正] dsh-subprocess-local spill 目录 ENOENT 缺陷已补丁。");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 补丁 dsh-subprocess-local 失败: " + ex.Message);
            }
        }
    }

    public static class JunctionGuard
    {
        public static void Ensure(string nodeModulesDir)
        {
            if (Paths.NodeDir == null) return;
            string target = Path.Combine(Paths.NodeDir, "node_modules", "@deepseek-ai", "dsh", "node_modules", "@deepseek-ai");
            if (!Directory.Exists(target)) return;
            EnsureSharedCorePackages(target);
            string link = Path.Combine(nodeModulesDir, "@deepseek-ai");
            try
            {
                Directory.CreateDirectory(nodeModulesDir);
                if (Directory.Exists(link))
                {
                    DirectoryInfo item = new DirectoryInfo(link);
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0
                        && Directory.Exists(Path.Combine(link, "cordis"))) return;
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(link)) Directory.Delete(link, true);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("      [提示] 删除旧 @deepseek-ai 目录失败（第 " + (attempt + 1) + " 次）: " + ex.Message);
                        Thread.Sleep(1500);
                        continue;
                    }
                    Shell.RunCmd("mklink /J \"" + link + "\" \"" + target + "\"");
                    if (Directory.Exists(Path.Combine(link, "cordis")))
                    {
                        Console.WriteLine("      [修正] @deepseek-ai 依赖已统一为 harness junction: " + nodeModulesDir);
                        return;
                    }
                    Console.Error.WriteLine("      [提示] @deepseek-ai junction 未生效，重试（第 " + (attempt + 1) + " 次）。");
                    Thread.Sleep(1500);
                }
                Console.Error.WriteLine("[!] @deepseek-ai peers junction 创建失败，插件的 Remote 端点可能 404。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] peers junction 异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 修复共享 harness 依赖集里缺失的核心包（dsh npm 安装可能只留空壳目录）。
        /// 版本钉子默认内置，可被 config.json 的 sharedCoreSpecs 逐包覆盖（防 rc tag 腐烂）。
        /// </summary>
        public static void EnsureSharedCorePackages(string sharedDir)
        {
            try
            {
                if (!Directory.Exists(sharedDir)) return;
                LinkCanonicalNames(sharedDir);   // 先补缓存包规范名 junction（防解析逃逸到主目录旧版）
                string[] core = new[] {
                    "cordis", "schemastery",
                    "dsh-llm", "dsh-session", "dsh-agent", "dsh-host-webserver",
                    "dsh-settings", "dsh-scope", "dsh-timeout", "dsh-brand" };
                bool need = false;
                foreach (string p in core)
                {
                    string entry = Path.Combine(sharedDir, p, "lib",
                        p == "schemastery" ? "index.cjs" : "index.js");
                    if (!File.Exists(entry)) { need = true; break; }
                }
                if (!need) return;

                Console.WriteLine("      [修正] 检测到 harness 核心依赖缺失，正在补齐 ...");
                string pj = Path.Combine(sharedDir, "package.json");
                bool hadPj = File.Exists(pj);
                if (!hadPj)
                    File.WriteAllText(pj, "{\"name\":\"dsh-harness-shared\",\"private\":true,\"dependencies\":{}}", new UTF8Encoding(false));
                Dictionary<string, string> specs = ResolveCoreSpecs();
                var sbCmd = new StringBuilder("\"" + PortableNode.PnpmCmd() + "\" add");
                foreach (string p in core)
                    sbCmd.Append(" @deepseek-ai/").Append(p).Append('@').Append(specs[p]);
                sbCmd.Append(" --registry=").Append(Config.Current.Registry).Append(" --ignore-scripts --no-fund --no-audit");
                Shell.RunCmdIn(sbCmd.ToString(), sharedDir);
                string hoisted = Path.Combine(sharedDir, "node_modules", "@deepseek-ai");
                if (Directory.Exists(hoisted))
                {
                    foreach (string p in core)
                    {
                        string src = Path.Combine(hoisted, p);
                        if (!Directory.Exists(src)) continue;
                        string dst = Path.Combine(sharedDir, p);
                        try { if (Directory.Exists(dst)) Directory.Delete(dst, true); } catch { }
                        YuyiPreset.CopyDirectory(src, dst);
                    }
                }
                Console.WriteLine("      [修正] harness 核心依赖已补齐。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 补齐 harness 核心依赖失败: " + ex.Message);
            }
        }
        /// <summary>
        /// dsh 依赖集里包以 ".名字-哈希" 缓存名存放（如 .dsh-app-boot-XyZ），Node 按规范名
        /// 解析不到 -> 沿目录树向上逃逸，可能命中用户主目录残留旧版（双实例 ->
        /// adapter 接口不匹配崩溃，如 prepareCall is not a function）。
        /// 为每个缓存包建规范名 junction（幂等），保证解析永远命中本依赖集。
        /// </summary>
        public static void LinkCanonicalNames(string sharedDir)
        {
            try
            {
                int linked = 0;
                foreach (string dir in Directory.GetDirectories(sharedDir))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.StartsWith(".")) continue;
                    string pj = Path.Combine(dir, "package.json");
                    if (!File.Exists(pj)) continue;
                    string pkg = ReadPackageJsonName(pj);
                    if (pkg == null || pkg.IndexOf('/') < 0) continue;
                    string shortName = pkg.Substring(pkg.IndexOf('/') + 1);
                    if (shortName.Length == 0) continue;
                    string canonical = Path.Combine(sharedDir, shortName);
                    if (File.Exists(Path.Combine(canonical, "package.json"))) continue;
                    if (Directory.Exists(canonical))
                    {
                        try { Directory.Delete(canonical, true); } catch { continue; }
                    }
                    try
                    {
                        Shell.RunCmd("mklink /J \"" + canonical + "\" \"" + dir + "\"");
                        if (File.Exists(Path.Combine(canonical, "package.json"))) linked++;
                    }
                    catch { }
                }
                if (linked > 0)
                    Console.WriteLine("      [修正] 已为 " + linked + " 个依赖集包补规范名链接（防双实例）。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[提示] 规范名链接检查失败: " + ex.Message);
            }
        }

        private static string ReadPackageJsonName(string packageJsonPath)
        {
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(packageJsonPath, Encoding.UTF8));
                return obj.GetString("name");
            }
            catch { return null; }
        }

        /// <summary>
        /// 核心依赖版本解析：内置默认钉子 + config.json sharedCoreSpecs 逐包覆盖。
        /// 抽成纯函数便于单测锁定"默认值 + 覆盖"行为；上游 rc tag 腐烂时可现场改配置修复。
        /// </summary>
        /// <summary>内置核心依赖钉子（包短名 → 版本）。config.json sharedCoreSpecs 只允许覆盖这些键。</summary>
        public static readonly Dictionary<string, string> DefaultCoreSpecs = new Dictionary<string, string>
        {
            ["cordis"] = "4.0.1-rc.4",
            ["schemastery"] = "3.18.1-rc.4",
            ["dsh-llm"] = "0.1.1-rc.2",
            ["dsh-session"] = "0.1.1-rc.2",
            ["dsh-agent"] = "0.1.1-rc.2",
            ["dsh-host-webserver"] = "0.1.1-rc.2",
            ["dsh-settings"] = "0.1.1-rc.2",
            ["dsh-scope"] = "0.1.1-rc.2",
            ["dsh-timeout"] = "0.1.1-rc.2",
            ["dsh-brand"] = "0.1.1-rc.2",
        };

        public static Dictionary<string, string> ResolveCoreSpecs()
        {
            var specs = new Dictionary<string, string>(DefaultCoreSpecs);
            foreach (var kv in Config.Current.SharedCoreSpecs)
                if (specs.ContainsKey(kv.Key)) specs[kv.Key] = kv.Value;
            return specs;
        }

    }

    public static class YuyiPreset
    {
        public static void Ensure()
        {
            string home = Paths.DshHome();
            string userPresets = Path.Combine(home, ".agent-presets");
            string targetPreset = Path.Combine(userPresets, "standard-yuyi");
            if (File.Exists(Path.Combine(targetPreset, "agent.cordis.yml"))) return;
            string shipped = FindShippedPreset("standard");
            if (shipped == null)
            {
                Console.Error.WriteLine("[提示] 未找到内置 standard preset，跳过 yuyi 工具 preset 创建。");
                return;
            }
            Console.WriteLine("      创建会话 preset: standard-yuyi ...");
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
                Console.Error.WriteLine("[提示] yuyi preset 创建失败: " + ex.Message);
            }
        }

        public static void PatchProfileDefaultPreset()
        {
            string patch = Path.Combine(Paths.ProfileDir(), "cordis.patch.yml");
            if (!File.Exists(patch)) return;
            try
            {
                string content = File.ReadAllText(patch, Encoding.UTF8);
                if (content.Contains("standard-yuyi")) return;
                string entry = "- id: agent-presets" + Environment.NewLine
                    + "  config:" + Environment.NewLine
                    + "    default: standard-yuyi" + Environment.NewLine;
                string replaced = Regex.Replace(content, @"^\s*\[\s*\]\s*$", entry, RegexOptions.Multiline);
                if (replaced == content) return;
                File.WriteAllText(patch, replaced, new UTF8Encoding(false));
                Console.WriteLine("      已将默认会话 preset 设为 standard-yuyi。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[提示] 默认 preset 设置失败: " + ex.Message);
            }
        }

        public static string FindShippedPreset(string id)
        {
            if (Paths.NodeDir == null) return null;
            string direct = Path.Combine(Paths.NodeDir, "node_modules", "@deepseek-ai", "dsh", "config", "agent-presets", id);
            if (File.Exists(Path.Combine(direct, "agent.cordis.yml"))) return direct;
            try
            {
                string root = Path.Combine(Paths.NodeDir, "node_modules");
                foreach (string d in Directory.GetDirectories(root, "agent-presets", SearchOption.AllDirectories))
                {
                    string p = Path.Combine(d, id);
                    if (File.Exists(Path.Combine(p, "agent.cordis.yml"))) return p;
                }
            }
            catch { }
            return null;
        }

        public static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }

    public static class ExitCodes
    {
        public const int Success = 0;
        public const int NetworkError = 10;
        public const int NodeSetupFailed = 20;
        public const int DshInstallFailed = 30;
        public const int PluginInstallFailed = 40;
        public const int ConfigError = 50;
        public const int InternalError = 60;
        public const int AlreadyRunning = 70;
    }

    public static class AppMain
    {
        public const string AppName = "DeepSeek Harness (dsh) 一键启动器";
        public const string Version = "1.6.0";  // Job Object 落地 + HTTP 超时/续传 + 注入白名单 + healthy 语义修正

        public const string Codeload = "https://codeload.github.com";
        public static readonly string[] GithubProxies = new[]
        {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "https://mirror.ghproxy.com/",
            "https://ghproxy.net/",
        };
    }
}
