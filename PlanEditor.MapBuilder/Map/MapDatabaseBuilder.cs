using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;

namespace PlanEditor.MapBuilder.Map;

public sealed class MapDatabaseBuilder
{
    public void Build(
        string geoJsonSeqPath,
        string outputDatabase)
    {
        if (!File.Exists(geoJsonSeqPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy map source: {geoJsonSeqPath}"
            );
        }

        string? directory =
            Path.GetDirectoryName(outputDatabase);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(outputDatabase))
            File.Delete(outputDatabase);

        using var connection =
            new SqliteConnection(
                $"Data Source={outputDatabase}"
            );

        connection.Open();

        CreateSchema(connection);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using SqliteCommand insertFeature =
            connection.CreateCommand();

        insertFeature.Transaction = transaction;

        insertFeature.CommandText = """
        INSERT INTO map_features
        (
            osm_id,
            feature_type,
            geometry_type,
            road_class,
            road_width,
            name,
            geometry_json,
            min_x,
            min_y,
            max_x,
            max_y
        )
        VALUES
        (
            $osmId,
            $featureType,
            $geometryType,
            $roadClass,
            $roadWidth,
            $name,
            $geometryJson,
            $minX,
            $minY,
            $maxX,
            $maxY
        );

        SELECT last_insert_rowid();
        """;

        insertFeature.Parameters.Add("$osmId", SqliteType.Text);
        insertFeature.Parameters.Add("$featureType", SqliteType.Integer);
        insertFeature.Parameters.Add("$geometryType", SqliteType.Integer);
        insertFeature.Parameters.Add("$roadClass", SqliteType.Integer);
        insertFeature.Parameters.Add("$roadWidth", SqliteType.Real);
        insertFeature.Parameters.Add("$name", SqliteType.Text);
        insertFeature.Parameters.Add("$geometryJson", SqliteType.Text);
        insertFeature.Parameters.Add("$minX", SqliteType.Real);
        insertFeature.Parameters.Add("$minY", SqliteType.Real);
        insertFeature.Parameters.Add("$maxX", SqliteType.Real);
        insertFeature.Parameters.Add("$maxY", SqliteType.Real);

        using SqliteCommand insertRtree =
            connection.CreateCommand();

        insertRtree.Transaction = transaction;

        insertRtree.CommandText = """
        INSERT INTO map_feature_rtree
        (
            id,
            min_x,
            max_x,
            min_y,
            max_y
        )
        VALUES
        (
            $id,
            $minX,
            $maxX,
            $minY,
            $maxY
        );
        """;

        insertRtree.Parameters.Add("$id", SqliteType.Integer);
        insertRtree.Parameters.Add("$minX", SqliteType.Real);
        insertRtree.Parameters.Add("$maxX", SqliteType.Real);
        insertRtree.Parameters.Add("$minY", SqliteType.Real);
        insertRtree.Parameters.Add("$maxY", SqliteType.Real);

        long sourceFeatures = 0;
        long storedParts = 0;
        long skipped = 0;

        foreach (string rawLine in File.ReadLines(geoJsonSeqPath))
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
                continue;

            if (line[0] == '\u001e')
                line = line[1..];

            sourceFeatures++;

