namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     One executable statement about what the <c>Situation</c> contract requires.
///     <see cref="Requirement" /> quotes the contract (or the interface semantics
///     documented for it) that the check enforces, so a failure points at the rule
///     rather than at this tool's opinion.
/// </summary>
/// <param name="Id">Stable slug, used to select checks and as the key in JSON output.</param>
/// <param name="Title">One-line description of what is being checked.</param>
/// <param name="Requirement">The contract rule this check enforces.</param>
/// <param name="Slow">Whether the check takes seconds rather than milliseconds (see --include-slow).</param>
/// <param name="RunAsync">The check itself.</param>
public sealed record ConformanceCheck(
    string Id,
    string Title,
    string Requirement,
    bool Slow,
    Func<ConformanceContext, CancellationToken, Task<CheckResult>> RunAsync);
