using Contracts;
using OEIMS.Sentinel.Agent.Domain;

namespace OEIMS.Sentinel.Agent.Monitors;

internal sealed class FocusMonitor(IActiveWindowSource activeWindowSource) : IMonitor
{
    public string Name => nameof(FocusMonitor);

    private const string ExamWindowTitle = "OEIMS Exam";

    private string _lastTitle = string.Empty;

    public Task StartAsync(Func<MonitorEvent, Task> onEvent, CancellationToken ct)
    {
        return activeWindowSource.StartAsync(async activeWindow =>
        {
            var title = activeWindow.Title;

            if (title == _lastTitle)
                return;

            _lastTitle = title;

            if (!title.Contains(ExamWindowTitle, StringComparison.Ordinal))
            {
                await onEvent(new MonitorEvent(
                    Name,
                    $"Focus lost: {title}",
                    Severity.Warning));
            }
        }, ct);
    }

    public void Dispose()
    {
        activeWindowSource.Dispose();
    }
}