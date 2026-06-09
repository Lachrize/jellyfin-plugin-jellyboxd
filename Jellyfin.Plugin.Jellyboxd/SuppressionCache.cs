using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Short-lived memory of changes the plugin itself applied to Jellyfin after
/// receiving them from Jellyboxd (used by the reverse direction, phase 2). When
/// applying such a change fires UserDataSaved, we must not push it back to
/// Jellyboxd or it would ping-pong. An entry matching the incoming snapshot
/// within the TTL means "this is our own echo — drop it".
/// </summary>
internal static class SuppressionCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private static string Key(Guid userId, string itemId) => userId.ToString("N") + ":" + itemId;

    /// <summary>Record a snapshot we just wrote to Jellyfin from Jellyboxd.</summary>
    public static void Remember(Guid userId, string itemId, bool played, bool favorite, double? rating)
    {
        Entries[Key(userId, itemId)] = new Entry(DateTime.UtcNow, played, favorite, rating);
    }

    /// <summary>True if the given snapshot matches a recent self-applied change.</summary>
    public static bool ShouldSuppress(Guid userId, string itemId, bool played, bool favorite, double? rating)
    {
        var key = Key(userId, itemId);
        if (!Entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (DateTime.UtcNow - entry.At > Ttl)
        {
            Entries.TryRemove(key, out _);
            return false;
        }

        return entry.Played == played
            && entry.Favorite == favorite
            && NullableEquals(entry.Rating, rating);
    }

    private static bool NullableEquals(double? a, double? b)
    {
        if (!a.HasValue && !b.HasValue)
        {
            return true;
        }

        return a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) < 0.001;
    }

    private readonly record struct Entry(DateTime At, bool Played, bool Favorite, double? Rating);
}
