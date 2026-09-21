using System.Text;

namespace FtpBackup.Core.Services;

public sealed class LogStore
{
    public LogStore() => AppPaths.EnsureDirectories();

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void Error(Exception ex, string context) =>
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}");

    public void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var path = Path.Combine(AppPaths.LogsDirectory, $"ftpbackup-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(line);

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch
        {
        }
    }
}
