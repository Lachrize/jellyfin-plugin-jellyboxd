using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Makes the Jellyboxd rating widget load in the Jellyfin web client.
///
/// Rather than serve the script through an ASP.NET controller (which drags in the
/// AspNetCore framework reference), this writes the embedded script straight into
/// the web folder as a static file and injects a &lt;script&gt; tag into
/// index.html. Runs at startup, is idempotent, and re-applies itself after a
/// Jellyfin update overwrites the web assets.
/// </summary>
public sealed class WebInjectionService : IHostedService
{
    private const string ScriptFileName = "jellyboxd-rating.js";
    private const string Marker = "<!-- Jellyboxd -->";
    private const string Tag = "<script defer src=\"" + ScriptFileName + "\"></script>" + Marker;
    private const string ResourceName = "Jellyfin.Plugin.Jellyboxd.Web.clientScript.js";

    private readonly IServerApplicationPaths _appPaths;
    private readonly ILogger<WebInjectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionService"/> class.
    /// </summary>
    /// <param name="appPaths">Server application paths (provides the web path).</param>
    /// <param name="logger">Logger.</param>
    public WebInjectionService(IServerApplicationPaths appPaths, ILogger<WebInjectionService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Inject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jellyboxd] Failed to inject the rating widget into the web client.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Inject()
    {
        var webPath = _appPaths.WebPath;
        if (string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath))
        {
            _logger.LogWarning("[Jellyboxd] Web path '{Path}' is unavailable; cannot inject widget.", webPath);
            return;
        }

        WriteScriptFile(webPath);

        var indexPath = Path.Combine(webPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _logger.LogWarning("[Jellyboxd] index.html not found at {Path}; skipping injection.", indexPath);
            return;
        }

        var html = File.ReadAllText(indexPath);
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            _logger.LogInformation("[Jellyboxd] Rating widget already injected.");
            return;
        }

        var closing = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closing < 0)
        {
            _logger.LogWarning("[Jellyboxd] No </body> in index.html; skipping injection.");
            return;
        }

        html = html.Insert(closing, Tag);
        File.WriteAllText(indexPath, html);
        _logger.LogInformation("[Jellyboxd] Injected the rating widget into {Path}.", indexPath);
    }

    private void WriteScriptFile(string webPath)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            _logger.LogWarning("[Jellyboxd] Embedded script resource '{Name}' not found.", ResourceName);
            return;
        }

        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();
        var target = Path.Combine(webPath, ScriptFileName);
        File.WriteAllText(target, script);
        _logger.LogInformation("[Jellyboxd] Wrote rating widget script to {Path}.", target);
    }
}
