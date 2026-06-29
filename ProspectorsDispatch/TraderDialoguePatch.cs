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
/// No item and no trade-window slots — the knowledge goes straight to the journal through conversation.
/// </summary>
public static class TraderDialogue
{
    public const string TriggerPrefix = "pddispatch-";

    internal static string Lower(ResourceCategory cat) => cat.ToString().ToLowerInvariant();
    internal static string Lower(DispatchTier tier) => tier.ToString().ToLowerInvariant();

    internal static string TriggerFor(ResourceCategory cat, DispatchTier tier)
        => TriggerPrefix + Lower(cat) + "-" + Lower(tier);

    internal static string TierName(DispatchTier tier) => tier == DispatchTier.Rumor ? "Rumour" : "Full survey";

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

        Inject(__result, config);
    }

    /// <summary>
    /// Adds the "ask about deposits" option to the dialogue's main menu plus the nested branch
    /// (category -> tier+price -> confirm -> buy). Idempotent. Returns true if it modified the config.
    /// </summary>
    public static bool Inject(DialogueConfig result, ProspectorsDispatchConfig config)
    {
        if (result?.components == null) return false;
        if (result.components.Any(c => c.Code == "pd-deposits")) return false; // already injected

        var tiers = new List<DispatchTier>();
        if (config.OfferRumorTier) tiers.Add(DispatchTier.Rumor);
        if (config.OfferSurveyTier) tiers.Add(DispatchTier.Survey);
        if (tiers.Count == 0) return false;

        if (result.components.FirstOrDefault(c => c.Code == "main") is not DlgTalkComponent main) return false;
        main.Text = main.Text.Append(Opt("Heard of any ore deposits worth the walk?", "pd-deposits")).ToArray();

        var added = new List<DialogueComponent>
        {
            Trader("pd-deposits", "Deposits? Aye, word gets around. What are you after?", "pd-deposits-menu"),
            CategoryMenu(),
            Trader("pd-bought", "Anything else I can help you with?", "pd-deposits-menu"),
        };

        foreach (var cat in Enum.GetValues<ResourceCategory>())
        {
            // category -> tier menu
            added.Add(Trader($"pd-cat-{TraderDialogue.Lower(cat)}",
                $"What sort of word on the {TraderDialogue.Lower(cat)} do you want?", $"pd-cat-{TraderDialogue.Lower(cat)}-menu"));
            added.Add(TierMenu(cat, tiers, config));

            foreach (var tier in tiers)
            {
                int price = config.PriceFor(cat, tier);
                string c = TraderDialogue.Lower(cat), t = TraderDialogue.Lower(tier);

                // Trader restates the price and asks for confirmation.
                added.Add(Trader($"pd-confirm-{c}-{t}",
                    $"A {t} on the {c} runs {TraderDialogue.PriceLabel(price)}. Shall I share what I've heard?",
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

        // CRITICAL: loadDialogue already ran Init() (which assigns each answer option a unique Id) before
        // this postfix added components — so our options would all keep Id 0 and every click would select
        // the first option. Re-run Init() to assign Ids to the new options (the controller isn't built yet).
        result.Init();
        return true;
    }

    private static DlgTalkComponent CategoryMenu()
    {
        var opts = Enum.GetValues<ResourceCategory>()
            .Select(cat => Opt(cat.ToString() + ".", $"pd-cat-{TraderDialogue.Lower(cat)}"))
            .Append(Opt("Never mind.", "main"))
            .ToArray();
        return PlayerMenu("pd-deposits-menu", opts);
    }

    private static DlgTalkComponent TierMenu(ResourceCategory cat, List<DispatchTier> tiers, ProspectorsDispatchConfig config)
    {
        var opts = tiers
            .Select(tier => Opt(
                $"{TraderDialogue.TierName(tier)} - {TraderDialogue.PriceLabel(config.PriceFor(cat, tier))}.",
                $"pd-confirm-{TraderDialogue.Lower(cat)}-{TraderDialogue.Lower(tier)}"))
            .Append(Opt("Back.", "pd-deposits-menu"))
            .ToArray();
        return PlayerMenu($"pd-cat-{TraderDialogue.Lower(cat)}-menu", opts);
    }

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

        string[] parts = value.Substring(TraderDialogue.TriggerPrefix.Length).Split('-');
        if (parts.Length != 2
            || !Enum.TryParse(parts[0], ignoreCase: true, out ResourceCategory category)
            || !Enum.TryParse(parts[1], ignoreCase: true, out DispatchTier tier))
        {
            return -1;
        }

        var mod = world.Api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        var sampler = mod?.Sampler;
        if (sampler == null || !sampler.Ready) return -1;
        var config = mod!.Config;

        var pos = trader.Pos.AsBlockPos;

        // Don't charge for a category with nothing in range — the trader simply has no word to share.
        bool anyInRange = ResourceCatalog.All
            .Where(e => e.Category == category)
            .Any(e => sampler.Sample(e.Code, pos.X, pos.Z, config.SearchRadius).Found);
        if (!anyInRange)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("prospectorsdispatch:trade-none", category), EnumChatType.Notification);
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
            sourceKey: "t" + trader.EntityId);

        player.SendMessage(GlobalConstants.GeneralChatGroup,
            price > 0
                ? Lang.Get("prospectorsdispatch:trade-filed-paid", price)
                : Lang.Get("prospectorsdispatch:trade-filed-free"),
            EnumChatType.Notification);
        return -1;
    }
}
