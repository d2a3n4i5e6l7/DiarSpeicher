namespace DiarSpeicher.Core.Domain.Models;

public class EpubDeviceProfile
{
    public const string DefaultFontFamily = "Inter";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Móvil (CDisplayEx)";
    public string DevicePattern { get; set; } = "CDisplayEx|Android|Mobile|iPhone";
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 2400;
    public int FontSize { get; set; } = 32;
    public float LineHeight { get; set; } = 1.6f;
    public string FontFamily { get; set; } = DefaultFontFamily;
    public int MarginHorizontal { get; set; } = 64;
    public int MarginVertical { get; set; } = 80;
    public string Theme { get; set; } = "core"; // "core", "oled", "sepia"
    public bool IsDefault { get; set; }

    public static List<EpubDeviceProfile> GetDefaults() =>
    [
        new EpubDeviceProfile
        {
            Id = "p_cdisplay",
            Name = "Móvil (CDisplayEx)",
            DevicePattern = "CDisplayEx|Android|Mobile|iPhone",
            Width = 1080,
            Height = 2400,
            FontSize = 32,
            LineHeight = 1.6f,
            FontFamily = DefaultFontFamily,
            MarginHorizontal = 64,
            MarginVertical = 80,
            Theme = "core",
            IsDefault = true
        },
        new EpubDeviceProfile
        {
            Id = "p_tablet",
            Name = "Tablet / iPad",
            DevicePattern = "iPad|Tablet",
            Width = 1600,
            Height = 2560,
            FontSize = 38,
            LineHeight = 1.6f,
            FontFamily = DefaultFontFamily,
            MarginHorizontal = 96,
            MarginVertical = 100,
            Theme = "core",
            IsDefault = false
        },
        new EpubDeviceProfile
        {
            Id = "p_desktop",
            Name = "Monitor / PC",
            DevicePattern = "Windows|Macintosh|X11",
            Width = 1920,
            Height = 1080,
            FontSize = 28,
            LineHeight = 1.5f,
            FontFamily = DefaultFontFamily,
            MarginHorizontal = 120,
            MarginVertical = 80,
            Theme = "core",
            IsDefault = false
        }
    ];
}
