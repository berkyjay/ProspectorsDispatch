namespace ProspectorsDispatch;

/// <summary>
/// Player-editable settings, loaded from (and written to) <c>ModConfig/ProspectorsDispatch.json</c> in the
/// game data folder. Server-side: the trade injection, the sampler and the journal all run on the server,
/// so on a multiplayer server the host's config governs. Edit the JSON and restart to apply.
/// </summary>
public class ProspectorsDispatchConfig
{
    /// <summary>How far (in blocks) a trader "knows", and the radius a reading is reckoned over.</summary>
    public int SearchRadius = 5000;

    /// <summary>Cap on how many resources a single dispatch lists (the nearest N).</summary>
    public int MaxResourcesPerDispatch = 5;

    /// <summary>Offer the cheap Rumor tier (vague heading, no distance).</summary>
    public bool OfferRumorTier = true;

    /// <summary>Offer the pricier Survey tier (precise heading + distance).</summary>
    public bool OfferSurveyTier = true;

    /// <summary>Price in gears per category and tier. Set any to <c>0</c> to make that dispatch free.</summary>
    public DispatchPriceTable Prices = new DispatchPriceTable();

    public int PriceFor(ResourceCategory category, DispatchTier tier) => (category, tier) switch
    {
        (ResourceCategory.Ores, DispatchTier.Rumor) => Prices.OresRumor,
        (ResourceCategory.Ores, DispatchTier.Survey) => Prices.OresSurvey,
        (ResourceCategory.Minerals, DispatchTier.Rumor) => Prices.MineralsRumor,
        (ResourceCategory.Minerals, DispatchTier.Survey) => Prices.MineralsSurvey,
        (ResourceCategory.Gems, DispatchTier.Rumor) => Prices.GemsRumor,
        (ResourceCategory.Gems, DispatchTier.Survey) => Prices.GemsSurvey,
        _ => 5
    };
}

/// <summary>Gear prices per category/tier. A value of 0 means that dispatch is free.</summary>
public class DispatchPriceTable
{
    // Rumours are the cheap "triangulation" tier (direction only — cross bearings from several traders).
    // Surveys are the "skip the legwork" premium (direction + distance + the trader's logged coords).
    // Kept low on purpose: covering all the resources you need means buying many dispatches over a wide
    // area, so per-dispatch burden must stay small. All tunable in ModConfig/ProspectorsDispatch.json.
    public int MineralsRumor = 1;
    public int MineralsSurvey = 4;
    public int OresRumor = 1;
    public int OresSurvey = 6;
    public int GemsRumor = 2;
    public int GemsSurvey = 10;
}
