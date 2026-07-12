using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="ActionTaskMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/ActionTaskMerger.cs).
/// </summary>
public sealed class ActionTaskMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new ActionTaskMerger();
        var effects = new UpdatePropertyReferences();
        effects.Contents.Add(new Identity { StringIdentity = "target1" });
        var create = new UpdateSituationObject
        {
            ActionTask = new UpdateActionTask
            {
                Identity = new Identity { StringIdentity = "t1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Recon Task" },
                ActionTaskType = new UpdatePropertyActionTask { Content = ActionTaskType.Reconnaissance },
                ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.InProgress },
                ActionTaskPriority =
                    new UpdatePropertyActionTaskPriorityCode { Content = ActionTaskPriorityType.Priority1 },
                CompletionRatio = new UpdatePropertyInt { Content = 10 },
                ActionTaskEffects = effects
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - only status and completion ratio change, name must survive.
        var update = new UpdateSituationObject
        {
            ActionTask = new UpdateActionTask
            {
                Identity = new Identity { StringIdentity = "t1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddMinutes(5)),
                ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.Complete },
                CompletionRatio = new UpdatePropertyInt { Content = 100 }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Recon Task", merged.ActionTask.Name.Content);
        Assert.Equal(ActionTaskType.Reconnaissance, merged.ActionTask.ActionTaskType.Content);
        Assert.Equal(ActionTaskStatusType.Complete, merged.ActionTask.ActionTaskStatus.Content);
        Assert.Equal(100, merged.ActionTask.CompletionRatio.Content);
        Assert.Equal("target1", Assert.Single(merged.ActionTask.ActionTaskEffects.Contents).StringIdentity);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal(ActionTaskStatusType.InProgress, created.ActionTask.ActionTaskStatus.Content);
    }
}
