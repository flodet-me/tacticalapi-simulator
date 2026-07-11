using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
/// Fully offline source: simulates aircraft flying circular patterns around a
/// configurable center. Useful for demos and load tests without internet
/// access. Emits plain TacticalAPI UpdateSituationObject (Symbol) messages.
/// </summary>
public sealed class SyntheticAirTrackSource : ISimulationSource
{
    private const double EarthRadiusKm = 6371.0;

    private readonly IOptionsMonitor<SyntheticAirTrackOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _epoch;

    public SyntheticAirTrackSource(IOptionsMonitor<SyntheticAirTrackOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _epoch = timeProvider.GetUtcNow();
    }

    public string Name => "SyntheticAirTracks";

    public bool Enabled => _options.CurrentValue.Enabled;

    public TimeSpan Interval => _options.CurrentValue.UpdateInterval;

    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _timeProvider.GetUtcNow();
        var elapsedSeconds = (now - _epoch).TotalSeconds;
        var random = new Random(options.Seed);

        var updates = new List<UpdateSituationObject>(options.TrackCount);
        for (var i = 0; i < options.TrackCount; i++)
        {
            // Per-track deterministic parameters.
            var phase = random.NextDouble() * 2 * Math.PI;
            var radiusKm = options.RadiusKm * (0.5 + random.NextDouble());
            var clockwise = random.Next(2) == 0 ? 1 : -1;
            var altitude = 1_000 + random.Next(10) * 1_000;

            // Angular speed follows the configured ground speed.
            var omega = clockwise * options.SpeedMetersPerSecond / (radiusKm * 1000.0);
            var angle = phase + (omega * elapsedSeconds);

            var latitude = options.CenterLatitude + (radiusKm / EarthRadiusKm) * (180.0 / Math.PI) * Math.Sin(angle);
            var longitude = options.CenterLongitude +
                            (radiusKm / EarthRadiusKm) * (180.0 / Math.PI) * Math.Cos(angle) /
                            Math.Cos(options.CenterLatitude * Math.PI / 180.0);

            // Course is tangential to the orbit.
            var course = NormalizeDegrees((angle * 180.0 / Math.PI) + (clockwise > 0 ? 90 : -90));

            var track = new TrackReport(
                Id: $"synthetic:air:{i:D3}",
                Name: $"SIM{i:D3}",
                Latitude: latitude,
                Longitude: longitude,
                AltitudeMeters: altitude,
                CourseDegrees: course,
                SpeedMetersPerSecond: options.SpeedMetersPerSecond,
                AdditionalInformation: "Synthetic simulator track");

            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, options.ReporterId, options.SymbolCode, options.SymbolCatalog, now, options.TrackTimeToLive));
        }

        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }
}
