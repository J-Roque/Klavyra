using System.Diagnostics;
using System.Security.Cryptography;

namespace Klavyra.Desktop;

/// <summary>
/// First launch (and after an update that changes the worker's dependencies): uv downloads Python 3.12 and the
/// worker's packages, PyTorch included, into %LOCALAPPDATA%\Klavyra\python. A marker with the fingerprint of
/// uv.lock says the environment is complete; an interrupted install has no marker and simply runs again.
/// </summary>
internal sealed class PythonSetup(AppPaths paths)
{
    public const long FreshInstallBytes = 6L * 1024 * 1024 * 1024;
    public const long UpdateBytes = 1L * 1024 * 1024 * 1024;
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(2);

    private string Marker => Path.Combine(paths.DataDir, "python", "ready.txt");

    public bool IsReady() =>
        File.Exists(paths.Python) && File.Exists(Marker) && File.ReadAllText(Marker).Trim() == Fingerprint();

    /// <summary>Changes whenever a new version of Klavyra ships different dependencies.</summary>
    public string Fingerprint()
    {
        using var sha = SHA256.Create();
        foreach (var file in new[] { "uv.lock", "pyproject.toml" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(paths.WorkerDir, file));
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>Null when there is room; otherwise what to tell the user.</summary>
    internal static string? CheckSpace(long freeBytes, bool updating)
    {
        var needed = updating ? UpdateBytes : FreshInstallBytes;
        return freeBytes >= needed
            ? null
            : $"No hay espacio suficiente: Klavyra necesita unos {Gb(needed)} libres para instalar lo necesario " +
              $"y solo quedan {Gb(freeBytes)}. Libera espacio y pulsa Reintentar.";
    }

    internal static ProcessStartInfo Start(AppPaths paths)
    {
        var python = Path.Combine(paths.DataDir, "python");
        var start = new ProcessStartInfo(paths.Uv,
            ["sync", "--frozen", "--no-dev", "--no-install-project", "--project", paths.WorkerDir])
        {
            WorkingDirectory = paths.WorkerDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var env = start.Environment;
        env["UV_PROJECT_ENVIRONMENT"] = paths.PythonEnv;
        env["UV_PYTHON_INSTALL_DIR"] = Path.Combine(python, "interpreters");
        env["UV_CACHE_DIR"] = Path.Combine(python, "cache");
        env["UV_PYTHON_PREFERENCE"] = "only-managed"; // never pick up whatever Python the user has installed
        // PyTorch is a single ~2.5 GB file: on a slow or unsteady connection uv's 30 s default gives up mid-download.
        env["UV_HTTP_TIMEOUT"] = "300";
        env["UV_HTTP_RETRIES"] = "5";
        env["UV_CONCURRENT_DOWNLOADS"] = "4"; // fewer parallel downloads leave more bandwidth for the big one
        env["NO_COLOR"] = "1";
        return start;
    }

    /// <summary>uv's English lines, as a short Spanish description of what it is doing.</summary>
    internal static string? Describe(string line)
    {
        var text = line.Trim();
        if (text.StartsWith("Downloading cpython", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Installed Python", StringComparison.OrdinalIgnoreCase))
        {
            return "Descargando Python";
        }
        if (text.StartsWith("Downloading ", StringComparison.Ordinal))
        {
            return "Descargando " + text["Downloading ".Length..];
        }
        if (text.StartsWith("Prepared ", StringComparison.Ordinal) || text.StartsWith("Installed ", StringComparison.Ordinal))
        {
            return "Instalando los paquetes";
        }
        if (text.StartsWith("Resolved ", StringComparison.Ordinal) || text.StartsWith("Using CPython", StringComparison.Ordinal))
        {
            return "Preparando la instalación";
        }
        return null;
    }

    public async Task RunAsync(IProgress<string> status, CancellationToken ct)
    {
        if (!File.Exists(paths.Uv))
        {
            throw new StartupException($"La instalación está incompleta: falta {paths.Uv}. Reinstala Klavyra.");
        }
        var python = Directory.CreateDirectory(Path.Combine(paths.DataDir, "python")).FullName;
        Directory.CreateDirectory(paths.Logs);
        var updating = Directory.Exists(paths.PythonEnv);
        var problem = CheckSpace(new DriveInfo(Path.GetPathRoot(python)!).AvailableFreeSpace, updating);
        if (problem is not null)
        {
            throw new StartupException(problem);
        }
        File.Delete(Marker);

        var title = updating
            ? "Actualizando el motor de transcripción…"
            : "Instalando lo necesario para la IA (solo la primera vez, unos 3 GB)…";
        var stage = "Preparando la instalación";
        var tail = new Queue<string>();
        using var log = new StreamWriter(Path.Combine(paths.Logs, "python-setup.log"), append: false) { AutoFlush = true };
        using var job = new JobObject(); // closing Klavyra mid-install stops uv too
        using var process = new Process { StartInfo = Start(paths) };
        DataReceivedEventHandler read = (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (tail)
            {
                log.WriteLine(e.Data);
                tail.Enqueue(e.Data.Trim());
                if (tail.Count > 4) tail.Dequeue();
                stage = Describe(e.Data) ?? stage;
            }
        };
        process.OutputDataReceived += read;
        process.ErrorDataReceived += read;
        log.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {paths.Uv} {string.Join(' ', process.StartInfo.ArgumentList)}");
        process.Start();
        job.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // uv downloads into its cache and then hard-links the files into the environment, so counting the
        // environment too would show every byte twice.
        long Downloaded() => FolderBytes(Path.Combine(python, "cache")) + FolderBytes(Path.Combine(python, "interpreters"));
        var started = DateTime.UtcNow;
        var startBytes = Downloaded();
        while (!process.HasExited)
        {
            var bytes = Downloaded() - startBytes;
            var seconds = Math.Max(1, (DateTime.UtcNow - started).TotalSeconds);
            string current;
            lock (tail) current = stage;
            status.Report($"{title}\n\n{current}\n{Gb(Math.Max(0, bytes))} descargados ·{Mb(bytes / seconds)}/s · {Elapsed(DateTime.UtcNow - started)}");
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(ReportEvery, ct);
            }
            catch (TimeoutException) { }
        }
        process.WaitForExit(); // flush the last output lines

        if (process.ExitCode != 0)
        {
            string last;
            lock (tail) last = string.Join("\n", tail);
            throw new StartupException(
                "No se pudo instalar lo necesario para la IA. Comprueba tu conexión a internet y pulsa Reintentar.\n\n" + last);
        }
        File.WriteAllText(Marker, Fingerprint());
    }

    /// <summary>Approximate: Windows can report a file that is still being written as smaller than it is.</summary>
    private static long FolderBytes(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                total += file.Length;
            }
        }
        catch (IOException) { } // files come and go while uv works
        return total;
    }

    private static string Gb(double bytes) => $"{bytes / (1024 * 1024 * 1024):0.0} GB";
    private static string Mb(double bytes) => $"{bytes / (1024 * 1024):0.0} MB";
    private static string Elapsed(TimeSpan time) => time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} min {time.Seconds} s" : $"{time.Seconds} s";
}
