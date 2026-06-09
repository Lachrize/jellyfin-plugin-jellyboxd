using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Jellyboxd.Configuration;

/// <summary>
/// Settings for the Jellyboxd two-way sync plugin, edited from the dashboard.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the Jellyboxd app (e.g. http://localhost:3000).
    /// </summary>
    public string JellyboxdUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Gets or sets the shared secret sent as a Bearer token to Jellyboxd.
    /// Must match JELLYBOXD_SYNC_KEY in the Jellyboxd environment.
    /// </summary>
    public string SyncKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to restrict sync to movies. When
    /// false (default), movies + whole series sync watched/rating/favourite and
    /// seasons/episodes sync "watched".
    /// </summary>
    public bool MoviesOnly { get; set; } = false;
}
