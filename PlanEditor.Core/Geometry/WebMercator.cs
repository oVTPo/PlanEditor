namespace PlanEditor.Core.Geometry;

public static class WebMercator
{
    private const double EarthRadius = 6378137.0;

    private const double MaxLatitude = 85.05112878;

    public static WorldPoint Project(
        double longitude,
        double latitude)
    {
        latitude = Math.Clamp(
            latitude,
            -MaxLatitude,
            MaxLatitude
        );

        double longitudeRad =
            longitude * Math.PI / 180.0;

        double latitudeRad =
            latitude * Math.PI / 180.0;

        double x =
            EarthRadius * longitudeRad;

        double y =
            EarthRadius *
            Math.Log(
                Math.Tan(
                    Math.PI / 4.0 +
                    latitudeRad / 2.0
                )
            );

        return new WorldPoint(x, y);
    }

    public static GeoCoordinate Unproject(
        double x,
        double y)
    {
        double longitude =
            x / EarthRadius *
            180.0 / Math.PI;

        double latitude =
            (
                2.0 *
                Math.Atan(
                    Math.Exp(y / EarthRadius)
                )
                - Math.PI / 2.0
            )
            * 180.0 / Math.PI;

        return new GeoCoordinate(
            latitude,
            longitude
        );
    }
}