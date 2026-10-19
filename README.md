# SkyConnect.RoamingoFlightExporter

.NET 8 console app that exports potentially relevant Roamingo flight disruptions for configurable airports and airlines to a Georgian Excel workbook.

The app discovers departures and arrivals from FlightLabs historical data, fetches dated details for each discovered occurrence, calculates delays from scheduled/actual times, and writes one Georgian Excel workbook for non-technical staff.

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
SkyConnect.RoamingoFlightExporter/bin/Debug/net8.0/Output/Roamingo_Flight_Disruptions_GE_YYYY-MM-DD_HHmmss.xlsx
```

To write somewhere else:

```powershell
dotnet run --project .\SkyConnect.RoamingoFlightExporter\SkyConnect.RoamingoFlightExporter.csproj -- Exporter:OutputDirectory="C:\Temp\RoamingoOutput"
```

## Configuration

Defaults live in `appsettings.json`.

- `Exporter:Airports`: airport IATA codes, currently `["KUT"]`. Use codes, not city names. Examples: `TBS`, `BUS`, `KUT`.
- `Exporter:MovementType`: `departure`, `arrival`, or `both`. Replaces the old `MovementTypes` array. Route direction is also enforced on returned detail rows.
- `Exporter:AirlineCodes`: IATA carrier codes such as `["W6", "W4", "W9"]`. An empty array accepts all airlines. Each Wizz Air operating carrier has its own code. Carrier selection uses the complete flight-number prefix and filters discovery before detail calls.
- `Exporter:LookbackDays`: currently `30`, starting that many days before run time through run time. Discovery operates in airport calendar dates; the first and last request windows are clipped to run time. Detail rows are matched by date because the API does not always provide timestamp offsets.
- `Exporter:AsOfDate`: `null` for a moving current period, or `yyyy-MM-dd` for a fixed calendar-date report and checkpoint folder.
- `Exporter:UseOnlyCachedData`: currently `true` for review. Set to `false` when ready to collect new data. Cache-only mode never fills missing checkpoints from the API.
- `Exporter:MinimumArrivalDelayMinutes`: default `180`
- `Exporter:MaxConcurrency`: currently `2`
- `Exporter:RequestsPerRateLimitWindow`: currently `4`
- `Exporter:RateLimitWindowSeconds`: default `10`, keeping the app safely under the 10 requests per 10 seconds package limit
- `Exporter:DelayBetweenRequestsMs`: legacy fallback delay, default `0`
- `Exporter:MaxApiCallsPerRun`: currently `1200`. The collector estimates logical calls; retries can increase actual HTTP attempts. The summary estimate is not a provider billing guarantee.
- `Exporter:OutputDirectory`: default `Output`
- `Exporter:HistoricalChunkDays`: default `1`
- `Exporter:PreferDailyHistoricalEndpoint`: default `true`

With `PreferDailyHistoricalEndpoint: true`, discovery uses one `date=yyyy-MM-dd` call per airport, direction and calendar day. Otherwise, it uses half-day windows. `HistoricalChunkDays` is a legacy option.

`DateSpecificDetails: true` explicitly supplies `date` for every discovered flight occurrence and caches by flight number plus date. The undated flight-number endpoint returned only nearby dates during validation, and range parameters did not provide the requested history. Historical dated responses can use `ATA` with `STATUS: Arrived`; ATA is used before falling back to a `Landed` status time. Dated mode needs many more calls than undated mode. `MaxApiCallsPerRun` now also caps actual HTTP attempts including retries. A budget-limited result is partial and must be read with its coverage diagnostics.

Current selection:

```json
{
  "Exporter": {
    "Airports": ["KUT"],
    "MovementType": "both",
    "AirlineCodes": ["W6", "W4", "W9"],
    "LookbackDays": 30,
    "AsOfDate": null,
    "UseOnlyCachedData": true,
    "MinimumArrivalDelayMinutes": 180
  }
}
```

Review settings before enabling live collection. Empty/missing details do not prove that no disruptions occurred; consult the workbook's coverage sheets. The flight-number endpoint can return a shorter history than requested.

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

- `საყურადღებო ფრენები`: confidently calculated delays meeting the configured threshold, plus clearly cancelled flights.
- `შეჯამება`: run date, period, selected airports/directions and totals.
- `მონაცემების სისრულე`: missing timestamps and unmatched historical occurrences.
- `დეტალური შემოწმება`: per-occurrence inclusion decisions and data limitations.
- `ყველა შერჩეული ფრენა`: all selected occurrences, including those with unavailable details. The duplicate technical audit is hidden.

Headers and business statuses are Georgian. Airport codes and airline names retain their source identifiers. Dates remain typed Excel values. Headers are frozen, tables are filterable, and cells wrap for readability. OutputDirectory is configurable; relative paths resolve under the executable folder. Filenames include the run time to avoid replacing previous reports.

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
