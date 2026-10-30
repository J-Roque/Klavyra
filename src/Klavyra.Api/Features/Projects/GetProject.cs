using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Klavyra.Api.Features.Projects;

public static class GetProject
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("/{id:guid}", Handle).WithName("GetProject");

    public static async Task<Results<Ok<ProjectDto>, NotFound>> Handle(Guid id, KlavyraDb db, CancellationToken ct) =>
        await db.Projects.FindAsync([id], ct) is { } project
            ? TypedResults.Ok(ProjectDto.From(project))
            : TypedResults.NotFound();
}
