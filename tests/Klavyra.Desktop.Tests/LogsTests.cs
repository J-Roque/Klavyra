using Xunit;

namespace Klavyra.Desktop.Tests;

public sealed class LogsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("klavyra-logs").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("file:///C:/Users/ana/Music/cancion.mp3", true)]
    [InlineData("http://127.0.0.1:5200/projects/1", false)]
    [InlineData("https://huggingface.co/MuScriptor", false)]
    [InlineData("not a uri", false)]
    public void Local_files_never_replace_the_app(string uri, bool blocked) =>
        Assert.Equal(blocked, MainForm.IsLocalFile(uri));

    [Fact]
    public void The_log_of_the_previous_run_is_kept_for_diagnosis()
    {
        var current = Path.Combine(_folder, "worker.log");
        var previous = Path.Combine(_folder, "worker.anterior.log");

        ServiceHost.KeepPreviousLog(current); // first ever run: nothing to keep
        Assert.False(File.Exists(previous));

        File.WriteAllText(current, "se cayó por memoria");
        ServiceHost.KeepPreviousLog(current);
        Assert.Equal("se cayó por memoria", File.ReadAllText(previous));
        Assert.False(File.Exists(current));

        File.WriteAllText(current, "segundo arranque");
        ServiceHost.KeepPreviousLog(current);
        Assert.Equal("segundo arranque", File.ReadAllText(previous)); // only the last run, so logs do not pile up
    }

    [Fact]
    public void A_log_that_cannot_be_moved_does_not_stop_Klavyra()
    {
        var current = Path.Combine(_folder, "api.log");
        File.WriteAllText(current, "x");
        using var open = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.None);
        ServiceHost.KeepPreviousLog(current); // locked by another program: it is simply left alone
    }
}
