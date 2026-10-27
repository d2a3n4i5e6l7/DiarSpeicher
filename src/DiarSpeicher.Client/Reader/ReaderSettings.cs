using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Client.Interop;
using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Client.Reader;

public enum ReaderMode
{
    Paged,
    Double,
    ContinuousVertical,
}

public enum ImageFit
{
    Width,
    Height,
    Original,
}

public sealed record ReaderSettings(ReaderMode Mode, ReadingDirection Direction, ImageFit Fit, string Background)
{
    private const string StorageKey = "diarspeicher-reader-settings";

    public static readonly IReadOnlyList<string> Backgrounds = ["#000000", "#050508", "#11131C", "#F0F2F6"];

    public static ReaderSettings Default { get; } = new(ReaderMode.Paged, ReadingDirection.LeftToRight, ImageFit.Height, "#000000");

    public bool RightToLeft => Direction == ReadingDirection.RightToLeft;

    public static ReaderSettings? Load()
    {
        var raw = StorageJs.GetItem(StorageKey);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(raw, ReaderJsonContext.Default.ReaderSettings);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save() => StorageJs.SetItem(StorageKey, JsonSerializer.Serialize(this, ReaderJsonContext.Default.ReaderSettings));

    public static ReaderSettings FromLibrary(string? defaultReadingMode, string? defaultReadingDir)
    {
        var continuous = defaultReadingMode is nameof(ReadingMode.ContinuousVertical) or nameof(ReadingMode.ContinuousHorizontal);
        var direction = defaultReadingDir == nameof(ReadingDirection.RightToLeft) ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight;
        return Default with { Mode = continuous ? ReaderMode.ContinuousVertical : ReaderMode.Paged, Direction = direction };
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReaderSettings))]
internal sealed partial class ReaderJsonContext : JsonSerializerContext;
