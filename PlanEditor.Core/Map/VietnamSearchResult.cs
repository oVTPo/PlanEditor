namespace PlanEditor.Core.Map;

public sealed class VietnamSearchResult
{
    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public string Subtype { get; set; } = "";

    public string ProvinceCode { get; set; } = "";

    public string ProvinceName { get; set; } = "";

    public string CommuneCode { get; set; } = "";

    public string CommuneName { get; set; } = "";

    public double Longitude { get; set; }

    public double Latitude { get; set; }
}