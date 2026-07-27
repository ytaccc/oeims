using OEIMS.Sentinel.Agent.Domain;
using OEIMS.Sentinel.Agent.Mitigators;
using Xunit;

namespace Tests.Agent.Mitigators;

public sealed class ClipboardBlockerTests
{
    [Fact(DisplayName = "Clipboard blocker exposes its component name")]
    public void ClipboardBlockerExposesItsComponentName()
    {
        using var blocker = new ClipboardBlocker(new FakeClipboardSource());

        Assert.Equal(nameof(ClipboardBlocker), blocker.Name);
    }

    [Fact(DisplayName = "Applying the clipboard blocker blocks the clipboard source")]
    public void ApplyingTheClipboardBlockerBlocksTheClipboardSource()
    {
        var source = new FakeClipboardSource();
        using var blocker = new ClipboardBlocker(source);

        blocker.Apply();

        Assert.Equal(1, source.BlockCalls);
        Assert.Equal(0, source.UnblockCalls);
    }

    [Fact(DisplayName = "Applying the clipboard blocker twice blocks only once")]
    public void ApplyingTheClipboardBlockerTwiceBlocksOnlyOnce()
    {
        var source = new FakeClipboardSource();
        using var blocker = new ClipboardBlocker(source);

        blocker.Apply();
        blocker.Apply();

        Assert.Equal(1, source.BlockCalls);
    }

    [Fact(DisplayName = "Disposing the clipboard blocker unblocks the clipboard source")]
    public void DisposingTheClipboardBlockerUnblocksTheClipboardSource()
    {
        var source = new FakeClipboardSource();
        var blocker = new ClipboardBlocker(source);

        blocker.Dispose();

        Assert.Equal(0, source.BlockCalls);
        Assert.Equal(1, source.UnblockCalls);
    }

    private sealed class FakeClipboardSource : IClipboardSource
    {
        public int BlockCalls { get; private set; }
        public int UnblockCalls { get; private set; }

        public void Block()
        {
            BlockCalls++;
        }

        public void Unblock()
        {
            UnblockCalls++;
        }

        public void Dispose()
        {
        }
    }
}
