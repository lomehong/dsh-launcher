using System;
using System.IO;
using System.Reflection;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// Uninstaller 集成测试：所有测试在隔离的临时目录构造 fake runtime/profile，
    /// 跑 Uninstaller.Run，然后断言目录被删/未删。
    /// 不删用户的真实 %LOCALAPPDATA%\dsh-launcher 和 ~/.dsh。
    [Collection("PathMutating")]
    public class UninstallerIntegrationTests : IDisposable
    {
        private readonly string _fakeRuntime;
        private readonly string _fakeHome;

        public UninstallerIntegrationTests()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "dsh-uninst-" + Guid.NewGuid().ToString("N"));
            _fakeHome = baseDir;
            _fakeRuntime = Path.Combine(_fakeHome, "runtime");
            Directory.CreateDirectory(_fakeRuntime);
            Directory.CreateDirectory(Path.Combine(_fakeRuntime, "node"));
            File.WriteAllText(Path.Combine(_fakeRuntime, "marker.txt"), "fake");

            // 把 Paths 指向 fake runtime：用反射写 internal static 属性
            SetInternalStatic(typeof(Paths), "RuntimeDir", _fakeRuntime);
            // DshHome 由 DSH_HOME env 决定
            Environment.SetEnvironmentVariable("DSH_HOME", _fakeHome);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DSH_HOME", null);
            // 测试后清理 fake 目录（Uninstaller 应该已经删了；万一没删就手动）
            try { if (Directory.Exists(_fakeRuntime)) Directory.Delete(_fakeRuntime, true); } catch { }
            try { if (Directory.Exists(_fakeHome)) Directory.Delete(_fakeHome, true); } catch { }
        }

        /// <summary>设置 internal static 属性（Paths 的 RuntimeDir 等）</summary>
        private static void SetInternalStatic(Type t, string propName, object value)
        {
            PropertyInfo p = t.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            p.SetValue(null, value);
        }

        [Fact]
        public void YesFlag_DeletesRuntime()
        {
            // 假设我们的 fake runtime 存在
            Assert.True(Directory.Exists(_fakeRuntime));

            int code = Uninstaller.Run(purge: false, assumeYes: true);

            Assert.Equal(ExitCodes.Success, code);
            Assert.False(Directory.Exists(_fakeRuntime));  // runtime 已删
            Assert.True(Directory.Exists(_fakeHome));        // ~/.dsh 保留
        }

        [Fact]
        public void Purge_DeletesBothRuntimeAndProfile()
        {
            // 模拟 ~/.dsh/profiles/web/package.json 真实结构
            string dshProfiles = Path.Combine(_fakeHome, "profiles", "web");
            Directory.CreateDirectory(dshProfiles);
            File.WriteAllText(Path.Combine(dshProfiles, "package.json"), "{}");

            int code = Uninstaller.Run(purge: true, assumeYes: true);

            Assert.Equal(ExitCodes.Success, code);
            Assert.False(Directory.Exists(_fakeRuntime));
            Assert.False(Directory.Exists(_fakeHome));
        }

        [Fact]
        public void NoFlags_RequiresInteractiveYes()
        {
            // assumeYes=false + stdin 无响应 = 应该走"已取消"分支
            // 但在测试里 Console.ReadLine() 会因无 stdin 抛异常，被吞，返回 null
            // → Uninstaller 应当识别为取消 → 退出码 0 且**不删任何东西**
            //
            // 注意：这个测试依赖 Console.ReadLine 在非交互终端抛异常的语义。
            // 在 xUnit + .NET Framework 下 ReadLine 通常抛 IOException，吞掉返回 null。
            Assert.True(Directory.Exists(_fakeRuntime));

            int code = Uninstaller.Run(purge: false, assumeYes: false);

            Assert.Equal(ExitCodes.Success, code);
            Assert.True(Directory.Exists(_fakeRuntime));
        }

        [Fact]
        public void MissingRuntime_NoOp()
        {
            // 先删 runtime，假装已经卸载过
            Directory.Delete(_fakeRuntime, true);
            Assert.False(Directory.Exists(_fakeRuntime));

            int code = Uninstaller.Run(purge: false, assumeYes: true);

            Assert.Equal(ExitCodes.Success, code);  // 不报错
        }
    }
}
