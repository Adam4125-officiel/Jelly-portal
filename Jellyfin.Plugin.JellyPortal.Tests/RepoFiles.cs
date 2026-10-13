namespace Jellyfin.Plugin.JellyPortal.Tests;

/// <summary>
/// Locates files of the repository from a test run, so tests can check that the files
/// which must agree with each other (build.yaml, the csproj, CLAUDE.md) really do.
/// </summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jellyfin.Plugin.JellyPortal.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
