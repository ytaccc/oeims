using Contracts;
using System.Net.NetworkInformation;

namespace OEIMS.Sentinel.Service.Monitors;

/// <summary>
/// Network interface considered active by the Service.
/// </summary>
/// <param name="Id">Stable operating system identifier for the interface.</param>
/// <param name="Name">Human-readable interface name shown in monitor messages.</param>
internal sealed record ActiveInterface(string Id, string Name);

/// <summary>
/// Network violation ready to be emitted as a monitor event.
/// </summary>
/// <param name="Key">
/// Deduplication key. Equal keys mean the same violation is still happening and should not be spammed.
/// </param>
/// <param name="Event">Professor-facing event that explains the violation.</param>
internal sealed record NetworkViolation(
    string Key,
    MonitorEvent Event);

/// <summary>
/// Snapshot of the current network state used for baseline comparison.
/// </summary>
/// <param name="ActiveInterfaces">All active non-loopback interfaces with a gateway.</param>
/// <param name="ActivePhysicalInterfaces">Active interfaces that are considered physical network adapters.</param>
/// <param name="NetworkId">Simple identity of the connected network, based on interface name and gateway.</param>
internal sealed record NetworkState(
    HashSet<ActiveInterface> ActiveInterfaces,
    HashSet<ActiveInterface> ActivePhysicalInterfaces,
    string NetworkId);

/// <summary>
/// Validates the student's network before the exam and detects suspicious network changes during the exam.
/// </summary>
/// <remarks>
/// The rule is intentionally strict: exactly one active physical interface is allowed.
/// This rejects VPNs, virtual adapters, multiple simultaneous connections, and interface changes after the baseline is created.
/// </remarks>
internal sealed class NetworkMonitor : IMonitor
{
    public string Name => nameof(NetworkMonitor);

    private static readonly HashSet<NetworkInterfaceType> AllowedPhysicalTypes =
    [
        // Wired internet connection, usually through an Ethernet cable.
        NetworkInterfaceType.Ethernet,

        // Faster wired internet connection, usually through an Ethernet cable.
        NetworkInterfaceType.GigabitEthernet,

        // Wi-Fi connection.
        NetworkInterfaceType.Wireless80211,

        // Mobile network connection, such as 3G, 4G, or 5G.
        NetworkInterfaceType.Wwanpp,

        // Mobile network connection, such as 3G, 4G, or 5G.
        NetworkInterfaceType.Wwanpp2,

        // Wireless metropolitan network connection, similar to long-range wireless internet.
        NetworkInterfaceType.Wman
    ];

    private NetworkState? _baseline;
    private string? _lastViolationKey;
    private bool _started;

    /// <summary>
    /// Raised whenever Windows reports a network address or availability change.
    /// </summary>
    public event Action? NetworkChanged;

    /// <summary>
    /// Raised when the current network state violates the exam baseline.
    /// </summary>
    public event Action<MonitorEvent>? NetworkViolationDetected;

    /// <summary>
    /// Captures the current valid network state as the exam baseline.
    /// </summary>
    public void InitializeBaseline()
    {
        _baseline = GetCurrentNetworkState();
        _lastViolationKey = null;
    }

    /// <summary>
    /// Subscribes to Windows network change notifications.
    /// </summary>
    public void Start()
    {
        if (_started)
            return;

        _baseline = null;
        _lastViolationKey = null;

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _started = true;
    }

    /// <summary>
    /// Unsubscribes from Windows network change notifications and clears monitor state.
    /// </summary>
    public void Stop()
    {
        if (!_started)
            return;

        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

        _baseline = null;
        _lastViolationKey = null;
        _started = false;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        HandleNetworkChange();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        HandleNetworkChange();
    }

    /// <summary>
    /// Handles one Windows network notification.
    /// </summary>
    private void HandleNetworkChange()
    {
        NetworkChanged?.Invoke();
        CheckNetworkViolation();
    }

    /// <summary>
    /// Compares the current network state with the baseline and emits only new violations.
    /// </summary>
    private void CheckNetworkViolation()
    {
        if (_baseline is null)
            return;

        var violation = GetCurrentViolation();

        if (violation is null)
        {
            _lastViolationKey = null;
            return;
        }

        if (violation.Key == _lastViolationKey)
            return;

        _lastViolationKey = violation.Key;

        NetworkViolationDetected?.Invoke(violation.Event);
    }

