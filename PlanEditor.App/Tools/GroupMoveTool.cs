
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

/// <summary>
/// Di chuyển toàn bộ selection hiện tại.
///
/// Quan trọng:
/// - Snapshot toàn bộ geometry ở thời điểm PointerPressed.
/// - Mỗi PointerMoved tính delta từ điểm bắt đầu kéo.
/// - Không cộng dồn delta frame-by-frame.
///
/// Cách này tránh drift/sai số và tránh object "nhảy" khi document redraw,
/// đặc biệt với geometry nhỏ nằm trên tọa độ WebMercator lớn.
/// </summary>
public sealed class GroupMoveTool :
    IMapTool
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;
    private readonly ToolManager _manager;

    private bool _dragging;

    private WorldPoint _dragStartWorld;

    private readonly Dictionary<
        PlanningObject,
        ObjectSnapshot
    > _snapshots =
        new();

    public string Name =>
        "Di chuyển khối";

    public GroupMoveTool(
        MapCanvas canvas,
        PlanningDocument document,
        ToolManager manager)
    {
        _canvas =
            canvas;

        _document =
            document;

        _manager =
            manager;
    }

    public void Activate()
    {
        _canvas.Cursor =
            new Cursor(
                StandardCursorType.SizeAll
            );
    }

    public void Deactivate()
    {
        EndDrag();
    }

    public bool PointerPressed(
        PointerPressedEventArgs e)
    {
        PointerPoint pointer =
            e.GetCurrentPoint(
                _canvas
            );

        if (
            !pointer.Properties
                .IsLeftButtonPressed
        )
        {
            return false;
        }

        if (
            _manager.SelectionCount ==
            0
        )
        {
            return false;
        }

        _snapshots.Clear();

        foreach (
            PlanningObject item
            in _manager.SelectedObjects)
        {
            if (item.IsLocked)
                continue;

            ObjectSnapshot? snapshot =
                CreateSnapshot(
                    item
                );

            if (snapshot != null)
            {
                _snapshots[item] =
                    snapshot;
            }
        }

        if (_snapshots.Count == 0)
            return false;

        Point screen =
            e.GetPosition(
                _canvas
            );

        Point world =
            _canvas.ScreenToWorld(
                screen
            );

        _dragStartWorld =
            new WorldPoint(
                world.X,
                world.Y
            );

        _dragging =
            true;

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

        Point screen =
            e.GetPosition(
                _canvas
            );

        Point currentPoint =
            _canvas.ScreenToWorld(
                screen
            );

        double dx =
            currentPoint.X -
            _dragStartWorld.X;

        double dy =
            currentPoint.Y -
            _dragStartWorld.Y;

        foreach (
            KeyValuePair<
                PlanningObject,
                ObjectSnapshot
            > pair
            in _snapshots)
        {
            ApplySnapshotOffset(
                pair.Key,
                pair.Value,
                dx,
                dy
            );
        }

        _document.NotifyChanged();

        _canvas.InvalidateVisual();

        return true;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return false;

        _dragging =
            false;

        _snapshots.Clear();

        e.Pointer.Capture(
            null
        );

        _canvas.InvalidateVisual();

        return true;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        if (
            e.Key ==
                Key.Escape &&
            _dragging
        )
        {
            /*
             * Esc: trả toàn bộ object về snapshot ban đầu.
             */
            foreach (
                KeyValuePair<
                    PlanningObject,
                    ObjectSnapshot
                > pair
                in _snapshots)
            {
                ApplySnapshotOffset(
                    pair.Key,
                    pair.Value,
                    0.0,
                    0.0
                );
            }

            _document.NotifyChanged();

            EndDrag();

            return true;
        }

        return false;
    }

    public void RenderOverlay(
        Avalonia.Media.DrawingContext context)
    {
    }

    private void EndDrag()
    {
        _dragging =
            false;

        _snapshots.Clear();

        _canvas.InvalidateVisual();
    }

    private static ObjectSnapshot?
        CreateSnapshot(
            PlanningObject item)
    {
        switch (item)
        {
            case PlanningPolyline line:
                return new ObjectSnapshot(
                    CopyPoints(
                        line.Points
                    )
                );

            case PlanningPolygon polygon:
                if (polygon.CurveEnabled)
                {
                    polygon.EnsureCurveHandles();
                }

                return new ObjectSnapshot(
                    CopyPoints(
                        polygon.Points
                    ),
                    CopyPolygonHandles(
                        polygon.CurveHandles
                    )
                );

            case PlanningArrow arrow:
                if (arrow.CurveEnabled)
                {
                    arrow.EnsureCurveHandles();
                }

                return new ObjectSnapshot(
                    CopyPoints(
                        arrow.Points
                    ),
                    CopyArrowHandles(
                        arrow.CurveHandles
                    )
                );

            case PlanningText text:
                return new ObjectSnapshot(
                    text.Position
                );

            case PlanningSymbol symbol:
                return new ObjectSnapshot(
                    symbol.Position
                );

            /*
             * Door bám host bằng PositionT.
             * Khi host được translate thì door tự đi theo.
             */
            case PlanningDoor:
                return null;

            default:
                return null;
        }
    }

    private static void ApplySnapshotOffset(
        PlanningObject item,
        ObjectSnapshot snapshot,
        double dx,
        double dy)
    {
        switch (item)
        {
            case PlanningPolyline line:
                ApplyPoints(
                    line.Points,
                    snapshot.Points,
                    dx,
                    dy
                );

                break;

            case PlanningPolygon polygon:
                ApplyPoints(
                    polygon.Points,
                    snapshot.Points,
                    dx,
                    dy
                );

                ApplyPolygonHandles(
                    polygon.CurveHandles,
                    snapshot.PolygonHandles,
                    dx,
                    dy
                );

                break;

            case PlanningArrow arrow:
                ApplyPoints(
                    arrow.Points,
                    snapshot.Points,
                    dx,
                    dy
                );

                ApplyArrowHandles(
                    arrow.CurveHandles,
                    snapshot.ArrowHandles,
                    dx,
                    dy
                );

                break;

            case PlanningText text
                when snapshot.Position
                    .HasValue:
                {
                    WorldPoint source =
                        snapshot.Position.Value;

                    text.Position =
                        new WorldPoint(
                            source.X +
                                dx,
                            source.Y +
                                dy
                        );

                    break;
                }

            case PlanningSymbol symbol
                when snapshot.Position
                    .HasValue:
                {
                    WorldPoint source =
                        snapshot.Position.Value;

                    symbol.Position =
                        new WorldPoint(
                            source.X +
                                dx,
                            source.Y +
                                dy
                        );

                    break;
                }
        }
    }

    private static List<WorldPoint>
        CopyPoints(
            List<WorldPoint> source)
    {
        var copy =
            new List<WorldPoint>(
                source.Count
            );

        foreach (
            WorldPoint point
            in source)
        {
            copy.Add(
                new WorldPoint(
                    point.X,
                    point.Y
                )
            );
        }

        return copy;
    }

    private static void ApplyPoints(
        List<WorldPoint> target,
        List<WorldPoint>? source,
        double dx,
        double dy)
    {
        if (source == null)
            return;

        if (
            target.Count !=
            source.Count
        )
        {
            target.Clear();

            foreach (
                WorldPoint point
                in source)
            {
                target.Add(
                    new WorldPoint(
                        point.X + dx,
                        point.Y + dy
                    )
                );
            }

            return;
        }

        for (
            int i = 0;
            i < source.Count;
            i++)
        {
            WorldPoint point =
                source[i];

            target[i] =
                new WorldPoint(
                    point.X + dx,
                    point.Y + dy
                );
        }
    }

    private static List<PolygonBezierHandlePair>
        CopyPolygonHandles(
            List<PolygonBezierHandlePair> source)
    {
        var copy =
            new List<PolygonBezierHandlePair>(
                source.Count
            );

        foreach (
            PolygonBezierHandlePair pair
            in source)
        {
            copy.Add(
                new PolygonBezierHandlePair
                {
                    InHandle =
                        new WorldPoint(
                            pair.InHandle.X,
                            pair.InHandle.Y
                        ),

                    OutHandle =
                        new WorldPoint(
                            pair.OutHandle.X,
                            pair.OutHandle.Y
                        ),

                    IsCustom =
                        pair.IsCustom
                }
            );
        }

        return copy;
    }

    private static List<ArrowBezierHandlePair>
        CopyArrowHandles(
            List<ArrowBezierHandlePair> source)
    {
        var copy =
            new List<ArrowBezierHandlePair>(
                source.Count
            );

        foreach (
            ArrowBezierHandlePair pair
            in source)
        {
            copy.Add(
                new ArrowBezierHandlePair
                {
                    InHandle =
                        new WorldPoint(
                            pair.InHandle.X,
                            pair.InHandle.Y
                        ),

                    OutHandle =
                        new WorldPoint(
                            pair.OutHandle.X,
                            pair.OutHandle.Y
                        ),

                    IsCustom =
                        pair.IsCustom
                }
            );
        }

        return copy;
    }

    private static void ApplyPolygonHandles(
        List<PolygonBezierHandlePair> target,
        List<PolygonBezierHandlePair>? source,
        double dx,
        double dy)
    {
        if (source == null)
            return;

        while (target.Count < source.Count)
        {
            target.Add(
                new PolygonBezierHandlePair()
            );
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(
                target.Count - 1
            );
        }

        for (
            int i = 0;
            i < source.Count;
            i++)
        {
            PolygonBezierHandlePair pair =
                source[i];

            target[i].InHandle =
                new WorldPoint(
                    pair.InHandle.X + dx,
                    pair.InHandle.Y + dy
                );

            target[i].OutHandle =
                new WorldPoint(
                    pair.OutHandle.X + dx,
                    pair.OutHandle.Y + dy
                );

            target[i].IsCustom =
                pair.IsCustom;
        }
    }

    private static void ApplyArrowHandles(
        List<ArrowBezierHandlePair> target,
        List<ArrowBezierHandlePair>? source,
        double dx,
        double dy)
    {
        if (source == null)
            return;

        while (target.Count < source.Count)
        {
            target.Add(
                new ArrowBezierHandlePair()
            );
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(
                target.Count - 1
            );
        }

        for (
            int i = 0;
            i < source.Count;
            i++)
        {
            ArrowBezierHandlePair pair =
                source[i];

            target[i].InHandle =
                new WorldPoint(
                    pair.InHandle.X + dx,
                    pair.InHandle.Y + dy
                );

            target[i].OutHandle =
                new WorldPoint(
                    pair.OutHandle.X + dx,
                    pair.OutHandle.Y + dy
                );

            target[i].IsCustom =
                pair.IsCustom;
        }
    }

    private sealed class ObjectSnapshot
    {
        public List<WorldPoint>?
            Points
        {
            get;
        }

        public List<PolygonBezierHandlePair>?
            PolygonHandles
        {
            get;
        }

        public List<ArrowBezierHandlePair>?
            ArrowHandles
        {
            get;
        }

        public WorldPoint?
            Position
        {
            get;
        }

        public ObjectSnapshot(
            List<WorldPoint> points)
        {
            Points =
                points;
        }

        public ObjectSnapshot(
            List<WorldPoint> points,
            List<PolygonBezierHandlePair> handles)
        {
            Points =
                points;

            PolygonHandles =
                handles;
        }

        public ObjectSnapshot(
            List<WorldPoint> points,
            List<ArrowBezierHandlePair> handles)
        {
            Points =
                points;

            ArrowHandles =
                handles;
        }

        public ObjectSnapshot(
            WorldPoint position)
        {
            Position =
                position;
        }
    }
}
