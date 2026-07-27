namespace TacticalApi.Simulator.Sources;

/// <summary>Description of a moving track, expressed in plain values.</summary>
public readonly record struct TrackReport(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    double? AltitudeMeters,
    double? CourseDegrees,
    double? SpeedMetersPerSecond,
    string? AdditionalInformation);
