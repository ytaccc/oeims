using System.IO.Pipes;
using System.Text.Json;
using Contracts.Ipc;

namespace OEIMS.Sentinel.Service.Connections.Agent;

/// <summary>
/// Receives heartbeats and monitor events sent by the Sentinel Agent.
/// </summary>
/// <remarks>
/// Direction: Sentinel Agent -> Sentinel Service.
/// <para>
/// The Service owns this pipe server because the Service is long-lived. The Agent connects as a client and sends
/// newline-delimited JSON messages using <see cref="AgentPipeMessage" />.
/// </para>
/// </remarks>
/// <param name="logger">Logger used for connection lifecycle and invalid messages.</param>
internal sealed class AgentEventPipeServer(
    ILogger<AgentEventPipeServer> logger
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Starts the event pipe server until cancellation.
    /// </summary>
    /// <param name="onMessage">
    /// Callback invoked for each valid <see cref="AgentPipeMessage" /> received from the Agent.
    /// </param>
    /// <param name="ct">Cancellation token used when the Service stops.</param>
    /// <returns>A task that completes when the server stops.</returns>
    public async Task StartAsync(
        Func<AgentPipeMessage, Task> onMessage,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeNames.AgentEvents,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            logger.LogInformation("Waiting for Agent pipe connection...");

            try
            {
                await pipe.WaitForConnectionAsync(ct);

                logger.LogInformation("Agent connected.");

                await ReadMessagesAsync(pipe, onMessage, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation("Agent pipe server cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent pipe connection failed.");
            }
        }
    }

    /// <summary>
    /// Reads newline-delimited JSON messages from one connected Agent.
    /// </summary>
    /// <param name="pipe">Connected named pipe stream.</param>
    /// <param name="onMessage">Callback invoked for valid messages.</param>
    /// <param name="ct">Cancellation token used when the Service stops.</param>
    private async Task ReadMessagesAsync(
        PipeStream pipe,
        Func<AgentPipeMessage, Task> onMessage,
        CancellationToken ct)
    {
        using var reader = new StreamReader(pipe);

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync(ct);

            if (line is null)
                break;

            AgentPipeMessage? message;

            try
            {
                message = JsonSerializer.Deserialize<AgentPipeMessage>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Invalid Agent pipe message ignored.");
                continue;
            }

            if (message is null)
                continue;

            await onMessage(message);
        }

        logger.LogWarning("Agent disconnected.");
    }
}