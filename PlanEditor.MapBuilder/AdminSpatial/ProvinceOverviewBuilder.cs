using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using PlanEditor.Core.Geometry;

namespace PlanEditor.MapBuilder.AdminSpatial;

/// <summary>
/// Builds province overview directly from the same post-2025
/// 34-province GeoJSON used by NationalMapBuilder.
///
/// This removes the old dependency on vietnam-overview.json
/// generated from the administrative-boundary source.
/// </summary>
public sealed class ProvinceOverviewBuilder
{
    private readonly GeometryFactory _geometryFactory =
        new(new PrecisionModel(), 4326);

    // Province overview scale only.
    private const double SimplifyToleranceDegrees =
        0.0025;

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
                File.ReadAllText(
                    sourceGeoJsonPath
                )
            );

        JsonElement root =
            json.RootElement;

        if (!root.TryGetProperty(
                "features",
                out JsonElement features) ||
            features.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "GeoJSON không có FeatureCollection/features."
            );
        }

        var parts =
            new List<ProvincePart>();

        var provinceNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        int sourceFeatureCount = 0;

        foreach (
            JsonElement feature
            in features.EnumerateArray())
        {
            sourceFeatureCount++;

            string name =
                ReadProvinceName(
                    feature,
                    sourceFeatureCount
                );

            if (!feature.TryGetProperty(
                    "geometry",
                    out JsonElement geometryElement) ||
                geometryElement.ValueKind ==
                    JsonValueKind.Null)
            {
                Console.WriteLine(
                    $"WARNING: {name} không có geometry."
                );

                continue;
            }

            Geometry? geometry =
                ReadGeometry(
                    geometryElement
                );

            if (geometry == null ||
                geometry.IsEmpty)
            {
                Console.WriteLine(
                    $"WARNING: {name} geometry rỗng."
                );

                continue;
            }

            if (!geometry.IsValid)
            {
                try
                {
                    geometry =
                        geometry.Buffer(0);
                }
                catch
                {
                    // Keep original.
                }
            }

            Geometry simplified;

            try
            {
                simplified =
                    TopologyPreservingSimplifier.Simplify(
                        geometry,
                        SimplifyToleranceDegrees
                    );
            }
            catch
            {
                simplified = geometry;
            }

            List<Polygon> polygons =
                FlattenPolygons(
                    simplified
                );

            if (polygons.Count == 0)
            {
                Console.WriteLine(
                    $"WARNING: {name} không tạo được polygon."
                );

                continue;
            }

            provinceNames.Add(
                name
            );

            foreach (Polygon polygon in polygons)
            {
                AddPolygonPart(
                    parts,
                    polygon,
                    name
                );
            }
        }

        var output =
            new ProvinceOverviewFile
            {
                Version = 3,
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
            "===== PROVINCE OVERVIEW V3 ====="
        );

        Console.WriteLine(
            $"Source features : {sourceFeatureCount:N0}"
        );

        Console.WriteLine(
            $"Unique provinces: {provinceNames.Count:N0}"
        );

        Console.WriteLine(
            $"Polygon parts   : {parts.Count:N0}"
        );

        Console.WriteLine(
            $"Created         : {outputJsonPath}"
        );

        Console.WriteLine();
        Console.WriteLine(
            "Province names:"
        );

        foreach (
            string provinceName
            in provinceNames.OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"  - {provinceName}"
            );
        }

        if (provinceNames.Count != 34)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"WARNING: expected 34 provinces, got {provinceNames.Count}."
            );
        }
    }

    private static string ReadProvinceName(
        JsonElement feature,
        int index)
    {
        if (feature.TryGetProperty(
                "properties",
                out JsonElement properties) &&
            properties.ValueKind ==
                JsonValueKind.Object)
        {
            string[] candidates =
            {
                "name",
                "Name",
                "NAME",
                "NAME_1",
                "province",
                "Province",
                "PROVINCE",
                "ten_tinh",
                "TEN_TINH",
                "ten",
                "Ten",
                "VARNAME_1"
            };

            foreach (
                string candidate
                in candidates)
            {
                if (properties.TryGetProperty(
                        candidate,
                        out JsonElement value) &&
                    value.ValueKind ==
                        JsonValueKind.String)
                {
                    string? text =
                        value.GetString();

                    if (!string.IsNullOrWhiteSpace(
                            text))
                    {
                        return text.Trim();
                    }
                }
            }

            /*
             * Last-resort: first non-empty string property.
             * The builder prints every resulting province name,
             * so this is easy to validate in terminal.
             */
            foreach (
                JsonProperty property
                in properties.EnumerateObject())
            {
                if (property.Value.ValueKind !=
                    JsonValueKind.String)
                {
                    continue;
                }

                string? text =
                    property.Value.GetString();

                if (!string.IsNullOrWhiteSpace(
                        text))
                {
                    return text.Trim();
                }
            }
        }

        return $"Province-{index:00}";
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

        if (!geometry.TryGetProperty(
                "coordinates",
                out JsonElement coordinates))
        {
            return null;
        }

        return typeElement.GetString() switch
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

        return _geometryFactory
            .CreatePolygon(
                rings[0],
                rings
                    .Skip(1)
                    .ToArray()
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
        List<ProvincePart> output,
        Polygon polygon,
        string provinceName)
    {
        Coordinate[] coordinates =
            polygon
                .ExteriorRing
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
            new ProvincePart
            {
                Kind = "province",
                Name = provinceName,
                Points = points
            }
        );
    }

    private sealed class ProvinceOverviewFile
    {
        public int Version { get; set; }

        public List<ProvincePart> Parts
        {
            get;
            set;
        } = new();
    }

    private sealed class ProvincePart
    {
        public string Kind
        {
            get;
            set;
        } = "province";

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
