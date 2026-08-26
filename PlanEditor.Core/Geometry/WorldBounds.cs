namespace PlanEditor.Core.Geometry;

public readonly record struct WorldBounds(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY
)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public bool Intersects(WorldBounds other)
    {
        return !(
            MaxX < other.MinX ||
            MinX > other.MaxX ||
            MaxY < other.MinY ||
            MinY > other.MaxY
        );
    }

    public static WorldBounds FromPoints(
        IReadOnlyList<WorldPoint> points)
    {
        if (points.Count == 0)
            return default;

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (WorldPoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);

            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new WorldBounds(
            minX,
            minY,
            maxX,
            maxY
        );
    }
}