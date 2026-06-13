namespace laundryghar.SharedDataModel.Common;

/// <summary>Great-circle distance helpers, computed client-side to avoid PostGIS
/// geography translation pitfalls (ST_Y/ST_X reject geography).</summary>
public static class GeoMath
{
    /// <summary>Haversine great-circle distance in kilometres.</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth mean radius km

        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
