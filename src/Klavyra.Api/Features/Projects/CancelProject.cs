using Klavyra.Api.Domain;
using Klavyra.Api.Hubs;
using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;

namespace Klavyra.Api.Features.Projects;

/// <summary>
/// Stops a transcription that is waiting or running. It ends as Failed with "Cancelada.", so it can be
/// retried or deleted like any other that did not finish.
/// </summary>
public static class CancelProject
{
    public const string Cancelled = "Cancelada.";
    public const string Cancelling = "Cancelando…";

    public static void Map(RouteGroupBuilder group) => group.MapPost("/{id:guid}/cancel", Handle).WithName("CancelProject");

    public static async Task<Results<Ok<ProjectDto>, NotFound, Conflict<string>>> Handle(
        Guid id, KlavyraDb db, WorkerClient worker, TranscriptionQueue queue, IHubContext<ProgressHub> hub, CancellationToken ct)
    {
        if (await db.Projects.FindAsync([id], ct) is not { } project)
        {
            return TypedResults.NotFound();
        }
        if (project.IsFinished)
        {
            return TypedResults.Conflict("La transcripción ya terminó.");
        }

        // Remembered too, in case the worker loop picked the project up an instant ago and has not said so yet.
        queue.RequestCancel(id);

        if (project.Status == ProjectStatus.Queued)
        {
            project.Status = ProjectStatus.Failed;
            project.Error = Cancelled;
            project.Message = null;
            project.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // The worker reports the end itself; a step that cannot be interrupted finishes first.
            await worker.CancelAsync(id, ct);
            project.Message = Cancelling;
        }
        await db.SaveChangesAsync(ct);

        var dto = ProjectDto.From(project);
        await hub.Clients.All.SendAsync(ProgressHub.ProgressEvent, dto, ct);
        return TypedResults.Ok(dto);
    }
}
