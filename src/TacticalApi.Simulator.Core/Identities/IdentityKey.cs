using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Identities;

/// <summary>
///     Turns the oneof-based <see cref="Identity" /> message into a stable string
///     key usable in dictionaries. The prefix keeps the identity kinds disjoint
///     (e.g. a string identity "42" never collides with the int identity 42).
/// </summary>
public static class IdentityKey
{
    public static string? TryCreate(Identity? identity)
    {
        return identity?.TypeCase switch
        {
            Identity.TypeOneofCase.UuidIdentity => $"u:{identity.UuidIdentity}",
            Identity.TypeOneofCase.StringIdentity => $"s:{identity.StringIdentity}",
            Identity.TypeOneofCase.Int32Identity => $"i:{identity.Int32Identity}",
            Identity.TypeOneofCase.Int64Identity => $"l:{identity.Int64Identity}",
            _ => null
        };
    }
}
