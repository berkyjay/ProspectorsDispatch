namespace ProspectorsDispatch;

/// <summary>The kinds of dispatch a trader sells. Each covers a group of resources.</summary>
public enum ResourceCategory
{
    /// <summary>Metal ores and coal.</summary>
    Ores,

    Gems,

    Minerals,

    /// <summary>
    /// Interesting Ore Gen hydrothermal districts. Sold instead of the three categories above when
    /// IOG 2.x is installed, because IOG disables the vanilla ore-map deposits those categories read.
    /// Never returned by <see cref="ResourceCatalog.CategoryOf"/>.
    /// </summary>
    Districts
}
