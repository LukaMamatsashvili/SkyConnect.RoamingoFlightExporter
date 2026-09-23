using SkyConnect.RoamingoFlightExporter.Application;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class DateRangePlannerTests
{
    [Fact]
    public void GetLookbackWindow_ReturnsInclusiveThirtyDayWindow()
    {
        var planner = new DateRangePlanner();

        var window = planner.GetLookbackWindow(new DateOnly(2026, 9, 21), 30);

        Assert.Equal(new DateOnly(2026, 8, 23), window.From);
        Assert.Equal(new DateOnly(2026, 9, 21), window.To);
    }

    [Fact]
    public void Split_ReturnsBoundedChunks()
    {
        var planner = new DateRangePlanner();

        var chunks = planner.Split(new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)), 2);

        Assert.Equal(
            [
                new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)),
                new DateRange(new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 4)),
                new DateRange(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5))
            ],
            chunks);
    }
}
