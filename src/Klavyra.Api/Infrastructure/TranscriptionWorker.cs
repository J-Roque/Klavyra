using Klavyra.Api.Domain;
using Klavyra.Api.Features.Projects;
using Klavyra.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Klavyra.Api.Infrastructure;

/// <summary>Processes one project at a time (single GPU) and pushes every progress step over SignalR.</summary>
public sealed class TranscriptionWorker(
    IServiceScopeFactory scopes,
    TranscriptionQueue queue,
    FileStorage storage,
    IHubContext<ProgressHub> hub,
    ILogger<TranscriptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // picked up again on next start
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transcription {ProjectId} failed", id);
                await UpdateAsync(id, p =>
                {
                    p.Status = ProjectStatus.Failed;
                    p.Error = ex is HttpRequestException
                        ? "No se pudo conectar con el worker de IA. ¿Está en ejecución?"
                        : ex.Message;
                    p.CompletedAt = DateTimeOffset.UtcNow;
                }, CancellationToken.None);
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KlavyraDb>();
        var pending = await db.Projects
            .Where(p => p.Status != ProjectStatus.Done && p.Status != ProjectStatus.Failed)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
        foreach (var project in pending)
        {
            project.Status = ProjectStatus.Queued;
            project.Progress = 0;
            queue.Enqueue(project.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KlavyraDb>();
        var worker = scope.ServiceProvider.GetRequiredService<WorkerClient>();
        if (await db.Projects.FindAsync([id], ct) is not { IsFinished: false } project)
        {
            queue.ClearCancel(id); // cancelled while it waited
            return;
        }

        var request = new WorkerRequest(
            storage.ProjectDir(id), project.SourceUrl, storage.UploadedSource(id),
            project.Engine, project.Model, project.Title, JobId: WorkerClient.JobId(id));

        var cancelSent = false;
        await foreach (var evt in worker.TranscribeAsync(request, ct))
        {
            Apply(project, evt);
            // Cancelled in the instant between leaving the queue and the worker's first report: stop it now.
            if (!cancelSent && !project.IsFinished && queue.IsCancelRequested(id))
            {
                cancelSent = true;
                await worker.CancelAsync(id, ct);
                project.Message = CancelProject.Cancelling;
            }
            await db.SaveChangesAsync(ct);
            await hub.Clients.All.SendAsync(ProgressHub.ProgressEvent, ProjectDto.From(project), ct);
        }
        queue.ClearCancel(id);

        if (!project.IsFinished)
        {
            Apply(project, new WorkerEvent("failed", 100, null, null, "El worker terminó sin resultado"));
            await db.SaveChangesAsync(ct);
            await hub.Clients.All.SendAsync(ProgressHub.ProgressEvent, ProjectDto.From(project), ct);
        }
    }

    /// <summary>True while the title is the one CreateProject gives a link when the user typed none.</summary>
    internal static bool HasDefaultTitle(Project project) =>
        Uri.TryCreate(project.SourceUrl, UriKind.Absolute, out var uri) && project.Title == uri.Host;

    internal static void Apply(Project project, WorkerEvent evt)
    {
        project.Progress = evt.Progress;
        project.Message = evt.Message;
        project.Status = evt.Stage switch
        {
            "downloading" => ProjectStatus.Downloading,
            "transcribing" => ProjectStatus.Transcribing,
            "notating" => ProjectStatus.Notating,
            "rendering" => ProjectStatus.Rendering,
            "done" => ProjectStatus.Done,
            "failed" => ProjectStatus.Failed,
            _ => project.Status,
        };
        // A project made from a link is first named after its web site; the worker then learns the video's title.
        if (!string.IsNullOrWhiteSpace(evt.Title) && HasDefaultTitle(project))
        {
            var title = evt.Title.Trim();
            project.Title = title.Length > 200 ? title[..200] : title;
        }
        if (evt.Files is not null)
        {
            project.Files = [.. evt.Files.Keys];
        }
        if (project.IsFinished)
        {
            project.Error = evt.Error;
            project.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task UpdateAsync(Guid id, Action<Project> change, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KlavyraDb>();
        if (await db.Projects.FindAsync([id], ct) is { } project)
        {
            change(project);
            await db.SaveChangesAsync(ct);
            await hub.Clients.All.SendAsync(ProgressHub.ProgressEvent, ProjectDto.From(project), ct);
        }
    }
}
