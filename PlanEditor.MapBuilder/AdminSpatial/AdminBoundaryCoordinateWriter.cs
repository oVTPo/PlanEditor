using Microsoft.Data.Sqlite;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminBoundaryCoordinateWriter
{
    public void Write(
        SqliteConnection connection,
        IEnumerable<AdminBoundary> boundaries)
    {
        List<AdminBoundary> matched =
            boundaries
                .Where(
                    b =>
                        !string.IsNullOrWhiteSpace(
                            b.ProvinceCode
                        )
                )
                .ToList();

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        WriteProvinces(
            connection,
            transaction,
            matched
        );

        WriteCommunes(
            connection,
            transaction,
            matched
        );

        transaction.Commit();
    }

    private static void WriteProvinces(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<AdminBoundary> boundaries)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
        UPDATE admin_provinces
        SET
            longitude = $longitude,
            latitude = $latitude
        WHERE code = $code;
        """;

        command.Parameters.Add(
            "$longitude",
            SqliteType.Real
        );

        command.Parameters.Add(
            "$latitude",
            SqliteType.Real
        );

        command.Parameters.Add(
            "$code",
            SqliteType.Text
        );

        IEnumerable<AdminBoundary> provinces =
            boundaries
                .Where(
                    b =>
                        string.IsNullOrWhiteSpace(
                            b.CommuneCode
                        )
                )
                .GroupBy(
                    b => b.ProvinceCode
                )
                .Select(
                    g =>
                        g.OrderByDescending(
                                b => b.Geometry.Area
                            )
                            .First()
                );

        int count = 0;

        foreach (AdminBoundary boundary in provinces)
        {
            var point =
                boundary.Geometry
                    .PointOnSurface;

            command.Parameters[
                "$longitude"
            ].Value =
                point.X;

            command.Parameters[
                "$latitude"
            ].Value =
                point.Y;

            command.Parameters[
                "$code"
            ].Value =
                boundary.ProvinceCode;

            count +=
                command.ExecuteNonQuery();
        }

        Console.WriteLine(
            $"Province coordinates: {count:N0}"
        );
    }

    private static void WriteCommunes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<AdminBoundary> boundaries)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
        UPDATE admin_communes
        SET
            longitude = $longitude,
            latitude = $latitude
        WHERE code = $code;
        """;

        command.Parameters.Add(
            "$longitude",
            SqliteType.Real
        );

        command.Parameters.Add(
            "$latitude",
            SqliteType.Real
        );

        command.Parameters.Add(
            "$code",
            SqliteType.Text
        );

        int count = 0;

        foreach (
            AdminBoundary boundary
            in boundaries.Where(
                b =>
                    !string.IsNullOrWhiteSpace(
                        b.CommuneCode
                    )
            ))
        {
            /*
             * Không dùng Centroid.
             *
             * Với polygon lõm hoặc quần đảo,
             * centroid có thể nằm ngoài polygon.
             *
             * PointOnSurface bảo đảm điểm
             * nằm trong khu vực hành chính.
             */
            var point =
                boundary.Geometry
                    .PointOnSurface;

            command.Parameters[
                "$longitude"
            ].Value =
                point.X;

            command.Parameters[
                "$latitude"
            ].Value =
                point.Y;

            command.Parameters[
                "$code"
            ].Value =
                boundary.CommuneCode;

            count +=
                command.ExecuteNonQuery();
        }

        Console.WriteLine(
            $"Commune coordinates : {count:N0}"
        );
    }
}