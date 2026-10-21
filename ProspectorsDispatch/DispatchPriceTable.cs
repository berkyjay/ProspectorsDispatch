namespace ProspectorsDispatch;

/// <summary>
/// Gear prices per category and tier. A price of 0 makes that dispatch free.
/// </summary>
/// <remarks>
/// Prices are kept low on purpose. Covering every resource a player needs means buying many dispatches
/// across a wide area. A Rumor gives only a heading, so several from different traders can be crossed
/// to triangulate. A Survey adds the distance and saves that legwork.
/// </remarks>
public sealed class DispatchPriceTable
{
    public int MineralsRumor { get; set; } = 1;
    public int MineralsSurvey { get; set; } = 4;
    public int OresRumor { get; set; } = 1;
    public int OresSurvey { get; set; } = 6;
    public int GemsRumor { get; set; } = 2;
    public int GemsSurvey { get; set; } = 10;

    /// <summary>Interesting Ore Gen 2.x districts. A district is very large, so its Survey is priced near the top.</summary>
    public int DistrictsRumor { get; set; } = 2;

    /// <inheritdoc cref="DistrictsRumor"/>
    public int DistrictsSurvey { get; set; } = 8;

    /// <summary>The one-time primer (Interesting Ore Gen 2.x only), priced as a cheap starter purchase.</summary>
    public int Primer { get; set; } = 1;
}
