using System.Collections.Frozen;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Builds and inspects situation objects of any of the eleven types, driven by
///     the protobuf descriptors.
///     Deliberately duplicated rather than shared with
///     <c>TacticalApi.Simulator.Core</c>, which has near-identical machinery: this
///     tool judges implementations of the contract, and a checker that shares code
///     with one of them cannot be trusted to judge the others. The only thing it is
///     allowed to depend on is the generated contract itself
///     (<c>TacticalApi.Simulator.Contracts</c>) - check the project file, it has no
///     other project reference, and that is on purpose.
///     Descriptor-driven so that a twelfth object type added upstream is covered by
///     the suite automatically instead of quietly going unchecked.
/// </summary>
public static class SituationObjects
{
    private const string IdentityField = "identity";
    private const string ReporterField = "reporter";
    private const string ReportingTimeField = "reporting_time";

    private static readonly FrozenDictionary<SituationObject.TypeOneofCase, FieldDescriptor> UpdateFieldsByType =
        BuildUpdateFields();

    /// <summary>Every situation object type the contract declares, in oneof order.</summary>
    public static IReadOnlyList<SituationObject.TypeOneofCase> AllTypes { get; } =
        SituationObject.Descriptor.Oneofs[0].Fields
            .Select(field => (SituationObject.TypeOneofCase)field.FieldNumber)
            .ToList();

    /// <summary>
    ///     Builds the smallest update the contract permits for a type: identity,
    ///     reporter and reporting_time, the three fields every one of the eleven
    ///     update messages marks "Required:" and nothing else.
    /// </summary>
    public static UpdateSituationObject CreateMinimalUpdate(
        SituationObject.TypeOneofCase type, Identity identity, Identity reporter, Timestamp reportingTime)
    {
        var field = UpdateFieldsByType[type];
        var inner = field.MessageType.Parser.ParseFrom(ReadOnlySpan<byte>.Empty);

        Set(inner, IdentityField, identity);
        Set(inner, ReporterField, reporter);
        Set(inner, ReportingTimeField, reportingTime);

        var update = new UpdateSituationObject();
        field.Accessor.SetValue(update, inner);
        return update;
    }

    /// <summary>Reads the identity out of whichever type the object carries, or null if it carries none.</summary>
    public static Identity? IdentityOf(SituationObject stored)
    {
        var typeCase = SituationObject.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(stored);
        if (typeCase?.Accessor.GetValue(stored) is not IMessage inner) return null;

        return inner.Descriptor.FindFieldByName(IdentityField)?.Accessor.GetValue(inner) as Identity;
    }

    /// <summary>
    ///     Reads a property's <c>creation_meta_data</c> - the reporter and timestamp
    ///     the implementation stamped when it stored that property.
    /// </summary>
    public static CreationMetaData? MetaDataOf(SituationObject stored, string propertyName)
    {
        var typeCase = SituationObject.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(stored);
        if (typeCase?.Accessor.GetValue(stored) is not IMessage inner) return null;
        if (inner.Descriptor.FindFieldByName(propertyName)?.Accessor.GetValue(inner) is not IMessage property)
            return null;

        return property.Descriptor.FindFieldByName("creation_meta_data")?.Accessor.GetValue(property)
            as CreationMetaData;
    }

    /// <summary>Kebab-case name of a type, used in check ids ("overlay-document").</summary>
    public static string SlugOf(SituationObject.TypeOneofCase type)
    {
        return UpdateFieldsByType[type].Name.Replace('_', '-');
    }

    /// <summary>Human-readable name of a type, used in check titles ("overlay_document").</summary>
    public static string NameOf(SituationObject.TypeOneofCase type)
    {
        return UpdateFieldsByType[type].Name;
    }

    private static void Set(IMessage message, string fieldName, object value)
    {
        // Every update message declares all three; if the contract ever changes that,
        // the check that uses this will fail loudly rather than build a silent lie.
        message.Descriptor.FindFieldByName(fieldName)?.Accessor.SetValue(message, value);
    }

    /// <summary>
    ///     Pairs each SituationObject oneof case with the UpdateSituationObject field
    ///     that produces it, matched by field name rather than by number - the two
    ///     oneofs happen to agree on numbers today, and relying on that silently
    ///     rather than checking the name would be exactly the kind of assumption this
    ///     tool exists to catch in other people's code.
    /// </summary>
    private static FrozenDictionary<SituationObject.TypeOneofCase, FieldDescriptor> BuildUpdateFields()
    {
        var pairs = new Dictionary<SituationObject.TypeOneofCase, FieldDescriptor>();

        foreach (var storedField in SituationObject.Descriptor.Oneofs[0].Fields)
        {
            var updateField = UpdateSituationObject.Descriptor.FindFieldByName(storedField.Name);
            if (updateField is not null)
                pairs[(SituationObject.TypeOneofCase)storedField.FieldNumber] = updateField;
        }

        return pairs.ToFrozenDictionary();
    }
}
