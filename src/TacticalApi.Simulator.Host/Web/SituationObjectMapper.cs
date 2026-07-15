using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Identities;

namespace TacticalApi.Simulator.Host.Web;

/// <summary>
///     Flattens store snapshots into <see cref="MapObject" />s for the read-only map GUI
///     (<c>/api/objects</c>). Display-only: geometry is simplified (e.g. an ellipse becomes its
///     center point) rather than reproducing exact TacticalAPI rendering, and only string-form
///     symbol identifiers (2525B/C, APP-6B/D SIDCs) are surfaced for icon rendering - numeric
///     identifiers (2525D/E) are left for the frontend to fall back to a plain marker.
/// </summary>
public static class SituationObjectMapper
{
    /// <summary>
    ///     Maps every object in <paramref name="objects" /> to a <see cref="MapObject" />. Overlay
    ///     documents are also expanded so the situation objects they carry appear on the map.
    /// </summary>
    public static IReadOnlyList<MapObject> Map(IReadOnlyList<SituationObject> objects)
    {
        var result = new List<MapObject>(objects.Count);
        foreach (var obj in objects) MapInto(obj, result);
        return result;
    }

    private static void MapInto(SituationObject obj, List<MapObject> result)
    {
        var mapped = obj.TypeCase switch
        {
            SituationObject.TypeOneofCase.Symbol => MapSymbol(obj.Symbol),
            SituationObject.TypeOneofCase.ActionTask => MapActionTask(obj.ActionTask),
            SituationObject.TypeOneofCase.ActionEvent => MapActionEvent(obj.ActionEvent),
            SituationObject.TypeOneofCase.OrganizationUnit => MapOrganizationUnit(obj.OrganizationUnit),
            SituationObject.TypeOneofCase.Route => MapRoute(obj.Route),
            SituationObject.TypeOneofCase.TextDocument => MapTextDocument(obj.TextDocument),
            SituationObject.TypeOneofCase.PictureDocument => MapPictureDocument(obj.PictureDocument),
            SituationObject.TypeOneofCase.VoiceMessageDocument => MapVoiceMessageDocument(obj.VoiceMessageDocument),
            SituationObject.TypeOneofCase.NatoMessageDocument => MapNatoMessageDocument(obj.NatoMessageDocument),
            SituationObject.TypeOneofCase.OverlayDocument => MapOverlayDocument(obj.OverlayDocument),
            SituationObject.TypeOneofCase.SketchDocument => MapSketchDocument(obj.SketchDocument),
            _ => null
        };

        if (mapped is not null) result.Add(mapped);

        // Overlay documents carry their own nested situation objects - surface those too, since
        // they're real objects in the world (e.g. the synthetic scenario's phase-line symbols).
        if (obj.TypeCase == SituationObject.TypeOneofCase.OverlayDocument)
            foreach (var nested in obj.OverlayDocument.OverlayData?.Contents ?? [])
                MapInto(nested, result);
    }

    private static MapObject? MapSymbol(Symbol s)
    {
        var details = BuildDetails(s.CreationMetaData, s.ExpiryTime, s.AdditionalAttributes, s.ForeignKeys,
            ("Higher formation", s.HigherFormation?.Content),
            ("Reinforcement", EnumText(s.Reinforcement?.Content)),
            ("Equipment", s.EquipmentType?.Content),
            ("Quantity", s.Quantity?.Content?.ToString()),
            ("Staff comment", s.StaffComment?.Content),
            ("Dimension", DimensionText(s.Dimension)));

        return Create("Symbol", s.Identity, s.Name, s.AdditionalInformation, s.Location, details,
            SymbolIdentifierText(s.SymbolIdentifier));
    }

    private static MapObject? MapActionTask(ActionTask t)
    {
        var details = BuildDetails(t.CreationMetaData, t.ExpiryTime, t.AdditionalAttributes, t.ForeignKeys,
            ("Task type", EnumText(t.ActionTaskType?.Content)),
            ("Status", EnumText(t.ActionTaskStatus?.Content)),
            ("Priority", EnumText(t.ActionTaskPriority?.Content)),
            ("Completion", t.CompletionRatio?.Content is int ratio ? $"{ratio}%" : null),
            ("Planned start", TimestampText(t.PlannedStartTime)),
            ("Planned end", TimestampText(t.PlannedEndTime)));

        return Create("ActionTask", t.Identity, t.Name, t.AdditionalInformation, t.Location, details, null);
    }

    private static MapObject? MapActionEvent(ActionEvent e)
    {
        var details = BuildDetails(e.CreationMetaData, e.ExpiryTime, e.AdditionalAttributes, e.ForeignKeys,
            ("Event type", EnumText(e.ActionEventType?.Content)),
            ("Threat level", e.ThreatLevel?.Content?.ToString()),
            ("Detection", e.DetectionDescription?.Content),
            ("Dimension", DimensionText(e.Dimension)));

        return Create("ActionEvent", e.Identity, e.Name, e.AdditionalInformation, e.Location, details, null);
    }

