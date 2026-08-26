using System.Text.Json;
using NetTopologySuite.Geometries;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminBoundaryLoader
{
    private readonly GeometryFactory _geometryFactory =
        new(new PrecisionModel(), 4326);

    public List<AdminBoundary> Load(
        string geoJsonSeqPath)
    {
        if (!File.Exists(geoJsonSeqPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy boundary file: {geoJsonSeqPath}"
            );
        }

        var result =
            new List<AdminBoundary>();

        int read = 0;
        int accepted = 0;

        foreach (
            string rawLine
            in File.ReadLines(geoJsonSeqPath))
        {
            string line =
                rawLine.Trim();

            if (line.Length == 0)
                continue;

            if (line[0] == '\u001e')
                line = line[1..];

            read++;

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(line);

                JsonElement root =
                    document.RootElement;

                if (!TryReadBoundary(
                        root,
                        out AdminBoundary? boundary))
                {
                    continue;
                }

                result.Add(boundary);

                accepted++;
            }
            catch
            {
                // Không để một relation lỗi phá toàn bộ build.
            }
        }

        Console.WriteLine(
            $"Boundary records read : {read:N0}"
        );

        Console.WriteLine(
            $"Boundary polygons kept: {accepted:N0}"
        );

        return result;
    }

    private bool TryReadBoundary(
        JsonElement feature,
        out AdminBoundary? boundary)
    {
        boundary = null;

        if (!feature.TryGetProperty(
                "properties",
                out JsonElement properties))
        {
            return false;
        }

        string boundaryType =
            ReadString(
                properties,
                "boundary"
            );

        if (!string.Equals(
                boundaryType,
                "administrative",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string name =
            ReadString(
                properties,
                "name"
            );

        if (string.IsNullOrWhiteSpace(name))
            return false;

        string adminLevel =
            ReadString(
                properties,
                "admin_level"
            );

        if (!feature.TryGetProperty(
                "geometry",
                out JsonElement geometryElement))
        {
            return false;
        }

        Geometry? geometry =
            ReadGeometry(
                geometryElement
            );

        if (geometry == null ||
            geometry.IsEmpty)
        {
            return false;
        }

        boundary =
            new AdminBoundary
            {
                Name = name,
                NormalizedName =
                    SearchTextNormalizer.Normalize(
                        name
                    ),
                AdminLevel =
                    adminLevel,
                Geometry =
                    geometry
            };

        return true;
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

        string? type =
            typeElement.GetString();

        return type switch
        {
            "Polygon" =>
                ReadPolygon(coordinates),

            "MultiPolygon" =>
                ReadMultiPolygon(coordinates),

            _ => null
        };
    }

    private Polygon? ReadPolygon(
        JsonElement coordinates)
    {
        var rings =
            coordinates
                .EnumerateArray()
                .ToList();

        if (rings.Count == 0)
            return null;

        LinearRing? shell =
            ReadRing(
                rings[0]
            );

        if (shell == null)
            return null;

        var holes =
            new List<LinearRing>();

        for (int i = 1;
             i < rings.Count;
             i++)
        {
            LinearRing? hole =
                ReadRing(
                    rings[i]
                );

            if (hole != null)
                holes.Add(hole);
        }

        try
        {
            return _geometryFactory
                .CreatePolygon(
                    shell,
                    holes.ToArray()
                );
        }
        catch
        {
            return null;
        }
    }

    private MultiPolygon?
        ReadMultiPolygon(
            JsonElement coordinates)
    {
        var polygons =
            new List<Polygon>();

        foreach (
            JsonElement polygonElement
            in coordinates.EnumerateArray())
        {
            Polygon? polygon =
                ReadPolygon(
                    polygonElement
                );

            if (polygon != null)
                polygons.Add(polygon);
        }

        if (polygons.Count == 0)
            return null;

        return _geometryFactory
            .CreateMultiPolygon(
                polygons.ToArray()
            );
    }

    private LinearRing? ReadRing(
        JsonElement ring)
    {
        var coordinates =
            new List<Coordinate>();

        foreach (
            JsonElement point
            in ring.EnumerateArray())
        {
            if (
                point.ValueKind !=
                    JsonValueKind.Array ||
                point.GetArrayLength() < 2)
            {
                continue;
            }

            coordinates.Add(
                new Coordinate(
                    point[0].GetDouble(),
                    point[1].GetDouble()
                )
            );
        }

        if (coordinates.Count < 3)
            return null;

        Coordinate first =
            coordinates[0];

        Coordinate last =
            coordinates[^1];

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

        try
        {
            return _geometryFactory
                .CreateLinearRing(
                    coordinates.ToArray()
                );
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(
        JsonElement properties,
        string key)
    {
        if (!properties.TryGetProperty(
                key,
                out JsonElement element))
        {
            return "";
        }

        if (element.ValueKind ==
            JsonValueKind.Null)
        {
            return "";
        }

        return element.ToString().Trim();
    }
}