using System.ComponentModel.DataAnnotations;

namespace SkyConnect.RoamingoFlightExporter.Configuration;

public sealed class ExporterOptions
{
    public const string SectionName = "Exporter";

    public List<string> Airports { get; init; } = ["TBS", "KUT", "BUS"];

    public List<string> MovementTypes { get; init; } = ["departure", "arrival"];

    [Range(1, 365)]
    public int LookbackDays { get; init; } = 30;

    public string? AsOfDate { get; init; }

    public bool UseOnlyCachedData { get; init; }

    [Range(1, 1440)]
    public int MinimumArrivalDelayMinutes { get; init; } = 180;

    [Range(1, 16)]
    public int MaxConcurrency { get; init; } = 4;

    [Range(0, 60000)]
    public int DelayBetweenRequestsMs { get; init; } = 1200;

    [Range(1, 10)]
    public int RequestsPerRateLimitWindow { get; init; } = 8;

    [Range(1, 60)]
    public int RateLimitWindowSeconds { get; init; } = 10;

    [Range(5, 300)]
    public int RequestTimeoutSeconds { get; init; } = 45;

    [Range(1, 4000)]
    public int MaxApiCallsPerRun { get; init; } = 3800;

    [Required]
    public string OutputDirectory { get; init; } = "Output";

    [Range(1, 14)]
    public int HistoricalChunkDays { get; init; } = 1;

    public bool PreferDailyHistoricalEndpoint { get; init; } = true;

    public FlightLabsOptions FlightLabs { get; init; } = new();
}

public sealed class FlightLabsOptions
{
    public string ApiKey { get; init; } = "";

    [Required]
    public string HistoricalEndpoint { get; init; } = "https://www.goflightlabs.com/historical";

    [Required]
    public string FlightEndpoint { get; init; } = "https://app.goflightlabs.com/flight";

    [Range(1, 10)]
    public int MaxRetryAttempts { get; init; } = 4;
}

public sealed record FlightLabsCredentials(string ApiKey);
