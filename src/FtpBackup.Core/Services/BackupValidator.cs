using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public static class BackupValidator
{
    public static IReadOnlyList<string> Validate(BackupSettings settings)
    {
        settings.NormalizeDefaults();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.SourceFolder))
            errors.Add("Selecione a pasta de origem.");
        else if (!Directory.Exists(settings.SourceFolder))
            errors.Add("A pasta de origem não existe.");

        if (string.IsNullOrWhiteSpace(settings.StagingFolder))
            errors.Add("Selecione a pasta de staging.");

        if (string.IsNullOrWhiteSpace(settings.FtpHost))
            errors.Add("Informe o servidor FTP/FTPS.");

        if (settings.FtpPort is < 1 or > 65535)
            errors.Add("A porta FTP é inválida.");

        try
        {
            if (!string.IsNullOrWhiteSpace(settings.SourceFolder) &&
                !string.IsNullOrWhiteSpace(settings.StagingFolder))
            {
                var source = EnsureTrailingSeparator(Path.GetFullPath(settings.SourceFolder));
                var staging = EnsureTrailingSeparator(Path.GetFullPath(settings.StagingFolder));

                if (staging.StartsWith(source, StringComparison.OrdinalIgnoreCase))
                    errors.Add("A pasta de staging não pode ficar dentro da pasta que será compactada.");
            }
        }
        catch
        {
            errors.Add("Origem ou staging contém um caminho inválido.");
        }

        return errors;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
