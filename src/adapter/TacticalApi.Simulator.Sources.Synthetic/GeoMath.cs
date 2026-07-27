namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Great-circle geometry shared by every synthetic scenario source: destination-point
///     projection, haversine distance, and point-in-polygon containment (e.g. "did this mortar
///     round land inside the perimeter?"). Spherical-earth approximation - plenty accurate at the
///     scale (tens of km) these scenarios operate at.
/// </summary>
internal static class GeoMath
{
    private const double EarthRadiusM = 6371000.0;

    /// <summary>Great-circle destination point, given a start point, bearing and distance.</summary>
    public static (double Lat, double Lon) Destination(double lat, double lon, double bearingDeg, double distanceM)
    {
        var angularDistance = distanceM / EarthRadiusM;
        var bearing = bearingDeg * Math.PI / 180.0;
        var lat1 = lat * Math.PI / 180.0;
        var lon1 = lon * Math.PI / 180.0;

        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angularDistance) +
                              Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearing));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(lat1),
            Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2));

        return (lat2 * 180.0 / Math.PI, lon2 * 180.0 / Math.PI);
    }

    /// <summary>Haversine distance in meters between two points.</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var dPhi = (lat2 - lat1) * Math.PI / 180.0;
        var dLambda = (lon2 - lon1) * Math.PI / 180.0;
        var h = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>
    ///     Ray-casting point-in-polygon test (treating lat/lon as planar - fine at neighborhood
    ///     scale). Used to tell whether an indirect-fire impact landed inside a defended perimeter.
    /// </summary>
    public static bool Contains(IReadOnlyList<(double Lat, double Lon)> polygon, double lat, double lon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (latI, lonI) = polygon[i];
            var (latJ, lonJ) = polygon[j];
            var intersects = latI > lat != latJ > lat &&
                              lon < (lonJ - lonI) * (lat - latI) / (latJ - latI) + lonI;
            if (intersects) inside = !inside;
        }

        return inside;
    }
}
