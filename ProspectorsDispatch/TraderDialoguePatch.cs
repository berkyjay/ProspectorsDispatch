using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ProspectorsDispatch;

/// <summary>
/// Dialogue-based delivery of dispatches. Two Harmony patches:
/// <list type="bullet">
/// <item>Inject a nested "ask about deposits" branch into every trader's dialogue (in code, because the
/// config/dialogue assets are not reachable by the JSON patch system): category -> tier (with gear price
/// shown) -> a confirmation step -> purchase.</item>
/// <item>Handle the resulting "pddispatch-&lt;category&gt;-&lt;tier&gt;" trigger: charge the configured
/// gears and file the journal entry reckoned from the trader's position.</item>
/// </list>
/// No item and no trade-window slots - the knowledge goes straight to the journal through conversation.
/// </summary>
public static class TraderDialogue
{
    public const string TriggerPrefix = "pddispatch-";

    internal static string Lower(ResourceCategory cat) => cat.ToString().ToLowerInvariant();
    internal static string Lower(DispatchTier tier) => tier.ToString().ToLowerInvariant();

    internal static string TriggerFor(ResourceCategory cat, DispatchTier tier)
        => TriggerPrefix + Lower(cat) + "-" + Lower(tier);

    internal static string TierName(DispatchTier tier) => tier == DispatchTier.Rumor ? "Rumour" : "Full survey";

    /// <summary>How a category is spoken of in dialogue and chat ("ores", "mining grounds", ...).</summary>
    internal static string CategoryWord(ResourceCategory cat)
        => cat == ResourceCategory.Districts ? "mining grounds" : Lower(cat);

    /// <summary>
    /// Name of the player-scoped dialogue variable that records "this player already bought this tier
    /// from this trader" ("1" once bought). Keyed by trader EntityId, so each trader keeps their own
    /// ledger; player-scoped variables persist in the savegame and sync to the client, driving the
    /// menu-option conditions that hide already-bought dispatches.
    /// </summary>
    internal static string BoughtVar(long traderEntityId, ResourceCategory cat, DispatchTier tier)
        => $"pdbought-t{traderEntityId}-{Lower(cat)}-{Lower(tier)}";

    /// <summary>
    /// The primer's bought-flag is GLOBAL per player (no trader id): the rock-lore is world-static and
    /// identical from every trader, so one purchase hides the option at all of them.
    /// </summary>
    public const string PrimerVar = "pdbought-primer";
    public const string PrimerTrigger = TriggerPrefix + "primer";

    internal static string PriceLabel(int price)
        => price <= 0 ? "free" : price == 1 ? "1 gear" : price + " gears";
}

/// <summary>Injects the dispatch dialogue branch into trader conversations as each dialogue is loaded.</summary>
[HarmonyPatch(typeof(EntityBehaviorConversable), "loadDialogue")]
public static class TraderDialogueInjectPatch
{
    public static void Postfix(EntityBehaviorConversable __instance, ref DialogueConfig __result)
    {
        if (__result?.components == null) return;
        if (__instance.entity is not EntityTradingHumanoid) return;

        if (__instance.entity.Api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>()?.Config
            is not ProspectorsDispatchConfig config) return;

        // With Interesting Ore Gen installed the vanilla ore maps place nothing, so traders sell
        // directions to IOG's hydrothermal districts instead. IOG is a universal mod (required on both
        // sides in multiplayer), so this flag - and therefore the injected dialogue structure and its
        // option Ids - is identical on client and server. That symmetry is load-bearing; see Inject.
        bool iogActive = __instance.entity.Api.ModLoader.IsModEnabled(IogDistrictSampler.IogModId);

        Inject(__result, config, iogActive, __instance.entity.EntityId);
    }

