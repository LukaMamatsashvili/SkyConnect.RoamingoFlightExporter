# SkyConnect.RoamingoFlightExporter

Production-oriented .NET 8 console app that exports potentially relevant Roamingo flight disruptions for Georgia's main airports: TBS, KUT, and BUS.

The app discovers departures and arrivals from FlightLabs historical data, queries each unique flight number once, calculates delays from scheduled/actual times, and writes one Excel workbook for non-technical staff.

## Credential rule

Do not paste an API key into source code. The app reads the key from `FLIGHTLABS_API_KEY`, or from a local-only `appsettings.Local.json` file that is ignored by Git.

PowerShell environment variable setup:

```powershell
$env:FLIGHTLABS_API_KEY = "your-flightlabs-api-key"
```

Persistent user environment variable:

```powershell
[Environment]::SetEnvironmentVariable("FLIGHTLABS_API_KEY", "your-flightlabs-api-key", "User")
```

Local config option:

```json
{
  "Exporter": {
    "FlightLabs": {
      "ApiKey": "your-flightlabs-api-key"
    }
  }
}
```

Save that as `SkyConnect.RoamingoFlightExporter/appsettings.Local.json`.

## Run

```powershell
dotnet run --project .\SkyConnect.RoamingoFlightExporter\SkyConnect.RoamingoFlightExporter.csproj
```

The Excel file is written to:

```text
SkyConnect.RoamingoFlightExporter/bin/Debug/net8.0/Output/Roamingo_Flight_Disruptions_YYYY-MM-DD.xlsx
```

To write somewhere else:

```powershell
dotnet run --project .\SkyConnect.RoamingoFlightExporter\SkyConnect.RoamingoFlightExporter.csproj -- Exporter:OutputDirectory="C:\Temp\RoamingoOutput"
```

## Configuration

Defaults live in `appsettings.json`.

- `Exporter:Airports`: airport codes, default `TBS`, `KUT`, `BUS`
- `Exporter:MovementTypes`: default `departure`, `arrival`
- `Exporter:LookbackDays`: default `7`
- `Exporter:MinimumArrivalDelayMinutes`: default `180`
- `Exporter:MaxConcurrency`: default `4`
- `Exporter:RequestsPerRateLimitWindow`: default `8`
- `Exporter:RateLimitWindowSeconds`: default `10`, keeping the app safely under the 10 requests per 10 seconds package limit
- `Exporter:DelayBetweenRequestsMs`: legacy fallback delay, default `0`
- `Exporter:MaxApiCallsPerRun`: default `1200`, useful for validating the workflow on a 7-day run before scaling up
- `Exporter:OutputDirectory`: default `Output`
- `Exporter:HistoricalChunkDays`: default `1`
- `Exporter:PreferDailyHistoricalEndpoint`: default `true`

If the FlightLabs historical endpoint does not support `date=YYYY-MM-DD` for the current plan, set `PreferDailyHistoricalEndpoint` to `false`; the app will use `date_from` and `date_to` chunks.

## Architecture

The app is intentionally still lightweight, but its responsibilities are separated:

- `Application`: orchestration helpers such as date-window planning and bounded parallel execution.
- `Domain`: business rules and value objects, including disruption classification and stable flight occurrence keys.
- `Services`: FlightLabs-facing collection workflow.
- `Infrastructure/Http`: reusable resilient JSON HTTP client with retry, backoff, jitter, and `Retry-After` handling.
- `Export`: Excel workbook generation.

The console startup wires interfaces to implementations through dependency injection, so the API workflow, retry policy, domain rules, and Excel generation can evolve independently.

## Excel output

The workbook contains:

- `Potential Disruptions`: flights with a confidently calculated 3+ hour arrival delay, plus clearly cancelled flights.
- `Summary`: run date, period covered, airports, discovery totals, queried unique flight numbers, delays, cancellations, and rows exported.

The file intentionally uses wording such as `Potentially relevant disruption`. It does not classify flights as guaranteed compensation cases.

## Checkpoints

Every successful FlightLabs response is saved during the run, so already collected data is not lost if the API quota is reached later. On later runs, the app loads existing checkpoint files first and skips those API calls.

Checkpoint files are written under:

```text
SkyConnect.RoamingoFlightExporter/bin/Debug/net8.0/Output/Checkpoints/YYYY-MM-DD/
```

Historical discovery responses go to `Historical`; flight-number detail responses go to `FlightDetails`.

The `FlightDetails` folder is the raw log of the `flight?flight_number=...` step.

## Tests

```powershell
dotnet test
```

Tests cover landed-status parsing, delay calculation, midnight rollover, and cancellation detection. Tests do not call live FlightLabs APIs.
