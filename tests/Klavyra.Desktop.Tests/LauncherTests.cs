using System.Diagnostics;
using Xunit;

namespace Klavyra.Desktop.Tests;

public sealed class LauncherTests
{
    private static readonly AppPaths Paths = new(@"C:\Programs\Klavyra", @"C:\Users\ana\AppData\Local\Klavyra");

    [Fact]
    public void User_data_stays_out_of_the_install_folder()
    {
        Assert.Equal(@"C:\Users\ana\AppData\Local\Klavyra\storage", Paths.Storage);
        Assert.Equal(@"C:\Users\ana\AppData\Local\Klavyra\python\env\Scripts\python.exe", Paths.Python);
        Assert.Equal(@"C:\Programs\Klavyra\api\Klavyra.Api.exe", Paths.ApiExe);
        Assert.Equal(@"D:\dev\python.exe", (Paths with { PythonOverride = @"D:\dev\python.exe" }).Python);
    }

    [Fact]
    public void Worker_gets_its_port_code_and_model_folders()
    {
        var start = ServiceStarts.Worker(Paths, 5123);
        Assert.Equal(Paths.Python, start.FileName);
        Assert.Equal(["-m", "klavyra_worker.app"], start.ArgumentList);
        Assert.Equal("5123", start.Environment["PORT"]);
        Assert.Equal(Paths.WorkerDir, start.Environment["PYTHONPATH"]);
        Assert.Equal(Paths.Models, start.Environment["KLAVYRA_MODELS"]);
        Assert.StartsWith(Paths.Models, start.Environment["HF_HOME"]);
        Assert.StartsWith(Paths.Models, start.Environment["TORCH_HOME"]);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void Api_serves_from_its_folder_and_knows_the_worker()
    {
        var start = ServiceStarts.Api(Paths, 5200, 5123);
        Assert.Equal(Paths.ApiDir, start.WorkingDirectory);
        Assert.Equal("http://127.0.0.1:5200", start.Environment["ASPNETCORE_URLS"]);
        Assert.Equal("http://127.0.0.1:5123", start.Environment["Services__worker__http__0"]);
        Assert.Equal(Paths.Storage, start.Environment["Storage__Root"]);
        Assert.Equal("Production", start.Environment["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public void Free_ports_are_different_and_local()
    {
        var a = ServiceHost.FreePort();
        Assert.InRange(a, 1024, 65535);
        Assert.NotEqual(0, ServiceHost.FreePort());
    }

    [Theory]
    [InlineData("http://127.0.0.1:5200/projects/1", false)]
    [InlineData("blob:http://127.0.0.1:5200/abc", false)]
    [InlineData("https://huggingface.co/MuScriptor", true)]
    [InlineData("http://127.0.0.1:9999/", true)]
    public void Only_other_sites_open_in_the_browser(string uri, bool external)
    {
        Assert.Equal(external, MainForm.IsExternal(uri, new Uri("http://127.0.0.1:5200/")));
    }

    [Fact]
    public void Closing_the_job_kills_its_processes_and_their_children()
    {
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 > nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        var job = new JobObject();
        job.Add(parent);

        job.Dispose();

        Assert.True(parent.WaitForExit(5000), "the process should die with the job");
    }
}
