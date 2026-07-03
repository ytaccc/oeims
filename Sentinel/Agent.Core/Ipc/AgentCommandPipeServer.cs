using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contracts.Ipc;
using Microsoft.Extensions.Logging;
using OEIMS.Sentinel.Agent.Domain;

namespace OEIMS.Sentinel.Agent.Ipc;

/// <summary>
/// Receives commands sent by the Sentinel Service to the Sentinel Agent.
/// </summary>
/// <remarks>
/// Direction: Sentinel Service -> Sentinel Agent.
/// <para>
/// Commands are newline-delimited JSON messages. Unknown, incomplete, or malformed commands are ignored so a bad
/// message does not kill the Agent command loop.
/// </para>
/// </remarks>
/// <param name="overlay">UI boundary used when the Service asks the Agent to display an exam identity code.</param>
/// <param name="logger">Logger used for malformed commands and pipe diagnostics.</param>
internal sealed class AgentCommandPipeServer(
    IExamIdentityCodeOverlay overlay,
    ILogger<AgentCommandPipeServer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Starts accepting command pipe connections until cancellation.
    /// </summary>
    /// <param name="ct">Cancellation token used when the Agent shuts down.</param>
    /// <returns>A task that completes when command handling stops.</returns>
    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeNames.AgentCommands,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe);

                var json = await reader.ReadLineAsync(ct);

                if (string.IsNullOrWhiteSpace(json))
                    continue;

                await HandleCommandAsync(json, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Agent command pipe disconnected.");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Ignored malformed Agent command.");
            }
        }
    }

    /// <summary>
    /// Reads the command type and dispatches to the matching handler.
    /// </summary>
    /// <param name="json">Raw command JSON read from the pipe.</param>
    /// <param name="ct">Cancellation token used to stop command handling.</param>
    /// <returns>A task that completes after the command is handled or ignored.</returns>
    private Task HandleCommandAsync(string json, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("type", out var type))
            return Task.CompletedTask;

        return type.Deserialize<AgentCommandType>(JsonOptions) switch
        {
            AgentCommandType.ShowExamIdentityCode =>
                HandleShowExamIdentityCodeAsync(json, ct),

            _ => Task.CompletedTask
        };
    }

    private async Task HandleShowExamIdentityCodeAsync(string json, CancellationToken ct)
    {
        var command = JsonSerializer.Deserialize<ShowExamIdentityCodeCommand>(
            json,
            JsonOptions);

        if (command is null || string.IsNullOrWhiteSpace(command.Code))
        {
            logger.LogWarning("Ignored empty exam identity code command.");
            return;
        }

        await overlay.DisplayCodeAsync(command.Code, ct);
    }
}