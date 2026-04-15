using Daemon.Abstractions;
using Daemon.Monitors;

namespace Daemon
{
    public class Worker(ILogger<Worker> logger) : BackgroundService
    {
        private readonly List<IMonitor> _monitors =
        [
            new FocusMonitor(),
            new ProcessMonitor(),
            new NetworkMonitor(),
        ];

        private readonly List<IMitigator> _mitigators =
        [
            new ClipboardMonitor(),
            new ProcessBlocker(),
        ];

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Task OnEvent(MonitorEvent e)
            {
                switch (e.Severity)
                {
                    case MonitorEventSeverity.Information:
                        logger.LogInformation("[{monitor}] {message}", e.MonitorName, e.Message);
                        break;
                    case MonitorEventSeverity.Warning:
                        logger.LogWarning("[{monitor}] {message}", e.MonitorName, e.Message);
                        break;
                    case MonitorEventSeverity.Critical:
                        logger.LogCritical("[{monitor}] {message}", e.MonitorName, e.Message);
                        break;
                    default:
                        logger.LogWarning("[{monitor}] {message}", e.MonitorName, e.Message);
                        break;
                }
                return Task.CompletedTask;
            }

            foreach (var mitigator in _mitigators)
            {
                mitigator.Apply();
                logger.LogInformation("Mitigator applied: {name}", mitigator.Name);
            }

            await Task.WhenAll(_monitors.Select(m => m.StartAsync(OnEvent, stoppingToken)));
        }

        public override void Dispose()
        {
            foreach (var mitigator in _mitigators)
                mitigator.Dispose();

            foreach (var monitor in _monitors)
                monitor.Dispose();

            base.Dispose();
        }
    }
}