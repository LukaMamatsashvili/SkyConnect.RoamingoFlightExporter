using SkyConnect.RoamingoFlightExporter.Domain;
using SkyConnect.RoamingoFlightExporter.Models;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class FlightDisruptionClassifierTests
{
    [Fact]
    public void Classify_UsesHistoricalAtaWhenStatusHasNoLandedTime()
    {
        var occurrence = new FlightDetailOccurrence(new DateOnly(2026, 7, 18), "W67905", "Wizz Air",
            "KUT", "LCA", "6:00 AM", null, "7:10 AM", "Arrived", "10:20 AM");
        var result = new FlightDisruptionClassifier().Classify(occurrence, 180);
        Assert.Equal(190, result.ArrivalDelayMinutes);
        Assert.True(result.IsExportable);
    }
    [Fact]
    public void Classify_ReturnsPotentialDisruption_WhenArrivalDelayMeetsThreshold()
    {
        var classifier = new FlightDisruptionClassifier();
        var occurrence = new FlightDetailOccurrence(
            new DateOnly(2026, 9, 20),
            "PC321",
            "Pegasus",
            "KUT",
            "SAW",
            "00:25",
            "02:43",
            "02:35",
            "Landed 5:36 AM");

        var result = classifier.Classify(occurrence, 180);

        Assert.True(result.IsExportable);
        Assert.Equal(181, result.ArrivalDelayMinutes);
        Assert.Contains("Potentially relevant disruption", result.DisruptionType);
    }

    [Fact]
    public void Classify_ReturnsCancelled_WhenStatusIsCancelledWithoutDelayData()
    {
        var classifier = new FlightDisruptionClassifier();
        var occurrence = new FlightDetailOccurrence(
            new DateOnly(2026, 9, 20),
            "W62248",
            "Wizz Air",
            "KUT",
            "BUD",
            "04:35",
            null,
            "06:15",
            "Cancelled");

        var result = classifier.Classify(occurrence, 180);

        Assert.True(result.IsExportable);
        Assert.Equal("Cancelled", result.DisruptionType);
    }
}
