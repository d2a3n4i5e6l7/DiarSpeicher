using MudBlazor;

namespace DiarSpeicher.Client.Theme;

public static class IronBloodTheme
{
    private static readonly string[] Body = ["Inter", "Chakra Petch", "sans-serif"];
    private static readonly string[] Display = ["Orbitron", "sans-serif"];
    private static readonly string[] Tactical = ["Rajdhani", "sans-serif"];
    private static readonly string[] Mono = ["JetBrains Mono", "monospace"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#C21818",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#A3ABB8",
            SecondaryContrastText = "#FFFFFF",
            Error = "#FF2E2E",
            Warning = "#F59E0B",
            Info = "#8E95A5",
            Success = "#22C55E",
            Background = "#050508",
            Surface = "#0F1015",
            AppbarBackground = "#0A0B0E",
            AppbarText = "#F0F2F6",
            DrawerBackground = "#0A0B0E",
            DrawerText = "#A3ABB8",
            TextPrimary = "#F0F2F6",
            TextSecondary = "#8E95A5",
            TextDisabled = "#636B7C",
            Divider = "#1C1F28",
            LinesDefault = "#282C38",
            LinesInputs = "#383E4C",
            TableLines = "#1C1F28",
            TableHover = "#161821",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Body },
            H1 = new H1Typography { FontFamily = Display, FontWeight = "900", LetterSpacing = "2px" },
            H2 = new H2Typography { FontFamily = Display, FontWeight = "900", LetterSpacing = "2px" },
            H3 = new H3Typography { FontFamily = Display, FontWeight = "700", LetterSpacing = "1.5px" },
            H4 = new H4Typography { FontFamily = Tactical, FontWeight = "700", LetterSpacing = "1px", TextTransform = "uppercase" },
            H5 = new H5Typography { FontFamily = Tactical, FontWeight = "700", LetterSpacing = "1px", TextTransform = "uppercase" },
            H6 = new H6Typography { FontFamily = Tactical, FontWeight = "700", LetterSpacing = "1px", TextTransform = "uppercase" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Tactical, FontWeight = "600", LetterSpacing = "0.5px" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Tactical, FontWeight = "600", LetterSpacing = "0.5px" },
            Button = new ButtonTypography { FontFamily = Tactical, FontWeight = "700", LetterSpacing = "1px", TextTransform = "uppercase" },
            Caption = new CaptionTypography { FontFamily = Mono },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "2px" },
    };
}
