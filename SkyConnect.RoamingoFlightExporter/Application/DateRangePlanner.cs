namespace SkyConnect.RoamingoFlightExporter.Application;

public readonly record struct DateRange(DateOnly From, DateOnly To);

public interface IDateRangePlanner
{
    DateRange GetLookbackWindow(DateOnly today, int lookbackDays);

    IReadOnlyList<DateRange> Split(DateRange range, int chunkDays);
}

public sealed class DateRangePlanner : IDateRangePlanner
{
    public DateRange GetLookbackWindow(DateOnly today, int lookbackDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackDays);
        return new DateRange(today.AddDays(-lookbackDays), today);
    }

    public IReadOnlyList<DateRange> Split(DateRange range, int chunkDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkDays);

        var chunks = new List<DateRange>();
        for (var start = range.From; start <= range.To; start = start.AddDays(chunkDays))
        {
            var end = start.AddDays(chunkDays - 1);
            if (end > range.To)
            {
                end = range.To;
            }

            chunks.Add(new DateRange(start, end));
        }

        return chunks;
    }
}
