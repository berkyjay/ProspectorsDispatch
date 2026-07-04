using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ProspectorsDispatch;

/// <summary>One hydrothermal district located near a trader: where it is and what it carries.</summary>
public struct DistrictReading
{
    /// <summary>Human-readable district kind, e.g. "cold hydrothermal" (from the district config code).</summary>
    public string Kind;

    /// <summary>Compass octant toward the district centre: 0=N, 1=NE, ... 7=NW.</summary>
    public int OctantIndex;

    /// <summary>Bearing toward the district centre in degrees, clockwise from north.</summary>
    public double BearingDeg;

    /// <summary>Straight-line distance in blocks from the origin to the district centre.</summary>
    public double DistanceBlocks;

    /// <summary>Display names of the ores this district type carries (deduped, prettified).</summary>
    public List<string> OreNames;

    /// <summary>District centre in world block coordinates (stable identity for knowledge rolls).</summary>
    public int CentreX, CentreZ;
}

/// <summary>
/// Compatibility with Interesting Ore Gen (modid "interestingoregen"). IOG turns off the vanilla
/// ore-map-driven generation that our <see cref="OreMapSampler"/> reads (every withOreMap deposit is set
/// to 0 tries per chunk) and instead places ore two ways: a uniform scatter (present everywhere in
/// suitable rock - nothing to point at) and rare, huge "hydrothermal district" fault systems, which are
/// exactly the "worth the walk" targets this mod exists to sell directions to.
///
/// IOG's districts are deterministic from the world seed: the world is tiled at
/// <c>MinDistanceBetweenDistricts</c> blocks, and each tile's roll (does a district exist, its centre,
/// its type, its seed) comes from an <c>LCGRandom</c> position-seeded with
/// <c>worldSeed ^ (tileX * 1000033), tileZ * 998244353</c> - no chunk data involved
/// (IOG HydrothermalDistrictSystem.TryGenerateDistrictInTile, v2.3.8). We replicate that roll here,
/// reading IOG's own district configs from its assets, so we can locate districts in unexplored terrain
/// the same way we locate vanilla ore-map concentrations.
///
/// COUPLING NOTE: the seeding constants (1000033, 998244353, the 0.4 existence chance) and the draw
/// order (exists, centreX, centreZ, type pick, district seed) mirror IOG v2.3.8 and must be kept in sync
/// if IOG changes its generation. Soft dependency: when IOG is not installed this class stays inactive
/// and costs nothing.
/// </summary>
public class IogDistrictSampler
{
    public const string IogModId = "interestingoregen";

    // The 40% per-tile district chance, hardcoded in IOG's TryGenerateDistrictInTile.
    private const double DistrictChancePerTile = 0.4;

    private bool active;
    private int worldSeed;
    private int tileSize;
    private readonly List<DistrictConfigLite> configs = new();

    /// <summary>True when IOG is installed and district configs were loaded - dispatches should then
    /// come from districts instead of the (dead under IOG) vanilla ore maps.</summary>
    public bool Active => active;

    public void Init(ICoreServerAPI api)
    {
        active = false;
        configs.Clear();
        if (!api.ModLoader.IsModEnabled(IogModId)) return;

        // Mirror IOG's LoadConfigs: every asset under config/hydrothermal/ (any domain), in asset-manager
        // order, skipping entries whose DependsOn mods are absent. Order matters twice: the weighted type
        // pick walks the list cumulatively, and IOG takes the FIRST config's MinDistanceBetweenDistricts
        // as the world tile size.
        foreach (IAsset asset in api.Assets.GetMany("config/hydrothermal/"))
        {
            DistrictConfigLite? cfg = null;
            try { cfg = asset.ToObject<DistrictConfigLite>(); }
            catch { /* malformed config - IOG skips these too */ }
            if (cfg == null) continue;
            if (cfg.DependsOn != null && cfg.DependsOn.Any(id => !api.ModLoader.IsModEnabled(id))) continue;
            configs.Add(cfg);
        }
        if (configs.Count == 0) return;

        worldSeed = api.WorldManager.Seed;
        tileSize = configs[0].MinDistanceBetweenDistricts;
        if (tileSize <= 0) tileSize = 8000;
        active = true;

        api.Logger.Notification(
            "[ProspectorsDispatch] Interesting Ore Gen detected - dispatches will point at hydrothermal districts ({0} district type(s), tile size {1}).",
            configs.Count, tileSize);
    }

    /// <summary>
    /// All districts whose centre lies within <paramref name="radiusBlocks"/> of the origin,
    /// nearest first.
    /// </summary>
    public List<DistrictReading> FindDistricts(int originX, int originZ, int radiusBlocks)
    {
        var found = new List<DistrictReading>();
        if (!active) return found;

        int minTileX = FloorDiv(originX - radiusBlocks, tileSize);
        int maxTileX = FloorDiv(originX + radiusBlocks, tileSize);
        int minTileZ = FloorDiv(originZ - radiusBlocks, tileSize);
        int maxTileZ = FloorDiv(originZ + radiusBlocks, tileSize);

        double radiusSq = (double)radiusBlocks * radiusBlocks;

        for (int tx = minTileX; tx <= maxTileX; tx++)
        {
            for (int tz = minTileZ; tz <= maxTileZ; tz++)
            {
                if (RollTile(tx, tz) is not (int cx, int cz, DistrictConfigLite cfg)) continue;

                double dx = cx - originX, dz = cz - originZ;
                double distSq = dx * dx + dz * dz;
                if (distSq > radiusSq) continue;

                // Vintage Story compass: north = -Z, east = +X; bearing clockwise from north.
                double bearing = Math.Atan2(dx, -dz) * (180.0 / Math.PI);
                if (bearing < 0) bearing += 360.0;

                found.Add(new DistrictReading
                {
                    Kind = Humanize(cfg.Code),
                    OctantIndex = ((int)Math.Round(bearing / 45.0)) % 8,
                    BearingDeg = bearing,
                    DistanceBlocks = Math.Sqrt(distSq),
                    OreNames = OreNamesOf(cfg),
                    CentreX = cx,
                    CentreZ = cz
                });
            }
        }

        return found.OrderBy(d => d.DistanceBlocks).ToList();
    }

