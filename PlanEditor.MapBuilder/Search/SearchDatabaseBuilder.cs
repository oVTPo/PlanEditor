using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PlanEditor.MapBuilder.Admin;
using PlanEditor.MapBuilder.AdminSpatial;

namespace PlanEditor.MapBuilder.Search;

public sealed class SearchDatabaseBuilder
{
    public void Build(
        string geoJsonSeqPath,
        string adminJsonPath,
        string outputDatabase)
    {
        if (!File.Exists(geoJsonSeqPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy: {geoJsonSeqPath}"
            );
        }

        string? directory =
            Path.GetDirectoryName(outputDatabase);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputDatabase))
        {
            File.Delete(outputDatabase);
        }

        using var connection =
            new SqliteConnection(
                $"Data Source={outputDatabase}"
            );

        connection.Open();

        CreateSchema(connection);

        Console.WriteLine(
            "Importing current administrative dataset..."
        );

        var adminImporter =
            new AdminDatabaseImporter();

        adminImporter.Import(
            connection,
            adminJsonPath
        );

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
        INSERT INTO search_items
        (
            osm_id,
            name,
            normalized_name,
            category,
            subtype,
            longitude,
            latitude
        )
        VALUES
        (
            $osmId,
            $name,
            $normalizedName,
            $category,
            $subtype,
            $longitude,
            $latitude
        );
        """;

        command.Parameters.Add("$osmId", SqliteType.Text);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$normalizedName", SqliteType.Text);
        command.Parameters.Add("$category", SqliteType.Text);
        command.Parameters.Add("$subtype", SqliteType.Text);
        command.Parameters.Add("$longitude", SqliteType.Real);
        command.Parameters.Add("$latitude", SqliteType.Real);

        int imported = 0;

        foreach (string rawLine
                 in File.ReadLines(geoJsonSeqPath))
        {
            string line =
                rawLine.Trim();

            if (line.Length == 0)
                continue;

            if (line[0] == '\u001e')
            {
                line =
                    line[1..];
            }

            using JsonDocument json =
                JsonDocument.Parse(line);

            JsonElement root =
                json.RootElement;

            if (!TryReadSearchItem(
                    root,
                    out SearchItem? item))
            {
                continue;
            }

            command.Parameters["$osmId"].Value =
                item.OsmId;

            command.Parameters["$name"].Value =
                item.Name;

            command.Parameters["$normalizedName"].Value =
                item.NormalizedName;

            command.Parameters["$category"].Value =
                item.Category;

            command.Parameters["$subtype"].Value =
                item.Subtype;

            command.Parameters["$longitude"].Value =
                item.Longitude;

            command.Parameters["$latitude"].Value =
                item.Latitude;

            command.ExecuteNonQuery();

            imported++;

            if (imported % 10000 == 0)
            {
                Console.WriteLine(
                    $"Imported: {imported:N0}"
                );
            }
        }

        transaction.Commit();

        Console.WriteLine(
            $"Imported total: {imported:N0}"
        );

        BuildFts(connection);
        
        Console.WriteLine();
        Console.WriteLine(
            "Loading current administrative boundaries..."
        );

        AdminDataset dataset =
            LoadAdminDataset(
                adminJsonPath
            );

        var boundaryLoader =
            new AdminBoundaryLoader();

        List<AdminBoundary> boundaries =
            boundaryLoader.Load(
                "MapSource/vietnam-admin-boundaries.geojsonseq"
            );

        var matcher =
            new AdminBoundaryMatcher();

        matcher.Match(
            dataset,
            boundaries
        );

        var coordinateWriter =
            new AdminBoundaryCoordinateWriter();

        coordinateWriter.Write(
            connection,
            boundaries
        );

        var boundaryIndex =
            new AdminBoundaryIndex(
                boundaries
            );

        var joiner =
            new SearchAdminJoiner();

        joiner.Join(
            connection,
            boundaryIndex
        );

        BuildFts(connection);

        Console.WriteLine(
            $"Created: {outputDatabase}"
        );
    }

    private static void CreateSchema(
    SqliteConnection connection)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = """
        PRAGMA journal_mode = OFF;
        PRAGMA synchronous = OFF;

        CREATE TABLE admin_provinces
        (
            code TEXT PRIMARY KEY,

            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,

            type TEXT NOT NULL,
            former_names TEXT DEFAULT '',

            longitude REAL,
            latitude REAL
        );

        CREATE TABLE admin_communes
        (
            code TEXT PRIMARY KEY,

            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,

            type TEXT NOT NULL,

            province_code TEXT NOT NULL,

            former_names TEXT DEFAULT '',

            longitude REAL,
            latitude REAL,

            FOREIGN KEY(province_code)
                REFERENCES admin_provinces(code)
        );

        CREATE INDEX idx_admin_commune_province
        ON admin_communes(province_code);

        CREATE VIRTUAL TABLE admin_fts
        USING fts5
        (
            unit_type UNINDEXED,
            unit_code UNINDEXED,

            name,
            normalized_name,
            former_names,

            province_code UNINDEXED
        );

        CREATE TABLE search_items
        (
            id INTEGER PRIMARY KEY AUTOINCREMENT,

            osm_id TEXT,

            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,

            category TEXT,
            subtype TEXT,

            province_code TEXT DEFAULT '',
            commune_code TEXT DEFAULT '',

            longitude REAL NOT NULL,
            latitude REAL NOT NULL
        );

        CREATE INDEX idx_search_position
        ON search_items(
            longitude,
            latitude
        );

        CREATE INDEX idx_search_admin
        ON search_items(
            province_code,
            commune_code
        );

        CREATE VIRTUAL TABLE search_fts
        USING fts5
        (
            item_id UNINDEXED,
            name,
            normalized_name,
            search_text
        );
        """;

        command.ExecuteNonQuery();
    }

    private static void BuildFts(
        SqliteConnection connection)
    {
        using SqliteCommand clear =
            connection.CreateCommand();

        clear.CommandText =
            "DELETE FROM search_fts;";

        clear.ExecuteNonQuery();

        using SqliteCommand select =
            connection.CreateCommand();

        select.CommandText = """
        SELECT
            s.id,
            s.name,
            s.normalized_name,

            COALESCE(c.name, ''),
            COALESCE(c.normalized_name, ''),

            COALESCE(p.name, ''),
            COALESCE(p.normalized_name, '')

        FROM search_items s

        LEFT JOIN admin_communes c
            ON c.code = s.commune_code

        LEFT JOIN admin_provinces p
            ON p.code = s.province_code;
        """;

        using SqliteDataReader reader =
            select.ExecuteReader();

        var rows =
            new List<
                (
                    long Id,
                    string Name,
                    string NormalizedName,
                    string SearchText
                )
            >();

        while (reader.Read())
        {
            long id =
                reader.GetInt64(0);

            string name =
                reader.GetString(1);

            string normalizedName =
                reader.GetString(2);

            string communeName =
                reader.GetString(3);

            string communeNormalized =
                reader.GetString(4);

            string provinceName =
                reader.GetString(5);

            string provinceNormalized =
                reader.GetString(6);

            string searchText =
                string.Join(
                    " ",
                    new[]
                    {
                        normalizedName,
                        communeNormalized,
                        provinceNormalized,

                        NormalizeAdminPrefix(
                            communeName
                        ),

                        NormalizeAdminPrefix(
                            provinceName
                        )
                    }
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(x)
                    )
                );

            rows.Add(
                (
                    id,
                    name,
                    normalizedName,
                    searchText
                )
            );
        }

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using SqliteCommand insert =
            connection.CreateCommand();

        insert.Transaction =
            transaction;

        insert.CommandText = """
        INSERT INTO search_fts
        (
            item_id,
            name,
            normalized_name,
            search_text
        )
        VALUES
        (
            $id,
            $name,
            $normalized,
            $searchText
        );
        """;

        insert.Parameters.Add(
            "$id",
            SqliteType.Integer
        );

        insert.Parameters.Add(
            "$name",
            SqliteType.Text
        );

        insert.Parameters.Add(
            "$normalized",
            SqliteType.Text
        );

        insert.Parameters.Add(
            "$searchText",
            SqliteType.Text
        );

        foreach (var row in rows)
        {
            insert.Parameters["$id"].Value =
                row.Id;

            insert.Parameters["$name"].Value =
                row.Name;

            insert.Parameters["$normalized"].Value =
                row.NormalizedName;

            insert.Parameters["$searchText"].Value =
                row.SearchText;

            insert.ExecuteNonQuery();
        }

        transaction.Commit();

        Console.WriteLine(
            $"FTS indexed: {rows.Count:N0}"
        );
    }

    private static bool TryReadSearchItem(
        JsonElement feature,
        out SearchItem? item)
    {
        item = null;

        if (!feature.TryGetProperty(
                "properties",
                out JsonElement properties))
        {
            return false;
        }

        if (!properties.TryGetProperty(
                "name",
                out JsonElement nameElement))
        {
            return false;
        }

        string? name =
            nameElement.GetString();

        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!feature.TryGetProperty(
                "geometry",
                out JsonElement geometry))
        {
            return false;
        }

        if (!TryGetCenter(
                geometry,
                out double longitude,
                out double latitude))
        {
            return false;
        }

        DetectCategory(
            properties,
            out string category,
            out string subtype
        );

        string osmId = "";

        if (properties.TryGetProperty(
                "@id",
                out JsonElement idElement))
        {
            osmId =
                idElement.ToString();
        }

        item =
            new SearchItem
            {
                OsmId = osmId,

                Name = name,

                NormalizedName =
                    Normalize(name),

                Category =
                    category,

                Subtype =
                    subtype,

                Longitude =
                    longitude,

                Latitude =
                    latitude
            };

        return true;
    }

    private static void DetectCategory(
        JsonElement properties,
        out string category,
        out string subtype)
    {
        category = "place";
        subtype = "";

        string[] keys =
        {
            "highway",
            "place",
            "amenity",
            "tourism",
            "leisure",
            "shop",
            "office",
            "public_transport",
            "railway",
            "aeroway",
            "healthcare",
            "man_made"
        };

        foreach (string key in keys)
        {
            if (!properties.TryGetProperty(
                    key,
                    out JsonElement value))
            {
                continue;
            }

            subtype =
                value.ToString();

            category =
                key == "highway"
                    ? "road"
                    : key;

            return;
        }
    }

    private static bool TryGetCenter(
        JsonElement geometry,
        out double longitude,
        out double latitude)
    {
        longitude = 0;
        latitude = 0;

        if (!geometry.TryGetProperty(
                "type",
                out JsonElement typeElement))
        {
            return false;
        }

        if (!geometry.TryGetProperty(
                "coordinates",
                out JsonElement coordinates))
        {
            return false;
        }

        string? type =
            typeElement.GetString();

        var accumulator =
            new CoordinateAccumulator();

        switch (type)
        {
            case "Point":
                accumulator.Add(coordinates);
                break;

            case "LineString":
            case "MultiPoint":
                AddCoordinates(
                    coordinates,
                    1,
                    accumulator
                );
                break;

            case "Polygon":
            case "MultiLineString":
                AddCoordinates(
                    coordinates,
                    2,
                    accumulator
                );
                break;

            case "MultiPolygon":
                AddCoordinates(
                    coordinates,
                    3,
                    accumulator
                );
                break;

            default:
                return false;
        }

        if (!accumulator.HasValue)
            return false;

        longitude =
            (accumulator.MinX +
             accumulator.MaxX) / 2.0;

        latitude =
            (accumulator.MinY +
             accumulator.MaxY) / 2.0;

        return true;
    }

    private static void AddCoordinates(
        JsonElement element,
        int depth,
        CoordinateAccumulator accumulator)
    {
        if (depth == 0)
        {
            accumulator.Add(element);
            return;
        }

        foreach (
            JsonElement child
            in element.EnumerateArray())
        {
            AddCoordinates(
                child,
                depth - 1,
                accumulator
            );
        }
    }

    private static string Normalize(
        string value)
    {
        string text =
            value
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD
                );

        var builder =
            new StringBuilder();

        foreach (char c in text)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(c);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                c == 'đ'
                    ? 'd'
                    : c
            );
        }

        return builder
            .ToString()
            .Normalize(
                NormalizationForm.FormC
            );
    }

    private sealed class CoordinateAccumulator
    {
        public bool HasValue { get; private set; }

        public double MinX { get; private set; } =
            double.MaxValue;

        public double MinY { get; private set; } =
            double.MaxValue;

        public double MaxX { get; private set; } =
            double.MinValue;

        public double MaxY { get; private set; } =
            double.MinValue;

        public void Add(
            JsonElement coordinate)
        {
            if (
                coordinate.ValueKind !=
                JsonValueKind.Array ||
                coordinate.GetArrayLength() < 2)
            {
                return;
            }

            double x =
                coordinate[0].GetDouble();

            double y =
                coordinate[1].GetDouble();

            HasValue = true;

            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);

            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }
    }

    private static AdminDataset LoadAdminDataset(
    string path)
    {
        string json =
            File.ReadAllText(path);

        AdminDataset? dataset =
            JsonSerializer.Deserialize<AdminDataset>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

        if (dataset == null)
        {
            throw new InvalidOperationException(
                "Không load được admin dataset."
            );
        }

        return dataset;
    }

    private static string NormalizeAdminPrefix(
    string value)
    {
        string normalized =
            Normalize(value);

        string[] prefixes =
        {
            "thanh pho ",
            "tinh ",
            "phuong ",
            "xa ",
            "dac khu "
        };

        foreach (string prefix in prefixes)
        {
            if (normalized.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return normalized[
                    prefix.Length..
                ];
            }
        }

        return normalized;
    }
}