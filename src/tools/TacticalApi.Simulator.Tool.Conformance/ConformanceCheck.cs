namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     How much weight a failing check carries.
///     The distinction exists because the TacticalAPI contract is a .proto file with
///     prose comments, not a specification with normative language, and pretending
///     otherwise would make this tool dishonest. Some rules are stated outright
///     ("Required: The unique identity of the symbol", "Returns all non-deleted
///     objects"); others are behaviour this simulator implements and believes
///     correct, but which the contract simply doesn't address.
///     Telling an implementer their server is non-conformant because it differs from
///     us on something the contract never mentions would be the fastest way to make
///     this tool ignored.
/// </summary>
public enum CheckSeverity
{
    /// <summary>The contract states this outright. A failure fails the run.</summary>
    Required,

    /// <summary>
    ///     Behaviour the contract leaves open, where this is the reading the simulator
    ///     implements. Reported, but doesn't fail the run unless <c>--strict</c>.
    /// </summary>
    Advisory
}

/// <summary>
///     One executable statement about what the <c>Situation</c> contract requires.
///     <see cref="Requirement" /> quotes the contract (or, for an
///     <see cref="CheckSeverity.Advisory" /> check, describes the reading being
///     applied) so a failure points at the rule rather than at this tool's opinion.
/// </summary>
/// <param name="Id">Stable slug, used to select checks and as the key in JSON output.</param>
/// <param name="Title">One-line description of what is being checked.</param>
/// <param name="Requirement">The contract rule this check enforces.</param>
/// <param name="Severity">Whether a failure means non-conformant or merely different.</param>
/// <param name="Mutating">Whether the check writes to the situation (see --read-only).</param>
/// <param name="Slow">Whether the check takes seconds rather than milliseconds (see --include-slow).</param>
/// <param name="RunAsync">The check itself.</param>
public sealed record ConformanceCheck(
    string Id,
    string Title,
    string Requirement,
    CheckSeverity Severity,
    bool Mutating,
    bool Slow,
    Func<ConformanceContext, CancellationToken, Task<CheckResult>> RunAsync);
