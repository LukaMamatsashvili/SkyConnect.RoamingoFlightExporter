using SkyConnect.RoamingoFlightExporter.Services;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class FlightTimeParserTests
{
    [Fact]
    public void ParseLandedTime_ReadsStandardStatus()
    {
        var landed = FlightTimeParser.ParseLandedTime("Landed 4:30 AM");

        Assert.Equal(new TimeOnly(4, 30), landed);
    }

    [Fact]
    public void CalculateDelayMinutes_HandlesArrivalAfterMidnight()
    {
        var flightDate = new DateOnly(2026, 9, 20);
        var scheduledDeparture = FlightTimeParser.BuildDateTime(flightDate, "22:10");
        var scheduledArrival = FlightTimeParser.BuildDateTime(flightDate, "00:35", scheduledDeparture);
        var actualArrival = FlightTimeParser.BuildDateTime(flightDate, "03:45", scheduledArrival);

        var delay = FlightTimeParser.CalculateDelayMinutes(scheduledArrival, actualArrival);

        Assert.Equal(190, delay);
    }

    [Fact]
    public void CalculateDelayMinutes_HandlesDepartureDelay()
    {
        var flightDate = new DateOnly(2026, 9, 20);
        var scheduled = FlightTimeParser.BuildDateTime(flightDate, "12:25 AM");
        var actual = FlightTimeParser.BuildDateTime(flightDate, "2:43 AM", scheduled);

        var delay = FlightTimeParser.CalculateDelayMinutes(scheduled, actual);

        Assert.Equal(138, delay);
    }

    [Fact]
    public void IsCancelled_DetectsCancelledStatuses()
    {
        Assert.True(FlightTimeParser.IsCancelled("Cancelled"));
        Assert.True(FlightTimeParser.IsCancelled("Flight canceled by airline"));
    }
}
