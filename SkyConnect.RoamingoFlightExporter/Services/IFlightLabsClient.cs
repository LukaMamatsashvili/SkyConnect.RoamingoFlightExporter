using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Services;

public interface IFlightLabsClient
{
    Task<IReadOnlyList<HistoricalFlightOccurrence>> GetHistoricalFlightsAsync(
        string airportCode,
        string movementType,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FlightDetailOccurrence>> GetFlightDetailsAsync(
        string flightNumber,
        CancellationToken cancellationToken);
}
