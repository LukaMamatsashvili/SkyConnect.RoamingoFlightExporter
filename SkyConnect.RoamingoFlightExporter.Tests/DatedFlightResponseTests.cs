using System.Text.Json;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Infrastructure.Http;
using SkyConnect.RoamingoFlightExporter.Services;

namespace SkyConnect.RoamingoFlightExporter.Tests;

public sealed class DatedFlightResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatedResponseAcceptsSingleObjectAndArray(bool array)
    {
        var json = """{"DATE":"18 Jul 2026","FROM":"Kutaisi (KUT)","TO":"Larnarca (LCA)","STD":"6:00 AM","STA":"7:10 AM","ATA":"10:20 AM","STATUS":"Arrived"}""";
        var client = new FlightLabsClient(new FakeJson("{\"success\":true,\"data\":" + (array ? "[" + json + "]" : json) + "}"),
            Options.Create(new ExporterOptions()), new FlightLabsCredentials("test-placeholder"));
        var rows = await client.GetFlightDetailsAsync("W67905", CancellationToken.None, new DateOnly(2026, 7, 18));
        Assert.Single(rows);
        Assert.Equal("10:20 AM", rows[0].ActualArrival);
        Assert.Equal("W67905", rows[0].FlightNumber);
    }

    private sealed class FakeJson(string json) : IJsonHttpClient
    {
        public Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(JsonDocument.Parse(json));
    }
}
