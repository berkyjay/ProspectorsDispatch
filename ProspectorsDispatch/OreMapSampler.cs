using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace ProspectorsDispatch;

/// <summary>
/// Reads Vintage Story's seeded ore-abundance maps to find, for a resource, the direction and distance
/// to its strongest concentration near an origin.
/// </summary>
/// <remarks>
/// Works for unexplored land: it calls <see cref="MapLayerBase.GenLayer"/> on each deposit's
/// <c>OreMapLayer</c>, which regenerates the same seeded noise worldgen uses for
/// <c>mapRegion.OreMaps</c>. <c>DepositVariant.GetOreMapFactor</c> is not used because it reads the
/// cached per-region map and returns 0 for regions that have not been generated yet.
/// Server side only, since the deposit system lives on the server.
/// </remarks>
public sealed class OreMapSampler
{
    // Blocks per ore-map cell (16 in 1.22).
    private readonly int _oreMapScale = TerraGenConfig.oreMapScale;

    private GenDeposits? _genDeposits;
    private readonly Dictionary<string, VeinSize> _veinByCode = new();
    private readonly Dictionary<string, ResourceCategory> _categoryByCode = new();

    // Primer data: for each ore code, the rocks that host it, and the rocks where its top
    // "bountiful" grade can form.
    private readonly Dictionary<string, HashSet<string>> _hostRocksByCode = new();
    private readonly Dictionary<string, HashSet<string>> _bountifulRocksByCode = new();

    /// <summary>True once the deposit system is available.</summary>
    public bool Ready => _genDeposits?.Deposits != null;

    public void Init(ICoreServerAPI api)
    {
        _genDeposits = api.ModLoader.GetModSystem<GenDeposits>();
        RecordDeposits();
    }

    /// <summary>The vein size of a resource, or null if its deposit has no radius.</summary>
    public VeinSize? VeinSizeOf(string code) => _veinByCode.TryGetValue(code, out var size) ? size : null;

    /// <summary>
    /// The resources in a dispatch category. Built from the deposits actually loaded, so ores added by
    /// other mods are included (see <see cref="ResourceCatalog.CategoryOf"/>).
    /// </summary>
    public IEnumerable<string> CodesInCategory(ResourceCategory category)
    {
        foreach (var pair in _categoryByCode)
        {
            if (pair.Value == category) yield return pair.Key;
        }
    }

    /// <summary>
    /// The rocks that host an ore, sorted, and the subset where its bountiful grade can form.
    /// Both are empty when unknown.
    /// </summary>
    public (List<string> Rocks, HashSet<string> Bountiful) HostRocksOf(string code)
    {
        var rocks = _hostRocksByCode.TryGetValue(code, out var hosts) ? hosts.OrderBy(x => x).ToList() : new List<string>();
        var bountiful = _bountifulRocksByCode.TryGetValue(code, out var rich) ? rich : new HashSet<string>();
        return (rocks, bountiful);
    }

    /// <summary>Every ore code with host-rock data, which is everything the primer can describe.</summary>
    public IEnumerable<string> ScatterLoreCodes() => _hostRocksByCode.Keys;

