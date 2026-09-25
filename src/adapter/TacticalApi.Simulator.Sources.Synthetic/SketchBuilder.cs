using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Builds the SketchDocument shapes the offline sources draw with - lines, areas and ellipses,
///     each individually styled. Sketches are the one place in the contract where free geometry can
///     carry its own color and line style, which is what makes them the natural carrier for control
///     measures (phase lines, boundaries, zones) as well as for a plain drawn shape.
///     Shared by <see cref="GeometryShowcaseSource" /> and <see cref="EasternFlankSource" /> so the
///     nesting (document → sketch location → element → geometry) is written once.
/// </summary>
internal static class SketchBuilder
{
    /// <summary>Wraps one or more elements into a SketchDocument update with a stable identity.</summary>
    public static UpdateSituationObject Document(string id, string name, string description, Identity reporter,
        Timestamp nowTs, params SketchLocationElement[] elements)
    {
        var sketch = new SketchLocation { LocationTime = nowTs, Name = name };
        foreach (var element in elements) sketch.Elements.Add(element);

        return new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = name },
                AdditionalInformation = new UpdatePropertyString { Content = description },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { SketchLocation = sketch } }
            }
        };
    }

    /// <summary>One styled element of a sketch.</summary>
    public static SketchLocationElement Element(SymbolLocation location, (int R, int G, int B) color, uint width,
        LineStyle style)
    {
        return new SketchLocationElement
        {
            Location = location,
            LineColor = new Color { Red = color.R, Green = color.G, Blue = color.B, Alpha = 255 },
            LineWidth = width,
            LineStyle = style
        };
    }

    /// <summary>A polyline through the given points.</summary>
    public static SymbolLocation Line(Timestamp nowTs, string name, params (double Lat, double Lon)[] points)
    {
        var line = new Line { LocationTime = nowTs, Name = name };
        foreach (var point in points) line.Points.Add(Geo(point));
        return new SymbolLocation { Line = line };
    }

    /// <summary>A closed area through the given corners.</summary>
    public static SymbolLocation Polygon(Timestamp nowTs, string name, params (double Lat, double Lon)[] points)
    {
        var polygon = new Polygon { LocationTime = nowTs, Name = name };
        foreach (var point in points) polygon.Points.Add(Geo(point));
        return new SymbolLocation { Polygon = polygon };
    }

    /// <summary>
    ///     An ellipse from center, semi-axes and rotation: the conjugate diameter points are the
    ///     endpoints of the two axes, the first one <paramref name="majorM" /> away along
    ///     <paramref name="rotationDeg" />, the second 90 degrees further round. Equal axes give a circle.
    /// </summary>
    public static SymbolLocation Ellipse(Timestamp nowTs, string name, (double Lat, double Lon) center, double majorM,
        double minorM, double rotationDeg)
    {
        return new SymbolLocation
        {
            Ellipse = new Ellipse
            {
                LocationTime = nowTs,
                Name = name,
                CenterPoint = Geo(center),
                FirstConjugateDiameterPoint = Geo(GeoMath.Destination(center.Lat, center.Lon, rotationDeg, majorM)),
                SecondConjugateDiameterPoint =
                    Geo(GeoMath.Destination(center.Lat, center.Lon, rotationDeg + 90, minorM))
            }
        };
    }

    /// <summary>A GeoPoint from a lat/lon pair.</summary>
    public static GeoPoint Geo((double Lat, double Lon) point)
    {
        return new GeoPoint { LatitudeCoordinate = point.Lat, LongitudeCoordinate = point.Lon };
    }
}
