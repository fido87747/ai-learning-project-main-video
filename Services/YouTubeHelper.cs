using System.Text.RegularExpressions;

namespace MentorBooking.Services;

// Parses YouTube video ids from common URL shapes and builds embed/thumbnail URLs.
public static class YouTubeHelper
{
    private static readonly Regex VideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    // Extracts an 11-char YouTube video id from watch?v=, youtu.be/, embed/, shorts/,
    // live/ URLs, or accepts a bare video id. Returns false for anything else.
    public static bool TryGetVideoId(string? url, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        url = url.Trim();

        // A bare video id.
        if (VideoIdPattern.IsMatch(url))
        {
            videoId = url;
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
        {
            host = host[4..];
        }

        string? candidate = null;

        if (host is "youtube.com" or "m.youtube.com" or "youtube-nocookie.com")
        {
            candidate = GetQueryValue(uri.Query, "v");
            if (string.IsNullOrEmpty(candidate))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" or "v")
                {
                    candidate = segments[1];
                }
            }
        }
        else if (host is "youtu.be")
        {
            candidate = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (!string.IsNullOrEmpty(candidate) && VideoIdPattern.IsMatch(candidate))
        {
            videoId = candidate;
            return true;
        }

        return false;
    }

    public static string EmbedUrl(string videoId) => $"https://www.youtube.com/embed/{videoId}";

    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    public static string ThumbnailUrl(string videoId) => $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

    private static string? GetQueryValue(string query, string key)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == key)
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();
    }
}
