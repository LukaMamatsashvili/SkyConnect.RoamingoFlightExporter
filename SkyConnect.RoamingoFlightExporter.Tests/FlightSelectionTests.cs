using SkyConnect.RoamingoFlightExporter.Configuration;
using Microsoft.Extensions.Configuration;
using SkyConnect.RoamingoFlightExporter.Domain;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class FlightSelectionTests
{
    [Fact]
    public void BindingDoesNotAppendDefaultAirports()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Airports:0"] = "KUT" }).Build();
        var options = new ExporterOptions();
        Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(configuration, options);
        Assert.Equal(new[] { "KUT" }, options.Airports);
    }
    [Theory]
    [InlineData("W67921", true)]
    [InlineData("W4 6743", true)]
    [InlineData("WZ7921", false)]
    [InlineData("PC321", false)]
    public void AirlineFilterUsesCompleteCarrierCode(string number, bool expected)
        => Assert.Equal(expected, new FlightSelection(new ExporterOptions
        { AirlineCodes = ["W6", "W4"] }).MatchesAirline(number));

    [Fact]
    public void EmptyAirlineFilterAcceptsAnyCarrier()
        => Assert.True(new FlightSelection(new ExporterOptions()).MatchesAirline("PC321"));

    [Theory]
    [InlineData("departure", "Kutaisi (KUT)", "BUD", true)]
    [InlineData("departure", "BUD", "KUT", false)]
    [InlineData("arrival", "KUT", "BUD", false)]
    [InlineData("arrival", "BUD", "Kutaisi (KUT)", true)]
    [InlineData("both", "BUD", "KUT", true)]
    [InlineData("both", "TBS", "BUD", false)]
    public void DirectionFilterHonorsSelectedAirport(string mode, string origin, string destination, bool expected)
        => Assert.Equal(expected, new FlightSelection(new ExporterOptions
        { Airports = ["KUT"], MovementType = mode }).MatchesRoute(origin, destination));
}
