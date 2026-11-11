namespace Klavyra.Api.Infrastructure;

/// <summary>Per-project folders under Storage:Root, shared with the Python worker on the same machine.</summary>
public sealed class FileStorage
{
    public static readonly IReadOnlyDictionary<string, (string FileName, string ContentType)> Kinds =
        new Dictionary<string, (string, string)>
        {
            ["audio"] = ("playback.mp3", "audio/mpeg"),
            ["midi"] = ("score.mid", "audio/midi"),
            ["musicxml"] = ("score.musicxml", "application/vnd.recordare.musicxml+xml"),
            ["notes"] = ("notes.json", "application/json"),
            ["pdf"] = ("score.pdf", "application/pdf"),
        };

    public FileStorage(IConfiguration config, IHostEnvironment env)
    {
        Root = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["Storage:Root"] ?? "storage"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string ProjectDir(Guid id) => Path.Combine(Root, "projects", id.ToString("N"));

    public string? FilePath(Guid id, string kind) =>
        Kinds.TryGetValue(kind, out var file) ? Path.Combine(ProjectDir(id), file.FileName) : null;

    public async Task<string> SaveUploadAsync(Guid id, IFormFile upload, CancellationToken ct)
    {
        var dir = Directory.CreateDirectory(ProjectDir(id)).FullName;
        var path = Path.Combine(dir, "source" + Path.GetExtension(upload.FileName).ToLowerInvariant());
        await using var stream = File.Create(path);
        await upload.CopyToAsync(stream, ct);
        return path;
    }

    public string? UploadedSource(Guid id) =>
        Directory.Exists(ProjectDir(id)) ? Directory.EnumerateFiles(ProjectDir(id), "source.*").FirstOrDefault() : null;

    /// <summary>
    /// Deletes a project's folder. A browser that was playing its audio can hold a file for a moment after the
    /// page lets go, so a locked folder is retried briefly; false if it is still in use.
    /// </summary>
    public async Task<bool> TryDeleteAsync(Guid id, CancellationToken ct, int attempts = 5, int waitMs = 200)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Delete(id);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    return false;
                }
                await Task.Delay(waitMs, ct);
            }
        }
    }

    public void Delete(Guid id)
    {
        if (Directory.Exists(ProjectDir(id)))
        {
            Directory.Delete(ProjectDir(id), recursive: true);
        }
    }
}
