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

    /// <summary>The title the worker reads from a downloaded video.</summary>
    public string? VideoTitle { get; set; }

    /// <summary>While true, a job reports its first step and then waits until it is cancelled.</summary>
    public bool Hold { get; set; }

    public System.Collections.Concurrent.ConcurrentDictionary<string, bool> CancelledJobs { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath == "/cancel")
        {
            var cancel = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
            CancelledJobs[cancel.GetProperty("jobId").GetString()!] = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"running\":true}", Encoding.UTF8, "application/json") };
        }
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

        var title = VideoTitle is null ? "" : $",\"title\":{JsonSerializer.Serialize(VideoTitle)}";
        var first = $"data: {{\"stage\":\"downloading\",\"progress\":10,\"message\":\"Descargando\"{title}}}\n\n";
        if (Hold)
        {
            var jobId = body.GetProperty("jobId").GetString()!;
            var pipe = new System.IO.Pipelines.Pipe();
            _ = Task.Run(async () =>
            {
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(first));
                while (!CancelledJobs.ContainsKey(jobId))
                {
                    await Task.Delay(20);
                }
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: {\"stage\":\"failed\",\"progress\":100,\"error\":\"Cancelada.\"}\n\n"));
                await pipe.Writer.CompleteAsync();
            }, CancellationToken.None);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(pipe.Reader.AsStream()) };
        }

        var sse = new StringBuilder();
        sse.Append(first);
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

    [Fact]
    public async Task A_project_whose_files_are_in_use_is_kept_whole_and_can_be_deleted_afterwards()
    {
        var created = await CreateAsync(new() { ["url"] = "https://example.com/en-uso.mp3" });
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
        await WaitFinishedAsync(project.Id);
        var notes = Path.Combine(factory.StorageRoot, "projects", project.Id.ToString("N"), "notes.json");

        string detail;
        using (File.Open(notes, FileMode.Open, FileAccess.Read, FileShare.None)) // like a player still streaming it
        {
            var blocked = await _client.DeleteAsync($"/api/projects/{project.Id}");
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            detail = await blocked.Content.ReadAsStringAsync();
        }
        Assert.Contains("otro programa", detail);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/projects/{project.Id}")).StatusCode); // still listed
        Assert.True(File.Exists(notes));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.False(Directory.Exists(Path.GetDirectoryName(notes)));
    }

    [Fact]
    public async Task Link_project_takes_the_video_title_unless_the_user_named_it()
    {
        factory.Worker.VideoTitle = "Clair de Lune – Debussy";
        try
        {
            var unnamed = await CreateAsync(new() { ["url"] = "https://www.youtube.com/watch?v=luna" });
            var project = (await unnamed.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            Assert.Equal("www.youtube.com", project.Title);
            Assert.Equal("Clair de Lune – Debussy", (await WaitFinishedAsync(project.Id)).Title);

            var named = await CreateAsync(new() { ["url"] = "https://www.youtube.com/watch?v=luna2", ["title"] = "Mi versión" });
            project = (await named.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            Assert.Equal("Mi versión", (await WaitFinishedAsync(project.Id)).Title);
        }
        finally
        {
            factory.Worker.VideoTitle = null;
        }
    }

    [Fact]
    public async Task Running_and_waiting_projects_can_be_cancelled_and_retried()
    {
        factory.Worker.Hold = true;
        ProjectDto running, waiting;
        try
        {
            running = (await (await CreateAsync(new() { ["url"] = "https://example.com/en-curso.mp3" })).Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            await WaitStatusAsync(running.Id, Domain.ProjectStatus.Downloading);
            waiting = (await (await CreateAsync(new() { ["url"] = "https://example.com/en-cola.mp3" })).Content.ReadFromJsonAsync<ProjectDto>(Json))!;

            var cancelledWaiting = await _client.PostAsync($"/api/projects/{waiting.Id}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelledWaiting.StatusCode);
            var dto = (await cancelledWaiting.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
            Assert.Equal(Domain.ProjectStatus.Failed, dto.Status);
            Assert.Equal("Cancelada.", dto.Error);

            var cancelledRunning = await _client.PostAsync($"/api/projects/{running.Id}/cancel", null);
            Assert.Equal("Cancelando…", (await cancelledRunning.Content.ReadFromJsonAsync<ProjectDto>(Json))!.Message);
            var ended = await WaitFinishedAsync(running.Id);
            Assert.Equal(Domain.ProjectStatus.Failed, ended.Status);
            Assert.Equal("Cancelada.", ended.Error);
        }
        finally
        {
            factory.Worker.Hold = false;
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/projects/{running.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/projects/{Guid.NewGuid()}/cancel", null)).StatusCode);

        // The one cancelled while waiting never reached the worker, and a retry is not cancelled by the old request.
        Assert.DoesNotContain(factory.Worker.Requests, r => r.GetProperty("url").GetString() == "https://example.com/en-cola.mp3");
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/projects/{waiting.Id}/retry", null)).StatusCode);
        Assert.Equal(Domain.ProjectStatus.Done, (await WaitFinishedAsync(waiting.Id)).Status);
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

    private async Task WaitStatusAsync(Guid id, Domain.ProjectStatus status)
    {
        for (var i = 0; i < 100; i++)
        {
            if ((await _client.GetFromJsonAsync<ProjectDto>($"/api/projects/{id}", Json))!.Status == status) return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"Project did not reach {status}");
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
