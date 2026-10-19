using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Export;

public interface IExcelExporter
{
    string Export(CollectionResult result);
}

public sealed class ExcelExporter(IOptions<ExporterOptions> options) : IExcelExporter
{
    private readonly ExporterOptions _options = options.Value;

    public string Export(CollectionResult result)
    {
        var outputDirectory = Path.IsPathRooted(_options.OutputDirectory)
            ? _options.OutputDirectory
            : Path.Combine(AppContext.BaseDirectory, _options.OutputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var fileName = $"Roamingo_Flight_Disruptions_GE_{DateTime.Now:yyyy-MM-dd_HHmmss}.xlsx";
        var path = Path.Combine(outputDirectory, fileName);

        using var workbook = new XLWorkbook();
        AddFlightsSheet(workbook, result.Rows);
        AddSummarySheet(workbook, result.Summary);
        var missing = result.Diagnostics.Count(row => row.InPeriod && row.ParserFinding.StartsWith("No matching detail row", StringComparison.Ordinal));
        var summarySheet = workbook.Worksheet("Summary");
        var warningRow = summarySheet.LastRowUsed()!.RowNumber() + 2;
        summarySheet.Cell(warningRow, 1).Value = "დეტალების გარეშე დარჩენილი ფრენები";
        summarySheet.Cell(warningRow, 2).Value = missing;
        summarySheet.Cell(warningRow + 1, 1).Value = "ისტორიის დაფარვა";
        summarySheet.Cell(warningRow + 1, 2).Value = missing > 0
            ? "არასრული: დაუმუშავებელი ფრენები არ ნიშნავს შეფერხების არარსებობას"
            : "ყველა ისტორიულ ფრენას მოეძებნა დეტალური ჩანაწერი";
        AddDiagnosticSummarySheet(workbook, result.Diagnostics, result.Summary);
        AddDiagnosticSheet(workbook, result.Diagnostics);
        var allFlights = workbook.Worksheet("Diagnostic Audit").CopyTo("ყველა შერჩეული ფრენა");
        var outOfPeriod = result.Diagnostics.Select((row, index) => (row, index))
            .Where(item => !item.row.InPeriod).Select(item => item.index + 2).OrderDescending();
        foreach (var row in outOfPeriod) allFlights.Row(row).Delete();
        workbook.Worksheet("Diagnostic Audit").Visibility = XLWorksheetVisibility.Hidden;
        GeorgianWorkbook.Apply(workbook);
        workbook.SaveAs(path);

        return path;
    }

    private static void AddFlightsSheet(XLWorkbook workbook, IReadOnlyList<DisruptedFlightRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Potential Disruptions");
        var headers = new[]
        {
            "Flight Date",
            "Flight Number",
            "Airline",
            "Departure Airport",
            "Destination Airport",
            "Scheduled Departure",
            "Actual Departure",
            "Departure Delay (minutes)",
            "Scheduled Arrival",
            "Actual Arrival",
            "Arrival Delay (minutes)",
            "Flight Status",
            "Disruption Type",
            "Passenger Check Needed"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var excelRow = rowIndex + 2;

            sheet.Cell(excelRow, 1).Value = row.FlightDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(excelRow, 2).Value = row.FlightNumber;
            sheet.Cell(excelRow, 3).Value = row.Airline;
            sheet.Cell(excelRow, 4).Value = row.DepartureAirport;
            sheet.Cell(excelRow, 5).Value = row.DestinationAirport;
            SetDateTime(sheet.Cell(excelRow, 6), row.ScheduledDeparture);
            SetDateTime(sheet.Cell(excelRow, 7), row.ActualDeparture);
            SetInt(sheet.Cell(excelRow, 8), row.DepartureDelayMinutes);
            SetDateTime(sheet.Cell(excelRow, 9), row.ScheduledArrival);
            SetDateTime(sheet.Cell(excelRow, 10), row.ActualArrival);
            SetInt(sheet.Cell(excelRow, 11), row.ArrivalDelayMinutes);
            sheet.Cell(excelRow, 12).Value = row.FlightStatus;
            sheet.Cell(excelRow, 13).Value = row.DisruptionType;
            sheet.Cell(excelRow, 14).Value = row.PassengerCheckNeeded;
        }

        var used = sheet.Range(1, 1, Math.Max(rows.Count + 1, 1), headers.Length);
        used.CreateTable("PotentialDisruptions");
        sheet.SheetView.FreezeRows(1);
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        sheet.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns(6, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        sheet.Columns(9, 10).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        sheet.Columns().AdjustToContents(8, 42);
        sheet.Column(13).Width = 44;
        sheet.Column(14).Width = 24;
    }

    private void AddSummarySheet(XLWorkbook workbook, ExportSummary summary)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        var values = new (string Label, object Value)[]
        {
            ("Run date", summary.RunDate.LocalDateTime),
            ("Period covered", $"{summary.PeriodStart:yyyy-MM-dd} to {summary.PeriodEnd:yyyy-MM-dd}"),
            ("Airports", string.Join(", ", summary.Airports)),
            ("Movement types", string.Join(", ", summary.MovementTypes)),
            ("ავიაკომპანიის კოდები", _options.AirlineCodes.Count == 0 ? "ყველა" : string.Join(", ", _options.AirlineCodes)),
            ("დაგვიანების მინიმალური ზღვარი (წუთი)", _options.MinimumArrivalDelayMinutes),
            ("Planned maximum API calls", summary.PlannedApiCalls),
            ("Total historical flights discovered", summary.TotalHistoricalFlightsDiscovered),
            ("Total unique flight numbers queried", summary.TotalUniqueFlightNumbersQueried),
            ("Total 180+ minute arrival delays", summary.TotalLongArrivalDelays),
            ("Total cancellations", summary.TotalCancellations),
            ("Rows exported", summary.RowsExported)
        };

        sheet.Cell(1, 1).Value = "Summary";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;

        for (var i = 0; i < values.Length; i++)
        {
            sheet.Cell(i + 3, 1).Value = values[i].Label;
            sheet.Cell(i + 3, 2).Value = XLCellValue.FromObject(values[i].Value);
        }

        sheet.Column(1).Style.Font.Bold = true;
        sheet.Column(1).Width = 38;
        sheet.Column(2).Width = 42;
        sheet.Cell(3, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
    }

    private static void AddDiagnosticSummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<DiagnosticFlightRow> diagnostics,
        ExportSummary summary)
    {
        var sheet = workbook.Worksheets.Add("Diagnostic Summary");
        var inPeriod = diagnostics.Where(row => row.InPeriod).ToList();
        var calculatedArrival = inPeriod.Count(row => row.ArrivalDelayMinutes is not null);
        var missingActualArrival = inPeriod.Count(row => row.ParserFinding.Contains("Actual arrival not found", StringComparison.OrdinalIgnoreCase));
        var missingScheduledArrival = inPeriod.Count(row => row.ParserFinding.Contains("Scheduled arrival missing", StringComparison.OrdinalIgnoreCase));
        var noMatchingDetail = inPeriod.Count(row => row.ParserFinding.Contains("No matching detail row", StringComparison.OrdinalIgnoreCase));
        var cancelled = inPeriod.Count(row => row.ParserFinding.Contains("Cancelled", StringComparison.OrdinalIgnoreCase));
        var over60 = inPeriod.Count(row => row.ArrivalDelayMinutes >= 60);
        var over120 = inPeriod.Count(row => row.ArrivalDelayMinutes >= 120);
        var over180 = inPeriod.Count(row => row.ArrivalDelayMinutes >= 180);
        var maxArrivalDelay = inPeriod
            .Where(row => row.ArrivalDelayMinutes is not null)
            .Select(row => row.ArrivalDelayMinutes!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var values = new (string Label, object Value)[]
        {
            ("Period covered", $"{summary.PeriodStart:yyyy-MM-dd} to {summary.PeriodEnd:yyyy-MM-dd}"),
            ("Airports", string.Join(", ", summary.Airports)),
            ("Historical flights discovered", summary.TotalHistoricalFlightsDiscovered),
            ("Unique flight numbers checked", summary.TotalUniqueFlightNumbersQueried),
            ("Diagnostic rows", diagnostics.Count),
            ("Rows in selected period", inPeriod.Count),
            ("Rows with calculated arrival delay", calculatedArrival),
            ("Rows missing actual arrival in status", missingActualArrival),
            ("Rows missing scheduled arrival", missingScheduledArrival),
            ("Historical rows without matching detail occurrence", noMatchingDetail),
            ("Cancelled rows detected", cancelled),
            ("Arrival delay 60+ minutes", over60),
            ("Arrival delay 120+ minutes", over120),
            ("Arrival delay 180+ minutes", over180),
            ("Maximum calculated arrival delay (minutes)", maxArrivalDelay),
            ("Potential disruption rows exported", summary.RowsExported)
        };

        sheet.Cell(1, 1).Value = "Diagnostic Summary";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;

        for (var i = 0; i < values.Length; i++)
        {
            sheet.Cell(i + 3, 1).Value = values[i].Label;
            sheet.Cell(i + 3, 2).Value = XLCellValue.FromObject(values[i].Value);
        }

        sheet.Column(1).Style.Font.Bold = true;
        sheet.Column(1).Width = 48;
        sheet.Column(2).Width = 36;
        sheet.Range(1, 1, values.Length + 2, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void AddDiagnosticSheet(XLWorkbook workbook, IReadOnlyList<DiagnosticFlightRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Diagnostic Audit");
        var headers = new[]
        {
            "Flight Date",
            "Flight Number",
            "Airline",
            "Departure Airport",
            "Destination Airport",
            "In Selected Period",
            "Matched Historical Discovery",
            "Georgia Airport In Route",
            "Scheduled Departure",
            "Actual Departure",
            "Departure Delay (minutes)",
            "Scheduled Arrival",
            "Actual Arrival",
            "Arrival Delay (minutes)",
            "Flight Status",
            "Parser Finding",
            "Export Decision"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var excelRow = rowIndex + 2;

            if (row.FlightDate is not null)
            {
                sheet.Cell(excelRow, 1).Value = row.FlightDate.Value.ToDateTime(TimeOnly.MinValue);
            }

            sheet.Cell(excelRow, 2).Value = row.FlightNumber;
            sheet.Cell(excelRow, 3).Value = row.Airline;
            sheet.Cell(excelRow, 4).Value = row.OriginAirport;
            sheet.Cell(excelRow, 5).Value = row.DestinationAirport;
            sheet.Cell(excelRow, 6).Value = row.InPeriod ? "Yes" : "No";
            sheet.Cell(excelRow, 7).Value = row.MatchedHistoricalDiscovery ? "Yes" : "No";
            sheet.Cell(excelRow, 8).Value = row.GeorgiaAirportInRoute ? "Yes" : "No";
            SetDateTime(sheet.Cell(excelRow, 9), row.ScheduledDeparture);
            SetDateTime(sheet.Cell(excelRow, 10), row.ActualDeparture);
            SetInt(sheet.Cell(excelRow, 11), row.DepartureDelayMinutes);
            SetDateTime(sheet.Cell(excelRow, 12), row.ScheduledArrival);
            SetDateTime(sheet.Cell(excelRow, 13), row.ActualArrival);
            SetInt(sheet.Cell(excelRow, 14), row.ArrivalDelayMinutes);
            sheet.Cell(excelRow, 15).Value = row.FlightStatus;
            sheet.Cell(excelRow, 16).Value = row.ParserFinding;
            sheet.Cell(excelRow, 17).Value = row.ExportDecision;
        }

        var used = sheet.Range(1, 1, Math.Max(rows.Count + 1, 1), headers.Length);
        used.CreateTable("DiagnosticAudit");
        sheet.SheetView.FreezeRows(1);
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
        sheet.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns(9, 10).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        sheet.Columns(12, 13).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        sheet.Columns().AdjustToContents(8, 44);
        sheet.Column(16).Width = 44;
        sheet.Column(17).Width = 54;
    }

    private static void SetDateTime(IXLCell cell, DateTime? value)
    {
        if (value is not null)
        {
            cell.Value = value.Value;
        }
    }

    private static void SetInt(IXLCell cell, int? value)
    {
        if (value is not null)
        {
            cell.Value = value.Value;
        }
    }
}
