using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    public class PluginInputParserTests
    {
        // ===== GitHub URL 形式 =====

        [Fact]
        public void HttpsUrl_WithBranch()
        {
            var r = PluginInputParser.Parse("https://github.com/owner/repo/tree/dev");
            Assert.True(r.Success);
            Assert.Equal(PluginInputParser.Kind.GitHub, r.Kind);
            Assert.Equal("repo", r.Id);
            Assert.Equal("owner/repo/dev", r.Source);
        }

        [Fact]
        public void HttpsUrl_DefaultBranch()
        {
            var r = PluginInputParser.Parse("https://github.com/owner/repo");
            Assert.True(r.Success);
            Assert.Equal("owner/repo/main", r.Source);
        }

        [Fact]
        public void HttpsUrl_WithGitExtension()
        {
            var r = PluginInputParser.Parse("https://github.com/owner/repo.git");
            Assert.True(r.Success);
            Assert.Equal("owner/repo/main", r.Source);
        }

        [Fact]
        public void HttpUrl_AlsoAccepted()
        {
            var r = PluginInputParser.Parse("http://github.com/owner/repo");
            Assert.True(r.Success);
        }

        // ===== SSH git@ 形式 =====

        [Fact]
        public void SshGitUrl()
        {
            var r = PluginInputParser.Parse("git@github.com:owner/repo.git");
            Assert.True(r.Success);
            Assert.Equal(PluginInputParser.Kind.GitHub, r.Kind);
            Assert.Equal("owner/repo/main", r.Source);
        }

        // ===== 简写 owner/repo =====

        [Fact]
        public void ShortOwnerRepo_DefaultBranch()
        {
            var r = PluginInputParser.Parse("owner/repo");
            Assert.True(r.Success);
            Assert.Equal(PluginInputParser.Kind.GitHub, r.Kind);
            Assert.Equal("repo", r.Id);
            Assert.Equal("owner/repo/main", r.Source);
        }

        [Fact]
        public void ShortOwnerRepo_WithBranch()
        {
            var r = PluginInputParser.Parse("owner/repo/feat-x");
            Assert.True(r.Success);
            Assert.Equal("owner/repo/feat-x", r.Source);
        }

        // ===== npm 包名 =====

        [Fact]
        public void NpmPackage_Plain()
        {
            var r = PluginInputParser.Parse("my-cool-package");
            Assert.True(r.Success);
            Assert.Equal(PluginInputParser.Kind.Npm, r.Kind);
            Assert.Equal("my-cool-package", r.Id);
            Assert.Equal("my-cool-package", r.Source);
        }

        [Fact]
        public void NpmPackage_Scoped()
        {
            var r = PluginInputParser.Parse("@scope/pkg-name");
            Assert.True(r.Success);
            Assert.Equal(PluginInputParser.Kind.Npm, r.Kind);
            // @ 被剥离；/ 替换为 -，确保 ID 是合法 slug
            Assert.Equal("scope-pkg-name", r.Id);
            Assert.Equal("@scope/pkg-name", r.Display);
            Assert.Equal("@scope/pkg-name", r.Source);
        }

        // ===== 错误情形 =====

        [Fact]
        public void Empty_ReturnsError()
        {
            var r = PluginInputParser.Parse("");
            Assert.False(r.Success);
            Assert.Contains("为空", r.Error);
        }

        [Fact]
        public void Whitespace_ReturnsError()
        {
            var r = PluginInputParser.Parse("   ");
            Assert.False(r.Success);
        }

        [Fact]
        public void Garbage_ReturnsError()
        {
            var r = PluginInputParser.Parse("not a url or pkg name at all !!!");
            Assert.False(r.Success);
            Assert.Contains("无法识别", r.Error);
        }

        [Fact]
        public void TooManySlashes_NotGithub()
        {
            // a/b/c/d 应该解析为 owner/repo/branch/d（被四段拦截）
            // 实际上 regex 允许 3 段；4 段应被拒绝
            var r = PluginInputParser.Parse("a/b/c/d");
            Assert.False(r.Success);
        }
    }
}
