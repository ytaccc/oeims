using System.Collections.Concurrent;
using System.IO.Pipes;
using Contracts.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using OEIMS.Sentinel.Agent.Domain;
using OEIMS.Sentinel.Agent.Ipc;
using OEIMS.Sentinel.Agent.Mitigators;
using Xunit;

namespace Tests.Agent.Ipc;

[Collection("Agent command pipe")]
public sealed class AgentCommandPipeServerTests
{
    [Fact(DisplayName = "A valid identity code command blocks the clipboard and displays the code")]
    public async Task AValidIdentityCodeCommandBlocksTheClipboardAndDisplaysTheCode()
    {
        var overlay = new FakeExamIdentityCodeOverlay();
        var clipboard = new FakeClipboardSource();
        using var blocker = new ClipboardBlocker(clipboard);
        var server = new AgentCommandPipeServer(
            overlay,
            blocker,
            NullLogger<AgentCommandPipeServer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(cts.Token);

        await SendCommandAsync("{\"type\":\"ShowExamIdentityCode\",\"code\":\"ABC-123\"}", cts.Token);

        Assert.Equal("ABC-123", await overlay.WaitForCodeAsync());
        Assert.Equal(1, clipboard.BlockCalls);

        await StopServerAsync(cts, serverTask);
    }

    [Fact(DisplayName = "A command without a type is ignored")]
    public async Task ACommandWithoutATypeIsIgnored()
    {
        var overlay = new FakeExamIdentityCodeOverlay();
        using var blocker = new ClipboardBlocker(new FakeClipboardSource());
        var server = new AgentCommandPipeServer(
            overlay,
            blocker,
            NullLogger<AgentCommandPipeServer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(cts.Token);

        await SendCommandAsync("{\"code\":\"ABC-123\"}", cts.Token);
        await Task.Delay(50, cts.Token);

        Assert.Empty(overlay.Codes);

        await StopServerAsync(cts, serverTask);
    }

    [Fact(DisplayName = "A command with an empty identity code is ignored")]
    public async Task ACommandWithAnEmptyIdentityCodeIsIgnored()
    {
        var overlay = new FakeExamIdentityCodeOverlay();
        var clipboard = new FakeClipboardSource();
        using var blocker = new ClipboardBlocker(clipboard);
        var server = new AgentCommandPipeServer(
            overlay,
            blocker,
            NullLogger<AgentCommandPipeServer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(cts.Token);

        await SendCommandAsync("{\"type\":\"ShowExamIdentityCode\",\"code\":\"   \"}", cts.Token);
        await Task.Delay(50, cts.Token);

        Assert.Empty(overlay.Codes);
        Assert.Equal(0, clipboard.BlockCalls);

        await StopServerAsync(cts, serverTask);
    }

    [Fact(DisplayName = "Malformed commands are ignored and the pipe keeps accepting commands")]
    public async Task MalformedCommandsAreIgnoredAndThePipeKeepsAcceptingCommands()
    {
        var overlay = new FakeExamIdentityCodeOverlay();
        using var blocker = new ClipboardBlocker(new FakeClipboardSource());
        var server = new AgentCommandPipeServer(
            overlay,
            blocker,
            NullLogger<AgentCommandPipeServer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(cts.Token);

        await SendCommandAsync("not-json", cts.Token);
        await SendCommandAsync("{\"type\":\"ShowExamIdentityCode\",\"code\":\"XYZ-789\"}", cts.Token);

        Assert.Equal("XYZ-789", await overlay.WaitForCodeAsync());

        await StopServerAsync(cts, serverTask);
    }

    private static async Task SendCommandAsync(string json, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeNames.AgentCommands,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(timeout: 2_000, ct);

        await using var writer = new StreamWriter(pipe);
        await writer.WriteLineAsync(json.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private static async Task StopServerAsync(
        CancellationTokenSource cts,
        Task serverTask)
    {
        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeExamIdentityCodeOverlay : IExamIdentityCodeOverlay
    {
        private readonly ConcurrentQueue<string> _codes = new();
        private readonly SemaphoreSlim _signal = new(0);

        public IReadOnlyCollection<string> Codes => _codes.ToArray();

        public Task DisplayCodeAsync(string code, CancellationToken ct = default)
        {
            _codes.Enqueue(code);
            _signal.Release();
            return Task.CompletedTask;
        }

        public async Task<string> WaitForCodeAsync()
        {
            var signaled = await _signal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(signaled, "Expected identity code to be displayed.");
            Assert.True(_codes.TryDequeue(out var code));
            return code;
        }
    }

    private sealed class FakeClipboardSource : IClipboardSource
    {
        public int BlockCalls { get; private set; }

        public void Block() => BlockCalls++;

        public void Unblock()
        {
        }

        public void Dispose()
        {
        }
    }
}

[CollectionDefinition("Agent command pipe", DisableParallelization = true)]
public sealed class AgentCommandPipeCollection;
