using Xunit;

namespace Klavyra.Desktop.Tests;

public sealed class PythonSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "klavyra-desktop-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public PythonSetupTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "install"), Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.WorkerDir);
        File.WriteAllText(Path.Combine(_paths.WorkerDir, "uv.lock"), "version = 1");
        File.WriteAllText(Path.Combine(_paths.WorkerDir, "pyproject.toml"), "[project]");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Environment_is_ready_only_with_python_and_a_matching_marker()
    {
        var setup = new PythonSetup(_paths);
        Assert.False(setup.IsReady());

        Directory.CreateDirectory(Path.GetDirectoryName(_paths.Python)!);
        File.WriteAllText(_paths.Python, "");
        Assert.False(setup.IsReady()); // an interrupted install has python.exe but no marker

        File.WriteAllText(Path.Combine(_paths.DataDir, "python", "ready.txt"), setup.Fingerprint());
        Assert.True(setup.IsReady());

        File.WriteAllText(Path.Combine(_paths.WorkerDir, "uv.lock"), "version = 2"); // a new Klavyra version
        Assert.False(setup.IsReady());
    }

    [Fact]
    public void Uv_installs_into_klavyra_folders_without_building_the_worker()
    {
        var start = PythonSetup.Start(_paths);
        Assert.Equal(_paths.Uv, start.FileName);
        Assert.Equal(["sync", "--frozen", "--no-dev", "--no-install-project", "--project", _paths.WorkerDir], start.ArgumentList);
        Assert.Equal(_paths.PythonEnv, start.Environment["UV_PROJECT_ENVIRONMENT"]);
        Assert.StartsWith(_paths.DataDir, start.Environment["UV_PYTHON_INSTALL_DIR"]);
        Assert.StartsWith(_paths.DataDir, start.Environment["UV_CACHE_DIR"]);
        Assert.Equal("only-managed", start.Environment["UV_PYTHON_PREFERENCE"]);
        Assert.Equal("300", start.Environment["UV_HTTP_TIMEOUT"]);
        Assert.Equal("5", start.Environment["UV_HTTP_RETRIES"]);
    }

    [Theory]
    [InlineData("Downloading cpython-3.12.12-windows-x86_64-none (download) (20.1MiB)", "Descargando Python")]
    [InlineData("Downloading torch (2.3GiB)", "Descargando torch (2.3GiB)")]
    [InlineData("Prepared 118 packages in 2m 10s", "Instalando los paquetes")]
    [InlineData("Resolved 118 packages in 5ms", "Preparando la instalación")]
    [InlineData(" Downloaded numpy", null)]
    public void Uv_lines_become_short_spanish_stages(string line, string? expected)
    {
        Assert.Equal(expected, PythonSetup.Describe(line));
    }

    [Fact]
    public void Low_disk_space_is_explained_before_downloading()
    {
        Assert.Null(PythonSetup.CheckSpace(PythonSetup.FreshInstallBytes, updating: false));
        Assert.Contains("No hay espacio suficiente", PythonSetup.CheckSpace(2L << 30, updating: false));
        Assert.Null(PythonSetup.CheckSpace(2L << 30, updating: true));
    }

    [Theory]
    [InlineData("{\"status\":\"ok\",\"cuda\":true}", true)]
    [InlineData("{\"status\":\"ok\",\"cuda\":false}", false)]
    [InlineData("{\"status\":\"ok\"}", null)]
    [InlineData("no es json", null)]
    public void Worker_health_says_whether_there_is_a_gpu(string health, bool? expected)
    {
        Assert.Equal(expected, ServiceHost.ReadCuda(health));
    }
}
