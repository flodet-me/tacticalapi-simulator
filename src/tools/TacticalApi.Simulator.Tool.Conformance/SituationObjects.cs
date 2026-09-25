using System.Collections;
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

    /// <summary>Every identity kind the contract declares, in oneof order.</summary>
    public static IReadOnlyList<FieldDescriptor> IdentityKinds { get; } =
        [.. Identity.Descriptor.Oneofs[0].Fields];

    /// <summary>Every location kind <c>SymbolLocation</c> declares, in oneof order.</summary>
    public static IReadOnlyList<FieldDescriptor> LocationKinds { get; } =
        [.. SymbolLocation.Descriptor.Oneofs[0].Fields];

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

    /// <summary>
    ///     Builds an <see cref="Identity" /> of one of the four kinds the contract's
    ///     oneof declares, carrying <paramref name="value" /> in whichever
    ///     representation that kind uses.
    ///     Integer kinds get a hash of the value rather than the string itself: they
    ///     are declared as int32/int64, and the point of the check they serve is
    ///     whether the implementation keys on the right oneof field at all.
    /// </summary>
    public static Identity CreateIdentity(FieldDescriptor kind, string value)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var identity = new Identity();
        object typed = kind.FieldType switch
        {
            FieldType.String when kind.Name == "uuid_identity" => Deterministic(value).ToString(),
            FieldType.String => value,
            FieldType.Int32 => (int)(uint)value.GetHashCode(StringComparison.Ordinal),
            FieldType.Int64 => (long)(uint)value.GetHashCode(StringComparison.Ordinal),
            _ => value
        };

        kind.Accessor.SetValue(identity, typed);
        return identity;
    }

    /// <summary>
    ///     Builds a <see cref="SymbolLocation" /> of one of the nine kinds the contract
    ///     declares, with its geometry filled in.
    ///     The geometry is populated by walking the descriptors for anything shaped
    ///     like a <see cref="GeoPoint" /> - every location kind carries its geometry
    ///     either as a singular GeoPoint field, a repeated one, or a repeated message
    ///     that contains one - rather than by nine hand-written builders. An empty
    ///     location would still exercise the oneof, but an implementation would be
    ///     entirely within its rights to reject a polygon with no points, and a check
    ///     that provokes a defensible rejection is a check that reports a false
    ///     failure.
    /// </summary>
    public static SymbolLocation CreateLocation(FieldDescriptor kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var location = new SymbolLocation();
        var inner = kind.MessageType.Parser.ParseFrom(ReadOnlySpan<byte>.Empty);
        PopulateGeometry(inner, 0);
        kind.Accessor.SetValue(location, inner);
        return location;
    }

    /// <summary>Kebab-case name of any oneof field, used in check ids ("overlay-document").</summary>
    public static string SlugOf(FieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Name.Replace('_', '-');
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

    /// <summary>
    ///     Fills in every GeoPoint reachable from <paramref name="message" />, giving
    ///     repeated geometry three distinct points so a polygon or line is a real one.
    ///     <paramref name="depth" /> bounds the walk: the location messages are shallow,
    ///     and a descriptor cycle here would otherwise hang the whole suite.
    /// </summary>
    private static void PopulateGeometry(IMessage message, int depth)
    {
        if (depth > 3) return;

        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.FieldType != FieldType.Message) continue;

            if (field.IsRepeated)
            {
                if (field.Accessor.GetValue(message) is not IList list) continue;

                for (var i = 0; i < 3; i++)
                {
                    var element = field.MessageType.Parser.ParseFrom(ReadOnlySpan<byte>.Empty);
                    if (element is GeoPoint point) Fill(point, i);
                    else PopulateGeometry(element, depth + 1);

                    list.Add(element);
                }

                continue;
            }

            if (field.MessageType.ClrType != typeof(GeoPoint)) continue;

            var geoPoint = new GeoPoint();
            Fill(geoPoint, depth);
            field.Accessor.SetValue(message, geoPoint);
        }
    }

    private static void Fill(GeoPoint point, int index)
    {
        // Somewhere plausible and, importantly, distinct per index - a "polygon" whose
        // three points coincide is not a polygon.
        point.LatitudeCoordinate = 53.0 + index * 0.01;
        point.LongitudeCoordinate = 8.8 + index * 0.01;
    }

    /// <summary>
    ///     A UUID derived from the whole of <paramref name="value" />.
    ///     It has to be the whole of it: a run's identities are seeded with its run id
    ///     precisely so two runs against the same endpoint can't collide, and folding
    ///     only the first sixteen bytes in threw the run id away - every run in the
    ///     same year produced the same UUID, so the second run against a long-lived
    ///     endpoint was really re-using the first run's already-deleted identity.
    /// </summary>
    private static Guid Deterministic(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
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
