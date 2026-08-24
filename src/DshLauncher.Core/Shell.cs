// DshLauncher.Core — Shell helpers
namespace DshLauncher
{
    using System.Diagnostics;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;

    public static class Shell
    {
        /// <summary>
        /// GUI 模式注入：非 null 时所有子进程改为 CreateNoWindow + 重定向 stdout/stderr，
        /// 逐行转发到该回调（GUI 日志流）。CLI 模式保持 null（继承控制台，行为不变）。
        /// 这消灭了 GUI 下 npm/pnpm/dsh 弹出的黑色 cmd 窗口。
        /// </summary>
        public static Action<string> OutputSink { get; set; }

        public static int RunCmd(string commandLine, JobObject job = null)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + commandLine + "\"")
                {
                    UseShellExecute = false,
                };
                if (OutputSink != null)
                {
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }
                else
                {
                    psi.CreateNoWindow = false;
                }
                using (Process p = Process.Start(psi))
                {
                    // Job Object 隔离：进程一旦启动立即入 job（KILL_ON_JOB_CLOSE）。
                    // 子进程（node 等）自动继承 job，launcher 退出即整树终止。
                    if (p != null && job != null && job.Handle != IntPtr.Zero)
                    {
                        try
                        {
                            if (!job.AddProcess(p.Handle))
                                Console.Error.WriteLine("      [提示] 进程未能加入 Job Object（隔离降级为控制台信号）。");
                        }
                        catch (Exception jex)
                        {
                            Console.Error.WriteLine("      [提示] Job Object 加入失败: " + jex.Message);
                        }
                    }
                    if (OutputSink != null)
                    {
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) OutputSink(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (e.Data != null) OutputSink(e.Data); };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 无法执行命令: " + commandLine + "  (" + ex.Message + ")");
                return -1;
            }
        }

        public static int RunCmdIn(string commandLine, string cwd)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + commandLine + "\"")
                {
                    UseShellExecute = false,
                    WorkingDirectory = string.IsNullOrEmpty(cwd) ? null : cwd,
                };
                if (OutputSink != null)
                {
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }
                else
                {
                    psi.CreateNoWindow = false;
                }
                using (Process p = Process.Start(psi))
                {
                    if (OutputSink != null)
                    {
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) OutputSink(e.Data); };
                        p.ErrorDataReceived += (s, e) => { if (e.Data != null) OutputSink(e.Data); };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] 无法执行命令: " + commandLine + "  (" + ex.Message + ")");
                return -1;
            }
        }

        public static string RunCapture(string commandLine)
        {
            return RunCapture(commandLine, out _);
        }

        /// <summary>
        /// 执行命令并捕获输出（临时文件重定向）。exitCode 区分"命令失败"与"输出为空"，
        /// 版本探测等调用方据此不再把失败误判为"未安装"。
        /// </summary>
        public static string RunCapture(string commandLine, out int exitCode)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dsh-cap-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                exitCode = RunCmd(commandLine + " > \"" + tmp + "\" 2>&1");
                return File.Exists(tmp) ? File.ReadAllText(tmp, Encoding.UTF8) : "";
            }
            catch
            {
                exitCode = -1;
                return "";
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        /// <summary>
        /// 下载文件到 dest。支持断点续传：失败时保留 .part，下次从已下载字节数处
        /// 以 Range 请求续传（服务器不支持 Range 则自动整档重下）。
        /// 头部超时 30s、整体看门狗 10 分钟，杜绝 WebClient 时代的无限等待。
        /// </summary>
        public static bool DownloadFile(string url, string dest)
        {
            string part = dest + ".part";
            long offset = 0;
            try { if (File.Exists(part)) offset = new FileInfo(part).Length; } catch { offset = 0; }

            try
            {
                using (var watchdog = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);

                    // 头部超时 30s（连接挂起快速失败），整体看门狗 10 分钟（大文件慢速链路兜底）
                    using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token))
                    using (HttpResponseMessage resp = SendWithHeaderTimeout(req, headerCts))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            Console.Error.WriteLine("[!] 下载失败: HTTP " + (int)resp.StatusCode + "  " + url);
                            // 4xx/5xx 时清理无用 .part，避免坏半截永远留着
                            try { if (offset > 0 && File.Exists(part)) File.Delete(part); } catch { }
                            return false;
                        }

                        bool resumed = resp.StatusCode == HttpStatusCode.PartialContent && offset > 0;
                        if (!resumed) offset = 0;   // 服务器忽略 Range → 整档重下

                        long total = offset + (resp.Content.Headers.ContentLength ?? -1);
                        using (Stream src = resp.Content.ReadAsStreamAsync(watchdog.Token).GetAwaiter().GetResult())
                        using (var fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buf = new byte[81920];
                            long done = offset;
                            int lastPct = -1;
                            while (true)
                            {
                                int read = src.ReadAsync(buf, 0, buf.Length, watchdog.Token).GetAwaiter().GetResult();
                                if (read <= 0) break;
                                fs.Write(buf, 0, read);
                                done += read;
                                if (total > 0)
                                {
                                    int pct = (int)(done * 100 / total);
                                    if (pct != lastPct)
                                    {
                                        lastPct = pct;
                                        Console.Write("\r      下载中 {0,3}%  ({1:N1} / {2:N1} MB)   ",
                                            pct, done / 1048576.0, total / 1048576.0);
                                    }
                                }
                            }
                            fs.Flush();
                        }
                        Console.WriteLine();
                    }
                }
                try { File.Move(part, dest, true); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[!] 保存文件失败: " + ex.Message);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.Error.WriteLine("[!] 下载失败: " + ex.Message
                    + (offset > 0 && File.Exists(part) && new FileInfo(part).Length > 0
                        ? "（已下载部分已保留，下次续传）" : ""));
                return false;
            }
        }

        /// <summary>发起 GET：30 秒头部超时 + 看门狗联动，ResponseHeadersRead 流式取体。</summary>
        private static HttpResponseMessage SendWithHeaderTimeout(HttpRequestMessage req, CancellationTokenSource linkedCts)
        {
            linkedCts.CancelAfter(TimeSpan.FromSeconds(30));
            return Http.Client.Send(req, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
        }

        public static bool PortInUse(int port)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    var r = c.BeginConnect("127.0.0.1", port, null, null);
                    if (!r.AsyncWaitHandle.WaitOne(800)) return false;
                    c.EndConnect(r);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 找出监听指定端口的所有本地进程 PID（解析 netstat -ano 的 LISTENING 行）。
        /// </summary>
        public static System.Collections.Generic.List<int> GetPortOwnerPids(int port)
        {
            var pids = new System.Collections.Generic.List<int>();
            var seen = new System.Collections.Generic.HashSet<int>();
            try
            {
                string output = RunCapture("netstat -ano -p tcp");
                foreach (string raw in output.Split('\n'))
                {
                    string line = raw.Trim();
                    // TCP    0.0.0.0:3080    0.0.0.0:0    LISTENING    12345
                    if (!line.StartsWith("TCP")) continue;
                    var parts = System.Text.RegularExpressions.Regex.Split(line, @"\s+");
                    if (parts.Length < 5) continue;
                    if (!parts[1].EndsWith(":" + port)) continue;
                    if (parts[3] != "LISTENING") continue;
                    if (int.TryParse(parts[4], out int pid) && pid > 0 && seen.Add(pid))
                    {
                        pids.Add(pid);
                    }
                }
            }
            catch { }
            return pids;
        }
        public static void OpenBrowser(string url)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
                {
                    if (p != null)
                    {
                        try { if (!p.WaitForInputIdle(2000)) Console.Error.WriteLine("[提示] 浏览器进程未在 2 秒内进入空闲，请检查系统默认浏览器设置。"); }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[提示] 无法自动打开浏览器，请手动访问 " + url + " (" + ex.Message + ")");
                Console.WriteLine("       请在系统设置中确认已配置默认浏览器。");
            }
        }

        public static void Pause()
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine("按任意键退出 ...");
                Console.ReadKey(true);
            }
            catch { }
        }

        public static string StripNpmWarns(string text)
        {
            if (text == null) return "";
            var sb = new StringBuilder();
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (t.StartsWith("npm warn", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        public static string GetVersionNumber(string text)
        {
            if (text == null) return "";
            Match m = Regex.Match(text, "[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.]+)?");
            return m.Success ? m.Value : "";
        }

        public static int CompareVersions(string a, string b)
        {
            if (a == b) return 0;
            string[] pa = a.Split('-');
            string[] pb = b.Split('-');
            string[] na = pa[0].Split('.');
            string[] nb = pb[0].Split('.');
            for (int i = 0; i < Math.Max(na.Length, nb.Length); i++)
            {
                int ai = i < na.Length ? SafeInt(na[i]) : 0;
                int bi = i < nb.Length ? SafeInt(nb[i]) : 0;
                if (ai != bi) return ai < bi ? -1 : 1;
            }
            bool aPre = pa.Length > 1;
            bool bPre = pb.Length > 1;
            if (aPre && !bPre) return -1;
            if (!aPre && bPre) return 1;
            if (aPre && bPre) return string.Compare(pa[1], pb[1], StringComparison.OrdinalIgnoreCase);
            return 0;
        }

        private static int SafeInt(string s) { int v; int.TryParse(s, out v); return v; }

        public static string ComputeSha256Hex(string filePath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", b);
                return sb.ToString();
            }
        }
    }
}
