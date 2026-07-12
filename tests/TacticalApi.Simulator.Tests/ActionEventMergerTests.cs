using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="ActionEventMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/ActionEventMerger.cs).
/// </summary>
public sealed class ActionEventMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_MapsThreatAndType()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new ActionEventMerger();

        // Act
        var created = merger.Merge(null, new UpdateSituationObject
        {
            ActionEvent = new UpdateActionEvent
            {
                Identity = new Identity { StringIdentity = "e1" },
                Reporter = reporter,
                ReportingTime = time,
                ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.SniperAttack },
                ThreatLevel = new UpdatePropertyInt { Content = 4 }
            }
        });

        // Assert
        Assert.Equal(ActionEventType.SniperAttack, created.ActionEvent.ActionEventType.Content);
        Assert.Equal(4, created.ActionEvent.ThreatLevel.Content);
    }
}
