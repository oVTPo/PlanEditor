using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public sealed class DoorTool :
    IMapTool
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;
    private readonly PlanningDoorKind _kind;

    private const double SnapTolerancePixels =
        14.0;

    public string Name =>
        _kind ==
            PlanningDoorKind.SingleLeaf
                ? "Cửa 1 cánh"
                : "Cửa 2 cánh";

    public DoorTool(
        MapCanvas canvas,
        PlanningDocument document,
        PlanningDoorKind kind)
    {
        _canvas = canvas;
        _document = document;
        _kind = kind;
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

        if (!TryFindNearestHostSegment(
                screen,
                out PlanningObject? host,
                out int segmentIndex,
                out double positionT,
                out double segmentLengthPixels))
        {
            return false;
        }

        double gapMeters =
            _kind ==
                PlanningDoorKind.SingleLeaf
                    ? 1.5
                    : 2.6;

        /*
         * Door must fit inside the host segment.
         *
         * Segment length is currently measured on screen, so convert
         * it back to world meters using the current map scale.
         */
        double segmentLengthMeters =
            segmentLengthPixels *
            _canvas.MetersPerPixel;

        if (segmentLengthMeters <=
            0.01)
        {
            return false;
        }

        double halfGapT =
            (
                gapMeters / 2.0 +
                0.15
            ) /
            segmentLengthMeters;

        if (halfGapT >= 0.48)
            return false;

        positionT =
            Math.Clamp(
                positionT,
                halfGapT,
                1.0 - halfGapT
            );

        var door =
            new PlanningDoor
            {
                HostObjectId =
                    host!.Id,

                SegmentIndex =
                    segmentIndex,

                PositionT =
                    positionT,

                Kind =
                    _kind,

                GapWidthMeters =
                    gapMeters,

                Name =
                    _kind ==
                        PlanningDoorKind.SingleLeaf
                            ? "Cửa 1 cánh"
                            : "Cửa 2 cánh"
            };

        _document.Add(
            door
        );

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
        DrawingContext context)
    {
    }

    private bool TryFindNearestHostSegment(
        Point screen,
        out PlanningObject? host,
        out int segmentIndex,
        out double positionT,
        out double segmentLengthPixels)
    {
        host =
            null;

        segmentIndex =
            -1;

        positionT =
            0.0;

        segmentLengthPixels =
            0.0;

        double bestDistance =
            SnapTolerancePixels;

        foreach (
            PlanningObject item
            in _document.Objects)
        {
            if (
                !item.IsVisible ||
                item.IsLocked
            )
            {
                continue;
            }

            if (item is PlanningPolyline line)
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
                            line.Points[i + 1].X,
                            line.Points[i + 1].Y
                        );

                    CheckSegment(
                        item,
                        i,
                        a,
                        b,
                        screen,
                        ref bestDistance,
                        ref host,
                        ref segmentIndex,
                        ref positionT,
                        ref segmentLengthPixels
                    );
                }

                continue;
            }

            if (item is PlanningPolygon polygon)
            {
                int count =
                    polygon.Points.Count;

                for (
                    int i = 0;
                    i < count;
                    i++)
                {
                    int next =
                        (i + 1) %
                        count;

                    Point a =
                        _canvas.WorldToScreen(
                            polygon.Points[i].X,
                            polygon.Points[i].Y
                        );

                    Point b =
                        _canvas.WorldToScreen(
                            polygon.Points[next].X,
                            polygon.Points[next].Y
                        );

                    CheckSegment(
                        item,
                        i,
                        a,
                        b,
                        screen,
                        ref bestDistance,
                        ref host,
                        ref segmentIndex,
                        ref positionT,
                        ref segmentLengthPixels
                    );
                }
            }
        }

        return
            host != null &&
            segmentLengthPixels > 1.0;
    }

    private static void CheckSegment(
        PlanningObject candidateHost,
        int candidateSegmentIndex,
        Point a,
        Point b,
        Point p,
        ref double bestDistance,
        ref PlanningObject? bestHost,
        ref int bestSegmentIndex,
        ref double bestT,
        ref double bestLength)
    {
        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double lengthSquared =
            dx * dx +
            dy * dy;

        if (lengthSquared <=
            1.0)
        {
            return;
        }

        double t =
            (
                (p.X - a.X) * dx +
                (p.Y - a.Y) * dy
            ) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0.0,
                1.0
            );

        double x =
            a.X +
            t * dx;

        double y =
            a.Y +
            t * dy;

        double ox =
            p.X - x;

        double oy =
            p.Y - y;

        double distance =
            Math.Sqrt(
                ox * ox +
                oy * oy
            );

        if (distance >
            bestDistance)
        {
            return;
        }

        bestDistance =
            distance;

        bestHost =
            candidateHost;

        bestSegmentIndex =
            candidateSegmentIndex;

        bestT =
            t;

        bestLength =
            Math.Sqrt(
                lengthSquared
            );
    }
}
