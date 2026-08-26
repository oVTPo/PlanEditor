using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class PresetAreaTool :
    IMapTool
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;
    private readonly PlanningAreaKind _kind;
    private readonly string _name;
    private readonly string _fillColor;
    private readonly FillPattern _fillPattern;
    private readonly double _fillOpacity;

    private readonly List<WorldPoint> _points =
        new();

    private WorldPoint _previewPoint;
    private bool _hasPreviewPoint;

    public string Name =>
        _name;

    public PresetAreaTool(
        MapCanvas canvas,
        PlanningDocument document,
        PlanningAreaKind kind,
        string name,
        string fillColor,
        FillPattern fillPattern,
        double fillOpacity)
    {
        _canvas = canvas;
        _document = document;
        _kind = kind;
        _name = name;
        _fillColor = fillColor;
        _fillPattern = fillPattern;
        _fillOpacity = fillOpacity;
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

        if (!pointer.Properties.IsLeftButtonPressed)
            return false;

        Point world =
            _canvas.ScreenToWorld(
                e.GetPosition(
                    _canvas
                )
            );

        var point =
            new WorldPoint(
                world.X,
                world.Y
            );

        _points.Add(point);
        _previewPoint = point;
        _hasPreviewPoint = true;

        if (
            e.ClickCount >= 2 &&
            _points.Count >= 3
        )
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

        Point world =
            _canvas.ScreenToWorld(
                e.GetPosition(
                    _canvas
                )
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

        if (
            e.Key == Key.Enter &&
            _points.Count >= 3
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

        Color color =
            ParseColor(
                _fillColor
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    color
                ),
                2.0
            )
            {
                LineJoin =
                    PenLineJoin.Round
            };

        var fill =
            new SolidColorBrush(
                Color.FromArgb(
                    52,
                    color.R,
                    color.G,
                    color.B
                )
            );

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
            fill,
            pen,
            geometry
        );
    }

    private void Finish()
    {
        if (_points.Count < 3)
        {
            Cancel();
            return;
        }

        var polygon =
            new PlanningPolygon
            {
                AreaKind = _kind,
                Name = _name,
                FillVisible = true,
                FillColorHex = _fillColor,
                FillPattern = _fillPattern,
                FillOpacity = _fillOpacity,
                StrokeVisible = true,
                StrokeColorHex = _fillColor,
                StrokePattern = StrokePattern.Solid,
                OutlineWidthPixels = 2.0,
                LabelText = ""
            };

        foreach (
            WorldPoint point
            in _points)
        {
            polygon.Points.Add(
                point
            );
        }

        _document.Add(
            polygon
        );

        Cancel();
    }

    private void Cancel()
    {
        _points.Clear();
        _hasPreviewPoint = false;
        _canvas.InvalidateVisual();
    }

    private static Color ParseColor(
        string value)
    {
        try
        {
            return Color.Parse(
                value
            );
        }
        catch
        {
            return Color.FromRgb(
                90,
                120,
                90
            );
        }
    }
}
