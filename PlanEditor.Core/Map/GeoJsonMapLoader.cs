using System.Text.Json;
using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public static class GeoJsonMapLoader
{
    public static MapDocument Load(string filePath)
    {
        string json = File.ReadAllText(filePath);

        using JsonDocument document =
            JsonDocument.Parse(json);

        var map = new MapDocument
        {
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty(
                "features",
                out JsonElement features))
        {
            return map;
        }

        foreach (JsonElement element in features.EnumerateArray())
        {
            foreach (MapFeature feature in ParseFeature(element))
            {
                map.Features.Add(feature);
            }
        } 
        map.BuildSpatialIndex();
        map.BuildJunctions();
        return map;
    }

    private static IEnumerable<MapFeature> ParseFeature(
        JsonElement element)
    {
        if (!element.TryGetProperty(
                "geometry",
                out JsonElement geometry))
        {
            yield break;
        }

        if (geometry.ValueKind == JsonValueKind.Null)
            yield break;

        if (!geometry.TryGetProperty(
                "type",
                out JsonElement geometryTypeElement))
        {
            yield break;
        }

        string? geometryType =
            geometryTypeElement.GetString();

        var properties =
            ReadProperties(element);

        MapFeatureType featureType =
            DetectFeatureType(properties);

        string? name = null;

        if (properties.TryGetValue("name", out string? value))
            name = value;

        JsonElement coordinates =
            geometry.GetProperty("coordinates");

        switch (geometryType)
        {
            case "LineString":
            {
                var feature = CreateFeature(
                    featureType,
                    MapGeometryType.LineString,
                    name,
                    properties
                );

                ReadCoordinateArray(
                    coordinates,
                    feature.Points
                );

                if (feature.Points.Count >= 2)
                    feature.UpdateBounds();
                    yield return feature;

                break;
            }

            case "MultiLineString":
            {
                foreach (JsonElement line
                         in coordinates.EnumerateArray())
                {
                    var feature = CreateFeature(
                        featureType,
                        MapGeometryType.LineString,
                        name,
                        properties
                    );

                    ReadCoordinateArray(
                        line,
                        feature.Points
                    );

                    if (feature.Points.Count >= 2)
                    {
                        feature.UpdateBounds();
                        yield return feature;
                    }
                }

                break;
            }

            case "Polygon":
            {
                if (coordinates.GetArrayLength() == 0)
                    yield break;

                var feature = CreateFeature(
                    featureType,
                    MapGeometryType.Polygon,
                    name,
                    properties
                );

                ReadCoordinateArray(
                    coordinates[0],
                    feature.Points
                );

                if (feature.Points.Count >= 3)
                {
                    feature.UpdateBounds();
                    yield return feature;
                }

                break;
            }

            case "MultiPolygon":
            {
                foreach (JsonElement polygon
                        in coordinates.EnumerateArray())
                {
                    if (polygon.GetArrayLength() == 0)
                        continue;

                    var feature = CreateFeature(
                        featureType,
                        MapGeometryType.Polygon,
                        name,
                        properties
                    );

                    // polygon[0] = outer ring
                    ReadCoordinateArray(
                        polygon[0],
                        feature.Points
                    );

                    if (feature.Points.Count >= 3)
                    {
                        feature.UpdateBounds();
                        yield return feature;
                    }
                }

                break;
            }
        }
    }

    private static MapFeature CreateFeature(
        MapFeatureType type,
        MapGeometryType geometryType,
        string? name,
        Dictionary<string, string> properties)
    {
        var feature = new MapFeature
        {
            Type = type,
            GeometryType = geometryType,
            Name = name,

            Properties =
                new Dictionary<string, string>(
                    properties
                )
        };

        if (type == MapFeatureType.Road)
        {
            feature.RoadClass =
                DetectRoadClass(properties);

            feature.IsVehicleRoad =
                DetectVehicleRoad(
                    feature.RoadClass
                );

            feature.IsPlanningRoad =
                DetectPlanningRoad(
                    feature.RoadClass
                );

            feature.RoadWidthMeters =
                DetectRoadWidth(
                    feature.RoadClass,
                    properties
                );
        }

        return feature;
    }

    private static RoadClass DetectRoadClass(
    Dictionary<string, string> properties)
    {
        if (!properties.TryGetValue(
                "highway",
                out string? highway))
        {
            return RoadClass.Unknown;
        }

        return highway.ToLowerInvariant() switch
        {
            "motorway" =>
                RoadClass.Motorway,

            "motorway_link" =>
                RoadClass.Motorway,

            "trunk" =>
                RoadClass.Trunk,

            "trunk_link" =>
                RoadClass.Trunk,

            "primary" =>
                RoadClass.Primary,

            "primary_link" =>
                RoadClass.Primary,

            "secondary" =>
                RoadClass.Secondary,

            "secondary_link" =>
                RoadClass.Secondary,

            "tertiary" =>
                RoadClass.Tertiary,

            "tertiary_link" =>
                RoadClass.Tertiary,

            "residential" =>
                RoadClass.Residential,

            "unclassified" =>
                RoadClass.Unclassified,

            "living_street" =>
                RoadClass.LivingStreet,

            "service" =>
                RoadClass.Service,

            "pedestrian" =>
                RoadClass.Pedestrian,

            "footway" =>
                RoadClass.Footway,

            "path" =>
                RoadClass.Path,

            "cycleway" =>
                RoadClass.Cycleway,

            "track" =>
                RoadClass.Track,

            "steps" =>
                RoadClass.Steps,

            _ =>
                RoadClass.Unknown
        };
    }

    private static Dictionary<string, string> ReadProperties(
        JsonElement element)
    {
        var result =
            new Dictionary<string, string>();

        if (!element.TryGetProperty(
                "properties",
                out JsonElement properties))
        {
            return result;
        }

        foreach (JsonProperty property
                 in properties.EnumerateObject())
        {
            result[property.Name] =
                property.Value.ToString();
        }

        return result;
    }

    private static MapFeatureType DetectFeatureType(
    Dictionary<string, string> properties)
    {
        if (properties.ContainsKey("highway"))
            return MapFeatureType.Road;

        if (properties.ContainsKey("building"))
            return MapFeatureType.Building;

        if (properties.ContainsKey("barrier"))
            return MapFeatureType.Barrier;

        // MẶT NƯỚC
        if (properties.TryGetValue(
                "natural",
                out string? natural) &&
            natural == "water")
        {
            return MapFeatureType.Water;
        }

        if (properties.TryGetValue(
                "water",
                out string? water))
        {
            if (water is
                "river" or
                "canal" or
                "lake" or
                "reservoir" or
                "pond" or
                "basin")
            {
                return MapFeatureType.Water;
            }
        }

        if (properties.TryGetValue(
                "waterway",
                out string? waterway))
        {
            if (waterway == "riverbank")
                return MapFeatureType.Water;
        }

        if (properties.ContainsKey("landuse"))
            return MapFeatureType.Land;

        if (properties.ContainsKey("boundary"))
            return MapFeatureType.Boundary;

        return MapFeatureType.Land;
    }

    private static void ReadCoordinateArray(
        JsonElement coordinates,
        List<WorldPoint> output)
    {
        foreach (JsonElement coordinate
                 in coordinates.EnumerateArray())
        {
            if (coordinate.GetArrayLength() < 2)
                continue;

            double longitude =
                coordinate[0].GetDouble();

            double latitude =
                coordinate[1].GetDouble();

            WorldPoint world =
                WebMercator.Project(
                    longitude,
                    latitude
                );

            output.Add(world);
        }
    }
    private static bool DetectVehicleRoad(
    RoadClass roadClass)
    {
        return roadClass is
            RoadClass.Motorway or
            RoadClass.Trunk or
            RoadClass.Primary or
            RoadClass.Secondary or
            RoadClass.Tertiary or
            RoadClass.Residential or
            RoadClass.Unclassified or
            RoadClass.LivingStreet or
            RoadClass.Service;
    }

    private static bool DetectPlanningRoad(
    RoadClass roadClass)
    {
        return roadClass is
            RoadClass.Motorway or
            RoadClass.Trunk or
            RoadClass.Primary or
            RoadClass.Secondary or
            RoadClass.Tertiary or
            RoadClass.Residential or
            RoadClass.Unclassified or
            RoadClass.LivingStreet or
            RoadClass.Service or
            RoadClass.Pedestrian;
    }

    private static double DetectRoadWidth(
    RoadClass roadClass,
    Dictionary<string, string> properties)
    {
        if (properties.TryGetValue(
                "width",
                out string? widthText))
        {
            widthText = widthText
                .Replace("m", "")
                .Trim();

            if (double.TryParse(
                    widthText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double width))
            {
                if (width > 1 && width < 100)
                    return width;
            }
        }

        return GetDefaultRoadWidth(
            roadClass
        );
    }

    private static double GetDefaultRoadWidth(
    RoadClass roadClass)
    {
        return roadClass switch
        {
            RoadClass.Motorway => 24.0,
            RoadClass.Trunk => 20.0,

            RoadClass.Primary => 16.0,
            RoadClass.Secondary => 14.0,
            RoadClass.Tertiary => 12.0,

            RoadClass.Residential => 9.0,
            RoadClass.Unclassified => 8.0,
            RoadClass.LivingStreet => 7.0,

            RoadClass.Service => 6.0,

            RoadClass.Pedestrian => 7.0,

            RoadClass.Cycleway => 3.0,
            RoadClass.Track => 4.0,

            _ => 7.0
        };
    }
    
    
}