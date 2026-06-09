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
    /// Gets or sets the user's personal Jellyboxd sync token (Bearer). Generated
    /// in Jellyboxd → Paramètres → Jellyfin.
    /// </summary>
    public string SyncKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin username this token belongs to. Only this user's
    /// activity is synced (push), and pulled changes are applied to this user.
    /// </summary>
    public string JellyfinUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the one-time link to open/log into Jellyboxd (filled
    /// automatically after pairing). Read-only for the user.
    /// </summary>
    public string ClaimUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to restrict sync to movies. When
    /// false (default), movies + whole series sync watched/rating/favourite and
    /// seasons/episodes sync "watched".
    /// </summary>
    public bool MoviesOnly { get; set; } = false;
}
