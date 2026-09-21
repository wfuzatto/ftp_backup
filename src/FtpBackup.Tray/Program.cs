namespace FtpBackup.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var startMinimized = args.Any(x => x.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApplicationContext(startMinimized));
    }
}
