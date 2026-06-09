using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Payload POSTed to Jellyboxd's /api/sync/event. Property names are pinned with
/// [JsonPropertyName] so they match the Zod schema on the Jellyboxd side exactly.
/// </summary>
public class SyncEventPayload
{
    [JsonPropertyName("user")]
    public SyncUser User { get; set; } = new();

    [JsonPropertyName("item")]
    public SyncItem Item { get; set; } = new();

    [JsonPropertyName("state")]
    public SyncStateDto State { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}

/// <summary>Identifies the Jellyfin user whose data changed.</summary>
public class SyncUser
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The Jellyfin user id (used by Jellyboxd to attribute the event).</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; }
}

/// <summary>Identifies the Jellyfin item and its external provider ids.</summary>
public class SyncItem
{
    [JsonPropertyName("jellyfinItemId")]
    public string JellyfinItemId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("providerIds")]
    public SyncProviderIds ProviderIds { get; set; } = new();

    /// <summary>Parent series provider ids (for Season/Episode matching).</summary>
    [JsonPropertyName("seriesProviderIds")]
    public SyncProviderIds? SeriesProviderIds { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; set; }
}

/// <summary>External provider ids (PascalCase to mirror Jellyfin).</summary>
public class SyncProviderIds
{
    [JsonPropertyName("Tmdb")]
    public string? Tmdb { get; set; }

    [JsonPropertyName("Imdb")]
    public string? Imdb { get; set; }

    [JsonPropertyName("Tvdb")]
    public string? Tvdb { get; set; }
}

/// <summary>Full snapshot of the Jellyfin user-data for the item.</summary>
public class SyncStateDto
{
    [JsonPropertyName("played")]
    public bool Played { get; set; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
}
