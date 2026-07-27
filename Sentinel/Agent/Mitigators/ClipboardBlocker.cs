using Contracts;
using OEIMS.Sentinel.Agent.Domain;

namespace OEIMS.Sentinel.Agent.Mitigators;

/// <summary>
/// Prevents clipboard access.
/// </summary>
/// <param name="clipboardSource">
/// Platform implementation that offers clipboard interaction.
/// </param>
internal sealed class ClipboardBlocker(IClipboardSource clipboardSource) : IMitigator
{
    private bool _applied;

    public string Name => nameof(ClipboardBlocker);

    public void Apply()
    {
        if (_applied)
            return;

        clipboardSource.Block();
        _applied = true;
    }

    public void Dispose() => clipboardSource.Unblock();
}
