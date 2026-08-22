namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>How a single conformance check turned out.</summary>
public enum CheckOutcome
{
    /// <summary>The implementation behaved as the contract requires.</summary>
    Passed,

    /// <summary>The implementation behaved differently from what the contract requires.</summary>
    Failed,

    /// <summary>The check could not be run (not selected, or a prerequisite was unavailable).</summary>
    Skipped
}

/// <summary>
///     The outcome of one check, plus the detail a reader needs to act on it.
///     A failure carries what was expected and what actually happened rather than
///     just "failed", because the audience for this report is someone about to argue
///     with an implementer about whose side of the contract is wrong.
/// </summary>
/// <param name="Outcome">Pass/fail/skip.</param>
/// <param name="Detail">Human-readable explanation; required for anything but a plain pass.</param>
public readonly record struct CheckResult(CheckOutcome Outcome, string? Detail)
{
    /// <summary>A passing result.</summary>
    public static CheckResult Pass(string? detail = null)
    {
        return new CheckResult(CheckOutcome.Passed, detail);
    }

    /// <summary>A failing result, describing the mismatch.</summary>
    public static CheckResult Fail(string detail)
    {
        return new CheckResult(CheckOutcome.Failed, detail);
    }

    /// <summary>A skipped result, describing why.</summary>
    public static CheckResult Skip(string detail)
    {
        return new CheckResult(CheckOutcome.Skipped, detail);
    }
}
