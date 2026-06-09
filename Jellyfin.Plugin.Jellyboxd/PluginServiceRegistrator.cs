using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Registers the plugin's background services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<JellyboxdSyncService>();
        serviceCollection.AddHostedService<WebInjectionService>();
        serviceCollection.AddHostedService<OutboundPullService>();
        serviceCollection.AddHostedService<JellyboxdLinkService>();
    }
}
