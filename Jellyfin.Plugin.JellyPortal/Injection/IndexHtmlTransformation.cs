namespace Jellyfin.Plugin.JellyPortal.Injection;

/// <summary>What File Transformation hands to a callback: the file as it stands.</summary>
public class TransformationPayload
{
    /// <summary>Gets or sets the current contents of the file being served.</summary>
    public string Contents { get; set; } = string.Empty;
}

/// <summary>
/// The callback File Transformation invokes, by reflection, for <c>index.html</c>. Its name, its class and its
/// single-parameter signature are what File Transformation looks up, so changing any of them breaks the
/// injection silently: a test calls it exactly the way File Transformation does.
/// </summary>
public static class IndexHtmlTransformation
{
    /// <summary>The registration key. File Transformation keeps ONE list of transformations per exact key, and for a
    /// file whose name equals a key it runs only that list: using the same literal as the other plugins that patch
    /// <c>index.html</c> (Home Sections and others) is what makes them all run, whereas a "tidier" anchored pattern
    /// would be skipped, or would silently skip them.</summary>
    public const string FilePattern = "index.html";

    /// <summary>A fixed identifier for this plugin's transformation; registering the same one twice is ignored.</summary>
    public const string TransformationId = "1f18e5b5-a6f3-4f8c-8e23-6fbff7e9b1ca";

    /// <summary>Called by File Transformation.</summary>
    /// <param name="payload">The file.</param>
    /// <returns>The file with the plugin's stylesheet and script added.</returns>
    public static string Apply(TransformationPayload payload) =>
        IndexHtmlInjector.Inject(payload.Contents, typeof(IndexHtmlTransformation).Assembly.GetName().Version?.ToString() ?? "0");
}
