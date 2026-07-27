using Contracts;
using Microsoft.Extensions.DependencyInjection;
using OEIMS.Sentinel.Agent.Domain;
using OEIMS.Sentinel.Agent.Ipc;
using OEIMS.Sentinel.Agent.Mitigators;
using OEIMS.Sentinel.Agent.Monitors;
using OEIMS.Sentinel.Agent.Platform.Windows;

namespace OEIMS.Sentinel.Agent;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgent(this IServiceCollection services)
    {
        services.AddSingleton<IActiveWindowSource, WinActiveWindowSource>();
        services.AddSingleton<IClipboardSource, WinClipboardSource>();
        services.AddSingleton<IExamIdentityCodeOverlay, WinExamIdentityCodeOverlay>();

        services.AddSingleton<AgentEventPipeClient>();
        services.AddSingleton<AgentCommandPipeServer>();

        services.AddSingleton<IMonitor, FocusMonitor>();
        services.AddSingleton<ClipboardBlocker>();

        services.AddHostedService<Worker>();

        return services;
    }
}
