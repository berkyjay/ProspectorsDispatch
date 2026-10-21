namespace ProspectorsDispatch;

/// <summary>Codes, variable names and labels shared by the trader dialogue patches.</summary>
public static class TraderDialogue
{
    public const string TriggerPrefix = "pddispatch-";

    /// <summary>
    /// Player variable recording the primer purchase. It has no trader ID because the primer is the
    /// same everywhere, so buying it once hides it at every trader.
    /// </summary>
    public const string PrimerVar = "pdbought-primer";

    public const string PrimerTrigger = TriggerPrefix + "primer";

    internal static string Lower(ResourceCategory category) => category.ToString().ToLowerInvariant();

    internal static string Lower(DispatchTier tier) => tier.ToString().ToLowerInvariant();

    internal static string TriggerFor(ResourceCategory category, DispatchTier tier)
        => TriggerPrefix + Lower(category) + "-" + Lower(tier);

    internal static string TierName(DispatchTier tier) => tier == DispatchTier.Rumor ? "Rumor" : "Full survey";

    /// <summary>How a category is named in dialogue and chat, such as "ores" or "mining grounds".</summary>
    internal static string CategoryWord(ResourceCategory category)
        => category == ResourceCategory.Districts ? "mining grounds" : Lower(category);

    /// <summary>
    /// Player variable set to "1" once the player buys this tier from this trader. Player variables are
    /// saved with the world and synced to the client, where they drive the menu conditions that hide
    /// dispatches the player already owns.
    /// </summary>
    internal static string BoughtVar(long traderEntityId, ResourceCategory category, DispatchTier tier)
        => $"pdbought-t{traderEntityId}-{Lower(category)}-{Lower(tier)}";

    internal static string PriceLabel(int price)
        => price <= 0 ? "free" : price == 1 ? "1 gear" : price + " gears";
}
