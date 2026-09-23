using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Application;
using SkyConnect.RoamingoFlightExporter.Configuration;
using SkyConnect.RoamingoFlightExporter.Domain;
using SkyConnect.RoamingoFlightExporter.Export;
using SkyConnect.RoamingoFlightExporter.Infrastructure.Http;
using SkyConnect.RoamingoFlightExporter.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services 
    .AddOptions<ExporterOptions>()
    .Bind(builder.Configuration.GetSection(ExporterOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.Airports.Count > 0, "At least one airport is required.")
    .Validate(options => options.LookbackDays > 0, "LookbackDays must be greater than zero.")
    .Validate(options => options.MaxConcurrency > 0, "MaxConcurrency must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<ExporterOptions>>().Value;
    var apiKey = builder.Configuration["FLIGHTLABS_API_KEY"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        apiKey = options.FlightLabs.ApiKey;
    }

    if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "FlightLabs API key is missing. Set FLIGHTLABS_API_KEY or put a local-only value in appsettings.Local.json.");
    }

    return new FlightLabsCredentials(apiKey);
});

builder.Services.AddHttpClient<IJsonHttpClient, ResilientJsonHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SkyConnect.RoamingoFlightExporter/1.0");
});

builder.Services.AddSingleton<IFlightLabsClient, FlightLabsClient>();
builder.Services.AddSingleton<IDateRangePlanner, DateRangePlanner>();
builder.Services.AddSingleton<IBoundedParallelRunner, BoundedParallelRunner>();
builder.Services.AddSingleton<IFlightDisruptionClassifier, FlightDisruptionClassifier>();
builder.Services.AddSingleton<ICheckpointStore, CheckpointStore>();
builder.Services.AddSingleton<IFlightDisruptionCollector, FlightDisruptionCollector>();
builder.Services.AddSingleton<IExcelExporter, ExcelExporter>();
builder.Services.AddSingleton<ExportRunner>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

try
{
    await host.Services.GetRequiredService<ExportRunner>().RunAsync(CancellationToken.None);
}
catch (OperationCanceledException)
{
    logger.LogWarning("Export cancelled.");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    logger.LogError(ex, "Export failed.");
    Environment.ExitCode = 1;
}
