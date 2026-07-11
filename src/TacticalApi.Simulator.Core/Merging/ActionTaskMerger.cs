using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="ActionTask" /> objects (orders/tasks).</summary>
public sealed class ActionTaskMerger : ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.ActionTask;

    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.ActionTask?.Identity;
    }

    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.ActionTask?.ReportingTime;
    }

    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.ActionTask;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var task = current?.ActionTask?.Clone() ?? new ActionTask
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        task.ExpiryTime = PropertyMerge.Time(task.ExpiryTime, u.ExpiryTime, meta);
        task.Location = PropertyMerge.Location(task.Location, u.Location, meta);
        task.Name = PropertyMerge.String(task.Name, u.Name, meta);
        task.AdditionalInformation = PropertyMerge.String(task.AdditionalInformation, u.AdditionalInformation, meta);
        task.ActionTaskType = PropertyMerge.ActionTask(task.ActionTaskType, u.ActionTaskType, meta);
        task.ActionTaskStatus = PropertyMerge.ActionTaskStatus(task.ActionTaskStatus, u.ActionTaskStatus, meta);
        task.ActionTaskPriority = PropertyMerge.ActionTaskPriority(task.ActionTaskPriority, u.ActionTaskPriority, meta);
        task.CompletionRatio = PropertyMerge.Int(task.CompletionRatio, u.CompletionRatio, meta);
        task.PlannedStartTime = PropertyMerge.Time(task.PlannedStartTime, u.PlannedStartTime, meta);
        task.PlannedEndTime = PropertyMerge.Time(task.PlannedEndTime, u.PlannedEndTime, meta);
        task.ActionTaskEffects = PropertyMerge.References(task.ActionTaskEffects, u.ActionTaskEffects, meta);
        task.ActionTaskResources = PropertyMerge.References(task.ActionTaskResources, u.ActionTaskResources, meta);
        PropertyMerge.ForeignKey(task.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            ActionTask = task,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
