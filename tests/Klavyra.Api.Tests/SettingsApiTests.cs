using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Klavyra.Api.Tests;

public sealed class SettingsApiTests(KlavyraFactory factory) : IClassFixture<KlavyraFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Settings_show_the_version_and_what_the_worker_found()
    {
        var settings = await _client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("0.1.0", settings.GetProperty("version").GetString());
        Assert.True(settings.GetProperty("workerOnline").GetBoolean());
        Assert.Equal("RTX de prueba", settings.GetProperty("worker").GetProperty("gpu").GetString());
    }

    [Fact]
    public async Task Token_goes_to_the_worker_and_its_answer_comes_back()
    {
        factory.Worker.Replies["PUT /settings/hugging-face"] =
            (HttpStatusCode.OK, "{\"user\":\"johel\",\"access\":{\"small\":true,\"medium\":false,\"large\":null}}");
        factory.Worker.Replies["DELETE /settings/hugging-face"] = (HttpStatusCode.NoContent, "");

        var saved = await _client.PutAsJsonAsync("/api/settings/hugging-face", new { token = "hf_secreto" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("johel", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetString());
        Assert.Contains(factory.Worker.SettingsBodies, b => b.Contains("hf_secreto"));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/settings/hugging-face")).StatusCode);
    }

    [Fact]
    public async Task Worker_errors_keep_their_status_and_message()
    {
        factory.Worker.Replies["GET /settings/hugging-face"] = (HttpStatusCode.NotFound, "{\"detail\":\"No hay ningún token guardado.\"}");
        var check = await _client.GetAsync("/api/settings/hugging-face");
        Assert.Equal(HttpStatusCode.NotFound, check.StatusCode);
        Assert.Contains("No hay ningún token", await check.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Downloader_update_is_run_by_the_worker()
    {
        factory.Worker.Replies["POST /settings/downloader/update"] =
            (HttpStatusCode.OK, "{\"before\":\"2026.1.1\",\"after\":\"2026.11.3\",\"updated\":true}");
        var updated = await _client.PostAsync("/api/settings/downloader/update", null);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("2026.11.3", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("after").GetString());

        factory.Worker.Replies["POST /settings/downloader/update"] =
            (HttpStatusCode.Conflict, "{\"detail\":\"Hay una transcripción en curso. Actualiza cuando termine.\"}");
        var busy = await _client.PostAsync("/api/settings/downloader/update", null);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Contains("en curso", await busy.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_worker_that_is_not_running_is_reported()
    {
        factory.Worker.Replies.Remove("GET /health");
        try
        {
            var settings = await _client.GetFromJsonAsync<JsonElement>("/api/settings");
            Assert.False(settings.GetProperty("workerOnline").GetBoolean());

            factory.Worker.Replies.Remove("PUT /settings/hugging-face");
            var saved = await _client.PutAsJsonAsync("/api/settings/hugging-face", new { token = "x" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, saved.StatusCode);
        }
        finally
        {
            factory.Worker.Replies["GET /health"] = (HttpStatusCode.OK, "{\"status\":\"ok\",\"cuda\":true,\"gpu\":\"RTX de prueba\"}");
        }
    }
}
