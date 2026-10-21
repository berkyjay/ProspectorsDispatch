using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Config;

namespace ProspectorsDispatch;

/// <summary>
/// Renders dispatches into journal text: a short hearsay header, then one short line per resource
/// (bold name and heading, with rarity and vein size in italics).
/// </summary>
/// <remarks>
/// A Rumor gives a 4-point heading and no distance. A Survey gives an 8-point heading and a rounded
/// distance. Neither gives coordinates: traders deal in hearsay, not a grid.
/// </remarks>
public static class DispatchReport
{
    /// <summary>
    /// Default cap on entries per dispatch. Listing only the nearest few keeps the page readable and
    /// gives players a reason to visit other traders.
    /// </summary>
    public const int MaxEntries = 5;

    // The journal (GuiDialogJournal.Paginate) estimates page height by measuring the raw VTML text in
    // a plain font, which underestimates the formatted, wrapped text. A full page then runs under the
    // page buttons, so the reports insert the journal's own page-break token well before that point.
    private const int EntriesPerPage = 3;
    private const int PrimerEntriesPerPage = 4;
    private const string PageBreak = "___NEWPAGE___";

    private static readonly string[] s_directions8 =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    private static readonly string[] s_directions4 = { "north", "east", "south", "west" };

    // Indexed by VeinSize.
    private static readonly string[] s_veinWords =
        { "scattered specks", "small veins", "sizable veins", "large masses" };

    // Mineral-group prefixes that IOG joins to a variety in one token ("corundumsapphire",
    // "nativeplatinum"). Most of these varieties have no lang entry, so splitting off the group gives
    // a readable two-word name.
    private static readonly string[] s_mineralGroupPrefixes =
        { "native", "corundum", "tourmaline", "garnet", "beryl", "topaz", "quartz" };

    /// <summary>Renders a vanilla ore-map dispatch for one category, reckoned from the origin.</summary>
    public static (string Title, string Body) Build(
        OreMapSampler sampler, ResourceCategory category, DispatchTier tier, int originX, int originZ,
        int radius, int maxEntries = MaxEntries)
    {
        string categoryWord = category.ToString().ToLowerInvariant();
        string title = $"Prospecting {TierTitle(tier)}: {category}";

        var hits = sampler.CodesInCategory(category)
            .Select(code => (Code: code, Reading: sampler.Sample(code, originX, originZ, radius)))
            .Where(hit => hit.Reading.Found)
            .OrderBy(hit => hit.Reading.DistanceBlocks)
            .Take(maxEntries)
            .ToList();

        var body = new StringBuilder();
        body.AppendLine(tier == DispatchTier.Survey
            ? $"<i>A surveyor's reckoning of the {categoryWord} in these parts, truer than tavern talk but no promises.</i>"
            : $"<i>Tavern talk of {categoryWord} hereabouts. A rough heading at best, so take it with a pinch of salt.</i>");
        body.AppendLine();

        if (hits.Count == 0)
        {
            body.AppendLine($"Nobody could name any {categoryWord} worth the walk from here.");
            return (title, body.ToString().TrimEnd());
        }

        int shown = 0;
        foreach (var (code, reading) in hits)
        {
            if (shown > 0 && shown % EntriesPerPage == 0) body.AppendLine(PageBreak);
            shown++;

            string name = LocalizedName(code);
            string aside = Aside(sampler, code);

            if (tier == DispatchTier.Survey)
            {
                string direction = s_directions8[reading.OctantIndex];
                int paces = RoundPaces(reading.DistanceBlocks);
                body.AppendLine($"<strong>{name}</strong>: roughly <strong>{paces:N0} paces</strong> to the <strong>{direction}</strong>. <i>{aside}</i>");
            }
            else
            {
                string direction = s_directions4[CompassMath.Cardinal(reading.BearingDeg)];
                body.AppendLine($"<strong>{name}</strong>: somewhere to the <strong>{direction}</strong>. <i>{aside}</i>");
            }

            body.AppendLine();
        }

        return (title, body.ToString().TrimEnd());
    }

