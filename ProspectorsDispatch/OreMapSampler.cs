using System;
using System.Collections.Generic;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace ProspectorsDispatch;

/// <summary>
/// The result of sampling the ore-abundance map for one resource around an origin point:
/// the direction and distance to the highest concentration found within the search radius.
/// </summary>
public struct OreReading
{
    /// <summary>False if the resource has no ore map or no concentration was found in range.</summary>
    public bool Found;

    /// <summary>Compass octant toward the peak: 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW.</summary>
    public int OctantIndex;

    /// <summary>Bearing toward the peak in degrees, clockwise from north (0=N, 90=E).</summary>
    public double BearingDeg;

    /// <summary>Straight-line distance in blocks from the origin to the nearest peak cell.</summary>
    public double DistanceBlocks;

    public static readonly OreReading None = new OreReading { Found = false };
}

/// <summary>How large each deposit blob is, derived from the deposit's worldgen radius.avg.</summary>
public enum VeinSize
{
    Scattered, // tiny isolated pockets (e.g. diamond, radius ~2)
    Small,
    Sizable,
    Large      // big masses (e.g. iron, radius ~26)
}

/// <summary>
/// Samples Vintage Story's deterministic worldgen ore-abundance maps to locate, for a given
/// resource, the direction and distance to the highest concentration within a radius of an origin.
///
/// Works for completely unexplored regions: it regenerates the same seeded noise that worldgen
/// uses to populate <c>mapRegion.OreMaps</c>, by calling <see cref="MapLayerBase.GenLayer"/>
/// directly on each deposit's <c>OreMapLayer</c>. (We deliberately do NOT use
/// <c>DepositVariant.GetOreMapFactor</c>, which reads the cached per-region map and returns 0 for
/// regions that have not been generated yet.)
///
/// Server-side only — the deposit/worldgen systems live on the server.
/// </summary>
public class OreMapSampler
{
    // Blocks per ore-map cell (TerraGenConfig.oreMapScale == 16 in 1.22). The absolute ore-map
    // cell coordinate of a block is simply blockCoord / oreMapScale.
    private readonly int oreMapScale = TerraGenConfig.oreMapScale;

    private GenDeposits? genDeposits;
    private readonly Dictionary<string, VeinSize> veinByCode = new();
    private readonly Dictionary<string, ResourceCategory> categoryByCode = new();

    public void Init(ICoreServerAPI api)
    {
        genDeposits = api.ModLoader.GetModSystem<GenDeposits>();
        RecordDeposits();
    }

    /// <summary>True once the deposit system is available and initialized.</summary>
    public bool Ready => genDeposits?.Deposits != null;

    /// <summary>The (static, data-derived) vein size of a resource, or null if unknown.</summary>
    public VeinSize? VeinSizeOf(string code) => veinByCode.TryGetValue(code, out var v) ? v : null;

    /// <summary>
    /// Resources discovered at runtime that fall into the given dispatch category. Driven by the
    /// deposits actually present, so ores added by other mods are covered automatically (categorized
    /// from their worldgen folder; see <see cref="ResourceCatalog.CategoryOf"/>).
    /// </summary>
    public IEnumerable<string> CodesInCategory(ResourceCategory category)
    {
        foreach (var kv in categoryByCode)
            if (kv.Value == category) yield return kv.Key;
    }

    // One pass over every ore-map deposit, recording per resource code: (a) its dispatch category and
    // (b) its vein size, derived from the deposit's worldgen radius.avg (iron radius ~26 = large masses
    // vs diamond ~2 = scattered specks — honest real data, unlike rarity). Category is recorded even
    // when a deposit carries no radius, so such a resource is still offered. First deposit per code wins.
    private void RecordDeposits()
    {
        veinByCode.Clear();
        categoryByCode.Clear();
        var deposits = genDeposits?.Deposits;
        if (deposits == null) return;
        foreach (var dep in deposits)
        {
            Record(dep, dep.fromFile);
            if (dep.ChildDeposits == null) continue;
            // Child deposits are defined inline in the parent's file, so inherit its source path.
            foreach (var child in dep.ChildDeposits) Record(child, dep.fromFile);
        }
    }

    private void Record(DepositVariant dep, string? sourceFile)
    {
        if (!dep.WithOreMap || dep.Code == null) return;

        if (!categoryByCode.ContainsKey(dep.Code))
        {
            ResourceCategory? category = ResourceCatalog.CategoryOf(dep.Code, dep.fromFile ?? sourceFile);
            if (category.HasValue) categoryByCode[dep.Code] = category.Value;
        }

        if (!veinByCode.ContainsKey(dep.Code) && dep.Attributes != null)
        {
            float radiusAvg = dep.Attributes["radius"]["avg"].AsFloat(0f);
            if (radiusAvg > 0f) veinByCode[dep.Code] = ClassifyVein(radiusAvg);
        }
    }

    private static VeinSize ClassifyVein(float radiusAvg)
    {
        if (radiusAvg < 3f) return VeinSize.Scattered;
        if (radiusAvg < 6f) return VeinSize.Small;
        if (radiusAvg < 12f) return VeinSize.Sizable;
        return VeinSize.Large;
    }

