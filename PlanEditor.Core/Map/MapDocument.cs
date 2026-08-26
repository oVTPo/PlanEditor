using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public sealed class MapDocument
{
    private SpatialGridIndex? _spatialIndex;

    public string Name { get; set; } =
        "Untitled Map";

    public int SpatialCellCount =>
    _spatialIndex?.CellCount ?? 0;

    public List<MapFeature> Features { get; set; } =
        new();
    
    public List<Junction> Junctions { get; set; }
    = new();

    public void BuildSpatialIndex(
        double cellSize = 500.0)
    {
        _spatialIndex =
            new SpatialGridIndex(cellSize);

        _spatialIndex.Build(Features);
    }

    public IEnumerable<MapFeature> Query(
        WorldBounds viewport)
    {
        if (_spatialIndex == null)
        {
            foreach (MapFeature feature in Features)
            {
                if (feature.Bounds.Intersects(viewport))
                    yield return feature;
            }

            yield break;
        }

        foreach (
            MapFeature feature
            in _spatialIndex.Query(viewport))
        {
            yield return feature;
        }
    }

    public bool TryGetBounds(
        out WorldPoint min,
        out WorldPoint max)
    {
        min = default;
        max = default;

        bool hasPoint = false;

        double minX = double.MaxValue;
        double minY = double.MaxValue;

        double maxX = double.MinValue;
        double maxY = double.MinValue;
        

        foreach (MapFeature feature in Features)
        {
            foreach (WorldPoint point in feature.Points)
            {
                hasPoint = true;

                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);

                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        if (!hasPoint)
            return false;

        min = new WorldPoint(
            minX,
            minY
        );

        max = new WorldPoint(
            maxX,
            maxY
        );

        return true;
    }

    public IEnumerable<MapFeature> QueryPlanningRoads(
    WorldBounds viewport)
    {
        foreach (MapFeature feature in Query(viewport))
        {
            if (feature.Type != MapFeatureType.Road)
                continue;

            if (!feature.IsPlanningRoad)
                continue;

            yield return feature;
        }
    }

    public IEnumerable<MapFeature> QueryVehicleRoads(
    WorldBounds viewport)
    {
        foreach (MapFeature feature in Query(viewport))
        {
            if (feature.Type != MapFeatureType.Road)
                continue;

            if (!feature.IsVehicleRoad)
                continue;

            yield return feature;
        }
    }

    public void BuildJunctions()
    {
        Junctions =
            JunctionDetector.Detect(
                Features.Where(
                    feature =>
                        feature.Type ==
                            MapFeatureType.Road &&
                        feature.IsPlanningRoad
                )
            );
    }
}