using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using PlanEditor.Core.Geometry;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminOverviewBuilder
{
    // Province overview: khoảng 500-600 m.
    private const double ProvinceToleranceDegrees =
        0.005;

    // National overview: giữ chi tiết bờ biển tốt hơn.
    // Khoảng 100-120 m theo vĩ độ.
    private const double CountryToleranceDegrees =
        0.003;

    public void Build(
        string boundaryGeoJsonSeqPath,
        string outputJsonPath)
    {
        var loader =
            new AdminBoundaryLoader();

        List<AdminBoundary> source =
            loader.Load(
                boundaryGeoJsonSeqPath
            );

        List<AdminBoundary> provinces =
            source
                .Where(
                    boundary =>
                        boundary.AdminLevel == "4"
                )
                .ToList();

        Console.WriteLine(
            $"Overview provinces : {provinces.Count:N0}"
        );

        if (provinces.Count == 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy boundary admin_level=4."
            );
        }

        var parts =
            new List<OverviewBoundaryPart>();

        /*
         * Province layer.
         */
        foreach (AdminBoundary province in provinces)
        {
            AddGeometry(
                parts,
                province.Geometry,
                "province",
                province.Name,
                ProvinceToleranceDegrees
            );
        }

        /*
         * National layer:
         * KHÔNG dùng admin_level=2 trực tiếp nữa.
         *
         * Union 34 tỉnh để tạo hình Việt Nam theo
         * chính geometry cấp tỉnh đã match đúng.
         * Cách này tránh trường hợp relation country
         * bị export quá thô thành dạng đa giác lạ.
         */
        Geometry national =
            provinces[0].Geometry;

        for (
            int i = 1;
            i < provinces.Count;
            i++)
        {
            try
            {
                national =
                    national.Union(
                        provinces[i].Geometry
                    );
            }
            catch
            {
                /*
                 * Một geometry lỗi không làm hỏng build.
                 * Province vẫn được giữ trong layer tỉnh.
                 */
            }
        }

        AddGeometry(
            parts,
            national,
            "country",
            "Việt Nam",
            CountryToleranceDegrees
        );

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

        var payload =
            new OverviewBoundaryFile
            {
                Version = 2,
                Parts = parts
            };

        string json =
            JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions
                {
                    WriteIndented = false
                }
            );

        File.WriteAllText(
            outputJsonPath,
            json
        );

        int countryParts =
            parts.Count(
                part =>
                    part.Kind == "country"
            );

        int provinceParts =
            parts.Count(
                part =>
                    part.Kind == "province"
            );

        long countryPoints =
            parts
                .Where(
                    part =>
                        part.Kind == "country"
                )
                .Sum(
                    part =>
                        (long)part.Points.Length
                );

        Console.WriteLine();
        Console.WriteLine(
            "===== VIETNAM OVERVIEW V2 ====="
        );

        Console.WriteLine(
            $"Country parts  : {countryParts:N0}"
        );

        Console.WriteLine(
            $"Province parts : {provinceParts:N0}"
        );

        Console.WriteLine(
            $"Country points : {countryPoints:N0}"
        );

        Console.WriteLine(
            $"Total parts    : {parts.Count:N0}"
        );

        Console.WriteLine(
            $"Created        : {outputJsonPath}"
        );
    }

    private static void AddGeometry(
        List<OverviewBoundaryPart> output,
        Geometry geometry,
        string kind,
        string name,
        double toleranceDegrees)
    {
        Geometry simplified;

        try
        {
            simplified =
                TopologyPreservingSimplifier.Simplify(
                    geometry,
                    toleranceDegrees
                );
        }
        catch
        {
            simplified = geometry;
        }

        switch (simplified)
        {
            case Polygon polygon:
                AddPolygon(
                    output,
                    polygon,
                    kind,
                    name
                );
                break;

            case MultiPolygon multiPolygon:
                for (
                    int i = 0;
                    i < multiPolygon.NumGeometries;
                    i++)
                {
                    if (
                        multiPolygon.GetGeometryN(i)
                        is Polygon child)
                    {
                        AddPolygon(
                            output,
                            child,
                            kind,
                            name
                        );
                    }
                }

                break;

            case GeometryCollection collection:
                for (
                    int i = 0;
                    i < collection.NumGeometries;
                    i++)
                {
                    AddGeometry(
                        output,
                        collection.GetGeometryN(i),
                        kind,
                        name,
                        toleranceDegrees
                    );
                }

                break;
        }
    }

    private static void AddPolygon(
        List<OverviewBoundaryPart> output,
        Polygon polygon,
        string kind,
        string name)
    {
        Coordinate[] coordinates =
            polygon.ExteriorRing.Coordinates;

        if (coordinates.Length < 4)
            return;

        var points =
            new List<double[]>(
                coordinates.Length
            );

        foreach (Coordinate coordinate in coordinates)
        {
            WorldPoint world =
                WebMercator.Project(
                    coordinate.X,
                    coordinate.Y
                );

            points.Add(
                new[]
                {
                    world.X,
                    world.Y
                }
            );
        }

        if (points.Count < 4)
            return;

        output.Add(
            new OverviewBoundaryPart
            {
                Kind = kind,
                Name = name,
                Points = points.ToArray()
            }
        );
    }

    private sealed class OverviewBoundaryFile
    {
        public int Version { get; set; }

        public List<OverviewBoundaryPart> Parts
        {
            get;
            set;
        } = new();
    }

    private sealed class OverviewBoundaryPart
    {
        public string Kind { get; set; } = "";

        public string Name { get; set; } = "";

        public double[][] Points
        {
            get;
            set;
        } = Array.Empty<double[]>();
    }
}
