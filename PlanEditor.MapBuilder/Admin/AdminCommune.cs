namespace PlanEditor.MapBuilder.Admin;

public sealed class AdminCommune
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public string ProvinceCode { get; set; } = "";

    public List<string> FormerNames { get; set; }
        = new();
}