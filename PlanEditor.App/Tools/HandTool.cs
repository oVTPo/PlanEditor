using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;

namespace PlanEditor.App.Tools;

public sealed class HandTool : IMapTool
{
    private readonly MapCanvas _canvas;

    private bool _dragging;
    private Point _last;

    public string Name =>
        "Di chuyển";

    public HandTool(
        MapCanvas canvas)
    {
        _canvas = canvas;
    }

    public void Activate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Hand
            );
    }

    public void Deactivate()
    {
        _dragging = false;
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

        _dragging = true;
        _last =
            e.GetPosition(_canvas);

        e.Pointer.Capture(
            _canvas
        );

        return true;
    }

    public bool PointerMoved(
        PointerEventArgs e)
    {
        if (!_dragging)
            return false;

        Point current =
            e.GetPosition(_canvas);

        Vector delta =
            current - _last;

        _last =
            current;

        _canvas.PanBy(
            delta
        );

        return true;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return false;

        _dragging = false;

        e.Pointer.Capture(null);

        return true;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        return false;
    }

    public void RenderOverlay(
        DrawingContext context)
    {
    }
}
