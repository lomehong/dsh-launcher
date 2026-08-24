using System;
using System.IO;
using System.Reflection;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// 注入防线测试：plugins.json 的 id/pkgName/source 最终拼进 cmd.exe 命令行
    /// （dsh plugin add）与本地路径（mklink /J）。白名单校验不过的条目必须被
    /// PluginRegistry.Load 跳过、安装入口必须拒绝。
    /// </summary>
    public class PluginSafetyTests
    {
        // ---------- IsSafePkgName ----------

        [Theory]
        [InlineData("dsh-yuyi")]
        [InlineData("dsh-better-sidebar")]
        [InlineData("@dsh-market/plugin")]
        [InlineData("@anionex/dsh-vision-toolkit")]
        [InlineData("@omdsh-dev/dsh-genui")]
        [InlineData("a")]
        [InlineData("pkg.with.dots")]
        [InlineData("pkg_with_underscores")]
        public void SafePkgName_Accepts(string name)
        {
            Assert.True(PluginSpec.IsSafePkgName(name));
        }

        [Theory]
        [InlineData("pkg;calc")]
        [InlineData("pkg & whoami")]
        [InlineData("pkg|del x")]
        [InlineData("pkg && calc")]
        [InlineData("$(whoami)")]
        [InlineData("`whoami`")]
        [InlineData("pkg^cmd")]
        [InlineData("..\\evil")]
        [InlineData("../evil")]
        [InlineData("pkg name")]          // 空格
        [InlineData("a/b/c")]             // 双斜杠（伪 scope）
        [InlineData("@/pkg")]             // 空 scope
        [InlineData("")]                  // 空
        [InlineData(null)]
        [InlineData("-lead-dash")]        // 首字符 -
        public void SafePkgName_Rejects(string name)
        {
            Assert.False(PluginSpec.IsSafePkgName(name));
        }

        // ---------- IsSafeSlug ----------

        [Theory]
        [InlineData("dsh-at-file")]
        [InlineData("omdsh-dev")]
        [InlineData("Nagi-ovo")]
        [InlineData("main")]
        [InlineData("v1.2.3-rc.1")]
        [InlineData("im-channel")]
        public void SafeSlug_Accepts(string s)
        {
            Assert.True(PluginSpec.IsSafeSlug(s));
        }

        [Theory]
        [InlineData("a;b")]
        [InlineData("a&b")]
        [InlineData("a|b")]
        [InlineData("a b")]
        [InlineData("a/b")]
        [InlineData("../x")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("-lead")]
        [InlineData(".lead")]
        public void SafeSlug_Rejects(string s)
        {
            Assert.False(PluginSpec.IsSafeSlug(s));
        }

        // ---------- PluginSpec.IsSafe 整体 ----------

        [Fact]
        public void IsSafe_GithubSpec_WithValidSource_Passes()
        {
            var spec = new PluginSpec("dsh-yuyi", "yuyi", "dsh-yuyi", false, "lomehong/dsh-yuyi/main");
            Assert.True(spec.IsSafe());
        }

        [Fact]
        public void IsSafe_NpmSpec_IgnoresSourceShape()
        {
            var spec = new PluginSpec("market", "market", "@dsh-market/plugin", true, "@dsh-market/plugin");
            Assert.True(spec.IsSafe());
        }

        [Fact]
        public void IsSafe_GithubSpec_WithBadSource_Fails()
        {
            var spec = new PluginSpec("evil", "evil", "evil", false, "a/b;calc/main");
            Assert.False(spec.IsSafe());
        }

        [Fact]
        public void IsSafe_GithubSpec_TwoSegments_Fails()
        {
            var spec = new PluginSpec("evil", "evil", "evil", false, "owner/repo");
            Assert.False(spec.IsSafe());
        }

        [Fact]
        public void IsSafe_BadPkgName_Fails()
        {
            var spec = new PluginSpec("ok-id", "x", "pkg & whoami", false, "a/b/main");
            Assert.False(spec.IsSafe());
        }

        /// <summary>
        /// 内置 9 个默认插件必须全部通过白名单——否则白名单过严会把默认安装打挂。
        /// </summary>
        [Fact]
        public void AllBuiltIn_AreSafe()
        {
            Assert.All(PluginRegistry.BuiltIn, b => Assert.True(b.IsSafe(), "内置插件未通过白名单: " + b.Id));
        }
    }

    /// <summary>
    /// 核心依赖版本钉子外部化：config.json 的 sharedCoreSpecs 逐包覆盖内置钉子，
    /// 无效条目（非法字符/未知包名）被忽略。上游删除 rc tag 时改配置即可修复自愈路径。
    /// </summary>
    [Collection("PathMutating")]
    public class SharedCoreSpecsTests : IDisposable
    {
        private readonly string _fakeRuntime;

        public SharedCoreSpecsTests()
        {
            _fakeRuntime = Path.Combine(Path.GetTempPath(), "dsh-corespec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_fakeRuntime);
            SetInternalStatic(typeof(Paths), "RuntimeDir", _fakeRuntime);
            SetInternalStatic(typeof(Paths), "ConfigJsonPath", Path.Combine(_fakeRuntime, "config.json"));
            SetInternalStatic(typeof(Paths), "ConfigTxtPath", Path.Combine(_fakeRuntime, "config.txt"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_fakeRuntime)) Directory.Delete(_fakeRuntime, true); } catch { }
        }

        private static void SetInternalStatic(Type t, string propName, object value)
        {
            PropertyInfo p = t.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            p.SetValue(null, value);
        }

        [Fact]
        public void Defaults_Used_WhenNoConfig()
        {
            Config.Load();
            var specs = JunctionGuard.ResolveCoreSpecs();
            Assert.Equal(10, specs.Count);
            Assert.Equal("0.1.1-rc.2", specs["dsh-llm"]);
            Assert.Equal("4.0.1-rc.4", specs["cordis"]);
            Assert.Equal("3.18.1-rc.4", specs["schemastery"]);
        }

        [Fact]
        public void ConfigOverrides_Applied_PerPackage()
        {
            File.WriteAllText(Path.Combine(_fakeRuntime, "config.json"), @"
{ ""sharedCoreSpecs"": { ""dsh-llm"": ""0.2.0"", ""cordis"": ""4.1.0-rc.1"" } }");
            Config.Load();
            var specs = JunctionGuard.ResolveCoreSpecs();
            Assert.Equal("0.2.0", specs["dsh-llm"]);          // 覆盖生效
            Assert.Equal("4.1.0-rc.1", specs["cordis"]);      // 覆盖生效
            Assert.Equal("0.1.1-rc.2", specs["dsh-session"]); // 未覆盖的保持默认
        }

        [Fact]
        public void InvalidEntries_Ignored()
        {
            // "dsh-llm;calc"：key 含元字符 → 拒绝；"dsh-agent" 的版本含命令 → 拒绝；
            // "unknown-pkg"：合法字符但解析器不认识的包名 → 接受进配置但 Resolve 时无效果（分层：
            // Config 只做字符校验，语义过滤归解析器）。
            File.WriteAllText(Path.Combine(_fakeRuntime, "config.json"), @"
{ ""sharedCoreSpecs"": { ""dsh-llm;calc"": ""1.0"", ""unknown-pkg"": ""1.0"", ""dsh-agent"": ""1.0 & whoami"" } }");
            Config.Load();
            // 仅 "unknown-pkg"（合法字符）被接受；两个注入形态条目被字符校验拒绝
            Assert.Single(Config.Current.SharedCoreSpecs);
            Assert.True(Config.Current.SharedCoreSpecs.ContainsKey("unknown-pkg"));
            var specs = JunctionGuard.ResolveCoreSpecs();
            Assert.Equal("0.1.1-rc.2", specs["dsh-llm"]);
            Assert.Equal("0.1.1-rc.2", specs["dsh-agent"]);
            Assert.False(specs.ContainsKey("unknown-pkg"));                 // 未知包名不影响解析
        }
    }

    /// <summary>
    /// PluginRegistry.Load 过滤集成测试：写一个含恶意条目的 plugins.json 到
    /// 隔离 RuntimeDir，验证 Load 跳过恶意条目、保留合法条目、内置默认仍在。
    /// </summary>
    [Collection("PathMutating")]
    public class PluginRegistryFilterTests : IDisposable
    {
        private readonly string _fakeRuntime;

        public PluginRegistryFilterTests()
        {
            _fakeRuntime = Path.Combine(Path.GetTempPath(), "dsh-reg-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_fakeRuntime);
            SetInternalStatic(typeof(Paths), "RuntimeDir", _fakeRuntime);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_fakeRuntime)) Directory.Delete(_fakeRuntime, true); } catch { }
        }

        private static void SetInternalStatic(Type t, string propName, object value)
        {
            PropertyInfo p = t.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            p.SetValue(null, value);
        }

        [Fact]
        public void Load_SkipsMaliciousEntries_KeepsValidOnes()
        {
            File.WriteAllText(Path.Combine(_fakeRuntime, "plugins.json"), @"
{
  ""plugins"": [
    { ""id"": ""evil"", ""display"": ""evil"", ""pkgName"": ""dsh-x & calc"", ""viaNpm"": true,  ""source"": ""dsh-x & calc"", ""required"": true },
    { ""id"": ""evil-git"", ""display"": ""e"", ""pkgName"": ""evil-git"", ""viaNpm"": false, ""source"": ""a/b;rm/c"", ""required"": true },
    { ""id"": ""evil-path"", ""display"": ""e"", ""pkgName"": ""ok-pkg"", ""viaNpm"": false, ""source"": ""a/b/main"", ""required"": true, ""repoSub"": ""../escape"" },
    { ""id"": ""good"", ""display"": ""good"", ""pkgName"": ""good-pkg"", ""viaNpm"": true,  ""source"": ""good-pkg"", ""required"": false }
  ]
}");
            var reg = PluginRegistry.Load();

            Assert.Contains(reg.Plugins, p => p.Id == "good");
            Assert.DoesNotContain(reg.Plugins, p => p.Id == "evil");
            Assert.DoesNotContain(reg.Plugins, p => p.Id == "evil-git");
            Assert.DoesNotContain(reg.Plugins, p => p.Id == "evil-path");
            // 内置默认仍在（9 个 + good）
            Assert.Contains(reg.Plugins, p => p.Id == "dsh-yuyi");
            Assert.Equal(10, reg.Plugins.Count);
        }

        [Fact]
        public void Load_ValidEntry_CanOverrideBuiltIn()
        {
            File.WriteAllText(Path.Combine(_fakeRuntime, "plugins.json"), @"
{
  ""plugins"": [
    { ""id"": ""dsh-yuyi"", ""display"": ""yuyi"", ""pkgName"": ""dsh-yuyi"", ""viaNpm"": false, ""source"": ""someone/dsh-yuyi/master"", ""required"": true }
  ]
}");
            var reg = PluginRegistry.Load();
            PluginSpec yuyi = reg.Plugins.Find(p => p.Id == "dsh-yuyi");
            Assert.NotNull(yuyi);
            Assert.Equal("someone/dsh-yuyi/master", yuyi.Source);   // 覆盖生效
            Assert.Equal(9, reg.Plugins.Count);                      // 不追加重复项
        }
    }
}
