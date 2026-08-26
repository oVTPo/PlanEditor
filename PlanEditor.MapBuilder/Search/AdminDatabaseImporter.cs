using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PlanEditor.MapBuilder.Admin;

namespace PlanEditor.MapBuilder.Search;

public sealed class AdminDatabaseImporter
{
    public void Import(
        SqliteConnection connection,
        string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy admin dataset: {jsonPath}"
            );
        }

        string json =
            File.ReadAllText(jsonPath);

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
                "Không đọc được admin-vn-current.json"
            );
        }

        if (dataset.Provinces.Count != 34)
        {
            throw new InvalidOperationException(
                $"Admin province count: " +
                $"{dataset.Provinces.Count}/34"
            );
        }

        if (dataset.CommuneCount != 3321)
        {
            throw new InvalidOperationException(
                $"Admin commune count: " +
                $"{dataset.CommuneCount}/3321"
            );
        }

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        ImportProvinces(
            connection,
            transaction,
            dataset
        );

        ImportCommunes(
            connection,
            transaction,
            dataset
        );

        transaction.Commit();

        BuildFts(connection);

        Console.WriteLine();
        Console.WriteLine(
            $"Admin provinces: " +
            $"{dataset.Provinces.Count}"
        );

        Console.WriteLine(
            $"Admin communes : " +
            $"{dataset.CommuneCount}"
        );
    }

    private static void ImportProvinces(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AdminDataset dataset)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText = """
        INSERT INTO admin_provinces
        (
            code,
            name,
            normalized_name,
            type,
            former_names
        )
        VALUES
        (
            $code,
            $name,
            $normalized,
            $type,
            $former
        );
        """;

        command.Parameters.Add(
            "$code",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$name",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$normalized",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$type",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$former",
            SqliteType.Text
        );

        foreach (
            AdminProvince province
            in dataset.Provinces)
        {
            command.Parameters["$code"].Value =
                province.Code;

            command.Parameters["$name"].Value =
                province.Name;

            command.Parameters["$normalized"].Value =
                Normalize(province.Name);

            command.Parameters["$type"].Value =
                province.Type;

            command.Parameters["$former"].Value =
                string.Join(
                    " ",
                    province.FormerNames
                );

            command.ExecuteNonQuery();
        }
    }

    private static void ImportCommunes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AdminDataset dataset)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText = """
        INSERT INTO admin_communes
        (
            code,
            name,
            normalized_name,
            type,
            province_code,
            former_names
        )
        VALUES
        (
            $code,
            $name,
            $normalized,
            $type,
            $provinceCode,
            $former
        );
        """;

        command.Parameters.Add(
            "$code",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$name",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$normalized",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$type",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$provinceCode",
            SqliteType.Text
        );

        command.Parameters.Add(
            "$former",
            SqliteType.Text
        );

        foreach (
            AdminProvince province
            in dataset.Provinces)
        {
            foreach (
                AdminCommune commune
                in province.Communes)
            {
                command.Parameters["$code"].Value =
                    commune.Code;

                command.Parameters["$name"].Value =
                    commune.Name;

                command.Parameters["$normalized"].Value =
                    Normalize(commune.Name);

                command.Parameters["$type"].Value =
                    commune.Type;

                command.Parameters["$provinceCode"].Value =
                    province.Code;

                command.Parameters["$former"].Value =
                    string.Join(
                        " ",
                        commune.FormerNames
                    );

                command.ExecuteNonQuery();
            }
        }
    }

    private static void BuildFts(
        SqliteConnection connection)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = """
        INSERT INTO admin_fts
        (
            unit_type,
            unit_code,
            name,
            normalized_name,
            former_names,
            province_code
        )

        SELECT
            'province',
            code,
            name,
            normalized_name,
            former_names,
            code
        FROM admin_provinces;

        INSERT INTO admin_fts
        (
            unit_type,
            unit_code,
            name,
            normalized_name,
            former_names,
            province_code
        )

        SELECT
            'commune',
            code,
            name,
            normalized_name,
            former_names,
            province_code
        FROM admin_communes;
        """;

        command.ExecuteNonQuery();
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
                CharUnicodeInfo.GetUnicodeCategory(c);

            if (
                category ==
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
}