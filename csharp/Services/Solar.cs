namespace AsusDisplayControl;

/// <summary>
/// Sun position math (NOAA algorithm). Used to decide day vs. night for the daylight
/// schedule and to display sunrise/sunset times. All inputs/outputs in UTC; longitude
/// is East-positive, latitude North-positive.
/// </summary>
internal static class Solar
{
    private const double D2R = Math.PI / 180.0;
    private const double R2D = 180.0 / Math.PI;

    private static double JulianDay(DateTime utc) =>
        (utc.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays + 2440587.5;

    private static double Mod(double a, double n) => ((a % n) + n) % n;

    /// <summary>Sun declination (deg) and equation of time (minutes) for a Julian day.</summary>
    private static (double decl, double eqTime) Params(double jd)
    {
        double t = (jd - 2451545.0) / 36525.0;
        double l0 = Mod(280.46646 + t * (36000.76983 + 0.0003032 * t), 360);
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double mr = m * D2R;
        double c = Math.Sin(mr) * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(2 * mr) * (0.019993 - 0.000101 * t)
                 + Math.Sin(3 * mr) * 0.000289;
        double trueLong = l0 + c;
        double omega = 125.04 - 1934.136 * t;
        double appLong = trueLong - 0.00569 - 0.00478 * Math.Sin(omega * D2R);
        double eps0 = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - 0.001813 * t))) / 60) / 60;
        double eps = eps0 + 0.00256 * Math.Cos(omega * D2R);
        double decl = Math.Asin(Math.Sin(eps * D2R) * Math.Sin(appLong * D2R)) * R2D;

        double y = Math.Tan(eps / 2 * D2R); y *= y;
        double l0r = l0 * D2R;
        double eqTime = 4 * R2D * (y * Math.Sin(2 * l0r)
                                   - 2 * e * Math.Sin(mr)
                                   + 4 * e * y * Math.Sin(mr) * Math.Cos(2 * l0r)
                                   - 0.5 * y * y * Math.Sin(4 * l0r)
                                   - 1.25 * e * e * Math.Sin(2 * mr));
        return (decl, eqTime);
    }

    /// <summary>Sun altitude above the horizon (degrees) at a given instant.</summary>
    public static double Altitude(DateTime utc, double lat, double lon)
    {
        double jd = JulianDay(utc);
        var (decl, eqTime) = Params(jd);
        double trueSolar = Mod(utc.ToUniversalTime().TimeOfDay.TotalMinutes + eqTime + 4 * lon, 1440);
        double ha = trueSolar / 4.0 - 180.0; // hour angle, degrees
        double latR = lat * D2R, declR = decl * D2R, haR = ha * D2R;
        double cosZen = Math.Sin(latR) * Math.Sin(declR) + Math.Cos(latR) * Math.Cos(declR) * Math.Cos(haR);
        cosZen = Math.Clamp(cosZen, -1.0, 1.0);
        return 90.0 - Math.Acos(cosZen) * R2D;
    }

    /// <summary>True if the sun is up (above -0.833°, standard sunrise threshold) right now.</summary>
    public static bool IsDaytime(DateTime utc, double lat, double lon) => Altitude(utc, lat, lon) > -0.833;

    /// <summary>Sunrise/sunset (UTC) for the given date, or nulls at polar day/night.</summary>
    public static (DateTime? sunrise, DateTime? sunset) SunriseSunset(DateTime dateUtc, double lat, double lon)
    {
        var d0 = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        // Evaluate parameters near this day's solar noon for accuracy.
        double jd = JulianDay(d0) + 0.5 - lon / 360.0;
        var (decl, eqTime) = Params(jd);
        double latR = lat * D2R, declR = decl * D2R;
        double cosH = (Math.Cos(90.833 * D2R) - Math.Sin(latR) * Math.Sin(declR)) / (Math.Cos(latR) * Math.Cos(declR));
        if (cosH > 1 || cosH < -1) return (null, null); // sun never rises / never sets
        double ha = Math.Acos(cosH) * R2D;
        double riseMin = 720 - 4 * (lon + ha) - eqTime; // minutes from UTC midnight
        double setMin = 720 - 4 * (lon - ha) - eqTime;
        return (d0.AddMinutes(riseMin), d0.AddMinutes(setMin));
    }
}
