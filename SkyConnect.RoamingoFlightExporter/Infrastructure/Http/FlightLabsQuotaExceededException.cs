namespace SkyConnect.RoamingoFlightExporter.Infrastructure.Http;

public sealed class FlightLabsQuotaExceededException(string message) : Exception(message);
