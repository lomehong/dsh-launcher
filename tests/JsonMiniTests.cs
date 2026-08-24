using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    public class JsonMiniTests
    {
        [Fact]
        public void Parse_EmptyObject()
        {
            var o = JsonMini.Parse("{}");
            Assert.Equal(0, o.Map.Count);
        }

        [Fact]
        public void Parse_SimpleObject()
        {
            var o = JsonMini.Parse("{\"a\":1,\"b\":\"x\"}");
            Assert.Equal("1", o.Map["a"].ToString());
            Assert.Equal("x", o.GetString("b"));
        }

        [Fact]
        public void Parse_NestedObject()
        {
            // 这是原 Regex 解析器（用 [^}]*）会失败的场景
            var o = JsonMini.Parse("{\"deps\":{\"a\":\"1\",\"b\":\"^{}\"}}");
            var deps = o.Map["deps"] as Dictionary<string, object>;
            Assert.NotNull(deps);
            Assert.Equal(2, deps.Count);
            Assert.Equal("1", deps["a"].ToString());
            Assert.Equal("^{}", deps["b"].ToString());
        }

        [Fact]
        public void Parse_ArrayOfMixedTypes()
        {
            var o = JsonMini.Parse("{\"items\":[1,\"two\",true,null,3.14]}");
            var items = o.Map["items"] as List<object>;
            Assert.Equal(5, items.Count);
            Assert.Equal(1L, items[0]);
            Assert.Equal("two", items[1]);
            Assert.Equal(true, items[2]);
            Assert.Null(items[3]);
            Assert.Equal(3.14, Convert.ToDouble(items[4], System.Globalization.CultureInfo.InvariantCulture), 5);
        }

        [Fact]
        public void Parse_StringEscapes()
        {
            var o = JsonMini.Parse("{\"s\":\"a\\\"b\\\\c\\n\\t\\u00e4\"}");
            Assert.Equal("a\"b\\c\n\tä", o.GetString("s"));
        }

        [Fact]
        public void Parse_BoolVariants()
        {
            var o = JsonMini.Parse("{\"t\":true,\"f\":false}");
            Assert.Equal(true, o.GetBool("t", false));
            Assert.Equal(false, o.GetBool("f", true));
        }

        [Fact]
        public void Parse_TrailingGarbageRejected()
        {
            Assert.Throws<FormatException>(() => JsonMini.Parse("{\"a\":1} junk"));
        }

        [Fact]
        public void Parse_UnterminatedStringRejected()
        {
            Assert.Throws<FormatException>(() => JsonMini.Parse("{\"a\":\"unterminated"));
        }

        [Fact]
        public void Parse_NegativeAndZeroNumbers()
        {
            var o = JsonMini.Parse("{\"n\":-42,\"z\":0,\"f\":-3.14}");
            Assert.Equal(-42L, o.Map["n"]);
            Assert.Equal(0L, o.Map["z"]);
            Assert.Equal(-3.14, Convert.ToDouble(o.Map["f"], System.Globalization.CultureInfo.InvariantCulture), 5);
        }

        [Fact]
        public void GetBool_ParsesStringTrueFalse()
        {
            var o = JsonMini.Parse("{\"a\":\"true\",\"b\":\"false\"}");
            Assert.True(o.GetBool("a", false));
            Assert.False(o.GetBool("b", true));
        }

        [Fact]
        public void GetBool_DefaultWhenMissing()
        {
            var o = JsonMini.Parse("{}");
            Assert.True(o.GetBool("missing", true));
            Assert.False(o.GetBool("missing", false));
        }

        [Fact]
        public void Stringify_Roundtrip()
        {
            var input = "{\"name\":\"test\",\"items\":[1,2,3],\"nested\":{\"k\":\"v\"}}";
            var obj = JsonMini.Parse(input);
            var back = JsonMini.Stringify(BuildRawMap(obj));
            var reparsed = JsonMini.Parse(back);
            Assert.Equal("test", reparsed.GetString("name"));
            var items = reparsed.Map["items"] as List<object>;
            Assert.Equal(3, items.Count);
        }

        [Fact]
        public void Stringify_EscapesSpecialChars()
        {
            var map = new Dictionary<string, object>
            {
                { "s", "a\"b\\c\n\td" }
            };
            var s = JsonMini.Stringify(map);
            Assert.Contains("\\\"", s);
            Assert.Contains("\\\\", s);
            Assert.Contains("\\n", s);
            Assert.Contains("\\t", s);
        }

        private static Dictionary<string, object> BuildRawMap(JsonObject o)
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in o.Entries) d[kv.Key] = kv.Value;
            return d;
        }
    }

    public class ShellTests
    {
        [Theory]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("v0.1.0-rc.7", "0.1.0-rc.7")]
        [InlineData("dsh 0.1.0-rc.7 (built ...)", "0.1.0-rc.7")]
        [InlineData("", "")]
        public void GetVersionNumber_ExtractsSemver(string input, string expected)
        {
            Assert.Equal(expected, Shell.GetVersionNumber(input));
        }

        [Theory]
        [InlineData("1.0.0", "1.0.0", 0)]
        [InlineData("1.0.1", "1.0.0", 1)]
        [InlineData("1.0.0", "1.0.1", -1)]
        [InlineData("0.1.0-rc.7", "0.1.0-rc.8", -1)]
        [InlineData("0.1.0-rc.7", "0.1.0", -1)]   // prerelease < stable
        [InlineData("0.1.0", "0.1.0-rc.1", 1)]   // stable > prerelease
        [InlineData("2.0.0", "1.99.99", 1)]
        public void CompareVersions_OrdersCorrectly(string a, string b, int expected)
        {
            Assert.Equal(expected, Shell.CompareVersions(a, b));
        }

        [Fact]
        public void StripNpmWarns_RemovesWarnLines()
        {
            string input = "npm warn deprecated foo@1.0.0\n0.1.0-rc.7\nnpm warn another\nmore stuff";
            string output = Shell.StripNpmWarns(input);
            Assert.DoesNotContain("npm warn", output);
            Assert.Contains("0.1.0-rc.7", output);
            Assert.Contains("more stuff", output);
        }

        [Fact]
        public void StripNpmWarns_HandlesNullAndEmpty()
        {
            Assert.Equal("", Shell.StripNpmWarns(null));
            Assert.Equal("", Shell.StripNpmWarns(""));
        }
    }

    public class ConfigValidationTests
    {
        [Theory]
        [InlineData("https://registry.npmjs.org", true)]
        [InlineData("https://registry.npmmirror.com", true)]
        [InlineData("http://localhost:4873", true)]
        [InlineData("", false)]
        [InlineData("not-a-url", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("https://foo.com|bar", false)]  // shell meta
        public void IsValidHttpsUrl(string input, bool expected)
        {
            Assert.Equal(expected, Config.IsValidHttpsUrl(input));
        }

        [Theory]
        [InlineData("https://ghfast.top/", true)]
        [InlineData("https://gh-proxy.com/", true)]
        [InlineData("https://ghproxy.net/", true)]
        [InlineData("", false)]                       // empty allowed (means default)
        [InlineData("https://foo.com", false)]        // must end with /
        [InlineData("https://foo.com/&evil=1", false)] // shell meta
        [InlineData("javascript:alert(1)/", false)]  // wrong scheme
        public void IsValidProxyPrefix(string input, bool expected)
        {
            Assert.Equal(expected, Config.IsValidProxyPrefix(input));
        }
    }

    /// <summary>
    /// 注意：本类调用 Paths.Init()（重置全局 RuntimeDir 等静态状态），
    /// 必须与其它路径型测试同入 PathMutating 集合串行执行——否则并行集合
    /// 会把其他测试正在使用的 RuntimeDir 重置回真实 %LOCALAPPDATA%。
    /// </summary>
    [Collection("PathMutating")]
    public class ParseLocalDepPathTests
    {
        [Fact]
        public void LinkPrefix_ReturnsAbsolutePath()
        {
            // 不依赖 ProfileDir 的相对解析测试
            // 当输入是绝对路径时直接归一返回
            var abs = Path.GetFullPath(@"C:\code\plugin");
            var result = PluginManager.ParseLocalDepPath("link:" + abs);
            Assert.Equal(abs, result);
        }

        [Fact]
        public void FilePrefix_NormalizesDoubleSlashes()
        {
            // E://code//... 这种 Windows 双斜杠写法
            var abs = Path.GetFullPath(@"C:\code\plugin");
            var input = "file:" + abs.Replace(@"\", @"//");
            var result = PluginManager.ParseLocalDepPath(input);
            Assert.Equal(abs, result);
        }
        [Fact]
        public void Semver_ReturnsNull()
        {
            Assert.Null(PluginManager.ParseLocalDepPath("^1.0.0"));
            Assert.Null(PluginManager.ParseLocalDepPath("1.0.0"));
            // file:../sibling 是相对路径，会被解析为 ProfileDir/sibling，不为 null
            // 测试仅保证：纯 semver 写法返回 null
        }

        [Fact]
        public void EmptyOrNull_ReturnsNull()
        {
            Assert.Null(PluginManager.ParseLocalDepPath(null));
            Assert.Null(PluginManager.ParseLocalDepPath(""));
            Assert.Null(PluginManager.ParseLocalDepPath("link:"));
            Assert.Null(PluginManager.ParseLocalDepPath("file:"));
        }

        [Fact]
        public void LauncherPath_ReturnsNull()
        {
            // 必须先 Init Paths（PluginManager.ParseLocalDepPath 内部读 Paths.PluginsDir）
            Paths.Init();
            // 指向启动器 plugins 目录的 link: 不算外部安装
            var inside = Path.Combine(Paths.PluginsDir, "dsh-yuyi");
            var input = "link:" + inside;
            Assert.Null(PluginManager.ParseLocalDepPath(input));
        }
    }

    public class PluginSpecTests
    {
        [Fact]
        public void Defaults_ContainsAllNinePlugins()
        {
            var defaults = PluginDefaults.BuiltIn;
            Assert.Equal(9, defaults.Length);
            // 关键插件必须存在
            Assert.Contains(defaults, p => p.Id == "dsh-yuyi");
            Assert.Contains(defaults, p => p.Id == "dsh-market");
            Assert.Contains(defaults, p => p.Id == "dsh-at-file");
        }

        [Fact]
        public void Defaults_AllHaveRequiredSource()
        {
            foreach (var p in PluginDefaults.BuiltIn)
            {
                Assert.False(string.IsNullOrEmpty(p.PkgName), $"Plugin {p.Id} missing pkgName");
                if (!p.ViaNpm)
                {
                    // github 插件必须形如 owner/repo/branch
                    var parts = p.Source.Split('/');
                    Assert.Equal(3, parts.Length);
                    Assert.False(string.IsNullOrEmpty(parts[0]));
                    Assert.False(string.IsNullOrEmpty(parts[1]));
                    Assert.False(string.IsNullOrEmpty(parts[2]));
                }
            }
        }
    }
}
