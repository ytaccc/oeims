namespace Deamon.Abstractions
{
    public interface IMitigator : IDisposable
    {
        string Name { get; }
        Task ApplyAsync(CancellationToken ct);
    }
}