// DshLauncher.Core — 路径 + 配置 + JSON
namespace DshLauncher
{
    public static class Paths
    {
        public static string RuntimeDir { get; private set; }
        public static string CacheDir { get; private set; }
        public static string NpmCacheDir { get; private set; }
        public static string PluginsDir { get; private set; }
        public static string LogsDir { get; private set; }
        public static string NodeDir { get; set; }
        public static string ConfigJsonPath { get; private set; }
        public static string ConfigTxtPath { get; private set; }

        public static void Init()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            RuntimeDir = Path.Combine(local, "dsh-launcher");
            CacheDir = Path.Combine(RuntimeDir, "cache");
            NpmCacheDir = Path.Combine(RuntimeDir, "npm-cache");
            PluginsDir = Path.Combine(RuntimeDir, "plugins");
            LogsDir = Path.Combine(RuntimeDir, "logs");
            ConfigJsonPath = Path.Combine(RuntimeDir, "config.json");
            ConfigTxtPath = Path.Combine(RuntimeDir, "config.txt");
            try
            {
                Directory.CreateDirectory(RuntimeDir);
                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(NpmCacheDir);
                Directory.CreateDirectory(PluginsDir);
                Directory.CreateDirectory(LogsDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 无法创建运行目录 " + RuntimeDir + " : " + ex.Message);
            }
        }

        public static string NodeModulesRoot =>
            NodeDir == null ? null : Path.Combine(NodeDir, "node_modules");

        public static string DshHome()
        {
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        }

        public static string ProfileDir() => Path.Combine(DshHome(), "profiles", "web");
    }

    public sealed class LauncherConfig
    {
        public string Registry = "https://registry.npmmirror.com";
        public string GithubProxy = "";
        public string Integrity = "lax";
        public bool ProtectExternal = false;
        public string LogLevel = "info";
        public string PinnedNodeVersion = "v24.19.0";

        /// <summary>
        /// harness 共享核心依赖的版本钉子（包短名 → 版本，如 "dsh-llm" → "0.1.1-rc.2"）。
        /// 默认空 = 用代码内置钉子；config.json 的 sharedCoreSpecs 可逐包覆盖——
        /// 上游发布新版本/删除 rc tag 时无需发新版启动器即可现场修复自愈路径。
        /// </summary>
        public Dictionary<string, string> SharedCoreSpecs { get; set; } = new();
    }

    public static class Config
    {
        public static LauncherConfig Current { get; private set; } = new();

