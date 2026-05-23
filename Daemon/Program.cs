using Daemon;
using Daemon.Platform.Windows;
using Daemon.ServerConnection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "oeims";
});

builder.Services.AddWindowsPlatform();

builder.Services.AddMonitors();
builder.Services.AddMitigators();

var serverConfig = builder.Configuration
    .GetSection("Server")
    .Get<ServerConfig>() ?? new ServerConfig();

builder.Services.AddSingleton(serverConfig);
builder.Services.AddSingleton<DaemonWebSocketClient>();
builder.Services.AddHttpClient<HeartbeatSender>();

builder.Services.AddHostedService<Worker>();

if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog();

var host = builder.Build();
host.Run();
