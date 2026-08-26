using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class TacticalAttackTool :
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
            3.0
        )
        {
            LineCap =
                PenLineCap.Round,

            LineJoin =
                PenLineJoin.Round
        };

    public string Name =>
        "Đường tác chiến";

    public TacticalAttackTool(
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

        if (
            e.ClickCount >= 2 &&
            _points.Count >= 2
        )
        {
            RemoveConsecutiveDuplicatePoints();
            Finish();
        }

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
        if (e.Key == Key.Escape)
        {
            Cancel();
            return true;
        }

        if (
            e.Key == Key.Enter &&
            _points.Count >= 2
        )
        {
            RemoveConsecutiveDuplicatePoints();
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
            Point start =
                _canvas.WorldToScreen(
                    _points[^1].X,
                    _points[^1].Y
                );

            Point tip =
                _canvas.WorldToScreen(
                    _previewPoint.X,
                    _previewPoint.Y
                );

            context.DrawLine(
                PreviewPen,
                start,
                tip
            );

            DrawPreviewHead(
                context,
                start,
                tip
            );
        }
        else if (_points.Count >= 2)
        {
            Point start =
                _canvas.WorldToScreen(
                    _points[^2].X,
                    _points[^2].Y
                );

            Point tip =
                _canvas.WorldToScreen(
                    _points[^1].X,
                    _points[^1].Y
                );

            DrawPreviewHead(
                context,
                start,
                tip
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

    private static void DrawPreviewHead(
        DrawingContext context,
        Point start,
        Point tip)
    {
        Vector direction =
            tip - start;

        double length =
            Math.Sqrt(
                direction.X * direction.X +
                direction.Y * direction.Y
            );

        if (length < 1.0)
            return;

        Vector unit =
            direction / length;

        Vector normal =
            new(
                -unit.Y,
                unit.X
            );

        double headLength =
            Math.Clamp(
                length * 0.30,
                18.0,
                42.0
            );

        double halfWidth =
            Math.Clamp(
                headLength * 0.42,
                9.0,
                22.0
            );

        Point shoulder =
            tip -
            unit *
            headLength;

        context.DrawLine(
            PreviewPen,
            shoulder -
                normal *
                halfWidth,
            tip
        );

        context.DrawLine(
            PreviewPen,
            tip,
            shoulder +
                normal *
                halfWidth
        );
    }

    private void Finish()
    {
        if (_points.Count < 2)
        {
            Cancel();
            return;
        }

        var arrow =
            new PlanningArrow
            {
                Name =
                    "Tiến công",

                LegendLabel =
                    "Tiến công",

                TacticalAttackMode =
                    TacticalAttackMode.Assault,

                StrokeVisible =
                    true,

                StrokeColorHex =
                    "#CD3737",

                StrokePattern =
                    StrokePattern.Solid,

                StrokeWidth =
                    3.0,

                StartHead =
                    ArrowHeadKind.None,

                EndHead =
                    ArrowHeadKind.Open,

                Closed =
                    false
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

        Cancel();
    }

    private void Cancel()
    {
        _points.Clear();

        _hasPreviewPoint =
            false;

        _canvas.InvalidateVisual();
    }

    private void RemoveConsecutiveDuplicatePoints()
    {
        if (_points.Count < 2)
            return;

        for (
            int i =
                _points.Count - 1;
            i > 0;
            i--)
        {
            WorldPoint a =
                _points[i - 1];

            WorldPoint b =
                _points[i];

            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            if (
                dx * dx +
                dy * dy <
                0.000001
            )
            {
                _points.RemoveAt(i);
            }
        }
    }
}
