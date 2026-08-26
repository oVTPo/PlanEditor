using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;

namespace PlanEditor.App.Map;

public sealed class VietnamMapStore : IDisposable
{
    private readonly string _databasePath;
    private bool _disposed;

    public VietnamMapStore(
        string? databasePath = null)
    {
        databasePath ??=
            Path.Combine(
                AppContext.BaseDirectory,
                "MapData",
                "vietnam-map.db"
            );

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy map database: {databasePath}"
            );
        }

        _databasePath = databasePath;
    }

    public MapDocument LoadArea(
    WorldPoint center,
    double radiusMeters,
    double metersPerPixel = 5.0)
    {
        return LoadArea(
            new WorldBounds(
                center.X - radiusMeters,
                center.Y - radiusMeters,
                center.X + radiusMeters,
                center.Y + radiusMeters
            ),
            metersPerPixel
        );
    }

    public MapDocument LoadArea(
        WorldBounds bounds,
        double metersPerPixel)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this
        );

        var document =
            new MapDocument();

        using var connection =
            new SqliteConnection(
                $"Data Source={_databasePath};Mode=ReadOnly"
            );

        connection.Open();

        using SqliteCommand command =
            connection.CreateCommand();

        BuildQuery(
            command,
            metersPerPixel
        );

        command.Parameters.AddWithValue(
            "$minX",
            bounds.MinX
        );

        command.Parameters.AddWithValue(
            "$minY",
            bounds.MinY
        );

        command.Parameters.AddWithValue(
            "$maxX",
            bounds.MaxX
        );

        command.Parameters.AddWithValue(
            "$maxY",
            bounds.MaxY
        );

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            MapFeature? feature =
                ReadFeature(reader);

            if (feature == null)
                continue;

            document.Features.Add(feature);
        }

        document.BuildSpatialIndex();

        Console.WriteLine(
            $"MapStore: {document.Features.Count:N0} features | " +
            $"{metersPerPixel:0.0} m/px | " +
            $"overview={metersPerPixel > 80.0}"
        );

        return document;
    }

    private static void BuildQuery(
        SqliteCommand command,
        double metersPerPixel)
    {
        const double OverviewThreshold = 80.0;

        MapFeatureType road =
            MapFeatureType.Road;

        MapFeatureType water =
            MapFeatureType.Water;

        MapFeatureType land =
            MapFeatureType.Land;

        MapFeatureType building =
            MapFeatureType.Building;

        MapFeatureType barrier =
            MapFeatureType.Barrier;

        MapFeatureType boundary =
            MapFeatureType.Boundary;

        /*
         * OVERVIEW MODE
         *
         * Khi nhìn rộng cỡ 2-3 tỉnh trở lên, tuyệt đối
         * không đọc road/building/barrier từ SQLite.
         *
         * Đây là tối ưu quan trọng: tránh JSON deserialize,
         * tránh tạo hàng nghìn MapFeature và tránh build
         * spatial index cho dữ liệu không cần hiển thị.
         *
         * Tạm thời giữ Boundary + Water. Layer ranh giới
         * tỉnh/Việt Nam đơn giản hóa sẽ được bổ sung riêng.
         */
        if (metersPerPixel > OverviewThreshold)
        {
            bool showOverviewWater =
                metersPerPixel <= 350.0;

            command.CommandText = """
            SELECT
                f.feature_type,
                f.geometry_type,
                f.road_class,
                f.road_width,
                COALESCE(f.name, ''),
                f.geometry_json

            FROM map_feature_rtree r

            JOIN map_features f
                ON f.id = r.id

            WHERE
                r.max_x >= $minX
                AND r.min_x <= $maxX
                AND r.max_y >= $minY
                AND r.min_y <= $maxY

                AND
                (
                    f.feature_type = $boundaryType

                    OR
                    (
                        $showWater = 1
                        AND f.feature_type = $waterType
                    )
                );
            """;

            command.Parameters.AddWithValue(
                "$boundaryType",
                (int)boundary
            );

            command.Parameters.AddWithValue(
                "$waterType",
                (int)water
            );

            command.Parameters.AddWithValue(
                "$showWater",
                showOverviewWater ? 1 : 0
            );

            return;
        }

        RoadClass[] roads =
            GetRoadClassesForScale(
                metersPerPixel
            );

        var roadParameterNames =
            new List<string>(
                roads.Length
            );

        for (int i = 0;
            i < roads.Length;
            i++)
        {
            string parameterName =
                $"$road{i}";

            roadParameterNames.Add(
                parameterName
            );

            command.Parameters.AddWithValue(
                parameterName,
                (int)roads[i]
            );
        }

        bool showWater = true;

        bool showLand =
            metersPerPixel <= 60.0;

        bool showBuildings =
            metersPerPixel <= 5.0;

        bool showBarriers =
            metersPerPixel <= 2.0;

        string roadList =
            string.Join(
                ", ",
                roadParameterNames
            );

        command.CommandText = $"""
        SELECT
            f.feature_type,
            f.geometry_type,
            f.road_class,
            f.road_width,
            COALESCE(f.name, ''),
            f.geometry_json

        FROM map_feature_rtree r

        JOIN map_features f
            ON f.id = r.id

        WHERE
            r.max_x >= $minX
            AND r.min_x <= $maxX
            AND r.max_y >= $minY
            AND r.min_y <= $maxY

            AND
            (
                f.feature_type = $boundaryType

                OR
                (
                    f.feature_type = $roadType
                    AND f.road_class IN ({roadList})
                )

                OR
                (
                    $showWater = 1
                    AND f.feature_type = $waterType
                )

                OR
                (
                    $showLand = 1
                    AND f.feature_type = $landType
                )

                OR
                (
                    $showBuildings = 1
                    AND f.feature_type = $buildingType
                )

                OR
                (
                    $showBarriers = 1
                    AND f.feature_type = $barrierType
                )
            );
        """;

        command.Parameters.AddWithValue(
            "$roadType",
            (int)road
        );

        command.Parameters.AddWithValue(
            "$waterType",
            (int)water
        );

        command.Parameters.AddWithValue(
            "$landType",
            (int)land
        );

        command.Parameters.AddWithValue(
            "$buildingType",
            (int)building
        );

        command.Parameters.AddWithValue(
            "$barrierType",
            (int)barrier
        );

        command.Parameters.AddWithValue(
            "$boundaryType",
            (int)boundary
        );

        command.Parameters.AddWithValue(
            "$showWater",
            showWater ? 1 : 0
        );

        command.Parameters.AddWithValue(
            "$showLand",
            showLand ? 1 : 0
        );

        command.Parameters.AddWithValue(
            "$showBuildings",
            showBuildings ? 1 : 0
        );

        command.Parameters.AddWithValue(
            "$showBarriers",
            showBarriers ? 1 : 0
        );
    }

    private static RoadClass[]
        GetRoadClassesForScale(
            double metersPerPixel)
    {
        if (metersPerPixel > 300.0)
        {
            return
            [
                RoadClass.Motorway,
                RoadClass.Trunk,
                RoadClass.Primary
            ];
        }

        if (metersPerPixel > 80.0)
        {
            return
            [
                RoadClass.Motorway,
                RoadClass.Trunk,
                RoadClass.Primary,
                RoadClass.Secondary
            ];
        }

        if (metersPerPixel > 20.0)
        {
            return
            [
                RoadClass.Motorway,
                RoadClass.Trunk,
                RoadClass.Primary,
                RoadClass.Secondary,
                RoadClass.Tertiary
            ];
        }

        if (metersPerPixel > 5.0)
        {
            return
            [
                RoadClass.Motorway,
                RoadClass.Trunk,
                RoadClass.Primary,
                RoadClass.Secondary,
                RoadClass.Tertiary,
                RoadClass.Residential,
                RoadClass.Unclassified,
                RoadClass.LivingStreet
            ];
        }

        return
        [
            RoadClass.Motorway,
            RoadClass.Trunk,
            RoadClass.Primary,
            RoadClass.Secondary,
            RoadClass.Tertiary,
            RoadClass.Residential,
            RoadClass.Unclassified,
            RoadClass.LivingStreet,
            RoadClass.Service,
            RoadClass.Pedestrian,
            RoadClass.Cycleway,
            RoadClass.Track
        ];
    }

    private static MapFeature?
        ReadFeature(
            SqliteDataReader reader)
    {
        var featureType =
            (MapFeatureType)
            reader.GetInt32(0);

        var geometryType =
            (MapGeometryType)
            reader.GetInt32(1);

        var roadClass =
            (RoadClass)
            reader.GetInt32(2);

        double roadWidth =
            reader.GetDouble(3);

        string name =
            reader.GetString(4);

        string geometryJson =
            reader.GetString(5);

        double[][]? rawPoints =
            JsonSerializer.Deserialize<double[][]>(
                geometryJson
            );

        if (rawPoints == null ||
            rawPoints.Length == 0)
        {
            return null;
        }

        var points =
            new List<WorldPoint>(
                rawPoints.Length
            );

        foreach (double[] raw in rawPoints)
        {
            if (raw.Length < 2)
                continue;

            points.Add(
                new WorldPoint(
                    raw[0],
                    raw[1]
                )
            );
        }

        int minimumPointCount =
            geometryType ==
                MapGeometryType.Polygon
                ? 3
                : 2;

        if (points.Count <
            minimumPointCount)
        {
            return null;
        }

        var feature =
            new MapFeature
            {
                Type = featureType,
                GeometryType = geometryType,
                RoadClass = roadClass,
                RoadWidthMeters = roadWidth,
                Name = name
            };

        feature.Points.AddRange(points);

        if (featureType ==
            MapFeatureType.Road)
        {
            feature.IsVehicleRoad =
                roadClass is
                    RoadClass.Motorway or
                    RoadClass.Trunk or
                    RoadClass.Primary or
                    RoadClass.Secondary or
                    RoadClass.Tertiary or
                    RoadClass.Residential or
                    RoadClass.Unclassified or
                    RoadClass.LivingStreet or
                    RoadClass.Service;

            feature.IsPlanningRoad =
                feature.IsVehicleRoad ||
                roadClass ==
                    RoadClass.Pedestrian;
        }

        feature.UpdateBounds();

        return feature;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
