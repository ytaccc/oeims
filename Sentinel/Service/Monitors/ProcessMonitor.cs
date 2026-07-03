using Contracts;
using OEIMS.Sentinel.Service.Domain.Platform;

namespace OEIMS.Sentinel.Service.Monitors;

/// <summary>
/// Internal event kind used to build professor process monitor messages.
/// </summary>
internal enum ProcessEvent
{
    /// <summary>
    /// A forbidden process was found and killed.
    /// </summary>
    ForbiddenProcessKilled,

    /// <summary>
    /// A forbidden process was found but could not be killed.
    /// </summary>
    ForbiddenProcessKillFailed
}

/// <summary>
/// Detects forbidden processes and tries to stop them during the exam.
/// </summary>
/// <remarks>
/// This monitor checks already running forbidden processes when it starts and also watches for new ones.
/// <para>
/// The difference from <c>ProcessBlocker</c> is that <c>ProcessMonitor</c> detects and kills processes that are running.
/// <c>ProcessBlocker</c> tries to prevent future launches through Windows mitigation. They are intentionally complementary.
/// </para>
/// </remarks>
/// <param name="processSource">
/// Platform boundary used to watch process starts and kill matching processes.
/// </param>
internal sealed class ProcessMonitor(IProcessSource processSource) : IMonitor
{
    public string Name => nameof(ProcessMonitor);

    private readonly SemaphoreSlim _killLock = new(1, 1);

    private readonly HashSet<string> _forbiddenProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "slack"
    };

    /// <summary>
    /// Starts process monitoring until cancellation.
    /// </summary>
    /// <param name="onEvent">
    /// Callback that receives warnings for successful and failed kills attempts.
    /// </param>
    /// <param name="ct">
    /// Cancellation token used by the Service to stop the monitor.
    /// </param>
    /// <returns>A task that completes when monitoring stops.</returns>
    public async Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
    {
        try
        {
            var monitoringTask = processSource.StartAsync(
                process => KillIfForbiddenProcessAsync(process, onEvent, ct),
                ct);

            await KillForbiddenProcessesAsync(onEvent, ct);

            await monitoringTask;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore since normal shutdown.
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The process monitor failed to start or stopped unexpectedly.",
                ex);
        }
    }

    /// <summary>
    /// Handles one notification of a process starting from the platform source.
    /// </summary>
    /// <param name="process">Process that has just started.</param>
    /// <param name="onEvent">Callback used to publish monitor events.</param>
    /// <param name="ct">Cancellation token used to stop the kill attempt.</param>
    private async Task KillIfForbiddenProcessAsync(
        ProcessInfo process,
        Func<MonitorEvent, Task> onEvent,
        CancellationToken ct)
    {
        var processName = NormalizeProcessName(process.Name);

        if (string.IsNullOrWhiteSpace(processName))
            return;

        if (!_forbiddenProcesses.Contains(processName))
            return;

        await KillProcessByNameAsync(processName, onEvent, ct);
    }

    /// <summary>
    /// Checks the current process list when the monitor starts.
    /// </summary>
    /// <param name="onEvent">Callback used to publish monitor events.</param>
    /// <param name="ct">Cancellation token used to stop the scan.</param>
    private async Task KillForbiddenProcessesAsync(
        Func<MonitorEvent, Task> onEvent,
        CancellationToken ct)
    {
        foreach (var forbiddenProcess in _forbiddenProcesses)
        {
            ct.ThrowIfCancellationRequested();

            await KillProcessByNameAsync(forbiddenProcess, onEvent, ct);
        }
    }

    /// <summary>
    /// Kills all process instances matching a forbidden process name and reports the outcome.
    /// </summary>
    /// <param name="processName">Normalized forbidden process name.</param>
    /// <param name="onEvent">Callback used to publish monitor events.</param>
    /// <param name="ct">Cancellation token used to stop the operation.</param>
    private async Task KillProcessByNameAsync(
        string processName,
        Func<MonitorEvent, Task> onEvent,
        CancellationToken ct)
    {
        await _killLock.WaitAsync(ct);

        try
        {
            IReadOnlyList<ProcessKillResult> results;

            try
            {
                results = await processSource.KillByNameAsync(processName, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await onEvent(CreateMonitorEvent(
                    ProcessEvent.ForbiddenProcessKillFailed,
                    processName,
                    count: 0,
                    errorMessage: ex.Message));

                return;
            }

            var successfulKills = results
                .Where(result => result.Succeeded)
                .ToList();

            if (successfulKills.Count > 0)
            {
                var displayName = successfulKills[0].ProcessName;

                await onEvent(CreateMonitorEvent(
                    ProcessEvent.ForbiddenProcessKilled,
                    displayName,
                    successfulKills.Count));
            }

            var failedGroups = results
                .Where(result => !result.Succeeded)
                .GroupBy(result => new
                {
                    result.ProcessName,
                    result.ErrorMessage
                });

            foreach (var failedGroup in failedGroups)
            {
                await onEvent(CreateMonitorEvent(
                    ProcessEvent.ForbiddenProcessKillFailed,
                    failedGroup.Key.ProcessName,
                    failedGroup.Count(),
                    failedGroup.Key.ErrorMessage));
            }
        }
        finally
        {
            _killLock.Release();
        }
    }

    /// <summary>
    /// Builds the final event sent to the Service pipeline.
    /// </summary>
    /// <param name="eventType">Process event category.</param>
    /// <param name="processName">Process name shown in the message.</param>
    /// <param name="count">Number of process instances represented by the message.</param>
    /// <param name="errorMessage">Optional failure reason.</param>
    /// <returns>A warning monitor event describing the kill result.</returns>
    private MonitorEvent CreateMonitorEvent(
        ProcessEvent eventType,
        string processName,
        int count,
        string? errorMessage = null)
    {
        var processDescription = count <= 1
            ? processName
            : $"{processName} ({count} instances)";

        return eventType switch
        {
            ProcessEvent.ForbiddenProcessKilled =>
                new MonitorEvent(
                    Name,
                    $"Forbidden process killed: {processDescription}",
                    Severity.Warning),

            ProcessEvent.ForbiddenProcessKillFailed =>
                new MonitorEvent(
                    Name,
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? $"Failed to kill forbidden process: {processDescription}"
                        : $"Failed to kill forbidden process: {processDescription} ({errorMessage})",
                    Severity.Warning),

            _ =>
                new MonitorEvent(
                    Name,
                    $"Unhandled process event: {eventType} ({processDescription})",
                    Severity.Warning)
        };
    }

    /// <summary>
    /// Converts process names such as <c>PROGRAM.exe</c> and <c>C:\\...\\Program.exe</c> into <c>program</c>.
    /// </summary>
    /// <param name="processName">Process name or executable path reported by the process source.</param>
    /// <returns>Lowercase executable name without extension.</returns>
    private static string NormalizeProcessName(string processName)
    {
        return Path
            .GetFileNameWithoutExtension(processName)
            .Trim()
            .ToLowerInvariant();
    }

    /// <summary>
    /// Releases monitor resources and the platform process source.
    /// </summary>
    public void Dispose()
    {
        _killLock.Dispose();
        processSource.Dispose();
    }
}