using Microsoft.Extensions.Logging;
using SkyConnect.RoamingoFlightExporter.Application;
using SkyConnect.RoamingoFlightExporter.Export;

namespace SkyConnect.RoamingoFlightExporter.Services;

public sealed class ExportRunner(
    IFlightDisruptionCollector collector,
    IExcelExporter exporter,
    ILogger<ExportRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Roamingo flight disruption export.");
        var result = await collector.CollectAsync(cancellationToken);
        var path = exporter.Export(result);

        logger.LogInformation("Export complete. Rows exported: {RowsExported}. File: {Path}",
            result.Summary.RowsExported, path);
    }
}
