using Avalonia.Input;
using Avalonia.Media;

namespace PlanEditor.App.Tools;

public interface IMapTool
{
    string Name { get; }

    void Activate();

    void Deactivate();

    bool PointerPressed(
        PointerPressedEventArgs e);

    bool PointerMoved(
        PointerEventArgs e);

    bool PointerReleased(
        PointerReleasedEventArgs e);

    bool KeyDown(
        KeyEventArgs e);

    void RenderOverlay(
        DrawingContext context);
}
