using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public sealed class SettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FtpBackup::DPAPI::v1");
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public SettingsStore() => AppPaths.EnsureDirectories();

    public BackupSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                var defaults = new BackupSettings { StagingFolder = AppPaths.StagingDirectory };
                SaveUnsafe(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<BackupSettings>(json, _json) ?? new BackupSettings();
                settings.NormalizeDefaults();
                return settings;
            }
            catch
            {
                return new BackupSettings { StagingFolder = AppPaths.StagingDirectory };
            }
        }
    }

    public void Save(BackupSettings settings)
    {
        lock (_sync)
            SaveUnsafe(settings);
    }

    private void SaveUnsafe(BackupSettings settings)
    {
        AppPaths.EnsureDirectories();
        settings.NormalizeDefaults();
        var json = JsonSerializer.Serialize(settings, _json);
        var tmp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, AppPaths.SettingsFile, true);
    }

    public string ProtectPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public string UnprotectPassword(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return string.Empty;

        var protectedBytes = Convert.FromBase64String(encrypted);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }
}
