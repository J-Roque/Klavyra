using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Klavyra.Api.Infrastructure;

public sealed record WorkerRequest(
    string OutputDir, string? Url, string? File, string Engine, string Model, string Title, int? MaxSeconds = null);

public sealed record WorkerEvent(
    string Stage, int Progress, string? Message, Dictionary<string, string>? Files, string? Error);

/// <summary>Calls the Python worker; POST /transcribe answers with a Server-Sent Events progress stream.</summary>
public sealed class WorkerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A short request (health, settings); the caller reads the status and the JSON body.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60)); // this client has no timeout of its own (transcriptions run for minutes)
        var message = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body, options: Json),
        };
        var response = await http.SendAsync(message, timeout.Token);
        await response.Content.LoadIntoBufferAsync(timeout.Token);
        return response;
    }

    public async IAsyncEnumerable<WorkerEvent> TranscribeAsync(
        WorkerRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/transcribe")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal)
                && JsonSerializer.Deserialize<WorkerEvent>(line.AsSpan(5).Trim(), Json) is { } evt)
            {
                yield return evt;
            }
        }
    }
}
