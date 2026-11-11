using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Klavyra.Desktop;

/// <summary>Shows "Iniciando…" while the services start, then the Klavyra web app inside the window.</summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(20, 26, 24);

    private readonly AppPaths _paths;
    private ServiceHost _host;
    private Uri? _app;
    private bool _updateChecked;
    private readonly CancellationTokenSource _closing = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Background };
    private readonly Panel _splash = new() { Dock = DockStyle.Fill, BackColor = Background };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.Gainsboro,
        Font = new Font("Segoe UI", 12f),
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly Button _openLogs = new() { Text = "Abrir carpeta de registros", AutoSize = true, Visible = false };
    private readonly Button _retry = new() { Text = "Reintentar", AutoSize = true, Visible = false };

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _host = new ServiceHost(paths);

        Text = "Klavyra";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
        _openLogs.Click += (_, _) => OpenExternal(_paths.Logs);
        _retry.Click += async (_, _) => await StartAsync();
        foreach (var button in new[] { _retry, _openLogs })
        {
            button.ForeColor = Color.Black;
            button.BackColor = Color.Gainsboro;
            bottom.Controls.Add(button);
        }
        _splash.Controls.Add(_status);
        _splash.Controls.Add(bottom);
        Controls.Add(_web);
        Controls.Add(_splash);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await StartAsync();
    }

    private async Task StartAsync()
    {
        _retry.Visible = false;
        _openLogs.Visible = false;
        _status.ForeColor = Color.Gainsboro;
        _status.Text = "Iniciando Klavyra…";
        // A retry starts from a clean slate: whatever the failed attempt left running is stopped first.
        _host.Dispose();
        _host = new ServiceHost(_paths);
        try
        {
            await _host.StartAsync(new Progress<string>(text => _status.Text = text), _closing.Token);
            _app = _host.AppUri!;
            if (_web.CoreWebView2 is null)
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, _paths.WebView);
                await _web.EnsureCoreWebView2Async(environment);
                Configure(_web.CoreWebView2!);
            }
            _web.Source = _app;
            _web.Visible = true;
            _splash.Visible = false;
            WarnOnceWithoutGpu();
            _ = NotifyNewVersionAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window was closed while starting.
        }
        catch (StartupException ex)
        {
            ShowError(ex.Message);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("Falta Microsoft Edge WebView2, que Klavyra usa para mostrar su ventana.\n" +
                      "Descárgalo gratis en https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelve a abrir Klavyra.");
        }
        catch (Exception ex)
        {
            ShowError($"Klavyra no pudo arrancar: {ex.Message}");
        }
    }

    /// <summary>Once per window; the handlers read <see cref="_app"/>, whose port changes after a retry.</summary>
    private void Configure(CoreWebView2 web)
    {
        web.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        web.Settings.AreDevToolsEnabled = false;
#endif
        web.DocumentTitleChanged += (_, _) => Text = string.IsNullOrWhiteSpace(web.DocumentTitle) ? "Klavyra" : web.DocumentTitle;
        // Links to other sites (Hugging Face, help pages…) open in the user's browser, not inside Klavyra.
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        web.NavigationStarting += (_, e) =>
        {
            if (_app is not null && IsExternal(e.Uri, _app))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
    }

    internal static bool IsExternal(string uri, Uri app) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var target)
        && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)
        && !string.Equals(target.GetLeftPart(UriPartial.Authority), app.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private void ShowError(string message)
    {
        _status.Text = message;
        _status.ForeColor = Color.FromArgb(255, 170, 160);
        _openLogs.Visible = true;
        _retry.Visible = true;
    }

    private void WarnOnceWithoutGpu()
    {
        var shown = Path.Combine(_paths.DataDir, "aviso-sin-gpu.txt");
        if (_host.HasGpu != false || File.Exists(shown)) return;
        File.WriteAllText(shown, DateTime.Now.ToString("O"));
        MessageBox.Show(this,
            "No se encontró una tarjeta gráfica NVIDIA compatible.\n\n" +
            "Klavyra funciona igual con el modelo ByteDance, pero cada transcripción tardará más (unos minutos). " +
            "MuScriptor necesita una GPU NVIDIA.",
            "Klavyra", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Tells the user once about each new release; any failure (offline, private repository…) is silent.</summary>
    private async Task NotifyNewVersionAsync()
    {
        if (_updateChecked) return;
        _updateChecked = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var release = await UpdateCheck.FindNewerAsync(http, UpdateCheck.Current, _closing.Token);
            var announced = Path.Combine(_paths.DataDir, "aviso-version.txt");
            if (release is null || IsDisposed) return;
            if (File.Exists(announced) && File.ReadAllText(announced).Trim() == release.Tag) return;
            File.WriteAllText(announced, release.Tag);
            var answer = MessageBox.Show(this,
                $"Hay una versión nueva de Klavyra: {release.Version} (tienes la {UpdateCheck.Current}).\n\n" +
                "¿Abrir la página de descarga? Instálala encima de esta: tus proyectos y modelos se conservan.",
                "Klavyra", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes) OpenExternal(release.PageUrl);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
        }
    }

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only when the user closes the window; Windows shutting down or an update must not be held back.
        if (e.CloseReason == CloseReason.UserClosing && _app is not null && _web.Visible && CloseGuard.Unfinished(_app) > 0)
        {
            var answer = MessageBox.Show(this, CloseGuard.Question, "Klavyra", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _closing.Cancel();
        _host.Dispose();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
