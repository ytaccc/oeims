using System.Runtime.InteropServices;
using System.Text;
using Daemon.Abstractions;

namespace Daemon.Monitors
{
    internal class FocusMonitor : IMonitor
    {
        public string Name => "FocusMonitor";

        private const string ExamWindowTitle = "OEIMS Exam";
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint PM_REMOVE = 0x0001;

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd,
            uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        public Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();

            var thread = new Thread(() =>
            {
                var foregroundHwnd = IntPtr.Zero;
                var lastTitle = string.Empty;

                void HandleEvent(IntPtr hwnd)
                {
                    var title = new StringBuilder(256);
                    GetWindowText(hwnd, title, 256);
                    var titleStr = title.ToString();

                    if (titleStr == lastTitle) return;
                    lastTitle = titleStr;

                    if (!titleStr.Contains(ExamWindowTitle))
                        _ = onEvent(new MonitorEvent(Name, $"Focus lost: {titleStr}", Severity.Warning));
                }

                WinEventDelegate foregroundCallback = (hook, type, hwnd, idObject, idChild, thread, time) =>
                {
                    foregroundHwnd = hwnd;
                    HandleEvent(hwnd);
                };

                WinEventDelegate nameChangeCallback = (hook, type, hwnd, idObject, idChild, thread, time) =>
                {
                    if (hwnd != foregroundHwnd) return;
                    HandleEvent(hwnd);
                };

                var hook1 = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, foregroundCallback, 0, 0, WINEVENT_OUTOFCONTEXT);

                var hook2 = SetWinEventHook(
                    EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE,
                    IntPtr.Zero, nameChangeCallback, 0, 0, WINEVENT_OUTOFCONTEXT);

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                        {
                            TranslateMessage(ref msg);
                            DispatchMessage(ref msg);
                        }
                        else
                        {
                            ct.WaitHandle.WaitOne(100);
                        }
                    }
                }
                finally
                {
                    UnhookWinEvent(hook1);
                    UnhookWinEvent(hook2);
                    tcs.SetResult();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        public void Dispose() { }
    }
}