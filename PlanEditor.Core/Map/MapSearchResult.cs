using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public sealed class MapSearchResult
{
    public string Name { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public MapFeature Feature { get; set; } = null!;

    public WorldPoint Position { get; set; }
}