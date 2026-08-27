using System;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PlanEditor.App.Tools;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;
using PlanEditor.Core.Project;

namespace PlanEditor.App;

public partial class MainWindow
{
    private string? _planningClipboardJson;
    private int _planningPasteSerial;

    /*
     * Copy / Paste đối tượng phương án:
     * macOS   : Command + C / Command + V
     * Windows : Ctrl + C / Ctrl + V
     *
     * Không chiếm shortcut khi đang nhập trong TextBox.
     */
    protected override void OnKeyDown(
        KeyEventArgs e)
    {
        bool command =
            OperatingSystem.IsMacOS()
                ? e.KeyModifiers.HasFlag(
                    KeyModifiers.Meta
                )
                : e.KeyModifiers.HasFlag(
                    KeyModifiers.Control
                );

        if (
            command &&
            e.Source is not TextBox
        )
        {
            if (e.Key == Key.C)
            {
                CopySelectedPlanningObject();

                e.Handled =
                    true;

                return;
            }

            if (e.Key == Key.V)
            {
                PastePlanningObject();

                e.Handled =
                    true;

                return;
            }
        }

        base.OnKeyDown(e);
    }

    private void CopySelectedPlanningObject()
    {
        PlanningObject? selected =
            MapCanvas.SelectedPlanningObject;

        if (selected == null)
        {
            PlanningStatusText.Text =
                "Chưa chọn đối tượng để sao chép.";

            return;
        }

        try
        {
            JsonObject node =
                PlanningObjectCodecRegistry
                    .Serialize(
                        selected
                    );

            _planningClipboardJson =
                node.ToJsonString();

            _planningPasteSerial =
                0;

            PlanningStatusText.Text =
                $"Đã sao chép: {selected.Name}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể sao chép: {ex.Message}";
        }
    }

    private void PastePlanningObject()
    {
        if (
            string.IsNullOrWhiteSpace(
                _planningClipboardJson
            )
        )
        {
            PlanningStatusText.Text =
                "Chưa có đối tượng đã sao chép.";

            return;
        }

        try
        {
            JsonNode? parsed =
                JsonNode.Parse(
                    _planningClipboardJson
                );

            if (parsed is not JsonObject node)
            {
                PlanningStatusText.Text =
                    "Dữ liệu sao chép không hợp lệ.";

                return;
            }

            PlanningObject pasted =
                PlanningObjectCodecRegistry
                    .Deserialize(
                        node
                    );

            /*
             * Bản paste phải là object hoàn toàn độc lập.
             */
            pasted.Id =
                Guid.NewGuid();

            _planningPasteSerial++;

            OffsetPastedPlanningObject(
                pasted,
                _planningPasteSerial
            );

            using (
                _planningDocument
                    .HistoryTransaction(
                        "Dán đối tượng"
                    )
            )
            {
                _planningDocument.Add(
                    pasted
                );
            }

            MapCanvas.SelectPlanningObject(
                pasted
            );

            MapCanvas.SetPlanningTool(
                MapToolKind.Select
            );

            MapCanvas.InvalidateVisual();

            UpdatePlanningUi();

            PlanningStatusText.Text =
                $"Đã dán: {pasted.Name}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể dán: {ex.Message}";
        }
    }

    private void OffsetPastedPlanningObject(
        PlanningObject item,
        int serial)
    {
        /*
         * Dịch bản copy theo pixel màn hình.
         * Vì vậy ở mọi mức zoom, object paste đều lệch ra
         * một khoảng dễ nhìn thấy.
         */
        Point worldOrigin =
            MapCanvas.ScreenToWorld(
                new Point(
                    0,
                    0
                )
            );

        Point worldOffset =
            MapCanvas.ScreenToWorld(
                new Point(
                    18.0 * serial,
                    18.0 * serial
                )
            );

        double dx =
            worldOffset.X -
            worldOrigin.X;

        double dy =
            worldOffset.Y -
            worldOrigin.Y;

        WorldPoint Shift(
            WorldPoint point)
        {
            return new WorldPoint(
                point.X + dx,
                point.Y + dy
            );
        }

        switch (item)
        {
            case PlanningPolyline line:
            {
                for (
                    int i = 0;
                    i < line.Points.Count;
                    i++
                )
                {
                    line.Points[i] =
                        Shift(
                            line.Points[i]
                        );
                }

                break;
            }

            case PlanningPolygon polygon:
            {
                for (
                    int i = 0;
                    i < polygon.Points.Count;
                    i++
                )
                {
                    polygon.Points[i] =
                        Shift(
                            polygon.Points[i]
                        );
                }

                for (
                    int i = 0;
                    i < polygon.CurveHandles.Count;
                    i++
                )
                {
                    PolygonBezierHandlePair pair =
                        polygon.CurveHandles[i];

                    pair.InHandle =
                        Shift(
                            pair.InHandle
                        );

                    pair.OutHandle =
                        Shift(
                            pair.OutHandle
                        );
                }

                break;
            }

            case PlanningArrow arrow:
            {
                for (
                    int i = 0;
                    i < arrow.Points.Count;
                    i++
                )
                {
                    arrow.Points[i] =
                        Shift(
                            arrow.Points[i]
                        );
                }

                for (
                    int i = 0;
                    i < arrow.CurveHandles.Count;
                    i++
                )
                {
                    ArrowBezierHandlePair pair =
                        arrow.CurveHandles[i];

                    pair.InHandle =
                        Shift(
                            pair.InHandle
                        );

                    pair.OutHandle =
                        Shift(
                            pair.OutHandle
                        );
                }

                break;
            }

            case PlanningText text:
            {
                text.Position =
                    Shift(
                        text.Position
                    );

                break;
            }

            case PlanningSymbol symbol:
            {
                symbol.Position =
                    Shift(
                        symbol.Position
                    );

                break;
            }

            case PlanningDoor door:
            {
                /*
                 * Door không có world position độc lập;
                 * nó bám vào host bằng SegmentIndex + PositionT.
                 */
                door.PositionT =
                    Math.Clamp(
                        door.PositionT +
                        0.06 * serial,
                        0.05,
                        0.95
                    );

                break;
            }
        }
    }
}
