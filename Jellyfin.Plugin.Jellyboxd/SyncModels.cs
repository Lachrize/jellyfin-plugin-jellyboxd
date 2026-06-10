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

/// <summary>Response from GET /api/sync/pending (outbound queue to apply locally).</summary>
public class PendingResponse
{
    [JsonPropertyName("changes")]
    public List<PendingChange> Changes { get; set; } = new();
}

/// <summary>A queued change from Jellyboxd to apply on this server.</summary>
public class PendingChange
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The Jellyfin user this change must be applied to.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Their Jellyfin display name (reliable key for GetUserByName).</summary>
    [JsonPropertyName("jellyfinUsername")]
    public string? JellyfinUsername { get; set; }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("tmdb")]
    public string? Tmdb { get; set; }

    [JsonPropertyName("seriesTmdb")]
    public string? SeriesTmdb { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; set; }

    [JsonPropertyName("played")]
    public bool? Played { get; set; }

    [JsonPropertyName("rating")]
    public int? Rating { get; set; } // null = no change, 0 = clear, 1..10 = set

    [JsonPropertyName("favorite")]
    public bool? Favorite { get; set; }
}

/// <summary>Body for POST /api/sync/pending (ack applied changes).</summary>
public class AckPayload
{
    [JsonPropertyName("acks")]
    public List<AckEntry> Acks { get; set; } = new();
}

/// <summary>One acked change.</summary>
public class AckEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>Body for POST /api/auth/jellyfin-link (pair/link an account).</summary>
public class LinkRequest
{
    [JsonPropertyName("serverId")]
    public string ServerId { get; set; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/auth/jellyfin-link.</summary>
public class LinkResponse
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; } // "linked" | "bootstrap" | "already_linked"

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("claimUrl")]
    public string? ClaimUrl { get; set; }
}