    /// <summary>
    /// Finds the strongest ore-map concentration of <paramref name="oreCode"/> within
    /// <paramref name="radiusBlocks"/> of the origin and returns the direction and distance to it.
    /// </summary>
    public OreReading Sample(string oreCode, int originX, int originZ, int radiusBlocks)
    {
        var deposit = FindDeposit(oreCode);
        if (deposit?.OreMapLayer == null) return OreReading.None;

        int radiusCells = Math.Max(1, radiusBlocks / _oreMapScale);
        int originCellX = CompassMath.FloorDiv(originX, _oreMapScale);
        int originCellZ = CompassMath.FloorDiv(originZ, _oreMapScale);
        int minCellX = originCellX - radiusCells;
        int minCellZ = originCellZ - radiusCells;
        int size = radiusCells * 2 + 1;

        // One noise tile covering the search square, row-major by z. The low byte of
        // noise[cz * size + cx] is the abundance (0 to 255) at cell (minCellX + cx, minCellZ + cz).
        int[] noise = deposit.OreMapLayer.GenLayer(minCellX, minCellZ, size, size);

        double radiusSq = (double)radiusBlocks * radiusBlocks;

        // First pass: the peak abundance within the radius. Ore maps are high contrast, so the peak is
        // usually a plateau of many cells at the maximum value.
        int maxValue = 0;
        for (int cz = 0; cz < size; cz++)
        {
            for (int cx = 0; cx < size; cx++)
            {
                double dx = (minCellX + cx) * _oreMapScale + _oreMapScale / 2 - originX;
                double dz = (minCellZ + cz) * _oreMapScale + _oreMapScale / 2 - originZ;
                if (dx * dx + dz * dz > radiusSq) continue;

                int value = noise[cz * size + cx] & 0xFF;
                if (value > maxValue) maxValue = value;
            }
        }
        if (maxValue <= 0) return OreReading.None;

        // Second pass: the peak cell nearest the origin. This avoids a directional bias across the
        // plateau and points at the closest rich spot.
        int targetCellX = originCellX, targetCellZ = originCellZ;
        double bestDistSq = double.MaxValue;
        for (int cz = 0; cz < size; cz++)
        {
            for (int cx = 0; cx < size; cx++)
            {
                if ((noise[cz * size + cx] & 0xFF) != maxValue) continue;

                int cellX = minCellX + cx, cellZ = minCellZ + cz;
                double dx = cellX * _oreMapScale + _oreMapScale / 2 - originX;
                double dz = cellZ * _oreMapScale + _oreMapScale / 2 - originZ;
                double distSq = dx * dx + dz * dz;
                if (distSq > radiusSq || distSq >= bestDistSq) continue;

                bestDistSq = distSq;
                targetCellX = cellX;
                targetCellZ = cellZ;
            }
        }

        double offsetX = targetCellX * _oreMapScale + _oreMapScale / 2 - originX;
        double offsetZ = targetCellZ * _oreMapScale + _oreMapScale / 2 - originZ;
        double bearing = CompassMath.Bearing(offsetX, offsetZ);

        return new OreReading
        {
            Found = true,
            OctantIndex = CompassMath.Octant(bearing),
            BearingDeg = bearing,
            DistanceBlocks = Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ)
        };
    }

    // Records each resource's dispatch category and vein size from the ore-map deposits, plus the
    // primer's host-rock data from every deposit. The first deposit seen for a code wins.
    private void RecordDeposits()
    {
        _veinByCode.Clear();
        _categoryByCode.Clear();

        var deposits = _genDeposits?.Deposits;
        if (deposits == null) return;

        foreach (var deposit in deposits)
        {
            Record(deposit, deposit.fromFile);
            if (deposit.ChildDeposits == null) continue;

            // Child deposits are defined inside the parent's file.
            foreach (var child in deposit.ChildDeposits) Record(child, deposit.fromFile);
        }
    }

    private void Record(DepositVariant deposit, string? sourceFile)
    {
        RecordScatterLore(deposit);
        if (!deposit.WithOreMap || deposit.Code == null) return;

        if (!_categoryByCode.ContainsKey(deposit.Code))
        {
            ResourceCategory? category = ResourceCatalog.CategoryOf(deposit.Code, deposit.fromFile ?? sourceFile);
            if (category.HasValue) _categoryByCode[deposit.Code] = category.Value;
        }

        // Vein size comes from the deposit's radius.avg (iron is about 26, diamond about 2). A deposit
        // without a radius is still offered, just without a vein size.
        if (!_veinByCode.ContainsKey(deposit.Code) && deposit.Attributes != null)
        {
            float radiusAvg = deposit.Attributes["radius"]["avg"].AsFloat(0f);
            if (radiusAvg > 0f) _veinByCode[deposit.Code] = ClassifyVein(radiusAvg);
        }
    }

    // Collects which rocks host each ore, from every deposit variant that places blocks inside rock
    // (TriesPerChunk > 0). Under Interesting Ore Gen 2.x the scattered ore has no location to point
    // at, but its host rocks, including where the bountiful grade forms, are readable worldgen data.
    private void RecordScatterLore(DepositVariant deposit)
    {
        if (deposit.Code == null || deposit.Attributes == null || deposit.TriesPerChunk <= 0f) return;

        var inBlock = deposit.Attributes["inblock"];
        if (!(inBlock?["code"].AsString("") ?? "").StartsWith("rock")) return;

        var variants = inBlock!["allowedVariants"];
        if (variants == null || !variants.Exists) return;

        if (!_hostRocksByCode.TryGetValue(deposit.Code, out var rocks))
        {
            _hostRocksByCode[deposit.Code] = rocks = new HashSet<string>();
        }
        foreach (var variant in variants.AsArray() ?? Array.Empty<JsonObject>())
        {
            string? rock = variant.AsString(null!);
            if (!string.IsNullOrEmpty(rock)) rocks.Add(rock);
        }

        // Rocks whose allowed grades include "bountiful". Keys look like "rock-basalt".
        var gradesByInBlock = deposit.Attributes["placeblock"]?["allowedVariantsByInBlock"];
        if (gradesByInBlock?.Exists != true || gradesByInBlock.Token is not JObject gradeTable) return;

        foreach (var property in gradeTable.Properties())
        {
            if (property.Value is not JArray grades) continue;
            if (!grades.Any(grade => (string?)grade == "bountiful")) continue;

            string rock = property.Name.StartsWith("rock-") ? property.Name.Substring(5) : property.Name;
            if (!_bountifulRocksByCode.TryGetValue(deposit.Code, out var bountiful))
            {
                _bountifulRocksByCode[deposit.Code] = bountiful = new HashSet<string>();
            }
            bountiful.Add(rock);
        }
    }

    private DepositVariant? FindDeposit(string oreCode)
    {
        var deposits = _genDeposits?.Deposits;
        if (deposits == null) return null;

        foreach (var deposit in deposits)
        {
            if (deposit.WithOreMap && deposit.Code == oreCode) return deposit;
            if (deposit.ChildDeposits == null) continue;

            foreach (var child in deposit.ChildDeposits)
            {
                if (child.WithOreMap && child.Code == oreCode) return child;
            }
        }
        return null;
    }

    private static VeinSize ClassifyVein(float radiusAvg)
    {
        if (radiusAvg < 3f) return VeinSize.Scattered;
        if (radiusAvg < 6f) return VeinSize.Small;
        if (radiusAvg < 12f) return VeinSize.Sizable;
        return VeinSize.Large;
    }
}