    /// <summary>
    /// Adds the "ask about deposits" option to the dialogue's main menu plus the nested branch
    /// (category -> tier+price -> confirm -> buy). Idempotent. Returns true if it modified the config.
    /// </summary>
    public static bool Inject(DialogueConfig result, ProspectorsDispatchConfig config, bool iogActive = false,
        long traderEntityId = 0)
    {
        if (result?.components == null) return false;
        if (result.components.Any(c => c.Code == "pd-deposits")) return false; // already injected

        var tiers = new List<DispatchTier>();
        if (config.OfferRumorTier) tiers.Add(DispatchTier.Rumor);
        if (config.OfferSurveyTier) tiers.Add(DispatchTier.Survey);
        if (tiers.Count == 0) return false;

        // Under Interesting Ore Gen only the district pseudo-category is sold (the ore-map categories
        // would point at nothing). Must resolve identically on both sides - see the Postfix note.
        var cats = iogActive
            ? new[] { ResourceCategory.Districts }
            : new[] { ResourceCategory.Ores, ResourceCategory.Gems, ResourceCategory.Minerals };

        if (result.components.FirstOrDefault(c => c.Code == "main") is not DlgTalkComponent main) return false;
        main.Text = main.Text.Append(Opt("Heard of any ore deposits worth the walk?", "pd-deposits")).ToArray();

        var added = new List<DialogueComponent>
        {
            Trader("pd-deposits", "Deposits? Aye, word gets around. What are you after?", "pd-deposits-menu"),
            CategoryMenu(cats, iogActive),
            Trader("pd-bought", "Anything else I can help you with?", "pd-deposits-menu"),
        };

        // The prospector's primer (IOG worlds): a one-time, dirt-cheap purchase of the world-static
        // rock-to-ore knowledge. Its menu option (added in CategoryMenu) hides globally once bought.
        if (iogActive)
        {
            added.Add(Trader("pd-primer",
                $"Ha - the old prospector's primer. Which stone carries which ore, all writ down. It runs {TraderDialogue.PriceLabel(config.Prices.Primer)}. Care for a copy?",
                "pd-primer-menu"));
            added.Add(PlayerMenu("pd-primer-menu",
                Opt("Aye, here's the coin.", "pd-buy-primer"),
                Opt("Not today.", "pd-deposits-menu")));
            added.Add(new DlgGenericComponent
            {
                Code = "pd-buy-primer",
                Owner = "trader",
                Trigger = TraderDialogue.PrimerTrigger,
                JumpTo = "pd-bought"
            });
        }

        foreach (var cat in cats)
        {
            // category -> tier menu
            added.Add(Trader($"pd-cat-{TraderDialogue.Lower(cat)}",
                $"What sort of word on the {TraderDialogue.CategoryWord(cat)} do you want?", $"pd-cat-{TraderDialogue.Lower(cat)}-menu"));
            added.Add(TierMenu(cat, tiers, config, traderEntityId));

            foreach (var tier in tiers)
            {
                int price = config.PriceFor(cat, tier);
                string c = TraderDialogue.Lower(cat), t = TraderDialogue.Lower(tier);

                // Trader restates the price and asks for confirmation.
                added.Add(Trader($"pd-confirm-{c}-{t}",
                    $"A {t} on the {TraderDialogue.CategoryWord(cat)} runs {TraderDialogue.PriceLabel(price)}. Shall I share what I've heard?",
                    $"pd-confirm-{c}-{t}-menu"));

                // Player confirms or backs out.
                added.Add(PlayerMenu($"pd-confirm-{c}-{t}-menu",
                    Opt("Aye, here's the coin.", $"pd-buy-{c}-{t}"),
                    Opt("On second thought, no.", "pd-deposits-menu")));

                // The purchase trigger (fires the handler), then a neutral acknowledgement.
                added.Add(new DlgGenericComponent
                {
                    Code = $"pd-buy-{c}-{t}",
                    Owner = "trader",
                    Trigger = TraderDialogue.TriggerFor(cat, tier),
                    JumpTo = "pd-bought"
                });
            }
        }

        result.components = result.components.Concat(added).ToArray();

        // Assign Ids to ONLY the newly-injected answer options, continuing past the highest existing Id.
        //
        // We must NOT call result.Init() here. DialogueConfig.Init() renumbers EVERY option from a counter
        // that loadDialogue already advanced, so it would shift the vanilla options' Ids. The client and the
        // server load and Init the dialogue independently and exchange the selected option purely by Id
        // (DlgTalkComponent.SelectAnswerById), so shifting the vanilla Ids on one side desyncs every menu
        // pick - including vanilla "I would like to trade" - and the player can no longer buy or sell.
        // Leaving the vanilla Ids untouched keeps the trade path aligned regardless of side/config skew.
        int nextId = result.components
            .OfType<DlgTalkComponent>()
            .SelectMany(c => c.Text ?? Enumerable.Empty<DialogeTextElement>())
            .Select(e => e.Id)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        main.Text[^1].Id = nextId++; // the "ask about deposits" option we appended above
        foreach (var comp in added.OfType<DlgTalkComponent>())
        {
            foreach (var el in comp.Text) el.Id = nextId++;
        }
        return true;
    }

    private static DlgTalkComponent CategoryMenu(ResourceCategory[] cats, bool iogActive)
    {
        var opts = cats
            .Select(cat => Opt(
                cat == ResourceCategory.Districts ? "Rich mining grounds." : cat.ToString() + ".",
                $"pd-cat-{TraderDialogue.Lower(cat)}"))
            .ToList();

        if (iogActive)
        {
            var primer = Opt("What ore hides in the rocks hereabouts?", "pd-primer");
            primer.Conditions = new[]
            {
                new ConditionElement { Variable = "player." + TraderDialogue.PrimerVar, IsValue = "1", Invert = true }
            };
            opts.Add(primer);
        }

        opts.Add(Opt("Never mind.", "main"));
        return PlayerMenu("pd-deposits-menu", opts.ToArray());
    }

