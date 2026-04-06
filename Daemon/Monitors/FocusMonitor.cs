using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Daemon.Abstractions;

namespace Daemon.Monitors
{
    internal class FocusMonitor: IMonitor
    {
        public string Name => "FocusMonitor";

        private const string ExamWindowTitle = "OEIMS Exam";

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        internal string GetForegroundWindowTitle()
        {
            IntPtr handle = GetForegroundWindow();
            StringBuilder title = new StringBuilder(256);
            GetWindowText(handle, title, 256);
            return title.ToString();
        }

        internal bool IsExamWindowFocused()
        {
            return GetForegroundWindowTitle().Contains(ExamWindowTitle);
        }

        public async Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!IsExamWindowFocused())
                    await onEvent(new MonitorEvent(Name, $"Focus lost: {GetForegroundWindowTitle()}", Severity.Warning));

                await Task.Delay(1000, ct);
            }
        }

        public void Dispose() { }
    }
}
