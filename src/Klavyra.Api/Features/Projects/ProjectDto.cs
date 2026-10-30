using Klavyra.Api.Domain;

namespace Klavyra.Api.Features.Projects;

public sealed record ProjectDto(
    Guid Id,
    string Title,
    string? SourceUrl,
    string? SourceFileName,
    string Engine,
    string Model,
    ProjectStatus Status,
    int Progress,
    string? Message,
    string? Error,
    IReadOnlyList<string> Files,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ProjectDto From(Project p) => new(
        p.Id, p.Title, p.SourceUrl, p.SourceFileName, p.Engine, p.Model, p.Status, p.Progress,
        p.Message, p.Error, p.Files, p.CreatedAt, p.CompletedAt);
}
