using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public sealed class LocalRetentionService
{
    private readonly LogStore _log;

    public LocalRetentionService(LogStore log) => _log = log;

    public void Cleanup(BackupSettings settings)
    {
        if (settings.LocalRetentionCount <= 0 || !Directory.Exists(settings.StagingFolder))
            return;

        var prefix = Sanitize(settings.ArchivePrefix) + "_";
        var archives = Directory.EnumerateFiles(settings.StagingFolder, "*.zip", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in archives.Skip(settings.LocalRetentionCount))
        {
            TryDelete(path);
            TryDelete(path + ".sha256");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            File.Delete(path);
            _log.Info($"Retenção local removeu: {path}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Não foi possível remover {path}: {ex.Message}");
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "backup" : clean;
    }
}