    /// <summary>
    /// Returns the current violation, if the active network no longer matches the baseline rules.
    /// </summary>
    /// <returns>A violation event or <c>null</c> when the network is still valid.</returns>
    private NetworkViolation? GetCurrentViolation()
    {
        if (_baseline is null)
            return null;

        var currentState = GetCurrentNetworkState();

        var baselineInterfaces = FormatInterfaces(_baseline.ActiveInterfaces);
        var activeInterfaces = FormatInterfaces(currentState.ActiveInterfaces);

        return currentState switch
        {
            _ when HasNoActiveNetwork(currentState) =>
                new NetworkViolation(
                    Key: "NoActiveNetwork",
                    Event: new MonitorEvent(
                        Name,
                        $"Network disconnected: no active network interface. Baseline was: {baselineInterfaces}.",
                        Severity.Warning)),

            _ when HasNoPhysicalInterface(currentState) =>
                new NetworkViolation(
                    Key: $"NoPhysicalInterface:{activeInterfaces}",
                    Event: new MonitorEvent(
                        Name,
                        $"Invalid network state: active interfaces exist, but none are physical. Active interfaces: {activeInterfaces}. Baseline was: {baselineInterfaces}.",
                        Severity.Warning)),

            _ when HasMultipleActiveInterfaces(currentState) =>
                new NetworkViolation(
                    Key: $"MultipleInterfaces:{activeInterfaces}",
                    Event: new MonitorEvent(
                        Name,
                        $"Multiple or suspicious active interfaces detected: {activeInterfaces}. Only one physical interface is allowed. Baseline was: {baselineInterfaces}.",
                        Severity.Warning)),

            _ when HasInterfaceChanged(_baseline, currentState) =>
                new NetworkViolation(
                    Key: $"InterfacesChanged:{activeInterfaces}",
                    Event: new MonitorEvent(
                        Name,
                        $"Network interface changed: baseline was {baselineInterfaces}, current is {activeInterfaces}.",
                        Severity.Warning)),

            _ when HasNetworkIdentityChanged(_baseline, currentState) =>
                new NetworkViolation(
                    Key: $"NetworkChanged:{currentState.NetworkId}",
                    Event: new MonitorEvent(
                        Name,
                        $"Network changed on the same interface: baseline was {_baseline.NetworkId}, current is {currentState.NetworkId}.",
                        Severity.Warning)),

            _ => null
        };
    }

    /// <summary>
    /// Checks if the current network state is acceptable before creating a baseline.
    /// </summary>
    /// <returns><c>true</c> when exactly one active physical interface is present.</returns>
    public static bool IsValidNetworkState()
    {
        return IsValidNetworkState(GetCurrentNetworkState());
    }

    /// <summary>
    /// Runs the pre-exam network gate.
    /// </summary>
    /// <param name="onEvent">Callback used to report waiting, warning, and success messages.</param>
    /// <param name="ct">Cancellation token used when the service stops before the exam starts.</param>
    /// <returns>A task that completes after a valid baseline is initialized.</returns>
    public async Task StartPreExamAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
    {
        Start();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!IsValidNetworkState())
                {
                    await onEvent(new MonitorEvent(
                        Name,
                        "Invalid network state. Exactly one active network interface is allowed, and it must be physical. Disable VPNs, virtual adapters, or extra network connections. Waiting...",
                        Severity.Warning));

                    await WaitForValidNetworkAsync(ct);
                }

                InitializeBaseline();

                if (IsValidNetworkState())
                {
                    await onEvent(new MonitorEvent(
                        Name,
                        "Valid network state and baseline initialized. Proceeding to exam.",
                        Severity.Info));

                    return;
                }

