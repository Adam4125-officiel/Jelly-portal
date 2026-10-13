using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyPortal.Injection;

/// <summary>How registering with File Transformation went.</summary>
public enum RegistrationResult
{
    /// <summary>Not tried yet.</summary>
    Pending,

    /// <summary>File Transformation will add the script to index.html.</summary>
    Registered,

    /// <summary>File Transformation is not installed (or not loaded): the plugin runs, but no icon appears.</summary>
    NotInstalled,

    /// <summary>File Transformation is there but does not offer the registration this plugin knows.</summary>
    Incompatible,

    /// <summary>File Transformation threw while registering.</summary>
    Failed,
}

/// <summary>
/// Registers the <c>index.html</c> transformation with the File Transformation plugin. That plugin cannot be
/// referenced as a library (plugins load into separate contexts), so everything here is reflection, as its
/// README prescribes. It must never throw: Jellyfin has to start whatever the state of the other plugin.
/// </summary>
public sealed class FileTransformationBridge
{
    private const string InterfaceType = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private const string RegisterMethod = "RegisterTransformation";

    private readonly ILogger<FileTransformationBridge> _logger;

    /// <summary>Initializes a new instance of the <see cref="FileTransformationBridge"/> class.</summary>
    /// <param name="logger">The logger.</param>
    public FileTransformationBridge(ILogger<FileTransformationBridge> logger) => _logger = logger;

    /// <summary>Gets every assembly of every load context, which is where File Transformation is looked for.</summary>
    public static IEnumerable<Assembly> AllLoadedAssemblies() =>
        AssemblyLoadContext.All.SelectMany(context => context.Assemblies);

    /// <summary>Registers the transformation.</summary>
    /// <param name="candidates">Where to look for File Transformation.</param>
    /// <returns>How it went. Never throws.</returns>
    public RegistrationResult Register(IEnumerable<Assembly> candidates)
    {
        try
        {
            var pluginInterface = Find(candidates);
            if (pluginInterface is null)
            {
                _logger.LogWarning(
                    "[JellyPortal] The File Transformation plugin is not installed (or not loaded), so the status icon cannot be added to the Jellyfin header. " +
                    "Install File Transformation 3.x from its repository (https://www.iamparadox.dev/jellyfin/plugins/manifest.json) and restart Jellyfin. Everything else keeps working.");
                return RegistrationResult.NotInstalled;
            }

            var register = pluginInterface.GetMethod(RegisterMethod, BindingFlags.Public | BindingFlags.Static);
            var parameters = register?.GetParameters();
            if (register is null || parameters is not { Length: 1 })
            {
                _logger.LogError("[JellyPortal] File Transformation is installed but has no static {Method}(payload) to register with: this version is not supported. The status icon cannot be added.", RegisterMethod);
                return RegistrationResult.Incompatible;
            }

            // File Transformation takes a Newtonsoft JObject. Building one of ITS type from JSON, through its own
            // Parse method, needs no compile-time reference to Newtonsoft and no assumption about which copy of it
            // File Transformation was loaded with.
            var payloadType = parameters[0].ParameterType;
            var parse = payloadType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (parse is null)
            {
                _logger.LogError("[JellyPortal] File Transformation's registration takes a {Type}, which this plugin cannot build. The status icon cannot be added.", payloadType.FullName);
                return RegistrationResult.Incompatible;
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["id"] = IndexHtmlTransformation.TransformationId,
                ["fileNamePattern"] = IndexHtmlTransformation.FilePattern,
                ["callbackAssembly"] = typeof(IndexHtmlTransformation).Assembly.FullName!,
                ["callbackClass"] = typeof(IndexHtmlTransformation).FullName!,
                ["callbackMethod"] = nameof(IndexHtmlTransformation.Apply),
            });
            register.Invoke(null, [parse.Invoke(null, [json])]);

            _logger.LogInformation("[JellyPortal] Registered the header script with File Transformation for {File}.", IndexHtmlTransformation.FilePattern);
            return RegistrationResult.Registered;
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogError(ex.InnerException ?? ex, "[JellyPortal] File Transformation refused the registration. The status icon cannot be added.");
            return RegistrationResult.Failed;
        }
#pragma warning disable CA1031 // Starting Jellyfin must never depend on another plugin behaving.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "[JellyPortal] Could not register with File Transformation. The status icon cannot be added.");
            return RegistrationResult.Failed;
        }
    }

    private static Type? Find(IEnumerable<Assembly> candidates)
    {
        foreach (var assembly in candidates)
        {
            try
            {
                if (assembly.GetType(InterfaceType, throwOnError: false) is { } type)
                {
                    return type;
                }
            }
#pragma warning disable CA1031 // An assembly that cannot be inspected is simply not File Transformation.
            catch (Exception)
#pragma warning restore CA1031
            {
                // skip it
            }
        }

        return null;
    }
}
