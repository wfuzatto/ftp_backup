using System.Text;
using System.Text.Json;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public sealed class StateStore
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public BackupState Load()
    {
        lock (_sync)
            return LoadUnsafe();
    }

    public void Save(BackupState state)
    {
        lock (_sync)
            SaveUnsafe(state);
    }

    public void Update(Action<BackupState> update)
    {
        lock (_sync)
        {
            var state = LoadUnsafe();
            update(state);
            SaveUnsafe(state);
        }
    }

    private BackupState LoadUnsafe()
    {
        if (!File.Exists(AppPaths.StateFile))
            return new BackupState();

        try
        {
            var json = File.ReadAllText(AppPaths.StateFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<BackupState>(json, _json) ?? new BackupState();
        }
        catch
        {
            return new BackupState();
        }
    }

    private void SaveUnsafe(BackupState state)
    {
        AppPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(state, _json);
        var tmp = AppPaths.StateFile + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, AppPaths.StateFile, true);
    }
}
