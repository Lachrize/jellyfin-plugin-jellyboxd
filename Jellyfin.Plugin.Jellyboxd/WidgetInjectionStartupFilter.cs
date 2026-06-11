using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyboxd;

/// <summary>
/// Injects the Jellyboxd rating widget into the Jellyfin web client by rewriting
/// the <c>index.html</c> response in memory — no disk write required.
///
/// This is the portable counterpart to <see cref="WebInjectionService"/> (which
/// writes the widget into the web folder). Writing to the web folder fails when
/// it is read-only — the common case for a containerized Jellyfin running as a
/// non-root user (e.g. <c>/usr/share/jellyfin/web</c> on the official Docker
/// image). This middleware works regardless of web-folder permissions, so the
/// widget loads on native AND Docker installs.
///
/// To avoid double injection, both paths use the same <c>&lt;!-- Jellyboxd --&gt;</c>
/// marker: if the served HTML already carries it (because the file-write path
/// succeeded), this middleware leaves the response untouched.
///
/// Implemented as an <see cref="IStartupFilter"/> so a plugin can add middleware
/// to Jellyfin's HTTP pipeline. It uses only ASP.NET Core types provided
/// transitively by Jellyfin.Controller — no explicit framework reference (which
/// would crash the server at load), and the build ships only the plugin DLL.
/// </summary>
public sealed class WidgetInjectionStartupFilter : IStartupFilter
{
    private const string Marker = "<!-- Jellyboxd -->";
    private const string ResourceName = "Jellyfin.Plugin.Jellyboxd.Web.clientScript.js";

    private readonly ILogger<WidgetInjectionStartupFilter> _logger;
    private static string? _cachedScript;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetInjectionStartupFilter"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public WidgetInjectionStartupFilter(ILogger<WidgetInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Added first => outermost middleware: it runs first on the request
            // (so it can disable downstream compression) and last on the response
            // (so it sees the final, uncompressed HTML).
            app.Use(InvokeAsync);
            next(app);
        };
    }

    private async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsWebIndexRequest(context.Request))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Force a fresh, uncompressed 200 we can edit: drop conditional headers
        // (avoid a 304 with no body) and Accept-Encoding (avoid gzip/br).
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");
        context.Request.Headers.Remove("Accept-Encoding");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context).ConfigureAwait(false);

            var injected = false;
            try
            {
                var contentType = context.Response.ContentType ?? string.Empty;
                if (context.Response.StatusCode == StatusCodes.Status200OK
                    && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var html = Encoding.UTF8.GetString(buffer.ToArray());
                    var closing = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (closing >= 0 && !html.Contains(Marker, StringComparison.Ordinal))
                    {
                        html = html.Insert(closing, "<script>" + GetScript() + "</script>" + Marker);
                        var bytes = Encoding.UTF8.GetBytes(html);
                        context.Response.ContentLength = bytes.Length;
                        context.Response.Body = originalBody;
                        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
                        injected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let widget injection break the web client: fall through to
                // streaming the original response unchanged.
                _logger.LogError(ex, "[Jellyboxd] In-memory widget injection failed; serving page unmodified.");
                injected = false;
            }

            if (!injected)
            {
                context.Response.Body = originalBody;
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsWebIndexRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        return path.Equals("/web", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetScript()
    {
        if (_cachedScript is not null)
        {
            return _cachedScript;
        }

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return _cachedScript = "/* Jellyboxd widget resource missing */";
        }

        using var reader = new StreamReader(stream);
        return _cachedScript = reader.ReadToEnd();
    }
}
