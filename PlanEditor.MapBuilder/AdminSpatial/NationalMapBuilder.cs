using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using PlanEditor.Core.Geometry;

namespace PlanEditor.MapBuilder.AdminSpatial;

/// <summary>
/// Build master national overview from a province-level GeoJSON.
///
/// Design goals:
/// - dissolve internal province borders;
/// - keep every disconnected island polygon separate;
/// - never connect islands to mainland with synthetic line segments;
/// - preserve source geometry for Hoang Sa / Truong Sa if it exists;
/// - output WebMercator coordinates for PlanEditor.App.
/// </summary>
public sealed class NationalMapBuilder
{
    private readonly GeometryFactory _geometryFactory =
        new(new PrecisionModel(), 4326);

    // National overview only: roughly a few hundred metres.
    // Small enough to keep coastline shape while remaining light to render.
    private const double SimplifyToleranceDegrees = 0.0018;

    // Remove microscopic polygon artefacts after dissolve.
    // This does NOT delete visible islands at national scale;
    // archipelago polygons are kept with a much lower threshold.
    private const double MinGeneralPolygonAreaDegrees = 0.0000025;
    private const double MinArchipelagoPolygonAreaDegrees = 0.00000008;

    // Remove very small holes created by invalid/sliver geometry.
    private const double MinHoleAreaDegrees = 0.0000015;

    public void Build(
        string sourceGeoJsonPath,
        string outputJsonPath)
    {
        if (!File.Exists(sourceGeoJsonPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy GeoJSON nguồn: {sourceGeoJsonPath}"
            );
        }

        using JsonDocument json =
            JsonDocument.Parse(
                File.ReadAllText(sourceGeoJsonPath)
            );

        JsonElement root =
            json.RootElement;

        if (!root.TryGetProperty(
                "type",
                out JsonElement typeElement) ||
            !string.Equals(
                typeElement.GetString(),
                "FeatureCollection",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "NationalMapBuilder cần GeoJSON FeatureCollection."
            );
        }

        if (!root.TryGetProperty(
                "features",
                out JsonElement features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "GeoJSON không có mảng features."
            );
        }

        var geometries =
            new List<Geometry>();

        int featureCount = 0;

        foreach (JsonElement feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty(
                    "geometry",
                    out JsonElement geometryElement) ||
                geometryElement.ValueKind ==
                    JsonValueKind.Null)
            {
                continue;
            }

            Geometry? geometry =
                ReadGeometry(
                    geometryElement
                );

            if (geometry == null ||
                geometry.IsEmpty)
            {
                continue;
            }

            if (!geometry.IsValid)
            {
                // Buffer(0) is a standard NTS repair for minor polygon issues.
                geometry =
                    geometry.Buffer(0);
            }

            if (geometry.IsEmpty)
                continue;

            geometries.Add(
                geometry
            );

            featureCount++;
        }

        if (geometries.Count == 0)
        {
            throw new InvalidDataException(
                "Không đọc được polygon nào từ GeoJSON."
            );
        }

        Console.WriteLine(
            $"National source features: {featureCount:N0}"
        );

        /*
         * Dissolve all province polygons.
         *
         * This removes province borders while preserving
         * disconnected islands as independent polygons.
         */
        Geometry dissolved =
            UnaryUnionOp.Union(
                geometries
            );

        /*
         * Clean union in two passes.
         *
         * Buffer(0) repairs many self-intersections/slivers.
         * A second unary union normalizes overlapping fragments.
         */
        dissolved =
            CleanGeometry(
                dissolved
            );

        Geometry simplified;

        try
        {
            simplified =
                TopologyPreservingSimplifier.Simplify(
                    dissolved,
                    SimplifyToleranceDegrees
                );
        }
        catch
        {
            simplified =
                dissolved;
        }

        simplified =
            CleanGeometry(
                simplified
            );

        List<Polygon> polygons =
            FlattenPolygons(
                simplified
            )
            .Select(
                RemoveTinyHoles
            )
            .Where(
                polygon =>
                    !polygon.IsEmpty
            )
            .ToList();

        if (polygons.Count == 0)
        {
            throw new InvalidDataException(
                "National union không tạo được polygon."
            );
        }

        Polygon mainland =
            polygons
                .OrderByDescending(
                    polygon =>
                        polygon.Area
                )
                .First();

        var parts =
            new List<NationalPart>();

        int mainlandParts = 0;
        int islandParts = 0;
        int hoangSaParts = 0;
        int truongSaParts = 0;

