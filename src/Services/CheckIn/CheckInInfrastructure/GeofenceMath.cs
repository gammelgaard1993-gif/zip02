namespace zip02.Services.CheckIn.Infrastructure;

public static class GeofenceMath
{
    private const double EarthRadiusMeters = 6371000d;

    public static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var lat1Rad = DegreesToRadians(latitude1);
        var lat2Rad = DegreesToRadians(latitude2);
        var deltaLat = DegreesToRadians(latitude2 - latitude1);
        var deltaLon = DegreesToRadians(longitude2 - longitude1);

        var a = Math.Pow(Math.Sin(deltaLat / 2), 2)
            + Math.Cos(lat1Rad) * Math.Cos(lat2Rad) * Math.Pow(Math.Sin(deltaLon / 2), 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }
}
