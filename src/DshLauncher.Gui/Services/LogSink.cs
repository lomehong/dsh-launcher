using Application = System.Windows.Application;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DshLauncher.Logging;

namespace DshLauncher.Gui.Services
{
    /// <summary>
    /// 把 ILogger 事件桥接到 WPF RichTextBox：在 UI 线程上按 LogLevel 着色追加。
    ///
    /// 性能设计（防 UI 假死的关键）：
    /// - MaxLines 收紧到 400：FlowDocument 每段落都是完整排版对象，几千段必卡；
    /// - 超限时一次批量删 200 段（RangeDelete），避免逐段 Remove 的 O(n²) 文档手术；
    /// - Flush 用 running 标志 + Dispatcher 优先级 Background，让布局/输入永远先于日志；
    /// - ScrollToEnd 仅在用户本来就在底部时执行，避免滚动争抢。
    /// </summary>
    public class LogSink
    {
        private const int MaxLines = 400;        // 显示上限
        private const int TrimBatch = 200;       // 每次超限批量删除量
        private const int FlushBatch = 120;      // 每次调度最多追加行数

        private readonly ConcurrentQueue<LogEvent> _pending = new();
        private RichTextBox _target;
        private bool _flushScheduled;
        private bool _overflowDropped;           // 有丢弃时提示（只提示一次）

        public void Attach(RichTextBox target) => _target = target;
        public void Detach() => _target = null;


        public void OnLog(LogEvent ev)
        {
            // 硬背压：队列积压超 20000 才丢弃（正常装 9 插件全程约 1-2 万行，不丢）
            if (_pending.Count > 20000)
            {
                _overflowDropped = true;
                return;
            }
            _pending.Enqueue(ev);
            ScheduleFlush();
        }

        private void ScheduleFlush()
        {
            if (_flushScheduled || _target == null) return;
            _flushScheduled = true;
            // Background 优先级：布局与用户输入永远先处理，日志追加让路
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(Flush),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Flush()
        {
            _flushScheduled = false;
            if (_target == null) return;
            var doc = _target.Document;

            // 追加（限量）
            var paraBuffer = new Paragraph[FlushBatch];
            int n = 0;
            while (n < FlushBatch && _pending.TryDequeue(out LogEvent ev))
            {
                var para = new Paragraph(new Run(ev.Format())) { Margin = new Thickness(0) };
                para.Foreground = ColorFor(ev.Level);
                paraBuffer[n++] = para;
            }

            // 超限批量修剪：限次删除 + 挂起排版（避免每删一段触发一次 FlowDocument 重排）
            if (doc.Blocks.Count + n > MaxLines)
            {
                int toRemove = Math.Min(TrimBatch, doc.Blocks.Count);
                _target.BeginChange();
                try
                {
                    for (int i = 0; i < toRemove; i++)
                    {
                        var first = doc.Blocks.FirstBlock;
                        if (first == null) break;
                        doc.Blocks.Remove(first);
                    }
                }
                finally { _target.EndChange(); }
                if (_overflowDropped)
                {
                    _overflowDropped = false;
                    var note = new Paragraph(new Run("…（日志过快，部分行已省略；完整日志见文件）"))
                    {
                        Margin = new Thickness(0),
                        Foreground = (Brush)Application.Current.Resources["Brush.LogVerbose"],
                    };
                    doc.Blocks.Add(note);
                }
            }


            for (int i = 0; i < n; i++) doc.Blocks.Add(paraBuffer[i]);

            _target.ScrollToEnd();

            // 还有积压 → 下一拍继续（Background 优先级让 UI 先喘息）
            if (!_pending.IsEmpty) ScheduleFlush();
        }

        public void Clear()
        {
            if (_target == null) return;
            Application.Current?.Dispatcher.Invoke(() => _target.Document.Blocks.Clear());
        }

        private static Brush ColorFor(LogLevel level)
        {
            var app = Application.Current;
            if (app == null) return Brushes.Gray;
            return level switch
            {
                LogLevel.Error => (Brush)app.Resources["Brush.Error"],
                LogLevel.Warn => (Brush)app.Resources["Brush.Warn"],
                LogLevel.Verbose => (Brush)app.Resources["Brush.LogVerbose"],
                LogLevel.Silent => (Brush)app.Resources["Brush.LogVerbose"],
                _ => (Brush)app.Resources["Brush.LogInfo"],
            };
        }
    }
}
