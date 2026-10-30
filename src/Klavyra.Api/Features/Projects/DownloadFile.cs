using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Klavyra.Api.Features.Projects;

public static class DownloadFile
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{id:guid}/files/{kind}", Handle).WithName("DownloadProjectFile");

    public static async Task<Results<PhysicalFileHttpResult, NotFound>> Handle(
        Guid id, string kind, bool? download, KlavyraDb db, FileStorage storage, CancellationToken ct)
    {
        if (await db.Projects.FindAsync([id], ct) is not { } project
            || !project.Files.Contains(kind)
            || storage.FilePath(id, kind) is not { } path
            || !File.Exists(path))
        {
            return TypedResults.NotFound();
        }

        var (fileName, contentType) = FileStorage.Kinds[kind];
        var downloadName = download == true ? $"{Safe(project.Title)}{Path.GetExtension(fileName)}" : null;
        return TypedResults.PhysicalFile(path, contentType, downloadName, enableRangeProcessing: true);
    }

    private static string Safe(string title) =>
        string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
