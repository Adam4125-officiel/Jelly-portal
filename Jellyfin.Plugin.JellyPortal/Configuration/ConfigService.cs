using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyPortal.Injection;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Configuration;

/// <summary>The allowed range of a numeric setting, sent to the settings page so it never hard-codes them.</summary>
/// <param name="Default">The default.</param>
/// <param name="Min">The lowest accepted value.</param>
/// <param name="Max">The highest accepted value.</param>
public sealed record SettingRange(
    [property: JsonPropertyName("default")] int Default,
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max);

/// <summary>What the settings page is shown. The API key is never in it: only whether one is saved.</summary>
public sealed class ConfigView
{
    /// <summary>Gets the address the portal is asked with.</summary>
    [JsonPropertyName("portalUrl")]
    public string PortalUrl { get; init; } = string.Empty;

    /// <summary>Gets the address of the panel's link.</summary>
    [JsonPropertyName("publicUrl")]
    public string PublicUrl { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether an API key is saved.</summary>
    [JsonPropertyName("hasApiKey")]
    public bool HasApiKey { get; init; }

    /// <summary>Gets the polling interval of the browsers, in seconds.</summary>
    [JsonPropertyName("pollSeconds")]
    public int PollSeconds { get; init; }

    /// <summary>Gets the lifetime of a reading of the portal, in seconds.</summary>
    [JsonPropertyName("cacheSeconds")]
    public int CacheSeconds { get; init; }

    /// <summary>Gets the "maintenance coming up" window, in hours.</summary>
    [JsonPropertyName("soonWindowHours")]
    public int SoonWindowHours { get; init; }

    /// <summary>Gets the priority services, one per line.</summary>
    [JsonPropertyName("priorityServices")]
    public string PriorityServices { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether only administrators see the icon.</summary>
    [JsonPropertyName("adminsOnly")]
    public bool AdminsOnly { get; init; }

    /// <summary>Gets the language setting: en, fr or auto.</summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = Languages.Default;

    /// <summary>Gets how registering with File Transformation went: pending, registered, not_installed, incompatible or failed.</summary>
    [JsonPropertyName("fileTransformation")]
    public string FileTransformation { get; init; } = "pending";

    /// <summary>Gets the ranges of the numeric settings.</summary>
    [JsonPropertyName("ranges")]
    public IReadOnlyDictionary<string, SettingRange> Ranges { get; init; } = new Dictionary<string, SettingRange>();
}

/// <summary>What the settings page submits. A null means "leave it as it is".</summary>
public sealed class ConfigUpdate
{
    /// <summary>Gets or sets the address the portal is asked with.</summary>
    [JsonPropertyName("portalUrl")]
    public string? PortalUrl { get; set; }

    /// <summary>Gets or sets the address of the panel's link. Null leaves it as it is; blank removes the link.</summary>
    [JsonPropertyName("publicUrl")]
    public string? PublicUrl { get; set; }

    /// <summary>Gets or sets a new API key. Null or blank keeps the saved one, because the page never has it to show.</summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether to erase the saved key.</summary>
    [JsonPropertyName("clearApiKey")]
    public bool ClearApiKey { get; set; }

    /// <summary>Gets or sets the polling interval, in seconds.</summary>
    [JsonPropertyName("pollSeconds")]
    public int? PollSeconds { get; set; }

    /// <summary>Gets or sets the cache lifetime, in seconds.</summary>
    [JsonPropertyName("cacheSeconds")]
    public int? CacheSeconds { get; set; }

    /// <summary>Gets or sets the "coming up" window, in hours.</summary>
    [JsonPropertyName("soonWindowHours")]
    public int? SoonWindowHours { get; set; }

    /// <summary>Gets or sets the priority services, one per line.</summary>
    [JsonPropertyName("priorityServices")]
    public string? PriorityServices { get; set; }

    /// <summary>Gets or sets the visibility.</summary>
    [JsonPropertyName("adminsOnly")]
    public bool? AdminsOnly { get; set; }

    /// <summary>Gets or sets the language setting: en, fr or auto. Null leaves it as it is; anything else is refused.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>The outcome of applying an update.</summary>
/// <param name="Configuration">The new configuration, or null when the update was refused.</param>
/// <param name="Error">Why it was refused, as a code (invalid_portal_url, invalid_public_url, invalid_language): the settings page words it.</param>
public sealed record ConfigApplyResult(PluginConfiguration? Configuration, string? Error);

/// <summary>Builds what the settings page shows and applies what it submits. No Jellyfin service involved.</summary>
public static class ConfigService
{
    /// <summary>Describes a configuration for the settings page.</summary>
    /// <param name="configuration">The stored configuration.</param>
    /// <param name="injection">How registering with File Transformation went.</param>
    /// <returns>The view, with the numbers already clamped.</returns>
    public static ConfigView View(PluginConfiguration configuration, RegistrationResult injection)
    {
        var settings = configuration.ToSettings();
        return new ConfigView
        {
            PortalUrl = settings.PortalUrl,
            PublicUrl = settings.PublicUrl,
            HasApiKey = settings.ApiKey.Length > 0,
            PollSeconds = settings.PollSeconds,
            CacheSeconds = settings.CacheSeconds,
            SoonWindowHours = settings.SoonWindowHours,
            PriorityServices = string.Join('\n', settings.PriorityServices),
            AdminsOnly = settings.AdminsOnly,
            Language = settings.Language,
            FileTransformation = injection switch
            {
                RegistrationResult.Registered => "registered",
                RegistrationResult.NotInstalled => "not_installed",
                RegistrationResult.Incompatible => "incompatible",
                RegistrationResult.Failed => "failed",
                _ => "pending",
            },
            Ranges = new Dictionary<string, SettingRange>
            {
                ["pollSeconds"] = new(JellyPortalSettings.PollRange.Default, JellyPortalSettings.PollRange.Min, JellyPortalSettings.PollRange.Max),
                ["cacheSeconds"] = new(JellyPortalSettings.CacheRange.Default, JellyPortalSettings.CacheRange.Min, JellyPortalSettings.CacheRange.Max),
                ["soonWindowHours"] = new(24, 1, (int)SummaryOptions.MaxSoonWindow.TotalHours),
            },
        };
    }

    /// <summary>Applies a submission to a configuration.</summary>
    /// <param name="current">The stored configuration, which is not modified.</param>
    /// <param name="update">What the page submitted.</param>
    /// <returns>The new configuration, or an error to show. Numbers are clamped into their range, not refused.</returns>
    public static ConfigApplyResult Apply(PluginConfiguration current, ConfigUpdate update)
    {
        var url = (update.PortalUrl ?? current.PortalUrl).Trim();
        if (url.Length > 0 && !PortalClient.TryBuildUri(url, out _))
        {
            return new ConfigApplyResult(null, "invalid_portal_url");
        }

        var publicUrl = (update.PublicUrl ?? current.PublicUrl).Trim();
        if (publicUrl.Length > 0 && !PortalClient.TryBuildUri(publicUrl, out _))
        {
            return new ConfigApplyResult(null, "invalid_public_url");
        }

        if (update.Language is not null && !Languages.IsValid(update.Language))
        {
            return new ConfigApplyResult(null, "invalid_language");
        }

        var key = update.ClearApiKey ? string.Empty
            : string.IsNullOrWhiteSpace(update.ApiKey) ? current.ApiKey
            : update.ApiKey.Trim();

        var clamped = JellyPortalSettings.From(
            url,
            publicUrl,
            key,
            update.PollSeconds ?? current.PollSeconds,
            update.CacheSeconds ?? current.CacheSeconds,
            update.SoonWindowHours ?? current.SoonWindowHours,
            update.PriorityServices ?? current.PriorityServices,
            update.AdminsOnly ?? current.AdminsOnly,
            update.Language ?? current.Language);

        return new ConfigApplyResult(
            new PluginConfiguration
            {
                PortalUrl = clamped.PortalUrl,
                PublicUrl = clamped.PublicUrl,
                ApiKey = clamped.ApiKey,
                PollSeconds = clamped.PollSeconds,
                CacheSeconds = clamped.CacheSeconds,
                SoonWindowHours = clamped.SoonWindowHours,
                PriorityServices = string.Join('\n', clamped.PriorityServices),
                AdminsOnly = clamped.AdminsOnly,
                Language = clamped.Language,
            },
            null);
    }
}