        foreach (Polygon polygon in polygons)
        {
            Point centroid =
                polygon.Centroid;

            bool hoangSa =
                IsHoangSaRegion(
                    centroid.X,
                    centroid.Y
                );

            bool truongSa =
                IsTruongSaRegion(
                    centroid.X,
                    centroid.Y
                );

            bool archipelago =
                hoangSa ||
                truongSa;

            bool isMainland =
                ReferenceEquals(
                    polygon,
                    mainland
                );

            /*
             * Filter only post-union artefacts.
             *
             * Archipelago polygons use a much lower minimum
             * because valid reef/islet geometries can be tiny.
             */
            double minimumArea =
                archipelago
                    ? MinArchipelagoPolygonAreaDegrees
                    : MinGeneralPolygonAreaDegrees;

            if (!isMainland &&
                polygon.Area <
                    minimumArea)
            {
                continue;
            }

            string kind;
            string name;

            if (hoangSa)
            {
                kind = "archipelago";
                name = "Hoàng Sa";
                hoangSaParts++;
            }
            else if (truongSa)
            {
                kind = "archipelago";
                name = "Trường Sa";
                truongSaParts++;
            }
            else if (isMainland)
            {
                kind = "country";
                name = "Việt Nam";
                mainlandParts++;
            }
            else
            {
                kind = "island";
                name = "Đảo Việt Nam";
                islandParts++;
            }

            AddPolygonPart(
                parts,
                polygon,
                kind,
                name
            );
        }

        var output =
            new NationalMapFile
            {
                Version = 3,
                Source =
                    Path.GetFileName(
                        sourceGeoJsonPath
                    ),
                Projection = "EPSG:3857",
                Parts = parts
            };

