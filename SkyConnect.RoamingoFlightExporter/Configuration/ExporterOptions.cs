using System.ComponentModel.DataAnnotations;

namespace SkyConnect.RoamingoFlightExporter.Configuration;

public sealed class ExporterOptions
{
    public const string SectionName = "Exporter";

    // Airport IATA codes: KUT, TBS, BUS.
    public List<string> Airports { get; init; } = [];

    // departure, arrival, or both.
    [RegularExpression("(?i)^(departure|arrival|both)$")]
    public string MovementType { get; init; } = "both";

    // Carrier IATA codes; empty = all airlines.
    public List<string> AirlineCodes { get; init; } = [];

    // Days before the period end.
    [Range(1, 365)]
    public int LookbackDays { get; init; } = 30;

    // null = now; yyyy-MM-dd = end of that day.
    public string? AsOfDate { get; init; }

    // true = cached data only, no API calls.
    public bool UseOnlyCachedData { get; init; }

    // Fetch each historical occurrence using its explicit date.
    public bool DateSpecificDetails { get; init; } = true;

    // Arrival delay threshold; cancellations also included.
    [Range(1, 1440)]
    public int MinimumArrivalDelayMinutes { get; init; } = 180;

    // Maximum concurrent requests.
    [Range(1, 16)]
    public int MaxConcurrency { get; init; } = 4;

    // Legacy; rate-window settings control pacing.
    [Range(0, 60000)]
    public int DelayBetweenRequestsMs { get; init; } = 1200;

    [Range(1, 10)]
    // Allowed requests per rate window.
    public int RequestsPerRateLimitWindow { get; init; } = 8;

    // Rate window length in seconds.
    [Range(1, 60)]
    public int RateLimitWindowSeconds { get; init; } = 10;

    // Request timeout in seconds.
    [Range(5, 300)]
    public int RequestTimeoutSeconds { get; init; } = 45;

    // Maximum HTTP attempts per run, including retries.
    [Range(1, 4000)]
    public int MaxApiCallsPerRun { get; init; } = 3800;

    [Required]
    // Output path, relative to the executable if not absolute.
    public string OutputDirectory { get; init; } = "Output";

    // Legacy; discovery uses half-day windows.
    [Range(1, 14)]
    public int HistoricalChunkDays { get; init; } = 1;

    // true = one request per day; false = half-day windows.
    public bool PreferDailyHistoricalEndpoint { get; init; } = true;

    // API endpoints, credentials, and retries.
    public FlightLabsOptions FlightLabs { get; init; } = new();
}

public sealed class FlightLabsOptions
{
    // Set via FLIGHTLABS_API_KEY or appsettings.Local.json.
    public string ApiKey { get; init; } = "";

    [Required]
    // Historical discovery endpoint.
    public string HistoricalEndpoint { get; init; } = "https://www.goflightlabs.com/historical";

    [Required]
    // Flight-number details endpoint.
    public string FlightEndpoint { get; init; } = "https://app.goflightlabs.com/flight";

    [Range(1, 10)]
    // Total attempts, including the initial request.
    public int MaxRetryAttempts { get; init; } = 4;
}

public sealed record FlightLabsCredentials(string ApiKey);
