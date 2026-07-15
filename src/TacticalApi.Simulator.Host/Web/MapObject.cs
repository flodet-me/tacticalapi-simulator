namespace TacticalApi.Simulator.Host.Web;

/// <summary>
///     A geographic coordinate, simplified for map display (no altitude/reference frame).
///     <see cref="Name" />/<see cref="Comment" />/<see cref="ArrivalTime" />/<see cref="TravelTimeSeconds" />
///     are only ever populated for a "route" kind's waypoints.
/// </summary>
public sealed record MapPoint(
    double Lat,
    double Lon,
    string? Name = null,
    string? Comment = null,
    string? ArrivalTime = null,
    double? TravelTimeSeconds = null);

/// <summary>
///     Simplified geometry for a situation object's location. <see cref="Kind" /> tells the
///     frontend how to interpret <see cref="Points" />: a single-point kind ("point", "ellipse",
///     "fan") is drawn as a marker/shape anchored on <c>Points[0]</c>; a multi-point kind
///     ("line", "corridor", "polygon", "route", "multipoint") is drawn as a shape connecting the
///     points in order; "sketch" is drawn from <see cref="Elements" /> instead of "Points".
///     The kind-specific fields below are only populated for the matching <see cref="Kind" />:
///     "fan" uses <see cref="OrientationDeg" />/<see cref="SectorDeg" />/<see cref="MinRangeM" />/
///     <see cref="MaxRangeM" />; "ellipse" uses <see cref="OrientationDeg" />/
///     <see cref="SemiMajorM" />/<see cref="SemiMinorM" />; "corridor" uses <see cref="WidthM" />.
/// </summary>
public sealed record MapLocation(
    string Kind,
    IReadOnlyList<MapPoint> Points,
    double? Course = null,
    double? Speed = null,
    double? OrientationDeg = null,
    double? SectorDeg = null,
    double? MinRangeM = null,
    double? MaxRangeM = null,
    double? SemiMajorM = null,
    double? SemiMinorM = null,
    double? WidthM = null,
    IReadOnlyList<MapSketchElement>? Elements = null);

/// <summary>One drawing primitive of a multi-element sketch, with its own style.</summary>
public sealed record MapSketchElement(
    string Kind,
    IReadOnlyList<MapPoint> Points,
    string? Color,
    string? LineStyle,
    string? FillStyle);

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
