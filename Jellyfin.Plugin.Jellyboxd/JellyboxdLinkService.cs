using System.Net.Http;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Pairs this server with Jellyboxd. When the user has set a Jellyfin username
/// but no token yet, it bootstraps a Jellyboxd account for them and stores the
/// returned sync token + one-time claim link in the config (so they can open
/// Jellyboxd without creating a second account). Idempotent.
/// </summary>
public sealed class JellyboxdLinkService : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<JellyboxdLinkService> _logger;
    private readonly JellyboxdClient _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyboxdLinkService"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="appHost">Server application host (server id/name).</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public JellyboxdLinkService(IUserManager userManager, IServerApplicationHost appHost, IHttpClientFactory httpClientFactory, ILogger<JellyboxdLinkService> logger)
    {
        _userManager = userManager;
        _appHost = appHost;
        _logger = logger;
        _client = new JellyboxdClient(httpClientFactory, logger);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunLoopAsync(_cts.Token);
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
            await Task.Delay(TimeSpan.FromSeconds(12), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (!token.IsCancellationRequested)
        {
            await LinkOnceAsync().ConfigureAwait(false);
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

    private async Task LinkOnceAsync()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.JellyboxdUrl) || string.IsNullOrWhiteSpace(config.JellyfinUsername))
        {
            return;
        }

        try
        {
            var user = _userManager.GetUserByName(config.JellyfinUsername);
            if (user is null)
            {
                _logger.LogWarning("[Jellyboxd] Link: Jellyfin user '{User}' not found.", config.JellyfinUsername);
                return;
            }

            var req = new LinkRequest
            {
                ServerId = _appHost.SystemId,
                ServerName = _appHost.FriendlyName,
                JellyfinUserId = user.Id.ToString("N"),
                Username = user.Username,
            };
            var res = await _client.LinkAsync(config, req).ConfigureAwait(false);

            if (res?.Mode == "bootstrap" && !string.IsNullOrEmpty(res.Token))
            {
                config.SyncKey = res.Token;
                config.ClaimUrl = res.ClaimUrl ?? string.Empty;
                plugin.SaveConfiguration();
                _logger.LogInformation("[Jellyboxd] Paired. Open the claim link from the plugin config to log into Jellyboxd.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jellyboxd] Link attempt failed.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cts?.Dispose();
}
