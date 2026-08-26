using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;

namespace PlanEditor.App.Map;

public sealed class AdminBoundaryStore
{
    private readonly string _overviewPath;
    private readonly string _nationalMasterPath;
    private readonly string _nationalExtraPath;
    private readonly object _sync = new();

    private OverviewBoundaryFile? _source;
    private MapDocument? _provinceOverview;
    private MapDocument? _nationalOverview;

    public AdminBoundaryStore(
        string? overviewPath = null,
        string? nationalMasterPath = null,
        string? nationalExtraPath = null)
    {
        overviewPath ??=
            Path.Combine(
                AppContext.BaseDirectory,
                "MapData",
                "vietnam-overview.json"
            );

        nationalMasterPath ??=
            Path.Combine(
                AppContext.BaseDirectory,
                "MapData",
                "vietnam-national.json"
            );

        nationalExtraPath ??=
            Path.Combine(
                AppContext.BaseDirectory,
                "MapData",
                "vietnam-national-extra.json"
            );

        _overviewPath = overviewPath;
        _nationalMasterPath = nationalMasterPath;
        _nationalExtraPath = nationalExtraPath;
    }

    public MapDocument LoadProvinceOverview()
    {
        lock (_sync)
        {
            if (_provinceOverview != null)
                return _provinceOverview;

            OverviewBoundaryFile source =
                LoadSource();

            /*
             * PROVINCE OVERVIEW chỉ dùng 34 polygon tỉnh.
             *
             * Không đưa country polygon cũ từ vietnam-overview.json
             * vào layer này nữa. Country geometry cũ có thể tạo
             * các cạnh đóng polygon dài/chéo và che sai ranh tỉnh
             * ở khoảng 300-450 m/px.
             *
             * National map đã có nguồn riêng:
             * vietnam-national.json
             */
            _provinceOverview =
                BuildDocument(
                    source.Parts,
                    includeProvince: true,
                    includeNational: false
                );

            Console.WriteLine(
                $"Province overview loaded: " +
                $"{_provinceOverview.Features.Count:N0} boundary parts"
            );

            return _provinceOverview;
        }
    }

    public MapDocument LoadNationalOverview()
    {
        lock (_sync)
        {
            if (_nationalOverview != null)
                return _nationalOverview;

            var nationalParts =
                new List<OverviewBoundaryPart>();

            /*
             * Ưu tiên master national geometry độc lập.
             *
             * Khi có:
             * MapData/vietnam-national.json
             *
             * đây sẽ là nguồn duy nhất cho hình dạng quốc gia,
             * phù hợp để thay bằng dataset hành chính chuẩn.
             */
            if (File.Exists(_nationalMasterPath))
            {
                string masterJson =
                    File.ReadAllText(
                        _nationalMasterPath
                    );

                OverviewBoundaryFile? master =
                    JsonSerializer.Deserialize<OverviewBoundaryFile>(
                        masterJson,
                        JsonOptions()
                    );

                if (master != null)
                {
                    nationalParts.AddRange(
                        master.Parts
                    );

                    Console.WriteLine(
                        "National master geometry loaded."
                    );
                }
            }
            else
            {
                /*
                 * Fallback hiện tại:
                 * dùng country geometry trong vietnam-overview.json.
                 *
                 * Khi có bộ dữ liệu chuẩn, chỉ cần đặt
                 * vietnam-national.json vào MapData.
                 */
                OverviewBoundaryFile source =
                    LoadSource();

                foreach (
                    OverviewBoundaryPart part
                    in source.Parts)
                {
                    if (IsNationalKind(part.Kind))
                    {
                        nationalParts.Add(part);
                    }
                }

                Console.WriteLine(
                    "National master chưa có; " +
                    "đang dùng vietnam-overview.json."
                );
            }

            /*
             * File bổ sung dành cho đảo/geometry bổ sung
             * từ nguồn được đơn vị chấp nhận.
             */
            if (File.Exists(_nationalExtraPath))
            {
                string extraJson =
                    File.ReadAllText(
                        _nationalExtraPath
                    );

                OverviewBoundaryFile? extra =
                    JsonSerializer.Deserialize<OverviewBoundaryFile>(
                        extraJson,
                        JsonOptions()
                    );

                if (extra != null)
                {
                    nationalParts.AddRange(
                        extra.Parts
                    );
                }
            }
            else
            {
                Console.WriteLine(
                    "National extra map chưa có; " +
                    "đang dùng geometry quốc gia từ vietnam-overview.json."
                );
            }

            _nationalOverview =
                BuildDocument(
                    nationalParts,
                    includeProvince: false,
                    includeNational: true
                );

            Console.WriteLine(
                $"National overview loaded: " +
                $"{_nationalOverview.Features.Count:N0} boundary parts"
            );

            return _nationalOverview;
        }
    }

    public bool TryGetNationalBounds(
        out WorldBounds bounds)
    {
        MapDocument national =
            LoadNationalOverview();

        if (!national.TryGetBounds(
                out WorldPoint min,
                out WorldPoint max))
        {
            bounds = default;
            return false;
        }

        bounds =
            new WorldBounds(
                min.X,
                min.Y,
                max.X,
                max.Y
            );

        return true;
    }

    private OverviewBoundaryFile LoadSource()
    {
        if (_source != null)
            return _source;

        if (!File.Exists(_overviewPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy overview map: {_overviewPath}"
            );
        }

        string json =
            File.ReadAllText(
                _overviewPath
            );

        _source =
            JsonSerializer.Deserialize<OverviewBoundaryFile>(
                json,
                JsonOptions()
            );

        if (_source == null)
        {
            throw new InvalidDataException(
                $"Không đọc được overview map: {_overviewPath}"
            );
        }

        return _source;
    }

    private static MapDocument BuildDocument(
        IEnumerable<OverviewBoundaryPart> parts,
        bool includeProvince,
        bool includeNational)
    {
        var document =
            new MapDocument();

        foreach (
            OverviewBoundaryPart part
            in parts)
        {
            bool province =
                string.Equals(
                    part.Kind,
                    "province",
                    StringComparison.OrdinalIgnoreCase
                );

            bool national =
                IsNationalKind(
                    part.Kind
                );

            if (province && !includeProvince)
                continue;

            if (national && !includeNational)
                continue;

            if (!province && !national)
                continue;

            if (part.Points == null ||
                part.Points.Length < 3)
            {
                continue;
            }

            var feature =
                new MapFeature
                {
                    Type =
                        MapFeatureType.Boundary,

                    GeometryType =
                        MapGeometryType.Polygon,

                    /*
                     * Prefix Kind được giữ lại để
                     * MapCanvas quyết định style.
                     */
                    Name =
                        $"{part.Kind}:{part.Name}"
                };

            foreach (
                double[] point
                in part.Points)
            {
                if (point == null ||
                    point.Length < 2)
                {
                    continue;
                }

                feature.Points.Add(
                    new WorldPoint(
                        point[0],
                        point[1]
                    )
                );
            }

            if (feature.Points.Count < 3)
                continue;

            feature.UpdateBounds();

            document.Features.Add(
                feature
            );
        }

        document.BuildSpatialIndex();

        return document;
    }

    private static bool IsNationalKind(
        string? kind)
    {
        return
            string.Equals(
                kind,
                "country",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            string.Equals(
                kind,
                "national",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            string.Equals(
                kind,
                "island",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            string.Equals(
                kind,
                "archipelago",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static JsonSerializerOptions
        JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
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
