namespace PlanEditor.MapBuilder.Admin;

public sealed class AdminProvince
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public List<string> FormerNames { get; set; }
        = new();

    public List<AdminCommune> Communes { get; set; }
        = new();
}