using ClosedXML.Excel;

namespace SkyConnect.RoamingoFlightExporter.Export;

internal static class GeorgianWorkbook
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Potential Disruptions"] = "საყურადღებო ფრენები",
        ["Summary"] = "შეჯამება",
        ["Diagnostic Summary"] = "მონაცემების სისრულე",
        ["Diagnostic Audit"] = "დეტალური შემოწმება",
        ["Flight Date"] = "ფრენის თარიღი",
        ["Flight Number"] = "რეისის ნომერი",
        ["Airline"] = "ავიაკომპანია",
        ["Departure Airport"] = "გამგზავრების აეროპორტი",
        ["Destination Airport"] = "დანიშნულების აეროპორტი",
        ["Scheduled Departure"] = "გეგმური გამგზავრება",
        ["Actual Departure"] = "ფაქტობრივი გამგზავრება",
        ["Departure Delay (minutes)"] = "გამგზავრების დაგვიანება (წუთი)",
        ["Scheduled Arrival"] = "გეგმური ჩამოფრენა",
        ["Actual Arrival"] = "ფაქტობრივი ჩამოფრენა",
        ["Arrival Delay (minutes)"] = "ჩამოფრენის დაგვიანება (წუთი)",
        ["Flight Status"] = "ფრენის სტატუსი",
        ["Disruption Type"] = "შეფერხების ტიპი",
        ["Passenger Check Needed"] = "მგზავრის გადამოწმება",
        ["Yes"] = "დიახ", ["No"] = "არა",
        ["Cancelled"] = "გაუქმებული", ["Canceled"] = "გაუქმებული",
        ["Scheduled"] = "დაგეგმილი", ["Unknown"] = "უცნობი",
        ["Run date"] = "ანგარიშის შექმნის დრო",
        ["Period covered"] = "პერიოდი",
        ["Airports"] = "აეროპორტები",
        ["Movement types"] = "მიმართულებები",
        ["Planned maximum API calls"] = "მოთხოვნების შეფასება (გამეორებების გარეშე)",
        ["Total historical flights discovered"] = "შერჩეული ისტორიული ფრენები",
        ["Total unique flight numbers queried"] = "შერჩეული უნიკალური რეისები",
        ["Total 180+ minute arrival delays"] = "ზღვარს მიღწეული დაგვიანებები",
        ["Total cancellations"] = "გაუქმებული ფრენები",
        ["Rows exported"] = "ექსპორტირებული ფრენები",
        ["Historical flights discovered"] = "შერჩეული ისტორიული ფრენები",
        ["Unique flight numbers checked"] = "უნიკალური რეისების რაოდენობა",
        ["Diagnostic rows"] = "შემოწმების ჩანაწერები",
        ["Rows in selected period"] = "ჩანაწერები არჩეულ პერიოდში",
        ["Rows with calculated arrival delay"] = "გამოთვლილი ჩამოფრენის დაგვიანებით",
        ["Rows missing actual arrival in status"] = "ფაქტობრივი ჩამოფრენის დრო აკლია",
        ["Rows missing scheduled arrival"] = "გეგმური ჩამოფრენის დრო აკლია",
        ["Historical rows without matching detail occurrence"] = "შესაბამისი დეტალური ჩანაწერი ვერ მოიძებნა",
        ["Cancelled rows detected"] = "აღმოჩენილი გაუქმებები",
        ["Arrival delay 60+ minutes"] = "ჩამოფრენის დაგვიანება 60+ წუთი",
        ["Arrival delay 120+ minutes"] = "ჩამოფრენის დაგვიანება 120+ წუთი",
        ["Arrival delay 180+ minutes"] = "ჩამოფრენის დაგვიანება 180+ წუთი",
        ["Maximum calculated arrival delay (minutes)"] = "მაქსიმალური დაგვიანება (წუთი)",
        ["Potential disruption rows exported"] = "ექსპორტირებული საყურადღებო ფრენები",
        ["In Selected Period"] = "არჩეულ პერიოდშია",
        ["Matched Historical Discovery"] = "ისტორიულ ჩანაწერს ემთხვევა",
        ["Georgia Airport In Route"] = "არჩეული აეროპორტი მარშრუტშია",
        ["Parser Finding"] = "შემოწმების შედეგი",
        ["Export Decision"] = "ექსპორტის შედეგი",
        ["Cancelled status detected"] = "დაფიქსირდა გაუქმება",
        ["Scheduled arrival missing or not parseable"] = "გეგმური ჩამოფრენის დრო ვერ დადგინდა",
        ["Actual arrival not found in status"] = "ფაქტობრივი ჩამოფრენის დრო ვერ დადგინდა",
        ["Arrival appears earlier than scheduled"] = "ჩამოფრენა გეგმურზე ადრეა",
        ["Arrival delay calculated"] = "ჩამოფრენის დაგვიანება გამოთვლილია",
        ["No matching detail row returned for this historical flight occurrence"] = "ისტორიული ფრენის დეტალები პასუხში არ არის",
        ["Exported"] = "ექსპორტირებულია",
        ["Not exported - outside selected period"] = "არ ექსპორტირდება: არჩეული პერიოდის გარეთაა",
        ["Not exported - detail row does not match selected Georgia airport route"] = "არ ექსპორტირდება: მარშრუტი ფილტრს არ ემთხვევა",
        ["Not exported - delay cannot be established confidently"] = "არ ექსპორტირდება: დაგვიანება სარწმუნოდ ვერ დადგინდა",
        ["Not exported - actual arrival not found in status"] = "არ ექსპორტირდება: ფაქტობრივი ჩამოფრენის დრო აკლია",
        ["Not exported - scheduled arrival missing or not parseable"] = "არ ექსპორტირდება: გეგმური ჩამოფრენის დრო აკლია"
    };

    public static void Apply(XLWorkbook workbook)
    {
        foreach (var sheet in workbook.Worksheets.ToList())
        {
            sheet.Name = Translate(sheet.Name);
            foreach (var cell in sheet.CellsUsed().Where(c => c.DataType == XLDataType.Text).ToList())
                cell.Value = Translate(cell.GetString());
            sheet.Style.Font.FontName = "Sylfaen";
            sheet.Style.Font.FontSize = 11;
            sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Style.Alignment.WrapText = true;
            sheet.Row(1).Height = 58;
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#14665B");
            sheet.Row(1).Style.Font.FontColor = XLColor.White;
            sheet.SheetView.FreezeRows(1);
            sheet.ShowGridLines = false;
            foreach (var row in sheet.RowsUsed().Skip(1)) row.Height = 38;
            foreach (var column in sheet.ColumnsUsed()) column.Width = Math.Max(20, column.Width);
        }
    }

    private static string Translate(string text)
    {
        if (Labels.TryGetValue(text, out var label)) return label;
        return text.Replace("Landed", "ჩამოფრენილი", StringComparison.OrdinalIgnoreCase)
            .Replace("Estimated", "სავარაუდო", StringComparison.OrdinalIgnoreCase)
            .Replace("Departed", "გაფრენილი", StringComparison.OrdinalIgnoreCase)
            .Replace("Potentially relevant disruption", "პოტენციურად საყურადღებო შეფერხება")
            .Replace("arrival delay >=", "ჩამოფრენის დაგვიანება >=")
            .Replace("hour arrival delay", "საათიანი ჩამოფრენის დაგვიანება")
            .Replace("Not exported - arrival delay below", "არ ექსპორტირდება: დაგვიანება ნაკლებია ვიდრე")
            .Replace("minutes", "წუთი")
            .Replace("departure", "გამავალი")
            .Replace("arrival", "შემომავალი");
    }
}
