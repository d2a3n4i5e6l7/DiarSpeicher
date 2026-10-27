using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Client.Components.Users;

public static class EpubProfileDefaults
{
    public const string AnyDevice = ".*";

    public static EpubDeviceProfile Create() => new()
    {
        Id = "default-epub-profile",
        Name = "CDisplayEx / Smartphone",
        DevicePattern = AnyDevice,
        Width = 1200,
        Height = 1920,
        FontSize = 40,
        LineHeight = 1.6f,
        FontFamily = EpubDeviceProfile.DefaultFontFamily,
        MarginHorizontal = 48,
        MarginVertical = 48,
        Theme = "core",
        IsDefault = true,
    };
}
