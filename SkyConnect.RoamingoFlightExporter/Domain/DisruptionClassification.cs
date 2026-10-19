using SkyConnect.RoamingoFlightExporter.Models;
using SkyConnect.RoamingoFlightExporter.Services;

namespace SkyConnect.RoamingoFlightExporter.Domain;

public sealed record DisruptionClassification(
    DateTime? ScheduledDeparture,
    DateTime? ActualDeparture,
    int? DepartureDelayMinutes,
    DateTime? ScheduledArrival,
    DateTime? ActualArrival,
    int? ArrivalDelayMinutes,
    string DisruptionType)
{
    public bool IsExportable => !string.IsNullOrWhiteSpace(DisruptionType);
}

public interface IFlightDisruptionClassifier
{
    DisruptionClassification Classify(FlightDetailOccurrence occurrence, int minimumArrivalDelayMinutes);
}

public sealed class FlightDisruptionClassifier : IFlightDisruptionClassifier
{
    public DisruptionClassification Classify(FlightDetailOccurrence occurrence, int minimumArrivalDelayMinutes)
    {
        var scheduledDeparture = FlightTimeParser.BuildDateTime(occurrence.FlightDate, occurrence.ScheduledDeparture);
        var actualDeparture = FlightTimeParser.BuildDateTime(occurrence.FlightDate, occurrence.ActualDeparture, scheduledDeparture);
        var scheduledArrival = FlightTimeParser.BuildDateTime(occurrence.FlightDate, occurrence.ScheduledArrival, scheduledDeparture);
        var landedTime = FlightTimeParser.ParseLandedTime(occurrence.Status);
        var actualArrival = FlightTimeParser.BuildDateTime(occurrence.FlightDate, occurrence.ActualArrival, scheduledArrival)
            ?? (landedTime is null ? null
                : FlightTimeParser.BuildDateTime(occurrence.FlightDate, landedTime.Value.ToString("HH:mm"), scheduledArrival));

        var departureDelay = FlightTimeParser.CalculateDelayMinutes(scheduledDeparture, actualDeparture);
        var arrivalDelay = FlightTimeParser.CalculateDelayMinutes(scheduledArrival, actualArrival);

        return new DisruptionClassification(
            scheduledDeparture,
            actualDeparture,
            departureDelay,
            scheduledArrival,
            actualArrival,
            arrivalDelay,
            GetDisruptionType(occurrence.Status, arrivalDelay, minimumArrivalDelayMinutes));
    }

    private static string GetDisruptionType(string? status, int? arrivalDelay, int minimumArrivalDelayMinutes)
    {
        if (FlightTimeParser.IsCancelled(status))
        {
            return "Cancelled";
        }

        return arrivalDelay >= minimumArrivalDelayMinutes
            ? $"Potentially relevant disruption - arrival delay >= {minimumArrivalDelayMinutes} minutes"
            : "";
    }
}
