using System.IO.Compression;
using System.Security.Cryptography;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public sealed record ArchiveResult(
    string ArchivePath,
    string Sha256,
    int FilesAdded,
    int FilesSkipped,
    long SourceBytes);

public sealed class ArchiveBuilder
{
    private readonly LogStore _log;

    public ArchiveBuilder(LogStore log) => _log = log;

    public async Task<ArchiveResult> CreateAsync(
        BackupSettings settings,
        Action<int, string>? progress,
        CancellationToken token)
    {
        var errors = BackupValidator.Validate(settings);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        Directory.CreateDirectory(settings.StagingFolder);

        progress?.Invoke(3, "Mapeando arquivos");
        var files = EnumerateFiles(settings.SourceFolder, settings.FailOnUnreadableFile, token, out var skippedDuringScan);
        var sourceBytes = files.Sum(x => x.Length);

        EnsureFreeSpace(settings.StagingFolder, sourceBytes);

        var safePrefix = SanitizeFileName(settings.ArchivePrefix);
        var archiveName = $"{safePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.MachineName}.zip";
        var archivePath = Path.Combine(settings.StagingFolder, archiveName);
        var tempPath = archivePath + ".building";

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        var added = 0;
        var skipped = skippedDuringScan;

        try
        {
            await using var zipStream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                for (var i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = files[i];
                    var pct = files.Count == 0 ? 45 : 5 + (int)((i + 1) * 45L / files.Count);
                    progress?.Invoke(pct, $"Compactando {i + 1}/{files.Count}");

                    try
                    {
                        var relative = Path.GetRelativePath(settings.SourceFolder, item.Path)
                            .Replace(Path.DirectorySeparatorChar, '/');

                        var entry = archive.CreateEntry(relative, settings.CompressionLevel);
                        try
                        {
                            entry.LastWriteTime = File.GetLastWriteTime(item.Path);
                        }
                        catch
                        {
                        }

                        await using var input = new FileStream(
                            item.Path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            1024 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);

                        await using var output = entry.Open();
                        await input.CopyToAsync(output, 1024 * 1024, token);
                        added++;
                    }
                    catch (Exception ex) when (!settings.FailOnUnreadableFile && ex is not OperationCanceledException)
                    {
                        skipped++;
                        _log.Warn($"Arquivo ignorado durante ZIP: {item.Path}. Motivo: {ex.Message}");
                    }
                }
            }

            await zipStream.FlushAsync(token);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        File.Move(tempPath, archivePath, true);

        progress?.Invoke(52, "Calculando SHA-256");
        string sha;
        await using (var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using var hasher = SHA256.Create();
            var hash = await hasher.ComputeHashAsync(stream, token);
            sha = Convert.ToHexString(hash).ToLowerInvariant();
        }

        if (settings.UploadSha256File)
        {
            await File.WriteAllTextAsync(
                archivePath + ".sha256",
                $"{sha}  {Path.GetFileName(archivePath)}{Environment.NewLine}",
                token);
        }

        _log.Info($"ZIP criado: {archivePath}; arquivos={added}; ignorados={skipped}; origem={sourceBytes} bytes; sha256={sha}");
        return new ArchiveResult(archivePath, sha, added, skipped, sourceBytes);
    }

    private List<FileItem> EnumerateFiles(
        string root,
        bool failOnUnreadable,
        CancellationToken token,
        out int skipped)
    {
        skipped = 0;
        var result = new List<FileItem>();
        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(root));

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            try
            {
                foreach (var child in Directory.EnumerateDirectories(dir))
                    stack.Push(child);
            }
            catch (Exception ex) when (!failOnUnreadable && ex is not OperationCanceledException)
            {
                skipped++;
                _log.Warn($"Diretório ignorado: {dir}. Motivo: {ex.Message}");
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        result.Add(new FileItem(file, info.Length));
                    }
                    catch (Exception ex) when (!failOnUnreadable && ex is not OperationCanceledException)
                    {
                        skipped++;
                        _log.Warn($"Arquivo ignorado no mapeamento: {file}. Motivo: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (!failOnUnreadable && ex is not OperationCanceledException)
            {
                skipped++;
                _log.Warn($"Falha ao listar arquivos em {dir}: {ex.Message}");
            }
        }

        return result;
    }

    private static void EnsureFreeSpace(string stagingFolder, long sourceBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(stagingFolder));
        if (string.IsNullOrWhiteSpace(root))
            return;

        var drive = new DriveInfo(root);
        const long reserve = 256L * 1024 * 1024;

        // ZIP de dados já comprimidos pode ficar próximo do tamanho original.
        if (drive.AvailableFreeSpace < sourceBytes + reserve)
            throw new IOException(
                $"Espaço insuficiente no staging. Livre: {drive.AvailableFreeSpace:N0} bytes; " +
                $"recomendado: pelo menos {sourceBytes + reserve:N0} bytes.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "backup" : clean;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record FileItem(string Path, long Length);
}
