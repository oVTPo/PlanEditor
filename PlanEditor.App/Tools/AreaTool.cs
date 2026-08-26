using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class AreaTool :
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
                    44,
                    120,
                    190
                )
            ),
            2.0
        )
        {
            LineJoin =
                PenLineJoin.Round
        };

    private static readonly IBrush PreviewFill =
        new SolidColorBrush(
            Color.FromArgb(
                45,
                44,
                120,
                190
            )
        );

    public string Name =>
        "Vùng";

    public AreaTool(
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
        _previewPoint = point;
        _hasPreviewPoint = true;

        if (e.ClickCount >= 2 &&
            _points.Count >= 3)
        {
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

        _hasPreviewPoint = true;

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

        if (e.Key == Key.Enter &&
            _points.Count >= 3)
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

        var geometry =
            new StreamGeometry();

        using (
            StreamGeometryContext gc =
                geometry.Open())
        {
            Point first =
                _canvas.WorldToScreen(
                    _points[0].X,
                    _points[0].Y
                );

            gc.BeginFigure(
                first,
                isFilled: true
            );

            for (
                int i = 1;
                i < _points.Count;
                i++)
            {
                gc.LineTo(
                    _canvas.WorldToScreen(
                        _points[i].X,
                        _points[i].Y
                    )
                );
            }

            if (_hasPreviewPoint)
            {
                gc.LineTo(
                    _canvas.WorldToScreen(
                        _previewPoint.X,
                        _previewPoint.Y
                    )
                );
            }

            gc.EndFigure(
                isClosed: true
            );
        }

        context.DrawGeometry(
            PreviewFill,
            PreviewPen,
            geometry
        );

        foreach (
            WorldPoint point
            in _points)
        {
            Point screen =
                _canvas.WorldToScreen(
                    point.X,
                    point.Y
                );

            context.DrawEllipse(
                Brushes.White,
                PreviewPen,
                screen,
                4,
                4
            );
        }
    }

    private void Finish()
    {
        if (_points.Count < 3)
        {
            Cancel();
            return;
        }

        var polygon =
            new PlanningPolygon();

        foreach (
            WorldPoint point
            in _points)
        {
            polygon.Points.Add(point);
        }

        _document.Add(
            polygon
        );

        _points.Clear();
        _hasPreviewPoint = false;

        _canvas.InvalidateVisual();
    }

    private void Cancel()
    {
        _points.Clear();
        _hasPreviewPoint = false;

        _canvas.InvalidateVisual();
    }
}
