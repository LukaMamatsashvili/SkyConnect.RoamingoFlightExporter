using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Export;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class GeorgianExportTests
{
    [Fact]
    public void ExportWritesGeorgianLabelsAndTypedDatesWithoutCallingApi()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var date = new DateOnly(2026, 10, 17);
            var row = new DisruptedFlightRow(date, "W67921", "Wizz Air", "KUT", "BUD",
                null, null, null, null, null, null, "Cancelled", "Cancelled", "Yes");
            var summary = new ExportSummary(DateTimeOffset.Now, date, date, ["KUT"],
                ["departure"], 1, 1, 3, 0, 1, 1);
            var exporter = new ExcelExporter(Options.Create(new ExporterOptions { OutputDirectory = directory }));
            var path = exporter.Export(new CollectionResult([row], summary, []));
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("საყურადღებო ფრენები");
            Assert.Equal("ფრენის თარიღი", sheet.Cell(1, 1).GetString());
            Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 1).DataType);
            Assert.Equal("გაუქმებული", sheet.Cell(2, 12).GetString());
            Assert.Equal("დიახ", sheet.Cell(2, 14).GetString());
            Assert.Equal(5, workbook.Worksheets.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
