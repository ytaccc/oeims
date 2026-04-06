namespace Deamon.Abstractions
{
    public interface IMitigator : IDisposable
    {
        string Name { get; }
        Task Apply(CancellationToken ct);
    }
}