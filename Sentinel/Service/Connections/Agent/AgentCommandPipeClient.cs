using Contracts.Ipc;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Sends commands from the Sentinel Service to the Sentinel Agent.
/// </summary>
/// <remarks>
/// Direction: Sentinel Service -> Sentinel Agent.
/// <para>
/// Each command is sent as one newline-delimited JSON message. A new pipe connection is opened per command,
/// which keeps the command protocol simple and avoids stale client state.
/// </para>
/// </remarks>
/// <param name="logger">Logger used when the Agent is unavailable or the pipe disconnects.</param>
internal sealed class AgentCommandPipeClient(
    ILogger<AgentCommandPipeClient> logger) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Sends one command to the Agent command pipe.
    /// </summary>
    /// <param name="command">Command payload to serialize and send.</param>
    /// <param name="ct">Cancellation token used to stop connection or write waiting.</param>
    /// <returns>A task that completes when the command is written or safely skipped because the Agent is unavailable.</returns>
    public async Task SendAsync(AgentCommand command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeNames.AgentCommands,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout: 2_000, ct);

            await using var writer = new StreamWriter(pipe);

            var json = JsonSerializer.Serialize(
                command,
                command.GetType(),
                JsonOptions);

            await writer.WriteLineAsync(json.AsMemory(), ct);
            await writer.FlushAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Agent unavailable; command not sent: {CommandType}", command.Type);
        }
        catch (IOException ex)
        {
            logger.LogWarning("Agent command pipe disconnected.");
            logger.LogDebug(ex, "Agent command pipe write failed.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Releases the write lock used to serialize command sends.
    /// </summary>
    public void Dispose()
    {
        _writeLock.Dispose();
    }
}