namespace TacticalApi.Simulator.Host.Web;

/// <summary>A geographic coordinate, simplified for map display (no altitude/reference frame).</summary>
public sealed record MapPoint(double Lat, double Lon);

/// <summary>
///     Simplified geometry for a situation object's location. <see cref="Kind" /> tells the
///     frontend how to interpret <see cref="Points" />: a single-point kind ("point", "ellipse",
///     "fan") is drawn as a marker; a multi-point kind ("line", "corridor", "polygon", "route",
///     "sketch", "multipoint") is drawn as a shape connecting the points in order.
/// </summary>
public sealed record MapLocation(string Kind, IReadOnlyList<MapPoint> Points, double? Course, double? Speed);

/// <summary>
///     A MIL-STD-2525/APP-6 symbol identifier (SIDC), present only when the underlying object
///     carries a string-form identifier the frontend's symbol renderer can draw.
/// </summary>
public sealed record MapSymbolIdentifier(string Sidc, string Catalog);

/// <summary>A situation object flattened to just what the read-only map GUI needs to render it.</summary>
public sealed record MapObject(
    string Id,
    string Type,
    string? Name,
    string? AdditionalInformation,
    MapLocation? Location,
    IReadOnlyDictionary<string, string> Details,
    MapSymbolIdentifier? SymbolIdentifier);
