using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public sealed class Junction
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public WorldPoint Position { get; set; }

    public List<MapFeature> ConnectedRoads { get; set; }
        = new();

    public int RoadCount =>
        ConnectedRoads.Count;
}