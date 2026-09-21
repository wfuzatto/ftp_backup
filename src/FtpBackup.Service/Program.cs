using FtpBackup.Core.Services;
using FtpBackup.Service;

AppPaths.EnsureDirectories();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FtpBackupService";
});

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<LogStore>();
builder.Services.AddSingleton<ArchiveBuilder>();
builder.Services.AddSingleton<FtpTransferService>();
builder.Services.AddSingleton<LocalRetentionService>();
builder.Services.AddSingleton<BackupCoordinator>();

builder.Services.AddHostedService<BackupSchedulerWorker>();
builder.Services.AddHostedService<ControlPipeService>();

await builder.Build().RunAsync();
