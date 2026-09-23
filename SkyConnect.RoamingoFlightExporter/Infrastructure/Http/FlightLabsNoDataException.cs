namespace SkyConnect.RoamingoFlightExporter.Infrastructure.Http;

public sealed class FlightLabsNoDataException(string message) : Exception(message);
