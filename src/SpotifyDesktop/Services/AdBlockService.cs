using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace SpotifyDesktop.Services;

public sealed class AdBlockService
{
    private static readonly byte[] EmptyResponseBytes = Array.Empty<byte>();

    // IMPORTANT: Never block spclient.wg.spotify.com (/ad-logic/, /ads/, /state) at network level.
    // Blocking spclient endpoints causes Spotify's state_machine to throw
    // "Spotify tidak bisa memutar ini sekarang" ("Spotify can't play this right now") when an ad break starts.
    // Instead, block only 3rd-party ad/tracker networks here, while Layer 2 & Layer 3 in JS
    // silently mute and fast-forward/skip ad streams in <100ms with zero playback errors.
    private static readonly string[] FilterPatterns =
    [
        "*doubleclick.net*",
        "*googlesyndication.com*",
        "*googleadservices.com*",
        "*adservice.google.*",
        "*2mdn.net*",
        "*amillionads.com*",
        "*moatads.com*",
        "*b.scorecardresearch.com*",
        "*omaze.com*",
        "*bounceexchange.com*",
        "*crashdump.spotify.com*"
    ];

    private static readonly string[] BlockedUrlSubstrings =
    [
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "adservice.google.",
        "2mdn.net",
        "amillionads.com",
        "moatads.com",
        "b.scorecardresearch.com",
        "omaze.com",
        "bounceexchange.com",
        "crashdump.spotify.com"
    ];

    private string? _cachedEngineScript;

    public async Task AttachAsync(CoreWebView2 webView)
    {
        string script = LoadEmbeddedAdBlockScript();
        if (!string.IsNullOrWhiteSpace(script))
        {
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }

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
                new MemoryStream(EmptyResponseBytes, writable: false),
                204,
                "No Content",
                "Access-Control-Allow-Origin: https://open.spotify.com");
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
