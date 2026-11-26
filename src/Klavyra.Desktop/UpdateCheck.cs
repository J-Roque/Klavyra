using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Klavyra.Desktop;

/// <summary>Asks GitHub for the latest Klavyra release. Only a notice: nothing is downloaded or installed.</summary>
internal static class UpdateCheck
{
    public const string LatestRelease = "https://api.github.com/repos/J-Roque/Klavyra/releases/latest";

    public sealed record Release(Version Version, string Tag, string PageUrl);

    /// <summary>The running version, e.g. 0.1.0 (the "+commit" suffix the SDK adds is dropped).</summary>
    public static Version Current
    {
        get
        {
            var informational = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ParseVersion(informational) ?? typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0);
        }
    }

    /// <summary>"v1.2.3", "1.2.3-beta" or "1.2.3+abc" → 1.2.3; anything else → null.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return Version.TryParse(core, out var version) ? Normalize(version) : null;
    }

    /// <summary>The release described by a GitHub "latest release" response, if it is newer than <paramref name="current"/>.</summary>
    public static Release? NewerRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var page = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        var version = ParseVersion(tag);
        if (version is null || page is null || version <= Normalize(current)) return null;
        return new Release(version, tag!, page);
    }

    /// <summary>Null when there is nothing newer or GitHub cannot be reached (offline, rate limit, private repository…).</summary>
    public static async Task<Release?> FindNewerAsync(HttpClient http, Version current, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Klavyra", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return NewerRelease(await response.Content.ReadAsStringAsync(ct), current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // 0.1 and 0.1.0 compare equal: missing parts count as 0.
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
