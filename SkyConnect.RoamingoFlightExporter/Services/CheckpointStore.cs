using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Application;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Services;

public interface ICheckpointStore
{
    Task<IReadOnlyList<HistoricalFlightOccurrence>?> TryLoadHistoricalAsync(
        string airport,
        string movementType,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);

    Task SaveHistoricalAsync(
        string airport,
        string movementType,
        DateTime from,
        DateTime to,
        IReadOnlyList<HistoricalFlightOccurrence> flights,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FlightDetailOccurrence>?> TryLoadFlightDetailsAsync(
        string flightNumber,
        CancellationToken cancellationToken);

    Task SaveFlightDetailsAsync(
        string flightNumber,
        IReadOnlyList<FlightDetailOccurrence> flights,
        CancellationToken cancellationToken);
}

public sealed class CheckpointStore(
    IOptions<ExporterOptions> options,
    ILogger<CheckpointStore> logger) : ICheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ExporterOptions _options = options.Value;

    public Task<IReadOnlyList<HistoricalFlightOccurrence>?> TryLoadHistoricalAsync(
        string airport,
        string movementType,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
        => LoadAsync<HistoricalFlightOccurrence>(
            "Historical",
            GetHistoricalFileName(airport, movementType, from, to),
            cancellationToken);

    public Task SaveHistoricalAsync(
        string airport,
        string movementType,
        DateTime from,
        DateTime to,
        IReadOnlyList<HistoricalFlightOccurrence> flights,
        CancellationToken cancellationToken)
        => SaveAsync(
            "Historical",
            GetHistoricalFileName(airport, movementType, from, to),
            flights,
            cancellationToken);

    public Task<IReadOnlyList<FlightDetailOccurrence>?> TryLoadFlightDetailsAsync(
        string flightNumber,
        CancellationToken cancellationToken)
        => LoadAsync<FlightDetailOccurrence>(
            "FlightDetails",
            GetFlightDetailsFileName(flightNumber),
            cancellationToken);

    public Task SaveFlightDetailsAsync(
        string flightNumber,
        IReadOnlyList<FlightDetailOccurrence> flights,
        CancellationToken cancellationToken)
        => SaveAsync(
            "FlightDetails",
            GetFlightDetailsFileName(flightNumber),
            flights,
            cancellationToken);

    private async Task SaveAsync<T>(
        string folder,
        string fileName,
        IReadOnlyList<T> payload,
        CancellationToken cancellationToken)
    {
        var checkpointDirectory = GetCheckpointDirectory(folder);
        Directory.CreateDirectory(checkpointDirectory);

        var path = Path.Combine(checkpointDirectory, fileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);

        logger.LogInformation("Saved checkpoint {Path}.", path);
    }

    private async Task<IReadOnlyList<T>?> LoadAsync<T>(
        string folder,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetCheckpointDirectory(folder), fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken);
        logger.LogInformation("Loaded checkpoint {Path}.", path);
        return payload;
    }

    private string GetCheckpointDirectory(string folder)
    {
        var outputDirectory = Path.IsPathRooted(_options.OutputDirectory)
            ? _options.OutputDirectory
            : Path.Combine(AppContext.BaseDirectory, _options.OutputDirectory);

        return Path.Combine(outputDirectory, "Checkpoints", GetCheckpointDate().ToString("yyyy-MM-dd"), folder);
    }

    private DateOnly GetCheckpointDate()
        => DateOnly.TryParse(_options.AsOfDate, out var asOfDate)
            ? asOfDate
            : DateOnly.FromDateTime(DateTime.Today);

    private static string GetHistoricalFileName(string airport, string movementType, DateTime from, DateTime to)
        => $"historical_{Safe(movementType)}_{Safe(airport)}_{from:yyyy-MM-dd_HHmm}_{to:yyyy-MM-dd_HHmm}.json";

    private static string GetFlightDetailsFileName(string flightNumber)
        => $"flight_{Safe(flightNumber)}.json";

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