    private static MapObject? MapOrganizationUnit(OrganizationUnit u)
    {
        var details = BuildDetails(u.CreationMetaData, u.ExpiryTime, u.AdditionalAttributes, u.ForeignKeys,
            ("Higher formation", u.HigherFormation?.Content),
            ("Unit designation", EnumText(u.UnitDesignation?.Content)),
            ("Color", ColorText(u.OrganizationUnitColor?.Content)));

        return Create("OrganizationUnit", u.Identity, u.Name, u.AdditionalInformation, null, details,
            SymbolIdentifierText(u.SymbolIdentifier));
    }

    private static MapObject? MapRoute(Rheinmetall.TacticalApi.V0.Route r)
    {
        var details = BuildDetails(r.CreationMetaData, r.ExpiryTime, r.AdditionalAttributes, r.ForeignKeys,
            ("Route type", EnumText(r.RouteType?.Content)),
            ("March speed", r.MarchSpeed?.Content is int speed ? $"{speed} m/s" : null),
            ("Line color", ColorText(r.LineColor?.Content)),
            ("Line width", r.LineWidth?.Content?.ToString()));

        return Create("Route", r.Identity, r.Name, r.AdditionalInformation, r.Location, details, null);
    }

    private static MapObject? MapTextDocument(TextDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)),
            ("Content", d.PlainContent?.Content));

        return Create("TextDocument", d.Identity, d.Name, d.AdditionalInformation, d.Location, details, null);
    }

    private static MapObject? MapPictureDocument(PictureDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)),
            ("Direction of view", d.DirectionOfView?.Content is int dir ? $"{dir}°" : null),
            ("Focal length", d.FocalLength?.Content is int focal ? $"{focal} mm" : null));

        return Create("PictureDocument", d.Identity, d.Name, d.AdditionalInformation, d.Location, details, null);
    }

    private static MapObject? MapVoiceMessageDocument(VoiceMessageDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)));

        return Create("VoiceMessageDocument", d.Identity, d.Name, d.AdditionalInformation, d.Location, details,
            null);
    }

    private static MapObject? MapNatoMessageDocument(NatoMessageDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)),
            ("MTF message", d.MtfMessageData?.Content));

        return Create("NatoMessageDocument", d.Identity, d.Name, d.AdditionalInformation, d.Location, details, null);
    }

    private static MapObject? MapOverlayDocument(OverlayDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Tag", d.Tag?.Content),
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)),
            ("Contains", d.OverlayData?.Contents.Count is int count && count > 0 ? $"{count} object(s)" : null));

        return Create("OverlayDocument", d.Identity, d.Name, d.AdditionalInformation, null, details, null);
    }

    private static MapObject? MapSketchDocument(SketchDocument d)
    {
        var details = BuildDetails(d.CreationMetaData, d.ExpiryTime, d.AdditionalAttributes, d.ForeignKeys,
            ("Category", EnumText(d.MessageCategory?.Content)),
            ("Precedence", EnumText(d.MessagePrecedence?.Content)));

        return Create("SketchDocument", d.Identity, d.Name, d.AdditionalInformation, d.Location, details, null);
    }

    private static MapObject? Create(
        string type, Identity? identity, DataPropertyString? name, DataPropertyString? additionalInformation,
        DataPropertyLocation? location, IReadOnlyDictionary<string, string> details,
        MapSymbolIdentifier? symbolIdentifier)
    {
        var id = IdentityKey.TryCreate(identity);
        if (id is null) return null;

        return new MapObject(id, type, name?.Content, additionalInformation?.Content,
            ExtractLocation(location?.Content), details, symbolIdentifier);
    }

    private static Dictionary<string, string> BuildDetails(
        CreationMetaData? creation, DataPropertyTimestamp? expiry,
        IEnumerable<KeyValuePair<string, AdditionalAttributeValue>> additionalAttributes,
        IEnumerable<KeyValuePair<string, DataPropertyIdentity>> foreignKeys,
        params (string Label, string? Value)[] extra)
    {
        var details = new Dictionary<string, string>();

        AddDetail(details, "Creator", IdentityText(creation?.CreatorIdentity));
        AddDetail(details, "Created", TimestampText(creation?.CreationTime));
        AddDetail(details, "Expires", TimestampText(expiry));

        foreach (var (label, value) in extra) AddDetail(details, label, value);

        foreach (var (key, value) in additionalAttributes) AddDetail(details, $"Attr: {key}", AttributeText(value));

        foreach (var (key, value) in foreignKeys)
            AddDetail(details, $"FK: {key}", IdentityText(value?.Content));

        return details;
    }

    private static void AddDetail(Dictionary<string, string> details, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value)) details[label] = value;
    }

    private static string? TimestampText(DataPropertyTimestamp? property)
    {
        return TimestampText(property?.Content);
    }

    private static string? TimestampText(Google.Protobuf.WellKnownTypes.Timestamp? timestamp)
    {
        return timestamp?.ToDateTimeOffset().ToString("O");
    }

    private static string? AttributeText(AdditionalAttributeValue value)
    {
        return value.TypeCase switch
        {
            AdditionalAttributeValue.TypeOneofCase.StringValue => value.StringValue,
            AdditionalAttributeValue.TypeOneofCase.Int32Value => value.Int32Value.ToString(),
            AdditionalAttributeValue.TypeOneofCase.DoubleValue => value.DoubleValue.ToString("G"),
            AdditionalAttributeValue.TypeOneofCase.BoolValue => value.BoolValue.ToString(),
            _ => null
        };
    }

    private static string? IdentityText(Identity? identity)
    {
        return identity?.TypeCase switch
        {
            Identity.TypeOneofCase.UuidIdentity => identity.UuidIdentity,
            Identity.TypeOneofCase.StringIdentity => identity.StringIdentity,
            Identity.TypeOneofCase.Int32Identity => identity.Int32Identity.ToString(),
            Identity.TypeOneofCase.Int64Identity => identity.Int64Identity.ToString(),
            _ => null
        };
    }

    private static string? ColorText(Color? color)
    {
        return color is null ? null : $"rgb({color.Red}, {color.Green}, {color.Blue})";
    }

    private static string? DimensionText(DataPropertyDimension? dimension)
    {
        if (dimension is null) return null;

        var parts = new List<string>(3);
        if (dimension.X is int x) parts.Add($"X={x}");
        if (dimension.Y is int y) parts.Add($"Y={y}");
        if (dimension.Z is int z) parts.Add($"Z={z}");

        return parts.Count == 0 ? null : string.Join(' ', parts) + " m";
    }

    private static string? EnumText<T>(T? value) where T : struct, Enum
    {
        return value is null || EqualityComparer<T>.Default.Equals(value.Value, default) ? null : value.ToString();
    }

    private static MapSymbolIdentifier? SymbolIdentifierText(DataPropertySymbolIdentifier? property)
    {
        var content = property?.Content;
        if (content is null || content.IdentifierCase != SymbolIdentifier.IdentifierOneofCase.StringIdentifier)
            return null;

        return new MapSymbolIdentifier(content.StringIdentifier, content.SymbolCatalog.ToString());
    }

    private static MapLocation? ExtractLocation(SymbolLocation? location)
    {
        if (location is null) return null;

        return location.LocationCase switch
        {
            SymbolLocation.LocationOneofCase.Point => new MapLocation("point",
                [ToPoint(location.Point.GeoPoint)], location.Point.Course, location.Point.Speed),
            SymbolLocation.LocationOneofCase.Ellipse => new MapLocation("ellipse",
                [ToPoint(location.Ellipse.CenterPoint)], null, null),
            SymbolLocation.LocationOneofCase.Fan => new MapLocation("fan",
                [ToPoint(location.Fan.VertexPoint)], null, null),
            SymbolLocation.LocationOneofCase.Multipoint => new MapLocation("multipoint",
                location.Multipoint.Points.Select(ToPoint).ToList(), null, null),
            SymbolLocation.LocationOneofCase.Corridor => new MapLocation("corridor",
                location.Corridor.Points.Select(ToPoint).ToList(), null, null),
            SymbolLocation.LocationOneofCase.Line => new MapLocation("line",
                location.Line.Points.Select(ToPoint).ToList(), null, null),
            SymbolLocation.LocationOneofCase.Polygon => new MapLocation("polygon",
                location.Polygon.Points.Select(ToPoint).ToList(), null, null),
            SymbolLocation.LocationOneofCase.RouteLocation => new MapLocation("route",
                location.RouteLocation.WayPoints.Select(ToPoint).ToList(), null, null),
            SymbolLocation.LocationOneofCase.SketchLocation => new MapLocation("sketch",
                location.SketchLocation.Elements
                    .SelectMany(element => ExtractLocation(element.Location)?.Points ?? [])
                    .ToList(), null, null),
            _ => null
        };
    }

    private static MapPoint ToPoint(GeoPoint point)
    {
        return new MapPoint(point.LatitudeCoordinate, point.LongitudeCoordinate);
    }

    private static MapPoint ToPoint(WayPoint point)
    {
        return new MapPoint(point.LatitudeCoordinate, point.LongitudeCoordinate);
    }
}
