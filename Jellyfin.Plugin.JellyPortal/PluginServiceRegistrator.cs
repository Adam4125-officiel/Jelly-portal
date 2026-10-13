using Jellyfin.Plugin.JellyPortal.Injection;
using Jellyfin.Plugin.JellyPortal.Portal;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.JellyPortal;

/// <summary>Registers this plugin's services with the server.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<IPortalClient, PortalClient>();
        serviceCollection.AddSingleton<PortalCache>();
        serviceCollection.AddSingleton<ISettingsProvider, PluginSettingsProvider>();
        serviceCollection.AddSingleton<SummaryService>();
        serviceCollection.AddSingleton<InjectionStatus>();
        serviceCollection.AddSingleton<FileTransformationBridge>();
        serviceCollection.AddHostedService<FileTransformationRegistrar>();
    }
}
