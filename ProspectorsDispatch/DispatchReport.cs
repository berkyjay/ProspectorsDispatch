using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Config;

namespace ProspectorsDispatch;

/// <summary>The detail/price tier of a dispatch.</summary>
public enum DispatchTier
{
    Rumor,  // cheap: a vague 4-point heading + rarity + vein size, no distance. Every trader sells these.
    Survey  // pricey: precise 8-point heading + an approximate (rounded) distance. Only well-informed traders sell these.
}

/// <summary>
/// Renders a purchased dispatch (a category + tier, reckoned from a trader's position) into the
/// title and body of a journal entry. The hedged, hearsay flavor lives in the header; each resource
/// line is kept short and scannable (bold name + heading, italic rarity/vein aside).
///
/// The tiers differ on two axes: a Rumor gives only a coarse 4-point heading and no distance; a
/// Survey gives a precise 8-point heading plus a (deliberately banded) distance. No exact coordinates
/// are ever given - traders deal in hearsay and landmarks, not a grid.
/// </summary>
public static class DispatchReport
{
    public const int DefaultRadius = 5000;

    // Cap on entries per dispatch - keeps the page readable and, by naming only the nearest few,
    // pushes players to seek out other traders for leads on everything else.
    public const int MaxEntries = 5;

    // Force a journal page break after this many entries. The native journal (GuiDialogJournal.Paginate)
    // estimates page height by measuring the raw VTML-tagged text with a plain font, which does not match
    // the formatted, wrapped render - so a full page overshoots the text area and the last lines end up
    // hidden behind the page buttons. Emitting the journal's own ___NEWPAGE___ token caps each page well
    // short of that, sidestepping the miscalculation.
    private const int EntriesPerPage = 3;
    private const string PageBreak = "___NEWPAGE___";

    private static readonly string[] Dir8 =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
    private static readonly string[] Dir4 = { "north", "east", "south", "west" };

    // Indexed by VeinSize: Scattered, Small, Sizable, Large.
    private static readonly string[] VeinWords =
        { "scattered specks", "small veins", "sizable veins", "large masses" };

    // The Survey distance: an approximate, rounded pace count. Explicit enough to plan a journey, but
    // coarse - and combined with only a 45-degree heading and no logged origin, it never pins a spot.
    private static int RoundPaces(double blocks)
    {
        int n = (int)Math.Round(blocks / 100.0) * 100;
        return Math.Max(100, n);
    }

