using System.Text.Json;

namespace SkyConnect.RoamingoFlightExporter.Infrastructure.Http;

public interface IJsonHttpClient
{
    Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken);
}
