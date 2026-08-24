using System;
using System.IO;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>monorepo 子包定位（FindPackageRecursive + repoSub）。</summary>
    public class FindPackageRecursiveTests : IDisposable
    {
        private readonly string _tmp;
        public FindPackageRecursiveTests()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "dsh-monorepo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
            // 模拟 repo 结构：_tmp/<repo>/im-channel/package.json + _tmp/<repo>/ui/package.json
            string repo = Path.Combine(_tmp, "dsh-im-bot-main");
            string im = Path.Combine(repo, "im-channel");
            string ui = Path.Combine(repo, "ui-settings-im");
            Directory.CreateDirectory(im); Directory.CreateDirectory(ui);
            File.WriteAllText(Path.Combine(im, "package.json"), "{\"name\":\"@dsh-extra/im-channel\"}");
            File.WriteAllText(Path.Combine(ui, "package.json"), "{\"name\":\"@dsh-extra/dsh-client-ui-settings-im\"}");
            File.WriteAllText(Path.Combine(im, "__dummy__"), "x"); // 确保存在
        }
        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        [Fact]
        public void RepoSub_PointsToSubpackage()
        {
            var spec = new PluginSpec("dsh-im-bot", "dsh-im-bot", "@dsh-extra/im-channel", false, "lomehong/dsh-im-bot/main");
            spec.RepoSub = "im-channel";
            string found = PluginManager.FindPackageRecursive(_tmp, spec);
            Assert.NotNull(found);
            Assert.EndsWith("im-channel", found.Replace('\\', '/'));
        }

        [Fact]
        public void PkgName_MatchesSubpackage()
        {
            var spec = new PluginSpec("x", "x", "@dsh-extra/im-channel", false, "lomehong/dsh-im-bot/main");
            string found = PluginManager.FindPackageRecursive(_tmp, spec);
            Assert.NotNull(found);
            Assert.EndsWith("im-channel", found.Replace('\\', '/'));
        }

        [Fact]
        public void PkgName_Mismatch_FallsBackToFirst()
        {
            var spec = new PluginSpec("y", "y", "nonexistent", false, "lomehong/dsh-im-bot/main");
            // 无 repoSub、pkgName 不匹配 → 回退第一个含 tsconfig/lib 的子包
            File.WriteAllText(Path.Combine(_tmp, "dsh-im-bot-main", "im-channel", "tsconfig.json"), "{}");
            string found = PluginManager.FindPackageRecursive(_tmp, spec);
            Assert.NotNull(found);
        }
    }
}

    /// <summary>共享 harness 核心包检测（EnsureSharedCorePackages 的 need 判定）。</summary>
    public class SharedCorePackagesTests
    {
        [Fact]
        public void DetectEmptyCore_FlagsNeedRepair()
        {
            // 模拟共享集：核心包为空壳目录（无 lib/index.js）
            string tmp = Path.Combine(Path.GetTempPath(), "dsh-shared-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, "cordis", "lib"));
                Directory.CreateDirectory(Path.Combine(tmp, "dsh-llm", "lib"));
                bool need = false;
                foreach (string p in new[] { "cordis", "dsh-llm", "dsh-session" })
                {
                    string entry = Path.Combine(tmp, p, "lib", p == "schemastery" ? "index.cjs" : "index.js");
                    if (!File.Exists(entry)) { need = true; break; }
                }
                Assert.True(need, "空壳目录应被判定为需修复");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
