using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

/// <summary>
/// Chú thích/văn bản được neo bằng TÂM của text vào một tọa độ world trên bản đồ.
///
/// FontSize là kích thước theo mét trong world-space.
///
/// Khi zoom map, kích thước hiển thị được tính:
///     displayPixels = FontSize / MetersPerPixel
///
/// Vì vậy text bám bản đồ giống ký hiệu SVG, không cố định theo camera.
///
/// Position là TÂM world-space của text. Mọi rotate/scale đều lấy tâm này làm pivot.
/// </summary>
public sealed class PlanningText :
    PlanningObject
{
    public WorldPoint Position
    {
        get;
        set;
    }

    public string Text
    {
        get;
        set;
    } = "Văn bản";

    /// <summary>
    /// Kích thước chữ theo mét trên bản đồ.
    ///
    /// Giữ tên FontSize để tương thích source hiện tại,
    /// nhưng đơn vị từ đây là METERS, không còn pixels.
    /// </summary>
    public double FontSize
    {
        get;
        set;
    } = 18.0;

    public bool IsBold
    {
        get;
        set;
    }

    /// <summary>
    /// Góc xoay chữ theo độ. 0 = ngang.
    /// Góc dương xoay theo chiều kim đồng hồ trên màn hình.
    /// </summary>
    public double RotationDegrees
    {
        get;
        set;
    } = 0.0;

    public PlanningText()
    {
        Name =
            "Văn bản";
    }
}
