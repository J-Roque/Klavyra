using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Klavyra.Desktop;

/// <summary>A problem the user can act on; the message is shown as is.</summary>
internal sealed class StartupException(string message) : Exception(message);

/// <summary>Starts the worker and the API on free local ports, waits until both answer, and stops them on dispose.</summary>
internal sealed class ServiceHost(AppPaths paths) : IDisposable
{
    private static readonly TimeSpan ApiStartup = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WorkerStartup = TimeSpan.FromMinutes(3); // loading PyTorch can take a while

    private readonly JobObject _job = new();
    private readonly List<(Process Process, StreamWriter Log)> _running = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public Uri? AppUri { get; private set; }

    /// <summary>Whether the worker found an NVIDIA GPU (CUDA); null if it did not say.</summary>
    public bool? HasGpu { get; private set; }

    public async Task StartAsync(IProgress<string> status, CancellationToken ct)
    {
        // A developer's own environment (KLAVYRA_PYTHON) is used as is.
        if (paths.PythonOverride is null)
        {
            var setup = new PythonSetup(paths);
            if (!setup.IsReady())
            {
                await setup.RunAsync(status, ct);
            }
        }
        if (!File.Exists(paths.Python))
        {
            throw new StartupException($"Falta el entorno de Python de Klavyra.\nSe esperaba en: {paths.Python}");
        }
        if (!File.Exists(paths.ApiExe))
        {
            throw new StartupException($"La instalación está incompleta: falta {paths.ApiExe}. Reinstala Klavyra.");
        }
        foreach (var dir in new[] { paths.Storage, paths.Models, paths.Logs })
        {
            Directory.CreateDirectory(dir);
        }

        var workerPort = FreePort();
        var apiPort = FreePort();
        status.Report("Iniciando Klavyra…");
        var worker = Start(ServiceStarts.Worker(paths, workerPort), "worker");
        var api = Start(ServiceStarts.Api(paths, apiPort, workerPort), "api");

        await WaitReadyAsync(api, "El servidor de Klavyra", $"{ServiceStarts.Url(apiPort)}/api/projects", ApiStartup, ct);
        status.Report("Preparando el motor de transcripción… (la primera vez tarda más)");
        var health = await WaitReadyAsync(worker, "El motor de transcripción", $"{ServiceStarts.Url(workerPort)}/health", WorkerStartup, ct);
        HasGpu = ReadCuda(health);
        AppUri = new Uri(ServiceStarts.Url(apiPort) + "/");
    }

    internal static bool? ReadCuda(string health)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(health);
            return json.RootElement.TryGetProperty("cuda", out var cuda) && cuda.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                ? cuda.GetBoolean()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Each start writes a new log, so the one from the run before is renamed first: after a crash, the user
    /// reopening Klavyra must not erase the evidence. Only the last run is kept.
    /// </summary>
    internal static void KeepPreviousLog(string log)
    {
        if (!File.Exists(log)) return;
        try
        {
            File.Move(log, Path.ChangeExtension(log, ".anterior.log"), overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Process Start(ProcessStartInfo start, string name)
    {
        var logPath = Path.Combine(paths.Logs, $"{name}.log");
        KeepPreviousLog(logPath);
        var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        log.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {start.FileName} {string.Join(' ', start.ArgumentList)}");
        var process = new Process { StartInfo = start };
        DataReceivedEventHandler write = (_, e) =>
        {
            if (e.Data is null) return;
            lock (log)
            {
                log.WriteLine(e.Data);
            }
        };
        process.OutputDataReceived += write;
        process.ErrorDataReceived += write;
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            log.Dispose();
            throw new StartupException($"No se pudo iniciar {start.FileName}: {e.Message}");
        }
        _job.Add(process);
        _running.Add((process, log));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>Waits for a successful answer and returns its body.</summary>
    private async Task<string> WaitReadyAsync(Process process, string label, string url, TimeSpan limit, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new StartupException(
                    $"{label} se cerró al arrancar (código {process.ExitCode}).\nLos detalles están en {paths.Logs}.");
            }
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(500, ct);
        }
        throw new StartupException($"{label} no respondió a tiempo.\nLos detalles están en {paths.Logs}.");
    }

    public void Dispose()
    {
        foreach (var (process, _) in _running)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        _job.Dispose(); // kills anything the line above missed
        foreach (var (process, log) in _running)
        {
            process.WaitForExit(2000);
            process.Dispose();
            lock (log)
            {
                log.Dispose();
            }
        }
        _running.Clear();
        _http.Dispose();
    }
}
