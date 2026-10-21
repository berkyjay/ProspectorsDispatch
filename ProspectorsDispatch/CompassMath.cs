using System;

namespace ProspectorsDispatch;

/// <summary>Grid and compass helpers shared by the samplers and the report renderer.</summary>
internal static class CompassMath
{
    /// <summary>Integer division rounded toward negative infinity, for negative world coordinates.</summary>
    public static int FloorDiv(int a, int b)
    {
        int quotient = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) quotient--;
        return quotient;
    }

    /// <summary>
    /// Bearing in degrees clockwise from north for a block offset. In Vintage Story north is -Z and
    /// east is +X.
    /// </summary>
    public static double Bearing(double dx, double dz)
    {
        double bearing = Math.Atan2(dx, -dz) * (180.0 / Math.PI);
        return bearing < 0 ? bearing + 360.0 : bearing;
    }

    /// <summary>The nearest of 8 compass points: 0 = N, 1 = NE, ... 7 = NW.</summary>
    public static int Octant(double bearingDeg) => (int)Math.Round(bearingDeg / 45.0) % 8;

    /// <summary>The nearest of 4 compass points: 0 = N, 1 = E, 2 = S, 3 = W.</summary>
    public static int Cardinal(double bearingDeg)
    {
        int cardinal = (int)Math.Round(bearingDeg / 90.0) % 4;
        return cardinal < 0 ? cardinal + 4 : cardinal;
    }
}
