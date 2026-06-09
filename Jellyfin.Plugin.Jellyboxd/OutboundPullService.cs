using System.Net.Http;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Outbound pull (Jellyboxd -> Jellyfin). Periodically pulls the user's queued
/// changes from Jellyboxd and applies them to the configured Jellyfin user
/// locally. Because the plugin makes the outgoing request, this works even when
/// the user's server is only reachable on their LAN.
/// </summary>
public sealed class OutboundPullService : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<OutboundPullService> _logger;
    private readonly JellyboxdClient _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundPullService"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="userDataManager">Jellyfin user-data manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public OutboundPullService(
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILogger<OutboundPullService> logger)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _client = new JellyboxdClient(httpClientFactory, logger);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunLoopAsync(_cts.Token);
        _logger.LogInformation("[Jellyboxd] Outbound pull started (every {Seconds}s).", Interval.TotalSeconds);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (!token.IsCancellationRequested)
        {
            await PullOnceAsync().ConfigureAwait(false);
            try
            {
                if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PullOnceAsync()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null
            || string.IsNullOrWhiteSpace(config.SyncKey)
            || string.IsNullOrWhiteSpace(config.JellyboxdUrl)
            || string.IsNullOrWhiteSpace(config.JellyfinUsername))
        {
            return;
        }

        try
        {
            var user = _userManager.GetUserByName(config.JellyfinUsername);
            if (user is null)
            {
                _logger.LogWarning("[Jellyboxd] Configured Jellyfin user '{User}' not found.", config.JellyfinUsername);
                return;
            }

            var pending = await _client.GetPendingAsync(config).ConfigureAwait(false);
            if (pending?.Changes is null || pending.Changes.Count == 0)
            {
                return;
            }

            var acks = new AckPayload();
            foreach (var change in pending.Changes)
            {
                try
                {
                    ApplyChange(user, change);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Jellyboxd] Failed applying change {Id}.", change.Id);
                }

                // Ack regardless: applied, or unresolvable (item not in library).
                acks.Acks.Add(new AckEntry { Id = change.Id, UpdatedAt = change.UpdatedAt });
            }

            if (acks.Acks.Count > 0)
            {
                await _client.AckAsync(config, acks).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jellyboxd] Outbound pull failed.");
        }
    }

    private void ApplyChange(User user, PendingChange change)
    {
        var item = ResolveItem(change);
        if (item is null)
        {
            return;
        }

        var data = _userDataManager.GetUserData(user, item);
        if (data is null)
        {
            return;
        }

        if (change.Played.HasValue)
        {
            data.Played = change.Played.Value;
        }

        if (change.Favorite.HasValue)
        {
            data.IsFavorite = change.Favorite.Value;
        }

        if (change.Rating.HasValue)
        {
            data.Rating = change.Rating.Value == 0 ? (double?)null : change.Rating.Value;
        }

        // Avoid the echo: our own UserDataSaved must not be pushed back.
        SuppressionCache.Remember(user.Id, item.Id.ToString("N"), data.Played, data.IsFavorite, data.Rating);
        _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
        _logger.LogInformation("[Jellyboxd] Applied pulled change to '{Item}'.", item.Name);
    }

    private BaseItem? ResolveItem(PendingChange change)
    {
        if (change.Kind == "MOVIE" && change.Tmdb is not null)
        {
            return FindByTmdb(change.Tmdb, BaseItemKind.Movie);
        }

        if (change.Kind == "SERIES" && change.Tmdb is not null)
        {
            return FindByTmdb(change.Tmdb, BaseItemKind.Series);
        }

        if ((change.Kind == "EPISODE" || change.Kind == "SEASON") && change.SeriesTmdb is not null && change.SeasonNumber.HasValue)
        {
            var series = FindByTmdb(change.SeriesTmdb, BaseItemKind.Series);
            if (series is null)
            {
                return null;
            }

            if (change.Kind == "SEASON")
            {
                return FindChild(series.Id, BaseItemKind.Season, change.SeasonNumber, null);
            }

            return change.EpisodeNumber.HasValue
                ? FindChild(series.Id, BaseItemKind.Episode, change.SeasonNumber, change.EpisodeNumber)
                : null;
        }

        return null;
    }

    private BaseItem? FindByTmdb(string tmdb, BaseItemKind kind)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            Recursive = true,
            HasAnyProviderId = new Dictionary<string, string> { ["Tmdb"] = tmdb },
        };
        return _libraryManager.GetItemList(query).FirstOrDefault();
    }

    private BaseItem? FindChild(Guid seriesId, BaseItemKind kind, int? seasonNumber, int? episodeNumber)
    {
        var query = new InternalItemsQuery
        {
            AncestorIds = new[] { seriesId },
            IncludeItemTypes = new[] { kind },
            Recursive = true,
        };
        if (kind == BaseItemKind.Season)
        {
            query.IndexNumber = seasonNumber;
        }
        else
        {
            query.ParentIndexNumber = seasonNumber;
            query.IndexNumber = episodeNumber;
        }

        return _libraryManager.GetItemList(query).FirstOrDefault();
    }

    /// <inheritdoc />
    public void Dispose() => _cts?.Dispose();
}
