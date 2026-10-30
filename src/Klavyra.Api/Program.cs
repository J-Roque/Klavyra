using System.Text.Json.Serialization;
using Klavyra.Api.Features.Projects;
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
    await scope.ServiceProvider.GetRequiredService<KlavyraDb>().Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapProjects();
app.MapHub<ProgressHub>("/hubs/progress");

app.Run();

public partial class Program;
