using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using OEIMS.Sentinel.Service.Connections.Agent;
using OEIMS.Sentinel.Service.Connections.Server;
using OEIMS.Sentinel.Service.Connections.WebClient;
using OEIMS.Sentinel.Service.Mitigators;
using OEIMS.Sentinel.Service.Monitors;
using OEIMS.Sentinel.Service.Platform.Windows;

namespace OEIMS.Sentinel.Service;

internal static class ServiceCollectionExtensions
{
    internal static WebApplicationBuilder AddExamMonitoringService(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        if (environment.IsProduction())
        {
            services.AddWindowsService(options =>
            {
                options.ServiceName = "oeims";
            });

            services.AddSingleton<SingleInstanceGuard>();
            builder.Logging.AddEventLog();
        }

        services.AddWindowsPlatform();

        services.AddSingleton<AgentEventPipeServer>();
        services.AddSingleton<AgentCommandPipeClient>();

        services.AddMonitors();
        services.AddMitigators();

        services.AddSingleton(GetApplicationConfig(configuration));
        services.AddSingleton(GetServerConfig(configuration));

        services.AddSingleton<ServerSession>();
        services.AddHttpClient<ServerApi>();

        services.AddSingleton<WebSocketClient>();
        services.AddHttpClient<HeartbeatSender>();

        services.AddCors();
        builder.AddLoopbackApi();

        services.AddHostedService<Worker>();

        return builder;
    }

    internal static WebApplication UseExamMonitoringService(
        this WebApplication app)
    {
        if (app.Environment.IsProduction())
            app.Services.GetRequiredService<SingleInstanceGuard>();

        app.UseCors();
        app.MapLoopbackApi();

        return app;
    }

    private static void AddMonitors(this IServiceCollection services)
    {
        services.AddSingleton<IMonitor, ProcessMonitor>();
        services.AddSingleton<IMonitor, NetworkMonitor>();
    }

    private static void AddMitigators(this IServiceCollection services) =>
        services.AddSingleton<IMitigator, ProcessBlocker>();

    private static void AddLoopbackApi(this WebApplicationBuilder builder)
    {
        var port = builder.Configuration.GetValue<int>("Loopback:Port");

        if (port <= 0)
            throw new InvalidOperationException(
                "Missing Loopback:Port configuration.");

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    }

    private static ApplicationConfig GetApplicationConfig(
        ConfigurationManager configuration)
    {
        return configuration
            .GetSection("Application")
            .Get<ApplicationConfig>() ?? new ApplicationConfig();
    }

    private static ServerConfig GetServerConfig(ConfigurationManager configuration)
    {
        return configuration
            .GetSection("Server")
            .Get<ServerConfig>() ?? new ServerConfig();
    }
}