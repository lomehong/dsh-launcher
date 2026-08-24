// DshLauncher.Core — 用户插件源解析（GitHub URL / SSH / owner/repo / npm 包名）
// 纯字符串解析，无 WPF 依赖，方便单测覆盖。
namespace DshLauncher
{
    using System.Text.RegularExpressions;

    public static class PluginInputParser
    {
        public enum Kind { Unknown, Npm, GitHub }

        public sealed class Result
        {
            public bool Success { get; init; }
            public string Error { get; init; } = "";
            public Kind Kind { get; init; }
            public string Id { get; init; } = "";       // plugins.json 的 id（slug）
            public string Display { get; init; } = "";  // 显示名
            public string Source { get; init; } = "";   // npm: 包名；github: "owner/repo/branch"
        }

        /// <summary>
        /// 把用户输入解析为插件规格。
        /// - "pkg-name" 或 "@scope/pkg-name" → npm
        /// - "owner/repo" 或 "owner/repo/branch" → github (默认 main)
        /// - "https://github.com/owner/repo[ /tree/branch ]" → github
        /// - "git@github.com:owner/repo[ .git ]" → github
        /// </summary>
        public static Result Parse(string raw)
        {
            string error = "", id = "", display = "", source = "";
            Kind kind = Kind.Unknown;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Result { Error = "输入为空" };
            }
            raw = raw.Trim();

            // npm: @scope/name 或 name（不能含 /）
            if (!raw.Contains("://") && !raw.StartsWith("git@"))
            {
                // npm: 纯包名（不含 /）— 含 / 的走 GitHub 分支
                // npm: scoped (@scope/name) 或普通（不含 /）
                if (Regex.IsMatch(raw, @"^(@[A-Za-z0-9_\-\.]+/[A-Za-z0-9_\-\.]+|[A-Za-z0-9_\-\.]+)$"))
                {
                    string pkgName = raw;
                    id = pkgName.TrimStart('@').Replace('/', '-');
                    display = pkgName;
                    kind = Kind.Npm;
                    source = pkgName;
                    return new Result { Success = true, Kind = Kind.Npm, Id = id, Display = display, Source = source };
                }
                Match m = Regex.Match(raw, @"^([A-Za-z0-9_\-\.]+)/([A-Za-z0-9_\-\.]+)(?:/([A-Za-z0-9_\-\.]+))?$");
                if (m.Success)
                {
                    string owner = m.Groups[1].Value;
                    string repo = m.Groups[2].Value;
                    string branch = m.Groups[3].Success ? m.Groups[3].Value : "main";
                    id = repo;
                    display = repo;
                    kind = Kind.GitHub;
                    source = owner + "/" + repo + "/" + branch;
                    return new Result { Success = true, Kind = Kind.GitHub, Id = id, Display = display, Source = source };
                }
            }

            // GitHub URL: https://github.com/owner/repo[ /tree/branch ]
            Match gh = Regex.Match(raw,
                @"^(?:https?://)?github\.com/([^/\s]+)/([^/\s#?]+?)(?:\.git)?(?:/tree/([^/\s#?]+))?/?$",
                RegexOptions.IgnoreCase);
            if (gh.Success)
            {
                string owner = gh.Groups[1].Value;
                string repo = gh.Groups[2].Value;
                string branch = gh.Groups[3].Success ? gh.Groups[3].Value : "main";
                id = repo;
                display = repo;
                kind = Kind.GitHub;
                source = owner + "/" + repo + "/" + branch;
                return new Result { Success = true, Kind = Kind.GitHub, Id = id, Display = display, Source = source };
            }

            // git@github.com:owner/repo.git
            Match ssh = Regex.Match(raw, @"^git@github\.com:([^/\s]+)/([^/\s]+?)(?:\.git)?$");
            if (ssh.Success)
            {
                string owner = ssh.Groups[1].Value;
                string repo = ssh.Groups[2].Value;
                id = repo;
                display = repo;
                kind = Kind.GitHub;
                source = owner + "/" + repo + "/main";
                return new Result { Success = true, Kind = Kind.GitHub, Id = id, Display = display, Source = source };
            }

            return new Result { Error = "无法识别格式。\n支持：GitHub URL、git@ SSH、owner/repo、@scope/pkg、pkg-name" };
        }
    }
}