    private static DlgTalkComponent TierMenu(ResourceCategory cat, List<DispatchTier> tiers,
        ProspectorsDispatchConfig config, long traderEntityId)
    {
        var opts = tiers
            .Select(tier =>
            {
                var opt = Opt(
                    $"{TraderDialogue.TierName(tier)} - {TraderDialogue.PriceLabel(config.PriceFor(cat, tier))}.",
                    $"pd-confirm-{TraderDialogue.Lower(cat)}-{TraderDialogue.Lower(tier)}");

                // Hide a tier once bought from this trader; a Rumour is also pointless once the (superset)
                // Survey is owned. Conditions only filter what is RENDERED - option Ids are assigned to
                // every element at Init regardless, so hidden options can never desync client and server.
                var conds = new List<ConditionElement>
                {
                    NotBought(traderEntityId, cat, tier)
                };
                if (tier == DispatchTier.Rumor)
                    conds.Add(NotBought(traderEntityId, cat, DispatchTier.Survey));
                opt.Conditions = conds.ToArray();
                return opt;
            })
            .Append(Opt("Back.", "pd-deposits-menu"))
            .ToArray();
        return PlayerMenu($"pd-cat-{TraderDialogue.Lower(cat)}-menu", opts);
    }

    /// <summary>Condition: the player has NOT bought this tier from this trader yet.</summary>
    private static ConditionElement NotBought(long traderEntityId, ResourceCategory cat, DispatchTier tier)
        => new ConditionElement
        {
            Variable = "player." + TraderDialogue.BoughtVar(traderEntityId, cat, tier),
            IsValue = "1",
            Invert = true
        };

    private static DlgTalkComponent Trader(string code, string line, string jumpTo)
        => new DlgTalkComponent { Code = code, Owner = "trader", Type = "talk", JumpTo = jumpTo,
            Text = new[] { new DialogeTextElement { Value = line } } };

    private static DlgTalkComponent PlayerMenu(string code, params DialogeTextElement[] options)
        => new DlgTalkComponent { Code = code, Owner = "player", Type = "talk", Text = options };

    private static DialogeTextElement Opt(string value, string jumpTo)
        => new DialogeTextElement { Value = value, JumpTo = jumpTo };
}

/// <summary>Registers the dispatch trigger handler on each trader's dialogue controller.</summary>
[HarmonyPatch(typeof(EntityTradingHumanoid), "Initialize")]
public static class TraderDialogueTriggerPatch
{
    public static void Postfix(EntityTradingHumanoid __instance)
    {
        var conv = __instance.GetBehavior<EntityBehaviorConversable>();
        if (conv == null) return;

        conv.OnControllerCreated += controller =>
            controller.DialogTriggers += (triggeringEntity, value, data) =>
                HandleTrigger(__instance, triggeringEntity, value);
    }

    // Returns -1 ("no flow override") like the vanilla inventory triggers; the dialogue option's own
    // jumpTo drives flow. We only perform the side effects (charge gears, file the journal).
    private static int HandleTrigger(EntityTradingHumanoid trader, EntityAgent triggeringEntity, string value)
    {
        if (value == null || !value.StartsWith(TraderDialogue.TriggerPrefix)) return -1;

        var world = trader.World;
        if (world.Side != EnumAppSide.Server) return -1;
        if ((triggeringEntity as EntityPlayer)?.Player is not IServerPlayer player) return -1;

        if (value == TraderDialogue.PrimerTrigger) return HandlePrimer(trader, triggeringEntity, player);

        string[] parts = value.Substring(TraderDialogue.TriggerPrefix.Length).Split('-');
        if (parts.Length != 2
            || !Enum.TryParse(parts[0], ignoreCase: true, out ResourceCategory category)
            || !Enum.TryParse(parts[1], ignoreCase: true, out DispatchTier tier))
        {
            return -1;
        }

        var mod = world.Api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        if (mod == null) return -1;
        var sampler = mod.Sampler;
        var config = mod.Config;

        // Already bought? Refuse politely, charge nothing. This is the authoritative backstop behind the
        // menu-hiding conditions (covers a stale menu mid-conversation, sync latency, etc.). A Survey also
        // covers its Rumor - a downgrade re-buy would overwrite the better journal entry.
        var varSys = world.Api.ModLoader.GetModSystem<VariablesModSystem>();
        int wantRank = tier == DispatchTier.Survey ? 2 : 1;
        if (BoughtRank(varSys, player.PlayerUID, trader.EntityId, category) >= wantRank)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-already"), EnumChatType.Notification);
            return -1;
        }

