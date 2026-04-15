using System.Net.NetworkInformation;
using Daemon.Abstractions;

namespace Daemon.Monitors
{
    internal record ActiveInterface(string Id, string Name);

    internal enum NetworkEvent
    {
        NetworkChanged,
        MultipleInterfacesDetected,
        MultipleActiveNetworksDetected,
        NoActiveNetworkDetected,
    }

    internal class NetworkMonitor : IMonitor
    {
        public string Name => "NetworkMonitor";

        private string? _initialNetworkId;
        private HashSet<ActiveInterface> _initialInterfaces = [];

        public void InitializeBaseline()
        {
            _initialNetworkId = GetCurrentNetworkId();
            _initialInterfaces = GetActiveInterfaces();
        }

        private static readonly HashSet<NetworkInterfaceType> AllowedPhysicalTypes =
        [
            NetworkInterfaceType.Ethernet,
            NetworkInterfaceType.GigabitEthernet,
            NetworkInterfaceType.Wireless80211,
            NetworkInterfaceType.Wwanpp,
            NetworkInterfaceType.Wwanpp2,
            NetworkInterfaceType.Wman
        ];

        public async Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
        {
            NetworkAddressChangedEventHandler onAddressChanged = (_, _) => _ = CheckNetworkViolation(onEvent);
            NetworkAvailabilityChangedEventHandler onAvailabilityChanged = (_, _) => _ = CheckNetworkViolation(onEvent);

            try
            {
                while (!ct.IsCancellationRequested && !IsValidNetworkState())
                {
                    await onEvent(new MonitorEvent(Name, "Invalid network state, waiting...", Severity.Warning));
                    await Task.Delay(5000, ct);
                }

                if (ct.IsCancellationRequested)
                    return;

                InitializeBaseline();

                NetworkChange.NetworkAddressChanged += onAddressChanged;
                NetworkChange.NetworkAvailabilityChanged += onAvailabilityChanged;

                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                NetworkChange.NetworkAddressChanged -= onAddressChanged;
                NetworkChange.NetworkAvailabilityChanged -= onAvailabilityChanged;
            }
        }

        private async Task CheckNetworkViolation(Func<MonitorEvent, Task> onEvent)
        {
            if (HasNetworkChanged())
                await onEvent(new MonitorEvent(Name, "Network change detected!", Severity.Warning));

            if (HasMultipleInterfaces())
                await onEvent(new MonitorEvent(Name, "Suspicious interfaces detected!", Severity.Warning));

            if (HasMultipleActiveNetworks())
                await onEvent(new MonitorEvent(Name, "Multiple active networks detected!", Severity.Warning));

            if (HasNoActiveNetworks())
                await onEvent(new MonitorEvent(Name, "No active network detected!", Severity.Warning));
        }

        public bool HasNetworkChanged()
        {
            var current = GetCurrentNetworkId();
            return current != _initialNetworkId;
        }

        public bool HasMultipleInterfaces()
        {
            var current = GetActiveInterfaces();
            return !_initialInterfaces.SetEquals(current);
        }

        public bool HasMultipleActiveNetworks()
        {
            var count = GetActiveInterfaces().Count;
            return count > 1;
        }

        public bool HasNoActiveNetworks()
        {
            var count = GetActiveInterfaces().Count;
            return count == 0;
        }

        public bool IsValidNetworkState()
        {
            return GetActivePhysicalInterfaces().Count == 1;
        }

        private string GetCurrentNetworkId()
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            return string.Join("|", active.Select(n =>
            {
                var ipProps = n.GetIPProperties();
                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault()?.Address.ToString() ?? "no-gw";

                return $"{n.Name}-{gateway}";
            }));
        }

        private static HashSet<ActiveInterface> GetActiveInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                        n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.GetIPProperties().GatewayAddresses.Count != 0
                )
                .Select(n => new ActiveInterface(n.Id, n.Name))
                .ToHashSet();
        }

        private static HashSet<ActiveInterface> GetActivePhysicalInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    AllowedPhysicalTypes.Contains(n.NetworkInterfaceType) &&
                    n.GetIPProperties().GatewayAddresses.Count != 0
                )
                .Select( n => new ActiveInterface(n.Id, n.Name))
                .ToHashSet();
        }

        public void Dispose() { }
    }
}
