using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Jellyboxd.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Thin HTTP client that pushes sync events to the Jellyboxd app. Nulls are
/// serialized (not omitted) so a cleared rating propagates as an explicit null.
/// </summary>
public sealed class JellyboxdClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyboxdClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public JellyboxdClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// POSTs a sync event to Jellyboxd. Failures are logged, not thrown.
    /// </summary>
    /// <param name="config">Current plugin configuration.</param>
    /// <param name="payload">Event payload.</param>
    /// <returns>A task.</returns>
    public Task SendEventAsync(PluginConfiguration config, SyncEventPayload payload) =>
        PostAsync(config, "/api/sync/event", payload);

    /// <summary>Pull the outbound queue (changes to apply locally).</summary>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>Pending changes, or null on failure.</returns>
    public async Task<PendingResponse?> GetPendingAsync(PluginConfiguration config)
    {
        var url = config.JellyboxdUrl.TrimEnd('/') + "/api/sync/pending";
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.SyncKey);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<PendingResponse>(stream, cancellationToken: cts.Token).ConfigureAwait(false);
    }

    /// <summary>Acknowledge applied changes so they leave the queue.</summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="payload">Acked ids.</param>
    /// <returns>A task.</returns>
    public Task AckAsync(PluginConfiguration config, AckPayload payload) => PostAsync(config, "/api/sync/pending", payload);

    /// <summary>Pair/link this server's user with a Jellyboxd account.</summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="req">Link request.</param>
    /// <returns>The link response, or null on failure.</returns>
    public async Task<LinkResponse?> LinkAsync(PluginConfiguration config, LinkRequest req)
    {
        var url = config.JellyboxdUrl.TrimEnd('/') + "/api/auth/jellyfin-link";
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(config.SyncKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.SyncKey);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Jellyboxd] Link failed: {Status}", (int)response.StatusCode);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<LinkResponse>(stream, cancellationToken: cts.Token).ConfigureAwait(false);
    }

    private async Task PostAsync<T>(PluginConfiguration config, string path, T payload)
    {
        var url = config.JellyboxdUrl.TrimEnd('/') + path;
        var client = _httpClientFactory.CreateClient(NamedClient.Default);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.SyncKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            _logger.LogWarning("[Jellyboxd] Push to {Url} failed: {Status} {Body}", url, (int)response.StatusCode, body);
        }
    }
}
