using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline source whose only job is to put one object of every location kind a mapping client
///     has to render on the map at once - a line, a rectangle (Polygon), a circle and a rotated
///     ellipse (both Ellipse), a sketch holding all three at the same time, a Symbol whose location
///     is a Line rather than a Point, and a plain point Symbol as a reference marker.
///     The scenario sources also produce these shapes, but buried in a moving picture and partly
///     behind random incidents, which makes "does my client draw an ellipse correctly" awkward to
///     answer. Here every shape is at a fixed offset around the configured center, derived only
///     from the options - no RNG, no motion - so two runs produce the same picture and a client's
///     rendering can be compared against it directly.
/// </summary>
public sealed class GeometryShowcaseSource(
    IOptionsMonitor<GeometryShowcaseOptions> options,
    TimeProvider timeProvider,
    ILogger<GeometryShowcaseSource> logger)
    : ISimulationSource
{
    private static readonly (int R, int G, int B) Blue = (0, 128, 255);
    private static readonly (int R, int G, int B) Red = (220, 50, 50);
    private static readonly (int R, int G, int B) Green = (40, 180, 90);
    private static readonly (int R, int G, int B) Magenta = (200, 0, 200);

    /// <inheritdoc />
    public string Name => GeometryShowcaseOptions.Name;

    /// <inheritdoc />
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc />
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <summary>
    ///     Emits the same seven objects every cycle, each with a stable identity, so repeated
    ///     cycles update the existing objects instead of adding new ones.
    /// </summary>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var nowTs = Timestamp.FromDateTimeOffset(timeProvider.GetUtcNow());
        var reporter = new Identity { StringIdentity = settings.ReporterId };
        var size = settings.ShapeSizeM;
        var half = size / 2;

        // Each shape gets its own slot on a ring around the center, so nothing overlaps.
        var lineSlot = Slot(settings, 0);
        var rectangleSlot = Slot(settings, 60);
        var circleSlot = Slot(settings, 120);
        var ellipseSlot = Slot(settings, 180);
        var multiSlot = Slot(settings, 240);
        var symbolSlot = Slot(settings, 300);

        var updates = new List<UpdateSituationObject>
        {
            SketchBuilder.Document("showcase:line", "Showcase line", "Line location with three points", reporter,
                nowTs,
                SketchBuilder.Element(
                    SketchBuilder.Line(nowTs, "Showcase line",
                        GeoMath.Destination(lineSlot.Lat, lineSlot.Lon, 270, half),
                        GeoMath.Destination(lineSlot.Lat, lineSlot.Lon, 0, half),
                        GeoMath.Destination(lineSlot.Lat, lineSlot.Lon, 90, half)),
                    Blue, 3, LineStyle.Solid)),

            SketchBuilder.Document("showcase:rectangle", "Showcase rectangle",
                "Polygon location with four corners", reporter, nowTs,
                SketchBuilder.Element(SketchBuilder.Polygon(nowTs, "Showcase rectangle", Corners(rectangleSlot, half)),
                    Red, 2, LineStyle.Dash)),

            SketchBuilder.Document("showcase:circle", "Showcase circle", "Ellipse location with equal axes",
                reporter, nowTs,
                SketchBuilder.Element(SketchBuilder.Ellipse(nowTs, "Showcase circle", circleSlot, half, half, 0),
                    Green, 2, LineStyle.Solid)),

            SketchBuilder.Document("showcase:ellipse", "Showcase ellipse",
                "Ellipse location, major axis rotated 45 degrees", reporter, nowTs,
                SketchBuilder.Element(
                    SketchBuilder.Ellipse(nowTs, "Showcase ellipse", ellipseSlot, half, size / 5, 45),
                    Magenta, 2, LineStyle.Dot)),

            // A client that handles the single-element sketches above can still get this one wrong
            // by only reading the first element.
            SketchBuilder.Document("showcase:multi", "Showcase multi-element sketch",
                "One sketch with a line, a polygon and an ellipse element", reporter, nowTs,
                SketchBuilder.Element(
                    SketchBuilder.Line(nowTs, "Multi: line",
                        GeoMath.Destination(multiSlot.Lat, multiSlot.Lon, 270, half),
                        GeoMath.Destination(multiSlot.Lat, multiSlot.Lon, 90, half)),
                    Blue, 3, LineStyle.Solid),
                SketchBuilder.Element(
                    SketchBuilder.Polygon(nowTs, "Multi: polygon",
                        Corners(GeoMath.Destination(multiSlot.Lat, multiSlot.Lon, 0, size / 8), size / 4)),
                    Red, 2, LineStyle.Dash),
                SketchBuilder.Element(
                    SketchBuilder.Ellipse(nowTs, "Multi: ellipse",
                        GeoMath.Destination(multiSlot.Lat, multiSlot.Lon, 180, size / 4), size / 4, size / 8, 90),
                    Green, 2, LineStyle.Dot)),

            SymbolOnLine(reporter, nowTs, symbolSlot, half),
            CenterSymbol(reporter, nowTs, settings)
        };

        logger.ShowcaseShapesProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    /// <summary>
    ///     A Symbol whose location is a Line, not a Point - legal per the contract and a case
    ///     clients routinely miss, since symbols are assumed to be point objects.
    /// </summary>
    private static UpdateSituationObject SymbolOnLine(Identity reporter, Timestamp nowTs, (double Lat, double Lon) slot,
        double halfM)
    {
        var line = new Line { LocationTime = nowTs, Name = "Showcase symbol line" };
        line.Points.Add(SketchBuilder.Geo(GeoMath.Destination(slot.Lat, slot.Lon, 225, halfM)));
        line.Points.Add(SketchBuilder.Geo(GeoMath.Destination(slot.Lat, slot.Lon, 45, halfM)));

        return new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "showcase:symbol:line" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Showcase symbol on a line" },
                AdditionalInformation = new UpdatePropertyString { Content = "Symbol whose location is a Line" },
                SymbolIdentifier = new UpdatePropertySymbolIdentifier
                {
                    Content = new SymbolIdentifier
                    {
                        StringIdentifier = "GFGPGLB--------",
                        SymbolCatalog = SymbolCatalog.Mil2525C
                    }
                },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Line = line } }
            }
        };
    }

    /// <summary>A plain point symbol in the middle, as a reference the other shapes are laid out around.</summary>
    private static UpdateSituationObject CenterSymbol(Identity reporter, Timestamp nowTs,
        GeometryShowcaseOptions settings)
    {
        return new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "showcase:symbol:center" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Showcase center" },
                AdditionalInformation = new UpdatePropertyString
                {
                    Content = "Reference marker; the showcase shapes are laid out around this point"
                },
                SymbolIdentifier = new UpdatePropertySymbolIdentifier
                {
                    Content = new SymbolIdentifier
                    {
                        StringIdentifier = "SFGPU----------",
                        SymbolCatalog = SymbolCatalog.Mil2525C
                    }
                },
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point
                        {
                            LocationTime = nowTs,
                            GeoPoint = SketchBuilder.Geo((settings.CenterLatitude, settings.CenterLongitude))
                        }
                    }
                }
            }
        };
    }

    /// <summary>The four corners of an axis-aligned square, clockwise from the northwest.</summary>
    private static (double Lat, double Lon)[] Corners((double Lat, double Lon) center, double halfEdgeM)
    {
        var north = GeoMath.Destination(center.Lat, center.Lon, 0, halfEdgeM);
        var south = GeoMath.Destination(center.Lat, center.Lon, 180, halfEdgeM);
        return
        [
            GeoMath.Destination(north.Lat, north.Lon, 270, halfEdgeM),
            GeoMath.Destination(north.Lat, north.Lon, 90, halfEdgeM),
            GeoMath.Destination(south.Lat, south.Lon, 90, halfEdgeM),
            GeoMath.Destination(south.Lat, south.Lon, 270, halfEdgeM)
        ];
    }

    /// <summary>The position of one shape's slot on the ring around the configured center.</summary>
    private static (double Lat, double Lon) Slot(GeometryShowcaseOptions settings, double bearingDeg)
    {
        return GeoMath.Destination(settings.CenterLatitude, settings.CenterLongitude, bearingDeg, settings.SpacingM);
    }
}
