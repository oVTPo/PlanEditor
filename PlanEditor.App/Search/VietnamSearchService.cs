using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PlanEditor.App.Search;

public sealed class VietnamSearchService : IDisposable
{
    private readonly SqliteConnection _connection;

    public VietnamSearchService(string? databasePath = null)
    {
        databasePath ??= Path.Combine(
            AppContext.BaseDirectory,
            "MapData",
            "vietnam-search.db"
        );

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy search database: {databasePath}",
                databasePath
            );
        }

        _connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly"
        );

        _connection.Open();
    }

    public List<VietnamSearchResult> Search(
        string query,
        int limit = 20)
    {
        string normalized = Normalize(query);

        if (normalized.Length < 2)
            return new List<VietnamSearchResult>();

        string ftsQuery = BuildFtsQuery(normalized);

        if (string.IsNullOrWhiteSpace(ftsQuery))
            return new List<VietnamSearchResult>();

        var results = new List<VietnamSearchResult>();

        SearchAdministrativeUnits(ftsQuery, results);
        SearchOsmItems(ftsQuery, results);

        return RankResults(results, normalized)
            .GroupBy(result => new
            {
                result.Name,
                result.Category,
                result.Longitude,
                result.Latitude
            })
            .Select(group => group.First())
            .Take(limit)
            .ToList();
    }

    private void SearchOsmItems(
        string ftsQuery,
        List<VietnamSearchResult> results)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
        SELECT
            s.id,
            s.name,
            COALESCE(s.category, ''),
            COALESCE(s.subtype, ''),
            COALESCE(s.province_code, ''),
            COALESCE(p.name, ''),
            COALESCE(s.commune_code, ''),
            COALESCE(c.name, ''),
            s.longitude,
            s.latitude,
            bm25(search_fts) AS rank
        FROM search_fts
        JOIN search_items s
            ON s.id = search_fts.item_id
        LEFT JOIN admin_provinces p
            ON p.code = s.province_code
        LEFT JOIN admin_communes c
            ON c.code = s.commune_code
        WHERE search_fts MATCH $query
        ORDER BY rank
        LIMIT 100;
        """;

        command.Parameters.AddWithValue("$query", ftsQuery);

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(
                new VietnamSearchResult
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Subtype = reader.GetString(3),
                    ProvinceCode = reader.GetString(4),
                    ProvinceName = reader.GetString(5),
                    CommuneCode = reader.GetString(6),
                    CommuneName = reader.GetString(7),
                    Longitude = reader.GetDouble(8),
                    Latitude = reader.GetDouble(9),
                    Score = reader.IsDBNull(10)
                        ? 0
                        : reader.GetDouble(10)
                }
            );
        }
    }

    private void SearchAdministrativeUnits(
        string ftsQuery,
        List<VietnamSearchResult> results)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
        SELECT
            f.unit_type,
            f.unit_code,
            f.name,
            f.province_code,
            bm25(admin_fts) AS rank
        FROM admin_fts f
        WHERE admin_fts MATCH $query
        ORDER BY rank
        LIMIT 60;
        """;

        command.Parameters.AddWithValue("$query", ftsQuery);

        using SqliteDataReader reader = command.ExecuteReader();

        var rows = new List<AdminFtsRow>();

        while (reader.Read())
        {
            rows.Add(
                new AdminFtsRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
                )
            );
        }

        foreach (AdminFtsRow row in rows)
        {
            if (string.Equals(
                    row.UnitType,
                    "province",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddProvince(row, results);
            }
            else if (string.Equals(
                         row.UnitType,
                         "commune",
                         StringComparison.OrdinalIgnoreCase))
            {
                AddCommune(row, results);
            }
        }
    }

    private void AddProvince(
        AdminFtsRow row,
        List<VietnamSearchResult> results)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
        SELECT longitude, latitude
        FROM admin_provinces
        WHERE code = $code;
        """;

        command.Parameters.AddWithValue("$code", row.UnitCode);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
            return;

        results.Add(
            new VietnamSearchResult
            {
                Id = ToAdministrativeId(row.UnitCode, true),
                Name = row.Name,
                Category = "province",
                Subtype = "province",
                ProvinceCode = row.UnitCode,
                ProvinceName = row.Name,
                CommuneCode = string.Empty,
                CommuneName = string.Empty,
                Longitude = reader.GetDouble(0),
                Latitude = reader.GetDouble(1),
                Score = row.Rank
            }
        );
    }

    private void AddCommune(
        AdminFtsRow row,
        List<VietnamSearchResult> results)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
        SELECT
            c.longitude,
            c.latitude,
            c.province_code,
            COALESCE(p.name, '')
        FROM admin_communes c
        LEFT JOIN admin_provinces p
            ON p.code = c.province_code
        WHERE c.code = $code;
        """;

        command.Parameters.AddWithValue("$code", row.UnitCode);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
            return;

        string provinceCode = reader.IsDBNull(2)
            ? row.ProvinceCode
            : reader.GetString(2);

        results.Add(
            new VietnamSearchResult
            {
                Id = ToAdministrativeId(row.UnitCode, false),
                Name = row.Name,
                Category = "commune",
                Subtype = "commune",
                ProvinceCode = provinceCode,
                ProvinceName = reader.GetString(3),
                CommuneCode = row.UnitCode,
                CommuneName = row.Name,
                Longitude = reader.GetDouble(0),
                Latitude = reader.GetDouble(1),
                Score = row.Rank
            }
        );
    }

    private static IEnumerable<VietnamSearchResult> RankResults(
        IEnumerable<VietnamSearchResult> results,
        string query)
    {
        string[] tokens = query.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries
        );

        return results
            .Select(result =>
            {
                string name = Normalize(result.Name);
                string province = Normalize(result.ProvinceName);
                string commune = Normalize(result.CommuneName);

                double score = 0;

                if (name == query)
                    score += 2000;
                else if (name.StartsWith(query, StringComparison.Ordinal))
                    score += 1000;

                foreach (string token in tokens)
                {
                    if (name.Contains(token, StringComparison.Ordinal))
                        score += 120;

                    if (commune.Contains(token, StringComparison.Ordinal))
                        score += 80;

                    if (province.Contains(token, StringComparison.Ordinal))
                        score += 90;
                }

                score += result.Category switch
                {
                    "province" => 90,
                    "commune" => 80,
                    "road" => 70,
                    "place" => 50,
                    "amenity" => 40,
                    _ => 10
                };

                if (!string.IsNullOrWhiteSpace(result.CommuneCode))
                    score += 15;

                return new
                {
                    Result = result,
                    Score = score
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Result.Score)
            .Select(item => item.Result);
    }

    private static string BuildFtsQuery(string normalized)
    {
        string[] tokens = normalized.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries
        );

        return string.Join(
            " AND ",
            tokens.Select(token => EscapeFtsToken(token) + "*")
        );
    }

    private static string EscapeFtsToken(string token)
    {
        return "\"" +
               token.Replace("\"", "\"\"") +
               "\"";
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (char c in text)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(c);

            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(c == 'đ' ? 'd' : c);
        }

        return string.Join(
            " ",
            builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
        );
    }

    private static long ToAdministrativeId(
        string code,
        bool province)
    {
        if (long.TryParse(code, out long numericCode))
        {
            return province
                ? -1_000_000L - numericCode
                : -2_000_000L - numericCode;
        }

        long hash = code.GetHashCode();
        return province
            ? -3_000_000L - Math.Abs(hash)
            : -4_000_000L - Math.Abs(hash);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private sealed record AdminFtsRow(
        string UnitType,
        string UnitCode,
        string Name,
        string ProvinceCode,
        double Rank
    );
}