    /// <summary>
    /// Every deposit code that has an ore map, i.e. the set of resources we can guide players to
    /// (and the natural set a trader could offer). Includes child deposits.
    /// </summary>
    public List<string> GuidableOreCodes()
    {
        var codes = new List<string>();
        var seen = new HashSet<string>();
        var deposits = genDeposits?.Deposits;
        if (deposits == null) return codes;

        // Dedupe by code: an ore can have several withOreMap deposit variants (e.g. a normal and a
        // rare "massive" bedded deposit), but they share one ore map per code, so one entry suffices.
        foreach (var dep in deposits)
        {
            if (dep.WithOreMap && dep.Code != null && seen.Add(dep.Code)) codes.Add(dep.Code);
            if (dep.ChildDeposits == null) continue;
            foreach (var child in dep.ChildDeposits)
            {
                if (child.WithOreMap && child.Code != null && seen.Add(child.Code)) codes.Add(child.Code);
            }
        }
        return codes;
    }

    private DepositVariant? FindDeposit(string oreCode)
    {
        var deposits = genDeposits?.Deposits;
        if (deposits == null) return null;

        foreach (var dep in deposits)
        {
            if (dep.WithOreMap && dep.Code == oreCode) return dep;
            if (dep.ChildDeposits == null) continue;
            foreach (var child in dep.ChildDeposits)
            {
                if (child.WithOreMap && child.Code == oreCode) return child;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the highest ore-map concentration of <paramref name="oreCode"/> within
    /// <paramref name="radiusBlocks"/> of (<paramref name="originX"/>, <paramref name="originZ"/>)
    /// and returns the direction, distance and peak abundance toward it.
    /// </summary>
    public OreReading Sample(string oreCode, int originX, int originZ, int radiusBlocks)
    {
        var dep = FindDeposit(oreCode);
        if (dep?.OreMapLayer == null) return OreReading.None;

        int radiusCells = Math.Max(1, radiusBlocks / oreMapScale);
        int originCellX = FloorDiv(originX, oreMapScale);
        int originCellZ = FloorDiv(originZ, oreMapScale);
        int minCellX = originCellX - radiusCells;
        int minCellZ = originCellZ - radiusCells;
        int size = radiusCells * 2 + 1;

        // One deterministic noise tile covering the whole search square. Layout is row-major by z:
        // noise[cz * size + cx] is the abundance (low byte, 0..255) at cell (minCellX+cx, minCellZ+cz).
        int[] noise = dep.OreMapLayer.GenLayer(minCellX, minCellZ, size, size);

        double radiusSq = (double)radiusBlocks * radiusBlocks;

        // Pass 1: peak abundance among in-radius cells. The ore map is high-contrast (cells sit near
        // 0 or near 255), so the peak is usually a plateau of many max-valued cells.
        int maxVal = 0;
        for (int cz = 0; cz < size; cz++)
        {
            for (int cx = 0; cx < size; cx++)
            {
                double ddx = (minCellX + cx) * oreMapScale + oreMapScale / 2 - originX;
                double ddz = (minCellZ + cz) * oreMapScale + oreMapScale / 2 - originZ;
                if (ddx * ddx + ddz * ddz > radiusSq) continue;
                int v = noise[cz * size + cx] & 0xFF;
                if (v > maxVal) maxVal = v;
            }
        }
        if (maxVal <= 0) return OreReading.None;

        // Pass 2: among the peak-valued cells, target the one NEAREST the origin. Picking nearest
        // (rather than first-in-iteration-order) both avoids a directional bias over the plateau and
        // points the player at the closest rich spot — the intuitive thing to travel toward.
        int targetCellX = originCellX, targetCellZ = originCellZ;
        double bestDistSq = double.MaxValue;
        for (int cz = 0; cz < size; cz++)
        {
            for (int cx = 0; cx < size; cx++)
            {
                if ((noise[cz * size + cx] & 0xFF) != maxVal) continue;
                int cellX = minCellX + cx, cellZ = minCellZ + cz;
                double ddx = cellX * oreMapScale + oreMapScale / 2 - originX;
                double ddz = cellZ * oreMapScale + oreMapScale / 2 - originZ;
                double d2 = ddx * ddx + ddz * ddz;
                if (d2 > radiusSq || d2 >= bestDistSq) continue;
                bestDistSq = d2;
                targetCellX = cellX;
                targetCellZ = cellZ;
            }
        }

        int peakX = targetCellX * oreMapScale + oreMapScale / 2;
        int peakZ = targetCellZ * oreMapScale + oreMapScale / 2;
        double dx = peakX - originX;
        double dz = peakZ - originZ;
        double dist = Math.Sqrt(dx * dx + dz * dz);

        // Vintage Story compass: north = -Z, east = +X. Bearing measured clockwise from north.
        double bearing = Math.Atan2(dx, -dz) * (180.0 / Math.PI);
        if (bearing < 0) bearing += 360.0;
        int octant = ((int)Math.Round(bearing / 45.0)) % 8;

        return new OreReading
        {
            Found = true,
            OctantIndex = octant,
            BearingDeg = bearing,
            DistanceBlocks = dist
        };
    }

    /// <summary>Integer floor division (handles negative world coordinates correctly).</summary>
    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q--;
        return q;
    }
}
