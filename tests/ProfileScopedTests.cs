using System;
using System.IO;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>ProfileHasPlugin 对 scoped 包名（dsh plugin add 写 @scope/name 进 profile）的匹配。</summary>
    [Collection("PathMutating")]
    public class ProfileScopedTests : IDisposable
    {
        private readonly string _tmpHome;
        public ProfileScopedTests()
        {
            _tmpHome = Path.Combine(Path.GetTempPath(), "dsh-scope-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("DSH_HOME", _tmpHome);
            Paths.Init();
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DSH_HOME", null);
            try { Directory.Delete(_tmpHome, true); } catch { }
        }

        [Fact]
        public void ScopedDepsAndBundles_MatchedByPlainName()
        {
            string web = Path.Combine(_tmpHome, "profiles", "web");
            Directory.CreateDirectory(web);
            File.WriteAllText(Path.Combine(web, "package.json"), @"{
  ""dependencies"": { ""@michengai/dsh-skills-manager"": ""link:C:/x/dsh-skills-manager"" },
  ""dsh"": { ""profile"": { ""bundles"": [""@michengai/dsh-skills-manager""] } }
}");
            Assert.True(PluginManager.ProfileHasPlugin("dsh-skills-manager"));
        }

        [Fact]
        public void PlainDeps_StillMatched()
        {
            string web = Path.Combine(_tmpHome, "profiles", "web");
            Directory.CreateDirectory(web);
            File.WriteAllText(Path.Combine(web, "package.json"), @"{
  ""dependencies"": { ""dsh-at-file"": ""link:C:/x/dsh-at-file"" },
  ""dsh"": { ""profile"": { ""bundles"": [""dsh-at-file""] } }
}");
            Assert.True(PluginManager.ProfileHasPlugin("dsh-at-file"));
        }

        [Fact]
        public void Missing_StillFalse()
        {
            string web = Path.Combine(_tmpHome, "profiles", "web");
            Directory.CreateDirectory(web);
            File.WriteAllText(Path.Combine(web, "package.json"), @"{
  ""dependencies"": { ""@a/x"": ""^1.0.0"" },
  ""dsh"": { ""profile"": { ""bundles"": [""@a/x""] } }
}");
            Assert.False(PluginManager.ProfileHasPlugin("unrelated"));
        }
    }
}