        string? directory =
            Path.GetDirectoryName(
                outputJsonPath
            );

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(
                directory
            );
        }

        File.WriteAllText(
            outputJsonPath,
            JsonSerializer.Serialize(
                output,
                new JsonSerializerOptions
                {
                    WriteIndented = false
                }
            )
        );

        Console.WriteLine();
        Console.WriteLine(
            "===== NATIONAL MAP ====="
        );

        Console.WriteLine(
            $"Mainland parts : {mainlandParts:N0}"
        );

        Console.WriteLine(
            $"Island parts   : {islandParts:N0}"
        );

        Console.WriteLine(
            $"Hoang Sa parts : {hoangSaParts:N0}"
        );

        Console.WriteLine(
            $"Truong Sa parts: {truongSaParts:N0}"
        );

        Console.WriteLine(
            $"Total parts    : {parts.Count:N0}"
        );

        Console.WriteLine(
            $"Created        : {outputJsonPath}"
        );

        if (hoangSaParts == 0 ||
            truongSaParts == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "WARNING: GeoJSON nguồn không có đủ polygon " +
                "Hoàng Sa/Trường Sa trong vùng kiểm tra. " +
                "Builder không tự tạo geometry thay thế."
            );
        }
    }

    private Geometry? ReadGeometry(
        JsonElement geometry)
    {
        if (!geometry.TryGetProperty(
                "type",
                out JsonElement typeElement))
        {
            return null;
        }

        string? type =
            typeElement.GetString();

        if (!geometry.TryGetProperty(
                "coordinates",
                out JsonElement coordinates))
        {
            return null;
        }

        return type switch
        {
            "Polygon" =>
                ReadPolygon(
                    coordinates
                ),

            "MultiPolygon" =>
                ReadMultiPolygon(
                    coordinates
                ),

            _ => null
        };
    }

    private Polygon ReadPolygon(
        JsonElement coordinates)
    {
        var rings =
            new List<LinearRing>();

        foreach (
            JsonElement ringElement
            in coordinates.EnumerateArray())
        {
            LinearRing? ring =
                ReadLinearRing(
                    ringElement
                );

            if (ring != null)
            {
                rings.Add(
                    ring
                );
            }
        }

        if (rings.Count == 0)
        {
            return _geometryFactory
                .CreatePolygon();
        }

        LinearRing shell =
            rings[0];

        LinearRing[] holes =
            rings
                .Skip(1)
                .ToArray();

        return _geometryFactory
            .CreatePolygon(
                shell,
                holes
            );
    }

    private MultiPolygon ReadMultiPolygon(
        JsonElement coordinates)
    {
        var polygons =
            new List<Polygon>();

        foreach (
            JsonElement polygonElement
            in coordinates.EnumerateArray())
        {
            Polygon polygon =
                ReadPolygon(
                    polygonElement
                );

            if (!polygon.IsEmpty)
            {
                polygons.Add(
                    polygon
                );
            }
        }

        return _geometryFactory
            .CreateMultiPolygon(
                polygons.ToArray()
            );
    }

    private LinearRing? ReadLinearRing(
        JsonElement ringElement)
    {
        var coordinates =
            new List<Coordinate>();

        foreach (
            JsonElement point
            in ringElement.EnumerateArray())
        {
            if (point.ValueKind !=
                JsonValueKind.Array)
            {
                continue;
            }

            JsonElement.ArrayEnumerator values =
                point.EnumerateArray();

            if (!values.MoveNext())
                continue;

            double lon =
                values.Current.GetDouble();

            if (!values.MoveNext())
                continue;

            double lat =
                values.Current.GetDouble();

            coordinates.Add(
                new Coordinate(
                    lon,
                    lat
                )
            );
        }

        if (coordinates.Count < 3)
            return null;

        Coordinate first =
            coordinates[0];

        Coordinate last =
            coordinates[
                coordinates.Count - 1
            ];

        if (!first.Equals2D(last))
        {
            coordinates.Add(
                new Coordinate(
                    first.X,
                    first.Y
                )
            );
        }

        if (coordinates.Count < 4)
            return null;

        return _geometryFactory
            .CreateLinearRing(
                coordinates.ToArray()
            );
    }

    private Geometry CleanGeometry(
        Geometry geometry)
    {
        if (geometry.IsEmpty)
            return geometry;

        Geometry cleaned =
            geometry;

        try
        {
            cleaned =
                cleaned.Buffer(0);
        }
        catch
        {
            // Keep original if repair fails.
        }

        try
        {
            cleaned =
                UnaryUnionOp.Union(
                    FlattenPolygons(
                        cleaned
                    )
                    .Cast<Geometry>()
                    .ToList()
                );
        }
        catch
        {
            // Keep last valid geometry.
        }

        return cleaned;
    }

    private Polygon RemoveTinyHoles(
        Polygon polygon)
    {
        if (polygon.IsEmpty)
            return polygon;

        LinearRing shell =
            (LinearRing)polygon
                .ExteriorRing
                .Copy();

        var holes =
            new List<LinearRing>();

        for (
            int i = 0;
            i < polygon.NumInteriorRings;
            i++)
        {
            LinearRing ring =
                (LinearRing)polygon
                    .GetInteriorRingN(i)
                    .Copy();

            Polygon holePolygon =
                _geometryFactory
                    .CreatePolygon(
                        ring
                    );

            if (Math.Abs(
                    holePolygon.Area) >=
                MinHoleAreaDegrees)
            {
                holes.Add(
                    ring
                );
            }
        }

        Polygon cleaned =
            _geometryFactory
                .CreatePolygon(
                    shell,
                    holes.ToArray()
                );

        if (!cleaned.IsValid)
        {
            Geometry repaired =
                cleaned.Buffer(0);

            List<Polygon> repairedParts =
                FlattenPolygons(
                    repaired
                );

            if (repairedParts.Count > 0)
            {
                return repairedParts
                    .OrderByDescending(
                        item => item.Area
                    )
                    .First();
            }
        }

        return cleaned;
    }

    private static List<Polygon>
        FlattenPolygons(
            Geometry geometry)
    {
        var result =
            new List<Polygon>();

        CollectPolygons(
            geometry,
            result
        );

        return result;
    }

    private static void CollectPolygons(
        Geometry geometry,
        List<Polygon> output)
    {
        switch (geometry)
        {
            case Polygon polygon:
                if (!polygon.IsEmpty)
                {
                    output.Add(
                        polygon
                    );
                }

                break;

            case MultiPolygon multiPolygon:
                for (
                    int i = 0;
                    i < multiPolygon.NumGeometries;
                    i++)
                {
                    CollectPolygons(
                        multiPolygon.GetGeometryN(i),
                        output
                    );
                }

                break;

            case GeometryCollection collection:
                for (
                    int i = 0;
                    i < collection.NumGeometries;
                    i++)
                {
                    CollectPolygons(
                        collection.GetGeometryN(i),
                        output
                    );
                }

                break;
        }
    }

    private static void AddPolygonPart(
        List<NationalPart> output,
        Polygon polygon,
        string kind,
        string name)
    {
        Coordinate[] coordinates =
            polygon.ExteriorRing
                .Coordinates;

        if (coordinates.Length < 4)
            return;

        var points =
            new double[
                coordinates.Length
            ][];

        for (
            int i = 0;
            i < coordinates.Length;
            i++)
        {
            Coordinate coordinate =
                coordinates[i];

            WorldPoint world =
                WebMercator.Project(
                    coordinate.X,
                    coordinate.Y
                );

            points[i] =
                new[]
                {
                    world.X,
                    world.Y
                };
        }

        output.Add(
            new NationalPart
            {
                Kind = kind,
                Name = name,
                Points = points
            }
        );
    }

    private static bool IsHoangSaRegion(
        double longitude,
        double latitude)
    {
        return
            longitude >= 110.5 &&
            longitude <= 114.5 &&
            latitude >= 14.0 &&
            latitude <= 18.5;
    }

    private static bool IsTruongSaRegion(
        double longitude,
        double latitude)
    {
        return
            longitude >= 110.5 &&
            longitude <= 118.0 &&
            latitude >= 6.0 &&
            latitude <= 13.0;
    }

    private sealed class NationalMapFile
    {
        public int Version { get; set; }

        public string Source
        {
            get;
            set;
        } = "";

        public string Projection
        {
            get;
            set;
        } = "";

        public List<NationalPart> Parts
        {
            get;
            set;
        } = new();
    }

    private sealed class NationalPart
    {
        public string Kind
        {
            get;
            set;
        } = "";

        public string Name
        {
            get;
            set;
        } = "";

        public double[][] Points
        {
            get;
            set;
        } =
            Array.Empty<double[]>();
    }
}
