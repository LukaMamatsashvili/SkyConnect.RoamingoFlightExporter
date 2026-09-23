using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyConnect.RoamingoFlightExporter.Services;

public static partial class FlightTimeParser
{
    public static DateTime? BuildDateTime(DateOnly flightDate, string? timeText, DateTime? reference = null)
    {
        var time = ParseTime(timeText);
        if (time is null)
        {
            return null;
        }

        var value = flightDate.ToDateTime(time.Value);

        if (reference is null)
        {
            return value;
        }

        while (value < reference.Value.AddHours(-12))
        {
            value = value.AddDays(1);
        }

        while (value > reference.Value.AddHours(18))
        {
            value = value.AddDays(-1);
        }

        return value;
    }

    public static int? CalculateDelayMinutes(DateTime? scheduled, DateTime? actual)
    {
        if (scheduled is null || actual is null)
        {
            return null;
        }

        var minutes = (int)Math.Round((actual.Value - scheduled.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        return minutes;
    }

    public static TimeOnly? ParseLandedTime(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var match = LandedStatusRegex().Match(status);
        return match.Success ? ParseTime(match.Groups["time"].Value) : null;
    }

    public static bool IsCancelled(string? status)
        => !string.IsNullOrWhiteSpace(status)
           && status.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static TimeOnly? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim();
        var exactFormats = new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "HH:mm:ss", "yyyy-MM-dd HH:mm'Z'", "yyyy-MM-dd HH:mmK" };

        if (DateTimeOffset.TryParseExact(cleaned, exactFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            return TimeOnly.FromTimeSpan(dto.TimeOfDay);
        }

        if (DateTime.TryParseExact(cleaned, exactFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            return TimeOnly.FromDateTime(dt);
        }

        if (DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
        {
            return TimeOnly.FromTimeSpan(dto.TimeOfDay);
        }

        return TimeOnly.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time)
            ? time
            : null;
    }

    [GeneratedRegex(@"\bLanded\s+(?<time>\d{1,2}:\d{2}(?:\s?[AP]M)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LandedStatusRegex();
}
