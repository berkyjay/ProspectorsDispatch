namespace ProspectorsDispatch;

/// <summary>The detail and price level of a dispatch. Every trader sells both.</summary>
public enum DispatchTier
{
    /// <summary>Cheap: a rough 4-point heading, with rarity and vein size, but no distance.</summary>
    Rumor,

    /// <summary>Expensive: an 8-point heading and an approximate, rounded distance.</summary>
    Survey
}
