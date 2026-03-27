using System.Text;
using System.Text.Json;

namespace pacman.Services;

public class OverpassService(IHttpClientFactory httpClientFactory, ILogger<OverpassService> logger)
{
    private const string OverpassUrl = "https://overpass-api.de/api/interpreter";

    /// <summary>
    /// Fetch all roads, paths, sidewalks within a bounding box from OpenStreetMap.
    /// Returns list of road geometries as coordinate arrays.
    /// </summary>
    public async Task<List<List<double[]>>> GetRoadsAsync(double centerLat, double centerLng, double radiusMeters)
    {
        var (south, west, north, east) = GeoMath.BoundingBox(centerLat, centerLng, radiusMeters);

        var query = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[out:json][timeout:30];way[\"highway\"~\"residential|footway|path|cycleway|pedestrian|living_street|service|unclassified|tertiary|secondary|primary|steps|track\"]({south:F6},{west:F6},{north:F6},{east:F6});out geom;");

        logger.LogInformation("Overpass query:\n{Query}", query);

        var client = httpClientFactory.CreateClient("overpass");
        var body = "data=" + Uri.EscapeDataString(query);
        var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await client.PostAsync(OverpassUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError("Overpass API error {Status}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Overpass API error: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var roads = new List<List<double[]>>();

        foreach (var element in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (element.GetProperty("type").GetString() != "way") continue;
            if (!element.TryGetProperty("geometry", out var geometry)) continue;

            var coords = new List<double[]>();
            foreach (var node in geometry.EnumerateArray())
            {
                var lat = node.GetProperty("lat").GetDouble();
                var lon = node.GetProperty("lon").GetDouble();
                coords.Add([lat, lon]);
            }

            if (coords.Count >= 2)
                roads.Add(coords);
        }

        logger.LogInformation("Overpass returned {Count} road segments", roads.Count);
        return roads;
    }
}
