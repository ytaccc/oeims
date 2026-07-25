using System.Collections.Concurrent;
using Contracts;
using OEIMS.Sentinel.Service.Domain.Platform;
using OEIMS.Sentinel.Service.Monitors;
using Xunit;

namespace Tests.Service.Monitors;

public sealed class ProcessMonitorTests
{
    [Fact(DisplayName = "Existing forbidden processes are killed when monitoring starts")]
    public async Task ExistingForbiddenProcessesAreKilledWhenMonitoringStarts()
    {
        var source = new FakeProcessSource
        {
            KillResults =
            [
                new ProcessKillResult("Slack", 10, Succeeded: true),
                new ProcessKillResult("slack", 11, Succeeded: true)
            ]
        };

        using var monitor = new ProcessMonitor(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<MonitorEvent>();

        var runTask = monitor.StartAsync(AddTo(events), cts.Token);

        await source.WaitForKillAsync();
        await WaitForEventCountAsync(events, 1);
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "slack" }, source.KillRequests);

        var monitorEvent = Assert.Single(events);
        Assert.Equal(nameof(ProcessMonitor), monitorEvent.MonitorName);
        Assert.Equal("Forbidden process killed: Slack (2 instances)", monitorEvent.Message);
        Assert.Equal(Severity.Critical, monitorEvent.Severity);
    }

    [Fact(DisplayName = "Started processes that are not forbidden are ignored")]
    public async Task StartedProcessesThatAreNotForbiddenAreIgnored()
    {
        var source = new FakeProcessSource();

        using var monitor = new ProcessMonitor(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<MonitorEvent>();

        var runTask = monitor.StartAsync(AddTo(events), cts.Token);

        await source.WaitUntilStartedAsync();
        await source.WaitForKillAsync();
        source.ClearKillRequests();

        await source.RaiseStartedAsync(new ProcessInfo("notepad.exe", 20));
        await Task.Delay(50);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(source.KillRequests);
        Assert.Empty(events);
    }

    [Fact(DisplayName = "Forbidden process names are normalized before matching")]
    public async Task ForbiddenProcessNamesAreNormalizedBeforeMatching()
    {
        var source = new FakeProcessSource
        {
            KillResults = [new ProcessKillResult("Slack", 20, Succeeded: true)]
        };

        using var monitor = new ProcessMonitor(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<MonitorEvent>();

        var runTask = monitor.StartAsync(AddTo(events), cts.Token);

        await source.WaitUntilStartedAsync();
        await source.WaitForKillAsync();
        source.ClearKillRequests();
        events.Clear();

        await source.RaiseStartedAsync(new ProcessInfo("SLACK.exe", 20));
        await WaitForEventCountAsync(events, 1);

        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "slack" }, source.KillRequests);

        var monitorEvent = Assert.Single(events);
        Assert.Equal("Forbidden process killed: Slack", monitorEvent.Message);
        Assert.Equal(Severity.Critical, monitorEvent.Severity);
    }

    [Fact(DisplayName = "Kill source failures are reported without crashing the monitor")]
    public async Task KillSourceFailuresAreReportedWithoutCrashingTheMonitor()
    {
        var source = new FakeProcessSource
        {
            KillException = new InvalidOperationException("boom")
        };

        using var monitor = new ProcessMonitor(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<MonitorEvent>();

        var runTask = monitor.StartAsync(AddTo(events), cts.Token);

        await source.WaitForKillAsync();
        await WaitForEventCountAsync(events, 1);
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var monitorEvent = Assert.Single(events);
        Assert.Equal("Failed to kill forbidden process: slack (boom)", monitorEvent.Message);
        Assert.Equal(Severity.Critical, monitorEvent.Severity);
    }

    [Fact(DisplayName = "Failed kill results are grouped by process and error")]
    public async Task FailedKillResultsAreGroupedByProcessAndError()
    {
        var source = new FakeProcessSource
        {
            KillResults =
            [
                new ProcessKillResult("Slack", 10, Succeeded: false, "access denied"),
                new ProcessKillResult("Slack", 11, Succeeded: false, "access denied"),
                new ProcessKillResult("Slack", 12, Succeeded: false, "already exited")
            ]
        };

        using var monitor = new ProcessMonitor(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentQueue<MonitorEvent>();

        var runTask = monitor.StartAsync(AddTo(events), cts.Token);

        await source.WaitForKillAsync();
        await WaitForEventCountAsync(events, 2);
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var messages = events.Select(e => e.Message).Order().ToArray();

        Assert.Equal(
            new[]
            {
                "Failed to kill forbidden process: Slack (2 instances) (access denied)",
                "Failed to kill forbidden process: Slack (already exited)"
            },
            messages);
    }

    private static Func<MonitorEvent, Task> AddTo(ConcurrentQueue<MonitorEvent> events) => e =>
    {
        events.Enqueue(e);
        return Task.CompletedTask;
    };

    private static async Task WaitForEventCountAsync(
        ConcurrentQueue<MonitorEvent> events,
        int expectedCount)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);

        while (events.Count < expectedCount && DateTime.UtcNow < timeoutAt)
            await Task.Delay(10);

        Assert.True(
            events.Count >= expectedCount,
            $"Expected at least {expectedCount} events, got {events.Count}.");
    }

    private sealed class FakeProcessSource : IProcessSource
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _killSignal = new(0);
        private readonly object _lock = new();
        private Func<ProcessInfo, Task>? _onStarted;

        public IReadOnlyList<ProcessKillResult> KillResults { get; init; } = [];
        public Exception? KillException { get; init; }
        public List<string> KillRequests { get; } = [];

        public Task StartAsync(Func<ProcessInfo, Task> onStarted, CancellationToken ct)
        {
            _onStarted = onStarted;
            _started.TrySetResult();

            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        public Task<IReadOnlyList<ProcessKillResult>> KillByNameAsync(
            string processName,
            CancellationToken ct)
        {
            lock (_lock)
                KillRequests.Add(processName);

            _killSignal.Release();

            if (KillException is not null)
                throw KillException;

            return Task.FromResult(KillResults);
        }

        public Task WaitUntilStartedAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task WaitForKillAsync()
        {
            var signaled = await _killSignal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(signaled, "Expected KillByNameAsync to be called.");
        }

        public void ClearKillRequests()
        {
            lock (_lock)
                KillRequests.Clear();
        }

        public Task RaiseStartedAsync(ProcessInfo process) =>
            _onStarted?.Invoke(process) ?? Task.CompletedTask;

        public void Dispose()
        {
            _killSignal.Dispose();
        }
    }
}
