namespace Klavyra.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Two copies would start two workers fighting over the GPU and the same database.
        using var single = new Mutex(initiallyOwned: true, @"Local\Klavyra.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("Klavyra ya está abierto.", "Klavyra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(AppPaths.FromEnvironment()));
    }
}
