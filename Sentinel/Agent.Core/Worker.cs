using Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OEIMS.Sentinel.Agent.Ipc;

namespace OEIMS.Sentinel.Agent;

internal sealed class Worker(
    IEnumerable<IMonitor> monitors,
    IEnumerable<IMitigator> mitigators,
    AgentEventPipeClient pipeClient,
    AgentCommandPipeServer commandPipeServer,
    ILogger<Worker> logger
) : BackgroundService
{
    private readonly IReadOnlyList<IMonitor> _monitors = monitors.ToList();
    private readonly IReadOnlyList<IMitigator> _mitigators = mitigators.ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agent starting...");

        foreach (var mitigator in _mitigators)
        {
            mitigator.Apply();
            logger.LogInformation("Mitigator applied: {Name}", mitigator.Name);
        }

        var tasks = new List<Task>
        {
            RunComponentAsync(
                "Agent heartbeat",
                SendHeartbeatLoopAsync,
                stoppingToken),

            RunComponentAsync(
                "Agent command pipe",
                commandPipeServer.StartAsync,
                stoppingToken)
        };

        tasks.AddRange(_monitors.Select(monitor => RunComponentAsync(
            monitor.Name,
            ct => monitor.StartAsync(OnEvent, ct),
            stoppingToken)));

        await Task.WhenAll(tasks);
    }

    private async Task SendHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await pipeClient.SendHeartbeatAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task RunComponentAsync(
        string name,
        Func<CancellationToken, Task> runAsync,
        CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Starting component: {Name}", name);

            await runAsync(ct);

            logger.LogInformation("Component stopped: {Name}", name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Component cancelled: {Name}", name);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Component failed: {Name}", name);
            throw;
        }
    }

    private async Task OnEvent(MonitorEvent e)
    {
        Log(e);

        await pipeClient.SendEventAsync(e, CancellationToken.None);
    }

    private void Log(MonitorEvent e)
    {
        switch (e.Severity)
        {
            case Severity.Info:
                logger.LogInformation("[{Monitor}] {Message}", e.MonitorName, e.Message);
                break;

            case Severity.Warning:
                logger.LogWarning("[{Monitor}] {Message}", e.MonitorName, e.Message);
                break;

            case Severity.Critical:
                logger.LogCritical("[{Monitor}] {Message}", e.MonitorName, e.Message);
                break;

            default:
                logger.LogWarning("[{Monitor}] {Message}", e.MonitorName, e.Message);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await pipeClient.DisposeAsync();
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