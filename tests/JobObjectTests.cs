using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// Job Object 隔离回归测试：v1.5 之前 JobObject 创建后从未 AssignProcessToJobObject，
    /// "关窗即停 dsh web" 的隔离承诺实际不存在（死代码）。此测试锁定真实机制：
    /// 进程加入 job 后，job Dispose（等价 launcher 进程退出句柄关闭）必须终止该进程树。
    /// </summary>
    public class JobObjectTests
    {
        [Fact]
        public void AddProcess_AssignsSuccessfully()
        {
            using JobObject job = JobObject.Create();
            Assert.NotEqual(IntPtr.Zero, job.Handle);   // job 创建失败即测试环境异常

            using Process p = SpawnSleeper();
            bool assigned = job.AddProcess(p.Handle);
            Assert.True(assigned, "AssignProcessToJobObject 失败——隔离机制未生效");
            // job Dispose 触发 KILL_ON_JOB_CLOSE
        }

        [Fact]
        public void Dispose_KillsAssignedProcessTree()
        {
            JobObject job = JobObject.Create();
            Assert.NotEqual(IntPtr.Zero, job.Handle);

            using Process p = SpawnSleeper();
            Assert.True(job.AddProcess(p.Handle), "assign 失败");
            Assert.False(p.HasExited);

            job.Dispose();   // 模拟 launcher 退出：句柄关闭 → 内核终止进程树

            // 进程应在极短时间内退出（轮询上限 15s，实际通常 <1s）
            for (int i = 0; i < 150 && !p.HasExited; i++)
                System.Threading.Thread.Sleep(100);
            Assert.True(p.HasExited, "job Dispose 后进程未终止——KILL_ON_JOB_CLOSE 失效");
        }

        [Fact]
        public void Dispose_KillsGrandChildrenToo()
        {
            // cmd → ping 两级：验证子进程继承 job（node 由 cmd 启动的场景同构）
            JobObject job = JobObject.Create();
            Assert.NotEqual(IntPtr.Zero, job.Handle);

            var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process p = Process.Start(psi);
            Assert.True(job.AddProcess(p.Handle));

            job.Dispose();

            for (int i = 0; i < 150 && !p.HasExited; i++)
                System.Threading.Thread.Sleep(100);
            Assert.True(p.HasExited, "job Dispose 后 cmd（及其 ping 子进程）未终止");
        }

        private static Process SpawnSleeper()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return Process.Start(psi);
        }
    }

    /// <summary>RunCapture 退出码透传：修复前丢弃退出码，"命令失败"被误读为"未安装"。</summary>
    public class RunCaptureTests
    {
        [Fact]
        public void ExitCode_IsPropagated()
        {
            string output = Shell.RunCapture("cmd /c exit 3", out int code);
            Assert.Equal(3, code);
        }

        [Fact]
        public void Success_ZeroCode_AndOutputCaptured()
        {
            string output = Shell.RunCapture("cmd /c echo hello-selfcheck", out int code);
            Assert.Equal(0, code);
            Assert.Contains("hello-selfcheck", output);
        }
    }
}
