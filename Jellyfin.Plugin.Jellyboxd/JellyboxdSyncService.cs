using System.Globalization;
using System.Net.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Listens to Jellyfin user-data changes and pushes them to Jellyboxd in real
/// time. Movies and whole series sync watched/favourite/rating; seasons and
/// episodes sync only "watched" (Jellyboxd ignores their rating/favourite).
/// </summary>
public sealed class JellyboxdSyncService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JellyboxdSyncService> _logger;
    private readonly JellyboxdClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyboxdSyncService"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user-data manager.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public JellyboxdSyncService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILogger<JellyboxdSyncService> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _client = new JellyboxdClient(httpClientFactory, logger);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _logger.LogInformation("[Jellyboxd] Sync service started; listening for user-data changes.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static SyncProviderIds ProviderIdsFrom(BaseItem item)
    {
        var ids = new SyncProviderIds();
        if (item.ProviderIds.TryGetValue("Tmdb", out var tmdb)) ids.Tmdb = NullIfEmpty(tmdb);
        if (item.ProviderIds.TryGetValue("Imdb", out var imdb)) ids.Imdb = NullIfEmpty(imdb);
        if (item.ProviderIds.TryGetValue("Tvdb", out var tvdb)) ids.Tvdb = NullIfEmpty(tvdb);
        return ids;
    }

    private static string? ItemType(BaseItem item)
    {
        if (item is Movie) return "Movie";
        if (item is Series) return "Series";
        if (item is Season) return "Season";
        if (item is Episode) return "Episode";
        return null;
    }

    private async void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        try
        {
            await HandleAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jellyboxd] Failed to push user-data change.");
        }
    }

    private async Task HandleAsync(UserDataSaveEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null
            || string.IsNullOrWhiteSpace(config.SyncKey)
            || string.IsNullOrWhiteSpace(config.JellyboxdUrl))
        {
            return;
        }

        if (e.SaveReason == UserDataSaveReason.PlaybackStart
            || e.SaveReason == UserDataSaveReason.PlaybackProgress)
        {
            return;
        }

        var item = e.Item;
        var data = e.UserData;
        if (item is null || data is null) return;

        var type = ItemType(item);
        if (type is null) return;
        if (config.MoviesOnly && type != "Movie") return;

        var itemId = item.Id.ToString("N");
        if (SuppressionCache.ShouldSuppress(e.UserId, itemId, data.Played, data.IsFavorite, data.Rating))
        {
            _logger.LogDebug("[Jellyboxd] Suppressed echo for {Item}.", item.Name);
            return;
        }

        var user = _userManager.GetUserById(e.UserId);
        if (user is null) return;

        var payload = new SyncEventPayload
        {
            User = new SyncUser { Name = user.Username, JellyfinUserId = e.UserId.ToString("N") },
            Item = new SyncItem
            {
                JellyfinItemId = itemId,
                Type = type,
                Name = item.Name,
                Year = item.ProductionYear,
                ProviderIds = ProviderIdsFrom(item),
            },
            State = new SyncStateDto
            {
                Played = data.Played,
                IsFavorite = data.IsFavorite,
                Rating = data.Rating,
            },
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };

        if (type == "Movie" || type == "Series")
        {
            var p = payload.Item.ProviderIds;
            if (string.IsNullOrEmpty(p.Tmdb) && string.IsNullOrEmpty(p.Imdb) && string.IsNullOrEmpty(p.Tvdb))
            {
                _logger.LogDebug("[Jellyboxd] {Item} has no provider id; skipping.", item.Name);
                return;
            }
        }
        else
        {
            // Season / Episode: match via the parent series + season/episode numbers.
            var series = (item as Episode)?.Series ?? (item as Season)?.Series;
            if (series is null)
            {
                var seriesId = (item as Episode)?.SeriesId ?? (item as Season)?.SeriesId ?? Guid.Empty;
                if (seriesId != Guid.Empty) series = _libraryManager.GetItemById(seriesId) as Series;
            }

            if (series is null) return;
            payload.Item.SeriesProviderIds = ProviderIdsFrom(series);
            if (string.IsNullOrEmpty(payload.Item.SeriesProviderIds.Tmdb)) return; // can't match without series TMDB id

            if (item is Episode ep)
            {
                payload.Item.SeasonNumber = ep.ParentIndexNumber;
                payload.Item.EpisodeNumber = ep.IndexNumber;
            }
            else if (item is Season se)
            {
                payload.Item.SeasonNumber = se.IndexNumber;
            }

            if (payload.Item.SeasonNumber is null) return;
        }

        await _client.SendEventAsync(config, payload).ConfigureAwait(false);
        _logger.LogInformation(
            "[Jellyboxd] Pushed {Type} '{Item}' (played={Played}, fav={Fav}, rating={Rating}).",
            type,
            item.Name,
            data.Played,
            data.IsFavorite,
            data.Rating);
    }
}
