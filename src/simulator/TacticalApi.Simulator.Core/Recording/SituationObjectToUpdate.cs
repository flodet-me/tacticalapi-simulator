using System.Collections;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     Turns a stored <see cref="SituationObject" /> back into the
///     <see cref="UpdateSituationObject" /> that would produce it - the inverse of
///     what an <c>ISituationObjectMerger</c> does.
///     This is what lets a recording be made from the read side of the contract
///     (<c>SubscribeSituationObjectEvents</c> against any TacticalAPI implementation,
///     not just this repo's Host) rather than only from an adapter's own write side.
///     Driven by the protobuf descriptors rather than eleven hand-written switch
///     arms, because the two message families are generated from the same contract
///     and mirror each other exactly: every <c>UpdateSymbol.location</c> has a
///     <c>Symbol.location</c>, every <c>UpdatePropertyString.content</c> a
///     <c>DataPropertyString.content</c>. Matching them by name means a field added
///     upstream is carried across automatically instead of being silently dropped
///     until someone notices - which matters for a repo whose CI already watches the
///     upstream contract for drift.
///     Two things a stored object holds cannot be expressed as an update and are
///     therefore not carried across: <c>creation_meta_data</c> (the server stamps its
///     own from the update's reporter/reporting time) and <c>additional_attributes</c>
///     (documented as created by the underlying service, with no update counterpart).
/// </summary>
public static class SituationObjectToUpdate
{
    private const string ForeignKeyField = "foreign_key";
    private const string ForeignKeysField = "foreign_keys";
    private const string IdentityField = "identity";
    private const string ReporterField = "reporter";
    private const string ReportingTimeField = "reporting_time";
    private const string UpdatePropertyPrefix = "UpdateProperty";

    /// <summary>
    ///     Converts one stored object. Returns null when the object carries no type
    ///     (an empty oneof), which nothing in the contract can act on anyway.
    /// </summary>
    public static UpdateSituationObject? Convert(SituationObject stored, Identity reporter, Timestamp reportingTime)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var storedCase = SituationObject.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(stored);
        if (storedCase is null) return null;

        var storedInner = (IMessage?)storedCase.Accessor.GetValue(stored);
        if (storedInner is null) return null;

        // The two oneofs use the same field names (and numbers) for the same types,
        // so the stored case names the update case directly.
        var updateCase = UpdateSituationObject.Descriptor.FindFieldByName(storedCase.Name);
        if (updateCase is null) return null;

        var updateInner = CreateEmpty(updateCase.MessageType);
        CopyIdentity(storedInner, updateInner);
        SetIfPresent(updateInner, ReporterField, reporter);
        SetIfPresent(updateInner, ReportingTimeField, reportingTime);
        CopyProperties(storedInner, updateInner, reporter, reportingTime);

