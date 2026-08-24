// DshLauncher.Core — 插件规格 + 注册表
namespace DshLauncher
{
    public sealed class PluginSpec
    {
        public string Id { get; set; }
        public string Display { get; set; }
        public string PkgName { get; set; }
        public bool ViaNpm { get; set; }
        public string Source { get; set; }     // npm: 包名；github: "owner/repo/branch"
        public string RepoSub { get; set; }   // monorepo：仓库内子目录（如 "im-channel"），可选
        public bool Required { get; set; } = true;

        public PluginSpec() { }
        public PluginSpec(string id, string display, string pkgName, bool viaNpm, string source)
        {
            Id = id; Display = display; PkgName = pkgName; ViaNpm = viaNpm; Source = source;
        }

        public override string ToString() => $"{(ViaNpm ? "npm" : "git")} {Id} ({PkgName})";

        // ============ 注入防护白名单（PkgName/Id 最终会拼进 cmd.exe 命令行与本地路径） ============

        /// <summary>
        /// npm 包名白名单：可选 @scope/ 前缀 + 名称段，字符限 [A-Za-z0-9._-]。
        /// 拒绝空格、;&|&lt;&gt;$"`^()! 等全部 cmd 元字符。与 Config.IsValidProxyPrefix 同为
        /// "拼进命令行前必须过白名单"防线——区别于 URL 校验，这里防的是 plugins.json 注入。
        /// </summary>
        public static bool IsSafePkgName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 214) return false;
            return Regex.IsMatch(s, @"^(@[A-Za-z0-9][A-Za-z0-9._-]*/)?[A-Za-z0-9][A-Za-z0-9._-]*$");
        }

        /// <summary>slug 白名单：插件 id / GitHub owner、repo、branch 段（字符限 [A-Za-z0-9._-]）。</summary>
        public static bool IsSafeSlug(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 128) return false;
            return Regex.IsMatch(s, @"^[A-Za-z0-9][A-Za-z0-9._-]*$");
        }

        /// <summary>
        /// 整体安全校验：Id/PkgName 白名单；GitHub 源必须恰好 owner/repo/branch 三段且各段合法。
        /// PluginRegistry.Load 对用户 plugins.json 逐条过滤；安装路径再各设一道防线。
        /// </summary>
        public bool IsSafe()
        {
            if (!IsSafeSlug(Id ?? "")) return false;
            if (!IsSafePkgName(PkgName ?? "")) return false;
            if (!string.IsNullOrEmpty(RepoSub) && !IsSafeSlug(RepoSub)) return false;
            if (!ViaNpm)
            {
                string[] parts = (Source ?? "").Split('/');
                if (parts.Length != 3) return false;
                foreach (string p in parts)
                    if (!IsSafeSlug(p)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 插件注册表：默认 9 个内置 + 用户的 plugins.json 自定义。
    /// M1 仍兼容只读模式（Load），后续 M3 加入 Add/Remove/Save。
    /// </summary>
    public sealed class PluginRegistry
    {
        public const string UserPluginsFile = "plugins.json";  // 相对 RuntimeDir

        public List<PluginSpec> Plugins { get; } = new();

        /// <summary>从内置默认 + plugins.json 加载（后者覆盖或追加）。</summary>
        public static PluginRegistry Load()
        {
            var reg = new PluginRegistry();
            reg.Plugins.AddRange(BuiltIn);
            string userFile = Path.Combine(Paths.RuntimeDir, UserPluginsFile);
            if (!File.Exists(userFile)) return reg;
            try
            {
                var obj = JsonMini.Parse(File.ReadAllText(userFile, Encoding.UTF8));
                if (!obj.Map.TryGetValue("plugins", out object arrObj) || arrObj is not List<object> arr) return reg;
                foreach (object item in arr)
                {
                    if (item is not Dictionary<string, object> d) continue;
                    var spec = new PluginSpec
                    {
                        Id = AsString(d, "id") ?? "",
                        Display = AsString(d, "display") ?? "",
                        PkgName = AsString(d, "pkgName") ?? "",
                        ViaNpm = AsBool(d, "viaNpm", false),
                        Source = AsString(d, "source") ?? "",
                        RepoSub = AsString(d, "repoSub") ?? "",
                        Required = AsBool(d, "required", true),
                    };
                    // 注入防线：id/pkgName/source 最终会拼进 cmd.exe 命令行与本地路径，
                    // 非法条目直接跳过（内置默认条目已由单测保证全部通过该校验）。
                    if (!spec.IsSafe())
                    {
                        Console.Error.WriteLine("[!] plugins.json 条目含非法字符，已跳过: id=" + spec.Id
                            + "（id/pkgName 仅限字母数字 . _ - 与 npm scope；source 须为 owner/repo/branch）");
                        continue;
                    }
                    // 覆盖同名插件
                    int existing = reg.Plugins.FindIndex(p => p.Id == spec.Id);
                    if (existing >= 0) reg.Plugins[existing] = spec;
                    else reg.Plugins.Add(spec);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] plugins.json 解析失败，回退内置默认: " + ex.Message);
            }
            return reg;
        }

        /// <summary>保存非内置插件到 plugins.json（GUI 调用）。</summary>
        public void Save()
        {
            string userFile = Path.Combine(Paths.RuntimeDir, UserPluginsFile);
            var arr = new List<object>();
            foreach (var p in Plugins)
            {
                if (BuiltIn.Any(b => b.Id == p.Id && !IsCustomized(p, b))) continue;  // 跳过未修改的内置
                arr.Add(new Dictionary<string, object>
                {
                    ["id"] = p.Id,
                    ["display"] = p.Display ?? p.Id,
                    ["pkgName"] = p.PkgName ?? "",
                    ["viaNpm"] = p.ViaNpm,
                    ["source"] = p.Source ?? "",
                    ["repoSub"] = p.RepoSub ?? "",
                    ["required"] = p.Required,
                });
            }
            var root = new Dictionary<string, object> { ["plugins"] = arr };
            try
            {
                File.WriteAllText(userFile, JsonMini.Stringify(root), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 保存 plugins.json 失败: " + ex.Message);
            }
        }

        private static bool IsCustomized(PluginSpec cur, PluginSpec baseline) =>
            cur.Display != baseline.Display ||
            cur.PkgName != baseline.PkgName ||
            cur.ViaNpm != baseline.ViaNpm ||
            cur.Source != baseline.Source ||
            cur.Required != baseline.Required;

        /// <summary>默认 9 个插件（M1 兼容 v1.4.0）。</summary>
        public static readonly PluginSpec[] BuiltIn = new[]
        {
            new PluginSpec("dsh-at-file",          "at-file",        "dsh-at-file",                  false, "omdsh-dev/dsh-at-file/main"),
            new PluginSpec("dsh-genui",            "genui",          "@omdsh-dev/dsh-genui",         false, "omdsh-dev/dsh-genui/main"),
            new PluginSpec("dsh-visualize",        "visualize",      "@dsh-external/dsh-visualize", false, "Nagi-ovo/dsh-visualize/main"),
            new PluginSpec("dsh-automation",       "automation",     "@dsh-external/dsh-automation",false, "titanwings/dsh-automation/main"),
            new PluginSpec("dsh-better-sidebar",   "better-sidebar", "dsh-better-sidebar",          true,  "dsh-better-sidebar"),
            new PluginSpec("dsh-mnemon",           "mnemon",         "dsh-mnemon",                  true,  "dsh-mnemon"),
            new PluginSpec("dsh-vision-toolkit",   "vision-toolkit", "@anionex/dsh-vision-toolkit", true,  "@anionex/dsh-vision-toolkit"),
            new PluginSpec("dsh-market",           "market",         "@dsh-market/plugin",          true,  "@dsh-market/plugin"),
            new PluginSpec("dsh-yuyi",             "yuyi(御驿)",     "dsh-yuyi",                    false, "lomehong/dsh-yuyi/main"),
        };

        private static string AsString(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) ? v as string : null;
        private static bool AsBool(Dictionary<string, object> d, string key, bool def) =>
            d.TryGetValue(key, out object v) && v is bool b ? b : def;
    }

    /// <summary>向后兼容 v1.4 旧代码（plugin install 路径里直接读 BuiltIn）。</summary>
    public static class PluginDefaults
    {
        public static PluginSpec[] BuiltIn => PluginRegistry.BuiltIn;
        public static List<PluginSpec> Load() => new List<PluginSpec>(PluginRegistry.Load().Plugins);
    }
}
