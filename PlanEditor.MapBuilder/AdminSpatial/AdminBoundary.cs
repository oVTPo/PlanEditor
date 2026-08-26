using NetTopologySuite.Geometries;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminBoundary
{
    public string Name { get; set; } = "";

    public string NormalizedName { get; set; } = "";

    public string AdminLevel { get; set; } = "";

    public string ProvinceCode { get; set; } = "";

    public string CommuneCode { get; set; } = "";

    public Geometry Geometry { get; set; } = default!;

    public Envelope Envelope =>
        Geometry.EnvelopeInternal;
}