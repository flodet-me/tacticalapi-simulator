using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;
using Enum = System.Enum;

namespace TacticalApi.Simulator.Tests;

public sealed class AllMergersTests
{
    [Fact]
    public void CreateAll_CoversEveryUpdateOneofCase()
    {
        var handled = AllMergers.CreateAll().Select(m => m.HandledCase).ToHashSet();

        var allCases = Enum.GetValues<UpdateSituationObject.TypeOneofCase>()
            .Where(c => c != UpdateSituationObject.TypeOneofCase.None);

        foreach (var oneofCase in allCases) Assert.Contains(oneofCase, handled);
    }

    [Fact]
    public void CreateAll_HasNoDuplicateCases()
    {
        var cases = AllMergers.CreateAll().Select(m => m.HandledCase).ToList();
        Assert.Equal(cases.Count, cases.Distinct().Count());
    }
}
