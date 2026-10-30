namespace Klavyra.Api.Domain;

public enum ProjectStatus
{
    Queued,
    Downloading,
    Transcribing,
    Notating,
    Rendering,
    Done,
    Failed,
}

public sealed class Project
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Title { get; set; }
    public string? SourceUrl { get; init; }
    public string? SourceFileName { get; init; }
    public required string Engine { get; init; }
    public required string Model { get; init; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Queued;
    public int Progress { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public List<string> Files { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsFinished => Status is ProjectStatus.Done or ProjectStatus.Failed;
}
