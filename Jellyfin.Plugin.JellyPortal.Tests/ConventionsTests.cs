using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyPortal.Tests;

/// <summary>Rules that are cheap to check and expensive to rediscover.</summary>
public class ConventionsTests
{
    [Fact]
    public void TheSummaryFolderStaysFreeOfJellyfinAndAspNetCode()
    {
        // Summary/ holds the logic that decides the icon. It is unit-tested without a server, and
        // it stays that way only if nothing in it can reach Jellyfin, ASP.NET or the host's services.
        var folder = Path.Combine(RepoFiles.Root, "Jellyfin.Plugin.JellyPortal", "Summary");
        var forbidden = new Regex(@"\b(MediaBrowser|Jellyfin\.(?!Plugin\.JellyPortal)|Microsoft\.AspNetCore|Microsoft\.Extensions)\b");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var offending = File.ReadLines(file)
                .Select((line, i) => (Line: line, Number: i + 1))
                .Where(x => !x.Line.TrimStart().StartsWith("//", StringComparison.Ordinal) && forbidden.IsMatch(x.Line))
                .ToList();
            if (offending.Count > 0)
            {
                Assert.Fail(
                    $"{Path.GetFileName(file)} line {offending[0].Number} reaches Jellyfin/ASP.NET: `{offending[0].Line.Trim()}`. " +
                    "Summary/ must stay pure; put host-facing code in Portal/, Api/ or Injection/.");
            }
        }
    }

    [Fact]
    public void NoSourceFileNamesAModelOrAVersionInAComment()
    {
        // The model identifiers never belong in the repository (see CLAUDE.md).
        var model = new Regex(@"claude-(opus|sonnet|haiku|fable)|Sonnet \d|Opus \d", RegexOptions.IgnoreCase);
        var sources = Directory.GetFiles(RepoFiles.Root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}refs{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        foreach (var file in sources)
        {
            Assert.False(model.IsMatch(File.ReadAllText(file)), $"{file} mentions a model name");
        }
    }
}
