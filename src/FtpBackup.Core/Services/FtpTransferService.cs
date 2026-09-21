using System.Net.Security;
using FluentFTP;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public sealed record TransferResult(string RemotePath, long Bytes);

public sealed class FtpTransferService
{
    private readonly SettingsStore _settingsStore;
    private readonly LogStore _log;

    public FtpTransferService(SettingsStore settingsStore, LogStore log)
    {
        _settingsStore = settingsStore;
        _log = log;
    }

    public async Task<string> TestConnectionAsync(BackupSettings settings, CancellationToken token)
    {
        settings.NormalizeDefaults();
        var password = _settingsStore.UnprotectPassword(settings.EncryptedPassword);
        await using var client = CreateClient(settings, password);

        await client.Connect(token);
        var encrypted = client.IsEncrypted ? "sim" : "não";
        var working = await client.GetWorkingDirectory(token);
        await client.Disconnect(token);

        return $"Conexão OK. TLS: {encrypted}. Diretório atual: {working}";
    }

    public async Task<TransferResult> UploadWithRetryAsync(
        BackupSettings settings,
        ArchiveResult archive,
        Action<int, string>? progress,
        CancellationToken token)
    {
        Exception? last = null;
        var maxAttempts = Math.Max(1, settings.RetryCount + 1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                if (attempt > 1)
                    _log.Warn($"Nova tentativa de upload {attempt}/{maxAttempts}.");

                return await UploadOnceAsync(settings, archive, progress, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                _log.Error(ex, $"Upload FTP falhou na tentativa {attempt}/{maxAttempts}");

                if (attempt < maxAttempts)
                {
                    progress?.Invoke(55, $"FTP falhou; nova tentativa em {settings.RetryDelaySeconds}s");
                    await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), token);
                }
            }
        }

        throw new IOException("Falha no upload FTP após todas as tentativas.", last);
    }

    public async Task CleanupRemoteAsync(BackupSettings settings, CancellationToken token)
    {
        if (settings.RemoteRetentionCount <= 0)
            return;

        var password = _settingsStore.UnprotectPassword(settings.EncryptedPassword);
        await using var client = CreateClient(settings, password);
        await client.Connect(token);

        try
        {
            var listing = await client.GetListing(settings.RemoteFolder, token: token);
            var prefix = SanitizeRemotePrefix(settings.ArchivePrefix) + "_";

            var archives = listing
                .Where(x => x.Type == FtpObjectType.File &&
                            x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var old in archives.Skip(settings.RemoteRetentionCount))
            {
                token.ThrowIfCancellationRequested();
                await client.DeleteFile(old.FullName, token);

                var sidecar = old.FullName + ".sha256";
                if (await client.FileExists(sidecar, token))
                    await client.DeleteFile(sidecar, token);

                _log.Info($"Retenção remota removeu: {old.FullName}");
            }
        }
        finally
        {
            if (client.IsConnected)
                await client.Disconnect(token);
        }
    }

    private async Task<TransferResult> UploadOnceAsync(
        BackupSettings settings,
        ArchiveResult archive,
        Action<int, string>? progress,
        CancellationToken token)
    {
        var password = _settingsStore.UnprotectPassword(settings.EncryptedPassword);
        await using var client = CreateClient(settings, password);
        await client.Connect(token);

        try
        {
            var fileName = Path.GetFileName(archive.ArchivePath);
            var remoteFinal = CombineRemote(settings.RemoteFolder, fileName);
            var remoteTemp = remoteFinal + ".uploading";

            progress?.Invoke(58, "Enviando ZIP ao FTP");

            var ftpProgress = new Progress<FtpProgress>(p =>
            {
                var percent = 58 + (int)Math.Clamp(p.Progress * 0.34, 0, 34);
                progress?.Invoke(percent, $"Enviando ZIP {Math.Clamp(p.Progress, 0, 100):0.0}%");
            });

            var status = await client.UploadFile(
                archive.ArchivePath,
                remoteTemp,
                FtpRemoteExists.Overwrite,
                createRemoteDir: true,
                verifyOptions: FtpVerify.Retry | FtpVerify.Size,
                progress: ftpProgress,
                token: token);

            if (status != FtpStatus.Success && status != FtpStatus.Skipped)
                throw new IOException($"Servidor FTP retornou status de upload: {status}");

            progress?.Invoke(93, "Finalizando arquivo remoto");
            var moved = await client.MoveFile(remoteTemp, remoteFinal, FtpRemoteExists.Overwrite, token);
            if (!moved)
                throw new IOException("O servidor recebeu o ZIP, mas não conseguiu renomear o arquivo temporário.");

            if (settings.UploadSha256File)
            {
                var localSha = archive.ArchivePath + ".sha256";
                var remoteSha = remoteFinal + ".sha256";
                var remoteShaTemp = remoteSha + ".uploading";

                var shaStatus = await client.UploadFile(
                    localSha,
                    remoteShaTemp,
                    FtpRemoteExists.Overwrite,
                    createRemoteDir: true,
                    verifyOptions: FtpVerify.Retry | FtpVerify.Size,
                    token: token);

                if (shaStatus != FtpStatus.Success && shaStatus != FtpStatus.Skipped)
                    throw new IOException($"Falha ao enviar SHA-256: {shaStatus}");

                if (!await client.MoveFile(remoteShaTemp, remoteSha, FtpRemoteExists.Overwrite, token))
                    throw new IOException("Falha ao finalizar o arquivo SHA-256 remoto.");
            }

            var size = new FileInfo(archive.ArchivePath).Length;
            _log.Info($"Upload concluído: {remoteFinal}; bytes={size}; sha256={archive.Sha256}");
            return new TransferResult(remoteFinal, size);
        }
        finally
        {
            if (client.IsConnected)
                await client.Disconnect(token);
        }
    }

    private AsyncFtpClient CreateClient(BackupSettings settings, string password)
    {
        var (host, port) = ParseHostAndPort(settings);
        var client = new AsyncFtpClient(host, settings.FtpUsername, password, port);

        client.Config.EncryptionMode = settings.SecurityMode switch
        {
            FtpSecurityMode.Plain => FtpEncryptionMode.None,
            FtpSecurityMode.ExplicitTls => FtpEncryptionMode.Explicit,
            FtpSecurityMode.ImplicitTls => FtpEncryptionMode.Implicit,
            FtpSecurityMode.Auto => FtpEncryptionMode.Auto,
            _ => FtpEncryptionMode.Auto
        };

        client.Config.DataConnectionType = settings.PassiveMode
            ? FtpDataConnectionType.AutoPassive
            : FtpDataConnectionType.AutoActive;

        client.Config.ConnectTimeout = settings.ConnectTimeoutSeconds * 1000;
        client.Config.ReadTimeout = settings.ReadTimeoutSeconds * 1000;
        client.Config.DataConnectionConnectTimeout = settings.ConnectTimeoutSeconds * 1000;
        client.Config.DataConnectionReadTimeout = settings.ReadTimeoutSeconds * 1000;
        client.Config.RetryAttempts = 1;

        client.ValidateCertificate += (_, e) =>
        {
            e.Accept = settings.AllowInvalidCertificate || e.PolicyErrors == SslPolicyErrors.None;
        };

        return client;
    }

    private static (string Host, int Port) ParseHostAndPort(BackupSettings settings)
    {
        var value = settings.FtpHost.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("ftps", StringComparison.OrdinalIgnoreCase)))
        {
            var port = uri.IsDefaultPort ? settings.FtpPort : uri.Port;
            return (uri.Host, port);
        }

        return (value, settings.FtpPort);
    }

    private static string CombineRemote(string folder, string fileName)
    {
        if (folder == "/")
            return "/" + fileName;

        return folder.TrimEnd('/') + "/" + fileName;
    }

    private static string SanitizeRemotePrefix(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "backup" : clean;
    }
}
