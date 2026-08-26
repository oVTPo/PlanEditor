using Microsoft.Data.Sqlite;
using PlanEditor.MapBuilder.AdminSpatial;

namespace PlanEditor.MapBuilder.Search;

public sealed class SearchAdminJoiner
{
    public void Join(
        SqliteConnection connection,
        AdminBoundaryIndex index)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Assigning current administrative units..."
        );

        var updates =
            new List<
                (
                    long Id,
                    string ProvinceCode,
                    string CommuneCode
                )
            >();

        using (
            SqliteCommand select =
                connection.CreateCommand())
        {
            select.CommandText = """
            SELECT
                id,
                longitude,
                latitude
            FROM search_items;
            """;

            using SqliteDataReader reader =
                select.ExecuteReader();

            int scanned = 0;
            int matched = 0;

            while (reader.Read())
            {
                long id =
                    reader.GetInt64(0);

                double longitude =
                    reader.GetDouble(1);

                double latitude =
                    reader.GetDouble(2);

                AdminBoundary? boundary =
                    index.Find(
                        longitude,
                        latitude
                    );

                scanned++;

                if (boundary == null)
                    continue;

                matched++;

                updates.Add(
                    (
                        id,
                        boundary.ProvinceCode,
                        boundary.CommuneCode
                    )
                );

                if (scanned % 25000 == 0)
                {
                    Console.WriteLine(
                        $"Spatial join: " +
                        $"{scanned:N0} scanned, " +
                        $"{matched:N0} matched"
                    );
                }
            }

            Console.WriteLine(
                $"Spatial scan completed: " +
                $"{scanned:N0}"
            );

            Console.WriteLine(
                $"Spatial matches       : " +
                $"{matched:N0}"
            );
        }

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using SqliteCommand update =
            connection.CreateCommand();

        update.Transaction =
            transaction;

        update.CommandText = """
        UPDATE search_items
        SET
            province_code = $province,
            commune_code = $commune
        WHERE id = $id;
        """;

        update.Parameters.Add(
            "$province",
            SqliteType.Text
        );

        update.Parameters.Add(
            "$commune",
            SqliteType.Text
        );

        update.Parameters.Add(
            "$id",
            SqliteType.Integer
        );

        int count = 0;

        foreach (
            var item
            in updates)
        {
            update.Parameters[
                "$province"
            ].Value =
                item.ProvinceCode;

            update.Parameters[
                "$commune"
            ].Value =
                item.CommuneCode;

            update.Parameters[
                "$id"
            ].Value =
                item.Id;

            update.ExecuteNonQuery();

            count++;
        }

        transaction.Commit();

        Console.WriteLine(
            $"Database rows updated: " +
            $"{count:N0}"
        );
    }
}