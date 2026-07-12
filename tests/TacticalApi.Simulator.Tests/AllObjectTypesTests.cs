using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;
using Enum = System.Enum;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="AllMergers" />
///     (src/TacticalApi.Simulator.Core/Merging/AllMergers.cs).
/// </summary>
public sealed class AllMergersTests
{
    [Fact]
    public void CreateAll_CoversEveryUpdateOneofCase()
    {
        // Arrange
        var handled = AllMergers.CreateAll().Select(m => m.HandledCase).ToHashSet();
        var allCases = Enum.GetValues<UpdateSituationObject.TypeOneofCase>()
            .Where(c => c != UpdateSituationObject.TypeOneofCase.None);

        // Act & Assert
        foreach (var oneofCase in allCases) Assert.Contains(oneofCase, handled);
    }

    [Fact]
    public void CreateAll_HasNoDuplicateCases()
    {
        // Arrange
        var cases = AllMergers.CreateAll().Select(m => m.HandledCase).ToList();

        // Act & Assert
        Assert.Equal(cases.Count, cases.Distinct().Count());
    }
}
