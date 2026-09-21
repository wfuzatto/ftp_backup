using System.IO.Pipes;
using System.Text.Json;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Ipc;

public static class ControlProtocol
{
    public const string PipeName = "FtpBackupControlV1";
}

public sealed record ControlRequest(string Command);

public sealed class ControlResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BackupState? State { get; set; }
}

public static class ControlClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<ControlResponse> SendAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(4));

        await using var pipe = new NamedPipeClientStream(
            ".",
            ControlProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(timeoutCts.Token);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        var request = JsonSerializer.Serialize(new ControlRequest(command), Json);
        await writer.WriteLineAsync(request.AsMemory(), timeoutCts.Token);

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(line))
            throw new IOException("O serviço não respondeu ao comando.");

        return JsonSerializer.Deserialize<ControlResponse>(line, Json)
               ?? throw new IOException("Resposta inválida do serviço.");
    }
}
