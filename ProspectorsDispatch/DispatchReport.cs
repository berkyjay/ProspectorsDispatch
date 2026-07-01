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
/// are ever given — traders deal in hearsay and landmarks, not a grid.
/// </summary>
public static class DispatchReport
{
    public const int DefaultRadius = 5000;

    // Cap on entries per dispatch — keeps the page readable and, by naming only the nearest few,
    // pushes players to seek out other traders for leads on everything else.
    public const int MaxEntries = 5;

    // Force a journal page break after this many entries. The native journal (GuiDialogJournal.Paginate)
    // estimates page height by measuring the raw VTML-tagged text with a plain font, which does not match
    // the formatted, wrapped render — so a full page overshoots the text area and the last lines end up
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
    // coarse — and combined with only a 45-degree heading and no logged origin, it never pins a spot.
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
            ? $"<i>A surveyor's reckoning of the {cat} in these parts — truer than tavern talk, but no promises.</i>"
            : $"<i>Tavern talk of {cat} hereabouts — a rough heading at best, so take it with a pinch of salt.</i>");
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

    /// <summary>Round a bearing to one of 4 cardinal directions (the coarse Rumor heading).</summary>
    private static int Cardinal4(double bearingDeg)
    {
        int c = (int)Math.Round(bearingDeg / 90.0) % 4;
        return c < 0 ? c + 4 : c;
    }

    // The italic aside: "(rarity, in vein)" when both are known. Rarity is curated and has no honest
    // automatic source (see ResourceCatalog), so a resource with no curated entry — e.g. one added by
    // another mod — gets an honest hearsay fallback rather than a fabricated rarity, keeping its real
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
            ? $"(traces detected — worth uncertain, in {veinWords})"
            : "(traces detected, extent uncertain)";
    }

    private static string LocalizedName(string code)
    {
        string key = "ore-" + code;
        string name = Lang.Get(key);
        // Lang.Get returns the key unchanged when there's no translation.
        return name == key ? Capitalize(code) : name;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