                await onEvent(new MonitorEvent(
                    Name,
                    "Network state changed while initializing baseline. Waiting...",
                    Severity.Warning));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Stop();
            throw;
        }
    }

    /// <summary>
    /// Starts exam-time monitoring after the pre-exam baseline has been created.
    /// </summary>
    /// <param name="onEvent">Callback used to publish network violation events.</param>
    /// <param name="ct">Cancellation token used when the exam ends or the service stops.</param>
    /// <returns>A task that completes when exam-time monitoring stops.</returns>
    public async Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
    {
        if (_baseline is null)
            throw new InvalidOperationException("Pre-exam validation must run before exam monitoring starts.");

        Action<MonitorEvent> onViolation = monitorEvent =>
        {
            _ = NotifyAsync(monitorEvent, onEvent);
        };

        NetworkViolationDetected += onViolation;

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore since normal shutdown.
        }
        finally
        {
            NetworkViolationDetected -= onViolation;
            Stop();
        }
    }

    /// <summary>
    /// Sends a monitor event without letting callback failures crash the Windows network callback.
    /// </summary>
    private static async Task NotifyAsync(
        MonitorEvent monitorEvent,
        Func<MonitorEvent, Task> onEvent)
    {
        try
        {
            await onEvent(monitorEvent);
        }
        catch
        {
            // ignore because the network callback should not crash because an event consumer failed.
        }
    }

    /// <summary>
    /// Waits until the current network becomes valid again.
    /// </summary>
    /// <param name="ct">Cancellation token used to stop waiting.</param>
    private async Task WaitForValidNetworkAsync(CancellationToken ct)
    {
        if (IsValidNetworkState())
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChange()
        {
            if (IsValidNetworkState())
                tcs.TrySetResult();
        }

        NetworkChanged += OnChange;

        try
        {
            await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            NetworkChanged -= OnChange;
        }
    }

    /// <summary>
    /// Reads the current operating system network state.
    /// </summary>
    /// <returns>A snapshot used for validation and baseline comparison.</returns>
    private static NetworkState GetCurrentNetworkState()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsActiveInterface)
            .ToList();

        return new NetworkState(
            ActiveInterfaces: [.. activeInterfaces.Select(ToActiveInterface)],
            ActivePhysicalInterfaces: [.. activeInterfaces
                .Where(IsPhysicalInterface)
                .Select(ToActiveInterface)],
            // <interface-name>-<gateway-address>|<interface-name>-<gateway-address>|...
            NetworkId: string.Join("|", activeInterfaces.Select(GetNetworkIdentity)));
    }

    private static bool IsValidNetworkState(NetworkState state)
    {
        return state.ActiveInterfaces.Count == 1 &&
               state.ActivePhysicalInterfaces.Count == 1;
    }

    private static bool HasNoActiveNetwork(NetworkState state)
    {
        return state.ActiveInterfaces.Count == 0;
    }

    private static bool HasNoPhysicalInterface(NetworkState state)
    {
        return state.ActivePhysicalInterfaces.Count == 0;
    }

    private static bool HasMultipleActiveInterfaces(NetworkState state)
    {
        return state.ActiveInterfaces.Count > 1;
    }

    private static bool HasInterfaceChanged(
        NetworkState baseline,
        NetworkState current)
    {
        return !baseline.ActiveInterfaces.SetEquals(current.ActiveInterfaces);
    }

    private static bool HasNetworkIdentityChanged(
        NetworkState baseline,
        NetworkState current)
    {
        return current.NetworkId != baseline.NetworkId;
    }

    /// <summary>
    /// Determines whether an interface should count as active for exam policy.
    /// </summary>
    private static bool IsActiveInterface(NetworkInterface networkInterface)
    {
        return networkInterface.OperationalStatus == OperationalStatus.Up &&
               networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               HasGateway(networkInterface);
    }

    /// <summary>
    /// Determines whether an interface type is accepted as physical for this project.
    /// </summary>
    private static bool IsPhysicalInterface(NetworkInterface networkInterface)
    {
        return AllowedPhysicalTypes.Contains(networkInterface.NetworkInterfaceType);
    }

    /// <summary>
    /// Checks whether the interface has at least one gateway.
    /// </summary>
    private static bool HasGateway(NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface.GetIPProperties().GatewayAddresses.Count != 0;
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static ActiveInterface ToActiveInterface(NetworkInterface networkInterface)
    {
        return new ActiveInterface(
            networkInterface.Id,
            networkInterface.Name);
    }

    /// <summary>
    /// Builds a simple network identity for detecting network changes on the same interface.
    /// </summary>
    private static string GetNetworkIdentity(NetworkInterface networkInterface)
    {
        // "<interface-name>-<gateway-address>" (e.g. "Wi-Fi-192.168.1.1")
        return $"{networkInterface.Name}-{GetGatewayAddress(networkInterface)}";
    }

    private static string GetGatewayAddress(NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface
                .GetIPProperties()
                .GatewayAddresses
                .FirstOrDefault()
                ?.Address
                .ToString() ?? "no-gw";
        }
        catch (NetworkInformationException)
        {
            return "unknown-gw";
        }
    }

    /// <summary>
    /// Formats interface names for messages shown to professors and logs.
    /// </summary>
    private static string FormatInterfaces(IEnumerable<ActiveInterface> interfaces)
    {
        var names = interfaces
            .Select(i => i.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name)
            .ToList();

        return names.Count == 0
            ? "none"
            : string.Join(", ", names);
    }

    /// <summary>
    /// Stops the monitor and unsubscribes from Windows network events.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }
}