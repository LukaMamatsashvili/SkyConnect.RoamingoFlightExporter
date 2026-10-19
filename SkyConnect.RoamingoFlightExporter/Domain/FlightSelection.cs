using System.Text.RegularExpressions;
using SkyConnect.RoamingoFlightExporter.Configuration;

namespace SkyConnect.RoamingoFlightExporter.Domain;

public sealed class FlightSelection(ExporterOptions options)
{
    public bool MatchesAirline(string flightNumber)
    {
        var codes = options.AirlineCodes.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (codes.Length == 0) return true;
        var number = FlightOccurrenceKey.Normalize(flightNumber);
        return codes.Any(code => Regex.IsMatch(number,
            "^" + Regex.Escape(code.Trim().ToUpperInvariant()) + "[0-9]+[A-Z]?$") );
    }

    public bool MatchesRoute(string? origin, string? destination)
    {
        var airports = options.Airports.Select(FlightOccurrenceKey.Normalize).ToHashSet();
        return (options.MovementType.Equals("both", StringComparison.OrdinalIgnoreCase)
                || options.MovementType.Equals("departure", StringComparison.OrdinalIgnoreCase))
                && airports.Contains(FlightOccurrenceKey.Normalize(origin))
            || (options.MovementType.Equals("both", StringComparison.OrdinalIgnoreCase)
                || options.MovementType.Equals("arrival", StringComparison.OrdinalIgnoreCase))
                && airports.Contains(FlightOccurrenceKey.Normalize(destination));
    }
}
