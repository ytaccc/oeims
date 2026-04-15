using System.Diagnostics;
using System.Management;
using Daemon.Abstractions;

namespace Daemon.Monitors
{
    internal class ProcessMonitor : IMonitor
    {
        public string Name => "ProcessMonitor";

        private readonly ManagementEventWatcher? _watcher;
        private readonly string[] _forbiddenProcesses =
        [
            "slack"
        ];

        internal ProcessMonitor()
        {
            _watcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _watcher.EventArrived += OnProcessStarted;
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            var processName = e.NewEvent["ProcessName"]?.ToString()?.Replace(".exe", "").ToLower();

            if (processName != null && _forbiddenProcesses.Contains(processName))
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    process.Kill();
                }
            }
        }

        public async Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
        {
            _watcher?.Start();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var killed = KillForbiddenProcesses();
                    foreach (var process in killed)
                    {
                        await onEvent(new MonitorEvent(Name, $"Forbidden process killed: {process}", Severity.Warning));
                    }
                    await Task.Delay(1000, ct);
                }
            }
            finally
            {
                _watcher?.Stop();
            }
        }

        private IEnumerable<string> KillForbiddenProcesses()
        {
            var killed = new List<string>();
            foreach (var process in Process.GetProcesses())
            {
                if (_forbiddenProcesses.Contains(process.ProcessName.ToLower()))
                {
                    killed.Add(process.ProcessName);
                    process.Kill();
                }
            }
            return killed;
        }

        public void Dispose()
        {
            _watcher?.Stop();
            _watcher?.Dispose();
        }
    }
}