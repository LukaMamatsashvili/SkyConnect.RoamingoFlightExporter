using System.Text.Json;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Infrastructure.Http;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Services;

public sealed class FlightLabsClient(
    IJsonHttpClient jsonHttpClient,
    IOptions<ExporterOptions> options,
    FlightLabsCredentials credentials) : IFlightLabsClient
{
    private readonly ExporterOptions _options = options.Value;

    public async Task<IReadOnlyList<HistoricalFlightOccurrence>> GetHistoricalFlightsAsync(
        string airportCode,
        string movementType,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var url = BuildHistoricalUrl(airportCode, movementType, from, to);
        using var document = await jsonHttpClient.GetAsync(url, cancellationToken);
        var items = GetPayloadItems(document.RootElement);

        return items
            .Select(item => ParseHistoricalOccurrence(item, airportCode, movementType, DateOnly.FromDateTime(from)))
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.Date >= DateOnly.FromDateTime(from) && item.Date <= DateOnly.FromDateTime(to))
            .ToList();
    }

    public async Task<IReadOnlyList<FlightDetailOccurrence>> GetFlightDetailsAsync(
        string flightNumber,
        CancellationToken cancellationToken)
    {
        var url = AddQueryString(_options.FlightLabs.FlightEndpoint, new Dictionary<string, string?>
        {
            ["access_key"] = credentials.ApiKey,
            ["flight_number"] = flightNumber
        });

        using var document = await jsonHttpClient.GetAsync(url, cancellationToken);
        var items = GetPayloadItems(document.RootElement);

        return items
            .Select(item => ParseFlightDetail(item, flightNumber))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private string BuildHistoricalUrl(string airportCode, string movementType, DateTime from, DateTime to)
    {
        var query = new Dictionary<string, string?>
        {
            ["access_key"] = credentials.ApiKey,
            ["type"] = movementType,
            ["code"] = airportCode,
            ["date_from"] = from.ToString("yyyy-MM-ddTHH:mm"),
            ["date_to"] = to.ToString("yyyy-MM-ddTHH:mm")
        };

        return AddQueryString(_options.FlightLabs.HistoricalEndpoint, query);
    }

    private static string AddQueryString(string baseUrl, IReadOnlyDictionary<string, string?> query)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var pairs = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

        return baseUrl + separator + string.Join("&", pairs);
    }

    private static IReadOnlyList<JsonElement> GetPayloadItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        foreach (var propertyName in new[] { "data", "flights", "results" })
        {
            if (root.TryGetProperty(propertyName, out var payload) && payload.ValueKind == JsonValueKind.Array)
            {
                return payload.EnumerateArray().ToList();
            }
        }

        return [];
    }

    private static HistoricalFlightOccurrence? ParseHistoricalOccurrence(
        JsonElement item,
        string airportCode,
        string movementType,
        DateOnly fallbackDate)
    {
        var flightNumber = FirstString(item, "number", "flight.number", "flightNumber", "flight_number");
        if (string.IsNullOrWhiteSpace(flightNumber))
        {
            return null;
        }

        var localScheduled = FirstString(item, "movement.scheduledTime.local", "departure.scheduled", "scheduled_departure");
        var date = TryDateFromText(localScheduled) ?? fallbackDate;
        var airline = FirstString(item, "airline.name", "airline.iata", "airline");
        var relatedAirport = FirstString(item, "movement.airport.iata", "arrival.iata", "departure.iata", "destination", "origin");
        var normalizedMovementType = movementType.Trim().ToLowerInvariant();
        var origin = normalizedMovementType == "arrival" ? relatedAirport : airportCode;
        var destination = normalizedMovementType == "arrival" ? airportCode : relatedAirport;

        return new HistoricalFlightOccurrence(
            airportCode,
            normalizedMovementType,
            date,
            NormalizeFlightNumber(flightNumber),
            airline,
            origin,
            destination);
    }

    private static FlightDetailOccurrence? ParseFlightDetail(JsonElement item, string fallbackFlightNumber)
    {
        var dateText = FirstString(item, "DATE", "date", "flight_date", "flightDate");
        if (!DateOnly.TryParse(dateText, out var date))
        {
            return null;
        }

        var flightNumber = FirstString(item, "FLIGHT", "flight", "flight_number", "flightNumber", "number") ?? fallbackFlightNumber;
        return new FlightDetailOccurrence(
            date,
            NormalizeFlightNumber(flightNumber),
            FirstString(item, "AIRLINE", "airline.name", "airline"),
            FirstString(item, "FROM", "from", "departure.iata", "origin"),
            FirstString(item, "TO", "to", "arrival.iata", "destination"),
            FirstString(item, "STD", "std", "scheduled_departure"),
            FirstString(item, "ATD", "atd", "actual_departure"),
            FirstString(item, "STA", "sta", "scheduled_arrival"),
            FirstString(item, "STATUS", "status"));
    }

    private static DateOnly? TryDateFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, out var dto))
        {
            return DateOnly.FromDateTime(dto.DateTime);
        }

        return DateTime.TryParse(text, out var dt) ? DateOnly.FromDateTime(dt) : null;
    }

    private static string NormalizeFlightNumber(string value)
        => value.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static string? FirstString(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (TryGetByPath(root, path, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }

                if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return value.ToString();
                }
            }
        }

        return null;
    }

    private static bool TryGetByPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var found = false;
            foreach (var property in value.EnumerateObject())
            {
                if (!string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                found = true;
                break;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
