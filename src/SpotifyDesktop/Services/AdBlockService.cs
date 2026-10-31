using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace SpotifyDesktop.Services;

public sealed class AdBlockService
{
    private static readonly byte[] EmptyJsonBytes = Encoding.UTF8.GetBytes("{}");

    private static readonly string[] FilterPatterns =
    [
        "*doubleclick.net*",
        "*googlesyndication.com*",
        "*googleadservices.com*",
        "*adservice.google.*",
        "*2mdn.net*",
        "*adeventtracker.spotify.com*",
        "*ads-fa.spotify.com*",
        "*analytics.spotify.com*",
        "*crashdump.spotify.com*",
        "*b.scorecardresearch.com*",
        "*omaze.com*",
        "*bounceexchange.com*",
        "*/ad-logic/*",
        "*/ads/v1/*",
        "*/ads/v2/*",
        "*/ads/v3/*",
        "*/gabo-receiver-service/*"
    ];

    private static readonly string[] BlockedUrlSubstrings =
    [
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "adservice.google.",
        "2mdn.net",
        "adeventtracker.spotify.com",
        "ads-fa.spotify.com",
        "analytics.spotify.com",
        "crashdump.spotify.com",
        "b.scorecardresearch.com",
        "omaze.com",
        "bounceexchange.com",
        "/ad-logic/",
        "/ads/v1/",
        "/ads/v2/",
        "/ads/v3/",
        "/gabo-receiver-service/"
    ];

    private string? _cachedEngineScript;

    public async Task AttachAsync(CoreWebView2 webView)
    {
        string script = LoadEmbeddedAdBlockScript();
        if (!string.IsNullOrWhiteSpace(script))
        {
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }

        // Register targeted ad filters instead of universal '*' to eliminate IPC UI thread bottlenecks
        // while keeping core spclient.wg.spotify.com music catalog & Widevine license endpoints untouched
        foreach (string filter in FilterPatterns)
        {
            webView.AddWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All);
        }

        webView.WebResourceRequested += OnWebResourceRequested;
        webView.DOMContentLoaded += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(script))
            {
                try
                {
                    await webView.ExecuteScriptAsync(script);
                }
                catch
                {
                    // Ignore script execution errors during navigation transitions
                }
            }
        };
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 webView)
        {
            return;
        }

        string uri = e.Request.Uri;
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        if (IsAdRequest(uri))
        {
            e.Response = webView.Environment.CreateWebResourceResponse(
                new MemoryStream(EmptyJsonBytes, writable: false),
                200,
                "OK",
                "Content-Type: application/json\r\nAccess-Control-Allow-Origin: https://open.spotify.com\r\nAccess-Control-Allow-Credentials: true");
        }
    }

    public static bool IsAdRequest(string uri)
    {
        foreach (string pattern in BlockedUrlSubstrings)
        {
            if (uri.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private string LoadEmbeddedAdBlockScript()
    {
        if (_cachedEngineScript is not null)
        {
            return _cachedEngineScript;
        }

        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("spotify-adblock-engine.js", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return string.Empty;
        }

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return string.Empty;
        }

        using StreamReader reader = new(stream);
        _cachedEngineScript = reader.ReadToEnd();
        return _cachedEngineScript;
    }
}
