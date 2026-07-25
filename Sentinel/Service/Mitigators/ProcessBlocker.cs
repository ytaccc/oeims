using Contracts;
using OEIMS.Sentinel.Service.Domain.Platform;

namespace OEIMS.Sentinel.Service.Mitigators;

/// <summary>
/// Prevents known forbidden executables from launching during the exam.
/// </summary>
/// <remarks>
/// This is a mitigation, not a monitor. It holds only the policy of which executables are forbidden and
/// delegates the operating system mechanism to an <see cref="IExecutionBlockSource" />.
/// <para>
/// Correlation with <c>ProcessMonitor</c>: <c>ProcessBlocker</c> tries to prevent future launches,
/// while <c>ProcessMonitor</c> detects and kills processes that are already running or still manage to start.
/// </para>
/// </remarks>
internal sealed class ProcessBlocker(IExecutionBlockSource executionBlockSource) : IMitigator
{
    /// <summary>
    /// Name used in logs and diagnostics.
    /// </summary>
    public string Name => nameof(ProcessBlocker);

    // Hard-coded for the academic prototype; move to configuration when more processes are supported.
    private readonly string[] _forbiddenProcesses =
    [
        "slack"
    ];

    /// <summary>
    /// Blocks each forbidden executable from launching.
    /// </summary>
    public void Apply()
    {
        foreach (var process in _forbiddenProcesses)
        {
            executionBlockSource.Block(process);
        }
    }

    /// <summary>
    /// Reverts every block applied by <see cref="Apply" />.
    /// </summary>
    public void Dispose()
    {
        executionBlockSource.UnblockAll();
    }
}