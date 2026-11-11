using System.Text.Json.Serialization;
using Klavyra.Api.Features.Projects;
using Klavyra.Api.Features.Settings;
using Klavyra.Api.Hubs;
using Klavyra.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

const long MaxUploadBytes = 512L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<FileStorage>();
builder.Services.AddDbContext<KlavyraDb>((sp, o) => o.UseSqlite(
    builder.Configuration.GetConnectionString("klavyra")
    ?? $"Data Source={Path.Combine(sp.GetRequiredService<FileStorage>().Root, "klavyra.db")}"));

builder.Services.AddSingleton<TranscriptionQueue>();
builder.Services.AddHostedService<TranscriptionWorker>();
// A transcription can take minutes and streams progress, so no timeouts or retries on this client.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
builder.Services.AddHttpClient<WorkerClient>(c =>
    {
        c.BaseAddress = new Uri("https+http://worker");
        c.Timeout = Timeout.InfiniteTimeSpan;
    })
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxUploadBytes);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DatabaseMigrator.MigrateAsync(
        scope.ServiceProvider.GetRequiredService<KlavyraDb>(),
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

app.UseExceptionHandler();

// The desktop build copies the compiled Angular app into wwwroot; in development Angular serves itself.
var servesWeb = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
if (servesWeb)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapProjects();
app.MapSettings();
app.MapHub<ProgressHub>("/hubs/progress");

if (servesWeb)
{
    // Angular routes such as /projects/123 are pages of the app; a wrong /api address stays a 404.
    app.MapFallback("/api/{*path}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
