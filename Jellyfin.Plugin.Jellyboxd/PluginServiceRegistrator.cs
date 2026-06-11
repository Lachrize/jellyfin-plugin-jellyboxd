using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
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
        // In-memory widget injection: works even when the web folder is read-only
        // (containerized Jellyfin). Complements WebInjectionService's file write.
        serviceCollection.AddSingleton<IStartupFilter, WidgetInjectionStartupFilter>();
        // JellyboxdLinkService was the single-user pairing loop; in the multi-user
        // model the app uses one shared server key + jellyfinUserId routing, so it
        // is no longer registered.
    }
}
