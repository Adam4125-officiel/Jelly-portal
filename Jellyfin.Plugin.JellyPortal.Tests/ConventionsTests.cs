using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyPortal.Summary;

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

    [Fact]
    public void EveryConsoleCallOfTheClientScriptCarriesThePrefix()
    {
        // The owner reads the browser console to diagnose; a message without the prefix cannot be told from Jellyfin's own.
        var calls = new Regex(@"console\.\w+\(");
        var prefixed = new Regex(@"console\.\w+\(\s*['""`]\[JellyPortal\]");
        var files = Directory.GetFiles(Path.Combine(RepoFiles.Root, "Jellyfin.Plugin.JellyPortal", "Web"), "*.js", SearchOption.AllDirectories)
            .Append(Path.Combine(RepoFiles.Root, "Jellyfin.Plugin.JellyPortal", "Configuration", "configPage.html"))
            .ToArray();
        Assert.True(files.Length >= 2);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.True(
                calls.Matches(text).Count == prefixed.Matches(text).Count,
                $"{Path.GetFileName(file)} has a console call whose first argument does not start with '[JellyPortal]'.");
        }
    }

    private static string ClientScript() => File.ReadAllText(Path.Combine(RepoFiles.Root, "Jellyfin.Plugin.JellyPortal", "Web", "jellyportal.js"));

    private static List<string> KeysOf(string script, string variable)
    {
        var block = Regex.Match(script, @"var " + variable + @" = \{(?<body>.*?)\n    \};", RegexOptions.Singleline);
        Assert.True(block.Success, $"{variable} not found in the client script");
        return Regex.Matches(block.Groups["body"].Value, @"^\s+(\w+):", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void TheClientKnowsEveryStateTheServerCanSend()
    {
        var wire = Enum.GetValues<Jellyfin.Plugin.JellyPortal.Summary.SummaryState>().Select(s => s.Name()).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(wire, KeysOf(ClientScript(), "STATE_TITLES").OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheClientHasASentenceForEveryReasonTheServerCanSend()
    {
        // jellyfin_unreachable is the client's own: the Jellyfin server itself could not be asked.
        var wire = Enum.GetValues<Jellyfin.Plugin.JellyPortal.Summary.SummaryFailure>().Select(f => f.Name()).Append("jellyfin_unreachable").OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(wire, KeysOf(ClientScript(), "REASON_TEXTS").OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheClientNeverBuildsHtmlFromWhatTheServerSends()
    {
        // Titles, names and messages come from the portal and from other people: textContent only.
        var script = ClientScript();
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClientScriptLoadsNothingFromElsewhere()
    {
        // No CDN, no framework, no font, no image from another host.
        var asset = File.ReadAllText(Path.Combine(RepoFiles.Root, "Jellyfin.Plugin.JellyPortal", "Web", "jellyportal.css")) + ClientScript();
        Assert.DoesNotMatch(@"https?://(?!www\.w3\.org/2000/svg)", asset);
        Assert.DoesNotContain("@import", asset, StringComparison.Ordinal);
        Assert.DoesNotContain("url(", asset, StringComparison.Ordinal);
    }
}
