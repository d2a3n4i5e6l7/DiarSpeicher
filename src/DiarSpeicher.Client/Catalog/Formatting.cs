using System.Globalization;

namespace DiarSpeicher.Client.Catalog;

public static class Formatting
{
    private const double Kibi = 1024;
    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var unit = Math.Min((int)Math.Floor(Math.Log(bytes) / Math.Log(Kibi)), ByteUnits.Length - 1);
        var value = bytes / Math.Pow(Kibi, unit);
        var text = value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
        return $"{text} {ByteUnits[unit]}";
    }

    public static string FormatDurationSeconds(double seconds)
    {
        if (seconds < SecondsPerMinute)
        {
            return $"{Math.Max((int)Math.Floor(seconds), 1)} s";
        }

        var minutes = (int)Math.Floor(seconds / SecondsPerMinute);
        var rest = (int)Math.Floor(seconds % SecondsPerMinute);
        if (minutes < MinutesPerHour)
        {
            return rest > 0 ? $"{minutes} min {rest} s" : $"{minutes} min";
        }

        return $"{minutes / MinutesPerHour} h {minutes % MinutesPerHour} min";
    }

    public static string FormatDateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("d/M/yyyy, H:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture);

    public static string FormatDurationMinutes(double minutes)
    {
        if (minutes < MinutesPerHour)
        {
            return $"{(int)Math.Floor(minutes)} MIN";
        }

        var hours = (int)Math.Floor(minutes / MinutesPerHour);
        var rest = (int)Math.Floor(minutes % MinutesPerHour);
        return rest == 0 ? $"{hours} H" : $"{hours} H {rest} MIN";
    }
}
