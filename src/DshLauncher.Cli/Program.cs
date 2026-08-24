// DshLauncher.Cli — 控制台入口（M1：把行为完整迁到 Core 后，CLI 只是薄壳）
// 后续 M2 起，CLI 主要用于无人值守 / CI；GUI 取代为用户主入口。

using System;
using System.IO;
using DshLauncher;
using DshLauncher.Logging;

namespace DshLauncher.Cli
{
    internal static class Program
    {
        [STAThread]   // Job Object 需要 STA（虽然其实不需要，但 .NET 在 Windows 上传统）
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            try { Console.Title = AppMain.AppName; } catch { }

            // TLS 1.3 + 1.2
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; }
            catch { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }

            Paths.Init();

            var logger = new ConsoleLogger(logDir: Paths.LogsDir);
            var opts = LauncherHost.ParseArgs(args);
            return LauncherHost.Run(opts, logger);
        }
    }
}