        var pos = trader.Pos.AsBlockPos;

        // Don't charge for a category with nothing in range - the trader simply has no word to share.
        bool anyInRange;
        if (category == ResourceCategory.Districts)
        {
            anyInRange = mod.DistrictSampler.Active
                && mod.DistrictSampler.FindDistricts(pos.X, pos.Z, config.DistrictSearchRadius).Count > 0;
        }
        else
        {
            if (sampler == null || !sampler.Ready) return -1;
            anyInRange = sampler.CodesInCategory(category)
                .Any(code => sampler.Sample(code, pos.X, pos.Z, config.SearchRadius).Found);
        }
        if (!anyInRange)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-none", TraderDialogue.CategoryWord(category)), EnumChatType.Notification);
            return -1;
        }

        int price = config.PriceFor(category, tier);
        if (price > 0 && InventoryTrader.GetPlayerAssets(triggeringEntity) < price)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-nofunds", price), EnumChatType.Notification);
            return -1;
        }

        if (price > 0) InventoryTrader.DeductFromEntity(world.Api, triggeringEntity, price);

        // Key the journal entry by the trader's stable EntityId (traders wander, so position is not stable).
        DispatchJournal.File((ICoreServerAPI)world.Api, player, sampler, category, tier, pos.X, pos.Z,
            sourceKey: "t" + trader.EntityId, traderEntityId: trader.EntityId);

        // Record the purchase (player-scoped dialogue variable: savegame-persisted, and what the
        // menu-hiding conditions read), then push fresh variable state to the buyer so the bought
        // option disappears immediately - the variables channel only auto-syncs on join.
        if (varSys != null)
        {
            varSys.SetPlayerVariable(player.PlayerUID,
                TraderDialogue.BoughtVar(trader.EntityId, category, tier), "1");
            ((ICoreServerAPI)world.Api).Network.GetChannel("variable").SendPacket(varSys.VarData, player);
        }

        player.SendMessage(GlobalConstants.GeneralChatGroup,
            price > 0
                ? Lang.Get("prospectorsdispatch:trade-filed-paid", price)
                : Lang.Get("prospectorsdispatch:trade-filed-free"),
            EnumChatType.Notification);
        return -1;
    }

    // The primer purchase: one-time per player (global bought-flag), world-static content, dirt cheap.
    private static int HandlePrimer(EntityTradingHumanoid trader, EntityAgent triggeringEntity, IServerPlayer player)
    {
        var world = trader.World;
        var mod = world.Api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        var sampler = mod?.Sampler;
        if (sampler == null || !sampler.Ready) return -1;
        var config = mod!.Config;

        var varSys = world.Api.ModLoader.GetModSystem<VariablesModSystem>();
        if (varSys?.GetPlayerVariable(player.PlayerUID, TraderDialogue.PrimerVar) == "1")
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-already"), EnumChatType.Notification);
            return -1;
        }

        int price = config.Prices.Primer;
        if (price > 0 && InventoryTrader.GetPlayerAssets(triggeringEntity) < price)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-nofunds", price), EnumChatType.Notification);
            return -1;
        }
        if (price > 0) InventoryTrader.DeductFromEntity(world.Api, triggeringEntity, price);

        DispatchJournal.FilePrimer((ICoreServerAPI)world.Api, player, sampler);

        if (varSys != null)
        {
            varSys.SetPlayerVariable(player.PlayerUID, TraderDialogue.PrimerVar, "1");
            ((ICoreServerAPI)world.Api).Network.GetChannel("variable").SendPacket(varSys.VarData, player);
        }

        player.SendMessage(GlobalConstants.GeneralChatGroup,
            price > 0
                ? Lang.Get("prospectorsdispatch:trade-filed-paid", price)
                : Lang.Get("prospectorsdispatch:trade-filed-free"),
            EnumChatType.Notification);
        return -1;
    }

    /// <summary>Highest tier already bought from this trader for this category: 0 none, 1 Rumor, 2 Survey.</summary>
    private static int BoughtRank(VariablesModSystem? varSys, string playerUid, long traderEntityId, ResourceCategory cat)
    {
        if (varSys == null) return 0;
        if (varSys.GetPlayerVariable(playerUid, TraderDialogue.BoughtVar(traderEntityId, cat, DispatchTier.Survey)) == "1") return 2;
        if (varSys.GetPlayerVariable(playerUid, TraderDialogue.BoughtVar(traderEntityId, cat, DispatchTier.Rumor)) == "1") return 1;
        return 0;
    }
}
