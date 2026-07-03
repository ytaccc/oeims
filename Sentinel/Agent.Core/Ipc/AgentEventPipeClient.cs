using System.IO.Pipes;
using System.Text.Json;
using Contracts;
using Contracts.Ipc;
using Microsoft.Extensions.Logging;

namespace OEIMS.Sentinel.Agent.Ipc;

/// <summary>
/// Sends Agent-side messages to the Sentinel Service through the event pipe.
/// </summary>
/// <remarks>
/// Direction: Sentinel Agent -> Sentinel Service.
/// <para>
/// This pipe carries heartbeats and Agent monitor events, such as focus changes detected in the student session.
/// </para>
/// </remarks>
/// <param name="logger">Logger used for connection failures and diagnostics.</param>
internal sealed class AgentEventPipeClient(
    ILogger<AgentEventPipeClient> logger
) : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sends a heartbeat to prove the Agent process is still alive.
    /// </summary>
    /// <param name="ct">Cancellation token used if the Service is stopping the Agent flow.</param>
    public async Task SendHeartbeatAsync(CancellationToken ct)
    {
        await SendAsync(AgentPipeMessage.Heartbeat(), ct);
    }

    /// <summary>
    /// Sends an Agent monitor event to the Service.
    /// </summary>
    /// <param name="e">Monitor event detected by an Agent-side monitor.</param>
    /// <param name="ct">Cancellation token used to stop the send operation.</param>
    public async Task SendEventAsync(MonitorEvent e, CancellationToken ct)
    {
        await SendAsync(AgentPipeMessage.FromEvent(e), ct);
    }

    /// <summary>
    /// Serializes and writes one message to the pipe.
    /// </summary>
    /// <param name="message">Message to send.</param>
    /// <param name="ct">Cancellation token used to stop the send operation.</param>
    private async Task SendAsync(AgentPipeMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);

        try
        {
            await ConnectPipeAsync(ct);

            var json = JsonSerializer.Serialize(message, JsonOptions);

            await _writer!.WriteLineAsync(json);
            await _writer.FlushAsync(ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Agent pipe disconnected.");

            DisposeConnection();
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Agent pipe connection timed out.");

            DisposeConnection();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Opens the pipe connection if it is not already connected.
    /// </summary>
    /// <param name="ct">Cancellation token used to stop connection waiting.</param>
    private async Task ConnectPipeAsync(CancellationToken ct)
    {
        if (_pipe?.IsConnected == true && _writer is not null)
            return;

        DisposeConnection();

        _pipe = new NamedPipeClientStream(
            ".",
            PipeNames.AgentEvents,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        logger.LogInformation("Connecting to Service pipe...");

        await _pipe.ConnectAsync(timeout: 2_000, ct);

        _writer = new StreamWriter(_pipe)
        {
            AutoFlush = true
        };

        logger.LogInformation("Connected to Service pipe.");
    }

    /// <summary>
    /// Closes only the active pipe connection and clears cached connection state.
    /// </summary>
    /// <remarks>
    /// The client remains usable after this. A later send will open a new pipe connection.
    /// </remarks>
    private void DisposeConnection()
    {
        _writer?.Dispose();
        _pipe?.Dispose();

        _writer = null;
        _pipe = null;
    }

    /// <summary>
    /// Shuts down the client after any active write finishes.
    /// </summary>
    /// <remarks>
    /// This is final cleanup for the whole client, including the synchronization primitive used to serialize writes.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await _writeLock.WaitAsync();

        try
        {
            DisposeConnection();
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }
}