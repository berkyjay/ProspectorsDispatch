using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ProspectorsDispatch;

/// <summary>
/// Builds a dispatch report and files it into the player's native Journal as a re-readable entry.
/// One entry per (trader location + category): re-reading the same trader's category updates that
/// entry (so a Survey upgrades an earlier Rumor); a different trader makes a separate entry.
/// </summary>
public static class DispatchJournal
{
    private static FieldInfo? journalsField;

    /// <param name="sourceKey">
    /// Stable identity of where the dispatch came from, used in the journal LoreCode so re-buying the same
    /// category from the same source updates one entry. Pass the trader's EntityId (traders wander, so
    /// their position is not stable); separate sources keep separate entries.
    /// </param>
    public static void File(ICoreServerAPI sapi, IServerPlayer player, OreMapSampler sampler,
        ResourceCategory category, DispatchTier tier, int originX, int originZ, string sourceKey,
        long traderEntityId = 0)
    {
        var journalSys = sapi.ModLoader.GetModSystem<ModJournal>();
        if (journalSys == null) return;

        var mod = sapi.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        var config = mod?.Config ?? new ProspectorsDispatchConfig();

        // The Districts pseudo-category (Interesting Ore Gen compat) reads hydrothermal districts
        // instead of the ore maps - only the subset THIS trader knows of (see FindDistrictsKnownBy);
        // everything else about the entry (lore code, title coords) is shared.
        (string title, string body) = category == ResourceCategory.Districts && mod != null
            ? DispatchReport.BuildDistricts(
                mod.DistrictSampler.FindDistrictsKnownBy(traderEntityId, originX, originZ,
                    config.DistrictSearchRadius, config.DistrictKnowledgeChance),
                tier, config.MaxResourcesPerDispatch)
            : DispatchReport.Build(
                sampler, category, tier, originX, originZ, config.SearchRadius, config.MaxResourcesPerDispatch);

        // Mark where the dispatch was bought - coordinates relative to the world spawn, matching the
        // in-game position readout. This is the reckoning origin, not the resource location.
        // COORDS-FIRST title: the journal's entry list is narrow and truncates long titles, and the
        // location is the only thing distinguishing entries - so it must lead, never trail.
        int relX = originX - (int)sapi.World.DefaultSpawnPosition.X;
        int relZ = originZ - (int)sapi.World.DefaultSpawnPosition.Z;
        string catWord = category == ResourceCategory.Districts ? "Mining Grounds" : category.ToString();
        string tierWord = tier == DispatchTier.Survey ? "Survey" : "Rumor";
        title = $"({relX}, {relZ}) {catWord} {tierWord}";
        body = $"<i>Bought from a trader near {relX}, {relZ}.</i><br>" + body;

        string loreCode = $"prospectorsdispatch-{category}-{sourceKey}";
        int entryId = ResolveEntryId(journalSys, player.PlayerUID, loreCode);

        var entry = new JournalEntry
        {
            Editable = false,
            Title = title,
            LoreCode = loreCode,
            EntryId = entryId
        };
        entry.Chapters.Add(new JournalChapter { Text = body, EntryId = entryId, ChapterId = 0 });

        journalSys.AddOrUpdateJournalEntry(player, entry);
    }

    /// <summary>
    /// Files the prospector's primer (rock-to-ore knowledge). One global entry per player - the
    /// knowledge is world-static, so it is not tied to any trader and is only ever bought once.
    /// </summary>
    public static void FilePrimer(ICoreServerAPI sapi, IServerPlayer player, OreMapSampler sampler)
    {
        var journalSys = sapi.ModLoader.GetModSystem<ModJournal>();
        if (journalSys == null) return;

        (string title, string body) = DispatchReport.BuildPrimer(sampler);

        const string loreCode = "prospectorsdispatch-primer";
        int entryId = ResolveEntryId(journalSys, player.PlayerUID, loreCode);

        var entry = new JournalEntry
        {
            Editable = false,
            Title = title,
            LoreCode = loreCode,
            EntryId = entryId
        };
        entry.Chapters.Add(new JournalChapter { Text = body, EntryId = entryId, ChapterId = 0 });

        journalSys.AddOrUpdateJournalEntry(player, entry);
    }

    // The journal client indexes entries by EntryId, but AddOrUpdateJournalEntry never assigns it.
    // So we read the player's current journal (a private dict) to choose the right index: an existing
    // entry's id when updating, or the entry count when adding. Falls back to 0 if unavailable.
    private static int ResolveEntryId(ModJournal journalSys, string playerUid, string loreCode)
    {
        try
        {
            journalsField ??= typeof(ModJournal).GetField(
                "journalsByPlayerUid", BindingFlags.NonPublic | BindingFlags.Instance);

            if (journalsField?.GetValue(journalSys) is Dictionary<string, Journal> journals
                && journals.TryGetValue(playerUid, out var journal) && journal != null)
            {
                for (int i = 0; i < journal.Entries.Count; i++)
                {
                    if (journal.Entries[i].LoreCode == loreCode) return journal.Entries[i].EntryId;
                }
                return journal.Entries.Count;
            }
        }
        catch
        {
            // Reflection failed (e.g. field renamed in a future version) - fall back.
        }
        return 0;
    }
}
