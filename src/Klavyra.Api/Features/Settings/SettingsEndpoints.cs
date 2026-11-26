using System.Reflection;
using System.Text.Json;
using Klavyra.Api.Infrastructure;

namespace Klavyra.Api.Features.Settings;

public sealed record SettingsDto(string Version, bool WorkerOnline, JsonElement? Worker);

public sealed record TokenRequest(string Token);

/// <summary>
/// What the Settings page shows: the version, the worker's view of this PC (GPU, ffmpeg, MuseScore) and the
/// Hugging Face token MuScriptor needs. The token itself only travels to the worker, which keeps it in
/// huggingface_hub's token file; the API never stores or logs it.
/// </summary>
public static class SettingsEndpoints
{
    private const string WorkerOffline = "El motor de transcripción no está disponible. Espera un momento o reinicia Klavyra.";

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");
        group.MapGet("/", GetAsync).WithName("GetSettings");
        group.MapGet("/hugging-face", (WorkerClient worker, CancellationToken ct) =>
            ForwardAsync(worker, HttpMethod.Get, null, ct)).WithName("CheckHuggingFace");
        group.MapPut("/hugging-face", (TokenRequest body, WorkerClient worker, CancellationToken ct) =>
            ForwardAsync(worker, HttpMethod.Put, new { token = body.Token }, ct)).WithName("SaveHuggingFaceToken");
        group.MapDelete("/hugging-face", (WorkerClient worker, CancellationToken ct) =>
            ForwardAsync(worker, HttpMethod.Delete, null, ct)).WithName("RemoveHuggingFaceToken");
        // Installing the newest link downloader takes a while on a slow connection.
        group.MapPost("/downloader/update", (WorkerClient worker, CancellationToken ct) =>
            ForwardAsync(worker, HttpMethod.Post, null, ct, "/settings/downloader/update", 180)).WithName("UpdateDownloader");
        return app;
    }

    public static string Version { get; } = ReadVersion();

    private static async Task<SettingsDto> GetAsync(WorkerClient worker, CancellationToken ct)
    {
        try
        {
            using var response = await worker.SendAsync(HttpMethod.Get, "/health", null, ct);
            if (response.IsSuccessStatusCode)
            {
                var health = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                return new SettingsDto(Version, true, health);
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
        }
        return new SettingsDto(Version, false, null);
    }

    private static async Task<IResult> ForwardAsync(
        WorkerClient worker, HttpMethod method, object? body, CancellationToken ct,
        string path = "/settings/hugging-face", int timeoutSeconds = 60)
    {
        try
        {
            using var response = await worker.SendAsync(method, path, body, ct, timeoutSeconds);
            var json = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrEmpty(json)
                ? Results.StatusCode((int)response.StatusCode)
                : Results.Content(json, "application/json", statusCode: (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Results.Json(new { detail = WorkerOffline }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string ReadVersion()
    {
        var version = typeof(SettingsEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return version.Split('+')[0]; // drop the commit hash the SDK appends
    }
}
