using Klavyra.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Klavyra.Api.Features.Projects;

public static class ListProjects
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("/", Handle).WithName("ListProjects");

    public static async Task<IReadOnlyList<ProjectDto>> Handle(KlavyraDb db, CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);
        return [.. projects.Select(ProjectDto.From)];
    }
}
