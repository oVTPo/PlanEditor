using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class SelectTool :
    IMapTool
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;
    private readonly ToolManager _manager;

    private PlanningObject? _dragObject;
    private int _dragVertexIndex =
        -1;

    private PlanningDoor? _dragDoor;
    private PlanningText? _dragText;
    private PlanningSymbol? _dragSymbol;
    private PlanningBridge? _dragBridgeWidth;

    /*
     * Circle scale là transform riêng. Không kéo 64 vertex nội bộ,
     * vì như vậy circle có thể bị méo thành polygon.
     */
    private PlanningPolygon? _dragCircle;
    private List<WorldPoint>? _dragCirclePointsBefore;
    private WorldPoint _dragCircleCenterBefore;
    private double _dragCircleStartDistance;
    private double _dragCircleStartAngle;

    private ShapeTransformMode
        _shapeTransformMode =
            ShapeTransformMode.None;

    private enum ShapeTransformMode
    {
        None,
        Scale,
        Rotate
    }

    private enum ShapeScaleHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft
    }

    private ShapeScaleHandle
        _dragShapeScaleHandle =
            ShapeScaleHandle.None;

    private List<Point>?
        _dragShapeScreenPointsBefore;

    private Point
        _dragShapeCenterScreenBefore;

    private Vector
        _dragShapeAxisX;

    private Vector
        _dragShapeAxisY;

    private double
        _dragShapeHalfWidthBefore;

    private double
        _dragShapeHalfHeightBefore;

    private TextTransformMode
        _textTransformMode =
            TextTransformMode.None;

    private double _dragTextFontSizeBefore;
    private double _dragTextRotationBefore;
    private double _dragTextStartDistance;
    private double _dragTextStartAngle;

    private SymbolTransformMode
        _symbolTransformMode =
            SymbolTransformMode.None;

    private double _dragSymbolSizeBefore;
    private double _dragSymbolScreenSizeBefore;
    private double _dragSymbolRotationBefore;
    private double _dragSymbolStartDistance;
    private double _dragSymbolStartAngle;

    private bool _dragging;
    private bool _dragChanged;

    private bool _regionSelecting;
    private Point _regionStart;
    private Point _regionCurrent;
    private bool _regionAdditive;

    private const double RegionDragThreshold =
        4.0;

    private WorldPoint _dragVertexBefore;
    private PlanningArrow? _dragBezierArrow;
    private PlanningPolygon? _dragBezierPolygon;
    private int _dragBezierAnchorIndex = -1;
    private BezierHandleKind _dragBezierHandleKind =
        BezierHandleKind.None;

    private enum BezierHandleKind
    {
        None,
        In,
        Out
    }

    private double _dragDoorBeforeT;
    private WorldPoint _dragTextBefore;
    private WorldPoint _dragSymbolBefore;

    private enum TextTransformMode
    {
        None,
        Move,
        Scale,
        Rotate
    }

    private enum SymbolTransformMode
    {
        None,
        Move,
        Scale,
        Rotate
    }

    public string Name =>
        "Chọn";

    public SelectTool(
        MapCanvas canvas,
        PlanningDocument document,
        ToolManager manager)
    {
        _canvas = canvas;
        _document = document;
        _manager = manager;
    }

    public void Activate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Arrow
            );
    }

    public void Deactivate()
    {
        EndDrag(
            notifyChanged: false
        );

        EndRegionSelection(
            clearOverlay: true
        );
    }

    public bool PointerPressed(
        PointerPressedEventArgs e)
    {
        PointerPoint point =
            e.GetCurrentPoint(
                _canvas
            );

        if (!point.Properties
            .IsLeftButtonPressed)
        {
            return false;
        }

        Point screen =
            e.GetPosition(
                _canvas
            );

        /*
         * PRIORITY -1:
         * Double-click Area/Circle để sửa nhãn.
         * Xử lý trước vertex/Bezier handle để lần click thứ hai
         * không bị drag ăn mất.
         */
        if (e.ClickCount >= 2)
        {
            PlanningObject? doubleHit =
                HitTest(screen);

            if (
                doubleHit is PlanningPolygon polygon
            )
            {
                e.Pointer.Capture(null);

                EndDrag(
                    notifyChanged: false
                );

                _manager.SetSelected(
                    polygon
                );

                _canvas.RequestAreaLabelEdit(
                    polygon
                );

                return true;
            }
        }

        /*
         * PRIORITY 0:
         * Handle scale / rotate của text đang selected.
         */
        if (
            _manager.SelectedObject
                is PlanningText
                    selectedText &&
            !selectedText.IsLocked
        )
        {
            if (TryHitTextRotationHandle(
                    selectedText,
                    screen))
            {
                BeginTextRotate(
                    e,
                    selectedText,
                    screen
                );

                return true;
            }

            if (TryHitTextScaleHandle(
                    selectedText,
                    screen))
            {
                BeginTextScale(
                    e,
                    selectedText,
                    screen
                );

                return true;
            }
        }

        /*
         * PRIORITY 0:
         * Handle scale / rotate của symbol đang selected.
         */
        if (
            _manager.SelectedObject
                is PlanningSymbol
                    selectedSymbol &&
            !selectedSymbol.IsLocked
        )
        {
            if (TryHitSymbolRotationHandle(
                    selectedSymbol,
                    screen))
            {
                BeginSymbolRotate(
                    e,
                    selectedSymbol,
                    screen
                );

                return true;
            }

            if (TryHitSymbolScaleHandle(
                    selectedSymbol,
                    screen))
            {
                BeginSymbolScale(
                    e,
                    selectedSymbol,
                    screen
                );

                return true;
            }
        }

        /*
         * PRIORITY 0.25:
         * Nhóm geometric shape (Ellipse / Rectangle / Hexagon)
         * hiện dùng PlanningAreaKind.Circle.
         *
         * Rotation được kiểm tra trước Scale để hai handle
         * không tranh hit-test khi shape nhỏ.
         */
        if (
            _manager.SelectedObject
                is PlanningPolygon selectedCircle &&
            selectedCircle.AreaKind ==
                PlanningAreaKind.Circle &&
            !selectedCircle.IsLocked
        )
        {
            if (
                TryHitCircleRotationHandle(
                    selectedCircle,
                    screen
                )
            )
            {
                BeginCircleRotate(
                    e,
                    selectedCircle,
                    screen
                );

                return true;
            }

            if (
                TryHitCircleScaleHandle(
                    selectedCircle,
                    screen,
                    out ShapeScaleHandle
                        scaleHandle
                )
            )
            {
                BeginCircleScale(
                    e,
                    selectedCircle,
                    scaleHandle,
                    screen
                );

                return true;
            }
        }

        /*
         * PRIORITY 0.5:
         * Bézier handle của arrow đang selected.
         */
        if (
            _manager.SelectedObject
                is PlanningArrow selectedArrow &&
            selectedArrow.CurveEnabled &&
            !selectedArrow.IsLocked &&
            TryHitBezierHandle(
                selectedArrow,
                screen,
                out int curveAnchorIndex,
                out BezierHandleKind curveHandleKind)
        )
        {
            BeginBezierHandleDrag(
                e,
                selectedArrow,
                curveAnchorIndex,
                curveHandleKind
            );

            return true;
        }

        /*
         * PRIORITY BRIDGE WIDTH:
         * kiểm tra trước vertex.
         */
        if (
            _manager.SelectedObject
                is PlanningBridge selectedBridge &&
            !selectedBridge.IsLocked &&
            TryHitBridgeWidthHandle(
                selectedBridge,
                screen
            )
        )
        {
            BeginBridgeWidthDrag(
                e,
                selectedBridge
            );

            return true;
        }

        /*
         * PRIORITY 1:
         * Node/vertex.
         *
         * Cho phép click trực tiếp vào node của Line / Area / Arrow
         * và kéo ngay, không cần click object trước rồi click node lần nữa.
         */
        if (TryHitVertex(
                screen,
                out PlanningObject?
                    vertexObject,
                out int vertexIndex))
        {
            if (
                vertexObject != null &&
                !vertexObject.IsLocked
            )
            {
                _manager.SetSelected(
                    vertexObject
                );

                BeginVertexDrag(
                    e,
                    vertexObject,
                    vertexIndex
                );

                return true;
            }
        }

        /*
         * PRIORITY 2:
         * Normal object selection.
         */
        PlanningObject? hit =
            HitTest(screen);

        bool additive =
            e.KeyModifiers.HasFlag(
                KeyModifiers.Shift
            );

        if (hit == null)
        {
            BeginRegionSelection(
                e,
                screen,
                additive
            );

            return true;
        }

        if (additive)
        {
            _manager.ToggleSelection(
                hit
            );

            /*
             * Shift+click chỉ thay selection, không bắt đầu drag,
             * tránh vô tình di chuyển object trong multi-selection.
             */
            return true;
        }

        _manager.SetSelected(
            hit
        );

        /*
         * Door không có vertex riêng.
         * Kéo trực tiếp cửa sẽ trượt PositionT dọc theo segment host.
         */
        if (
            hit is PlanningSymbol symbol &&
            !symbol.IsLocked
        )
        {
            BeginSymbolDrag(
                e,
                symbol
            );

            return true;
        }

        if (
            hit is PlanningText text &&
            !text.IsLocked
        )
        {
            BeginTextDrag(
                e,
                text
            );

            return true;
        }

        if (
            hit is PlanningDoor door &&
            !door.IsLocked
        )
        {
            BeginDoorDrag(
                e,
                door
            );

            return true;
        }

        return true;
    }

    public bool PointerMoved(
        PointerEventArgs e)
    {
        if (_regionSelecting)
        {
            _regionCurrent =
                e.GetPosition(
                    _canvas
                );

            _canvas.InvalidateVisual();

            return true;
        }

        if (!_dragging)
            return false;

        Point screen =
            e.GetPosition(
                _canvas
            );

        if (_dragCircle != null)
        {
            if (
                _shapeTransformMode ==
                    ShapeTransformMode.Rotate
            )
            {
                RotateCircle(
                    screen,
                    e.KeyModifiers
                );
            }
            else
            {
                ScaleCircle(
                    screen,
                    e.KeyModifiers
                );
            }

            return true;
        }

        if (
            _dragBezierArrow != null &&
            _dragBezierAnchorIndex >= 0 &&
            _dragBezierHandleKind !=
                BezierHandleKind.None
        )
        {
            MoveBezierHandle(
                screen
            );

            return true;
        }

        if (
            _dragBezierPolygon != null &&
            _dragBezierAnchorIndex >= 0 &&
            _dragBezierHandleKind !=
                BezierHandleKind.None
        )
        {
            MovePolygonBezierHandle(
                screen
            );

            return true;
        }

        if (
            _dragBridgeWidth != null
        )
        {
            MoveBridgeWidthHandle(
                screen
            );

            return true;
        }

        if (
            _dragObject != null &&
            _dragVertexIndex >= 0
        )
        {
            MoveVertex(
                screen
            );

            return true;
        }

        if (_dragSymbol != null)
        {
            if (
                _symbolTransformMode ==
                    SymbolTransformMode.Scale
            )
            {
                ScaleSymbol(
                    screen
                );
            }
            else if (
                _symbolTransformMode ==
                    SymbolTransformMode.Rotate
            )
            {
                RotateSymbol(
                    screen
                );
            }
            else
            {
                MoveSymbol(
                    screen
                );
            }

            return true;
        }

        if (_dragText != null)
        {
            if (
                _textTransformMode ==
                    TextTransformMode.Scale
            )
            {
                ScaleText(
                    screen
                );
            }
            else if (
                _textTransformMode ==
                    TextTransformMode.Rotate
            )
            {
                RotateText(
                    screen
                );
            }
            else
            {
                MoveText(
                    screen
                );
            }

            return true;
        }

        if (_dragDoor != null)
        {
            MoveDoor(
                screen
            );

            return true;
        }

        return false;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        if (_regionSelecting)
        {
            _regionCurrent =
                e.GetPosition(
                    _canvas
                );

            CompleteRegionSelection();

            e.Pointer.Capture(
                null
            );

            return true;
        }

        if (!_dragging)
            return false;

        e.Pointer.Capture(null);

        bool changed =
            _dragChanged;

        if (changed)
        {
            CommitDragHistory();
        }

        EndDrag(
            notifyChanged: false
        );

        return true;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        if (e.Key is
            Key.Delete or
            Key.Back)
        {
            _manager.DeleteSelected();

            return true;
        }

        if (e.Key ==
            Key.Escape)
        {
            if (_regionSelecting)
            {
                EndRegionSelection(
                    clearOverlay: true
                );

                return true;
            }

            if (_dragging)
            {
                /*
                 * MVP hiện tại dừng drag.
                 * Không rollback vị trí vừa kéo.
                 */
                if (_dragChanged)
                {
                    CommitDragHistory();
                }

                EndDrag(
                    notifyChanged:
                        false
                );

                return true;
            }

            _manager.SetSelected(
                null
            );

            return true;
        }

        return false;
    }

    public void RenderOverlay(
        DrawingContext context)
    {
        if (!_regionSelecting)
            return;

        Rect rect =
            GetRegionRect();

        /*
         * Marquee selection chỉ dùng một style trung tính:
         * xám + nét đứt khúc.
         *
         * Hướng kéo vẫn giữ nguyên semantics:
         * trái -> phải = window
         * phải -> trái = crossing
         * nhưng không đổi màu overlay nữa.
         */
        Color strokeColor =
            Color.FromRgb(
                105,
                110,
                116
            );

        Color fillColor =
            Color.FromArgb(
                18,
                strokeColor.R,
                strokeColor.G,
                strokeColor.B
            );

        var fill =
            new SolidColorBrush(
                fillColor
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    strokeColor
                ),
                1.1
            )
            {
                DashStyle =
                    DashStyle.Dash,

                LineCap =
                    PenLineCap.Flat
            };

        context.DrawRectangle(
            fill,
            pen,
            rect
        );
    }

    private void BeginRegionSelection(
        PointerPressedEventArgs e,
        Point screen,
        bool additive)
    {
        EndDrag(
            notifyChanged: false
        );

        _regionSelecting =
            true;

        _regionStart =
            screen;

        _regionCurrent =
            screen;

        _regionAdditive =
            additive;

        if (!additive)
        {
            _manager.SetSelected(
                null
            );
        }

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Cross
            );

        e.Pointer.Capture(
            _canvas
        );

        _canvas.InvalidateVisual();
    }

    private void CompleteRegionSelection()
    {
        if (!_regionSelecting)
            return;

        Rect rect =
            GetRegionRect();

        double distance =
            Math.Sqrt(
                Math.Pow(
                    _regionCurrent.X -
                    _regionStart.X,
                    2.0
                )
                +
                Math.Pow(
                    _regionCurrent.Y -
                    _regionStart.Y,
                    2.0
                )
            );

        if (distance <
            RegionDragThreshold)
        {
            if (!_regionAdditive)
            {
                _manager.SetSelected(
                    null
                );
            }

            EndRegionSelection(
                clearOverlay: true
            );

            return;
        }

        /*
         * CAD-style window/crossing selection:
         *
         * trái -> phải:
         *   chỉ object nằm HOÀN TOÀN trong vùng.
         *
         * phải -> trái:
         *   object chỉ cần GIAO với vùng.
         */
        bool crossing =
            _regionCurrent.X <
            _regionStart.X;

        var matches =
            new List<PlanningObject>();

        foreach (
            PlanningObject item
            in _document.Objects)
        {
            if (!item.IsVisible)
                continue;

            if (!TryGetObjectScreenBounds(
                    item,
                    out Rect bounds))
            {
                continue;
            }

            bool selected =
                crossing
                    ? rect.Intersects(
                        bounds
                    )
                    : rect.Contains(
                        bounds
                    );

            if (selected)
            {
                matches.Add(
                    item
                );
            }
        }

        if (_regionAdditive)
        {
            _manager.AddToSelection(
                matches
            );
        }
        else
        {
            _manager.SetSelection(
                matches
            );
        }

        EndRegionSelection(
            clearOverlay: true
        );
    }

    private void EndRegionSelection(
        bool clearOverlay)
    {
        _regionSelecting =
            false;

        _regionAdditive =
            false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Arrow
            );

        if (clearOverlay)
        {
            _canvas.InvalidateVisual();
        }
    }

    private Rect GetRegionRect()
    {
        double left =
            Math.Min(
                _regionStart.X,
                _regionCurrent.X
            );

        double top =
            Math.Min(
                _regionStart.Y,
                _regionCurrent.Y
            );

        double right =
            Math.Max(
                _regionStart.X,
                _regionCurrent.X
            );

        double bottom =
            Math.Max(
                _regionStart.Y,
                _regionCurrent.Y
            );

        return new Rect(
            left,
            top,
            right - left,
            bottom - top
        );
    }

    private bool TryGetObjectScreenBounds(
        PlanningObject item,
        out Rect bounds)
    {
        if (
            item is PlanningSymbol symbol
        )
        {
            bounds =
                _canvas
                    .GetPlanningSymbolScreenBounds(
                        symbol
                    );

            return true;
        }

        if (
            item is PlanningText text
        )
        {
            bounds =
                _canvas
                    .GetPlanningTextScreenBounds(
                        text
                    );

            return true;
        }

        if (
            item is PlanningDoor door
        )
        {
            if (
                TryGetDoorScreenPoint(
                    door,
                    out Point doorPoint)
            )
            {
                bounds =
                    new Rect(
                        doorPoint.X - 9.0,
                        doorPoint.Y - 9.0,
                        18.0,
                        18.0
                    );

                return true;
            }

            bounds =
                default;

            return false;
        }

        IReadOnlyList<WorldPoint>? points =
            item switch
            {
                PlanningPolyline line =>
                    line.Points,

                PlanningPolygon polygon =>
                    polygon.Points,

                PlanningArrow arrow =>
                    arrow.Points,

                _ =>
                    null
            };

        if (
            points == null ||
            points.Count == 0
        )
        {
            bounds =
                default;

            return false;
        }

        Point first =
            _canvas.WorldToScreen(
                points[0].X,
                points[0].Y
            );

        double minX =
            first.X;

        double minY =
            first.Y;

        double maxX =
            first.X;

        double maxY =
            first.Y;

        for (
            int i = 1;
            i < points.Count;
            i++)
        {
            Point screen =
                _canvas.WorldToScreen(
                    points[i].X,
                    points[i].Y
                );

            minX =
                Math.Min(
                    minX,
                    screen.X
                );

            minY =
                Math.Min(
                    minY,
                    screen.Y
                );

            maxX =
                Math.Max(
                    maxX,
                    screen.X
                );

            maxY =
                Math.Max(
                    maxY,
                    screen.Y
                );
        }

        bounds =
            new Rect(
                minX,
                minY,
                Math.Max(
                    1.0,
                    maxX - minX
                ),
                Math.Max(
                    1.0,
                    maxY - minY
                )
            )
            .Inflate(
                4.0
            );

        return true;
    }


    private bool TryHitBridgeWidthHandle(
        PlanningBridge bridge,
        Point screen)
    {
        if (bridge.Points.Count < 2)
            return false;

        WorldPoint wa = bridge.Points[0];
        WorldPoint wb = bridge.Points[bridge.Points.Count - 1];

        Point a = _canvas.WorldToScreen(wa.X, wa.Y);
        Point b = _canvas.WorldToScreen(wb.X, wb.Y);

        Point h1 =
            BridgeGeometryRenderer.GetWidthHandlePosition(
                a,
                b,
                bridge.BridgeWidthPixels,
                1.0
            );

        Point h2 =
            BridgeGeometryRenderer.GetWidthHandlePosition(
                a,
                b,
                bridge.BridgeWidthPixels,
                -1.0
            );

        const double radius = 15.0;
        double r2 = radius * radius;

        return
            DistanceSquared(screen, h1) <= r2 ||
            DistanceSquared(screen, h2) <= r2;
    }



    private void BeginBridgeWidthDrag(
        PointerPressedEventArgs e,
        PlanningBridge bridge)
    {
        _dragBridgeWidth = bridge;

        _dragObject = null;
        _dragVertexIndex = -1;
        _dragDoor = null;
        _dragText = null;
        _dragSymbol = null;
        _dragBezierArrow = null;
        _dragBezierAnchorIndex = -1;
        _dragBezierHandleKind =
            BezierHandleKind.None;

        _dragging = true;
        _dragChanged = false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeNorthSouth
            );

        e.Pointer.Capture(_canvas);
    }

    private void MoveBridgeWidthHandle(
        Point screen)
    {
        PlanningBridge? bridge = _dragBridgeWidth;

        if (bridge == null || bridge.Points.Count < 2)
            return;

        WorldPoint wa = bridge.Points[0];
        WorldPoint wb = bridge.Points[bridge.Points.Count - 1];

        Point a = _canvas.WorldToScreen(wa.X, wa.Y);
        Point b = _canvas.WorldToScreen(wb.X, wb.Y);

        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len < 0.001)
            return;

        double nx = -dy / len;
        double ny = dx / len;

        Point mid =
            new(
                (a.X + b.X) / 2.0,
                (a.Y + b.Y) / 2.0
            );

        double projection =
            (screen.X - mid.X) * nx +
            (screen.Y - mid.Y) * ny;

        double halfWidth =
            Math.Max(
                2.5,
                Math.Abs(projection) - 16.0
            );

        double width =
            Math.Clamp(
                halfWidth * 2.0,
                5.0,
                120.0
            );

        if (Math.Abs(bridge.BridgeWidthPixels - width) < 0.001)
            return;

        bridge.BridgeWidthPixels = width;
        _dragChanged = true;
        _canvas.InvalidateVisual();
    }



    private void BeginVertexDrag(
        PointerPressedEventArgs e,
        PlanningObject item,
        int vertexIndex)
    {
        _dragObject =
            item;

        _dragVertexIndex =
            vertexIndex;

        _dragDoor =
            null;

        _dragText =
            null;

        _dragBezierArrow =
            null;

        _dragBezierPolygon =
            null;

        _dragBezierAnchorIndex =
            -1;

        _dragBezierHandleKind =
            BezierHandleKind.None;

        _dragSymbol =
            null;

        _dragCircle =
            null;

        _dragCirclePointsBefore =
            null;

        _dragging =
            true;

        _dragChanged =
            false;

        if (!TryGetVertex(
                item,
                vertexIndex,
                out _dragVertexBefore))
        {
            _dragVertexBefore =
                default;
        }

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void BeginDoorDrag(
        PointerPressedEventArgs e,
        PlanningDoor door)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            door;

        _dragText =
            null;

        _dragSymbol =
            null;

        _dragging =
            true;

        _dragChanged =
            false;

        _dragDoorBeforeT =
            door.PositionT;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private bool TryHitCircleRotationHandle(
        PlanningPolygon shape,
        Point screen)
    {
        if (
            shape.AreaKind !=
                PlanningAreaKind.Circle ||
            shape.Points.Count < 3
        )
        {
            return false;
        }

        if (!TryGetShapeHelperFrame(
                shape,
                out Point topLeft,
                out Point topRight,
                out _,
                out _,
                out Vector outward))
        {
            return false;
        }

        Point topMid =
            new Point(
                (topLeft.X + topRight.X) / 2.0,
                (topLeft.Y + topRight.Y) / 2.0
            );

        Point handle =
            topMid +
            outward * 28.0;

        const double hitRadius =
            12.0;

        return
            DistanceSquared(
                screen,
                handle
            ) <=
            hitRadius *
            hitRadius;
    }


    private bool TryGetShapeHelperFrame(
        PlanningPolygon shape,
        out Point topLeft,
        out Point topRight,
        out Point bottomRight,
        out Point bottomLeft,
        out Vector outward)
    {
        topLeft = default;
        topRight = default;
        bottomRight = default;
        bottomLeft = default;
        outward = default;

        if (shape.Points.Count < 3)
            return false;

        var screenPoints =
            new List<Point>(
                shape.Points.Count
            );

        double centerX = 0.0;
        double centerY = 0.0;

        foreach (
            WorldPoint world
            in shape.Points)
        {
            Point p =
                _canvas.WorldToScreen(
                    world.X,
                    world.Y
                );

            screenPoints.Add(
                p
            );

            centerX += p.X;
            centerY += p.Y;
        }

        Point center =
            new Point(
                centerX /
                    screenPoints.Count,
                centerY /
                    screenPoints.Count
            );

        Vector axisX;

        /*
         * Rectangle:
         * point[0] -> point[1] chính là cạnh trên.
         *
         * Hexagon / ellipse:
         * point[0] được sinh tại góc 0 độ,
         * nên vector center -> point[0] là trục ngang local.
         */
        if (screenPoints.Count == 4)
        {
            axisX =
                screenPoints[1] -
                screenPoints[0];
        }
        else
        {
            axisX =
                screenPoints[0] -
                center;
        }

        double axisLength =
            Math.Sqrt(
                axisX.X * axisX.X +
                axisX.Y * axisX.Y
            );

        if (axisLength < 0.001)
            return false;

        axisX =
            new Vector(
                axisX.X /
                    axisLength,
                axisX.Y /
                    axisLength
            );

        Vector axisY =
            new Vector(
                -axisX.Y,
                axisX.X
            );

        double minX =
            double.PositiveInfinity;

        double maxX =
            double.NegativeInfinity;

        double minY =
            double.PositiveInfinity;

        double maxY =
            double.NegativeInfinity;

        foreach (
            Point p
            in screenPoints)
        {
            Vector d =
                p -
                center;

            double px =
                d.X * axisX.X +
                d.Y * axisX.Y;

            double py =
                d.X * axisY.X +
                d.Y * axisY.Y;

            minX =
                Math.Min(
                    minX,
                    px
                );

            maxX =
                Math.Max(
                    maxX,
                    px
                );

            minY =
                Math.Min(
                    minY,
                    py
                );

            maxY =
                Math.Max(
                    maxY,
                    py
                );
        }

        topLeft =
            center +
            axisX * minX +
            axisY * minY;

        topRight =
            center +
            axisX * maxX +
            axisY * minY;

        bottomRight =
            center +
            axisX * maxX +
            axisY * maxY;

        bottomLeft =
            center +
            axisX * minX +
            axisY * maxY;

        /*
         * outward phải đi ra ngoài cạnh "top".
         * axisY tăng theo hướng top -> bottom, nên outward = -axisY.
         */
        outward =
            new Vector(
                -axisY.X,
                -axisY.Y
            );

        return true;
    }


    private void GetShapeScreenBounds(
        PlanningPolygon shape,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.PositiveInfinity;
        minY = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        maxY = double.NegativeInfinity;

        foreach (
            WorldPoint point
            in shape.Points)
        {
            Point p =
                _canvas.WorldToScreen(
                    point.X,
                    point.Y
                );

            minX =
                Math.Min(
                    minX,
                    p.X
                );

            minY =
                Math.Min(
                    minY,
                    p.Y
                );

            maxX =
                Math.Max(
                    maxX,
                    p.X
                );

            maxY =
                Math.Max(
                    maxY,
                    p.Y
                );
        }
    }


    private bool TryHitCircleScaleHandle(
        PlanningPolygon shape,
        Point screen,
        out ShapeScaleHandle handleKind)
    {
        handleKind =
            ShapeScaleHandle.None;

        if (
            shape.AreaKind !=
                PlanningAreaKind.Circle ||
            shape.Points.Count < 3
        )
        {
            return false;
        }

        if (!TryGetShapeHelperFrame(
                shape,
                out Point topLeft,
                out Point topRight,
                out Point bottomRight,
                out Point bottomLeft,
                out _))
        {
            return false;
        }

        const double hitRadius =
            13.0;

        double r2 =
            hitRadius *
            hitRadius;

        if (
            DistanceSquared(
                screen,
                topLeft
            ) <= r2
        )
        {
            handleKind =
                ShapeScaleHandle.TopLeft;

            return true;
        }

        if (
            DistanceSquared(
                screen,
                topRight
            ) <= r2
        )
        {
            handleKind =
                ShapeScaleHandle.TopRight;

            return true;
        }

        if (
            DistanceSquared(
                screen,
                bottomRight
            ) <= r2
        )
        {
            handleKind =
                ShapeScaleHandle.BottomRight;

            return true;
        }

        if (
            DistanceSquared(
                screen,
                bottomLeft
            ) <= r2
        )
        {
            handleKind =
                ShapeScaleHandle.BottomLeft;

            return true;
        }

        return false;
    }



    private void BeginCircleScale(
        PointerPressedEventArgs e,
        PlanningPolygon shape,
        ShapeScaleHandle handleKind,
        Point screen)
    {
        GetCircleCenterAndRadius(
            shape,
            out _dragCircleCenterBefore,
            out _
        );

        _dragCircle =
            shape;

        _shapeTransformMode =
            ShapeTransformMode.Scale;

        _dragShapeScaleHandle =
            handleKind;

        _dragCirclePointsBefore =
            new List<WorldPoint>(
                shape.Points.Count
            );

        _dragShapeScreenPointsBefore =
            new List<Point>(
                shape.Points.Count
            );

        double centerX = 0.0;
        double centerY = 0.0;

        foreach (
            WorldPoint point
            in shape.Points)
        {
            _dragCirclePointsBefore.Add(
                new WorldPoint(
                    point.X,
                    point.Y
                )
            );

            Point p =
                _canvas.WorldToScreen(
                    point.X,
                    point.Y
                );

            _dragShapeScreenPointsBefore.Add(
                p
            );

            centerX += p.X;
            centerY += p.Y;
        }

        _dragShapeCenterScreenBefore =
            new Point(
                centerX /
                    shape.Points.Count,
                centerY /
                    shape.Points.Count
            );

        /*
         * Trục local của shape:
         * - rectangle: cạnh point0 -> point1
         * - ellipse/hexagon: center -> point0
         */
        if (
            _dragShapeScreenPointsBefore.Count ==
                4
        )
        {
            _dragShapeAxisX =
                _dragShapeScreenPointsBefore[1] -
                _dragShapeScreenPointsBefore[0];
        }
        else
        {
            _dragShapeAxisX =
                _dragShapeScreenPointsBefore[0] -
                _dragShapeCenterScreenBefore;
        }

        double axisLength =
            Math.Sqrt(
                _dragShapeAxisX.X *
                    _dragShapeAxisX.X +
                _dragShapeAxisX.Y *
                    _dragShapeAxisX.Y
            );

        if (axisLength < 0.001)
        {
            _dragShapeAxisX =
                new Vector(
                    1.0,
                    0.0
                );
        }
        else
        {
            _dragShapeAxisX =
                new Vector(
                    _dragShapeAxisX.X /
                        axisLength,
                    _dragShapeAxisX.Y /
                        axisLength
                );
        }

        _dragShapeAxisY =
            new Vector(
                -_dragShapeAxisX.Y,
                _dragShapeAxisX.X
            );

        _dragShapeHalfWidthBefore =
            0.0;

        _dragShapeHalfHeightBefore =
            0.0;

        foreach (
            Point p
            in _dragShapeScreenPointsBefore)
        {
            Vector d =
                p -
                _dragShapeCenterScreenBefore;

            double localX =
                d.X *
                    _dragShapeAxisX.X +
                d.Y *
                    _dragShapeAxisX.Y;

            double localY =
                d.X *
                    _dragShapeAxisY.X +
                d.Y *
                    _dragShapeAxisY.Y;

            _dragShapeHalfWidthBefore =
                Math.Max(
                    _dragShapeHalfWidthBefore,
                    Math.Abs(
                        localX
                    )
                );

            _dragShapeHalfHeightBefore =
                Math.Max(
                    _dragShapeHalfHeightBefore,
                    Math.Abs(
                        localY
                    )
                );
        }

        _dragShapeHalfWidthBefore =
            Math.Max(
                3.0,
                _dragShapeHalfWidthBefore
            );

        _dragShapeHalfHeightBefore =
            Math.Max(
                3.0,
                _dragShapeHalfHeightBefore
            );

        _dragCircleStartDistance =
            Math.Max(
                1.0,
                Distance(
                    _dragShapeCenterScreenBefore,
                    screen
                )
            );

        _dragObject = null;
        _dragVertexIndex = -1;
        _dragDoor = null;
        _dragText = null;
        _dragSymbol = null;
        _dragBezierArrow = null;
        _dragBezierPolygon = null;
        _dragBezierAnchorIndex = -1;
        _dragBezierHandleKind =
            BezierHandleKind.None;

        _dragging = true;
        _dragChanged = false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.BottomRightCorner
            );

        e.Pointer.Capture(
            _canvas
        );
    }


    private void BeginCircleRotate(
        PointerPressedEventArgs e,
        PlanningPolygon shape,
        Point screen)
    {
        GetCircleCenterAndRadius(
            shape,
            out _dragCircleCenterBefore,
            out _
        );

        _dragCircle =
            shape;

        _shapeTransformMode =
            ShapeTransformMode.Rotate;

        _dragCirclePointsBefore =
            new List<WorldPoint>(
                shape.Points.Count
            );

        foreach (
            WorldPoint point
            in shape.Points)
        {
            _dragCirclePointsBefore.Add(
                new WorldPoint(
                    point.X,
                    point.Y
                )
            );
        }

        Point centerScreen =
            _canvas.WorldToScreen(
                _dragCircleCenterBefore.X,
                _dragCircleCenterBefore.Y
            );

        _dragCircleStartAngle =
            Math.Atan2(
                screen.Y -
                    centerScreen.Y,
                screen.X -
                    centerScreen.X
            );

        _dragObject = null;
        _dragVertexIndex = -1;
        _dragDoor = null;
        _dragText = null;
        _dragSymbol = null;
        _dragBezierArrow = null;
        _dragBezierPolygon = null;
        _dragBezierAnchorIndex = -1;
        _dragBezierHandleKind =
            BezierHandleKind.None;

        _dragging = true;
        _dragChanged = false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Hand
            );

        e.Pointer.Capture(
            _canvas
        );
    }


    private void RotateCircle(
        Point screen,
        KeyModifiers modifiers)
    {
        PlanningPolygon? shape =
            _dragCircle;

        List<WorldPoint>? source =
            _dragCirclePointsBefore;

        if (
            shape == null ||
            source == null ||
            source.Count < 3 ||
            shape.Points.Count !=
                source.Count
        )
        {
            return;
        }

        Point centerScreen =
            _canvas.WorldToScreen(
                _dragCircleCenterBefore.X,
                _dragCircleCenterBefore.Y
            );

        double currentAngle =
            Math.Atan2(
                screen.Y -
                    centerScreen.Y,
                screen.X -
                    centerScreen.X
            );

        double delta =
            currentAngle -
            _dragCircleStartAngle;

        /*
         * Shift = snap mỗi 15 độ.
         */
        if (
            modifiers.HasFlag(
                KeyModifiers.Shift
            )
        )
        {
            double step =
                Math.PI /
                12.0;

            delta =
                Math.Round(
                    delta /
                    step
                ) *
                step;
        }

        /*
         * World Y và screen Y ngược chiều nhau.
         * Dùng -delta để shape quay theo đúng hướng kéo chuột trên màn hình.
         */
        double worldAngle =
            -delta;

        double cos =
            Math.Cos(
                worldAngle
            );

        double sin =
            Math.Sin(
                worldAngle
            );

        for (
            int i = 0;
            i < source.Count;
            i++
        )
        {
            WorldPoint original =
                source[i];

            double dx =
                original.X -
                _dragCircleCenterBefore.X;

            double dy =
                original.Y -
                _dragCircleCenterBefore.Y;

            shape.Points[i] =
                new WorldPoint(
                    _dragCircleCenterBefore.X +
                        dx * cos -
                        dy * sin,

                    _dragCircleCenterBefore.Y +
                        dx * sin +
                        dy * cos
                );
        }

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }


    private void ScaleCircle(
        Point screen,
        KeyModifiers modifiers)
    {
        PlanningPolygon? shape =
            _dragCircle;

        List<Point>? source =
            _dragShapeScreenPointsBefore;

        if (
            shape == null ||
            source == null ||
            source.Count < 3 ||
            shape.Points.Count !=
                source.Count
        )
        {
            return;
        }

        Vector pointer =
            screen -
            _dragShapeCenterScreenBefore;

        double projectedX =
            pointer.X *
                _dragShapeAxisX.X +
            pointer.Y *
                _dragShapeAxisX.Y;

        double projectedY =
            pointer.X *
                _dragShapeAxisY.X +
            pointer.Y *
                _dragShapeAxisY.Y;

        /*
         * Corner handle scale:
         * mặc định X/Y độc lập => kéo thoải mái thành ellipse,
         * rectangle dài/ngắn, hexagon dẹt/cao.
         */
        double nextHalfWidth =
            Math.Max(
                6.0,
                Math.Abs(
                    projectedX
                )
            );

        double nextHalfHeight =
            Math.Max(
                6.0,
                Math.Abs(
                    projectedY
                )
            );

        double scaleX =
            nextHalfWidth /
            Math.Max(
                1.0,
                _dragShapeHalfWidthBefore
            );

        double scaleY =
            nextHalfHeight /
            Math.Max(
                1.0,
                _dragShapeHalfHeightBefore
            );

        /*
         * Shift = giữ tỷ lệ gốc.
         * Dùng hướng thay đổi mạnh hơn làm scale chung.
         */
        if (
            modifiers.HasFlag(
                KeyModifiers.Shift
            )
        )
        {
            double uniform =
                Math.Abs(
                    scaleX - 1.0
                ) >=
                Math.Abs(
                    scaleY - 1.0
                )
                    ? scaleX
                    : scaleY;

            scaleX =
                uniform;

            scaleY =
                uniform;
        }

        scaleX =
            Math.Clamp(
                scaleX,
                0.02,
                100.0
            );

        scaleY =
            Math.Clamp(
                scaleY,
                0.02,
                100.0
            );

        for (
            int i = 0;
            i < source.Count;
            i++
        )
        {
            Vector d =
                source[i] -
                _dragShapeCenterScreenBefore;

            double localX =
                d.X *
                    _dragShapeAxisX.X +
                d.Y *
                    _dragShapeAxisX.Y;

            double localY =
                d.X *
                    _dragShapeAxisY.X +
                d.Y *
                    _dragShapeAxisY.Y;

            Point nextScreen =
                _dragShapeCenterScreenBefore +
                _dragShapeAxisX *
                    (localX * scaleX) +
                _dragShapeAxisY *
                    (localY * scaleY);

            Point world =
                _canvas.ScreenToWorld(
                    nextScreen
                );

            shape.Points[i] =
                new WorldPoint(
                    world.X,
                    world.Y
                );
        }

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }


    private static void GetCircleCenterAndRadius(
        PlanningPolygon circle,
        out WorldPoint center,
        out double radius)
    {
        if (circle.Points.Count == 0)
        {
            center = default;
            radius = 0.0;
            return;
        }

        double sumX = 0.0;
        double sumY = 0.0;

        foreach (
            WorldPoint point
            in circle.Points)
        {
            sumX += point.X;
            sumY += point.Y;
        }

        center =
            new WorldPoint(
                sumX / circle.Points.Count,
                sumY / circle.Points.Count
            );

        double radiusSum = 0.0;

        foreach (
            WorldPoint point
            in circle.Points)
        {
            double dx =
                point.X - center.X;

            double dy =
                point.Y - center.Y;

            radiusSum +=
                Math.Sqrt(
                    dx * dx +
                    dy * dy
                );
        }

        radius =
            radiusSum /
            circle.Points.Count;
    }

    private void BeginSymbolScale(
        PointerPressedEventArgs e,
        PlanningSymbol symbol,
        Point screen)
    {
        PrepareSymbolTransform(
            symbol
        );

        _symbolTransformMode =
            SymbolTransformMode.Scale;

        _dragSymbolSizeBefore =
            symbol.SizeMeters;
Point center =
            GetSymbolCenter(
                symbol
            );

        _dragSymbolStartDistance =
            Distance(
                center,
                screen
            );

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.BottomRightCorner
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void BeginSymbolRotate(
        PointerPressedEventArgs e,
        PlanningSymbol symbol,
        Point screen)
    {
        PrepareSymbolTransform(
            symbol
        );

        _symbolTransformMode =
            SymbolTransformMode.Rotate;

        _dragSymbolRotationBefore =
            symbol.RotationDegrees;

        Point center =
            GetSymbolCenter(
                symbol
            );

        _dragSymbolStartAngle =
            Math.Atan2(
                screen.Y - center.Y,
                screen.X - center.X
            );

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Cross
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void PrepareSymbolTransform(
        PlanningSymbol symbol)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            null;

        _dragText =
            null;

        _dragSymbol =
            symbol;

        _dragging =
            true;

        _dragChanged =
            false;
    }

    private void BeginSymbolDrag(
        PointerPressedEventArgs e,
        PlanningSymbol symbol)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            null;

        _dragText =
            null;

        _dragSymbol =
            symbol;

        _symbolTransformMode =
            SymbolTransformMode.Move;

        _dragging =
            true;

        _dragChanged =
            false;

        _dragSymbolBefore =
            symbol.Position;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void BeginTextScale(
        PointerPressedEventArgs e,
        PlanningText text,
        Point screen)
    {
        PrepareTextTransform(
            text
        );

        _textTransformMode =
            TextTransformMode.Scale;

        _dragTextFontSizeBefore =
            text.FontSize;

        Point center =
            GetTextCenter(
                text
            );

        _dragTextStartDistance =
            Distance(
                center,
                screen
            );

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.BottomRightCorner
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void BeginTextRotate(
        PointerPressedEventArgs e,
        PlanningText text,
        Point screen)
    {
        PrepareTextTransform(
            text
        );

        _textTransformMode =
            TextTransformMode.Rotate;

        _dragTextRotationBefore =
            text.RotationDegrees;

        Point center =
            GetTextCenter(
                text
            );

        _dragTextStartAngle =
            Math.Atan2(
                screen.Y - center.Y,
                screen.X - center.X
            );

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Cross
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void PrepareTextTransform(
        PlanningText text)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            null;

        _dragText =
            text;

        _dragSymbol =
            null;

        _textTransformMode =
            TextTransformMode.Move;

        _dragging =
            true;

        _dragChanged =
            false;

        _dragTextBefore =
            text.Position;
    }

    private void BeginTextDrag(
        PointerPressedEventArgs e,
        PlanningText text)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            null;

        _dragText =
            text;

        _dragSymbol =
            null;

        _textTransformMode =
            TextTransformMode.Move;

        _dragging =
            true;

        _dragChanged =
            false;

        _dragTextBefore =
            text.Position;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void EndDrag(
        bool notifyChanged)
    {
        _dragObject =
            null;

        _dragVertexIndex =
            -1;

        _dragDoor =
            null;

        _dragText =
            null;

        _dragSymbol =
            null;

        _dragBridgeWidth =
            null;

        _dragCircle =
            null;

        _dragCirclePointsBefore =
            null;

        _shapeTransformMode =
            ShapeTransformMode.None;

        _dragShapeScaleHandle =
            ShapeScaleHandle.None;

        _dragShapeScreenPointsBefore =
            null;

        _dragBezierArrow =
            null;

        _dragBezierPolygon =
            null;

        _dragBezierAnchorIndex =
            -1;

        _dragBezierHandleKind =
            BezierHandleKind.None;

        _symbolTransformMode =
            SymbolTransformMode.None;

        _textTransformMode =
            TextTransformMode.None;

        _dragging =
            false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.Arrow
            );

        if (notifyChanged)
        {
            /*
             * Mark project dirty + trigger normal planning changed pipeline.
             * Save .pas sẽ lưu tọa độ mới.
             */
            _document.NotifyChanged();
        }

        _dragChanged =
            false;

        _canvas.InvalidateVisual();
    }

    private void CommitDragHistory()
    {
        if (
            _dragBridgeWidth != null
        )
        {
            _document.NotifyChanged();
            return;
        }

        if (
            _dragBezierArrow != null ||
            _dragBezierPolygon != null
        )
        {
            _document.NotifyChanged();

            return;
        }

        if (_dragCircle != null)
        {
            /*
             * MapCanvas đang bọc gesture bằng snapshot history.
             * Circle scale chỉ cần phát Changed khi kết thúc gesture.
             */
            _document.NotifyChanged();

            return;
        }

        if (
            _dragObject != null &&
            _dragVertexIndex >= 0 &&
            TryGetVertex(
                _dragObject,
                _dragVertexIndex,
                out WorldPoint after)
        )
        {
            _document.CommitVertexMove(
                _dragObject,
                _dragVertexIndex,
                _dragVertexBefore,
                after
            );

            return;
        }

        if (_dragSymbol != null)
        {
            if (
                _symbolTransformMode ==
                    SymbolTransformMode.Scale
            )
            {
                _document.CommitSymbolSize(
                    _dragSymbol,
                    _dragSymbolSizeBefore,
                    _dragSymbol.SizeMeters
                );

                return;
            }

            if (
                _symbolTransformMode ==
                    SymbolTransformMode.Rotate
            )
            {
                _document.CommitSymbolRotation(
                    _dragSymbol,
                    _dragSymbolRotationBefore,
                    _dragSymbol.RotationDegrees
                );

                return;
            }

            _document.CommitSymbolMove(
                _dragSymbol,
                _dragSymbolBefore,
                _dragSymbol.Position
            );

            return;
        }

        if (_dragText != null)
        {
            /*
             * Generic snapshot history đang bọc toàn gesture ở MapCanvas.
             * Chỉ cần phát Changed; không cần TextScaleAction/TextRotateAction.
             */
            if (
                _textTransformMode ==
                    TextTransformMode.Scale
                ||
                _textTransformMode ==
                    TextTransformMode.Rotate
            )
            {
                _document.NotifyChanged();

                return;
            }

            _document.CommitTextMove(
                _dragText,
                _dragTextBefore,
                _dragText.Position
            );

            return;
        }

        if (_dragDoor != null)
        {
            _document.CommitDoorMove(
                _dragDoor,
                _dragDoorBeforeT,
                _dragDoor.PositionT
            );
        }
    }

    private static bool TryGetVertex(
        PlanningObject item,
        int vertexIndex,
        out WorldPoint point)
    {
        if (
            item is PlanningPolyline line &&
            vertexIndex >= 0 &&
            vertexIndex < line.Points.Count
        )
        {
            point =
                line.Points[
                    vertexIndex
                ];

            return true;
        }

        if (
            item is PlanningPolygon polygon &&
            polygon.AreaKind !=
                PlanningAreaKind.Circle &&
            vertexIndex >= 0 &&
            vertexIndex < polygon.Points.Count
        )
        {
            point =
                polygon.Points[
                    vertexIndex
                ];

            return true;
        }

        if (
            item is PlanningArrow arrow &&
            vertexIndex >= 0 &&
            vertexIndex < arrow.Points.Count
        )
        {
            point =
                arrow.Points[
                    vertexIndex
                ];

            return true;
        }

        point =
            default;

        return false;
    }

    private void MoveVertex(
        Point screen)
    {
        if (
            _dragObject == null ||
            _dragVertexIndex < 0
        )
        {
            return;
        }

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var newPoint =
            new WorldPoint(
                world.X,
                world.Y
            );

        if (
            _dragObject is
                PlanningPolyline line
        )
        {
            if (
                _dragVertexIndex >=
                line.Points.Count
            )
            {
                return;
            }

            line.Points[
                _dragVertexIndex
            ] = newPoint;
        }
        else if (
            _dragObject is
                PlanningPolygon polygon
        )
        {
            if (
                _dragVertexIndex >=
                polygon.Points.Count
            )
            {
                return;
            }

            if (polygon.CurveEnabled)
            {
                polygon.MoveAnchorAndHandles(
                    _dragVertexIndex,
                    newPoint
                );
            }
            else
            {
                polygon.Points[
                    _dragVertexIndex
                ] = newPoint;
            }
        }
        else if (
            _dragObject is
                PlanningArrow arrow
        )
        {
            if (
                _dragVertexIndex >=
                arrow.Points.Count
            )
            {
                return;
            }

            if (arrow.CurveEnabled)
            {
                arrow.MoveAnchorAndHandles(
                    _dragVertexIndex,
                    newPoint
                );
            }
            else
            {
                arrow.Points[
                    _dragVertexIndex
                ] = newPoint;
            }
        }
        else
        {
            return;
        }

        _dragChanged =
            true;

        /*
         * Door gắn line/polygon dùng HostObjectId + SegmentIndex + PositionT,
         * nên khi vertex host di chuyển, door sẽ tự render theo segment mới.
         */
        _canvas.InvalidateVisual();
    }

    private void ScaleSymbol(
        Point screen)
    {
        PlanningSymbol? symbol =
            _dragSymbol;

        if (symbol == null)
            return;

        Point center =
            GetSymbolCenter(
                symbol
            );

        double distance =
            Distance(
                center,
                screen
            );

        if (
            _dragSymbolStartDistance <=
                0.001
        )
        {
            return;
        }

        double factor =
            distance /
            _dragSymbolStartDistance;

        double next =
            Math.Clamp(
                _dragSymbolSizeBefore *
                    factor,
                1.0,
                500.0
            );

        if (
            Math.Abs(
                symbol.SizeMeters -
                next
            ) < 0.0001
        )
        {
            return;
        }

        symbol.SizeMeters =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private void RotateSymbol(
        Point screen)
    {
        PlanningSymbol? symbol =
            _dragSymbol;

        if (symbol == null)
            return;

        Point center =
            GetSymbolCenter(
                symbol
            );

        double currentAngle =
            Math.Atan2(
                screen.Y - center.Y,
                screen.X - center.X
            );

        double deltaDegrees =
            (
                currentAngle -
                _dragSymbolStartAngle
            )
            *
            180.0 /
            Math.PI;

        double next =
            NormalizeDegrees(
                _dragSymbolRotationBefore +
                deltaDegrees
            );

        if (
            Math.Abs(
                ShortestAngleDelta(
                    symbol.RotationDegrees,
                    next
                )
            ) < 0.0001
        )
        {
            return;
        }

        symbol.RotationDegrees =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private Point GetSymbolCenter(
        PlanningSymbol symbol)
    {
        Rect box =
            _canvas
                .GetPlanningSymbolScreenBounds(
                    symbol
                );

        return box.Center;
    }

    private bool TryHitSymbolScaleHandle(
        PlanningSymbol symbol,
        Point screen)
    {
        Point handle =
            _canvas
                .GetPlanningSymbolScaleHandle(
                    symbol
                );

        return Distance(
                handle,
                screen
            ) <= 10.0;
    }

    private bool TryHitSymbolRotationHandle(
        PlanningSymbol symbol,
        Point screen)
    {
        Point handle =
            _canvas
                .GetPlanningSymbolRotationHandle(
                    symbol
                );

        return Distance(
                handle,
                screen
            ) <= 10.0;
    }

    private static double Distance(
        Point a,
        Point b)
    {
        double dx =
            a.X - b.X;

        double dy =
            a.Y - b.Y;

        return Math.Sqrt(
            dx * dx +
            dy * dy
        );
    }

    private static double NormalizeDegrees(
        double value)
    {
        double result =
            value % 360.0;

        if (result < 0.0)
        {
            result += 360.0;
        }

        return result;
    }

    private static double ShortestAngleDelta(
        double from,
        double to)
    {
        double delta =
            NormalizeDegrees(
                to
            )
            -
            NormalizeDegrees(
                from
            );

        if (delta > 180.0)
            delta -= 360.0;

        if (delta < -180.0)
            delta += 360.0;

        return delta;
    }

    private void MoveSymbol(
        Point screen)
    {
        PlanningSymbol? symbol =
            _dragSymbol;

        if (symbol == null)
            return;

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var next =
            new WorldPoint(
                world.X,
                world.Y
            );

        if (
            Math.Abs(
                next.X -
                symbol.Position.X
            ) < 0.000001
            &&
            Math.Abs(
                next.Y -
                symbol.Position.Y
            ) < 0.000001
        )
        {
            return;
        }

        symbol.Position =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private void ScaleText(
        Point screen)
    {
        PlanningText? text =
            _dragText;

        if (text == null)
            return;

        Point center =
            GetTextCenter(
                text
            );

        double distance =
            Distance(
                center,
                screen
            );

        if (
            _dragTextStartDistance <=
                0.001
        )
        {
            return;
        }

        double factor =
            distance /
            _dragTextStartDistance;

        double next =
            Math.Clamp(
                _dragTextFontSizeBefore *
                    factor,
                8.0,
                300.0
            );

        if (
            Math.Abs(
                text.FontSize -
                next
            ) < 0.0001
        )
        {
            return;
        }

        text.FontSize =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private void RotateText(
        Point screen)
    {
        PlanningText? text =
            _dragText;

        if (text == null)
            return;

        Point center =
            GetTextCenter(
                text
            );

        double currentAngle =
            Math.Atan2(
                screen.Y - center.Y,
                screen.X - center.X
            );

        double deltaDegrees =
            (
                currentAngle -
                _dragTextStartAngle
            )
            *
            180.0 /
            Math.PI;

        double next =
            NormalizeDegrees(
                _dragTextRotationBefore +
                deltaDegrees
            );

        if (
            Math.Abs(
                ShortestAngleDelta(
                    text.RotationDegrees,
                    next
                )
            ) < 0.0001
        )
        {
            return;
        }

        text.RotationDegrees =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private Point GetTextCenter(
        PlanningText text)
    {
        return
            _canvas
                .GetPlanningTextScreenBounds(
                    text
                )
                .Center;
    }

    private bool TryHitTextScaleHandle(
        PlanningText text,
        Point screen)
    {
        Point handle =
            _canvas
                .GetPlanningTextScaleHandle(
                    text
                );

        return Distance(
                handle,
                screen
            ) <= 10.0;
    }

    private bool TryHitTextRotationHandle(
        PlanningText text,
        Point screen)
    {
        Point handle =
            _canvas
                .GetPlanningTextRotationHandle(
                    text
                );

        return Distance(
                handle,
                screen
            ) <= 10.0;
    }

    private void MoveText(
        Point screen)
    {
        PlanningText? text =
            _dragText;

        if (text == null)
            return;

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var next =
            new WorldPoint(
                world.X,
                world.Y
            );

        if (
            Math.Abs(
                next.X -
                text.Position.X
            ) <
            0.000001
            &&
            Math.Abs(
                next.Y -
                text.Position.Y
            ) <
            0.000001
        )
        {
            return;
        }

        text.Position =
            next;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private void MoveDoor(
        Point screen)
    {
        PlanningDoor? door =
            _dragDoor;

        if (door == null)
            return;

        PlanningObject? host =
            FindObjectById(
                door.HostObjectId
            );

        if (host == null)
            return;

        if (!TryGetHostSegment(
                host,
                door.SegmentIndex,
                out Point a,
                out Point b))
        {
            return;
        }

        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double lengthSquared =
            dx * dx +
            dy * dy;

        if (lengthSquared <=
            double.Epsilon)
        {
            return;
        }

        double t =
            (
                (screen.X - a.X) *
                    dx +
                (screen.Y - a.Y) *
                    dy
            ) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0.0,
                1.0
            );

        if (
            Math.Abs(
                door.PositionT -
                t
            ) <
            0.000001
        )
        {
            return;
        }

        door.PositionT =
            t;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private bool TryGetDoorScreenPoint(
        PlanningDoor door,
        out Point screenPoint)
    {
        screenPoint =
            default;

        PlanningObject? host =
            _document.Objects
                .FirstOrDefault(
                    item =>
                        item.Id ==
                        door.HostObjectId
                );

        if (
            host == null ||
            !TryGetHostSegment(
                host,
                door.SegmentIndex,
                out Point a,
                out Point b)
        )
        {
            return false;
        }

        double t =
            Math.Clamp(
                door.PositionT,
                0.0,
                1.0
            );

        screenPoint =
            new Point(
                a.X +
                    (b.X - a.X) *
                    t,
                a.Y +
                    (b.Y - a.Y) *
                    t
            );

        return true;
    }

    private bool TryHitBezierHandle(
        PlanningArrow arrow,
        Point screen,
        out int anchorIndex,
        out BezierHandleKind kind)
    {
        arrow.EnsureCurveHandles();

        const double radius = 8.0;
        double radiusSquared = radius * radius;

        for (
            int i = arrow.CurveHandles.Count - 1;
            i >= 0;
            i--)
        {
            ArrowBezierHandlePair pair =
                arrow.CurveHandles[i];

            Point inPoint =
                _canvas.WorldToScreen(
                    pair.InHandle.X,
                    pair.InHandle.Y
                );

            if (
                DistanceSquared(
                    screen,
                    inPoint
                ) <= radiusSquared
            )
            {
                anchorIndex = i;
                kind = BezierHandleKind.In;
                return true;
            }

            Point outPoint =
                _canvas.WorldToScreen(
                    pair.OutHandle.X,
                    pair.OutHandle.Y
                );

            if (
                DistanceSquared(
                    screen,
                    outPoint
                ) <= radiusSquared
            )
            {
                anchorIndex = i;
                kind = BezierHandleKind.Out;
                return true;
            }
        }

        anchorIndex = -1;
        kind = BezierHandleKind.None;
        return false;
    }

    private void BeginBezierHandleDrag(
        PointerPressedEventArgs e,
        PlanningArrow arrow,
        int anchorIndex,
        BezierHandleKind kind)
    {
        arrow.EnsureCurveHandles();

        _dragBezierArrow = arrow;
        _dragBezierAnchorIndex = anchorIndex;
        _dragBezierHandleKind = kind;

        _dragObject = null;
        _dragVertexIndex = -1;
        _dragDoor = null;
        _dragText = null;
        _dragSymbol = null;

        _dragging = true;
        _dragChanged = false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void MoveBezierHandle(
        Point screen)
    {
        if (
            _dragBezierArrow == null ||
            _dragBezierAnchorIndex < 0 ||
            _dragBezierAnchorIndex >=
                _dragBezierArrow.CurveHandles.Count
        )
        {
            return;
        }

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var point =
            new WorldPoint(
                world.X,
                world.Y
            );

        ArrowBezierHandlePair pair =
            _dragBezierArrow
                .CurveHandles[
                    _dragBezierAnchorIndex
                ];

        WorldPoint anchor =
            _dragBezierArrow
                .Points[
                    _dragBezierAnchorIndex
                ];

        if (
            _dragBezierHandleKind ==
                BezierHandleKind.In
        )
        {
            pair.InHandle = point;

            pair.OutHandle =
                new WorldPoint(
                    anchor.X +
                        (anchor.X - point.X),
                    anchor.Y +
                        (anchor.Y - point.Y)
                );
        }
        else if (
            _dragBezierHandleKind ==
                BezierHandleKind.Out
        )
        {
            pair.OutHandle = point;

            pair.InHandle =
                new WorldPoint(
                    anchor.X +
                        (anchor.X - point.X),
                    anchor.Y +
                        (anchor.Y - point.Y)
                );
        }

        pair.IsCustom = true;
        _dragChanged = true;

        _canvas.InvalidateVisual();
    }

    private bool TryHitPolygonBezierHandle(
        PlanningPolygon polygon,
        Point screen,
        out int anchorIndex,
        out BezierHandleKind kind)
    {
        if (
            !polygon.CurveEnabled ||
            polygon.AreaKind ==
                PlanningAreaKind.Circle
        )
        {
            anchorIndex = -1;
            kind = BezierHandleKind.None;
            return false;
        }

        polygon.EnsureCurveHandles();

        const double radius = 8.0;
        double radiusSquared =
            radius * radius;

        for (
            int i = polygon.CurveHandles.Count - 1;
            i >= 0;
            i--)
        {
            PolygonBezierHandlePair pair =
                polygon.CurveHandles[i];

            Point inPoint =
                _canvas.WorldToScreen(
                    pair.InHandle.X,
                    pair.InHandle.Y
                );

            if (
                DistanceSquared(
                    screen,
                    inPoint
                ) <= radiusSquared
            )
            {
                anchorIndex = i;
                kind = BezierHandleKind.In;
                return true;
            }

            Point outPoint =
                _canvas.WorldToScreen(
                    pair.OutHandle.X,
                    pair.OutHandle.Y
                );

            if (
                DistanceSquared(
                    screen,
                    outPoint
                ) <= radiusSquared
            )
            {
                anchorIndex = i;
                kind = BezierHandleKind.Out;
                return true;
            }
        }

        anchorIndex = -1;
        kind = BezierHandleKind.None;
        return false;
    }

    private void BeginPolygonBezierHandleDrag(
        PointerPressedEventArgs e,
        PlanningPolygon polygon,
        int anchorIndex,
        BezierHandleKind kind)
    {
        polygon.EnsureCurveHandles();

        _dragBezierPolygon =
            polygon;

        _dragBezierArrow =
            null;

        _dragBezierAnchorIndex =
            anchorIndex;

        _dragBezierHandleKind =
            kind;

        _dragObject = null;
        _dragVertexIndex = -1;
        _dragDoor = null;
        _dragText = null;
        _dragSymbol = null;
        _dragCircle = null;
        _dragCirclePointsBefore = null;

        _dragging = true;
        _dragChanged = false;

        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );

        e.Pointer.Capture(
            _canvas
        );
    }

    private void MovePolygonBezierHandle(
        Point screen)
    {
        PlanningPolygon? polygon =
            _dragBezierPolygon;

        if (
            polygon == null ||
            _dragBezierAnchorIndex < 0 ||
            _dragBezierAnchorIndex >=
                polygon.CurveHandles.Count ||
            _dragBezierAnchorIndex >=
                polygon.Points.Count
        )
        {
            return;
        }

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        var point =
            new WorldPoint(
                world.X,
                world.Y
            );

        PolygonBezierHandlePair pair =
            polygon.CurveHandles[
                _dragBezierAnchorIndex
            ];

        WorldPoint anchor =
            polygon.Points[
                _dragBezierAnchorIndex
            ];

        if (
            _dragBezierHandleKind ==
                BezierHandleKind.In
        )
        {
            pair.InHandle =
                point;

            pair.OutHandle =
                new WorldPoint(
                    anchor.X +
                        (anchor.X - point.X),
                    anchor.Y +
                        (anchor.Y - point.Y)
                );
        }
        else if (
            _dragBezierHandleKind ==
                BezierHandleKind.Out
        )
        {
            pair.OutHandle =
                point;

            pair.InHandle =
                new WorldPoint(
                    anchor.X +
                        (anchor.X - point.X),
                    anchor.Y +
                        (anchor.Y - point.Y)
                );
        }
        else
        {
            return;
        }

        pair.IsCustom =
            true;

        _dragChanged =
            true;

        _canvas.InvalidateVisual();
    }

    private bool TryHitVertex(
        Point screen,
        out PlanningObject? item,
        out int vertexIndex)
    {
        const double radiusPixels =
            9.0;

        double radiusSquared =
            radiusPixels *
            radiusPixels;

        /*
         * Selected object ưu tiên trước để handle không bị object khác
         * đang chồng lên giành hit.
         */
        PlanningObject? selected =
            _manager.SelectedObject;

        if (
            selected != null &&
            selected.IsVisible &&
            TryHitVertexOnObject(
                selected,
                screen,
                radiusSquared,
                out vertexIndex)
        )
        {
            item =
                selected;

            return true;
        }

        for (
            int objectIndex =
                _document.Objects.Count - 1;
            objectIndex >= 0;
            objectIndex--)
        {
            PlanningObject candidate =
                _document.Objects[
                    objectIndex
                ];

            if (
                !candidate.IsVisible ||
                ReferenceEquals(
                    candidate,
                    selected
                )
            )
            {
                continue;
            }

            if (TryHitVertexOnObject(
                    candidate,
                    screen,
                    radiusSquared,
                    out vertexIndex))
            {
                item =
                    candidate;

                return true;
            }
        }

        item =
            null;

        vertexIndex =
            -1;

        return false;
    }

    private bool TryHitVertexOnObject(
        PlanningObject item,
        Point screen,
        double radiusSquared,
        out int vertexIndex)
    {
        if (
            item is PlanningPolyline line
        )
        {
            return TryHitPointList(
                line.Points,
                screen,
                radiusSquared,
                out vertexIndex
            );
        }

        if (
            item is PlanningPolygon polygon
        )
        {
            /*
             * Circle phải luôn giữ hình tròn chuẩn.
             * Không cho hit 64 vertex nội bộ để kéo méo object.
             */
            if (
                polygon.AreaKind ==
                    PlanningAreaKind.Circle
            )
            {
                vertexIndex =
                    -1;

                return false;
            }

            return TryHitPointList(
                polygon.Points,
                screen,
                radiusSquared,
                out vertexIndex
            );
        }

        if (
            item is PlanningArrow arrow
        )
        {
            return TryHitPointList(
                arrow.Points,
                screen,
                radiusSquared,
                out vertexIndex
            );
        }

        vertexIndex =
            -1;

        return false;
    }

    private bool TryHitPointList(
        System.Collections.Generic.IReadOnlyList<WorldPoint>
            points,
        Point screen,
        double radiusSquared,
        out int vertexIndex)
    {
        for (
            int i =
                points.Count - 1;
            i >= 0;
            i--)
        {
            Point node =
                _canvas.WorldToScreen(
                    points[i].X,
                    points[i].Y
                );

            if (
                DistanceSquared(
                    screen,
                    node
                ) <=
                radiusSquared
            )
            {
                vertexIndex =
                    i;

                return true;
            }
        }

        vertexIndex =
            -1;

        return false;
    }

    private PlanningObject? HitTest(
        Point screen)
    {
        const double tolerancePixels =
            8.0;

        for (
            int objectIndex =
                _document.Objects.Count - 1;
            objectIndex >= 0;
            objectIndex--)
        {
            PlanningObject item =
                _document.Objects[
                    objectIndex
                ];

            if (!item.IsVisible)
                continue;

            if (
                item is
                    PlanningSymbol symbol
            )
            {
                if (
                    _canvas
                        .HitTestPlanningSymbol(
                            symbol,
                            screen,
                            6.0
                        )
                )
                {
                    return item;
                }

                continue;
            }

            if (
                item is
                    PlanningText text
            )
            {
                if (
                    _canvas
                        .HitTestPlanningText(
                            text,
                            screen,
                            10.0
                        )
                )
                {
                    return item;
                }

                continue;
            }

            if (
                item is
                    PlanningDoor door
            )
            {
                if (HitDoor(
                        screen,
                        door))
                {
                    return item;
                }

                continue;
            }

            if (
                item is
                    PlanningArrow arrow
            )
            {
                if (HitArrow(
                        screen,
                        arrow,
                        tolerancePixels))
                {
                    return item;
                }

                continue;
            }

            if (
                item is
                    PlanningPolyline line
            )
            {
                for (
                    int i = 0;
                    i <
                    line.Points.Count - 1;
                    i++)
                {
                    Point a =
                        _canvas.WorldToScreen(
                            line.Points[i].X,
                            line.Points[i].Y
                        );

                    Point b =
                        _canvas.WorldToScreen(
                            line.Points[
                                i + 1
                            ].X,
                            line.Points[
                                i + 1
                            ].Y
                        );

                    if (
                        DistanceToSegment(
                            screen,
                            a,
                            b
                        ) <=
                        tolerancePixels
                    )
                    {
                        return item;
                    }
                }

                continue;
            }

            if (
                item is
                    PlanningPolygon polygon
            )
            {
                if (HitPolygon(
                        screen,
                        polygon,
                        tolerancePixels))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private bool HitArrow(
        Point screen,
        PlanningArrow arrow,
        double tolerancePixels)
    {
        if (arrow.Points.Count < 2)
            return false;

        double hitTolerance =
            Math.Max(
                tolerancePixels,
                arrow.StrokeWidth *
                    0.5 +
                5.0
            );

        for (
            int i = 0;
            i <
            arrow.Points.Count - 1;
            i++)
        {
            Point a =
                _canvas.WorldToScreen(
                    arrow.Points[i].X,
                    arrow.Points[i].Y
                );

            Point b =
                _canvas.WorldToScreen(
                    arrow.Points[
                        i + 1
                    ].X,
                    arrow.Points[
                        i + 1
                    ].Y
                );

            if (
                DistanceToSegment(
                    screen,
                    a,
                    b
                ) <=
                hitTolerance
            )
            {
                return true;
            }
        }

        Point start =
            _canvas.WorldToScreen(
                arrow.Points[0].X,
                arrow.Points[0].Y
            );

        Point end =
            _canvas.WorldToScreen(
                arrow.Points[^1].X,
                arrow.Points[^1].Y
            );

        const double headRadius =
            14.0;

        return
            DistanceSquared(
                screen,
                start
            ) <=
            headRadius *
            headRadius
            ||
            DistanceSquared(
                screen,
                end
            ) <=
            headRadius *
            headRadius;
    }

    private bool HitDoor(
        Point screen,
        PlanningDoor door)
    {
        PlanningObject? host =
            FindObjectById(
                door.HostObjectId
            );

        if (host == null)
            return false;

        if (!TryGetHostSegment(
                host,
                door.SegmentIndex,
                out Point a,
                out Point b))
        {
            return false;
        }

        Point center =
            new(
                a.X +
                (
                    b.X - a.X
                ) *
                door.PositionT,

                a.Y +
                (
                    b.Y - a.Y
                ) *
                door.PositionT
            );

        /*
         * Door renderer hiện dùng capped zoom scale.
         * Hit target tối thiểu 10px để cửa nhỏ vẫn dễ chọn.
         */
        double doorWidthPixels =
            door.GapWidthMeters /
            _canvas.MetersPerPixel;

        double radius =
            Math.Max(
                10.0,
                Math.Min(
                    28.0,
                    doorWidthPixels /
                    2.0
                )
            );

        return
            DistanceSquared(
                screen,
                center
            ) <=
            radius *
            radius;
    }

    private PlanningObject? FindObjectById(
        Guid id)
    {
        foreach (
            PlanningObject candidate
            in _document.Objects)
        {
            if (candidate.Id ==
                id)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryGetHostSegment(
        PlanningObject host,
        int segmentIndex,
        out Point a,
        out Point b)
    {
        a =
            default;

        b =
            default;

        if (
            host is
                PlanningPolyline line
        )
        {
            if (
                segmentIndex < 0 ||
                segmentIndex >=
                    line.Points.Count -
                    1
            )
            {
                return false;
            }

            a =
                _canvas.WorldToScreen(
                    line.Points[
                        segmentIndex
                    ].X,
                    line.Points[
                        segmentIndex
                    ].Y
                );

            b =
                _canvas.WorldToScreen(
                    line.Points[
                        segmentIndex +
                        1
                    ].X,
                    line.Points[
                        segmentIndex +
                        1
                    ].Y
                );

            return true;
        }

        if (
            host is
                PlanningPolygon polygon
        )
        {
            int count =
                polygon.Points.Count;

            if (
                count < 3 ||
                segmentIndex < 0 ||
                segmentIndex >= count
            )
            {
                return false;
            }

            int next =
                (
                    segmentIndex + 1
                ) %
                count;

            a =
                _canvas.WorldToScreen(
                    polygon.Points[
                        segmentIndex
                    ].X,
                    polygon.Points[
                        segmentIndex
                    ].Y
                );

            b =
                _canvas.WorldToScreen(
                    polygon.Points[
                        next
                    ].X,
                    polygon.Points[
                        next
                    ].Y
                );

            return true;
        }

        return false;
    }

    private bool HitPolygon(
        Point screen,
        PlanningPolygon polygon,
        double tolerancePixels)
    {
        if (polygon.Points.Count < 3)
            return false;

        bool inside =
            false;

        int count =
            polygon.Points.Count;

        Point previous =
            _canvas.WorldToScreen(
                polygon.Points[
                    count - 1
                ].X,
                polygon.Points[
                    count - 1
                ].Y
            );

        for (
            int i = 0;
            i < count;
            i++)
        {
            Point current =
                _canvas.WorldToScreen(
                    polygon.Points[i].X,
                    polygon.Points[i].Y
                );

            if (
                DistanceToSegment(
                    screen,
                    previous,
                    current
                ) <=
                tolerancePixels
            )
            {
                return true;
            }

            bool crosses =
                (
                    current.Y >
                    screen.Y
                ) !=
                (
                    previous.Y >
                    screen.Y
                );

            if (crosses)
            {
                double denominator =
                    previous.Y -
                    current.Y;

                if (
                    Math.Abs(
                        denominator
                    ) >
                    double.Epsilon
                )
                {
                    double xAtY =
                        (
                            previous.X -
                            current.X
                        ) *
                        (
                            screen.Y -
                            current.Y
                        ) /
                        denominator +
                        current.X;

                    if (
                        screen.X <
                        xAtY
                    )
                    {
                        inside =
                            !inside;
                    }
                }
            }

            previous =
                current;
        }

        return inside;
    }

    private static double
        DistanceToSegment(
            Point p,
            Point a,
            Point b)
    {
        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double lengthSquared =
            dx * dx +
            dy * dy;

        if (
            lengthSquared <=
            double.Epsilon
        )
        {
            return Math.Sqrt(
                DistanceSquared(
                    p,
                    a
                )
            );
        }

        double t =
            (
                (p.X - a.X) *
                    dx +
                (p.Y - a.Y) *
                    dy
            ) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0.0,
                1.0
            );

        double nearestX =
            a.X +
            t * dx;

        double nearestY =
            a.Y +
            t * dy;

        double ox =
            p.X -
            nearestX;

        double oy =
            p.Y -
            nearestY;

        return Math.Sqrt(
            ox * ox +
            oy * oy
        );
    }

    private static double
        DistanceSquared(
            Point a,
            Point b)
    {
        double dx =
            a.X - b.X;

        double dy =
            a.Y - b.Y;

        return
            dx * dx +
            dy * dy;
    }
}
