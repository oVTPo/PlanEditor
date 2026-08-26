using System;

namespace PlanEditor.Core.Planning;

public enum PlanningDoorKind
{
    SingleLeaf,
    DoubleLeaf
}

public sealed class PlanningDoor :
    PlanningObject
{
    /*
     * Door is attached to one segment of a PlanningPolyline
     * or PlanningPolygon.
     */
    public Guid HostObjectId { get; set; }

    public int SegmentIndex { get; set; }

    /*
     * 0..1 position along the host segment.
     */
    public double PositionT { get; set; } =
        0.5;

    public PlanningDoorKind Kind { get; set; } =
        PlanningDoorKind.SingleLeaf;

    /*
     * Physical opening width in map/world meters.
     *
     * The door now scales with the map:
     * zoom in  -> larger on screen
     * zoom out -> smaller on screen
     */
    public double GapWidthMeters { get; set; } =
        1.5;

    public PlanningDoor()
    {
        Name = "Cửa 1 cánh";
    }
}
