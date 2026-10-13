using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyPortal.Tests;

public class PluginIdentityTests
{
    private static string BuildYamlValue(string key)
    {
        var match = Regex.Match(RepoFiles.Read("build.yaml"), "^" + key + @":\s*""([^""]*)""", RegexOptions.Multiline);
        Assert.True(match.Success, $"build.yaml has no `{key}: \"...\"` line");
        return match.Groups[1].Value;
    }

    [Fact]
    public void PluginIdIsAValidGuid()
    {
        Assert.True(Guid.TryParse(PluginIdentity.Id, out var id));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void BuildYamlGuidMatchesThePluginId()
    {
        // The GUID must never change: Jellyfin would treat every version as a new plugin.
        Assert.Equal(PluginIdentity.Id, BuildYamlValue("guid"));
    }

    [Fact]
    public void BuildYamlNameMatchesThePluginName()
    {
        Assert.Equal(PluginIdentity.Name, BuildYamlValue("name"));
    }

    [Fact]
    public void BuildYamlCarriesNoRealVersion()
    {
        // The version comes only from the git tag, injected at build time.
        Assert.Equal("0.0.0.0", BuildYamlValue("version"));
    }

    [Fact]
    public void BuildYamlFrameworkMatchesTheProjects()
    {
        var framework = BuildYamlValue("framework");
        Assert.Contains($"<TargetFramework>{framework}</TargetFramework>", RepoFiles.Read("Jellyfin.Plugin.JellyPortal/Jellyfin.Plugin.JellyPortal.csproj"));
    }

    [Fact]
    public void JellyfinPackagesMatchTheTargetAbi()
    {
        // targetAbi "12.2.0.0" means the plugin is compiled against Jellyfin.Controller/Model 12.2.0.
        var abi = BuildYamlValue("targetAbi");
        Assert.EndsWith(".0", abi);
        var packageVersion = abi[..^2];
        var csproj = RepoFiles.Read("Jellyfin.Plugin.JellyPortal/Jellyfin.Plugin.JellyPortal.csproj");
        Assert.Contains($"Include=\"Jellyfin.Controller\" Version=\"{packageVersion}\"", csproj);
        Assert.Contains($"Include=\"Jellyfin.Model\" Version=\"{packageVersion}\"", csproj);
    }
}
