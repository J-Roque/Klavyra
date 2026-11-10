namespace Klavyra.Desktop;

/// <summary>
/// Where the installed program lives (read-only) and where the user's data goes (%LOCALAPPDATA%\Klavyra):
/// songs, models, logs, the browser profile and the Python environment.
/// </summary>
internal sealed record AppPaths(string InstallDir, string DataDir, string? PythonOverride = null)
{
    /// <summary>KLAVYRA_DATA and KLAVYRA_PYTHON let a developer point the launcher at other folders.</summary>
    public static AppPaths FromEnvironment() => new(
        AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("KLAVYRA_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klavyra"),
        Environment.GetEnvironmentVariable("KLAVYRA_PYTHON"));

    public string ApiDir => Path.Combine(InstallDir, "api");
    public string ApiExe => Path.Combine(ApiDir, "Klavyra.Api.exe");
    public string WorkerDir => Path.Combine(InstallDir, "worker");
    public string Ffmpeg => Path.Combine(InstallDir, "tools", "ffmpeg.exe");

    public string Storage => Path.Combine(DataDir, "storage");
    public string Models => Path.Combine(DataDir, "models");
    public string Logs => Path.Combine(DataDir, "logs");
    public string WebView => Path.Combine(DataDir, "webview");
    public string PythonEnv => Path.Combine(DataDir, "python", "env");
    public string Python => PythonOverride ?? Path.Combine(PythonEnv, "Scripts", "python.exe");
}
