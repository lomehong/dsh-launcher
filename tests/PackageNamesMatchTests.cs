using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    public class PackageNamesMatchTests
    {
        [Theory]
        [InlineData("@michengai/dsh-skills-manager", "dsh-skills-manager", true)]  // scoped vs plain
        [InlineData("@michengai/dsh-skills-manager", "@michengai/dsh-skills-manager", true)] // exact scoped
        [InlineData("dsh-at-file", "dsh-at-file", true)] // exact
        [InlineData("dsh-genui", "dsh-genui", true)]
        [InlineData("@scope/a", "a", true)] // scope stripped
        [InlineData("@scope/a", "b", false)] // different tail
        [InlineData("a", "@other/a", true)] // tail match regardless of scope
        [InlineData("", "a", false)] // empty
        [InlineData("a", "", false)]
        [InlineData(null, "a", false)]
        public void PackageNamesMatch_Cases(string actual, string expected, bool should)
        {
            Assert.Equal(should, PluginManager.PackageNamesMatch(actual, expected));
        }
    }
}
