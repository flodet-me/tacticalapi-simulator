namespace TacticalApi.Simulator.Core.Control;

/// <summary>
///     The simulator's pause switch: a single process-wide flag the store and the
///     expiry sweeper consult before doing anything that changes the situation.
///     While paused the situation is frozen - writes are rejected with an error
///     header (rather than silently swallowed, so a client can tell the difference)
///     and nothing expires - which is what makes a paused situation safe to inspect,
///     screenshot, or diff against.
///     Lives in Core rather than the Host because the store enforces it; the Host
///     only exposes the toggle over its control endpoints, and nothing in the
///     TacticalAPI contract itself is involved.
/// </summary>
public sealed class SimulationPause
{
    private volatile bool _paused;

    /// <summary>Whether the situation is currently frozen.</summary>
    public bool IsPaused => _paused;

    /// <summary>Error message returned by write paths while paused.</summary>
    public const string PausedMessage =
        "Simulator is paused; the situation is frozen and writes are rejected until it is resumed.";

    /// <summary>Freezes the situation. Returns true if this call was the one that paused it.</summary>
    public bool Pause()
    {
        var wasPaused = _paused;
        _paused = true;
        return !wasPaused;
    }

    /// <summary>Unfreezes the situation. Returns true if this call was the one that resumed it.</summary>
    public bool Resume()
    {
        var wasPaused = _paused;
        _paused = false;
        return wasPaused;
    }
}