            try
            {
                using JsonDocument json =
                    JsonDocument.Parse(line);

                JsonElement root =
                    json.RootElement;

                if (!TryClassify(
                        root,
                        out FeatureClassification classification))
                {
                    skipped++;
                    continue;
                }

                foreach (
                    StoredGeometry geometry
                    in ReadGeometries(
                        root,
                        classification.GeometryType))
                {
                    if (geometry.Points.Count <
                        (classification.GeometryType ==
                         MapGeometryType.Polygon ? 3 : 2))
                    {
                        continue;
                    }

                    string geometryJson =
                        JsonSerializer.Serialize(
                            geometry.Points.Select(
                                p => new[] { p.X, p.Y }
                            )
                        );

                    insertFeature.Parameters["$osmId"].Value =
                        classification.OsmId;

                    insertFeature.Parameters["$featureType"].Value =
                        (int)classification.FeatureType;

                    insertFeature.Parameters["$geometryType"].Value =
                        (int)classification.GeometryType;

                    insertFeature.Parameters["$roadClass"].Value =
                        (int)classification.RoadClass;

                    insertFeature.Parameters["$roadWidth"].Value =
                        classification.RoadWidthMeters;

                    insertFeature.Parameters["$name"].Value =
                        classification.Name;

                    insertFeature.Parameters["$geometryJson"].Value =
                        geometryJson;

                    insertFeature.Parameters["$minX"].Value =
                        geometry.MinX;

                    insertFeature.Parameters["$minY"].Value =
                        geometry.MinY;

                    insertFeature.Parameters["$maxX"].Value =
                        geometry.MaxX;

                    insertFeature.Parameters["$maxY"].Value =
                        geometry.MaxY;

                    long id =
                        Convert.ToInt64(
                            insertFeature.ExecuteScalar(),
                            CultureInfo.InvariantCulture
                        );

                    insertRtree.Parameters["$id"].Value = id;
                    insertRtree.Parameters["$minX"].Value = geometry.MinX;
                    insertRtree.Parameters["$maxX"].Value = geometry.MaxX;
                    insertRtree.Parameters["$minY"].Value = geometry.MinY;
                    insertRtree.Parameters["$maxY"].Value = geometry.MaxY;

                    insertRtree.ExecuteNonQuery();

                    storedParts++;

                    if (storedParts % 50000 == 0)
                    {
                        Console.WriteLine(
                            $"Map parts stored: {storedParts:N0}"
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                skipped++;

                if (skipped <= 20)
                {
                    Console.Error.WriteLine(
                        $"Skip map feature #{sourceFeatures:N0}: " +
                        exception.Message
                    );
                }
            }
        }

        transaction.Commit();

        using (
            SqliteCommand analyze =
                connection.CreateCommand())
        {
            analyze.CommandText = """
            ANALYZE;
            PRAGMA optimize;
            """;

            analyze.ExecuteNonQuery();
        }

        Console.WriteLine();
        Console.WriteLine("===== MAP DATABASE =====");
        Console.WriteLine($"Source features : {sourceFeatures:N0}");
        Console.WriteLine($"Stored parts    : {storedParts:N0}");
        Console.WriteLine($"Skipped         : {skipped:N0}");
        Console.WriteLine($"Created         : {outputDatabase}");
    }

    private static void CreateSchema(
        SqliteConnection connection)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = """
        PRAGMA journal_mode = OFF;
        PRAGMA synchronous = OFF;
        PRAGMA temp_store = MEMORY;

        CREATE TABLE map_features
        (
            id INTEGER PRIMARY KEY,

            osm_id TEXT,

            feature_type INTEGER NOT NULL,
            geometry_type INTEGER NOT NULL,

            road_class INTEGER NOT NULL DEFAULT 0,
            road_width REAL NOT NULL DEFAULT 0,

            name TEXT DEFAULT '',

            geometry_json TEXT NOT NULL,

            min_x REAL NOT NULL,
            min_y REAL NOT NULL,
            max_x REAL NOT NULL,
            max_y REAL NOT NULL
        );

        CREATE VIRTUAL TABLE map_feature_rtree
        USING rtree
        (
            id,
            min_x,
            max_x,
            min_y,
            max_y
        );

        CREATE INDEX idx_map_feature_type
        ON map_features(feature_type);

        CREATE INDEX idx_map_road_class
        ON map_features(road_class);
        """;

        command.ExecuteNonQuery();
    }

