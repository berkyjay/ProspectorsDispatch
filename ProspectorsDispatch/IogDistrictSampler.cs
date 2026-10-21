using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ProspectorsDispatch;

/// <summary>
/// Compatibility with Interesting Ore Gen 2.x (mod ID "interestingoregen"): locates IOG's
/// hydrothermal districts so traders can sell directions to them.
/// </summary>
/// <remarks>
/// IOG 2.x turns off the vanilla ore-map deposits that <see cref="OreMapSampler"/> reads. It places ore
/// in two ways instead: a uniform scatter in suitable rock, which has no location worth pointing at,
/// and rare, very large hydrothermal districts, which do.
/// <para>
/// Districts are fully determined by the world seed. The world is divided into square tiles of
/// <c>MinDistanceBetweenDistricts</c> blocks, and each tile's district (whether it exists, its center,
/// its type) comes from an <c>LCGRandom</c> seeded with
/// <c>worldSeed ^ (tileX * 1000033), tileZ * 998244353</c>. No chunk data is involved. This class
/// repeats that roll using IOG's own district configs, so districts can be found in unexplored land.
/// </para>
/// <para>
/// The seeding constants, the 40% chance and the order of the random draws match
/// <c>HydrothermalDistrictSystem.TryGenerateDistrictInTile</c> in IOG 2.3.8 and must be kept in sync
/// with it. IOG 3.0 removed districts entirely (see <see cref="IogSupport.Unsupported"/>).
/// </para>
/// </remarks>
public sealed class IogDistrictSampler
{
    public const string IogModId = "interestingoregen";

    // The per-tile district chance, hardcoded in IOG's TryGenerateDistrictInTile.
    private const double DistrictChancePerTile = 0.4;

    // Noise tokens dropped when turning an ore block code into a mineral name.
    private static readonly HashSet<string> s_noiseTokens = new(StringComparer.OrdinalIgnoreCase)
        { "ore", "rock", "*", "poor", "medium", "rich", "bountiful" };

    private readonly List<DistrictConfig> _configs = new();
    private bool _active;
    private int _worldSeed;
    private int _tileSize;

    /// <summary>True when IOG 2.x is installed and its district configs were loaded.</summary>
    public bool Active => _active;

