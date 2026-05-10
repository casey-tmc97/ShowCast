using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Serializes SKColor as #RRGGBBAA hex string.
/// NOTE: We parse manually instead of using SKColor.TryParse because SkiaSharp
/// interprets 8-char hex as #AARRGGBB (alpha-first), which would mis-read our format.
/// </summary>
public sealed class SKColorJsonConverter : JsonConverter<SKColor>
{
    public override SKColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hex = reader.GetString();
        if (string.IsNullOrEmpty(hex)) return SKColors.Black;

        // Parse our own #RRGGBBAA format
        if (hex.Length == 9 && hex[0] == '#')
        {
            try
            {
                byte r = Convert.ToByte(hex[1..3], 16);
                byte g = Convert.ToByte(hex[3..5], 16);
                byte b = Convert.ToByte(hex[5..7], 16);
                byte a = Convert.ToByte(hex[7..9], 16);
                return new SKColor(r, g, b, a);
            }
            catch { /* fall through */ }
        }

        // 6-char fallback for any legacy entries without alpha
        return SKColor.TryParse(hex, out var c) ? c : SKColors.Black;
    }

    public override void Write(Utf8JsonWriter writer, SKColor v, JsonSerializerOptions options) =>
        writer.WriteStringValue($"#{v.Red:X2}{v.Green:X2}{v.Blue:X2}{v.Alpha:X2}");
}

/// <summary>
/// Saves and loads ShowCast project files (.scf) as indented JSON.
/// Relies on .NET 9 STJ's JsonObjectCreationHandling.Populate to populate
/// read-only List&lt;T&gt; properties, and init-setter support for Guid IDs.
/// </summary>
public static class ShowFileSerializer
{
    public const string Extension   = ".scf";
    public const string FileFilter  = "ShowCast File (*.scf)|*.scf";

    public static JsonSerializerOptions CreateSerializerOptions() => new()
    {
        WriteIndented    = true,
        Converters       = { new SKColorJsonConverter(), new JsonStringEnumConverter() },
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    public static async Task SaveAsync(ShowFile file, string path)
    {
        // Write to a temp file first, then atomic-rename so a crash during save
        // doesn't corrupt the existing file.
        var tmp = path + ".tmp";
        {
            await using var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                                    FileShare.None, 65536, true);
            await JsonSerializer.SerializeAsync(stream, file, CreateSerializerOptions());
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<ShowFile?> LoadAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                FileShare.Read, 65536, true);
        return await JsonSerializer.DeserializeAsync<ShowFile>(stream, CreateSerializerOptions());
    }
}
