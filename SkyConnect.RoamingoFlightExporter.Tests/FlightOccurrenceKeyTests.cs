using SkyConnect.RoamingoFlightExporter.Domain;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class FlightOccurrenceKeyTests
{
    [Theory]
    [InlineData("Tbilisi (TBS)", "TBS")]
    [InlineData("Batumi (BUS)", "BUS")]
    [InlineData("Kutaisi (KUT)", "KUT")]
    [InlineData(" TBS ", "TBS")]
    public void Normalize_ExtractsIataCodeFromAirportDisplayName(string value, string expected)
    {
        var normalized = FlightOccurrenceKey.Normalize(value);

        Assert.Equal(expected, normalized);
    }
}
