using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

/// <summary>
/// Tool chữ:
/// 1. Chọn nút T.
/// 2. Click vị trí trên map.
/// 3. MainWindow mở TextBox inline tại đúng vị trí đó.
/// 4. Enter commit / Esc cancel.
/// </summary>
public sealed class TextTool :
    IMapTool
{
    private readonly MapCanvas _canvas;

    public string Name =>
        "Văn bản";

    public TextTool(
        MapCanvas canvas)
    {
        _canvas =
            canvas;
    }

    public void Activate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Ibeam
            );
    }

    public void Deactivate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Arrow
            );
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

        Point rawWorld =
            _canvas.ScreenToWorld(
                screen
            );

        var world =
            new WorldPoint(
                rawWorld.X,
                rawWorld.Y
            );

        _canvas.RequestTextPlacement(
            world,
            screen
        );

        return true;
    }

    public bool PointerMoved(
        PointerEventArgs e)
    {
        return false;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        return false;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        if (e.Key ==
            Key.Escape)
        {
            _canvas.CancelTextPlacementRequest();

            return true;
        }

        return false;
    }

    public void RenderOverlay(
        DrawingContext context)
    {
    }
}
