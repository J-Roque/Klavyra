using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Klavyra.Api.Tests;

/// <summary>The API with a compiled web app in its web root, as the desktop build ships it.</summary>
public sealed class DesktopWebFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "klavyra-tests", Guid.NewGuid().ToString("N"));

    public string WebRoot => Path.Combine(_root, "wwwroot");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(WebRoot);
        File.WriteAllText(Path.Combine(WebRoot, "index.html"), "<app-root></app-root>");
        File.WriteAllText(Path.Combine(WebRoot, "main.js"), "console.log('klavyra')");
        builder.UseSetting("Storage:Root", Path.Combine(_root, "storage"));
        builder.UseSetting(WebHostDefaults.WebRootKey, WebRoot);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

public sealed class DesktopWebTests(DesktopWebFactory factory) : IClassFixture<DesktopWebFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Serves_the_app_and_its_files()
    {
        Assert.Contains("<app-root>", await _client.GetStringAsync("/"));
        Assert.Contains("klavyra", await _client.GetStringAsync("/main.js"));
    }

    [Fact]
    public async Task App_pages_load_the_app_when_reloaded()
    {
        var page = await _client.GetAsync("/projects/123");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<app-root>", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_keeps_working_and_wrong_api_addresses_stay_missing()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/no-existe")).StatusCode);
    }
}
