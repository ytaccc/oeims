using System.Runtime.InteropServices;
using Daemon.Abstractions;

namespace Daemon.Monitors
{
    internal class ClipboardMonitor : IMitigator
    {
        public string Name => "ClipboardMonitor";

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        public void Apply()
        {
            OpenClipboard(IntPtr.Zero);
        }

        public void Dispose()
        {
            CloseClipboard();
        }
    }
}