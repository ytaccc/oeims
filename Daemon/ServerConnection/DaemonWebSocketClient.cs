using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Daemon.Domain;

namespace Daemon.ServerConnection;

internal sealed class DaemonWebSocketClient(
    ServerConfig config,
    ILogger<DaemonWebSocketClient> logger) : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Uri _uri = new(
        $"{config.BaseUrl.TrimEnd('/').Replace("http://", "ws://").Replace("https://", "wss://")}/ws/daemon/{config.ParticipantId}?token={Uri.EscapeDataString(config.Token)}");

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _ws.Options.SetRequestHeader("Authorization", $"Bearer {config.Token}");

                await _ws.ConnectAsync(_uri, ct);
                logger.LogInformation("[ServerConnection] Connected to {Uri}", _uri);

                var buffer = new byte[256];
                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.LogWarning("[ServerConnection] Server closed the connection: {Reason}",
                            result.CloseStatusDescription);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ServerConnection] Disconnected — retrying in 5 s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        await CloseAsync();
    }

    public async Task SendEventAsync(MonitorEvent e, CancellationToken ct = default)
    {
        if (_ws?.State != WebSocketState.Open)
        {
            logger.LogDebug("[ServerConnection] Socket not open — event dropped: [{Monitor}] {Message}",
                e.MonitorName, e.Message);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            monitorName = e.MonitorName,
            message     = e.Message,
            severity    = e.Severity.ToString()
        });

        var bytes = Encoding.UTF8.GetBytes(payload);

        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task CloseAsync()
    {
        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Daemon shutting down",
                    CancellationToken.None);
            }
            catch { /* best-effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
