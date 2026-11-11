using System.Text.Json;
using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class SyncStateStore
{
    private readonly string _path;
    private Dictionary<string, LocalSyncRecord> _cache;

    public SyncStateStore()
    {
        _path = Path.Combine(FileSystem.AppDataDirectory, "sync-state.json");
        _cache = Load();
    }

    public LocalSyncRecord? Get(string localPath) =>
        _cache.TryGetValue(localPath, out var r) ? r : null;

    public void Upsert(LocalSyncRecord record)
    {
        _cache[record.LocalPath] = record;
        Save();
    }

    private Dictionary<string, LocalSyncRecord> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, LocalSyncRecord>>(
                File.ReadAllText(_path)) ?? new();
        }
        catch { return new(); }
    }

    private void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_cache,
            new JsonSerializerOptions { WriteIndented = true }));
}