namespace PlanEditor.MapBuilder.Admin;

public sealed class AdminDataset
{
    public string Version { get; set; } =
        "2025-07-01";

    public string Source { get; set; } =
        "19/2025/QĐ-TTg";

    public List<AdminProvince> Provinces
    {
        get;
        set;
    } = new();

    public int CommuneCount =>
        Provinces.Sum(
            province =>
                province.Communes.Count
        );
}