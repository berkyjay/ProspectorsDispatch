using System.Collections.Generic;

namespace ProspectorsDispatch;

/// <summary>One Interesting Ore Gen hydrothermal district near a trader: where it is and what it carries.</summary>
public readonly record struct DistrictReading
{
    /// <summary>Readable district kind, such as "cold hydrothermal", from the district config code.</summary>
    public string Kind { get; init; }

    /// <summary>Compass octant toward the district center: 0 = N, 1 = NE, ... 7 = NW.</summary>
    public int OctantIndex { get; init; }

    /// <summary>Bearing toward the district center in degrees, clockwise from north.</summary>
    public double BearingDeg { get; init; }

    /// <summary>Straight-line distance in blocks from the origin to the district center.</summary>
    public double DistanceBlocks { get; init; }

    /// <summary>Display names of the ores this district type carries, without duplicates.</summary>
    public IReadOnlyList<string> OreNames { get; init; }

    /// <summary>The district's signature host rock, such as "basalt". Empty if unknown.</summary>
    public string HostRock { get; init; }

    /// <summary>District center in world block coordinates. Also identifies the district.</summary>
    public int CenterX { get; init; }

    /// <inheritdoc cref="CenterX"/>
    public int CenterZ { get; init; }
}
