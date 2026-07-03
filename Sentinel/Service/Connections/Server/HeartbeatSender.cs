using System.Net;
using System.Net.Http.Headers;

namespace OEIMS.Sentinel.Service.Connections.Server;

/// <summary>
/// Calls the Server periodically to report that the Sentinel Service is still alive.
/// </summary>
/// <remarks>
/// Communication:
/// <code>
/// Sentinel Service -> Server
/// </code>
/// </remarks>
internal sealed class HeartbeatSender(
    ServerConfig config,
    ServerSession serverSession,
    HttpClient httpClient,
    ILogger<HeartbeatSender> logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly string _apiBaseUrl = config.ApiBaseUrl.TrimEnd('/');

    public async Task StartAsync(CancellationToken ct)
    {
        logger.LogInformation("[Heartbeat] Waiting for Sentinel authorization");

        while (!ct.IsCancellationRequested)
        {
            var authorization = await serverSession.WaitUntilAuthorizedAsync(ct);

            if (await SendAsync(authorization, ct))
            {
                try { await Task.Delay(Interval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }


    /// <summary>
    /// Sends one heartbeat request with the current Sentinel authorization token.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the current authorization is valid and the heartbeat loop should continue normally.
    /// <para/>
    /// <c>false</c> when the current authorization was cleared or the service is stopping.
    /// </returns>
    private async Task<bool> SendAsync(
    ServerAuthorization authorization,
    CancellationToken ct)
    {
        var url = $"{_apiBaseUrl}/participants/{authorization.ParticipantId}/heartbeat";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authorization.Token);

            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return true;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                serverSession.Clear();
                logger.LogWarning("[Heartbeat] Authorization rejected by server");
                return false;
            }

            logger.LogDebug("[Heartbeat] Server returned transient status {StatusCode}", (int)response.StatusCode);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Heartbeat] Server temporarily unreachable");
            return true;
        }
    }
}