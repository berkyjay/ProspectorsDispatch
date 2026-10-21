using System.Collections.Generic;
using System.Linq;

namespace ProspectorsDispatch;

/// <summary>
/// Hand-maintained category and rarity for the known resources.
/// </summary>
/// <remarks>
/// Rarity is curated rather than derived from worldgen <c>triesPerChunk</c>, which is a poor measure
/// of abundance: emerald has 64 tries per chunk against iron's 0.5, because gems are many single-block
/// attempts while iron forms a few large masses. These values are balance settings: the category decides
/// which dispatch lists a resource, and the rarity is shown in its entry. Prices are set per category in
/// the config.
/// </remarks>
public static class ResourceCatalog
{
    private static readonly ResourceEntry[] s_entries =
    {
        // Ores (metals and coal)
        new("nativecopper", ResourceCategory.Ores, Rarity.Common),
        new("malachite", ResourceCategory.Ores, Rarity.Common),
        new("lignite", ResourceCategory.Ores, Rarity.Common),
        new("bituminouscoal", ResourceCategory.Ores, Rarity.Common),
        new("anthracite", ResourceCategory.Ores, Rarity.Uncommon),
        new("limonite", ResourceCategory.Ores, Rarity.Common),
        new("hematite", ResourceCategory.Ores, Rarity.Uncommon),
        new("magnetite", ResourceCategory.Ores, Rarity.Uncommon),
        new("cassiterite", ResourceCategory.Ores, Rarity.Uncommon),
        new("sphalerite", ResourceCategory.Ores, Rarity.Uncommon),
        new("galena", ResourceCategory.Ores, Rarity.Uncommon),
        new("rhodochrosite", ResourceCategory.Ores, Rarity.Uncommon),
        new("silver", ResourceCategory.Ores, Rarity.Rare),
        new("gold", ResourceCategory.Ores, Rarity.Rare),
        new("bismuthinite", ResourceCategory.Ores, Rarity.Rare),
        new("chromite", ResourceCategory.Ores, Rarity.Rare),
        new("ilmenite", ResourceCategory.Ores, Rarity.Rare),
        new("pentlandite", ResourceCategory.Ores, Rarity.Rare),

        // Gems
        new("emerald", ResourceCategory.Gems, Rarity.Rare),
        new("peridot", ResourceCategory.Gems, Rarity.Rare),
        new("diamond", ResourceCategory.Gems, Rarity.Exotic),

        // Minerals
        new("halite", ResourceCategory.Minerals, Rarity.Common),
        new("lapis", ResourceCategory.Minerals, Rarity.Uncommon),
        new("alum", ResourceCategory.Minerals, Rarity.Uncommon),
        new("fluorite", ResourceCategory.Minerals, Rarity.Uncommon),
        new("graphite", ResourceCategory.Minerals, Rarity.Uncommon),
        new("sulfur", ResourceCategory.Minerals, Rarity.Uncommon),
        new("phosphorite", ResourceCategory.Minerals, Rarity.Uncommon),
        new("borax", ResourceCategory.Minerals, Rarity.Rare),
        new("cinnabar", ResourceCategory.Minerals, Rarity.Rare),
        new("kernite", ResourceCategory.Minerals, Rarity.Rare),
    };

    private static readonly Dictionary<string, ResourceEntry> s_byCode = s_entries.ToDictionary(e => e.Code);

    /// <summary>The curated entry for a resource code, or null if the resource is not in the catalog.</summary>
    public static ResourceEntry? Get(string code) => s_byCode.TryGetValue(code, out var entry) ? entry : null;

    /// <summary>
    /// The dispatch category for a resource. A curated entry wins, so gold and silver stay in Ores and
    /// peridot stays in Gems regardless of their worldgen folder. Otherwise the category comes from the
    /// deposit's source folder, which picks up ores added by other mods. Returns null if the resource
    /// cannot be classified, in which case it is not offered.
    /// </summary>
    public static ResourceCategory? CategoryOf(string code, string? fromFile) =>
        s_byCode.TryGetValue(code, out var entry) ? entry.Category : CategoryFromFile(fromFile);

    // Vanilla and most mods put deposits under worldgen/deposits/{metalore,mineralore,gem}/, with coal at
    // the top of the deposits folder. The specific ore folders are checked before the coal fallback.
    private static ResourceCategory? CategoryFromFile(string? fromFile)
    {
        if (string.IsNullOrEmpty(fromFile)) return null;

        string file = fromFile.ToLowerInvariant();
        if (file.Contains("mineralore")) return ResourceCategory.Minerals;
        if (file.Contains("metalore")) return ResourceCategory.Ores;
        if (file.Contains("gem")) return ResourceCategory.Gems;
        if (file.Contains("coal")) return ResourceCategory.Ores;
        return null;
    }
}
