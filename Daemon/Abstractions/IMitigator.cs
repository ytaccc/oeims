namespace Daemon.Abstractions
{
    public interface IMitigator : IDisposable
    {
        string Name { get; }
        void Apply();
    }
}