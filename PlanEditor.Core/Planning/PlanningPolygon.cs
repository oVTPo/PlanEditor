using System.Collections.Generic;
using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

public enum PlanningAreaKind
{
    Standard = 0,
    Circle = 1,
    Vegetation = 2,
    Water = 3,
    Sand = 4
}

public enum FillPattern
{
    Solid = 0,
    Diagonal = 1,
    Cross = 2,
    Dots = 3,
    Orchard = 4,
    MixedForest = 5,
    Reeds = 6,
    WaterWaves = 7,
    WaterRipples = 8,
    SandDots = 9,
    SandDunes = 10
}

public sealed class PolygonBezierHandlePair
{
    public WorldPoint InHandle { get; set; }

    public WorldPoint OutHandle { get; set; }

    public bool IsCustom { get; set; }
}

public sealed class PlanningPolygon :
    PlanningObject
{
    public List<WorldPoint> Points { get; } =
        new();

    public PlanningAreaKind AreaKind { get; set; } =
        PlanningAreaKind.Standard;

    public bool FillVisible { get; set; } =
        true;

    public string FillColorHex { get; set; } =
        "#2C78BE";

    public FillPattern FillPattern { get; set; } =
        FillPattern.Solid;

    public double FillOpacity { get; set; } =
        0.22;

    public bool StrokeVisible { get; set; } =
        true;

    public string StrokeColorHex { get; set; } =
        "#2C78BE";

    public StrokePattern StrokePattern { get; set; } =
        StrokePattern.Solid;

    public double OutlineWidthPixels { get; set; } =
        2.5;

    public string LabelText { get; set; } =
        "";

    public double LabelFontSize { get; set; } =
        16.0;

    public bool CurveEnabled { get; set; }

    public List<PolygonBezierHandlePair>
        CurveHandles
    {
        get;
    } = new();

    public PlanningPolygon()
    {
        Name =
            "Vùng phương án";
    }

    public void EnsureCurveHandles()
    {
        int count =
            Points.Count;

        if (count == 0)
        {
            CurveHandles.Clear();

            return;
        }

        while (
            CurveHandles.Count <
            count
        )
        {
            int i =
                CurveHandles.Count;

            WorldPoint anchor =
                Points[i];

            WorldPoint previous =
                Points[
                    (
                        i - 1 +
                        count
                    ) %
                    count
                ];

            WorldPoint next =
                Points[
                    (
                        i + 1
                    ) %
                    count
                ];

            double dx =
                (
                    next.X -
                    previous.X
                ) /
                6.0;

            double dy =
                (
                    next.Y -
                    previous.Y
                ) /
                6.0;

            CurveHandles.Add(
                new PolygonBezierHandlePair
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
            count
        )
        {
            CurveHandles.RemoveAt(
                CurveHandles.Count - 1
            );
        }
    }

    public void ResetCurveHandles()
    {
        CurveHandles.Clear();

        EnsureCurveHandles();
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

        PolygonBezierHandlePair pair =
            CurveHandles[index];

        pair.InHandle =
            new WorldPoint(
                pair.InHandle.X + dx,
                pair.InHandle.Y + dy
            );

        pair.OutHandle =
            new WorldPoint(
                pair.OutHandle.X + dx,
                pair.OutHandle.Y + dy
            );
    }
}