    /// <summary>
    /// The districts a PARTICULAR trader has heard of: the nearest one always (no useless traders),
    /// the rest each passing a deterministic knowledge roll seeded by (trader, district). Different
    /// traders thus know different subsets - the reason to visit more than one - while any given
    /// trader's knowledge never changes. <paramref name="knowledgeChance"/> of 1+ disables filtering.
    /// </summary>
    public List<DistrictReading> FindDistrictsKnownBy(long traderEntityId, int originX, int originZ,
        int radiusBlocks, double knowledgeChance)
    {
        var all = FindDistricts(originX, originZ, radiusBlocks); // nearest first
        if (all.Count == 0 || knowledgeChance >= 1.0) return all;

        var known = new List<DistrictReading> { all[0] };
        for (int i = 1; i < all.Count; i++)
        {
            var d = all[i];
            var rnd = new LCGRandom();
            rnd.InitPositionSeed((int)traderEntityId ^ (d.CentreX * 7919), d.CentreZ);
            if (rnd.NextDouble() < knowledgeChance) known.Add(d);
        }
        return known;
    }

    /// <summary>
    /// IOG's per-tile district roll, byte-for-byte: same seeding, same draw order. Returns the district
    /// centre and its type config, or null when the tile rolled no district.
    /// </summary>
    private (int cx, int cz, DistrictConfigLite cfg)? RollTile(int tileX, int tileZ)
    {
        var rnd = new LCGRandom();
        rnd.InitPositionSeed(worldSeed ^ (tileX * 1000033), tileZ * 998244353);

        if (rnd.NextDouble() >= DistrictChancePerTile) return null;

        int cx = (int)(tileX * (double)tileSize + rnd.NextDouble() * tileSize);
        int cz = (int)(tileZ * (double)tileSize + rnd.NextDouble() * tileSize);

        float totalWeight = 0f;
        foreach (var c in configs) totalWeight += c.SpawnWeight;
        float pick = (float)(rnd.NextDouble() * totalWeight);
        float accum = 0f;
        DistrictConfigLite chosen = configs[configs.Count - 1];
        foreach (var c in configs)
        {
            accum += c.SpawnWeight;
            if (pick <= accum) { chosen = c; break; }
        }

        return (cx, cz, chosen);
    }

    /// <summary>"hydrothermal-cold" → "cold hydrothermal"; "magmatic-felsic-shallow" → "shallow felsic magmatic".</summary>
    private static string Humanize(string code)
    {
        if (string.IsNullOrEmpty(code)) return "mineral-rich";
        var tokens = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(tokens);
        return string.Join(" ", tokens);
    }

    // Ore entries are BLOCK codes ("ore-*-malachite-travertine", "rock-halite",
    // "interestingoregen:saltpeterore"). Extract a readable mineral name: strip domain, drop structural
    // tokens (ore/rock/*/grades) and the host-rock suffix, then keep the remaining token.
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
        { "ore", "rock", "*", "poor", "medium", "rich", "bountiful" };

    private static List<string> OreNamesOf(DistrictConfigLite cfg)
    {
        string hostRock = cfg.HostMaterialCode?.Split('-').LastOrDefault() ?? "";
        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var ore in cfg.Ores ?? Enumerable.Empty<OreAssignmentLite>())
        {
            if (string.IsNullOrEmpty(ore.OreCode)) continue;
            string code = ore.OreCode;
            int colon = code.IndexOf(':');
            if (colon >= 0) code = code.Substring(colon + 1);

            // Split on '-' ONLY: '_' joins compound ore codes ("quartz_nativegold") that must stay whole,
            // both to resolve their proper localized name and so gold- and silver-bearing quartz don't
            // collapse into two identical "Quartz" entries.
            var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !NoiseTokens.Contains(p) && !p.Equals(hostRock, StringComparison.OrdinalIgnoreCase))
                .ToList();
            string name = parts.Count > 0 ? string.Join(" ", parts) : code;
            // "saltpeterore" -> "saltpeter": item-style codes glue an "ore" suffix on.
            if (name.Length > 4 && name.EndsWith("ore", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);
            if (seen.Add(name)) names.Add(name);
        }
        return names;
    }

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q--;
        return q;
    }

    /// <summary>The subset of IOG's HydrothermalDistrictConfig we need (JSON keys match IOG's;
    /// fields are populated by the JSON deserializer).</summary>
    private class DistrictConfigLite
    {
        public string Code = "";
        public List<string>? DependsOn = null;
        public float SpawnWeight = 1f;
        public string HostMaterialCode = "";
        public int MinDistanceBetweenDistricts = 8000;
        public List<OreAssignmentLite>? Ores = null;
    }

    private class OreAssignmentLite
    {
        public string OreCode = "";
    }
}
