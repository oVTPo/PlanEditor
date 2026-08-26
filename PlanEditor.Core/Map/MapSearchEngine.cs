using System.Globalization;
using System.Text;
using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public sealed class MapSearchEngine
{
    private readonly MapDocument _map;

    public MapSearchEngine(MapDocument map)
    {
        _map = map;
    }

    public List<MapSearchResult> Search(
        string query,
        int maxResults = 20)
    {
        string normalizedQuery = Normalize(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return new List<MapSearchResult>();

        var results =
            new List<(int Score, MapSearchResult Result)>();

        foreach (MapFeature feature in _map.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Name))
                continue;

            string normalizedName =
                Normalize(feature.Name);

            int score =
                CalculateScore(
                    normalizedName,
                    normalizedQuery
                );

            if (score <= 0)
                continue;

            WorldPoint center =
                GetFeatureCenter(feature);

            results.Add(
                (
                    score,
                    new MapSearchResult
                    {
                        Name = feature.Name!,
                        Subtitle = GetSubtitle(feature),
                        Feature = feature,
                        Position = center
                    }
                )
            );
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Result.Name)
            .Take(maxResults)
            .Select(x => x.Result)
            .ToList();
    }

    private static int CalculateScore(
        string name,
        string query)
    {
        if (name == query)
            return 1000;

        if (name.StartsWith(query))
            return 800;

        if (name.Contains(query))
            return 500;

        return 0;
    }

    private static string GetSubtitle(
        MapFeature feature)
    {
        if (feature.Type == MapFeatureType.Road)
        {
            if (feature.Properties.TryGetValue(
                    "highway",
                    out string? highway))
            {
                return $"Đường • {highway}";
            }

            return "Đường";
        }

        if (feature.Type == MapFeatureType.Boundary)
            return "Khu vực hành chính";

        if (feature.Type == MapFeatureType.Building)
            return "Công trình";

        return feature.Type.ToString();
    }

    private static WorldPoint GetFeatureCenter(
        MapFeature feature)
    {
        double x =
            (feature.Bounds.MinX +
             feature.Bounds.MaxX) / 2.0;

        double y =
            (feature.Bounds.MinY +
             feature.Bounds.MaxY) / 2.0;

        return new WorldPoint(x, y);
    }

    private static string Normalize(
        string value)
    {
        string normalized =
            value
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD
                );

        var builder =
            new StringBuilder();

        foreach (char character in normalized)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(
                    character
                );

            if (category !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        string result =
            builder
                .ToString()
                .Normalize(
                    NormalizationForm.FormC
                );

        result =
            result.Replace('đ', 'd');

        return result;
    }
}