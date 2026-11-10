using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klavyra.Api.Features.Projects;
using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Klavyra.Api.Tests;

public sealed class KlavyraFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "klavyra-tests", Guid.NewGuid().ToString("N"));
    public FakeWorker Worker { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Root", StorageRoot);
        builder.ConfigureServices(services =>
            services.AddHttpClient<WorkerClient>().ConfigurePrimaryHttpMessageHandler(() => Worker));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(StorageRoot, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Plays the worker's SSE stream and writes the output files it reports.</summary>
public sealed class FakeWorker : HttpMessageHandler
{
    public bool Fail { get; set; }
    public List<JsonElement> Requests { get; } = [];

    /// <summary>Answers /health and /settings/... with these, keyed by "METHOD /path".</summary>
    public Dictionary<string, (HttpStatusCode Status, string Json)> Replies { get; } = new()
    {
        ["GET /health"] = (HttpStatusCode.OK, "{\"status\":\"ok\",\"cuda\":true,\"gpu\":\"RTX de prueba\",\"huggingFaceToken\":false}"),
    };

    public List<string> SettingsBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath != "/transcribe")
        {
            if (request.Content is not null) SettingsBodies.Add(await request.Content.ReadAsStringAsync(ct));
            if (!Replies.TryGetValue($"{request.Method} {request.RequestUri.AbsolutePath}", out var reply))
            {
                throw new HttpRequestException("worker apagado");
            }
            return new HttpResponseMessage(reply.Status)
            {
                Content = reply.Json.Length == 0 ? null : new StringContent(reply.Json, Encoding.UTF8, "application/json"),
            };
        }
        var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
        Requests.Add(body);
        var outDir = Directory.CreateDirectory(body.GetProperty("outputDir").GetString()!).FullName;

        var sse = new StringBuilder();
        sse.Append("data: {\"stage\":\"downloading\",\"progress\":10,\"message\":\"Descargando\"}\n\n");
        sse.Append("data: {\"stage\":\"transcribing\",\"progress\":50}\n\n");
        if (Fail)
        {
            sse.Append("data: {\"stage\":\"failed\",\"progress\":100,\"error\":\"RuntimeError: sin notas\"}\n\n");
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(outDir, "notes.json"), "{\"notes\":[]}", ct);
            await File.WriteAllTextAsync(Path.Combine(outDir, "score.mid"), "MThd", ct);
            sse.Append("data: {\"stage\":\"done\",\"progress\":100,\"files\":{\"notes\":\"notes.json\",\"midi\":\"score.mid\"}}\n\n");
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }
}

public sealed class ProjectsApiTests(KlavyraFactory factory) : IClassFixture<KlavyraFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Url_project_runs_through_the_worker_and_exposes_its_files()
    {
        var created = await CreateAsync(new() { ["url"] = "https://www.youtube.com/watch?v=abc", ["engine"] = "bytedance_piano" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;

        var done = await WaitFinishedAsync(project.Id);
        Assert.Equal(Domain.ProjectStatus.Done, done.Status);
        Assert.Equal(["notes", "midi"], done.Files);
        Assert.Contains(factory.Worker.Requests, r => r.GetProperty("url").GetString() == "https://www.youtube.com/watch?v=abc");

        var notes = await _client.GetAsync($"/api/projects/{project.Id}/files/notes");
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/projects/{project.Id}/files/pdf")).StatusCode);

        var list = await _client.GetFromJsonAsync<List<ProjectDto>>("/api/projects", Json);
        Assert.Contains(list!, p => p.Id == project.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }

    [Fact]
    public async Task Uploaded_file_is_stored_and_sent_to_the_worker_by_path()
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "mi cancion.mp3" },
        };
        var created = await _client.PostAsync("/api/projects", form);
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
        Assert.Equal("mi cancion", project.Title);

        await WaitFinishedAsync(project.Id);
        var request = factory.Worker.Requests.Single(r => r.GetProperty("outputDir").GetString()!.Contains(project.Id.ToString("N")));
        Assert.EndsWith("source.mp3", request.GetProperty("file").GetString());
    }

    [Fact]
    public async Task Worker_failure_marks_project_failed()
    {
        factory.Worker.Fail = true;
        try
        {
            var created = await CreateAsync(new() { ["url"] = "https://example.com/song.mp3" });
            var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            var failed = await WaitFinishedAsync(project.Id);
            Assert.Equal(Domain.ProjectStatus.Failed, failed.Status);
            Assert.Equal("RuntimeError: sin notas", failed.Error);
        }
        finally
        {
            factory.Worker.Fail = false;
        }
    }

    [Fact]
    public async Task Failed_project_can_be_retried_once_it_is_finished()
    {
        factory.Worker.Fail = true;
        ProjectDto project;
        try
        {
            var created = await CreateAsync(new() { ["url"] = "https://example.com/retry.mp3", ["engine"] = "muscriptor", ["model"] = "large" });
            project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            await WaitFinishedAsync(project.Id);
        }
        finally
        {
            factory.Worker.Fail = false;
        }

        var retried = await _client.PostAsync($"/api/projects/{project.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);

        var done = await WaitFinishedAsync(project.Id);
        Assert.Equal(Domain.ProjectStatus.Done, done.Status);
        Assert.Null(done.Error);
        Assert.Equal(2, factory.Worker.Requests.Count(r => r.GetProperty("url").GetString() == "https://example.com/retry.mp3" && r.GetProperty("model").GetString() == "large"));

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/projects/{project.Id}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/projects/{Guid.NewGuid()}/retry", null)).StatusCode);
    }

    [Theory]
    [InlineData(null, null)] // no source
    [InlineData("ftp://x.com/a.mp3", null)] // bad scheme
    [InlineData("https://x.com/a.mp3", "whisper")] // unknown engine
    public async Task Invalid_requests_are_rejected(string? url, string? engine)
    {
        var fields = new Dictionary<string, string> { ["title"] = "x" }; // an empty multipart body is malformed
        if (url is not null) fields["url"] = url;
        if (engine is not null) fields["engine"] = engine;
        var response = await CreateAsync(fields);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> CreateAsync(Dictionary<string, string> fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (key, value) in fields)
        {
            form.Add(new StringContent(value), key);
        }
        return _client.PostAsync("/api/projects", form);
    }

    private async Task<ProjectDto> WaitFinishedAsync(Guid id)
    {
        for (var i = 0; i < 100; i++)
        {
            var p = (await _client.GetFromJsonAsync<ProjectDto>($"/api/projects/{id}", Json))!;
            if (p.Status is Domain.ProjectStatus.Done or Domain.ProjectStatus.Failed)
            {
                return p;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("Project did not finish");
    }
}