        var update = new UpdateSituationObject();
        updateCase.Accessor.SetValue(update, updateInner);
        return update;
    }

    /// <summary>
    ///     Reads the identity out of whichever object type the oneof holds. Same
    ///     descriptor lookup <see cref="Convert" /> uses, exposed because a recorder
    ///     also needs the identity of objects it is NOT converting - a deleted object
    ///     is recorded as a delete, which carries nothing but its identity.
    /// </summary>
    public static Identity? IdentityOf(SituationObject stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var storedCase = SituationObject.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(stored);
        if (storedCase?.Accessor.GetValue(stored) is not IMessage storedInner) return null;

        return storedInner.Descriptor.FindFieldByName(IdentityField)?.Accessor.GetValue(storedInner) as Identity;
    }

    private static void CopyIdentity(IMessage storedInner, IMessage updateInner)
    {
        var storedIdentity = storedInner.Descriptor.FindFieldByName(IdentityField);
        if (storedIdentity?.Accessor.GetValue(storedInner) is Identity identity)
            SetIfPresent(updateInner, IdentityField, identity.Clone());
    }

    private static void CopyProperties(
        IMessage storedInner, IMessage updateInner, Identity reporter, Timestamp reportingTime)
    {
        foreach (var updateField in updateInner.Descriptor.Fields.InDeclarationOrder())
        {
            if (updateField.FieldType != FieldType.Message) continue;
            if (!updateField.MessageType.Name.StartsWith(UpdatePropertyPrefix, StringComparison.Ordinal)) continue;

            // The one field pair the contract does not mirror: the stored model keeps
            // a dictionary of foreign keys, the update model sets one at a time.
            if (updateField.Name == ForeignKeyField)
            {
                CopyForeignKey(storedInner, updateInner, updateField, reporter, reportingTime);
                continue;
            }

            var storedField = storedInner.Descriptor.FindFieldByName(updateField.Name);
            if (storedField?.Accessor.GetValue(storedInner) is not IMessage storedProperty) continue;

            var updateProperty = CreateEmpty(updateField.MessageType);
            CopyPropertyContent(storedProperty, updateProperty, reporter, reportingTime);
            updateField.Accessor.SetValue(updateInner, updateProperty);
        }
    }

    /// <summary>
    ///     Copies the payload fields of one property wrapper (content/contents, plus
    ///     the extras a few of them carry: a byte array's type, an identity's source,
    ///     a dimension's x/y/z). Everything the update wrapper declares exists on the
    ///     stored wrapper under the same name; creation_meta_data is stored-only and
    ///     is skipped for free by iterating the update side.
    /// </summary>
    private static void CopyPropertyContent(
        IMessage storedProperty, IMessage updateProperty, Identity reporter, Timestamp reportingTime)
    {
        foreach (var field in updateProperty.Descriptor.Fields.InDeclarationOrder())
        {
            var storedField = storedProperty.Descriptor.FindFieldByName(field.Name);
            if (storedField is null) continue;

            if (field.IsRepeated)
            {
                CopyRepeated(storedProperty, storedField, updateProperty, field, reporter, reportingTime);
                continue;
            }

            var value = storedField.Accessor.GetValue(storedProperty);
            if (value is null) continue;

            field.Accessor.SetValue(updateProperty, value is IMessage message ? DeepClone(message) : value);
        }
    }

    private static void CopyRepeated(
        IMessage storedProperty, FieldDescriptor storedField,
        IMessage updateProperty, FieldDescriptor updateField,
        Identity reporter, Timestamp reportingTime)
    {
        if (storedField.Accessor.GetValue(storedProperty) is not IList source) return;
        if (updateField.Accessor.GetValue(updateProperty) is not IList target) return;

        foreach (var item in source)
            switch (item)
            {
                // Overlay documents nest whole situation objects, which have to be
                // converted the same way rather than copied across.
                case SituationObject nested when Convert(nested, reporter, reportingTime) is { } nestedUpdate:
                    target.Add(nestedUpdate);
                    break;
                case SituationObject:
                    break;
                case IMessage message:
                    target.Add(DeepClone(message));
                    break;
                default:
                    target.Add(item);
                    break;
            }
    }

    private static void CopyForeignKey(
        IMessage storedInner, IMessage updateInner, FieldDescriptor updateField,
        Identity reporter, Timestamp reportingTime)
    {
        var storedField = storedInner.Descriptor.FindFieldByName(ForeignKeysField);
        if (storedField?.Accessor.GetValue(storedInner) is not IDictionary map) return;

        // An update carries exactly one foreign key, so a stored object holding
        // several can only be reconstructed one at a time. Recording the first
        // (rather than none) keeps the common single-key case lossless; a source
        // that sets several would need one update per key, which no source here does.
        foreach (DictionaryEntry entry in map)
        {
            if (entry.Value is not IMessage storedProperty) continue;

            var updateProperty = CreateEmpty(updateField.MessageType);
            CopyPropertyContent(storedProperty, updateProperty, reporter, reportingTime);
            updateField.Accessor.SetValue(updateInner, updateProperty);
            return;
        }
    }

    private static void SetIfPresent(IMessage message, string fieldName, object value)
    {
        message.Descriptor.FindFieldByName(fieldName)?.Accessor.SetValue(message, value);
    }

    private static IMessage CreateEmpty(MessageDescriptor descriptor)
    {
        return descriptor.Parser.ParseFrom(ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    ///     Deep-clones a message reached through reflection. The generated types all
    ///     have a strongly typed <c>Clone()</c>, but <see cref="IMessage" /> - which is
    ///     all a descriptor hands back - doesn't expose one, so this round-trips
    ///     through the wire format instead. Copies are cheap here (single properties,
    ///     not whole situations) and this keeps the recorded update fully detached
    ///     from the stored object it was read from.
    /// </summary>
    private static IMessage DeepClone(IMessage message)
    {
        return message.Descriptor.Parser.ParseFrom(message.ToByteArray());
    }
}
