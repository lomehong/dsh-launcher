// DshLauncher.Core — 插件管理器（带 CancellationToken + Progress）
using DshLauncher.Logging;

namespace DshLauncher
{
    using System.IO.Compression;
    using System.Net;

    public static class PluginManager
    {
        public const string YUYI_PRESET_ID = "standard-yuyi";

        public static int EnsureAll(ILogger log = null, Action<InstallProgress> onProgress = null,
            CancellationToken ct = default)
        {
            log ??= new NullLogger();
            Console.WriteLine("[3/4] 检查默认插件 ...");
            if (Paths.NodeDir == null) return ExitCodes.PluginInstallFailed;
            if (InstallPnpm() != 0) return ExitCodes.PluginInstallFailed;
            EnsureProfile();
            JunctionGuard.Ensure(Path.Combine(Paths.ProfileDir(), "node_modules"));
            int installed = 0, failed = 0, skipped = 0;
            var plugins = PluginRegistry.Load().Plugins;
            for (int i = 0; i < plugins.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                PluginSpec spec = plugins[i];
                onProgress?.Invoke(new InstallProgress
                {
                    Phase = InstallPhase.PluginInstall,
                    CurrentIndex = i + 1,
                    TotalItems = plugins.Count,
                    CurrentItem = spec.Display,
                    Percent = (int)(100.0 * i / plugins.Count),
                    Message = "正在检查 " + spec.Id,
                });
                string externalDir = spec.ViaNpm ? null : ExternalPluginDir(spec);
                bool ready = spec.ViaNpm
                    ? ProfileHasPlugin(spec.PkgName)
                    : ProfileHasPlugin(spec.PkgName) && (PluginDirReady(spec) || externalDir != null);
                if (ready)
                {
                    Console.WriteLine("      [" + (i + 1) + "/" + plugins.Count + "] " + spec.Display + " 已安装。");
                    if (externalDir != null)
                        Console.WriteLine("          外部/开发安装: " + externalDir);
                    installed++;
                    continue;
                }
                if (!spec.Required)
                {
                    Console.WriteLine("      [" + (i + 1) + "/" + plugins.Count + "] " + spec.Display + " 可选插件未安装，跳过。");
                    skipped++;
                    continue;
                }
                Console.WriteLine("      [" + (i + 1) + "/" + plugins.Count + "] 安装 " + spec.Display + " ...");
                int code = spec.ViaNpm ? InstallPluginNpm(spec, ct) : InstallPluginGithub(spec, ct);
                if (code == 0 && ProfileHasPlugin(spec.PkgName))
                {
                    Console.WriteLine("      " + spec.Display + " 已就绪。");
                    installed++;
                }
                else
                {
                    log.Error("[!] " + spec.Display + " 安装失败（不影响其他步骤）。");
                    failed++;
                }
            }
            JunctionGuard.Ensure(Path.Combine(Paths.ProfileDir(), "node_modules"));
            if (!Config.Current.ProtectExternal) UnifyExternalPluginPeers();
            if (ProfileHasPlugin("dsh-yuyi"))
            {
                YuyiPreset.Ensure();
                YuyiPreset.PatchProfileDefaultPreset();
            }
            Console.WriteLine("      插件检查完成：成功 " + installed + "，跳过 " + skipped + "，失败 " + failed + "。");
            onProgress?.Invoke(new InstallProgress
            {
                Phase = failed == 0 ? InstallPhase.Complete : InstallPhase.PluginInstall,
                CurrentIndex = plugins.Count,
                TotalItems = plugins.Count,
                Percent = 100,
                Message = "完成",
            });
            return failed == 0 ? ExitCodes.Success : ExitCodes.PluginInstallFailed;
        }

        public static bool PluginDirReady(PluginSpec spec)
        {
            string target = Path.Combine(Paths.PluginsDir, spec.Id);
            return File.Exists(Path.Combine(target, ".dsh-ready"))
                && Directory.Exists(Path.Combine(target, "node_modules", "@deepseek-ai"))
                && File.Exists(Path.Combine(target, "lib", "index.js"));
        }

