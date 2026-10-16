namespace WinSetup.Models;

public sealed class Catalog
{
    public List<CatalogCategory> Categories { get; set; } = new();
}

public sealed class CatalogCategory
{
    public string Name { get; set; } = "";

    /// <summary>"app" (winget install), "debloat" (remove UWP apps) or "tweak" (privacy / performance).</summary>
    public string Kind { get; set; } = "app";

    public List<CatalogEntry> Items { get; set; } = new();
}

public sealed class CatalogEntry
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// app     -> winget package id (e.g. "Google.Chrome")
    /// debloat -> AppX package name pattern(s), separated by ';' (wildcards allowed)
    /// tweak   -> tweak key understood by TweakService
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>For apps without a winget package: page opened in the browser instead of installing.</summary>
    public string? Url { get; set; }

    /// <summary>winget source: "winget" (default) or "msstore".</summary>
    public string? Source { get; set; }

    /// <summary>Pre-ticked when the app starts.</summary>
    public bool Default { get; set; }

    /// <summary>Optional fallback when winget fails or is unavailable.</summary>
    public GitHubFallback? GitHub { get; set; }
}

public sealed class GitHubFallback
{
    public string Repo { get; set; } = "";          // "owner/name"
    public string AssetPattern { get; set; } = "";  // regex matched against release asset file names
    public string? SilentArgs { get; set; }         // for .exe installers, default "/S"
}
