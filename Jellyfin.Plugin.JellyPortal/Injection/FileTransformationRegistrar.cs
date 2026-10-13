using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellyPortal.Injection;

/// <summary>What the settings page can say about the injection.</summary>
public sealed class InjectionStatus
{
    /// <summary>Gets or sets how registering with File Transformation went.</summary>
    public RegistrationResult Result { get; set; } = RegistrationResult.Pending;
}

/// <summary>Registers with File Transformation once, when the server has started and every plugin is loaded.</summary>
public sealed class FileTransformationRegistrar : IHostedService
{
    private readonly FileTransformationBridge _bridge;
    private readonly InjectionStatus _status;

    /// <summary>Initializes a new instance of the <see cref="FileTransformationRegistrar"/> class.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <param name="status">Where the outcome is kept.</param>
    public FileTransformationRegistrar(FileTransformationBridge bridge, InjectionStatus status)
    {
        _bridge = bridge;
        _status = status;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _status.Result = _bridge.Register(FileTransformationBridge.AllLoadedAssemblies());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
