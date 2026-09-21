using System.IO.Compression;

namespace FtpBackup.Core.Models;

public enum BackupFrequency
{
    Hourly,
    Daily,
    Weekly,
    Monthly
}

public enum FtpSecurityMode
{
    Plain,
    ExplicitTls,
    ImplicitTls,
    Auto
}

public sealed class BackupSettings
{
    public bool Enabled { get; set; } = true;
    public string SourceFolder { get; set; } = string.Empty;
    public string StagingFolder { get; set; } = string.Empty;

    public string FtpHost { get; set; } = string.Empty;
    public int FtpPort { get; set; } = 21;
    public string FtpUsername { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string RemoteFolder { get; set; } = "/backups";
    public FtpSecurityMode SecurityMode { get; set; } = FtpSecurityMode.ExplicitTls;
    public bool PassiveMode { get; set; } = true;
    public bool AllowInvalidCertificate { get; set; }

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;
    public string StartTime { get; set; } = "02:00";
    public int HourlyMinute { get; set; }
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;
    public int MonthlyDay { get; set; } = 1;

    public string ArchivePrefix { get; set; } = "backup";
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
    public bool FailOnUnreadableFile { get; set; } = true;
    public bool UploadSha256File { get; set; } = true;

    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public int ConnectTimeoutSeconds { get; set; } = 30;
    public int ReadTimeoutSeconds { get; set; } = 120;

    public int LocalRetentionCount { get; set; } = 3;
    public int RemoteRetentionCount { get; set; } = 30;

    public void NormalizeDefaults()
    {
        if (string.IsNullOrWhiteSpace(StagingFolder))
            StagingFolder = Services.AppPaths.StagingDirectory;

        FtpPort = Math.Clamp(FtpPort, 1, 65535);
        HourlyMinute = Math.Clamp(HourlyMinute, 0, 59);
        MonthlyDay = Math.Clamp(MonthlyDay, 1, 31);
        RetryCount = Math.Clamp(RetryCount, 0, 20);
        RetryDelaySeconds = Math.Clamp(RetryDelaySeconds, 1, 3600);
        ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, 5, 600);
        ReadTimeoutSeconds = Math.Clamp(ReadTimeoutSeconds, 10, 7200);
        LocalRetentionCount = Math.Clamp(LocalRetentionCount, 0, 10000);
        RemoteRetentionCount = Math.Clamp(RemoteRetentionCount, 0, 10000);

        if (string.IsNullOrWhiteSpace(RemoteFolder))
            RemoteFolder = "/backups";

        if (!RemoteFolder.StartsWith('/'))
            RemoteFolder = "/" + RemoteFolder;

        RemoteFolder = RemoteFolder.TrimEnd('/');
        if (RemoteFolder.Length == 0)
            RemoteFolder = "/";

        if (string.IsNullOrWhiteSpace(ArchivePrefix))
            ArchivePrefix = "backup";
    }
}
