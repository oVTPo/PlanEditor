using System;
using System.Collections.Generic;
using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

public enum StrokePattern
{
    Solid = 0,
    Dashed = 1,
    Dotted = 2
}


public enum TacticalAttackMode
{
    None = 0,
    Assault = 1,
    Raid = 2
}

public enum ArrowHeadKind
{
    None = 0,
    Triangle = 1,
    Open = 2,
    Circle = 3,
    Diamond = 4
}

public sealed class ArrowBezierHandlePair
{
    public WorldPoint InHandle { get; set; }

    public WorldPoint OutHandle { get; set; }

    public bool IsCustom { get; set; }
}

public sealed class PlanningArrow : PlanningObject
{
    public PlanningArrow()
    {
        Name = "Mũi tên";
    }

    public List<WorldPoint> Points { get; } =
        new();

    public bool StrokeVisible { get; set; } =
        true;

    public string StrokeColorHex { get; set; } =
        "#CD3737";

    public StrokePattern StrokePattern { get; set; } =
        StrokePattern.Solid;

    public ArrowHeadKind StartHead { get; set; } =
        ArrowHeadKind.None;

    public ArrowHeadKind EndHead { get; set; } =
        ArrowHeadKind.Triangle;

    public double StrokeWidth { get; set; } = 2.5;

    /// <summary>
    /// Bật đường cong mượt qua các anchor point.
    /// </summary>
    public bool CurveEnabled { get; set; }

    public List<ArrowBezierHandlePair> CurveHandles { get; } =
        new();


    /// <summary>
    /// Đầu mũi tên tác chiến lớn hơn mũi tên thường nhẹ.
    /// </summary>
    public double TacticalHeadScale { get; set; } = 1.15;

    public bool Closed { get; set; }

    /// <summary>
    /// None = mũi tên thường.
    /// Assault = "Tiến công" (không vòng).
    /// Raid = "Tập kích" (có vòng quanh đầu mũi tên).
    /// </summary>
    public TacticalAttackMode TacticalAttackMode
    {
        get;
        set;
    } = TacticalAttackMode.None;

    public bool IsTacticalAttackSymbol =>
        TacticalAttackMode !=
            TacticalAttackMode.None;

    /// <summary>
    /// Chú thích riêng cho bảng quy ước. Mặc định để trống.
    /// </summary>
    public string LegendLabel { get; set; } =
        "";

    public void EnsureCurveHandles()
    {
        while (
            CurveHandles.Count <
            Points.Count
        )
        {
            int i =
                CurveHandles.Count;

            WorldPoint anchor =
                Points[i];

            WorldPoint previous =
                Points[
                    System.Math.Max(
                        0,
                        i - 1
                    )
                ];

            WorldPoint next =
                Points[
                    System.Math.Min(
                        Points.Count - 1,
                        i + 1
                    )
                ];

            double dx =
                (next.X - previous.X) /
                6.0;

            double dy =
                (next.Y - previous.Y) /
                6.0;

            CurveHandles.Add(
                new ArrowBezierHandlePair
                {
                    InHandle =
                        new WorldPoint(
                            anchor.X - dx,
                            anchor.Y - dy
                        ),

                    OutHandle =
                        new WorldPoint(
                            anchor.X + dx,
                            anchor.Y + dy
                        ),

                    IsCustom =
                        false
                }
            );
        }

        while (
            CurveHandles.Count >
            Points.Count
        )
        {
            CurveHandles.RemoveAt(
                CurveHandles.Count - 1
            );
        }
    }


    public void MoveAnchorAndHandles(
        int index,
        WorldPoint newPoint)
    {
        if (
            index < 0 ||
            index >= Points.Count
        )
        {
            return;
        }

        EnsureCurveHandles();

        WorldPoint oldPoint =
            Points[index];

        double dx =
            newPoint.X -
            oldPoint.X;

        double dy =
            newPoint.Y -
            oldPoint.Y;

        Points[index] =
            newPoint;

        ArrowBezierHandlePair handles =
            CurveHandles[index];

        handles.InHandle =
            new WorldPoint(
                handles.InHandle.X + dx,
                handles.InHandle.Y + dy
            );

        handles.OutHandle =
            new WorldPoint(
                handles.OutHandle.X + dx,
                handles.OutHandle.Y + dy
            );
    }


}
