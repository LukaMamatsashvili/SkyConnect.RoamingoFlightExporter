using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Application;

public interface IFlightDisruptionCollector
{
    Task<CollectionResult> CollectAsync(CancellationToken cancellationToken);
}
