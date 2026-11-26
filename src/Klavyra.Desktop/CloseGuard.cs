using System.Text.Json;

namespace Klavyra.Desktop;

/// <summary>Before the window closes: is a transcription still waiting or running? Closing would restart it next time.</summary>
internal static class CloseGuard
{
    public const string Question =
        "Hay una transcripción en curso.\n\nSi cierras Klavyra ahora, volverá a empezar desde el principio la próxima vez que lo abras.\n\n¿Cerrar de todos modos?";

    /// <summary>How many projects in the API's list (GET /api/projects) have not finished.</summary>
    public static int Unfinished(string projectsJson)
    {
        using var doc = JsonDocument.Parse(projectsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
        return doc.RootElement.EnumerateArray().Count(p =>
            p.TryGetProperty("status", out var status) && status.GetString() is not ("Done" or "Failed"));
    }

    /// <summary>Asks the API, waiting two seconds at most; when it cannot answer there is nothing to protect.</summary>
    public static int Unfinished(Uri app)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            // On a thread-pool thread: the window's thread is the one waiting here.
            var json = Task.Run(() => http.GetStringAsync(new Uri(app, "api/projects"))).GetAwaiter().GetResult();
            return Unfinished(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return 0;
        }
    }
}
