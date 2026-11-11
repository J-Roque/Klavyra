namespace Klavyra.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");
        CreateProject.Map(group);
        ListProjects.Map(group);
        GetProject.Map(group);
        DownloadFile.Map(group);
        DeleteProject.Map(group);
        RetryProject.Map(group);
        CancelProject.Map(group);
        return app;
    }
}