    /// <summary>
    /// Renders an Interesting Ore Gen mining-grounds dispatch. Each entry is a hydrothermal district:
    /// its kind, heading (and distance, for a Survey) and the ores its type carries.
    /// </summary>
    /// <param name="known">The districts this trader knows of, nearest first.</param>
    public static (string Title, string Body) BuildDistricts(
        IReadOnlyList<DistrictReading> known, DispatchTier tier, int maxEntries = MaxEntries)
    {
        string title = $"Prospecting {TierTitle(tier)}: Mining Grounds";

        // District kinds carry different ores (a mafic district has no tin, a cold hydrothermal one no
        // gold), so the nearest district of each kind is listed first. That keeps a distant but unique
        // kind from being crowded out by several close districts of the same kind. Remaining slots go
        // to the next nearest of any kind, and the final list is sorted by distance.
        var hits = new List<DistrictReading>();
        var seenKinds = new HashSet<string>();
        foreach (var district in known)
        {
            if (hits.Count >= maxEntries) break;
            if (seenKinds.Add(district.Kind)) hits.Add(district);
        }
        foreach (var district in known)
        {
            if (hits.Count >= maxEntries) break;
            if (!hits.Contains(district)) hits.Add(district);
        }
        hits = hits.OrderBy(d => d.DistanceBlocks).ToList();

        var body = new StringBuilder();
        body.AppendLine(tier == DispatchTier.Survey
            ? "<i>A surveyor's reckoning of the great mining grounds in this land, truer than tavern talk but no promises.</i>"
            : "<i>Tavern talk of rich mining grounds. A rough heading at best, so take it with a pinch of salt.</i>");

        // Districts span 1000 to 3000 blocks, so players reach the edge well before the quoted distance.
        body.AppendLine("<i>Each heading marks the heart of the grounds. They sprawl far wider, so you'll strike their edges sooner.</i>");

        // Each trader knows only some districts (see IogDistrictSampler.FindDistrictsKnownBy).
        body.AppendLine("<i>And mind, no one trader hears of every grounds. Another may know of nearer ones.</i>");
        body.AppendLine();

        if (hits.Count == 0)
        {
            body.AppendLine("Nobody could name mining grounds worth the walk from here.");
            return (title, body.ToString().TrimEnd());
        }

        // A Survey names every ore a district carries; a Rumor shortens the list. Full lists are much
        // longer, so Surveys show one district per page.
        bool fullOreList = tier == DispatchTier.Survey;
        int perPage = fullOreList ? 1 : 2;

        int shown = 0;
        foreach (var district in hits)
        {
            if (shown > 0 && shown % perPage == 0) body.AppendLine(PageBreak);
            shown++;

            string name = Capitalize(district.Kind) + " grounds";
            string country = string.IsNullOrEmpty(district.HostRock)
                ? ""
                : $" <i>({RockDisplayName(district.HostRock)} country)</i>";
            string ores = OresPhrase(district.OreNames, fullOreList);

            if (tier == DispatchTier.Survey)
            {
                string direction = s_directions8[district.OctantIndex];
                int paces = RoundPaces(district.DistanceBlocks);
                body.AppendLine($"<strong>{name}</strong>{country}: roughly <strong>{paces:N0} paces</strong> to the <strong>{direction}</strong>.<br><i>Bearing {ores}.</i>");
            }
            else
            {
                string direction = s_directions4[CompassMath.Cardinal(district.BearingDeg)];
                body.AppendLine($"<strong>{name}</strong>{country}: somewhere to the <strong>{direction}</strong>.<br><i>Bearing {ores}.</i>");
            }

            body.AppendLine();
        }

        return (title, body.ToString().TrimEnd());
    }

    /// <summary>
    /// Renders the prospector's primer: which rocks host which scattered ores, and where the bountiful
    /// grade forms. The content is the same everywhere in a world, so it is bought once.
    /// </summary>
    public static (string Title, string Body) BuildPrimer(OreMapSampler sampler)
    {
        const string title = "Prospector's Primer";

        var body = new StringBuilder();
        body.AppendLine("<i>A weathered primer on reading the rocks: which stone carries which ore. The land tells you where to dig, if you know its tongue.</i>");
        body.AppendLine();

        var entries = sampler.ScatterLoreCodes()
            .Select(code =>
            {
                var (rocks, bountiful) = sampler.HostRocksOf(code);
                return (Name: LocalizedName(code), Rocks: rocks, Bountiful: bountiful);
            })
            .Where(entry => entry.Rocks.Count > 0)
            .OrderBy(entry => entry.Name)
            .ToList();

        if (entries.Count == 0)
        {
            body.AppendLine("Its pages are water-stained beyond reading.");
            return (title, body.ToString().TrimEnd());
        }

        int shown = 0;
        foreach (var (name, rocks, bountiful) in entries)
        {
            if (shown > 0 && shown % PrimerEntriesPerPage == 0) body.AppendLine(PageBreak);
            shown++;

            string rockList = string.Join(", ", rocks.Select(RockDisplayName));
            string bountifulNote = bountiful.Count > 0
                ? $" <i>(bountiful in {string.Join(", ", bountiful.OrderBy(x => x).Select(RockDisplayName))})</i>"
                : "";
            body.AppendLine($"<strong>{name}</strong>: {rockList}.{bountifulNote}");
            body.AppendLine();
        }

        return (title, body.ToString().TrimEnd());
    }

