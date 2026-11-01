using Klavyra.Api.Domain;
using Klavyra.Api.Hubs;
using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;

namespace Klavyra.Api.Features.Projects;

public static class RetryProject
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("/{id:guid}/retry", Handle).WithName("RetryProject");

    public static async Task<Results<Ok<ProjectDto>, NotFound, Conflict<string>>> Handle(
        Guid id, KlavyraDb db, TranscriptionQueue queue, IHubContext<ProgressHub> hub, CancellationToken ct)
    {
        if (await db.Projects.FindAsync([id], ct) is not { } project)
        {
            return TypedResults.NotFound();
        }
        if (project.Status != ProjectStatus.Failed)
        {
            return TypedResults.Conflict("Solo se puede reintentar una transcripción que falló.");
        }

        project.Status = ProjectStatus.Queued;
        project.Progress = 0;
        project.Message = null;
        project.Error = null;
        project.Files = [];
        project.CompletedAt = null;
        await db.SaveChangesAsync(ct);
        queue.Enqueue(project.Id);

        var dto = ProjectDto.From(project);
        await hub.Clients.All.SendAsync(ProgressHub.ProgressEvent, dto, ct);
        return TypedResults.Ok(dto);
    }
}
