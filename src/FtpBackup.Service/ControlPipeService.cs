using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using FtpBackup.Core.Ipc;
using FtpBackup.Core.Services;

namespace FtpBackup.Service;

public sealed class ControlPipeService : BackgroundService
{
    private readonly StateStore _stateStore;
    private readonly SettingsStore _settingsStore;
    private readonly BackupCoordinator _coordinator;
    private readonly FtpTransferService _ftp;
    private readonly LogStore _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ControlPipeService(
        StateStore stateStore,
        SettingsStore settingsStore,
        BackupCoordinator coordinator,
        FtpTransferService ftp,
        LogStore log)
    {
        _stateStore = stateStore;
        _settingsStore = settingsStore;
        _coordinator = coordinator;
        _ftp = ftp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Erro no canal de controle local");
                try { await Task.Delay(1000, stoppingToken); } catch { }
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            ControlProtocol.PipeName,
            PipeDirection.InOut,
            5,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(line))
            return;

        ControlResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<ControlRequest>(line, _json)
                          ?? throw new InvalidDataException("Comando vazio.");

            response = await HandleCommandAsync(request.Command, token);
        }
        catch (Exception ex)
        {
            response = new ControlResponse { Success = false, Message = ex.Message };
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, _json));
    }

    private async Task<ControlResponse> HandleCommandAsync(string command, CancellationToken token)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "status":
                return new ControlResponse
                {
                    Success = true,
                    Message = "OK",
                    State = _stateStore.Load()
                };

            case "backup-now":
                var started = _coordinator.TryStart("Manual", token);
                return new ControlResponse
                {
                    Success = started,
                    Message = started ? "Backup iniciado." : "Já existe um backup em execução.",
                    State = _stateStore.Load()
                };

            case "test-ftp":
                var settings = _settingsStore.Load();
                var errors = BackupValidator.Validate(settings);
                if (errors.Count > 0)
                {
                    return new ControlResponse
                    {
                        Success = false,
                        Message = string.Join(" ", errors)
                    };
                }

                var message = await _ftp.TestConnectionAsync(settings, token);
                return new ControlResponse
                {
                    Success = true,
                    Message = message,
                    State = _stateStore.Load()
                };

            default:
                return new ControlResponse
                {
                    Success = false,
                    Message = $"Comando desconhecido: {command}"
                };
        }
    }
}
