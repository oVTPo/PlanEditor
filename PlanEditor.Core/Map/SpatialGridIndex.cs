using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public sealed class SpatialGridIndex
{
    private readonly double _cellSize;

    public int CellCount => _cells.Count;

    private readonly Dictionary<(int X, int Y), List<MapFeature>>
        _cells = new();

    public SpatialGridIndex(double cellSize)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        _cellSize = cellSize;
    }

    public void Clear()
    {
        _cells.Clear();
    }

    public void Build(IEnumerable<MapFeature> features)
    {
        _cells.Clear();

        foreach (MapFeature feature in features)
        {
            Add(feature);
        }
    }

    public void Add(MapFeature feature)
    {
        WorldBounds bounds = feature.Bounds;

        int minCellX = ToCell(bounds.MinX);
        int maxCellX = ToCell(bounds.MaxX);

        int minCellY = ToCell(bounds.MinY);
        int maxCellY = ToCell(bounds.MaxY);

        for (int x = minCellX; x <= maxCellX; x++)
        {
            for (int y = minCellY; y <= maxCellY; y++)
            {
                var key = (x, y);

                if (!_cells.TryGetValue(
                        key,
                        out List<MapFeature>? list))
                {
                    list = new List<MapFeature>();
                    _cells[key] = list;
                }

                list.Add(feature);
            }
        }
    }

    public IEnumerable<MapFeature> Query(
        WorldBounds viewport)
    {
        int minCellX = ToCell(viewport.MinX);
        int maxCellX = ToCell(viewport.MaxX);

        int minCellY = ToCell(viewport.MinY);
        int maxCellY = ToCell(viewport.MaxY);

        var seen = new HashSet<string>();

        for (int x = minCellX; x <= maxCellX; x++)
        {
            for (int y = minCellY; y <= maxCellY; y++)
            {
                if (!_cells.TryGetValue(
                        (x, y),
                        out List<MapFeature>? list))
                {
                    continue;
                }

                foreach (MapFeature feature in list)
                {
                    if (!seen.Add(feature.Id))
                        continue;

                    if (!feature.Bounds.Intersects(viewport))
                        continue;

                    yield return feature;
                }
            }
        }
    }

    private int ToCell(double value)
    {
        return (int)Math.Floor(
            value / _cellSize
        );
    }
}