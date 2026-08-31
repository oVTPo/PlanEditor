using System.Collections.Generic;
using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

public class PlanningPolyline :
    PlanningObject
{
    public List<WorldPoint> Points { get; } =
        new();

    public bool StrokeVisible { get; set; } =
        true;

    public string StrokeColorHex { get; set; } =
        "#CD3737";

    public StrokePattern StrokePattern { get; set; } =
        StrokePattern.Solid;

    public double WidthPixels { get; set; } =
        3.0;

    /// <summary>
    /// Chú thích riêng cho bảng quy ước. Mặc định để trống.
    /// </summary>
    public string LegendLabel { get; set; } =
        "";

    public PlanningPolyline()
    {
        Name = "Đường phương án";
    }
}