        public static string ExternalPluginDir(PluginSpec spec)
        {
            string pj = Path.Combine(Paths.ProfileDir(), "package.json");
            if (!File.Exists(pj)) return null;
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(pj, Encoding.UTF8));
                if (!obj.Map.TryGetValue("dependencies", out object depsObj) || depsObj is not Dictionary<string, object> deps)
                    return null;
                if (!deps.TryGetValue(spec.PkgName, out object depValObj)) return null;
                string path = ParseLocalDepPath(depValObj as string);
                if (path == null) return null;
                return Directory.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        public static string ParseLocalDepPath(string depValue)
        {
            if (depValue == null) return null;
            string v = depValue.Trim();
            string prefix = null;
            if (v.StartsWith("link:", StringComparison.OrdinalIgnoreCase)) prefix = "link:";
            else if (v.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) prefix = "file:";
            if (prefix == null) return null;
            string raw = v.Substring(prefix.Length).Trim();
            if (raw.Length == 0 || raw.IndexOf("dsh-launcher", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            string path = raw.Replace('/', '\\');
            while (path.IndexOf("\\\\", StringComparison.Ordinal) >= 0) path = path.Replace("\\\\", "\\");
            if (!Path.IsPathRooted(path)) path = Path.Combine(Paths.ProfileDir(), path);
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }

        public static void UnifyExternalPluginPeers()
        {
            string pj = Path.Combine(Paths.ProfileDir(), "package.json");
            if (!File.Exists(pj)) return;
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(pj, Encoding.UTF8));
                if (!obj.Map.TryGetValue("dependencies", out object depsObj) || depsObj is not Dictionary<string, object> deps) return;
                foreach (var kv in deps)
                {
                    string val = kv.Value as string;
                    if (val == null) continue;
                    if (!val.StartsWith("link:", StringComparison.OrdinalIgnoreCase)
                        && !val.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = ParseLocalDepPath(val);
                    if (path == null || !Directory.Exists(path)) continue;
                    string pkg = Path.Combine(path, "package.json");
                    if (!File.Exists(pkg)) continue;
                    try
                    {
                        if (!File.ReadAllText(pkg, Encoding.UTF8).Contains("@deepseek-ai/")) continue;
                    }
                    catch { continue; }
                    JunctionGuard.Ensure(Path.Combine(path, "node_modules"));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[提示] 外部安装依赖统一检查失败: " + ex.Message);
            }
        }

        /// <summary>
        /// ProfileHasPlugin：检查 dependencies 字典与 bundles 数组（dsh plugin add 写入路径是 dsh.profile.bundles）。
        /// 兼容 dsh.profile.bundles 和顶层 bundles 两种模板。
        /// </summary>
        public static bool ProfileHasPlugin(string pkgName)
        {
            string pj = Path.Combine(Paths.ProfileDir(), "package.json");
            if (!File.Exists(pj)) return false;
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(pj, Encoding.UTF8));
                bool hasDeps = false;
                if (obj.Map.TryGetValue("dependencies", out object depsObj) && depsObj is Dictionary<string, object> deps)
                {
                    foreach (string key in deps.Keys)
                    {
                        // dsh plugin add 用 package.json 的真实 name（可能带 @scope/）写 profile，
                        // 而 plugins.json 里可能是无 scope 简写。用 PackageNamesMatch 宽松匹配。
                        if (PackageNamesMatch(key, pkgName)) { hasDeps = true; break; }
                    }
                }
                if (!hasDeps) return false;
                return BundleContains(obj, pkgName);
            }
            catch { return false; }
        }

        private static bool BundleContains(JsonObject obj, string pkgName)
        {
            if (obj.Map.TryGetValue("bundles", out object top) && top is List<object> topList)
                foreach (var item in topList)
                    if (item is string s && PackageNamesMatch(s, pkgName)) return true;
            if (!obj.Map.TryGetValue("dsh", out object dshObj) || dshObj is not Dictionary<string, object> dsh) return false;
            if (!dsh.TryGetValue("profile", out object profObj) || profObj is not Dictionary<string, object> profile) return false;
            if (!profile.TryGetValue("bundles", out object bObj) || bObj is not List<object> bundles) return false;
            foreach (var item in bundles)
                if (item is string s && PackageNamesMatch(s, pkgName)) return true;
            return false;
        }

        public static int InstallPluginNpm(PluginSpec spec, CancellationToken ct = default)
        {
            // 注入防线：pkgName 拼进 dsh plugin add 命令行，白名单校验不过即拒绝
            if (!PluginSpec.IsSafePkgName(spec.PkgName))
            {
                Console.Error.WriteLine("[!] 插件包名含非法字符，拒绝执行: " + spec.PkgName);
                return ExitCodes.PluginInstallFailed;
            }
            string old = Environment.GetEnvironmentVariable("npm_config_registry");
            try
            {
                Environment.SetEnvironmentVariable("npm_config_registry", Config.Current.Registry);
                string dsh = PortableNode.DshCmdPath();
                if (dsh == null) return ExitCodes.DshInstallFailed;
                ct.ThrowIfCancellationRequested();
                return Shell.RunCmd("\"" + dsh + "\" plugin --profile web add " + spec.PkgName);
            }
            finally
            {
                Environment.SetEnvironmentVariable("npm_config_registry", old);
            }
        }