    private static string TierTitle(DispatchTier tier) => tier == DispatchTier.Survey ? "Survey" : "Rumor";

    // A Survey distance rounded to the nearest 100 paces. Enough to plan a trip, but combined with a
    // 45-degree heading it never pins down a spot.
    private static int RoundPaces(double blocks)
    {
        int paces = (int)Math.Round(blocks / 100.0) * 100;
        return Math.Max(100, paces);
    }

    private static string RockDisplayName(string rock)
    {
        foreach (string key in new[] { "rock-" + rock, "block-rock-" + rock })
        {
            string localized = Lang.Get(key);
            if (localized != key) return localized.ToLowerInvariant();
        }
        return rock;
    }

    // "Sulfur, Cinnabar, Malachite and more". Duplicates are removed by display name, since two codes
    // can share a label. The short form caps the list; the full form names every ore, so a rarer ore
    // such as chromite is never hidden behind "and more".
    private static string OresPhrase(IReadOnlyList<string>? names, bool full)
    {
        const int MaxNames = 6;
        if (names == null || names.Count == 0) return "ore of kinds unknown";

        var shown = names.Select(OreDisplayName).Distinct().ToList();
        if (full) return string.Join(", ", shown);

        string phrase = string.Join(", ", shown.Take(MaxNames));
        return shown.Count > MaxNames ? phrase + " and more" : phrase;
    }

    // District ore names arrive as code tokens, sometimes compound ("quartz_nativegold" is gold in a
    // quartz tracer). Tries the full code first (vanilla has entries such as ore-quartz_nativegold),
    // then each '_' part from the most specific, then splits off a known mineral group, and finally
    // falls back to the raw token. Lang entries differ in case between mods, so every result is
    // capitalized.
    private static string OreDisplayName(string name)
    {
        string fullKey = "ore-" + name;
        string localized = Lang.Get(fullKey);
        if (localized != fullKey) return Capitalize(localized);

        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 1; i--)
        {
            string key = "ore-" + parts[i];
            localized = Lang.Get(key);
            if (localized != key) return Capitalize(localized);
        }

        // "gemruby" -> "Ruby"; "corundumsapphire" -> "Corundum sapphire".
        string token = parts[parts.Length - 1];
        if (token.StartsWith("gem") && token.Length > 3) return Capitalize(token.Substring(3));

        foreach (string prefix in s_mineralGroupPrefixes)
        {
            if (token.StartsWith(prefix) && token.Length > prefix.Length)
            {
                return Capitalize(prefix) + " " + token.Substring(prefix.Length);
            }
        }

        return Capitalize(name.Replace('_', ' '));
    }

    // The italic note after each entry, such as "(rare, in small veins)". Rarity is curated (see
    // ResourceCatalog), so a resource without a catalog entry, such as one added by another mod, gets
    // a "traces detected" note instead of an invented rarity.
    private static string Aside(OreMapSampler sampler, string code)
    {
        VeinSize? vein = sampler.VeinSizeOf(code);
        string? veinWords = vein.HasValue ? s_veinWords[(int)vein.Value] : null;

        ResourceEntry? entry = ResourceCatalog.Get(code);
        if (entry != null)
        {
            string rarity = entry.Rarity.ToString().ToLowerInvariant();
            return veinWords != null ? $"({rarity}, in {veinWords})" : $"({rarity})";
        }

        return veinWords != null
            ? $"(traces detected, worth uncertain, in {veinWords})"
            : "(traces detected, extent uncertain)";
    }

    // Lang.Get returns the key itself when there is no translation. The result is capitalized either
    // way, because some mods' lang entries are lowercase.
    private static string LocalizedName(string code)
    {
        string key = "ore-" + code;
        string name = Lang.Get(key);
        return Capitalize(name == key ? code : name);
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
