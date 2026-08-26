using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class ArrowTool :
    IMapTool
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;

    private readonly List<WorldPoint>
        _points = new();

    private WorldPoint _previewPoint;
    private bool _hasPreviewPoint;

    private static readonly IPen PreviewPen =
        new Pen(
            new SolidColorBrush(
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            ),
            2.5
        )
        {
            LineCap =
                PenLineCap.Round,

            LineJoin =
                PenLineJoin.Round
        };

    public string Name =>
        "Mũi tên";

    public ArrowTool(
        MapCanvas canvas,
        PlanningDocument document)
    {
        _canvas = canvas;
        _document = document;
    }

    public void Activate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Cross
            );
    }

    public void Deactivate()
    {
        Cancel();
    }

    public bool PointerPressed(
        PointerPressedEventArgs e)
    {
        PointerPoint pointer =
            e.GetCurrentPoint(
                _canvas
            );

        if (!pointer.Properties
            .IsLeftButtonPressed)
        {
            return false;
        }

        /*
         * Avalonia phát press thứ hai của double-click với ClickCount = 2.
         *
         * Nếu vẫn Add(point) ở press thứ hai, hai điểm cuối sẽ trùng nhau.
         * Renderer lấy đoạn cuối để tính hướng arrow-head => length = 0
         * và đầu mũi tên kết thúc biến mất.
         *
         * Vì click thứ nhất của double-click đã thêm điểm cuối rồi,
         * press ClickCount=2 chỉ cần Finish(), KHÔNG thêm thêm một điểm.
         */
        if (
            e.ClickCount >= 2 &&
            _points.Count >= 2
        )
        {
            Finish();

            return true;
        }

        Point screen =
            e.GetPosition(
                _canvas
            );

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var point =
            new WorldPoint(
                world.X,
                world.Y
            );

        _points.Add(point);

        _previewPoint =
            point;

        _hasPreviewPoint =
            true;

        /*
         * Workflow:
         * - click: thêm node
         * - double-click: kết thúc
         * - Enter: kết thúc
         * - Esc: hủy
         */
        _canvas.InvalidateVisual();

        return true;
    }

    public bool PointerMoved(
        PointerEventArgs e)
    {
        if (_points.Count == 0)
            return false;

        Point screen =
            e.GetPosition(
                _canvas
            );

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        _previewPoint =
            new WorldPoint(
                world.X,
                world.Y
            );

        _hasPreviewPoint =
            true;

        _canvas.InvalidateVisual();

        return true;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        return _points.Count > 0;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        if (e.Key ==
            Key.Escape)
        {
            Cancel();

            return true;
        }

        if (
            e.Key ==
                Key.Enter &&
            _points.Count >= 2
        )
        {
            Finish();

            return true;
        }

        return false;
    }

    public void RenderOverlay(
        DrawingContext context)
    {
        if (_points.Count == 0)
            return;

        for (
            int i = 0;
            i < _points.Count - 1;
            i++)
        {
            Point a =
                _canvas.WorldToScreen(
                    _points[i].X,
                    _points[i].Y
                );

            Point b =
                _canvas.WorldToScreen(
                    _points[i + 1].X,
                    _points[i + 1].Y
                );

            context.DrawLine(
                PreviewPen,
                a,
                b
            );
        }

        if (_hasPreviewPoint)
        {
            WorldPoint last =
                _points[^1];

            Point start =
                _canvas.WorldToScreen(
                    last.X,
                    last.Y
                );

            Point end =
                _canvas.WorldToScreen(
                    _previewPoint.X,
                    _previewPoint.Y
                );

            context.DrawLine(
                PreviewPen,
                start,
                end
            );

            DrawPreviewArrowHead(
                context,
                start,
                end
            );
        }
        else if (_points.Count >= 2)
        {
            Point start =
                _canvas.WorldToScreen(
                    _points[^2].X,
                    _points[^2].Y
                );

            Point end =
                _canvas.WorldToScreen(
                    _points[^1].X,
                    _points[^1].Y
                );

            DrawPreviewArrowHead(
                context,
                start,
                end
            );
        }

        foreach (
            WorldPoint world
            in _points)
        {
            Point screen =
                _canvas.WorldToScreen(
                    world.X,
                    world.Y
                );

            context.DrawEllipse(
                Brushes.White,
                PreviewPen,
                screen,
                4.0,
                4.0
            );
        }
    }

    private static void DrawPreviewArrowHead(
        DrawingContext context,
        Point start,
        Point tip)
    {
        double dx =
            tip.X -
            start.X;

        double dy =
            tip.Y -
            start.Y;

        double length =
            System.Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        double tx =
            dx / length;

        double ty =
            dy / length;

        double nx =
            -ty;

        double ny =
            tx;

        const double size =
            11.0;

        Point back =
            new(
                tip.X -
                tx * size,
                tip.Y -
                ty * size
            );

        Point left =
            new(
                back.X +
                nx * size * 0.48,
                back.Y +
                ny * size * 0.48
            );

        Point right =
            new(
                back.X -
                nx * size * 0.48,
                back.Y -
                ny * size * 0.48
            );

        var geometry =
            new StreamGeometry();

        using (
            StreamGeometryContext gc =
                geometry.Open())
        {
            gc.BeginFigure(
                tip,
                isFilled: true
            );

            gc.LineTo(left);
            gc.LineTo(right);

            gc.EndFigure(
                isClosed: true
            );
        }

        context.DrawGeometry(
            new SolidColorBrush(
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            ),
            null,
            geometry
        );
    }

    private void Finish()
    {
        RemoveConsecutiveDuplicatePoints();

        if (_points.Count < 2)
        {
            Cancel();
            return;
        }

        var arrow =
            new PlanningArrow
            {
                Name =
                    "Mũi tên",

                StrokePattern =
                    StrokePattern.Solid,

                StartHead =
                    ArrowHeadKind.None,

                EndHead =
                    ArrowHeadKind.Triangle,

                StrokeWidth =
                    2.5
            };

        foreach (
            WorldPoint point
            in _points)
        {
            arrow.Points.Add(
                point
            );
        }

        _document.Add(
            arrow
        );

        /*
         * Chọn ngay object vừa hoàn thành để:
         * - thấy selection handles
         * - Property Inspector hiện ngay
         * - user biết object đã commit thành công
         */
        _canvas.SelectPlanningObject(
            arrow
        );

        _points.Clear();

        _hasPreviewPoint =
            false;

        _canvas.InvalidateVisual();
    }

    private void RemoveConsecutiveDuplicatePoints()
    {
        /*
         * Defensive cleanup cho cả Enter và các input sequence khác.
         * So sánh theo screen-space để tolerance ổn định ở mọi zoom.
         */
        const double tolerancePixels =
            1.5;

        for (
            int i =
                _points.Count - 1;
            i > 0;
            i--)
        {
            Point a =
                _canvas.WorldToScreen(
                    _points[i - 1].X,
                    _points[i - 1].Y
                );

            Point b =
                _canvas.WorldToScreen(
                    _points[i].X,
                    _points[i].Y
                );

            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            if (
                dx * dx +
                dy * dy
                <=
                tolerancePixels *
                tolerancePixels
            )
            {
                _points.RemoveAt(i);
            }
        }
    }

    private void Cancel()
    {
        _points.Clear();

        _hasPreviewPoint =
            false;

        _canvas.InvalidateVisual();
    }
}
