using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public enum MapFeatureType
{
    Road,
    Building,
    Water,
    Land,
    Barrier,
    Boundary
}

public enum MapGeometryType
{
    LineString,
    Polygon
}

public sealed class MapFeature
{

    public bool IsVehicleRoad { get; set; }
    public double RoadWidthMeters { get; set; }
    public bool IsPlanningRoad { get; set; }


    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public MapFeatureType Type { get; set; }


    public RoadClass RoadClass { get; set; }

    public MapGeometryType GeometryType { get; set; }

    public string? Name { get; set; }

    public List<WorldPoint> Points { get; set; } = new();

    public WorldBounds Bounds { get; set; }

    public Dictionary<string, string> Properties { get; set; } = new();

    public void UpdateBounds()
    {
        Bounds = WorldBounds.FromPoints(Points);
    }
    
}

 public enum RoadClass
{
    Unknown = 0,

    Motorway,
    Trunk,
    Primary,
    Secondary,
    Tertiary,

    Residential,
    Unclassified,
    LivingStreet,
    Service,

    Pedestrian,
    Cycleway,
    Track,

    Footway,
    Path,
    Steps
}