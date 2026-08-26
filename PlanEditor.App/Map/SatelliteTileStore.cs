using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace PlanEditor.App.Map;

/// <summary>
/// Read-only local MBTiles satellite raster source.
///
/// MBTiles uses:
/// - Web Mercator / XYZ zoom levels
/// - TMS tile_row storage (Y is flipped vs XYZ)
///
/// The store loads tile bytes on background threads and keeps
/// a bounded in-memory Bitmap cache for smooth pan/zoom.
/// </summary>
public sealed class SatelliteTileStore : IDisposable
{
    private readonly string _path;
    private readonly SqliteConnection? _connection;

    private readonly object _dbSync = new();
    private readonly object _cacheSync = new();

    private readonly Dictionary<TileKey, Bitmap>
        _cache = new();

    private readonly HashSet<TileKey>
        _loading = new();

    private readonly Queue<TileKey>
        _cacheOrder = new();

    private const int MaxCachedTiles = 256;

    private bool _disposed;

    public event EventHandler? TileReady;

    public bool IsAvailable =>
        _connection != null &&
        !_disposed;

    public int MinZoom { get; private set; } = 0;
    public int MaxZoom { get; private set; } = 22;

    public string Path => _path;

    public SatelliteTileStore(
        string? path = null)
    {
        path ??=
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "MapData",
                "satellite.mbtiles"
            );

        _path = path;

        if (!File.Exists(_path))
        {
            Console.WriteLine(
                $"Satellite MBTiles chưa có: {_path}"
            );

            return;
        }

        try
        {
            _connection =
                new SqliteConnection(
                    $"Data Source={_path};Mode=ReadOnly"
                );

            _connection.Open();

            ReadZoomRange();

            Console.WriteLine(
                $"Satellite MBTiles loaded: {_path}"
            );

            Console.WriteLine(
                $"Satellite zoom range: {MinZoom}..{MaxZoom}"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Satellite MBTiles init failed: {ex}"
            );

            _connection?.Dispose();
            _connection = null;
        }
    }

    public Bitmap? GetOrRequestTile(
        int zoom,
        int x,
        int y)
    {
        if (!IsAvailable)
            return null;

        TileKey key =
            new(
                zoom,
                x,
                y
            );

        lock (_cacheSync)
        {
            if (_cache.TryGetValue(
                    key,
                    out Bitmap? cached))
            {
                return cached;
            }

            if (_loading.Contains(key))
                return null;

            _loading.Add(key);
        }

        _ =
            Task.Run(
                () => LoadTileWorker(key)
            );

        return null;
    }

    private void LoadTileWorker(
        TileKey key)
    {
        Bitmap? bitmap = null;

        try
        {
            byte[]? data =
                ReadTileBytes(
                    key.Zoom,
                    key.X,
                    key.Y
                );

            if (data != null &&
                data.Length > 0)
            {
                using var stream =
                    new MemoryStream(
                        data,
                        writable: false
                    );

                bitmap =
                    new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Satellite tile load failed " +
                $"z={key.Zoom} x={key.X} y={key.Y}: {ex.Message}"
            );
        }

        lock (_cacheSync)
        {
            _loading.Remove(key);

            if (_disposed)
            {
                bitmap?.Dispose();
                return;
            }

            if (bitmap != null)
            {
                _cache[key] =
                    bitmap;

                _cacheOrder.Enqueue(
                    key
                );

                TrimCache();
            }
        }

        if (bitmap != null)
        {
            TileReady?.Invoke(
                this,
                EventArgs.Empty
            );
        }
    }

    private byte[]? ReadTileBytes(
        int zoom,
        int x,
        int xyzY)
    {
        if (_connection == null)
            return null;

        if (zoom < MinZoom ||
            zoom > MaxZoom)
        {
            return null;
        }

        int tilesPerAxis =
            1 << zoom;

        if (x < 0 ||
            xyzY < 0 ||
            x >= tilesPerAxis ||
            xyzY >= tilesPerAxis)
        {
            return null;
        }

        // MBTiles stores TMS Y.
        int tmsY =
            tilesPerAxis -
            1 -
            xyzY;

        lock (_dbSync)
        {
            using SqliteCommand command =
                _connection.CreateCommand();

            command.CommandText =
                """
                SELECT tile_data
                FROM tiles
                WHERE zoom_level = $z
                  AND tile_column = $x
                  AND tile_row = $y
                LIMIT 1;
                """;

            command.Parameters.AddWithValue(
                "$z",
                zoom
            );

            command.Parameters.AddWithValue(
                "$x",
                x
            );

            command.Parameters.AddWithValue(
                "$y",
                tmsY
            );

            object? result =
                command.ExecuteScalar();

            return result as byte[];
        }
    }

    private void ReadZoomRange()
    {
        if (_connection == null)
            return;

        int? min =
            ReadMetadataInt(
                "minzoom"
            );

        int? max =
            ReadMetadataInt(
                "maxzoom"
            );

        if (!min.HasValue ||
            !max.HasValue)
        {
            lock (_dbSync)
            {
                using SqliteCommand command =
                    _connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT
                        MIN(zoom_level),
                        MAX(zoom_level)
                    FROM tiles;
                    """;

                using SqliteDataReader reader =
                    command.ExecuteReader();

                if (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        min =
                            reader.GetInt32(0);
                    }

                    if (!reader.IsDBNull(1))
                    {
                        max =
                            reader.GetInt32(1);
                    }
                }
            }
        }

        MinZoom =
            Math.Clamp(
                min ?? 0,
                0,
                22
            );

        MaxZoom =
            Math.Clamp(
                max ?? 22,
                MinZoom,
                22
            );
    }

    private int? ReadMetadataInt(
        string name)
    {
        if (_connection == null)
            return null;

        try
        {
            lock (_dbSync)
            {
                using SqliteCommand command =
                    _connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT value
                    FROM metadata
                    WHERE name = $name
                    LIMIT 1;
                    """;

                command.Parameters.AddWithValue(
                    "$name",
                    name
                );

                object? value =
                    command.ExecuteScalar();

                if (value == null)
                    return null;

                if (int.TryParse(
                        Convert.ToString(
                            value,
                            CultureInfo.InvariantCulture
                        ),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // metadata table is optional in some custom MBTiles.
        }

        return null;
    }

    private void TrimCache()
    {
        while (_cache.Count >
               MaxCachedTiles &&
               _cacheOrder.Count > 0)
        {
            TileKey oldest =
                _cacheOrder.Dequeue();

            if (_cache.Remove(
                    oldest,
                    out Bitmap? bitmap))
            {
                bitmap.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_cacheSync)
        {
            foreach (
                Bitmap bitmap
                in _cache.Values)
            {
                bitmap.Dispose();
            }

            _cache.Clear();
            _loading.Clear();
            _cacheOrder.Clear();
        }

        lock (_dbSync)
        {
            _connection?.Dispose();
        }
    }

    private readonly record struct TileKey(
        int Zoom,
        int X,
        int Y);
}
