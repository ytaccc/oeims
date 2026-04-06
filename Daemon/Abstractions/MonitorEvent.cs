namespace Daemon.Abstractions
{
    public record MonitorEvent(string MonitorName, string Message, Severity Severity);
}

