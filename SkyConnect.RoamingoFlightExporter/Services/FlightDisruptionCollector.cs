using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Application;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Domain;
using SkyConnect.RoamingoFlightExporter.Infrastructure.Http;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Services;

public sealed class FlightDisruptionCollector(
    IFlightLabsClient client,
    IDateRangePlanner dateRangePlanner,
    IBoundedParallelRunner parallelRunner,
    IFlightDisruptionClassifier classifier,
    ICheckpointStore checkpointStore,
    IOptions<ExporterOptions> options,
    ILogger<FlightDisruptionCollector> logger) : IFlightDisruptionCollector
{
    private readonly ExporterOptions _options = options.Value;

    public async Task<CollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var periodEnd = GetPeriodEndDate();
        var period = dateRangePlanner.GetLookbackWindow(periodEnd, _options.LookbackDays);
        var airports = GetConfiguredAirports();
        var movementTypes = GetConfiguredMovementTypes();

        logger.LogInformation("Discovering {MovementTypes} flights from {Start} through {End} for {Airports}.",
            string.Join(", ", movementTypes), period.From, period.To, string.Join(", ", airports));

        GuardHistoricalRequestBudget(period, airports, movementTypes);

        var historicalDiscovery = await DiscoverHistoricalFlightsAsync(period, airports, movementTypes, cancellationToken);
        if (!historicalDiscovery.IsComplete)
        {
            var missingPreview = string.Join(
                "; ",
                historicalDiscovery.MissingWindows
                    .Take(10)
                    .Select(window => $"{window.MovementType} {window.Airport} {window.From:yyyy-MM-dd HH:mm}-{window.To:HH:mm}"));

            throw new InvalidOperationException(
                $"Historical data is incomplete: {historicalDiscovery.CompletedWindowCount}/{historicalDiscovery.ExpectedWindowCount} windows completed. " +
                $"Missing examples: {missingPreview}. FlightDetails and Excel export were skipped to avoid wasting API calls.");
        }

        var historical = historicalDiscovery.Flights;
        if (historical.Count == 0)
        {
            logger.LogWarning("No historical data was collected. Export will contain no disruption rows.");
        }

        var uniqueFlightNumbers = historical
            .Select(item => item.FlightNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Discovered {HistoricalCount} historical flight rows and {UniqueCount} unique flight numbers.",
            historical.Count, uniqueFlightNumbers.Count);

        var detailsByFlightNumber = new ConcurrentDictionary<string, IReadOnlyList<FlightDetailOccurrence>>(StringComparer.OrdinalIgnoreCase);
        var quotaExceeded = false;
        var apiDetailCalls = 0;
        var remainingDetailCallBudget = Math.Max(0, _options.MaxApiCallsPerRun - EstimateHistoricalRequestCount(period, airports, movementTypes));
        await parallelRunner.ForEachAsync(
            uniqueFlightNumbers,
            _options.MaxConcurrency,
            async (flightNumber, token) =>
            {
                if (quotaExceeded)
                {
                    return;
                }

                try
                {
                    var details = await checkpointStore.TryLoadFlightDetailsAsync(flightNumber, token);
                    if (details is null)
                    {
                        if (_options.UseOnlyCachedData)
                        {
                            logger.LogWarning("Missing cached detail checkpoint for {FlightNumber}. API calls are disabled.", flightNumber);
                            return;
                        }

                        if (Interlocked.Increment(ref apiDetailCalls) > remainingDetailCallBudget)
                        {
                            quotaExceeded = true;
                            logger.LogWarning("Configured API call budget reached while fetching details. Stopping remaining detail requests.");
                            return;
                        }

                        details = await client.GetFlightDetailsAsync(flightNumber, token);
                        await checkpointStore.SaveFlightDetailsAsync(flightNumber, details, token);
                    }

                    detailsByFlightNumber[flightNumber] = details;
                    logger.LogInformation("Fetched {Count} detail rows for {FlightNumber}.", details.Count, flightNumber);
                }
                catch (FlightLabsQuotaExceededException ex)
                {
                    quotaExceeded = true;
                    logger.LogWarning(ex, "FlightLabs quota reached while fetching details. Stopping remaining detail requests.");
                }
                catch (FlightLabsNoDataException ex)
                {
                    var emptyDetails = Array.Empty<FlightDetailOccurrence>();
                    await checkpointStore.SaveFlightDetailsAsync(flightNumber, emptyDetails, token);
                    detailsByFlightNumber[flightNumber] = emptyDetails;
                    logger.LogWarning(ex, "No detail rows exist for {FlightNumber}; saved empty checkpoint.", flightNumber);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException)
                {
                    logger.LogWarning(ex, "Skipping detail request for {FlightNumber}; already collected data will be kept.", flightNumber);
                }
            },
            cancellationToken);

        var rows = BuildExportRows(historical, detailsByFlightNumber, period);
        var diagnostics = BuildDiagnosticRows(historical, detailsByFlightNumber, period);
        var sortedRows = rows
            .OrderByDescending(row => row.FlightDate)
            .ThenByDescending(row => row.ArrivalDelayMinutes ?? int.MinValue)
            .ThenBy(row => row.FlightNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summary = new ExportSummary(
            DateTimeOffset.Now,
            period.From,
            period.To,
            airports,
            movementTypes,
            historical.Count,
            uniqueFlightNumbers.Count,
            EstimateHistoricalRequestCount(period, airports, movementTypes) + uniqueFlightNumbers.Count,
            sortedRows.Count(row => row.ArrivalDelayMinutes >= _options.MinimumArrivalDelayMinutes),
            sortedRows.Count(row => row.DisruptionType.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)),
            sortedRows.Count);

        return new CollectionResult(sortedRows, summary, diagnostics);
    }

    private async Task<HistoricalDiscoveryResult> DiscoverHistoricalFlightsAsync(
        DateRange period,
        IReadOnlyCollection<string> airports,
        IReadOnlyCollection<string> movementTypes,
        CancellationToken cancellationToken)
    {
        var chunks = new List<HistoricalWindow>();

        foreach (var airport in airports)
        {
            foreach (var movementType in movementTypes)
            {
                foreach (var chunk in SplitIntoHistoricalWindows(period))
                {
                    chunks.Add(new HistoricalWindow(airport, movementType, chunk.From, chunk.To));
                }
            }
        }

        var results = new ConcurrentBag<HistoricalFlightOccurrence>();
        var completedWindows = new ConcurrentDictionary<HistoricalWindow, byte>();
        var quotaExceeded = 0;

        await parallelRunner.ForEachAsync(
            chunks,
            _options.MaxConcurrency,
            async (chunk, token) =>
            {
                if (Volatile.Read(ref quotaExceeded) == 1)
                {
                    return;
                }

                try
                {
                    var items = await checkpointStore.TryLoadHistoricalAsync(
                        chunk.Airport,
                        chunk.MovementType,
                        chunk.From,
                        chunk.To,
                        token);

                    if (items is null)
                    {
                        if (_options.UseOnlyCachedData)
                        {
                            throw new InvalidOperationException("Cached historical checkpoint is missing and API calls are disabled.");
                        }

                        items = await client.GetHistoricalFlightsAsync(chunk.Airport, chunk.MovementType, chunk.From, chunk.To, token);
                        await checkpointStore.SaveHistoricalAsync(
                            chunk.Airport,
                            chunk.MovementType,
                            chunk.From,
                            chunk.To,
                            items,
                            token);
                    }

                    foreach (var item in items)
                    {
                        results.Add(item);
                    }

                    completedWindows.TryAdd(chunk, 0);
                    logger.LogInformation("Historical discovery {MovementType} {Airport} {From}-{To}: {Count} flights.",
                        chunk.MovementType, chunk.Airport, chunk.From, chunk.To, items.Count);
                }
                catch (FlightLabsQuotaExceededException ex)
                {
                    Interlocked.Exchange(ref quotaExceeded, 1);
                    logger.LogWarning(ex, "FlightLabs quota reached. Stopping remaining historical requests and keeping collected checkpoints.");
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Skipping historical request {MovementType} {Airport} {From}-{To}; already collected data will be kept.",
                        chunk.MovementType, chunk.Airport, chunk.From, chunk.To);
                }
            },
            cancellationToken);

        var flights = results
            .GroupBy(FlightOccurrenceKey.From)
            .Select(group => group.First())
            .ToList();

        var missingWindows = chunks
            .Where(chunk => !completedWindows.ContainsKey(chunk))
            .OrderBy(chunk => chunk.From)
            .ThenBy(chunk => chunk.Airport, StringComparer.OrdinalIgnoreCase)
            .ThenBy(chunk => chunk.MovementType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "Historical discovery completed {Completed}/{Expected} windows.",
            completedWindows.Count,
            chunks.Count);

        return new HistoricalDiscoveryResult(
            flights,
            chunks.Count,
            completedWindows.Count,
            missingWindows);
    }

    private List<DisruptedFlightRow> BuildExportRows(
        IReadOnlyCollection<HistoricalFlightOccurrence> historical,
        IReadOnlyDictionary<string, IReadOnlyList<FlightDetailOccurrence>> detailsByFlightNumber,
        DateRange period)
    {
        var historicalKeys = historical
            .Select(FlightOccurrenceKey.From)
            .ToHashSet();

        var departureAirports = _options.Airports
            .Select(FlightOccurrenceKey.Normalize)
            .Where(airport => airport.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<FlightOccurrenceKey, DisruptedFlightRow>();

        foreach (var details in detailsByFlightNumber.Values.SelectMany(x => x))
        {
            if (details.FlightDate < period.From || details.FlightDate > period.To)
            {
                continue;
            }

            var key = FlightOccurrenceKey.From(details);

            if (!historicalKeys.Contains(key)
                && !departureAirports.Contains(key.OriginAirport)
                && !departureAirports.Contains(key.DestinationAirport))
            {
                continue;
            }

            var classification = classifier.Classify(details, _options.MinimumArrivalDelayMinutes);
            if (!classification.IsExportable)
            {
                continue;
            }

            rows[key] = new DisruptedFlightRow(
                details.FlightDate,
                details.FlightNumber,
                BlankIfMissing(details.Airline),
                BlankIfMissing(key.OriginAirport),
                BlankIfMissing(key.DestinationAirport),
                classification.ScheduledDeparture,
                classification.ActualDeparture,
                classification.DepartureDelayMinutes,
                classification.ScheduledArrival,
                classification.ActualArrival,
                classification.ArrivalDelayMinutes,
                BlankIfMissing(details.Status),
                classification.DisruptionType,
                "Yes");
        }

        return rows.Values.ToList();
    }

    private List<DiagnosticFlightRow> BuildDiagnosticRows(
        IReadOnlyCollection<HistoricalFlightOccurrence> historical,
        IReadOnlyDictionary<string, IReadOnlyList<FlightDetailOccurrence>> detailsByFlightNumber,
        DateRange period)
    {
        var historicalKeys = historical
            .Select(FlightOccurrenceKey.From)
            .ToHashSet();

        var configuredAirports = _options.Airports
            .Select(FlightOccurrenceKey.Normalize)
            .Where(airport => airport.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<string, DiagnosticFlightRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var details in detailsByFlightNumber.Values.SelectMany(x => x))
        {
            var key = FlightOccurrenceKey.From(details);
            var classification = classifier.Classify(details, _options.MinimumArrivalDelayMinutes);
            var inPeriod = details.FlightDate >= period.From && details.FlightDate <= period.To;
            var matchedHistorical = historicalKeys.Contains(key);
            var georgiaAirportInRoute = configuredAirports.Contains(key.OriginAirport)
                                        || configuredAirports.Contains(key.DestinationAirport);
            var parserFinding = GetParserFinding(details, classification);
            var exportDecision = GetExportDecision(inPeriod, matchedHistorical, georgiaAirportInRoute, classification, parserFinding);

            rows[GetDiagnosticKey(key)] = new DiagnosticFlightRow(
                details.FlightDate,
                details.FlightNumber,
                BlankIfMissing(details.Airline),
                BlankIfMissing(key.OriginAirport),
                BlankIfMissing(key.DestinationAirport),
                inPeriod,
                matchedHistorical,
                georgiaAirportInRoute,
                classification.ScheduledDeparture,
                classification.ActualDeparture,
                classification.DepartureDelayMinutes,
                classification.ScheduledArrival,
                classification.ActualArrival,
                classification.ArrivalDelayMinutes,
                BlankIfMissing(details.Status),
                parserFinding,
                exportDecision);
        }

        foreach (var occurrence in historical)
        {
            var key = FlightOccurrenceKey.From(occurrence);
            if (rows.ContainsKey(GetDiagnosticKey(key)))
            {
                continue;
            }

            rows[GetDiagnosticKey(key)] = new DiagnosticFlightRow(
                occurrence.Date,
                occurrence.FlightNumber,
                BlankIfMissing(occurrence.Airline),
                BlankIfMissing(key.OriginAirport),
                BlankIfMissing(key.DestinationAirport),
                true,
                true,
                configuredAirports.Contains(key.OriginAirport) || configuredAirports.Contains(key.DestinationAirport),
                null,
                null,
                null,
                null,
                null,
                null,
                "",
                "No matching detail row returned for this historical flight occurrence",
                "Not exported - delay cannot be established confidently");
        }

        return rows.Values
            .OrderByDescending(row => row.FlightDate)
            .ThenByDescending(row => row.ArrivalDelayMinutes ?? int.MinValue)
            .ThenBy(row => row.FlightNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetDiagnosticKey(FlightOccurrenceKey key)
        => $"{key.FlightNumber}|{key.FlightDate:yyyy-MM-dd}|{key.OriginAirport}|{key.DestinationAirport}";

    private static string GetParserFinding(FlightDetailOccurrence details, DisruptionClassification classification)
    {
        if (FlightTimeParser.IsCancelled(details.Status))
        {
            return "Cancelled status detected";
        }

        if (classification.ScheduledArrival is null)
        {
            return "Scheduled arrival missing or not parseable";
        }

        if (classification.ActualArrival is null)
        {
            return "Actual arrival not found in status";
        }

        if (classification.ArrivalDelayMinutes < 0)
        {
            return "Arrival appears earlier than scheduled";
        }

        return "Arrival delay calculated";
    }

    private string GetExportDecision(
        bool inPeriod,
        bool matchedHistorical,
        bool georgiaAirportInRoute,
        DisruptionClassification classification,
        string parserFinding)
    {
        if (!inPeriod)
        {
            return "Not exported - outside selected period";
        }

        if (!matchedHistorical && !georgiaAirportInRoute)
        {
            return "Not exported - detail row does not match selected Georgia airport route";
        }

        if (classification.IsExportable)
        {
            return "Exported";
        }

        if (classification.ArrivalDelayMinutes is null)
        {
            return $"Not exported - {parserFinding.ToLowerInvariant()}";
        }

        return $"Not exported - arrival delay below {_options.MinimumArrivalDelayMinutes} minutes";
    }

    private static string BlankIfMissing(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private IReadOnlyList<string> GetConfiguredAirports()
        => _options.Airports
            .Select(FlightOccurrenceKey.Normalize)
            .Where(airport => airport.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private DateOnly GetPeriodEndDate()
        => DateOnly.TryParse(_options.AsOfDate, out var asOfDate)
            ? asOfDate
            : DateOnly.FromDateTime(DateTime.Today);

    private IReadOnlyList<string> GetConfiguredMovementTypes()
        => _options.MovementTypes
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(type => type is "departure" or "arrival")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void GuardHistoricalRequestBudget(
        DateRange period,
        IReadOnlyCollection<string> airports,
        IReadOnlyCollection<string> movementTypes)
    {
        var plannedHistoricalCalls = EstimateHistoricalRequestCount(period, airports, movementTypes);
        if (plannedHistoricalCalls > _options.MaxApiCallsPerRun)
        {
            throw new InvalidOperationException(
                $"Planned historical API calls ({plannedHistoricalCalls}) exceed MaxApiCallsPerRun ({_options.MaxApiCallsPerRun}).");
        }
    }

    private int EstimateHistoricalRequestCount(
        DateRange period,
        IReadOnlyCollection<string> airports,
        IReadOnlyCollection<string> movementTypes)
        => airports.Count * movementTypes.Count * SplitIntoHistoricalWindows(period).Count;

    private static IReadOnlyList<(DateTime From, DateTime To)> SplitIntoHistoricalWindows(DateRange period)
    {
        var windows = new List<(DateTime From, DateTime To)>();

        for (var date = period.From; date <= period.To; date = date.AddDays(1))
        {
            var day = date.ToDateTime(TimeOnly.MinValue);
            windows.Add((day, day.AddHours(11).AddMinutes(59)));
            windows.Add((day.AddHours(12), day.AddHours(23).AddMinutes(59)));
        }

        return windows;
    }

    private sealed record HistoricalWindow(string Airport, string MovementType, DateTime From, DateTime To);

    private sealed record HistoricalDiscoveryResult(
        IReadOnlyList<HistoricalFlightOccurrence> Flights,
        int ExpectedWindowCount,
        int CompletedWindowCount,
        IReadOnlyList<HistoricalWindow> MissingWindows)
    {
        public bool IsComplete => CompletedWindowCount == ExpectedWindowCount;
    }
}
