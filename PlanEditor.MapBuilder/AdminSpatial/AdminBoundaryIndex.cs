using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminBoundaryIndex
{
    private readonly STRtree<AdminBoundary>
        _index =
            new();

    private readonly GeometryFactory
        _geometryFactory =
            new(
                new PrecisionModel(),
                4326
            );

    public AdminBoundaryIndex(
        IEnumerable<AdminBoundary> boundaries)
    {
        foreach (
            AdminBoundary boundary
            in boundaries)
        {
            if (string.IsNullOrWhiteSpace(
                    boundary.CommuneCode))
            {
                continue;
            }

            _index.Insert(
                boundary.Envelope,
                boundary
            );
        }

        _index.Build();
    }

    public AdminBoundary? Find(
        double longitude,
        double latitude)
    {
        var envelope =
            new Envelope(
                longitude,
                longitude,
                latitude,
                latitude
            );

        IList<AdminBoundary> candidates =
            _index.Query(envelope);

        if (candidates.Count == 0)
            return null;

        Point point =
            _geometryFactory.CreatePoint(
                new Coordinate(
                    longitude,
                    latitude
                )
            );

        foreach (
            AdminBoundary candidate
            in candidates)
        {
            try
            {
                if (candidate.Geometry.Covers(point))
                    return candidate;
            }
            catch
            {
                // Geometry lỗi thì bỏ candidate đó.
            }
        }

        return null;
    }
}