    public static (string Title, string Body) Build(
        OreMapSampler sampler, ResourceCategory category, DispatchTier tier, int originX, int originZ,
        int radius, int maxEntries = MaxEntries)
    {
        string cat = category.ToString().ToLowerInvariant();
        string title = $"Prospecting {(tier == DispatchTier.Survey ? "Survey" : "Rumor")}: {category}";

        // Gather what's findable, then keep only the nearest few.
        var hits = new List<(string code, OreReading r)>();
        foreach (var code in sampler.CodesInCategory(category))
        {
            OreReading r = sampler.Sample(code, originX, originZ, radius);
            if (r.Found) hits.Add((code, r));
        }
        hits = hits.OrderBy(h => h.r.DistanceBlocks).Take(maxEntries).ToList();

        var sb = new StringBuilder();

        // The hearsay framing lives here, once, so the lines below can stay terse and scannable.
        sb.AppendLine(tier == DispatchTier.Survey
            ? $"<i>A surveyor's reckoning of the {cat} in these parts - truer than tavern talk, but no promises.</i>"
            : $"<i>Tavern talk of {cat} hereabouts - a rough heading at best, so take it with a pinch of salt.</i>");
        sb.AppendLine();

        if (hits.Count == 0)
        {
            sb.AppendLine($"Nobody could name any {cat} worth the walk from here.");
            return (title, sb.ToString().TrimEnd());
        }

        int shown = 0;
        foreach (var (code, r) in hits)
        {
            if (shown > 0 && shown % EntriesPerPage == 0) sb.AppendLine(PageBreak);
            shown++;

            string name = LocalizedName(code);
            string aside = Aside(sampler, code);

            if (tier == DispatchTier.Survey)
            {
                string dir = Dir8[r.OctantIndex];
                int paces = RoundPaces(r.DistanceBlocks);
                sb.AppendLine($"<strong>{name}</strong>: roughly <strong>{paces:N0} paces</strong> to the <strong>{dir}</strong>. <i>{aside}</i>");
            }
            else
            {
                string dir = Dir4[Cardinal4(r.BearingDeg)];
                sb.AppendLine($"<strong>{name}</strong>: somewhere to the <strong>{dir}</strong>. <i>{aside}</i>");
            }

            sb.AppendLine(); // blank line between entries for readability
        }

        return (title, sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Renders a mining-grounds dispatch (Interesting Ore Gen compat): each entry is a hydrothermal
    /// district - its kind, heading (and distance, on a Survey) and the ores its type carries. District
    /// entries run two lines each, so they page at two per journal page.
    /// </summary>
    public static (string Title, string Body) BuildDistricts(
        List<DistrictReading> known, DispatchTier tier, int maxEntries = MaxEntries)
    {
        string title = $"Prospecting {(tier == DispatchTier.Survey ? "Survey" : "Rumor")}: Mining Grounds";

        // Prefer TYPE DIVERSITY over raw distance: district kinds carry different ore portfolios (a
        // mafic district has no tin; a cold hydrothermal no gold), so "the nearest grounds of each
        // kind" is far more useful than "the N nearest" - which could be N copies of the same kind,
        // burying the one far-but-unique district the player actually needs. Remaining slots then
        // fill with the next-nearest of any kind; final list reads nearest-first.
        var hits = new List<DistrictReading>();
        var seenKinds = new HashSet<string>();
        foreach (var d in known) // known is nearest-first
        {
            if (hits.Count >= maxEntries) break;
            if (seenKinds.Add(d.Kind)) hits.Add(d);
        }
        foreach (var d in known)
        {
            if (hits.Count >= maxEntries) break;
            if (!hits.Contains(d)) hits.Add(d);
        }
        hits = hits.OrderBy(d => d.DistanceBlocks).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(tier == DispatchTier.Survey
            ? "<i>A surveyor's reckoning of the great mining grounds in this land - truer than tavern talk, but no promises.</i>"
            : "<i>Tavern talk of rich mining grounds - a rough heading at best, so take it with a pinch of salt.</i>");
        // Districts are 1000-3000 blocks in radius (their faults reach even wider), so the player hits
        // the outskirts well before the quoted centre distance - say so, or the walk feels overstated.
        sb.AppendLine("<i>Each heading marks the heart of the grounds - they sprawl far wider, so you'll strike their edges sooner.</i>");
        // Knowledge is split across traders (see FindDistrictsKnownBy), so a lead that feels far may be
        // nearer from someone else. Kept in the header so it is seen without paging to the end.
        sb.AppendLine("<i>And mind - no one trader hears of every grounds. Another may know of nearer ones.</i>");
        sb.AppendLine();

        if (hits.Count == 0)
        {
            sb.AppendLine("Nobody could name mining grounds worth the walk from here.");
            return (title, sb.ToString().TrimEnd());
        }

        // A Survey names every ore a grounds carries (the paid, complete reading); a Rumour keeps the
        // short "and more" list. Full lists are much taller, so Surveys page one district at a time.
        bool fullOres = tier == DispatchTier.Survey;
        int perPage = fullOres ? 1 : 2;

        int shown = 0;
        foreach (var d in hits)
        {
            if (shown > 0 && shown % perPage == 0) sb.AppendLine(PageBreak);
            shown++;

            string name = Capitalize(d.Kind) + " grounds";
            string country = string.IsNullOrEmpty(d.HostRock)
                ? "" : $" <i>({RockDisplayName(d.HostRock)} country)</i>";
            string ores = OresPhrase(d.OreNames, fullOres);

            if (tier == DispatchTier.Survey)
            {
                string dir = Dir8[d.OctantIndex];
                int paces = RoundPaces(d.DistanceBlocks);
                sb.AppendLine($"<strong>{name}</strong>{country}: roughly <strong>{paces:N0} paces</strong> to the <strong>{dir}</strong>.<br><i>Bearing {ores}.</i>");
            }
            else
            {
                string dir = Dir4[Cardinal4(d.BearingDeg)];
                sb.AppendLine($"<strong>{name}</strong>{country}: somewhere to the <strong>{dir}</strong>.<br><i>Bearing {ores}.</i>");
            }

            sb.AppendLine();
        }

        return (title, sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Renders the prospector's primer: which rock types host which scattered ores (and where the top
    /// "bountiful" grade forms). World-static knowledge read from the active worldgen deposits - the
    /// player learns to read the land instead of being handed coordinates. One journal entry, bought once.
    /// </summary>
    public static (string Title, string Body) BuildPrimer(OreMapSampler sampler)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<i>A weathered primer on reading the rocks - which stone carries which ore. The land tells you where to dig, if you know its tongue.</i>");
        sb.AppendLine();

        var entries = sampler.ScatterLoreCodes()
            .Select(code =>
            {
                (List<string> rocks, HashSet<string> bounty) = sampler.HostRocksOf(code);
                return (name: LocalizedName(code), rocks, bounty);
            })
            .Where(e => e.rocks.Count > 0)
            .OrderBy(e => e.name)
            .ToList();

        if (entries.Count == 0)
        {
            sb.AppendLine("Its pages are water-stained beyond reading.");
            return ("Prospector's Primer", sb.ToString().TrimEnd());
        }

        int shown = 0;
        foreach (var (name, rocks, bounty) in entries)
        {
            if (shown > 0 && shown % 4 == 0) sb.AppendLine(PageBreak);
            shown++;

            string rockList = string.Join(", ", rocks.Select(RockDisplayName));
            string bountyNote = bounty.Count > 0
                ? $" <i>(bountiful in {string.Join(", ", bounty.OrderBy(x => x).Select(RockDisplayName))})</i>"
                : "";
            sb.AppendLine($"<strong>{name}</strong>: {rockList}.{bountyNote}");
            sb.AppendLine();
        }

        return ("Prospector's Primer", sb.ToString().TrimEnd());
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

    // "sulfur, cinnabar, malachite and more" - deduped by DISPLAY name (two codes may localize to the
    // same label). Rumour caps the list so the page stays short; a Survey (full=true) names them all,
    // so a rarer ore like chromite is never hidden behind "and more".
    private static string OresPhrase(List<string> names, bool full = false)
    {
        const int MaxNames = 6;
        if (names == null || names.Count == 0) return "ore of kinds unknown";
        var shown = names.Select(OreDisplayName).Distinct().ToList();
        if (full) return string.Join(", ", shown);
        string phrase = string.Join(", ", shown.Take(MaxNames));
        return shown.Count > MaxNames ? phrase + " and more" : phrase;
    }

    // Mineral-group prefixes IOG glues onto a variety inside one token ("corundumsapphire",
    // "nativeplatinum", "tourmalinerubellite"). Most of these gem varieties have no lang entry anywhere,
    // so splitting on the group yields a readable, mineralogically-sensible two-word name.
    private static readonly string[] MineralGroupPrefixes =
        { "native", "corundum", "tourmaline", "garnet", "beryl", "topaz", "quartz" };

    // District ore names arrive as code tokens, possibly compound ("quartz_nativegold" = gold in a
    // quartz tracer). Resolve the localized name by trying the FULL code first (vanilla has entries
    // like ore-quartz_nativegold), then the '_' subtokens most-specific-last, then split a known
    // mineral group prefix, and only then fall back to the raw token prettified.
    private static string OreDisplayName(string name)
    {
        // Every return path is funnelled through Capitalize: lang entries vary in case across mods
        // (some ores come back lowercase), so we force a consistent leading capital for a tidy list.
        string full = "ore-" + name;
        string localized = Lang.Get(full);
        if (localized != full) return Capitalize(localized);

        var subs = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = subs.Length - 1; i >= 1; i--)
        {
            string key = "ore-" + subs[i];
            localized = Lang.Get(key);
            if (localized != key) return Capitalize(localized);
        }

        // IOG glues a mineral group onto a variety with no lang entry. "gemruby" -> the "gem" prefix is
        // redundant (drop it -> "Ruby"); "corundumsapphire" -> "Corundum sapphire".
        string token = subs[subs.Length - 1];
        if (token.StartsWith("gem") && token.Length > 3)
            return Capitalize(token.Substring(3));
        foreach (string p in MineralGroupPrefixes)
        {
            if (token.StartsWith(p) && token.Length > p.Length)
                return Capitalize(p) + " " + token.Substring(p.Length);
        }

        return Capitalize(name.Replace('_', ' '));
    }

    /// <summary>Round a bearing to one of 4 cardinal directions (the coarse Rumor heading).</summary>
    private static int Cardinal4(double bearingDeg)
    {
        int c = (int)Math.Round(bearingDeg / 90.0) % 4;
        return c < 0 ? c + 4 : c;
    }

    // The italic aside: "(rarity, in vein)" when both are known. Rarity is curated and has no honest
    // automatic source (see ResourceCatalog), so a resource with no curated entry - e.g. one added by
    // another mod - gets an honest hearsay fallback rather than a fabricated rarity, keeping its real
    // vein descriptor when we have it.
    private static string Aside(OreMapSampler sampler, string code)
    {
        VeinSize? vein = sampler.VeinSizeOf(code);
        string? veinWords = vein.HasValue ? VeinWords[(int)vein.Value] : null;

        ResourceEntry? entry = ResourceCatalog.Get(code);
        if (entry != null)
        {
            string rarity = entry.Rarity.ToString().ToLowerInvariant();
            return veinWords != null ? $"({rarity}, in {veinWords})" : $"({rarity})";
        }

        return veinWords != null
            ? $"(traces detected - worth uncertain, in {veinWords})"
            : "(traces detected, extent uncertain)";
    }

    private static string LocalizedName(string code)
    {
        string key = "ore-" + code;
        string name = Lang.Get(key);
        // Lang.Get returns the key unchanged when there's no translation. Capitalize either way, so
        // ore names read consistently even when a mod's lang entry is lowercase.
        return Capitalize(name == key ? code : name);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
