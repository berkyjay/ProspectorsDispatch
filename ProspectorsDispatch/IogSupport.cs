namespace ProspectorsDispatch;

/// <summary>How Interesting Ore Gen affects what traders can sell. Client and server always agree on it.</summary>
public enum IogSupport
{
    /// <summary>IOG is not installed. Traders sell the vanilla ore-map dispatches.</summary>
    Absent,

    /// <summary>IOG 2.x, which has hydrothermal districts. Traders sell directions to the districts.</summary>
    Districts,

    /// <summary>
    /// A version of IOG without hydrothermal districts (3.0 and later). IOG still disables the vanilla
    /// ore-map deposits, so neither kind of dispatch would point at real ore, and traders offer none.
    /// </summary>
    Unsupported
}
