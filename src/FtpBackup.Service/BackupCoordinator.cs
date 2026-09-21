using FtpBackup.Core.Models;
using FtpBackup.Core.Services;

namespace FtpBackup.Service;

public sealed class BackupCoordinator
{
    private readonly SettingsStore _settingsStore;
    private readonly StateStore _stateStore;
    private readonly ArchiveBuilder _archiveBuilder;
    private readonly FtpTransferService _ftp;
    private readonly LocalRetentionService _localRetention;
    private readonly LogStore _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupCoordinator(
        SettingsStore settingsStore,
        StateStore stateStore,
        ArchiveBuilder archiveBuilder,
        FtpTransferService ftp,
        LocalRetentionService localRetention,
        LogStore log)
    {
        _settingsStore = settingsStore;
        _stateStore = stateStore;
        _archiveBuilder = archiveBuilder;
        _ftp = ftp;
        _localRetention = localRetention;
        _log = log;
    }

    public bool IsRunning => _gate.CurrentCount == 0;

    public bool TryStart(string reason, CancellationToken serviceStoppingToken)
    {
        if (!_gate.Wait(0))
            return false;

        _ = Task.Run(
            () => RunOwnedAsync(reason, serviceStoppingToken),
            CancellationToken.None);

        return true;
    }

    private async Task RunOwnedAsync(string reason, CancellationToken token)
    {
        var keepRecoveryMarker = false;

        try
        {
            AppPaths.EnsureDirectories();
            await File.WriteAllTextAsync(
                AppPaths.RunMarkerFile,
                $"{DateTimeOffset.Now:O}|{reason}",
                CancellationToken.None);

            var settings = _settingsStore.Load();
            settings.NormalizeDefaults();

            var validation = BackupValidator.Validate(settings);
            if (validation.Count > 0)
                throw new InvalidOperationException(string.Join(" ", validation));

            UpdateState(state =>
            {
                state.Status = "Running";
                state.Stage = "Iniciando";
                state.ProgressPercent = 1;
                state.LastStart = DateTimeOffset.Now;
                state.LastEnd = null;
                state.LastError = null;
                state.LastRunReason = reason;
            });

            _log.Info($"Backup iniciado. Motivo={reason}; origem={settings.SourceFolder}");

            var archive = await _archiveBuilder.CreateAsync(
                settings,
                (percent, stage) => ReportProgress(percent, stage),
                token);

            UpdateState(state => state.LastArchivePath = archive.ArchivePath);

            var transfer = await _ftp.UploadWithRetryAsync(
                settings,
                archive,
                (percent, stage) => ReportProgress(percent, stage),
                token);

            UpdateState(state => state.LastRemotePath = transfer.RemotePath);

            ReportProgress(96, "Aplicando retenção");

            try
            {
                await _ftp.CleanupRemoteAsync(settings, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"Backup enviado, mas a retenção remota falhou: {ex.Message}");
            }

            try
            {
                _localRetention.Cleanup(settings);
            }
            catch (Exception ex)
            {
                _log.Warn($"Backup enviado, mas a retenção local falhou: {ex.Message}");
            }

            UpdateState(state =>
            {
                state.Status = "Idle";
                state.Stage = "Concluído";
                state.ProgressPercent = 100;
                state.LastEnd = DateTimeOffset.Now;
                state.LastSuccess = DateTimeOffset.Now;
                state.LastError = null;
            });

            _log.Info($"Backup concluído com sucesso. Remoto={transfer.RemotePath}");
        }
        catch (OperationCanceledException)
        {
            keepRecoveryMarker = true;
            UpdateState(state =>
            {
                state.Status = "Interrupted";
                state.Stage = "Interrompido; será recuperado no próximo início";
                state.LastEnd = DateTimeOffset.Now;
                state.LastError = "Execução interrompida por parada/reinício do serviço.";
            });
            _log.Warn("Backup interrompido por parada do serviço; marcador de recuperação mantido.");
        }
        catch (Exception ex)
        {
            UpdateState(state =>
            {
                state.Status = "Error";
                state.Stage = "Falhou";
                state.LastEnd = DateTimeOffset.Now;
                state.LastError = ex.Message;
            });
            _log.Error(ex, "Backup falhou");
        }
        finally
        {
            if (!keepRecoveryMarker)
            {
                try
                {
                    if (File.Exists(AppPaths.RunMarkerFile))
                        File.Delete(AppPaths.RunMarkerFile);
                }
                catch
                {
                }
            }

            _gate.Release();
        }
    }

    private void ReportProgress(int percent, string stage)
    {
        UpdateState(state =>
        {
            state.ProgressPercent = Math.Clamp(percent, 0, 100);
            state.Stage = stage;
        });
    }

    private void UpdateState(Action<BackupState> action) => _stateStore.Update(action);
}
