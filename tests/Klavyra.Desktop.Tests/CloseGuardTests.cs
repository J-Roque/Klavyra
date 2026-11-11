using Xunit;

namespace Klavyra.Desktop.Tests;

public sealed class CloseGuardTests
{
    [Fact]
    public void Only_projects_that_have_not_finished_hold_the_window()
    {
        Assert.Equal(0, CloseGuard.Unfinished("[]"));
        Assert.Equal(0, CloseGuard.Unfinished("""[{"status":"Done"},{"status":"Failed"}]"""));
        Assert.Equal(2, CloseGuard.Unfinished("""[{"status":"Done"},{"status":"Queued"},{"status":"Transcribing"}]"""));
        Assert.Equal(0, CloseGuard.Unfinished("""{"detail":"error"}"""));
    }

    [Fact]
    public void An_api_that_does_not_answer_does_not_block_closing() =>
        Assert.Equal(0, CloseGuard.Unfinished(new Uri("http://127.0.0.1:9/")));

    [Fact]
    public void Worker_learns_where_uv_is_only_when_it_is_shipped()
    {
        var folder = Directory.CreateTempSubdirectory("klavyra-uv").FullName;
        try
        {
            var paths = new AppPaths(folder, Path.Combine(folder, "data"));
            Assert.False(ServiceStarts.Worker(paths, 5000).Environment.ContainsKey("KLAVYRA_UV"));
            Directory.CreateDirectory(Path.GetDirectoryName(paths.Uv)!);
            File.WriteAllBytes(paths.Uv, []);
            Assert.Equal(paths.Uv, ServiceStarts.Worker(paths, 5000).Environment["KLAVYRA_UV"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
