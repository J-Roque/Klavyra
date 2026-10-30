using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Klavyra.Api.Features.Projects;

public static class DeleteProject
{
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

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        storage.Delete(id);
        return TypedResults.NoContent();
    }
}
