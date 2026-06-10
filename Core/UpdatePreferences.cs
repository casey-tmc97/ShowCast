using System;
using System.IO;
using System.Text.Json;

namespace ShowCast.Core;

public class UpdatePreferences
{
    public string?   SkippedVersion { get; set; }
    public DateTime? RemindAfter    { get; set; }

    public bool ShouldShow(string latestVersion) =>
        SkippedVersion != latestVersion &&
        (RemindAfter is null || DateTime.UtcNow >= RemindAfter);

    public static UpdatePreferences Load(string? path = null)
    {
        path ??= AppFolders.UpdatePrefsFile;
        if (!File.Exists(path)) return new UpdatePreferences();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdatePreferences>(json)
                ?? new UpdatePreferences();
        }
        catch { return new UpdatePreferences(); }
    }

    public void Save(string? path = null)
    {
        path ??= AppFolders.UpdatePrefsFile;
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
