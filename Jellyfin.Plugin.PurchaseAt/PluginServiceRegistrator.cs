using Jellyfin.Plugin.PurchaseAt.MusicBrainz;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PurchaseAt;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PurchaseLinkService>();
        serviceCollection.AddHostedService<PurchaseLinkWorker>();
    }
}