    private static bool TryClassify(
        JsonElement feature,
        out FeatureClassification classification)
    {
        classification = default;

        if (!feature.TryGetProperty(
                "properties",
                out JsonElement properties))
        {
            return false;
        }

        if (!feature.TryGetProperty(
                "geometry",
                out JsonElement geometry))
        {
            return false;
        }

        string geometryType =
            ReadString(geometry, "type");

        MapGeometryType mapGeometryType;

        switch (geometryType)
        {
            case "LineString":
            case "MultiLineString":
                mapGeometryType =
                    MapGeometryType.LineString;
                break;

            case "Polygon":
            case "MultiPolygon":
                mapGeometryType =
                    MapGeometryType.Polygon;
                break;

            default:
                return false;
        }

        string highway =
            ReadString(properties, "highway");

        string building =
            ReadString(properties, "building");

        string natural =
            ReadString(properties, "natural");

        string water =
            ReadString(properties, "water");

        string waterway =
            ReadString(properties, "waterway");

        string landuse =
            ReadString(properties, "landuse");

        string barrier =
            ReadString(properties, "barrier");

        MapFeatureType featureType;
        RoadClass roadClass = default;
        double roadWidthMeters = 0;

        if (!string.IsNullOrWhiteSpace(highway))
        {
            if (mapGeometryType !=
                MapGeometryType.LineString)
            {
                return false;
            }

            featureType = MapFeatureType.Road;
            roadClass = ParseRoadClass(highway);
            roadWidthMeters =
                ReadRoadWidth(
                    properties,
                    roadClass
                );
        }
        else if (
            mapGeometryType ==
                MapGeometryType.Polygon &&
            (
                natural.Equals(
                    "water",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(water) ||
                waterway.Equals(
                    "riverbank",
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            featureType = MapFeatureType.Water;
        }
        else if (
            mapGeometryType ==
                MapGeometryType.Polygon &&
            !string.IsNullOrWhiteSpace(building))
        {
            featureType = MapFeatureType.Building;
        }
        else if (
            !string.IsNullOrWhiteSpace(barrier))
        {
            featureType = MapFeatureType.Barrier;
        }
        else if (
            mapGeometryType ==
                MapGeometryType.Polygon &&
            !string.IsNullOrWhiteSpace(landuse))
        {
            featureType = MapFeatureType.Land;
        }
        else
        {
            return false;
        }

        classification =
            new FeatureClassification(
                ReadString(properties, "@id"),
                ReadString(properties, "name"),
                featureType,
                mapGeometryType,
                roadClass,
                roadWidthMeters
            );

        return true;
    }

    private static IEnumerable<StoredGeometry>
        ReadGeometries(
            JsonElement feature,
            MapGeometryType geometryType)
    {
        JsonElement geometry =
            feature.GetProperty("geometry");

        string type =
            geometry.GetProperty("type").GetString()
            ?? "";

        JsonElement coordinates =
            geometry.GetProperty("coordinates");

        if (geometryType ==
            MapGeometryType.LineString)
        {
            if (type == "LineString")
            {
                StoredGeometry? result =
                    ReadPointSequence(coordinates);

                if (result != null)
                    yield return result;

                yield break;
            }

            if (type == "MultiLineString")
            {
                foreach (
                    JsonElement line
                    in coordinates.EnumerateArray())
                {
                    StoredGeometry? result =
                        ReadPointSequence(line);

                    if (result != null)
                        yield return result;
                }
            }

            yield break;
        }

        if (type == "Polygon")
        {
            // MapFeature hiện tại chỉ chứa một vòng điểm.
            // Dùng outer ring; holes giữ cho phase polygon nâng cao sau.
            JsonElement.ArrayEnumerator rings =
                coordinates.EnumerateArray();

            if (rings.MoveNext())
            {
                StoredGeometry? result =
                    ReadPointSequence(
                        rings.Current
                    );

                if (result != null)
                    yield return result;
            }

            yield break;
        }

        if (type == "MultiPolygon")
        {
            foreach (
                JsonElement polygon
                in coordinates.EnumerateArray())
            {
                JsonElement.ArrayEnumerator rings =
                    polygon.EnumerateArray();

                if (!rings.MoveNext())
                    continue;

                StoredGeometry? result =
                    ReadPointSequence(
                        rings.Current
                    );

                if (result != null)
                    yield return result;
            }
        }
    }

    private static StoredGeometry?
        ReadPointSequence(
            JsonElement coordinates)
    {
        var points =
            new List<WorldPoint>();

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (
            JsonElement coordinate
            in coordinates.EnumerateArray())
        {
            if (
                coordinate.ValueKind !=
                    JsonValueKind.Array ||
                coordinate.GetArrayLength() < 2)
            {
                continue;
            }

            double lon =
                coordinate[0].GetDouble();

            double lat =
                coordinate[1].GetDouble();

            WorldPoint point =
                WebMercator.Project(
                    lon,
                    lat
                );

            points.Add(point);

            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        if (points.Count == 0)
            return null;

        return new StoredGeometry(
            points,
            minX,
            minY,
            maxX,
            maxY
        );
    }

    private static RoadClass ParseRoadClass(
        string highway)
    {
        return highway.Trim().ToLowerInvariant() switch
        {
            "motorway" or "motorway_link" =>
                RoadClass.Motorway,

            "trunk" or "trunk_link" =>
                RoadClass.Trunk,

            "primary" or "primary_link" =>
                RoadClass.Primary,

            "secondary" or "secondary_link" =>
                RoadClass.Secondary,

            "tertiary" or "tertiary_link" =>
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

            "cycleway" =>
                RoadClass.Cycleway,

            "track" =>
                RoadClass.Track,

            "footway" =>
                RoadClass.Footway,

            "path" =>
                RoadClass.Path,

            "steps" =>
                RoadClass.Steps,

            _ =>
                RoadClass.Unclassified
        };
    }

    private static double ReadRoadWidth(
        JsonElement properties,
        RoadClass roadClass)
    {
        string width =
            ReadString(properties, "width");

        if (!string.IsNullOrWhiteSpace(width))
        {
            string numeric =
                new string(
                    width
                        .Replace(',', '.')
                        .TakeWhile(
                            c =>
                                char.IsDigit(c) ||
                                c == '.' ||
                                c == '-'
                        )
                        .ToArray()
                );

            if (double.TryParse(
                    numeric,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed) &&
                parsed > 0)
            {
                return parsed;
            }
        }

        return roadClass switch
        {
            RoadClass.Motorway => 24,
            RoadClass.Trunk => 20,
            RoadClass.Primary => 16,
            RoadClass.Secondary => 14,
            RoadClass.Tertiary => 12,
            RoadClass.Residential => 9,
            RoadClass.Unclassified => 8,
            RoadClass.LivingStreet => 7,
            RoadClass.Service => 6,
            RoadClass.Pedestrian => 7,
            RoadClass.Cycleway => 3,
            RoadClass.Track => 4,
            RoadClass.Footway => 2,
            RoadClass.Path => 2,
            RoadClass.Steps => 2,
            _ => 6
        };
    }

    private static string ReadString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return "";
        }

        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return "";
        }

        return value.ToString().Trim();
    }

    private readonly record struct FeatureClassification(
        string OsmId,
        string Name,
        MapFeatureType FeatureType,
        MapGeometryType GeometryType,
        RoadClass RoadClass,
        double RoadWidthMeters
    );

    private sealed record StoredGeometry(
        List<WorldPoint> Points,
        double MinX,
        double MinY,
        double MaxX,
        double MaxY
    );
}