        public static int InstallPluginGithub(PluginSpec spec, CancellationToken ct = default)
        {
            // 注入防线：Id 参与本地路径与 mklink 命令；source 各段参与下载 URL
            if (!PluginSpec.IsSafeSlug(spec.Id))
            {
                Console.Error.WriteLine("[!] 插件 id 含非法字符，拒绝执行: " + spec.Id);
                return ExitCodes.PluginInstallFailed;
            }
            string[] srcParts = (spec.Source ?? "").Split('/');
            if (srcParts.Length != 3 || Array.Exists(srcParts, p => !PluginSpec.IsSafeSlug(p)))
            {
                Console.Error.WriteLine("[!] 插件 source 必须为 owner/repo/branch 三段白名单字符，拒绝执行: " + spec.Source);
                return ExitCodes.PluginInstallFailed;
            }
            string dir = EnsurePluginSource(spec, false, ct);
            if (dir == null) return ExitCodes.PluginInstallFailed;
            string dsh = PortableNode.DshCmdPath();
            if (dsh == null) return ExitCodes.DshInstallFailed;
            ct.ThrowIfCancellationRequested();
            return Shell.RunCmd("\"" + dsh + "\" plugin --profile web add \"" + dir + "\"");
        }

        public static string EnsurePluginSource(PluginSpec spec, bool force, CancellationToken ct = default)
        {
            string target = Path.Combine(Paths.PluginsDir, spec.Id);
            bool hasPkg = File.Exists(Path.Combine(target, "package.json"));
            bool hasDeps = Directory.Exists(Path.Combine(target, "node_modules"));
            bool hasLib = File.Exists(Path.Combine(target, "lib", "index.js"));
            if (!force && hasPkg && hasDeps && hasLib && File.Exists(Path.Combine(target, ".dsh-ready"))) return target;
            if (!force && hasPkg && !hasDeps) return PreparePluginDeps(spec, target, ct) ? target : null;

            string zipName = spec.Id + ".zip";
            string zipPath = Path.Combine(Paths.CacheDir, zipName);
            if (!File.Exists(zipPath))
            {
                Console.WriteLine("      下载 " + spec.Display + " 源码 ...");
                string[] urls = PluginZipUrls(spec);
                bool ok = false;
                foreach (string u in urls)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Shell.DownloadFile(u, zipPath)) { ok = true; break; }
                }
                if (!ok)
                {
                    // 所有 URL 失败（最常见是分支名变了：main <-> master）。探测默认分支重试一次。
                    string altBranch = MaybeRetryWithDefaultBranch(spec);
                    if (altBranch != null) return EnsurePluginSource(spec, force, ct);
                    return null;
                }
            }

