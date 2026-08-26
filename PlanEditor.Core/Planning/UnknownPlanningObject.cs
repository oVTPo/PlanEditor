namespace PlanEditor.Core.Planning;

/// <summary>
/// Giữ nguyên dữ liệu của object do phiên bản PlanEditor này
/// chưa hỗ trợ. Object lạ không được render, nhưng khi Save lại
/// dữ liệu JSON gốc vẫn được giữ để tránh mất dữ liệu.
/// </summary>
public sealed class UnknownPlanningObject :
    PlanningObject
{
    public string ObjectType { get; set; } =
        "unknown";

    public string RawJson { get; set; } =
        "{}";

    public UnknownPlanningObject()
    {
        Name = "Đối tượng chưa hỗ trợ";
    }
}
