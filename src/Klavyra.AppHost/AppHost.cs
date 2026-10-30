var builder = DistributedApplication.CreateBuilder(args);

// Uploads, generated scores and the SQLite database live in <repo>/storage, shared by the API and the worker.
var storage = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "storage"));

var worker = builder.AddUvicornApp("worker", "../../worker", "klavyra_worker.app:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Klavyra_Api>("api")
    .WithEnvironment("Storage__Root", storage)
    .WithReference(worker);

builder.AddJavaScriptApp("web", "../../web")
    .WithRunScript("start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
