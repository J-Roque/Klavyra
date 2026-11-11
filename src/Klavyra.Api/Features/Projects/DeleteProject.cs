using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Klavyra.Api.Features.Projects;

public static class DeleteProject
{
    public const string FilesInUse =
        "No se pudieron borrar los archivos porque otro programa los está usando (por ejemplo, el reproductor). Ciérralo e inténtalo de nuevo.";

    public static void Map(RouteGroupBuilder group) => group.MapDelete("/{id:guid}", Handle).WithName("DeleteProject");

    public static async Task<Results<NoContent, NotFound, Conflict<string>>> Handle(
        Guid id, KlavyraDb db, FileStorage storage, CancellationToken ct)
    {
        if (await db.Projects.FindAsync([id], ct) is not { } project)
        {
            return TypedResults.NotFound();
        }
        if (!project.IsFinished)
        {
            return TypedResults.Conflict("El proyecto se está procesando; espera a que termine.");
        }

        // Files first: if Windows will not let go of them the project stays whole and can be deleted again,
        // instead of leaving files behind that nothing points to.
        if (!await storage.TryDeleteAsync(id, ct))
        {
            return TypedResults.Conflict(FilesInUse);
        }
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
