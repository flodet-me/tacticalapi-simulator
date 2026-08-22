using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>
///     Moves a recorded update's timestamps forward to now, whatever object type it
///     carries.
///     Descriptor-driven for the same reason
///     <see cref="TacticalApi.Simulator.Core.Recording.SituationObjectToUpdate" /> is:
///     every one of the eleven update messages declares reporting_time - and those
///     that expire declare expiry_time - under the same names, so there is nothing
///     here for a per-type switch to decide.
/// </summary>
internal static class ReplayTimestamps
{
    private const string ContentField = "content";
    private const string ExpiryTimeField = "expiry_time";
    private const string ReportingTimeField = "reporting_time";

    /// <summary>
    ///     Restamps one update to <paramref name="now" />, shifting its expiry_time by
    ///     the same amount.
    ///     Shifting expiry rather than leaving it is what makes an old recording
    ///     replayable at all: expiry_time is an absolute instant, so a recording made
    ///     an hour ago carries objects the receiving implementation would correctly
    ///     sweep the moment they arrived. Moving it with the reporting time preserves
    ///     what the recording actually captured - how long each object was meant to
    ///     live - instead of the wall-clock instant that lifetime happened to end at.
    /// </summary>
    internal static void Restamp(UpdateSituationObject update, Timestamp now)
    {
        var typeCase = UpdateSituationObject.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(update);
        if (typeCase?.Accessor.GetValue(update) is not IMessage inner) return;

        var reportingTimeField = inner.Descriptor.FindFieldByName(ReportingTimeField);
        if (reportingTimeField is null) return;

        var recordedReportingTime = reportingTimeField.Accessor.GetValue(inner) as Timestamp;
        reportingTimeField.Accessor.SetValue(inner, now);

        if (recordedReportingTime is null) return;

        var shift = now.ToDateTimeOffset() - recordedReportingTime.ToDateTimeOffset();
        ShiftExpiry(inner, shift);
    }

    private static void ShiftExpiry(IMessage inner, TimeSpan shift)
    {
        // Not every object type expires, and an object that never had an expiry
        // must not acquire one here.
        if (inner.Descriptor.FindFieldByName(ExpiryTimeField)?.Accessor.GetValue(inner) is not IMessage expiry) return;

        var contentField = expiry.Descriptor.FindFieldByName(ContentField);
        if (contentField?.Accessor.GetValue(expiry) is not Timestamp recorded) return;

        contentField.Accessor.SetValue(expiry, Timestamp.FromDateTimeOffset(recorded.ToDateTimeOffset() + shift));
    }
}