        public static LauncherConfig Load()
        {
            var cfg = new LauncherConfig();

            if (File.Exists(Paths.ConfigJsonPath))
            {
                try
                {
                    var obj = JsonMini.Parse(File.ReadAllText(Paths.ConfigJsonPath, Encoding.UTF8));
                    string s;
                    if ((s = obj.GetString("registry")) != null) cfg.Registry = s;
                    if ((s = obj.GetString("githubProxy")) != null) cfg.GithubProxy = s;
                    if ((s = obj.GetString("integrity")) != null) cfg.Integrity = s;
                    if ((s = obj.GetString("logLevel")) != null) cfg.LogLevel = s;
                    if ((s = obj.GetString("pinnedNodeVersion")) != null) cfg.PinnedNodeVersion = s;
                    cfg.ProtectExternal = obj.GetBool("protectExternal", cfg.ProtectExternal);
                    // sharedCoreSpecs: { "dsh-llm": "0.2.0", "cordis": "4.1.0-rc.1" } —— 逐包覆盖内置钉子
                    if (obj.Map.TryGetValue("sharedCoreSpecs", out object specsObj)
                        && specsObj is Dictionary<string, object> specs)
                    {
                        foreach (var kv in specs)
                        {
                            string ver = kv.Value as string;
                            if (PluginSpec.IsSafeSlug(kv.Key) && !string.IsNullOrEmpty(ver)
                                && Regex.IsMatch(ver, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$"))
                            {
                                cfg.SharedCoreSpecs[kv.Key] = ver;
                            }
                            else
                            {
                                Console.Error.WriteLine("[!] sharedCoreSpecs 条目非法，已跳过: " + kv.Key);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[!] config.json 解析失败，使用默认配置: " + ex.Message);
                }
            }
            else if (File.Exists(Paths.ConfigTxtPath))
            {
                Console.Error.WriteLine("[提示] 检测到旧 config.txt，正在自动迁移到 config.json ...");
                try
                {
                    foreach (string line in File.ReadAllLines(Paths.ConfigTxtPath, Encoding.UTF8))
                    {
                        string s = line.Trim();
                        int eq = s.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = s.Substring(0, eq).Trim();
                        string val = s.Substring(eq + 1).Trim();
                        if (string.Equals(key, "registry", StringComparison.OrdinalIgnoreCase) && val.Length > 0)
                            cfg.Registry = val;
                        else if (string.Equals(key, "githubProxy", StringComparison.OrdinalIgnoreCase) && val.Length > 0)
                            cfg.GithubProxy = val;
                    }
                    MigrateConfigTxt(cfg);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[!] config.txt 迁移失败: " + ex.Message);
                }
            }

            // 环境变量覆盖
            string env;
            if (!string.IsNullOrWhiteSpace(env = Environment.GetEnvironmentVariable("DSH_REGISTRY"))) cfg.Registry = env.Trim();
            if (!string.IsNullOrWhiteSpace(env = Environment.GetEnvironmentVariable("DSH_GITHUB_PROXY"))) cfg.GithubProxy = env.Trim();
            if (!string.IsNullOrWhiteSpace(env = Environment.GetEnvironmentVariable("DSH_INTEGRITY"))) cfg.Integrity = env.Trim();
            if (!string.IsNullOrWhiteSpace(env = Environment.GetEnvironmentVariable("DSH_LOG_LEVEL"))) cfg.LogLevel = env.Trim();
            if (!string.IsNullOrWhiteSpace(env = Environment.GetEnvironmentVariable("DSH_PINNED_NODE"))) cfg.PinnedNodeVersion = env.Trim();
            string pe = Environment.GetEnvironmentVariable("DSH_PROTECT_EXTERNAL");
            if (!string.IsNullOrWhiteSpace(pe))
                cfg.ProtectExternal = pe == "1" || pe.Equals("true", StringComparison.OrdinalIgnoreCase);

            // 校验
            if (!IsValidHttpsUrl(cfg.Registry))
            {
                Console.Error.WriteLine("[!] 配置项 registry 不是合法 https/http URL，已回退默认: " + cfg.Registry);
                cfg.Registry = "https://registry.npmmirror.com";
            }
            if (cfg.GithubProxy.Length > 0 && !IsValidProxyPrefix(cfg.GithubProxy))
            {
                Console.Error.WriteLine("[!] 配置项 githubProxy 含非法字符，已清空: " + cfg.GithubProxy);
                cfg.GithubProxy = "";
            }
            switch (cfg.Integrity)
            {
                case "strict": case "lax": case "none": break;
                default:
                    Console.Error.WriteLine("[!] 配置项 integrity 非法 (" + cfg.Integrity + ")，回退 lax");
                    cfg.Integrity = "lax";
                    break;
            }

            Current = cfg;
            return cfg;
        }

        public static bool IsValidHttpsUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!Uri.TryCreate(s, UriKind.Absolute, out Uri u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            return u.Host.IndexOfAny(new[] { '|', ';', '<', '>', ' ', '"' }) < 0;
        }

        public static bool IsValidProxyPrefix(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!IsValidHttpsUrl(s)) return false;
            return s.IndexOfAny(new[] { '&', '|', ';', '<', '>', '`', '$', '\n', '\r' }) < 0
                && s.EndsWith("/");
        }

        private static void MigrateConfigTxt(LauncherConfig cfg)
        {
            try
            {
                var json = new Dictionary<string, object>
                {
                    ["registry"] = cfg.Registry,
                    ["integrity"] = cfg.Integrity,
                    ["protectExternal"] = cfg.ProtectExternal,
                    ["logLevel"] = cfg.LogLevel,
                    ["pinnedNodeVersion"] = cfg.PinnedNodeVersion,
                };
                if (cfg.GithubProxy.Length > 0) json["githubProxy"] = cfg.GithubProxy;
                string content = JsonMini.Stringify(json);
                content = PrettyPrintJson(content);
                File.WriteAllText(Paths.ConfigJsonPath, content, new UTF8Encoding(false));
                string bak = Paths.ConfigTxtPath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(Paths.ConfigTxtPath, bak);
                Console.WriteLine("      已写入 " + Paths.ConfigJsonPath + "；原 config.txt 已备份为 config.txt.bak");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 写入 config.json 失败: " + ex.Message);
            }
        }

        private static string PrettyPrintJson(string minified)
        {
            try
            {
                var obj = JsonMini.Parse(minified);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                bool first = true;
                foreach (var kv in obj.Entries)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    sb.Append("  ");
                    sb.Append(JsonMini.Stringify(new Dictionary<string, object> { { kv.Key, kv.Value } }).Trim('{', '}'));
                }
                if (!first) sb.AppendLine();
                sb.Append("}");
                return sb.ToString();
            }
            catch { return minified; }
        }

        /// <summary>GUI 保存设置时调用：把当前 Config 写回 config.json。</summary>
        public static void Save(LauncherConfig cfg = null)
        {
            cfg ??= Current;
            try
            {
                var json = new Dictionary<string, object>
                {
                    ["registry"] = cfg.Registry,
                    ["integrity"] = cfg.Integrity,
                    ["protectExternal"] = cfg.ProtectExternal,
                    ["logLevel"] = cfg.LogLevel,
                    ["pinnedNodeVersion"] = cfg.PinnedNodeVersion,
                };
                if (cfg.GithubProxy.Length > 0) json["githubProxy"] = cfg.GithubProxy;
                if (cfg.SharedCoreSpecs.Count > 0)
                {
                    var specs = new Dictionary<string, object>();
                    foreach (var kv in cfg.SharedCoreSpecs) specs[kv.Key] = kv.Value;
                    json["sharedCoreSpecs"] = specs;
                }
                File.WriteAllText(Paths.ConfigJsonPath, JsonMini.Stringify(json), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 保存 config.json 失败: " + ex.Message);
            }
        }
    }
}