            Console.WriteLine("      解压 " + spec.Display + " ...");
            string tmp = Path.Combine(Paths.PluginsDir, "_" + spec.Id + "_tmp");
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
                    Console.Error.WriteLine("      解压尝试 " + (attempt + 1) + "/3 失败: " + ex.Message);
                    Thread.Sleep(1500);
                }
            }
            if (!extracted)
            {
                Console.Error.WriteLine("[!] " + spec.Display + " 解压失败（可能被占用或磁盘问题）。");
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
                    // 仓库作者可能用 scoped 写法（@scope/name）发布到 npm，但 plugins.json 里写的是
                    // 不带 scope 的简写。优先严格相等；否则按去 scope 后的尾段比较。
                    if (PackageNamesMatch(name, spec.PkgName) || PackageNamesMatch(name, spec.Id))
                    {
                        found = d; break;
                    }
                }
                if (found == null)
                {
                    // monorepo：包可能在 repo 根的子目录里（如 dsh-im-bot-main/im-channel）。
                    // 递归搜索真正含 package.json 的包目录。
                    found = FindPackageRecursive(tmp, spec);
                }
                if (found == null)
                {
                    Console.Error.WriteLine("[!] 源码包中未找到 " + spec.Id + " 包（pkgName=" + spec.PkgName + "）——可能默认分支错了。探测中 ...");
                    string altBranch = MaybeRetryWithDefaultBranch(spec);
                    if (altBranch != null)
                    {
                        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                        return EnsurePluginSource(spec, force, ct);
                    }
                    return null;
                }
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(found, target);
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 解压整理失败: " + ex.Message);
                return null;
            }
            return PreparePluginDeps(spec, target, ct) ? target : null;
        }

        public static bool PreparePluginDeps(PluginSpec spec, string target, CancellationToken ct = default)
        {
            string lockFile = Path.Combine(target, "pnpm-lock.yaml");
            try { if (File.Exists(lockFile)) File.Delete(lockFile); } catch { }
            bool hasLib = File.Exists(Path.Combine(target, "lib", "index.js"));
            if (hasLib) StripDevDependencies(target);
            Console.WriteLine("      安装 " + spec.Display + " 依赖 ...");
            string pnpm = PortableNode.PnpmCmd();
            string installCmd = "\"" + pnpm + "\" install --ignore-scripts --no-frozen-lockfile --registry=" + Config.Current.Registry
                + " --cache-dir=\"" + Paths.NpmCacheDir + "\"";
            if (Shell.RunCmdIn(installCmd, target) != 0) return false;
            if (!hasLib)
            {
                Console.WriteLine("      构建 " + spec.Display + " ...");
                if (Shell.RunCmdIn("\"" + pnpm + "\" build", target) != 0) return false;
            }
            if (!File.Exists(Path.Combine(target, "lib", "index.js")))
            {
                Console.Error.WriteLine("[!] " + spec.Display + " 构建后仍未找到 lib/index.js。");
                return false;
            }
            JunctionGuard.Ensure(Path.Combine(target, "node_modules"));
            try { File.WriteAllText(Path.Combine(target, ".dsh-ready"), AppMain.Version, new UTF8Encoding(false)); } catch { }
            return true;
        }

        public static void StripDevDependencies(string target)
        {
            string pj = Path.Combine(target, "package.json");
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(pj, Encoding.UTF8));
                if (obj.Writable.Remove("devDependencies"))
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var kv in obj.Entries) dict[kv.Key] = kv.Value;
                    File.WriteAllText(pj, JsonMini.Stringify(dict), new UTF8Encoding(false));
                }
            }
            catch { }
        }
        /// <summary>
        /// 包名宽松匹配：plugins.json 里写 "dsh-skills-manager"，但仓库 package.json 里可能是
        /// "@scope/dsh-skills-manager"。先去 scope 再比尾段。
        /// </summary>
        /// <summary>
        /// 递归查找解压目录里的包目录（支持 monorepo：包在子目录下）。
        /// 只匹配含 package.json 且 name 与 id/pkgName 匹配的目录。
        /// </summary>
        public static string FindPackageRecursive(string rootDir, PluginSpec spec)
        {
            try
            {
                // 0) 若配置了 repoSub（monorepo 子目录），优先精确定位
                if (!string.IsNullOrEmpty(spec.RepoSub))
                {
                    foreach (string dir in System.IO.Directory.EnumerateDirectories(rootDir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        if (!dir.Replace('\\', '/').EndsWith("/" + spec.RepoSub.Trim('/'), StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(Path.Combine(dir, "package.json"))) return dir;
                    }
                }
                // 1) 按 pkgName / id 匹配
                foreach (string dir in System.IO.Directory.EnumerateDirectories(rootDir, "*", System.IO.SearchOption.AllDirectories))
                {
                    string pj = Path.Combine(dir, "package.json");
                    if (!File.Exists(pj)) continue;
                    string name = ReadPackageName(pj);
                    if (PackageNamesMatch(name, spec.PkgName) || PackageNamesMatch(name, spec.Id))
                        return dir;
                }
                // 2) 回退：repo 里第一个可装包
                string fallback = FirstInstallablePackage(rootDir);
                if (fallback != null)
                {
                    // 显式告警：回退装错包比报错更糟——症状会以"工具没出现"的形态在远处爆发
                    Console.Error.WriteLine("      [提示] 未按名称匹配到 " + spec.Id + "，回退选用第一个可构建包: " + fallback);
                    Console.Error.WriteLine("      [提示] 若装错包，请在 plugins.json 为该插件加 \"repoSub\" 字段精确指定子目录。");
                }
                return fallback;
            }
            catch { }
            return null;
        }

        /// <summary>找 rootDir 下第一个含 package.json + lib（或可构建）的包目录。</summary>
        private static string FirstInstallablePackage(string rootDir)
        {
            foreach (string dir in System.IO.Directory.EnumerateDirectories(rootDir, "*", System.IO.SearchOption.AllDirectories))
            {
                if (!File.Exists(Path.Combine(dir, "package.json"))) continue;
                if (File.Exists(Path.Combine(dir, "lib", "index.js"))) return dir;
                if (File.Exists(Path.Combine(dir, "tsconfig.json"))) return dir;
            }
            return null;
        }

        public static bool PackageNamesMatch(string actual, string expected)
        {
            if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(expected)) return false;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            string a = actual.StartsWith("@") ? actual.Substring(actual.IndexOf('/') + 1) : actual;
            string e = expected.StartsWith("@") ? expected.Substring(expected.IndexOf('/') + 1) : expected;
            return string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
        }

        public static string ReadPackageName(string packageJsonPath)
        {
            try
            {
                JsonObject obj = JsonMini.Parse(File.ReadAllText(packageJsonPath, Encoding.UTF8));
                return obj.GetString("name") ?? "";
            }
            catch { return ""; }
        }

        public static int InstallPnpm()
        {
            string pnpm = PortableNode.PnpmCmd();
            if (File.Exists(pnpm)) return 0;
            Console.WriteLine("      安装 pnpm ...");
            string cmdLine = "\"" + PortableNode.NpmCmd() + "\" install -g pnpm --registry=" + Config.Current.Registry
                + " --cache=\"" + Paths.NpmCacheDir + "\" --no-fund --no-audit --loglevel=notice";
            int code = Shell.RunCmd(cmdLine);
            if (code != 0 || !File.Exists(pnpm))
            {
                Console.Error.WriteLine("[!] pnpm 安装失败。");
                return ExitCodes.PluginInstallFailed;
            }
            return ExitCodes.Success;
        }

        public static void EnsureProfile()
        {
            string dir = Paths.ProfileDir();
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
                Console.Error.WriteLine("[提示] profile 初始化失败: " + ex.Message);
            }
        }

        public static string[] PluginZipUrls(PluginSpec spec)
        {
            string[] parts = spec.Source.Split('/');
            if (parts.Length != 3) return new string[0];
            string direct = AppMain.Codeload + "/" + parts[0] + "/" + parts[1] + "/zip/refs/heads/" + parts[2];
            if (Config.Current.GithubProxy.Length > 0)
                return new[] { Config.Current.GithubProxy + direct, direct };
            var urls = new List<string> { direct };
            foreach (string p in AppMain.GithubProxies) urls.Add(p + direct);
            return urls.ToArray();
        }

        /// <summary>
        /// 通过 GitHub API 探测仓库的 default_branch。返回 null = 探测失败。
        /// </summary>
        public static string DetectDefaultBranch(string owner, string repo)
        {
            string api = "https://api.github.com/repos/" + owner + "/" + repo;
            string[] candidates;
            if (Config.Current.GithubProxy.Length > 0)
                candidates = new[] { Config.Current.GithubProxy + api, api };
            else
            {
                var list = new List<string> { api };
                foreach (string p in AppMain.GithubProxies) list.Add(p + api);
                candidates = list.ToArray();
            }
            foreach (string u in candidates)
            {
                string json = Http.GetString(u, timeoutSeconds: 12, accept: "application/vnd.github+json");
                if (json == null) continue;
                try
                {
                    JsonObject obj = JsonMini.Parse(json);
                    string branch = obj.GetString("default_branch");
                    if (!string.IsNullOrEmpty(branch)) return branch;
                }
                catch { }
            }
            return null;
        }

        public static string MaybeRetryWithDefaultBranch(PluginSpec spec)
        {
            string[] parts = spec.Source.Split('/');
            if (parts.Length != 3) return null;
            string detected = DetectDefaultBranch(parts[0], parts[1]);
            if (detected == null) return null;
            if (string.Equals(detected, parts[2], StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("      [提示] 探测到 default branch = " + detected + "（与配置一致）。");
                return null;
            }
            Console.Error.WriteLine("      [提示] 探测到 default branch = " + detected + "（配置为 " + parts[2] + "），改用探测结果重试。");
            var altSpec = new PluginSpec(spec.Id, spec.Display, spec.PkgName, spec.ViaNpm,
                parts[0] + "/" + parts[1] + "/" + detected);
            string altTarget = Path.Combine(Paths.CacheDir, spec.Id + "-" + detected + ".zip");
            string[] urls = PluginZipUrls(altSpec);
            foreach (string u in urls)
            {
                if (Shell.DownloadFile(u, altTarget))
                {
                    try { File.Delete(Path.Combine(Paths.CacheDir, spec.Id + ".zip")); } catch { }
                    try { File.Move(altTarget, Path.Combine(Paths.CacheDir, spec.Id + ".zip")); } catch { }
                    return detected;
                }
            }
            return null;
        }
    }
}
