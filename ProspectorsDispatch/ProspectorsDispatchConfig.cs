namespace ProspectorsDispatch;

/// <summary>
/// Server settings, stored in <c>ModConfig/ProspectorsDispatch.json</c> in the game data folder. On a
/// multiplayer server the host's file applies. Edit the file and restart to apply changes.
/// </summary>
public sealed class ProspectorsDispatchConfig
{
    /// <summary>How far, in blocks, a trader knows of resources.</summary>
    public int SearchRadius { get; set; } = 5000;

    /// <summary>
    /// How far, in blocks, a trader knows of Interesting Ore Gen districts. Districts are large but sparse
    /// (one 40% roll per 6 to 8 km tile), so this is much larger than <see cref="SearchRadius"/>.
    /// </summary>
    public int DistrictSearchRadius { get; set; } = 12000;

    /// <summary>
    /// Chance (0 to 1) that a trader has heard of each district beyond the nearest one, which every
    /// trader knows. Without this, neighboring traders would all sell the same few districts. Set to 1
    /// for every trader to know every district in range.
    /// </summary>
    public double DistrictKnowledgeChance { get; set; } = 0.6;

    /// <summary>The most resources a single dispatch lists (the nearest ones).</summary>
    public int MaxResourcesPerDispatch { get; set; } = 5;

    /// <summary>Offer the cheap Rumor tier.</summary>
    public bool OfferRumorTier { get; set; } = true;

    /// <summary>Offer the more expensive Survey tier.</summary>
    public bool OfferSurveyTier { get; set; } = true;

    /// <summary>Prices in gears. Set any price to 0 to make that dispatch free.</summary>
    public DispatchPriceTable Prices { get; set; } = new();

    public int PriceFor(ResourceCategory category, DispatchTier tier) => (category, tier) switch
    {
        (ResourceCategory.Ores, DispatchTier.Rumor) => Prices.OresRumor,
        (ResourceCategory.Ores, DispatchTier.Survey) => Prices.OresSurvey,
        (ResourceCategory.Minerals, DispatchTier.Rumor) => Prices.MineralsRumor,
        (ResourceCategory.Minerals, DispatchTier.Survey) => Prices.MineralsSurvey,
        (ResourceCategory.Gems, DispatchTier.Rumor) => Prices.GemsRumor,
        (ResourceCategory.Gems, DispatchTier.Survey) => Prices.GemsSurvey,
        (ResourceCategory.Districts, DispatchTier.Rumor) => Prices.DistrictsRumor,
        (ResourceCategory.Districts, DispatchTier.Survey) => Prices.DistrictsSurvey,
        _ => 5
    };
}
