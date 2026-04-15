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

        public event Action? NetworkChanged;
        public event Action<NetworkEvent>? NetworkViolationDetected;

        public void Start()
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        public void Stop()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            HandleNetworkChange();
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            HandleNetworkChange();
        }

        private void HandleNetworkChange()
        {
            NetworkChanged?.Invoke();
            CheckNetworkViolation();
        }

        private void CheckNetworkViolation()
        {
            if (HasNetworkChanged())
                NetworkViolationDetected?.Invoke(NetworkEvent.NetworkChanged);

            if (HasMultipleInterfaces())
                NetworkViolationDetected?.Invoke(NetworkEvent.MultipleInterfacesDetected);

            if (HasMultipleActiveNetworks())
                NetworkViolationDetected?.Invoke(NetworkEvent.MultipleActiveNetworksDetected);

            if (HasNoActiveNetworks())
                NetworkViolationDetected?.Invoke(NetworkEvent.NoActiveNetworkDetected);

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
