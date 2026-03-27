using pacman.Models;

namespace pacman.Services;

public class DotGeneratorService(OverpassService overpassService, ILogger<DotGeneratorService> logger)
{
    private static readonly string[] PowerUpTypes = ["multiplier", "magnet", "ghost", "steal"];
    private static readonly Random Rng = new();

    /// <summary>
    /// Generate dots along all roads within a radius, spaced 15m apart.
    /// Also generates power-up pellets at ~2-3% of dot locations.
    /// Returns (dots, powerUps, roadSegments).
    /// </summary>
    public async Task<(List<Dot> dots, List<PowerUp> powerUps, List<RoadSegment> roads)> GenerateAsync(
        double centerLat, double centerLng, double radiusMeters)
    {
        var roadGeometries = await overpassService.GetRoadsAsync(centerLat, centerLng, radiusMeters);

        // Interpolate points along all roads
        var allPoints = new List<double[]>();
        var roadSegments = new List<RoadSegment>();

        foreach (var roadCoords in roadGeometries)
        {
            var points = GeoMath.InterpolateAlong(roadCoords, 15.0);
            allPoints.AddRange(points);

            roadSegments.Add(new RoadSegment
            {
                Coordinates = roadCoords,
                Length = GeoMath.LineLength(roadCoords)
            });
        }

        logger.LogInformation("Interpolated {Count} raw points from {Roads} roads", allPoints.Count, roadGeometries.Count);

        // Filter: only points within radius
        allPoints = allPoints
            .Where(p => GeoMath.HaversineDistance(centerLat, centerLng, p[0], p[1]) <= radiusMeters)
            .ToList();

        // De-duplicate points within 5m of each other
        var deduplicated = DeduplicatePoints(allPoints, 5.0);

        logger.LogInformation("After filtering and dedup: {Count} dots", deduplicated.Count);

        // Create dot objects
        var dots = new List<Dot>();
        var powerUps = new List<PowerUp>();

        for (int i = 0; i < deduplicated.Count; i++)
        {
            var point = deduplicated[i];

            // ~2.5% chance of being a power pellet
            if (Rng.NextDouble() < 0.025)
            {
                powerUps.Add(new PowerUp
                {
                    Id = $"pu-{i:D4}",
                    Lat = point[0],
                    Lng = point[1],
                    Type = PowerUpTypes[Rng.Next(PowerUpTypes.Length)]
                });
            }
            else
            {
                dots.Add(new Dot
                {
                    Id = $"d-{i:D5}",
                    Lat = point[0],
                    Lng = point[1]
                });
            }
        }

        // Build road connectivity for ghost pathfinding
        BuildRoadConnectivity(roadSegments);

        logger.LogInformation("Generated {Dots} dots and {PowerUps} power-ups", dots.Count, powerUps.Count);
        return (dots, powerUps, roadSegments);
    }

    private static List<double[]> DeduplicatePoints(List<double[]> points, double minDistanceMeters)
    {
        var result = new List<double[]>();

        foreach (var point in points)
        {
            bool tooClose = false;
            // Check against last few added points for efficiency (nearby points are usually sequential)
            for (int j = Math.Max(0, result.Count - 20); j < result.Count; j++)
            {
                if (GeoMath.HaversineDistance(point[0], point[1], result[j][0], result[j][1]) < minDistanceMeters)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose)
                result.Add(point);
        }

        return result;
    }

    private static void BuildRoadConnectivity(List<RoadSegment> segments)
    {
        // Connect road segments that share endpoints (within 10m)
        for (int i = 0; i < segments.Count; i++)
        {
            var endA = segments[i].Coordinates[0];
            var endB = segments[i].Coordinates[^1];

            for (int j = i + 1; j < segments.Count; j++)
            {
                var otherEndA = segments[j].Coordinates[0];
                var otherEndB = segments[j].Coordinates[^1];

                if (GeoMath.HaversineDistance(endA[0], endA[1], otherEndA[0], otherEndA[1]) < 10 ||
                    GeoMath.HaversineDistance(endA[0], endA[1], otherEndB[0], otherEndB[1]) < 10 ||
                    GeoMath.HaversineDistance(endB[0], endB[1], otherEndA[0], otherEndA[1]) < 10 ||
                    GeoMath.HaversineDistance(endB[0], endB[1], otherEndB[0], otherEndB[1]) < 10)
                {
                    segments[i].ConnectedSegments.Add(j);
                    segments[j].ConnectedSegments.Add(i);
                }
            }
        }
    }
}
