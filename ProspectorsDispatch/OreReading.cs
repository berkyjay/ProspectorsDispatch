namespace ProspectorsDispatch;

/// <summary>
/// The result of sampling one resource's ore map around an origin: the direction and distance to
/// the strongest concentration within the search radius.
/// </summary>
public readonly record struct OreReading
{
    /// <summary>A reading for a resource with no ore map, or with no concentration in range.</summary>
    public static readonly OreReading None = default;

    /// <summary>False if the resource has no ore map or nothing was found in range.</summary>
    public bool Found { get; init; }

    /// <summary>Compass octant toward the concentration: 0 = N, 1 = NE, ... 7 = NW.</summary>
    public int OctantIndex { get; init; }

    /// <summary>Bearing toward the concentration in degrees, clockwise from north.</summary>
    public double BearingDeg { get; init; }

    /// <summary>Straight-line distance in blocks from the origin.</summary>
    public double DistanceBlocks { get; init; }
}
