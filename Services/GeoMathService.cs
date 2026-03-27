namespace pacman.Services;

public static class GeoMath
{
    private const double EarthRadius = 6371000; // meters

    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static double BearingTo(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRad(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRad(lat2));
        var x = Math.Cos(ToRad(lat1)) * Math.Sin(ToRad(lat2)) -
                Math.Sin(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Cos(dLon);
        return (ToDeg(Math.Atan2(y, x)) + 360) % 360;
    }

    public static (double lat, double lng) DestinationPoint(double lat, double lng, double bearingDeg, double distanceMeters)
    {
        var d = distanceMeters / EarthRadius;
        var brng = ToRad(bearingDeg);
        var lat1 = ToRad(lat);
        var lon1 = ToRad(lng);

        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) +
                             Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brng));
        var lon2 = lon1 + Math.Atan2(Math.Sin(brng) * Math.Sin(d) * Math.Cos(lat1),
                                      Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));

        return (ToDeg(lat2), ToDeg(lon2));
    }

    /// <summary>
    /// Interpolate points along a polyline at a fixed interval.
    /// Returns list of (lat, lng) points spaced intervalMeters apart.
    /// </summary>
    public static List<double[]> InterpolateAlong(List<double[]> lineCoords, double intervalMeters)
    {
        var points = new List<double[]>();
        if (lineCoords.Count < 2) return points;

        double accumulated = 0;
        points.Add(lineCoords[0]); // always include start

        for (int i = 0; i < lineCoords.Count - 1; i++)
        {
            var segStart = lineCoords[i];
            var segEnd = lineCoords[i + 1];
            var segLen = HaversineDistance(segStart[0], segStart[1], segEnd[0], segEnd[1]);
            var bearing = BearingTo(segStart[0], segStart[1], segEnd[0], segEnd[1]);

            double segProgress = 0;

            // How much distance is left to the next interval point?
            var remaining = intervalMeters - accumulated;

            while (segProgress + remaining <= segLen)
            {
                segProgress += remaining;
                var (lat, lng) = DestinationPoint(segStart[0], segStart[1], bearing, segProgress);
                points.Add([lat, lng]);
                accumulated = 0;
                remaining = intervalMeters;
            }

            accumulated += (segLen - segProgress);
        }

        return points;
    }

    /// <summary>
    /// Compute bounding box for Overpass API query.
    /// Returns (south, west, north, east).
    /// </summary>
    public static (double south, double west, double north, double east) BoundingBox(double centerLat, double centerLng, double radiusMeters)
    {
        var (north, _) = DestinationPoint(centerLat, centerLng, 0, radiusMeters);
        var (south, _) = DestinationPoint(centerLat, centerLng, 180, radiusMeters);
        var (_, east) = DestinationPoint(centerLat, centerLng, 90, radiusMeters);
        var (_, west) = DestinationPoint(centerLat, centerLng, 270, radiusMeters);
        return (south, west, north, east);
    }

    /// <summary>
    /// Calculate total length of a polyline in meters.
    /// </summary>
    public static double LineLength(List<double[]> coords)
    {
        double total = 0;
        for (int i = 0; i < coords.Count - 1; i++)
            total += HaversineDistance(coords[i][0], coords[i][1], coords[i + 1][0], coords[i + 1][1]);
        return total;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
    private static double ToDeg(double rad) => rad * 180 / Math.PI;
}
