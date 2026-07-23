using zip02.Services.CheckIn.Infrastructure;

namespace Zip02.Tests.CheckIn.Unit;

public sealed class GeofenceMathTests
{
    [Fact]
    public void DistanceMeters_SamePoint_ReturnsZero()
    {
        var distance = GeofenceMath.DistanceMeters(55.6761, 12.5683, 55.6761, 12.5683);

        Assert.Equal(0d, distance, precision: 5);
    }

    [Fact]
    public void DistanceMeters_KnownDistance_IsApproximatelyCorrect()
    {
        // ~0.0045 degrees of latitude ≈ 500 m near Copenhagen
        var distance = GeofenceMath.DistanceMeters(55.6761, 12.5683, 55.6806, 12.5683);

        Assert.InRange(distance, 480, 520);
    }

    [Fact]
    public void DistanceMeters_IsSymmetric()
    {
        var ab = GeofenceMath.DistanceMeters(55.6761, 12.5683, 55.7000, 12.6000);
        var ba = GeofenceMath.DistanceMeters(55.7000, 12.6000, 55.6761, 12.5683);

        Assert.Equal(ab, ba, precision: 5);
    }

    [Fact]
    public void DistanceMeters_AntipodalPoints_IsApproximatelyHalfEarthCircumference()
    {
        // (0, 0) and (0, 180) = half circumference ≈ 20,015 km
        var distance = GeofenceMath.DistanceMeters(0, 0, 0, 180);

        Assert.InRange(distance, 20_000_000, 20_050_000);
    }

    [Fact]
    public void DistanceMeters_EquatorOneDegree_IsApproximately111km()
    {
        // On the equator, 1° longitude ≈ 111,195 m
        var distance = GeofenceMath.DistanceMeters(0, 0, 0, 1);

        Assert.InRange(distance, 111_000, 111_500);
    }

    [Theory]
    [InlineData(55.6761, 12.5683, 55.6806, 12.5683, 600, true)]   // ~500 m away, 600 m radius → inside
    [InlineData(55.6761, 12.5683, 55.6806, 12.5683, 400, false)]  // ~500 m away, 400 m radius → outside
    [InlineData(55.6761, 12.5683, 55.6761, 12.5683, 1, true)]     // same point, any radius → inside
    public void DistanceMeters_ComparedToRadius_CorrectlyClassifiesInsideOutside(
        double lat1, double lon1, double lat2, double lon2, double radiusMeters, bool expectedInside)
    {
        var distance = GeofenceMath.DistanceMeters(lat1, lon1, lat2, lon2);

        Assert.Equal(expectedInside, distance <= radiusMeters);
    }
}
