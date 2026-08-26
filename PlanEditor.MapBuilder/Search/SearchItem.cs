namespace PlanEditor.MapBuilder.Search;

public sealed class SearchItem
{
    public string OsmId { get; set; } = "";

    public string Name { get; set; } = "";

    public string NormalizedName { get; set; } = "";

    public string Category { get; set; } = "";

    public string Subtype { get; set; } = "";

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    public string ProvinceCode { get; set; } = "";

    public string CommuneCode { get; set; } = "";
}