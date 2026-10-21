using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ProspectorsDispatch;

/// <summary>
/// Files dispatches into the player's journal as read-only entries. There is one entry per trader
/// and category, so buying a Survey from a trader replaces an earlier Rumor from the same trader.
/// </summary>
public static class DispatchJournal
{
    private const string PrimerLoreCode = "prospectorsdispatch-primer";

    private static FieldInfo? s_journalsField;

    /// <summary>Builds a dispatch reckoned from the trader's position and files it in the journal.</summary>
    /// <param name="sourceKey">
    /// Stable identity of the seller, used in the entry's lore code so that buying the same category
    /// from the same trader updates one entry. Pass the trader's entity ID, since traders wander.
    /// </param>
    /// <param name="traderEntityId">The trader, which decides which districts they know of.</param>
    public static void File(ICoreServerAPI sapi, IServerPlayer player, OreMapSampler sampler,
        ResourceCategory category, DispatchTier tier, int originX, int originZ, string sourceKey,
        long traderEntityId = 0)
    {
        var journal = sapi.ModLoader.GetModSystem<ModJournal>();
        if (journal == null) return;

        var mod = sapi.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        var config = mod?.Config ?? new ProspectorsDispatchConfig();

        string body = category == ResourceCategory.Districts && mod != null
            ? DispatchReport.BuildDistricts(
                mod.DistrictSampler.FindDistrictsKnownBy(traderEntityId, originX, originZ,
                    config.DistrictSearchRadius, config.DistrictKnowledgeChance),
                tier, config.MaxResourcesPerDispatch).Body
            : DispatchReport.Build(
                sampler, category, tier, originX, originZ, config.SearchRadius, config.MaxResourcesPerDispatch).Body;

        // Where the dispatch was bought, relative to world spawn to match the in-game position display.
        // This is the reckoning origin, not the resource's location. The coordinates lead the title
        // because the journal's entry list truncates long titles and they are what tell entries apart.
        int relX = originX - (int)sapi.World.DefaultSpawnPosition.X;
        int relZ = originZ - (int)sapi.World.DefaultSpawnPosition.Z;
        string categoryWord = category == ResourceCategory.Districts ? "Mining Grounds" : category.ToString();
        string tierWord = tier == DispatchTier.Survey ? "Survey" : "Rumor";

        string title = $"({relX}, {relZ}) {categoryWord} {tierWord}";
        body = $"<i>Bought from a trader near {relX}, {relZ}.</i><br>" + body;

        AddOrUpdateEntry(journal, player, $"prospectorsdispatch-{category}-{sourceKey}", title, body);
    }

    /// <summary>Files the prospector's primer. Each player has at most one, shared by all traders.</summary>
    public static void FilePrimer(ICoreServerAPI sapi, IServerPlayer player, OreMapSampler sampler)
    {
        var journal = sapi.ModLoader.GetModSystem<ModJournal>();
        if (journal == null) return;

        var (title, body) = DispatchReport.BuildPrimer(sampler);
        AddOrUpdateEntry(journal, player, PrimerLoreCode, title, body);
    }

    private static void AddOrUpdateEntry(ModJournal journal, IServerPlayer player, string loreCode,
        string title, string body)
    {
        int entryId = ResolveEntryId(journal, player.PlayerUID, loreCode);

        var entry = new JournalEntry
        {
            Editable = false,
            Title = title,
            LoreCode = loreCode,
            EntryId = entryId
        };
        entry.Chapters.Add(new JournalChapter { Text = body, EntryId = entryId, ChapterId = 0 });

        journal.AddOrUpdateJournalEntry(player, entry);
    }

    // The journal client indexes entries by EntryId, but AddOrUpdateJournalEntry never assigns one.
    // This reads the player's journal (a private field) to reuse an existing entry's ID, or to use the
    // entry count for a new entry. Falls back to 0 if the field cannot be read.
    private static int ResolveEntryId(ModJournal journal, string playerUid, string loreCode)
    {
        try
        {
            s_journalsField ??= typeof(ModJournal).GetField(
                "journalsByPlayerUid", BindingFlags.NonPublic | BindingFlags.Instance);

            if (s_journalsField?.GetValue(journal) is Dictionary<string, Journal> journals
                && journals.TryGetValue(playerUid, out var playerJournal) && playerJournal != null)
            {
                foreach (var entry in playerJournal.Entries)
                {
                    if (entry.LoreCode == loreCode) return entry.EntryId;
                }
                return playerJournal.Entries.Count;
            }
        }
        catch
        {
            // The private field was renamed or changed type in a newer game version.
        }
        return 0;
    }
}
