using System;
using Avalonia;
using Avalonia.Input;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

/// <summary>
/// Hình tròn dạng ĐỐI TƯỢNG.
///
/// Không vẽ bán kính.
/// Khi tool đang active:
/// - click một lần trên map => tạo ngay hình tròn kích thước mặc định.
/// - sau khi tạo, circle là PlanningPolygon AreaKind.Circle nên dùng
///   toàn bộ property Area hiện có.
/// - double-click bằng Select Tool => editor nhãn Area hiện có,
///   chữ được vẽ tại tâm polygon.
///
/// Bán kính mặc định được tính từ 22 px tại thời điểm đặt,
/// sau đó lưu thành world coordinate để object bám bản đồ.
/// </summary>
public sealed class CircleAreaTool :
    IMapTool
{
    private const int SegmentCount =
        64;

    private const double DefaultScreenRadius =
        22.0;

    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;

    public string Name =>
        "Hình tròn chữ";

    public CircleAreaTool(
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
        _canvas.InvalidateVisual();
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

        Point centerScreen =
            e.GetPosition(
                _canvas
            );

        Point centerPoint =
            _canvas.ScreenToWorld(
                centerScreen
            );

        /*
         * Lấy điểm cách tâm 22 px trên màn hình rồi đổi sang world.
         * Nhờ vậy object vừa tạo có kích thước thao tác dễ nhìn,
         * nhưng sau đó vẫn bám đúng world coordinate khi zoom/pan.
         */
        Point edgePoint =
            _canvas.ScreenToWorld(
                new Point(
                    centerScreen.X +
                        DefaultScreenRadius,
                    centerScreen.Y
                )
            );

        var center =
            new WorldPoint(
                centerPoint.X,
                centerPoint.Y
            );

        double radius =
            Math.Abs(
                edgePoint.X -
                centerPoint.X
            );

        radius =
            Math.Max(
                radius,
                0.25
            );

        var polygon =
            new PlanningPolygon
            {
                AreaKind =
                    PlanningAreaKind.Circle,

                Name =
                    "Hình tròn",

                FillVisible =
                    true,

                FillColorHex =
                    "#FFFFFF",

                FillPattern =
                    FillPattern.Solid,

                FillOpacity =
                    0.82,

                StrokeVisible =
                    true,

                StrokeColorHex =
                    "#30343B",

                StrokePattern =
                    StrokePattern.Solid,

                OutlineWidthPixels =
                    2.0,

                LabelText =
                    "",

                LabelFontSize =
                    16.0
            };

        for (
            int i = 0;
            i < SegmentCount;
            i++)
        {
            double angle =
                Math.PI *
                2.0 *
                i /
                SegmentCount;

            polygon.Points.Add(
                new WorldPoint(
                    center.X +
                        Math.Cos(
                            angle
                        ) *
                        radius,

                    center.Y +
                        Math.Sin(
                            angle
                        ) *
                        radius
                )
            );
        }

        _document.Add(
            polygon
        );

        /*
         * Không tự chuyển Select ở đây để người dùng có thể click
         * liên tiếp đặt nhiều hình tròn.
         * Muốn nhập chữ: chuyển Select rồi double-click hình.
         */
        _canvas.InvalidateVisual();

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
        return false;
    }

    public void RenderOverlay(
        Avalonia.Media.DrawingContext context)
    {
        /*
         * Không preview bán kính vì đây là object placement tool,
         * không phải công cụ vẽ circle geometry.
         */
    }
}
