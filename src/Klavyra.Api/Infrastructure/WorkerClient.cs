using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Klavyra.Api.Infrastructure;

public sealed record WorkerRequest(
    string OutputDir, string? Url, string? File, string Engine, string Model, string Title, int? MaxSeconds = null,
    string? JobId = null);

public sealed record WorkerEvent(
    string Stage, int Progress, string? Message, Dictionary<string, string>? Files, string? Error, string? Title = null);

/// <summary>Calls the Python worker; POST /transcribe answers with a Server-Sent Events progress stream.</summary>
public sealed class WorkerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A short request (health, settings); the caller reads the status and the JSON body.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken ct, int timeoutSeconds = 60)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds)); // this client has no timeout of its own (transcriptions run for minutes)
        var message = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body, options: Json),
        };
        var response = await http.SendAsync(message, timeout.Token);
        await response.Content.LoadIntoBufferAsync(timeout.Token);
        return response;
    }

    /// <summary>The id the worker knows a project's job by.</summary>
    public static string JobId(Guid projectId) => projectId.ToString("N");

    /// <summary>Asks the worker to stop a project's job; false if the worker could not be reached.</summary>
    public async Task<bool> CancelAsync(Guid projectId, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "/cancel", new { jobId = JobId(projectId) }, ct, 10);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
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
