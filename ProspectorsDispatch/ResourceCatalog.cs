using System.Collections.Generic;
using System.Linq;

namespace ProspectorsDispatch;

/// <summary>Which dispatch good covers a resource.</summary>
public enum ResourceCategory
{
    Ores,     // metals + coal — the early-game essentials
    Gems,
    Minerals
}

/// <summary>Static rarity tag (the D3 descriptor) — also the basis for dispatch pricing.</summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Exotic
}

public sealed class ResourceEntry
{
    public readonly string Code;
    public readonly ResourceCategory Category;
    public readonly Rarity Rarity;

    public ResourceEntry(string code, ResourceCategory category, Rarity rarity)
    {
        Code = code;
        Category = category;
        Rarity = rarity;
    }
}

/// <summary>
/// Curated classification of the guidable resources into categories (which dispatch covers them) and
/// rarity (the static journal tag + the basis for dispatch prices).
///
/// DELIBERATELY hand-authored, NOT derived from worldgen <c>triesPerChunk</c> — that number is a
/// misleading abundance proxy (emerald is 64 tries/chunk vs iron's ~0.5, because gems are many tiny
/// single-block attempts while iron forms a few large masses). These values are gameplay/balance
/// knobs: tune them freely. They drive both the rarity tag shown in the journal and pricing.
/// </summary>
public static class ResourceCatalog
{
    private static readonly ResourceEntry[] Entries =
    {
        // ── Ores (metals + coal) ────────────────────────────────────────────────
        new("nativecopper",  ResourceCategory.Ores,     Rarity.Common),
        new("malachite",     ResourceCategory.Ores,     Rarity.Common),
        new("lignite",       ResourceCategory.Ores,     Rarity.Common),
        new("bituminouscoal",ResourceCategory.Ores,     Rarity.Common),
        new("anthracite",    ResourceCategory.Ores,     Rarity.Uncommon),
        new("limonite",      ResourceCategory.Ores,     Rarity.Common),
        new("hematite",      ResourceCategory.Ores,     Rarity.Uncommon),
        new("magnetite",     ResourceCategory.Ores,     Rarity.Uncommon),
        new("cassiterite",   ResourceCategory.Ores,     Rarity.Uncommon),
        new("sphalerite",    ResourceCategory.Ores,     Rarity.Uncommon),
        new("galena",        ResourceCategory.Ores,     Rarity.Uncommon),
        new("rhodochrosite", ResourceCategory.Ores,     Rarity.Uncommon),
        new("silver",        ResourceCategory.Ores,     Rarity.Rare),
        new("gold",          ResourceCategory.Ores,     Rarity.Rare),
        new("bismuthinite",  ResourceCategory.Ores,     Rarity.Rare),
        new("chromite",      ResourceCategory.Ores,     Rarity.Rare),
        new("ilmenite",      ResourceCategory.Ores,     Rarity.Rare),
        new("pentlandite",   ResourceCategory.Ores,     Rarity.Rare),

        // ── Gems ────────────────────────────────────────────────────────────────
        new("emerald",       ResourceCategory.Gems,     Rarity.Rare),
        new("peridot",       ResourceCategory.Gems,     Rarity.Rare),
        new("diamond",       ResourceCategory.Gems,     Rarity.Exotic),

        // ── Minerals ──────────────────────────────────────────────────────────────
        new("halite",        ResourceCategory.Minerals, Rarity.Common),
        new("lapis",         ResourceCategory.Minerals, Rarity.Uncommon),
        new("alum",          ResourceCategory.Minerals, Rarity.Uncommon),
        new("fluorite",      ResourceCategory.Minerals, Rarity.Uncommon),
        new("graphite",      ResourceCategory.Minerals, Rarity.Uncommon),
        new("sulfur",        ResourceCategory.Minerals, Rarity.Uncommon),
        new("phosphorite",   ResourceCategory.Minerals, Rarity.Uncommon),
        new("borax",         ResourceCategory.Minerals, Rarity.Rare),
        new("cinnabar",      ResourceCategory.Minerals, Rarity.Rare),
        new("kernite",       ResourceCategory.Minerals, Rarity.Rare),
    };

    private static readonly Dictionary<string, ResourceEntry> ByCode =
        Entries.ToDictionary(e => e.Code);

    public static ResourceEntry? Get(string code) =>
        ByCode.TryGetValue(code, out var entry) ? entry : null;

    public static IEnumerable<string> CodesIn(ResourceCategory category) =>
        Entries.Where(e => e.Category == category).Select(e => e.Code);

    public static IEnumerable<ResourceEntry> All => Entries;
}
