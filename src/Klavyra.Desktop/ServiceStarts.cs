using System.Diagnostics;

namespace Klavyra.Desktop;

/// <summary>How to start the worker and the API: the same settings Aspire passes them during development.</summary>
internal static class ServiceStarts
{
    public static ProcessStartInfo Worker(AppPaths paths, int port)
    {
        var start = Hidden(paths.Python, paths.WorkerDir, "-m", "klavyra_worker.app");
        var env = start.Environment;
        env["PORT"] = port.ToString();
        env["PYTHONPATH"] = paths.WorkerDir; // the worker code shipped with this version, not an older copy
        env["PYTHONUTF8"] = "1";
        env["KLAVYRA_MODELS"] = paths.Models;
        env["HF_HOME"] = Path.Combine(paths.Models, "huggingface");
        env["TORCH_HOME"] = Path.Combine(paths.Models, "torch");
        if (File.Exists(paths.Ffmpeg))
        {
            env["FFMPEG"] = paths.Ffmpeg;
        }
        if (File.Exists(paths.Uv))
        {
            env["KLAVYRA_UV"] = paths.Uv; // lets Ajustes update the link downloader without reinstalling
        }
        return start;
    }

    public static ProcessStartInfo Api(AppPaths paths, int port, int workerPort)
    {
        // The working directory is the content root, so the API finds its wwwroot (the web app) there.
        var start = Hidden(paths.ApiExe, paths.ApiDir);
        var env = start.Environment;
        env["ASPNETCORE_URLS"] = Url(port);
        env["ASPNETCORE_ENVIRONMENT"] = "Production";
        env["Storage__Root"] = paths.Storage;
        env["Services__worker__http__0"] = Url(workerPort);
        return start;
    }

    public static string Url(int port) => $"http://127.0.0.1:{port}";

    private static ProcessStartInfo Hidden(string file, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return start;
    }
}
