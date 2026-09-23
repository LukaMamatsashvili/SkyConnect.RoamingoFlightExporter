using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Domain;

public readonly record struct FlightOccurrenceKey(
    string FlightNumber,
    DateOnly FlightDate,
    string OriginAirport,
    string DestinationAirport)
{
    public static FlightOccurrenceKey From(HistoricalFlightOccurrence occurrence)
        => new(
            Normalize(occurrence.FlightNumber),
            occurrence.Date,
            Normalize(occurrence.OriginAirport),
            Normalize(occurrence.DestinationAirport));

    public static FlightOccurrenceKey From(FlightDetailOccurrence occurrence)
        => new(
            Normalize(occurrence.FlightNumber),
            occurrence.FlightDate,
            Normalize(occurrence.OriginAirport),
            Normalize(occurrence.DestinationAirport));

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        var openParen = trimmed.LastIndexOf('(');
        var closeParen = trimmed.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
        {
            var code = trimmed[(openParen + 1)..closeParen].Trim();
            if (code.Length == 3 && code.All(char.IsLetter))
            {
                return code.ToUpperInvariant();
            }
        }

        return trimmed.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }
}
