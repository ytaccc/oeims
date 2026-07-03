using Contracts;
using Contracts.Ipc;
using OEIMS.Sentinel.Service.Connections.Agent;
using OEIMS.Sentinel.Service.Connections.Server;
using OEIMS.Sentinel.Service.Monitors;

namespace OEIMS.Sentinel.Service
{
    /// <summary>
    /// Main background service that validates pre-exam state before and then runs monitoring and mitigating components.
    /// </summary>
    /// <remarks>
    /// This is the Service orchestration point. Its only reponsability is to wire monitors,
    /// mitigators, Agent IPC, heartbeat, and server communication together.
    /// </remarks>
    /// <param name="monitors">All local monitors registered by dependency injection.</param>
    /// <param name="mitigators">All mitigators registered by dependency injection.</param>
    /// <param name="serverConfig">Server connection settings and feature flags.</param>
    /// <param name="wsClient">WebSocket client used to send Service and Agent monitor events to the backend.</param>
    /// <param name="agentPipeServer">Pipe server used to receive Agent messages.</param>
    /// <param name="heartbeatSender">Periodic Service heartbeat sender.</param>
    /// <param name="logger">Worker logger.</param>
    internal class Worker(
        IEnumerable<IMonitor> monitors,
        IEnumerable<IMitigator> mitigators,
        ServerConfig serverConfig,
        WebSocketClient wsClient,
        AgentEventPipeServer agentPipeServer,
        HeartbeatSender heartbeatSender,
        ILogger<Worker> logger
        ) : BackgroundService
    {
        private readonly IReadOnlyList<IMonitor> _monitors = monitors.ToList();
        private readonly IReadOnlyList<IMitigator> _mitigators = mitigators.ToList();

        /// <summary>
        /// Starts Sentinel Service.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token triggered by Windows Service shutdown.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var mitigator in _mitigators)
            {
                mitigator.Apply();
                logger.LogInformation("Mitigator applied: {name}", mitigator.Name);
            }

            var networkMonitor = _monitors.OfType<NetworkMonitor>().Single();
            await networkMonitor.StartPreExamAsync(OnLocalEvent, stoppingToken);

            var tasks = new List<Task>();

            if (serverConfig.ShouldConnect)
            {
                logger.LogInformation("Connecting to server...");

                tasks.Add(RunComponentAsync(
                    "WebSocket client",
                    wsClient.StartAsync,
                    stoppingToken));

                tasks.Add(RunComponentAsync(
                    "Agent pipe server",
                    ct => agentPipeServer.StartAsync(OnAgentMessage, ct),
                    stoppingToken));

                tasks.Add(RunComponentAsync(
                    "Heartbeat sender",
                    heartbeatSender.StartAsync,
                    stoppingToken));
            }
            else if (!serverConfig.Enabled)
            {
                logger.LogWarning(
                    "Disabled server connection by configuration.");
            }
            else
            {
                logger.LogWarning(
                    "No server config found so running without server connection. " +
                    "Set Server:ApiBaseUrl, Server:RealtimeBaseUrl, Server:Token and Server:ParticipantId in appsettings.json.");
            }

            tasks.AddRange(_monitors.Select(monitor => RunComponentAsync(
                monitor.Name,
                ct => monitor.StartAsync(OnEvent, ct),
                stoppingToken)));

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Runs one long-lived component with consistent lifecycle logging and failure handling.
        /// </summary>
        /// <param name="name">Component name shown in logs.</param>
        /// <param name="runAsync">Function that starts the component.</param>
        /// <param name="ct">Cancellation token used to stop the component.</param>
        private async Task RunComponentAsync(
            string name,
            Func<CancellationToken, Task> runAsync,
            CancellationToken ct)
        {
            try
            {
                logger.LogInformation("Starting component: {name}", name);

                await runAsync(ct);

                logger.LogInformation("Component stopped: {name}", name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation("Component cancelled: {name}", name);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Component failed: {name}", name);
                throw;
            }
        }

        /// <summary>
        /// Handles local events that should be logged but not sent to the backend.
        /// </summary>
        /// <param name="e">Monitor event produced before the exam connection starts.</param>
        private Task OnLocalEvent(MonitorEvent e)
        {
            Log(e);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles monitor events that should be logged and sent to the backend when connected.
        /// </summary>
        /// <param name="e">Monitor event from the Service or Agent.</param>
        private async Task OnEvent(MonitorEvent e)
        {
            Log(e);

            if (serverConfig.ShouldConnect)
                await wsClient.SendEventAsync(e, CancellationToken.None);
        }

        /// <summary>
        /// Handles messages received from the Agent event pipe.
        /// </summary>
        /// <param name="message">Heartbeat or monitor event sent by the Agent.</param>
        private async Task OnAgentMessage(AgentPipeMessage message)
        {
            switch (message.Type)
            {
                case AgentMessageType.Heartbeat:
                    logger.LogInformation("Agent heartbeat received at {sentAt}", message.SentAt);
                    break;

                case AgentMessageType.Event:
                    if (message.Event is null)
                    {
                        logger.LogWarning("Agent event message ignored because Event was null.");
                        return;
                    }

                    await OnEvent(message.Event);
                    break;

                default:
                    logger.LogWarning("Unknown Agent pipe message type: {type}", message.Type);
                    break;
            }
        }

        /// <summary>
        /// Logs one monitor event to the correct log level.
        /// </summary>
        /// <param name="e">Event to log.</param>
        private void Log(MonitorEvent e)
        {
            switch (e.Severity)
            {
                case Severity.Info:
                    logger.LogInformation("[{monitor}] {message}", e.MonitorName, e.Message);
                    break;

                case Severity.Warning:
                    logger.LogWarning("[{monitor}] {message}", e.MonitorName, e.Message);
                    break;

                case Severity.Critical:
                    logger.LogCritical("[{monitor}] {message}", e.MonitorName, e.Message);
                    break;

                default:
                    logger.LogWarning("[{monitor}] {message}", e.MonitorName, e.Message);
                    break;
            }
        }

        /// <summary>
        /// Stops the worker and releases the WebSocket client.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            await wsClient.DisposeAsync();
        }

        /// <summary>
        /// Disposes all mitigators and monitors owned by the worker.
        /// </summary>
        public override void Dispose()
        {
            foreach (var mitigator in _mitigators)
                mitigator.Dispose();

            foreach (var monitor in _monitors)
                monitor.Dispose();

            base.Dispose();
        }
}
}