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

        private readonly TaskCompletionSource _tcs = new TaskCompletionSource();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var clipboardMonitor = new ClipboardMonitor();
            clipboardMonitor.BlockClipboard();

            _networkMonitor.Start();

            try
            {
                await WaitForValidNetwork(stoppingToken);

                _networkMonitor.InitializeBaseline();
                SubscribeEvents();

                await WaitForShutdown(stoppingToken);
            }
            finally
            {
                UnsubscribeEvents();
                _networkMonitor.Stop();
            }
        }

        private async Task WaitForValidNetwork(CancellationToken stoppingToken)
        {
            if (_networkMonitor.IsValidNetworkState())
            {
                logger.LogInformation("Valid network state. Proceeding...");
                return;
            }

            logger.LogWarning("Invalid network state. Guarantee only one physical network connected. Waiting...");

            _networkMonitor.NetworkChanged += OnNetworkChange;

            try
            {
                await _tcs.Task.WaitAsync(stoppingToken);
            }
            finally
            {
                _networkMonitor.NetworkChanged -= OnNetworkChange;
            }
        }

        private void OnNetworkViolationDetected(NetworkEvent eventType)
        {
            switch (eventType)
            {
                case NetworkEvent.NetworkChanged:
                    logger.LogWarning("Network change detected!");
                    break;

                case NetworkEvent.MultipleInterfacesDetected:
                    logger.LogWarning("Suspicious interfaces detected!");
                    break;

                case NetworkEvent.MultipleActiveNetworksDetected:
                    logger.LogWarning("Multiple active networks detected!");
                    break;

                case NetworkEvent.NoActiveNetworkDetected:
                    logger.LogWarning("No active network detected!");
                    break;
            }
        }

        private void OnNetworkChange()
        {
            if (!_networkMonitor.IsValidNetworkState())
            {
                logger.LogWarning("Invalid network state. Guarantee only one physical network connected. Waiting...");
                return;
            }

            logger.LogInformation("Valid network state. Proceeding...");
            _tcs.TrySetResult();
        }

        private static async Task WaitForShutdown(CancellationToken stoppingToken)
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private void SubscribeEvents()
        {
            _networkMonitor.NetworkViolationDetected += OnNetworkViolationDetected;
        }

        private void UnsubscribeEvents()
        {
            _networkMonitor.NetworkViolationDetected -= OnNetworkViolationDetected;
        }
    }
}
