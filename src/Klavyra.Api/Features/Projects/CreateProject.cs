using Klavyra.Api.Domain;
using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Klavyra.Api.Features.Projects;

public static class CreateProject
{
    public static readonly string[] Engines = ["bytedance_piano", "muscriptor"];
    public static readonly string[] Models = ["small", "medium", "large"];

    private static readonly HashSet<string> AllowedExtensions =
        [".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac", ".mp4", ".mov", ".webm", ".mkv"];

    public sealed class Request
    {
        public IFormFile? File { get; set; }
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Engine { get; set; }
        public string? Model { get; set; }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/", Handle)
            .DisableAntiforgery() // local API consumed by our own SPA; no cookies involved
            .Accepts<Request>("multipart/form-data")
            .WithName("CreateProject");

    public static async Task<Results<Created<ProjectDto>, ValidationProblem>> Handle(
        [FromForm] Request request, KlavyraDb db, FileStorage storage, TranscriptionQueue queue, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var url = request.Url?.Trim();
        var title = request.Title?.Trim() is { Length: > 0 } t ? t : DefaultTitle(request.File, url);
        var project = new Project
        {
            Title = title.Length > 200 ? title[..200] : title,
            SourceUrl = request.File is null ? url : null,
            SourceFileName = request.File?.FileName,
            Engine = request.Engine ?? "bytedance_piano",
            Model = request.Model ?? "small",
        };

        if (request.File is not null)
        {
            await storage.SaveUploadAsync(project.Id, request.File, ct);
        }
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        queue.Enqueue(project.Id);

        return TypedResults.Created($"/api/projects/{project.Id}", ProjectDto.From(project));
    }

    private static Dictionary<string, string[]> Validate(Request r)
    {
        var errors = new Dictionary<string, string[]>();
        var hasUrl = !string.IsNullOrWhiteSpace(r.Url);
        if (r.File is null == !hasUrl)
        {
            errors["source"] = ["Envía un archivo o una URL (solo uno)."];
        }
        if (hasUrl && !(Uri.TryCreate(r.Url!.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            errors["url"] = ["La URL debe empezar por http:// o https://."];
        }
        if (r.File is not null && !AllowedExtensions.Contains(Path.GetExtension(r.File.FileName).ToLowerInvariant()))
        {
            errors["file"] = [$"Formato no soportado. Usa: {string.Join(", ", AllowedExtensions)}."];
        }
        if (r.Engine is not null && !Engines.Contains(r.Engine))
        {
            errors["engine"] = [$"Motor inválido. Opciones: {string.Join(", ", Engines)}."];
        }
        if (r.Model is not null && !Models.Contains(r.Model))
        {
            errors["model"] = [$"Modelo inválido. Opciones: {string.Join(", ", Models)}."];
        }
        return errors;
    }

    private static string DefaultTitle(IFormFile? file, string? url) =>
        file is not null ? Path.GetFileNameWithoutExtension(file.FileName)
        : Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host
        : "Sin título";
}
