namespace FtpBackup.Core.Models;

public sealed class BackupState
{
    public string Status { get; set; } = "Idle";
    public string Stage { get; set; } = "Aguardando";
    public int ProgressPercent { get; set; }
    public DateTimeOffset? LastStart { get; set; }
    public DateTimeOffset? LastEnd { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public string? LastArchivePath { get; set; }
    public string? LastRemotePath { get; set; }
    public string? LastError { get; set; }
    public string? LastRunReason { get; set; }
    public string? ScheduleKey { get; set; }
    public DateTime? NextScheduledLocal { get; set; }
}
