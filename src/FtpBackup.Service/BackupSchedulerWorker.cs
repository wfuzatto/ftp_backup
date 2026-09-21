using FtpBackup.Core.Services;

namespace FtpBackup.Service;

public sealed class BackupSchedulerWorker : BackgroundService
{
    private readonly SettingsStore _settingsStore;
    private readonly StateStore _stateStore;
    private readonly BackupCoordinator _coordinator;
    private readonly LogStore _log;

    public BackupSchedulerWorker(
        SettingsStore settingsStore,
        StateStore stateStore,
        BackupCoordinator coordinator,
        LogStore log)
    {
        _settingsStore = settingsStore;
        _stateStore = stateStore;
        _coordinator = coordinator;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AppPaths.EnsureDirectories();
        _log.Info("FtpBackupService iniciado.");

        if (File.Exists(AppPaths.RunMarkerFile))
        {
            _log.Warn("Execução interrompida detectada. Iniciando recuperação.");
            _coordinator.TryStart("Recovery", stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Erro no scheduler");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private void Tick(CancellationToken stoppingToken)
    {
        var settings = _settingsStore.Load();
        settings.NormalizeDefaults();
        var state = _stateStore.Load();

        if (!settings.Enabled)
        {
            if (state.NextScheduledLocal is not null || state.ScheduleKey is not null)
            {
                state.NextScheduledLocal = null;
                state.ScheduleKey = null;
                _stateStore.Save(state);
            }
            return;
        }

        var key = BackupScheduler.GetScheduleKey(settings);

        if (!string.Equals(state.ScheduleKey, key, StringComparison.Ordinal) ||
            state.NextScheduledLocal is null)
        {
            state.ScheduleKey = key;
            state.NextScheduledLocal = BackupScheduler.GetNextOccurrence(DateTime.Now, settings);
            _stateStore.Save(state);
            _log.Info($"Próximo backup agendado: {state.NextScheduledLocal:yyyy-MM-dd HH:mm:ss}");
            return;
        }

        var now = DateTime.Now;
        if (now < state.NextScheduledLocal.Value || _coordinator.IsRunning)
            return;

        var occurrence = state.NextScheduledLocal.Value;
        var next = BackupScheduler.GetNextOccurrence(occurrence.AddSeconds(1), settings);

        state.NextScheduledLocal = next;
        state.ScheduleKey = key;
        _stateStore.Save(state);

        if (!_coordinator.TryStart($"Scheduled:{occurrence:yyyy-MM-dd HH:mm:ss}", stoppingToken))
        {
            state = _stateStore.Load();
            state.NextScheduledLocal = occurrence;
            _stateStore.Save(state);
            return;
        }

        _log.Info($"Backup agendado disparado para ocorrência {occurrence:yyyy-MM-dd HH:mm:ss}. Próximo={next:yyyy-MM-dd HH:mm:ss}");
    }
}
