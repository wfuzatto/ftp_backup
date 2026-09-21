namespace FtpBackup.Core.Services;

public static class AppPaths
{
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FtpBackup");

    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");
    public static string StateFile => Path.Combine(BaseDirectory, "state.json");
    public static string RunMarkerFile => Path.Combine(BaseDirectory, "run.lock");
    public static string LogsDirectory => Path.Combine(BaseDirectory, "logs");
    public static string StagingDirectory => Path.Combine(BaseDirectory, "staging");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(StagingDirectory);
    }
}
