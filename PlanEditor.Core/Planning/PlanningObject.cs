using System;

namespace PlanEditor.Core.Planning;

public abstract class PlanningObject
{
    /*
     * Settable để ID được giữ nguyên khi Save/Open project.
     */
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string Name { get; set; } =
        "Đối tượng";

    public bool IsVisible { get; set; } =
        true;

    public bool IsLocked { get; set; }

    /// <summary>
    /// Có đưa object vào bảng quy ước khi in hay không.
    /// Ẩn khỏi legend không xóa object khỏi phương án.
    /// </summary>
    public bool ShowInLegend { get; set; } =
        true;
}
