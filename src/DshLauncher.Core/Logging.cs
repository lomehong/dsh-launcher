// DshLauncher.Core — 日志抽象（替换旧 AppLogger）
// GUI Host 注入自己的 ILogger；CLI Host 用 ConsoleLogger。
namespace DshLauncher.Logging
{
    public enum LogLevel { Silent, Info, Warn, Error, Verbose }

    public sealed class LogEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public string CallerMember { get; init; } = "";
        public string CallerFile { get; init; } = "";

        public string Format() =>
            $"[{Timestamp:HH:mm:ss}] [{Level,-5}] {Message}";
    }

    public interface ILogger
    {
        LogLevel Level { get; set; }
        void Log(LogLevel level, string message,
                 [System.Runtime.CompilerServices.CallerMemberName] string member = "",
                 [System.Runtime.CompilerServices.CallerFilePath] string file = "");
        event Action<LogEvent> Event;
    }

    public sealed class NullLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Silent;
        public void Log(LogLevel level, string message, string member = "", string file = "") { }
        public event Action<LogEvent> Event { add { } remove { } }
    }

    /// <summary>
    /// 默认日志：stdout（带 ANSI 颜色）+ 文件（7 天滚动，UTF-8 无 BOM）。
    /// 保留 v1.4 行为：去掉 [!]/[提示]/[OK]/[修正] 冗余标记。
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;
        public event Action<LogEvent> Event;
        public string LogDir { get; set; }

        private readonly object _fileLock = new();
        private string _currentFile;

        public ConsoleLogger(string logDir = null)
        {
            LogDir = logDir;
        }

        public void Log(LogLevel level, string message, string member = "", string file = "")
        {
            if (Level == LogLevel.Silent && level != LogLevel.Error) return;
            string stripped = StripMarker(message);
            var ev = new LogEvent { Level = level, Message = stripped, CallerMember = member, CallerFile = file };
            string line = ev.Format();
            if (!_consoleRedirected)
                Console.WriteLine(line);
            WriteToFile(line);
            Event?.Invoke(ev);
        }

        private static string StripMarker(string msg)
        {
            if (msg == null) return "";
            string s = msg;
            if (s.StartsWith("[!] ") || s.StartsWith("[!]")) s = s.Substring(3).TrimStart();
            else if (s.StartsWith("[提示] ")) s = s.Substring(4).TrimStart();
            else if (s.StartsWith("[OK] ")) s = s.Substring(4).TrimStart();
            else if (s.StartsWith("[修正] ")) s = s.Substring(4).TrimStart();
            return s;
        }

        private void WriteToFile(string line)
        {
            if (LogDir == null) return;
            try
            {
                lock (_fileLock)
                {
                    string today = DateTime.Today.ToString("yyyy-MM-dd");
                    string file = Path.Combine(LogDir, "launcher-" + today + ".log");
                    if (file != _currentFile)
                    {
                        _currentFile = file;
                        CleanupOldLogs();
                    }
                    File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private volatile bool _consoleRedirected;

        /// <summary>
        /// GUI 模式：把 Console.Out 重定向进日志事件流。
        /// Core 的大量进度输出走 Console.WriteLine（CLI 遗产）；GUI 没有 Console，
        /// 不重定向的话这些行全部丢失。调用后：
        /// - 所有 Console 输出以 Verbose 级别走 Event（写文件 + 上 GUI 面板）
        /// - Log() 自身不再写 Console（Event 已覆盖），避免双重记录
        /// </summary>
        public void RedirectConsoleOutput()
        {
            _consoleRedirected = true;
            var self = this;
            Console.SetOut(new RedirectTextWriter(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                var ev = new LogEvent { Level = LogLevel.Verbose, Message = line.TrimEnd() };
                WriteToFile(ev.Format());
                Event?.Invoke(ev);
            }));
        }

        private sealed class RedirectTextWriter : System.IO.TextWriter
        {
            private readonly Action<string> _onLine;
            private readonly StringBuilder _buf = new();
            public RedirectTextWriter(Action<string> onLine) { _onLine = onLine; }
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value)
            {
                if (value == '\n') { FlushLine(); }
                else if (value != '\r') _buf.Append(value);
            }
            public override void Write(string value)
            {
                if (value == null) return;
                foreach (char c in value) Write(c);
            }
            public override void WriteLine(string value) { Write(value); FlushLine(); }
            public override void WriteLine() { FlushLine(); }
            private void FlushLine()
            {
                if (_buf.Length == 0) return;
                var s = _buf.ToString();
                _buf.Clear();
                _onLine(s);
            }
        }

        private void CleanupOldLogs()
        {
            try
            {
                DateTime cutoff = DateTime.Today.AddDays(-7);
                foreach (string f in Directory.GetFiles(LogDir, "launcher-*.log"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    string datePart = name.Substring("launcher-".Length);
                    if (DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime d) && d < cutoff)
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>Info/Warn/Error/Debug 便捷扩展。</summary>
    public static class LoggerExtensions
    {
        public static void Info(this ILogger l, string msg)  => l.Log(LogLevel.Info, msg);
        public static void Warn(this ILogger l, string msg)  => l.Log(LogLevel.Warn, msg);
        public static void Error(this ILogger l, string msg) => l.Log(LogLevel.Error, msg);
        public static void Verbose(this ILogger l, string msg)
        {
            if (l.Level >= LogLevel.Verbose) l.Log(LogLevel.Verbose, msg);
        }
    }
}
