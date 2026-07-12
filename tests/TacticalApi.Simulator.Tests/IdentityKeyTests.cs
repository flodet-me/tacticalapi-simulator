using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Identities;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="IdentityKey" />
///     (src/TacticalApi.Simulator.Core/Identities/IdentityKey.cs).
/// </summary>
public sealed class IdentityKeyTests
{
    [Fact]
    public void TryCreate_StringIdentity_PrefixesWithS()
    {
        // Arrange
        var identity = new Identity { StringIdentity = "track-1" };

        // Act
        var key = IdentityKey.TryCreate(identity);

        // Assert
        Assert.Equal("s:track-1", key);
    }

    [Fact]
    public void TryCreate_UuidIdentity_PrefixesWithU()
    {
        // Arrange
        var identity = new Identity { UuidIdentity = "11111111-1111-1111-1111-111111111111" };

        // Act
        var key = IdentityKey.TryCreate(identity);

        // Assert
        Assert.Equal("u:11111111-1111-1111-1111-111111111111", key);
    }

    [Fact]
    public void TryCreate_Int32Identity_PrefixesWithI()
    {
        // Arrange
        var identity = new Identity { Int32Identity = 42 };

        // Act
        var key = IdentityKey.TryCreate(identity);

        // Assert
        Assert.Equal("i:42", key);
    }

    [Fact]
    public void TryCreate_Int64Identity_PrefixesWithL()
    {
        // Arrange
        var identity = new Identity { Int64Identity = 9_000_000_000L };

        // Act
        var key = IdentityKey.TryCreate(identity);

        // Assert
        Assert.Equal("l:9000000000", key);
    }

    [Fact]
    public void TryCreate_DifferentKinds_NeverCollideForTheSameRawValue()
    {
        // Arrange: the same textual/numeric value "42" in each oneof case.
        var stringId = new Identity { StringIdentity = "42" };
        var int32Id = new Identity { Int32Identity = 42 };
        var int64Id = new Identity { Int64Identity = 42L };

        // Act
        var keys = new[] { IdentityKey.TryCreate(stringId), IdentityKey.TryCreate(int32Id), IdentityKey.TryCreate(int64Id) };

        // Assert
        Assert.Equal(3, keys.Distinct().Count());
    }

    [Fact]
    public void TryCreate_NullIdentity_ReturnsNull()
    {
        // Act
        var key = IdentityKey.TryCreate(null);

        // Assert
        Assert.Null(key);
    }

    [Fact]
    public void TryCreate_IdentityWithoutOneofSet_ReturnsNull()
    {
        // Arrange
        var identity = new Identity();

        // Act
        var key = IdentityKey.TryCreate(identity);

        // Assert
        Assert.Null(key);
    }
}
