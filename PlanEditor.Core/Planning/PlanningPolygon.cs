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

        /*
         * Chỉ tạo handle còn thiếu.
         * Handle user đã kéo (IsCustom = true) được giữ nguyên.
         */
        while (
            CurveHandles.Count <
            count
        )
        {
            int i =
                CurveHandles.Count;

            CurveHandles.Add(
                CreateAutoCurveHandle(
                    i
                )
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

    /*
     * Dùng khi user bấm "Bézier":
     * bỏ toàn bộ handle cũ và tự sinh lại một đường cong mượt,
     * sau đó user vẫn có thể kéo từng handle để tinh chỉnh.
     */
    public void ResetCurveHandles()
    {
        CurveHandles.Clear();
        EnsureCurveHandles();
    }

    private PolygonBezierHandlePair
        CreateAutoCurveHandle(
            int index)
    {
        int count =
            Points.Count;

        WorldPoint anchor =
            Points[index];

        if (count < 2)
        {
            return new PolygonBezierHandlePair
            {
                InHandle =
                    anchor,

                OutHandle =
                    anchor,

                IsCustom =
                    false
            };
        }

        WorldPoint previous =
            Points[
                (
                    index - 1 +
                    count
                ) %
                count
            ];

        WorldPoint next =
            Points[
                (
                    index + 1
                ) %
                count
            ];

        double prevDx =
            anchor.X -
            previous.X;

        double prevDy =
            anchor.Y -
            previous.Y;

        double nextDx =
            next.X -
            anchor.X;

        double nextDy =
            next.Y -
            anchor.Y;

        double previousLength =
            System.Math.Sqrt(
                prevDx * prevDx +
                prevDy * prevDy
            );

        double nextLength =
            System.Math.Sqrt(
                nextDx * nextDx +
                nextDy * nextDy
            );

        /*
         * Hướng tangent theo Catmull-Rom:
         * previous -> next.
         */
        double tangentX =
            next.X -
            previous.X;

        double tangentY =
            next.Y -
            previous.Y;

        double tangentLength =
            System.Math.Sqrt(
                tangentX * tangentX +
                tangentY * tangentY
            );

        if (tangentLength <
            0.000001)
        {
            return new PolygonBezierHandlePair
            {
                InHandle =
                    anchor,

                OutHandle =
                    anchor,

                IsCustom =
                    false
            };
        }

        tangentX /=
            tangentLength;

        tangentY /=
            tangentLength;

        /*
         * 0.28 tạo auto-smooth rõ ràng nhưng hạn chế overshoot.
         * Mỗi phía dùng chiều dài cạnh tương ứng nên handle nằm
         * đúng theo hình học local thay vì phình quá xa ở cạnh ngắn.
         */
        const double smoothing =
            0.28;

        double inLength =
            previousLength *
            smoothing;

        double outLength =
            nextLength *
            smoothing;

        /*
         * Clamp thêm theo cạnh ngắn hơn để góc nhọn không bị
         * kéo thành "giọt nước".
         */
        double localLimit =
            System.Math.Min(
                previousLength,
                nextLength
            ) *
            0.42;

        inLength =
            System.Math.Min(
                inLength,
                localLimit
            );

        outLength =
            System.Math.Min(
                outLength,
                localLimit
            );

        return new PolygonBezierHandlePair
        {
            InHandle =
                new WorldPoint(
                    anchor.X -
                        tangentX *
                        inLength,

                    anchor.Y -
                        tangentY *
                        inLength
                ),

            OutHandle =
                new WorldPoint(
                    anchor.X +
                        tangentX *
                        outLength,

                    anchor.Y +
                        tangentY *
                        outLength
                ),

            IsCustom =
                false
        };
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
