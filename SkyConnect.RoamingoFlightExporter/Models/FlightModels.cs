namespace SkyConnect.RoamingoFlightExporter.Models;

public sealed record HistoricalFlightOccurrence(
    string AirportCode,
    string MovementType,
    DateOnly Date,
    string FlightNumber,
    string? Airline,
    string? OriginAirport,
    string? DestinationAirport);

public sealed record FlightDetailOccurrence(
    DateOnly FlightDate,
    string FlightNumber,
    string? Airline,
    string? OriginAirport,
    string? DestinationAirport,
    string? ScheduledDeparture,
    string? ActualDeparture,
    string? ScheduledArrival,
    string? Status,
    string? ActualArrival = null);

public sealed record DisruptedFlightRow(
    DateOnly FlightDate,
    string FlightNumber,
    string Airline,
    string DepartureAirport,
    string DestinationAirport,
    DateTime? ScheduledDeparture,
    DateTime? ActualDeparture,
    int? DepartureDelayMinutes,
    DateTime? ScheduledArrival,
    DateTime? ActualArrival,
    int? ArrivalDelayMinutes,
    string FlightStatus,
    string DisruptionType,
    string PassengerCheckNeeded);

public sealed record DiagnosticFlightRow(
    DateOnly? FlightDate,
    string FlightNumber,
    string Airline,
    string OriginAirport,
    string DestinationAirport,
    bool InPeriod,
    bool MatchedHistoricalDiscovery,
    bool GeorgiaAirportInRoute,
    DateTime? ScheduledDeparture,
    DateTime? ActualDeparture,
    int? DepartureDelayMinutes,
    DateTime? ScheduledArrival,
    DateTime? ActualArrival,
    int? ArrivalDelayMinutes,
    string FlightStatus,
    string ParserFinding,
    string ExportDecision);

public sealed record ExportSummary(
    DateTimeOffset RunDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyCollection<string> Airports,
    IReadOnlyCollection<string> MovementTypes,
    int TotalHistoricalFlightsDiscovered,
    int TotalUniqueFlightNumbersQueried,
    int PlannedApiCalls,
    int TotalLongArrivalDelays,
    int TotalCancellations,
    int RowsExported);

public sealed record CollectionResult(
    IReadOnlyList<DisruptedFlightRow> Rows,
    ExportSummary Summary,
    IReadOnlyList<DiagnosticFlightRow> Diagnostics);
