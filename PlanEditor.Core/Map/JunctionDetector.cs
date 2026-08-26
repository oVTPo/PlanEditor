using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Map;

public static class JunctionDetector
{
    private const double MergeToleranceMeters =
        3.0;

    public static List<Junction> Detect(
        IEnumerable<MapFeature> roads)
    {
        var candidates =
            new List<
                (WorldPoint Point, MapFeature Road)
            >();

        foreach (MapFeature road in roads)
        {
            if (!road.IsPlanningRoad)
                continue;

            if (road.Points.Count == 0)
                continue;

            foreach (WorldPoint point
                     in road.Points)
            {
                candidates.Add(
                    (point, road)
                );
            }
        }

        var junctions =
            new List<Junction>();

        foreach (var candidate
                 in candidates)
        {
            Junction? junction =
                FindNearbyJunction(
                    junctions,
                    candidate.Point
                );

            if (junction == null)
            {
                junction =
                    new Junction
                    {
                        Position =
                            candidate.Point
                    };

                junctions.Add(
                    junction
                );
            }

            if (!junction
                .ConnectedRoads
                .Contains(candidate.Road))
            {
                junction
                    .ConnectedRoads
                    .Add(candidate.Road);
            }
        }

        junctions.RemoveAll(
            j => j.RoadCount < 2
        );

        return junctions;
    }

    private static Junction? FindNearbyJunction(
        IEnumerable<Junction> junctions,
        WorldPoint point)
    {
        double toleranceSquared =
            MergeToleranceMeters *
            MergeToleranceMeters;

        foreach (Junction junction
                 in junctions)
        {
            double dx =
                junction.Position.X -
                point.X;

            double dy =
                junction.Position.Y -
                point.Y;

            double distanceSquared =
                dx * dx + dy * dy;

            if (distanceSquared <=
                toleranceSquared)
            {
                return junction;
            }
        }

        return null;
    }
}