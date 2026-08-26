using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class LineTool : IMapTool
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
            2.0
        );

    public string Name =>
        "Đường";

    public LineTool(
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
        PointerPoint point =
            e.GetCurrentPoint(_canvas);

        if (!point.Properties
            .IsLeftButtonPressed)
        {
            return false;
        }

        Point screen =
            e.GetPosition(_canvas);

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        _points.Add(
            new WorldPoint(
                world.X,
                world.Y
            )
        );

        _previewPoint =
            _points[^1];
        _hasPreviewPoint = true;

        /*
         * Double click kết thúc đường.
         * Enter cũng kết thúc.
         */
        if (e.ClickCount >= 2 &&
            _points.Count >= 2)
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
            e.GetPosition(_canvas);

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
            _points.Count >= 2)
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

            WorldPoint preview =
                _previewPoint;

            context.DrawLine(
                PreviewPen,
                _canvas.WorldToScreen(
                    last.X,
                    last.Y
                ),
                _canvas.WorldToScreen(
                    preview.X,
                    preview.Y
                )
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

    private void Finish()
    {
        if (_points.Count < 2)
        {
            Cancel();
            return;
        }

        var line =
            new PlanningPolyline();

        foreach (
            WorldPoint point
            in _points)
        {
            line.Points.Add(point);
        }

        _document.Add(line);

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
