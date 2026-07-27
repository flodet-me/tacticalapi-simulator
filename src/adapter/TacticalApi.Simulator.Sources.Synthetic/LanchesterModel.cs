namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Lanchester's Square Law (F. W. Lanchester, 1916): in aimed-fire combat each side's
///     casualty rate is proportional to the other side's current strength, so an edge in
///     numbers or effectiveness compounds instead of trading 1-for-1. Used to resolve engagement
///     outcomes between a friendly and a hostile force credibly, instead of picking a winner by
///     coin flip.
/// </summary>
internal static class LanchesterModel
{
    /// <summary>Result of resolving an engagement: survivors and casualties on both sides.</summary>
    public readonly record struct Outcome(
        int FriendlyRemaining, int HostileRemaining, int FriendlyCasualties, int HostileCasualties);

    /// <summary>
    ///     Euler-integrates dF/dt = -hostileEffectiveness * H and dH/dt = -friendlyEffectiveness * F
    ///     over <paramref name="ticks" /> steps. The two effectiveness coefficients capture
    ///     everything that isn't raw headcount - cover, surprise, training, weapon quality - so a
    ///     defender in prepared positions or an ambusher with surprise should use a higher
    ///     coefficient than the side on the receiving end.
    /// </summary>
    public static Outcome Resolve(
        int friendlyStrength, int hostileStrength,
        double friendlyEffectiveness, double hostileEffectiveness,
        int ticks = 6)
    {
        double f = friendlyStrength;
        double h = hostileStrength;

        for (var i = 0; i < ticks && f > 0 && h > 0; i++)
        {
            var friendlyLoss = hostileEffectiveness * h;
            var hostileLoss = friendlyEffectiveness * f;
            f = Math.Max(0, f - friendlyLoss);
            h = Math.Max(0, h - hostileLoss);
        }

        var friendlyRemaining = (int)Math.Round(f);
        var hostileRemaining = (int)Math.Round(h);
        return new Outcome(
            friendlyRemaining, hostileRemaining,
            friendlyStrength - friendlyRemaining, hostileStrength - hostileRemaining);
    }
}
