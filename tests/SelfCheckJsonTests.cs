using System;
using System.IO;
using System.Reflection;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// --check --json 语义回归测试：v1.6.0 之前 healthy = 环境完整 && 端口空闲，
    /// "装好了且 dsh web 正在运行"（最健康态）会被误报为不健康（退出码 1）。
    /// 修复后 healthy 只描述环境完整性，服务在跑与否由 webRunning 单独表达。
    /// </summary>
    [Collection("PathMutating")]
    public class SelfCheckJsonTests : IDisposable
    {
        private readonly string _fakeRuntime;
        private readonly string _fakeNodeDir;
        private readonly string _fakeHome;
        private readonly TextWriter _oldOut;

        public SelfCheckJsonTests()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "dsh-selfcheck-" + Guid.NewGuid().ToString("N"));
            _fakeRuntime = Path.Combine(baseDir, "runtime");
            _fakeNodeDir = Path.Combine(_fakeRuntime, "node", "node-v24.0.0-win-x64");
            _fakeHome = Path.Combine(baseDir, "home");
            Directory.CreateDirectory(_fakeNodeDir);
            Directory.CreateDirectory(_fakeHome);

            // 伪造一个完全健康的环境：
            // 1) current-node.txt 指向含 node.exe 的目录（node.exe 用 cmd.exe 顶替——只验证存在性）
            Directory.CreateDirectory(_fakeNodeDir);
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Path.Combine(_fakeNodeDir, "node.exe"), true);
            File.WriteAllText(Path.Combine(_fakeRuntime, "current-node.txt"), _fakeNodeDir);
            // 2) dsh.cmd 假装已安装，--version 输出版本号且退出码 0
            File.WriteAllText(Path.Combine(_fakeNodeDir, "dsh.cmd"), "@echo off\r\necho 1.0.0\r\n");
            // 3) profile 里 9 个内置插件全部就位
            string web = Path.Combine(_fakeHome, "profiles", "web");
            Directory.CreateDirectory(web);
            var deps = new System.Collections.Generic.Dictionary<string, object>();
            var bundles = new System.Collections.Generic.List<object>();
            foreach (PluginSpec spec in PluginRegistry.BuiltIn)
            {
                deps[spec.PkgName] = "^1.0.0";
                bundles.Add(spec.PkgName);
            }
            var profile = new System.Collections.Generic.Dictionary<string, object>
            {
                ["name"] = "dsh-profile-web",
                ["dependencies"] = deps,
                ["dsh"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["profile"] = new System.Collections.Generic.Dictionary<string, object> { ["bundles"] = bundles }
                },
            };
            File.WriteAllText(Path.Combine(web, "package.json"), JsonMini.Stringify(profile));

            SetInternalStatic(typeof(Paths), "RuntimeDir", _fakeRuntime);
            Environment.SetEnvironmentVariable("DSH_HOME", _fakeHome);
            _oldOut = Console.Out;
        }

        public void Dispose()
        {
            Console.SetOut(_oldOut);
            Environment.SetEnvironmentVariable("DSH_HOME", null);
            try { Directory.Delete(_fakeRuntime, true); } catch { }
            try { Directory.Delete(_fakeHome, true); } catch { }
        }

        private static void SetInternalStatic(Type t, string propName, object value)
        {
            PropertyInfo p = t.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            p.SetValue(null, value);
        }

        [Fact]
        public void RunJson_HealthyEnvironment_ReportsHealthy_EvenWhenPortBusy()
        {
            string json;
            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                int code = SelfCheck.Run(jsonOut: true, log: new DshLauncher.Logging.NullLogger());
                json = sw.ToString();
                // 环境完整 → 退出码 0，无论 3080 是否被占用（旧代码在端口占用时会错误返回 1）
                Assert.Equal(0, code);
            }

            JsonObject root = JsonMini.Parse(json);
            Assert.True((bool)root.Map["healthy"], "环境齐全时 healthy 必须为 true（与端口占用无关）");
            Assert.Equal(9L, (long)root.Map["pluginsTotalCount"]);
            Assert.Equal(9L, (long)root.Map["pluginsOkCount"]);
            // webRunning 字段存在且与 portInUse 一致
            Assert.True(root.Map.ContainsKey("webRunning"));
            Assert.Equal((bool)root.Map["portInUse"], (bool)root.Map["webRunning"]);
            // node 状态 ok（marker + node.exe 存在）
            var node = root.Map["node"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.NotNull(node);
            Assert.Equal("ok", node["status"]);
        }

        [Fact]
        public void RunJson_MissingNode_ReportsUnhealthy()
        {
            File.Delete(Path.Combine(_fakeRuntime, "current-node.txt"));
            string json;
            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                int code = SelfCheck.Run(jsonOut: true, log: new DshLauncher.Logging.NullLogger());
                json = sw.ToString();
                Assert.Equal(1, code);
            }
            JsonObject root = JsonMini.Parse(json);
            Assert.False((bool)root.Map["healthy"]);
        }
    }
}
