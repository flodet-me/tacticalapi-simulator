using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Fully offline source: simulates aircraft flying circular patterns around a
///     configurable center. Useful for demos and load tests without internet
///     access. Emits plain TacticalAPI UpdateSituationObject (Symbol) messages.
/// </summary>
public sealed class SyntheticAirTrackSource(
    IOptionsMonitor<SyntheticAirTrackOptions> options,
    TimeProvider timeProvider,
    ILogger<SyntheticAirTrackSource> logger)
    : ISimulationSource
{
    private const double EarthRadiusKm = 6371.0;
    private readonly DateTimeOffset _epoch = timeProvider.GetUtcNow();

    /// <inheritdoc/>
    public string Name => SimulationSourceName.FromSectionName(SyntheticAirTrackOptions.SectionName);

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <inheritdoc/>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var options1 = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var elapsedSeconds = (now - _epoch).TotalSeconds;
        var random = new Random(options1.Seed);

        var updates = new List<UpdateSituationObject>(options1.TrackCount);
        for (var i = 0; i < options1.TrackCount; i++)
        {
            // Per-track deterministic parameters.
            var phase = random.NextDouble() * 2 * Math.PI;
            var radiusKm = options1.RadiusKm * (0.5 + random.NextDouble());
            var clockwise = random.Next(2) == 0 ? 1 : -1;
            var altitude = 1_000 + random.Next(10) * 1_000;

            // Angular speed follows the configured ground speed.
            var omega = clockwise * options1.SpeedMetersPerSecond / (radiusKm * 1000.0);
            var angle = phase + omega * elapsedSeconds;

            var latitude = options1.CenterLatitude + radiusKm / EarthRadiusKm * (180.0 / Math.PI) * Math.Sin(angle);
            var longitude = options1.CenterLongitude +
                            radiusKm / EarthRadiusKm * (180.0 / Math.PI) * Math.Cos(angle) /
                            Math.Cos(options1.CenterLatitude * Math.PI / 180.0);

            // Course is tangential to the orbit.
            var course = NormalizeDegrees(angle * 180.0 / Math.PI + (clockwise > 0 ? 90 : -90));

            var track = new TrackReport(
                $"synthetic:air:{i:D3}",
                $"SIM{i:D3}",
                latitude,
                longitude,
                altitude,
                course,
                options1.SpeedMetersPerSecond,
                "Synthetic simulator track");

            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, options1.ReporterId, options1.SymbolCode, options1.SymbolCatalog, now,
                options1.TrackTimeToLive));
        }

        logger.AirTracksProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }
}