    /// <summary>
    /// Works out which <see cref="IogSupport"/> mode applies. Uses only the mod list and IOG's district
    /// config assets, which client and server both have because IOG is a universal mod, so both sides
    /// reach the same answer. The trader dialogue depends on that (see <see cref="TraderDialogueInjectPatch"/>).
    /// </summary>
    public static IogSupport DetectSupport(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(IogModId)) return IogSupport.Absent;
        return LoadConfigs(api).Count > 0 ? IogSupport.Districts : IogSupport.Unsupported;
    }

    public void Init(ICoreServerAPI api)
    {
        _active = false;
        _configs.Clear();
        if (!api.ModLoader.IsModEnabled(IogModId)) return;

        _configs.AddRange(LoadConfigs(api));
        if (_configs.Count == 0) return;

        _worldSeed = api.WorldManager.Seed;
        _tileSize = _configs[0].MinDistanceBetweenDistricts;
        if (_tileSize <= 0) _tileSize = 8000;
        _active = true;

        api.Logger.Notification(
            "[ProspectorsDispatch] Interesting Ore Gen detected; dispatches will point at hydrothermal districts ({0} district type(s), tile size {1}).",
            _configs.Count, _tileSize);
    }

    /// <summary>All districts whose center lies within <paramref name="radiusBlocks"/> of the origin, nearest first.</summary>
    public List<DistrictReading> FindDistricts(int originX, int originZ, int radiusBlocks)
    {
        var found = new List<DistrictReading>();
        if (!_active) return found;

        int minTileX = CompassMath.FloorDiv(originX - radiusBlocks, _tileSize);
        int maxTileX = CompassMath.FloorDiv(originX + radiusBlocks, _tileSize);
        int minTileZ = CompassMath.FloorDiv(originZ - radiusBlocks, _tileSize);
        int maxTileZ = CompassMath.FloorDiv(originZ + radiusBlocks, _tileSize);

        double radiusSq = (double)radiusBlocks * radiusBlocks;

        for (int tileX = minTileX; tileX <= maxTileX; tileX++)
        {
            for (int tileZ = minTileZ; tileZ <= maxTileZ; tileZ++)
            {
                if (RollTile(tileX, tileZ) is not (int centerX, int centerZ, DistrictConfig config)) continue;

                double dx = centerX - originX, dz = centerZ - originZ;
                double distSq = dx * dx + dz * dz;
                if (distSq > radiusSq) continue;

                double bearing = CompassMath.Bearing(dx, dz);
                found.Add(new DistrictReading
                {
                    Kind = Humanize(config.Code),
                    OctantIndex = CompassMath.Octant(bearing),
                    BearingDeg = bearing,
                    DistanceBlocks = Math.Sqrt(distSq),
                    OreNames = OreNamesOf(config),
                    HostRock = HostRockOf(config),
                    CenterX = centerX,
                    CenterZ = centerZ
                });
            }
        }

        return found.OrderBy(d => d.DistanceBlocks).ToList();
    }

    /// <summary>
    /// The districts a particular trader has heard of: always the nearest one, plus each other district
    /// that passes a roll seeded by the trader and the district. Different traders know different
    /// subsets, which makes visiting several worthwhile, while each trader's knowledge never changes.
    /// A <paramref name="knowledgeChance"/> of 1 or more returns every district.
    /// </summary>
    public List<DistrictReading> FindDistrictsKnownBy(long traderEntityId, int originX, int originZ,
        int radiusBlocks, double knowledgeChance)
    {
        var all = FindDistricts(originX, originZ, radiusBlocks);
        if (all.Count == 0 || knowledgeChance >= 1.0) return all;

        var known = new List<DistrictReading> { all[0] };
        for (int i = 1; i < all.Count; i++)
        {
            var district = all[i];
            var random = new LCGRandom();
            random.InitPositionSeed((int)traderEntityId ^ (district.CenterX * 7919), district.CenterZ);
            if (random.NextDouble() < knowledgeChance) known.Add(district);
        }
        return known;
    }

    // Loads IOG's district configs the way IOG does: every asset under config/hydrothermal/ in any
    // domain, in asset-manager order, skipping configs whose DependsOn mods are missing. The order
    // matters because the weighted type pick walks the list in order, and IOG takes the tile size
    // from the first config.
    private static List<DistrictConfig> LoadConfigs(ICoreAPI api)
    {
        var configs = new List<DistrictConfig>();
        foreach (IAsset asset in api.Assets.GetMany("config/hydrothermal/"))
        {
            DistrictConfig? config = null;
            try
            {
                config = asset.ToObject<DistrictConfig>();
            }
            catch
            {
                // IOG skips malformed configs too.
            }
            if (config == null) continue;
            if (config.DependsOn != null && config.DependsOn.Any(id => !api.ModLoader.IsModEnabled(id))) continue;
            configs.Add(config);
        }
        return configs;
    }

    // IOG's per-tile district roll with the same seeding and draw order. Returns the district center
    // and type, or null when the tile has no district.
    private (int CenterX, int CenterZ, DistrictConfig Config)? RollTile(int tileX, int tileZ)
    {
        var random = new LCGRandom();
        random.InitPositionSeed(_worldSeed ^ (tileX * 1000033), tileZ * 998244353);

        if (random.NextDouble() >= DistrictChancePerTile) return null;

        int centerX = (int)(tileX * (double)_tileSize + random.NextDouble() * _tileSize);
        int centerZ = (int)(tileZ * (double)_tileSize + random.NextDouble() * _tileSize);

        float totalWeight = 0f;
        foreach (var config in _configs) totalWeight += config.SpawnWeight;

        float pick = (float)(random.NextDouble() * totalWeight);
        float accumulated = 0f;
        DistrictConfig chosen = _configs[_configs.Count - 1];
        foreach (var config in _configs)
        {
            accumulated += config.SpawnWeight;
            if (pick <= accumulated)
            {
                chosen = config;
                break;
            }
        }

        return (centerX, centerZ, chosen);
    }

    // "rock-basalt" -> "basalt".
    private static string HostRockOf(DistrictConfig config)
    {
        string code = config.HostMaterialCode ?? "";
        return code.StartsWith("rock-") ? code.Substring(5) : code.Split('-').LastOrDefault() ?? "";
    }

    // "hydrothermal-cold" -> "cold hydrothermal"; "magmatic-felsic-shallow" -> "shallow felsic magmatic".
    private static string Humanize(string code)
    {
        if (string.IsNullOrEmpty(code)) return "mineral-rich";

        var tokens = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(tokens);
        return string.Join(" ", tokens);
    }

    // Ore entries are block codes such as "ore-*-malachite-travertine", "rock-halite" or
    // "interestingoregen:saltpeterore". The mineral name is what remains after removing the domain,
    // the structural tokens (ore, rock, *, grades) and the host-rock suffix.
    private static List<string> OreNamesOf(DistrictConfig config)
    {
        string hostRock = config.HostMaterialCode?.Split('-').LastOrDefault() ?? "";
        var names = new List<string>();
        var seen = new HashSet<string>();

        foreach (var ore in config.Ores ?? Enumerable.Empty<OreAssignment>())
        {
            if (string.IsNullOrEmpty(ore.OreCode)) continue;

            string code = ore.OreCode;
            int colon = code.IndexOf(':');
            if (colon >= 0) code = code.Substring(colon + 1);

            // Split on '-' only. '_' joins compound codes such as "quartz_nativegold", which must stay
            // whole to find their localized name and to keep gold- and silver-bearing quartz distinct.
            var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !s_noiseTokens.Contains(p) && !p.Equals(hostRock, StringComparison.OrdinalIgnoreCase))
                .ToList();
            string name = parts.Count > 0 ? string.Join(" ", parts) : code;

            // Item-style codes add an "ore" suffix: "saltpeterore" -> "saltpeter".
            if (name.Length > 4 && name.EndsWith("ore", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 3);
            }
            if (seen.Add(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>The part of IOG's HydrothermalDistrictConfig used here. Property names match IOG's JSON keys.</summary>
    private sealed class DistrictConfig
    {
        public string Code { get; set; } = "";
        public List<string>? DependsOn { get; set; }
        public float SpawnWeight { get; set; } = 1f;
        public string HostMaterialCode { get; set; } = "";
        public int MinDistanceBetweenDistricts { get; set; } = 8000;
        public List<OreAssignment>? Ores { get; set; }
    }

    private sealed class OreAssignment
    {
        public string OreCode { get; set; } = "";
    }
}
