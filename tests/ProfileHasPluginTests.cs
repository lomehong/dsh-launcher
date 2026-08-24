using System;
using System.IO;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// ProfileHasPlugin 回归测试：dsh plugin add CLI 写入的 bundles 在嵌套路径
    /// dsh.profile.bundles 下。v1.4.0 重构后此判定若退化（只查顶层 bundles），
    /// 会导致所有插件都被误报"安装失败"，但 dsh web 实际启动正常——极隐蔽。
    /// </summary>
    [Collection("PathMutating")]
    public class ProfileHasPluginTests : IDisposable
    {
        private readonly string _tmpProfile;

        public ProfileHasPluginTests()
        {
            _tmpProfile = Path.Combine(Path.GetTempPath(), "dsh-profile-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpProfile);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tmpProfile, true); } catch { }
        }


        private void WriteProfile(string json)
        {
            // ProfileHasPlugin 通过 Paths.ProfileDir() 读 package.json；
            // ProfileDir = DshHome() + "profiles/web"。
            string profileWeb = Path.Combine(_tmpProfile, "profiles", "web");
            Directory.CreateDirectory(profileWeb);
            File.WriteAllText(Path.Combine(profileWeb, "package.json"), json, new System.Text.UTF8Encoding(false));
        }
        // 解决方法：直接在测试里 patch 把 DSH_HOME 指向我们创建的目录。
        private void PointProfileHere()
        {
            Environment.SetEnvironmentVariable("DSH_HOME", _tmpProfile);
            Paths.Init();
        }

        [Fact]
        public void NestedBundles_Recognized()
        {
            PointProfileHere();
            WriteProfile(@"{
  ""name"": ""dsh-profile-web"",
  ""dependencies"": {
    ""dsh-at-file"": ""link:C:/tmp/x"",
    ""dsh-better-sidebar"": ""^0.15.0""
  },
  ""dsh"": { ""profile"": { ""bundles"": [""dsh-at-file"", ""dsh-better-sidebar""] } }
}");
            Assert.True(PluginManager.ProfileHasPlugin("dsh-at-file"));
            Assert.True(PluginManager.ProfileHasPlugin("dsh-better-sidebar"));
        }

        [Fact]
        public void MissingFromBundles_NotRecognized()
        {
            PointProfileHere();
            WriteProfile(@"{
  ""dependencies"": { ""dsh-at-file"": ""link:C:/tmp/x"" },
  ""dsh"": { ""profile"": { ""bundles"": [""dsh-better-sidebar""] } }
}");
            Assert.False(PluginManager.ProfileHasPlugin("dsh-at-file"));
        }

        [Fact]
        public void TopLevelBundles_StillWorks()
        {
            // 向后兼容：旧自定义模板把 bundles 写到顶层
            PointProfileHere();
            WriteProfile(@"{
  ""dependencies"": { ""dsh-at-file"": ""^1.0.0"" },
  ""bundles"": [""dsh-at-file""]
}");
            Assert.True(PluginManager.ProfileHasPlugin("dsh-at-file"));
        }

        [Fact]
        public void MissingDeps_NotRecognized()
        {
            PointProfileHere();
            WriteProfile(@"{
  ""dsh"": { ""profile"": { ""bundles"": [""dsh-at-file""] } }
}");
            Assert.False(PluginManager.ProfileHasPlugin("dsh-at-file"));
        }

        [Fact]
        public void NoProfileFile_ReturnsFalse()
        {
            // 不写 package.json
            PointProfileHere();
            Assert.False(PluginManager.ProfileHasPlugin("anything"));
        }

        [Fact]
        public void EmptyBundles_NotRecognized()
        {
            PointProfileHere();
            WriteProfile(@"{
  ""dependencies"": { ""dsh-at-file"": ""^1.0.0"" },
  ""dsh"": { ""profile"": { ""bundles"": [] } }
}");
            Assert.False(PluginManager.ProfileHasPlugin("dsh-at-file"));
        }

        [Fact]
        public void BothNestedAndTopLevel_UnionRecognized()
        {
            PointProfileHere();
            WriteProfile(@"{
  ""dependencies"": { ""a"": ""^1"", ""b"": ""^1"", ""c"": ""^1"" },
  ""bundles"": [""a""],
  ""dsh"": { ""profile"": { ""bundles"": [""b"", ""c""] } }
}");
            Assert.True(PluginManager.ProfileHasPlugin("a"));
            Assert.True(PluginManager.ProfileHasPlugin("b"));
            Assert.True(PluginManager.ProfileHasPlugin("c"));
            Assert.False(PluginManager.ProfileHasPlugin("d"));
        }
    }
}
