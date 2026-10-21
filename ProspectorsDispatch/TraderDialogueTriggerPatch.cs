using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ProspectorsDispatch;

/// <summary>
/// Handles the purchase triggers fired by the injected dialogue (see <see cref="TraderDialogueInjectPatch"/>):
/// charges the gears and files the journal entry, on the server only.
/// </summary>
[HarmonyPatch(typeof(EntityTradingHumanoid), "Initialize")]
public static class TraderDialogueTriggerPatch
{
    public static void Postfix(EntityTradingHumanoid __instance)
    {
        var conversable = __instance.GetBehavior<EntityBehaviorConversable>();
        if (conversable == null) return;

        conversable.OnControllerCreated += controller =>
            controller.DialogTriggers += (triggeringEntity, value, data) =>
                HandleTrigger(__instance, triggeringEntity, value);
    }

    // Like the vanilla inventory triggers, this returns -1 ("no jump override") so the dialogue option's
    // own JumpTo controls the flow. Only the side effects happen here.
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

        var api = (ICoreServerAPI)world.Api;
        var mod = api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        if (mod == null) return -1;
        var config = mod.Config;

        // Refuse a repeat purchase without charging. The menu conditions normally hide these options;
        // this covers a menu that is out of date, such as one left open during the purchase. A Survey
        // also covers its Rumor, which would otherwise overwrite the better journal entry.
        var variables = api.ModLoader.GetModSystem<VariablesModSystem>();
        int wantedRank = tier == DispatchTier.Survey ? 2 : 1;
        if (BoughtRank(variables, player.PlayerUID, trader.EntityId, category) >= wantedRank)
        {
            Notify(player, Lang.Get("prospectorsdispatch:trade-already"));
            return -1;
        }

        var pos = trader.Pos.AsBlockPos;

        // Don't charge for a category with nothing in range; the trader simply has no news.
        bool anyInRange;
        if (category == ResourceCategory.Districts)
        {
            anyInRange = mod.DistrictSampler.Active
                && mod.DistrictSampler.FindDistricts(pos.X, pos.Z, config.DistrictSearchRadius).Count > 0;
        }
        else
        {
            var sampler = mod.Sampler;
            if (!sampler.Ready) return -1;
            anyInRange = sampler.CodesInCategory(category)
                .Any(code => sampler.Sample(code, pos.X, pos.Z, config.SearchRadius).Found);
        }
        if (!anyInRange)
        {
            Notify(player, Lang.Get("prospectorsdispatch:trade-none", TraderDialogue.CategoryWord(category)));
            return -1;
        }

        int price = config.PriceFor(category, tier);
        if (!TryCharge(api, triggeringEntity, player, price)) return -1;

        // The entry is keyed by the trader's entity ID because traders wander.
        DispatchJournal.File(api, player, mod.Sampler, category, tier, pos.X, pos.Z,
            sourceKey: "t" + trader.EntityId, traderEntityId: trader.EntityId);

        MarkBought(api, variables, player, TraderDialogue.BoughtVar(trader.EntityId, category, tier));
        NotifyFiled(player, price);
        return -1;
    }

    // The primer is bought once per player, from any trader.
    private static int HandlePrimer(EntityTradingHumanoid trader, EntityAgent triggeringEntity, IServerPlayer player)
    {
        var api = (ICoreServerAPI)trader.World.Api;
        var mod = api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        if (mod == null || !mod.Sampler.Ready) return -1;

        var variables = api.ModLoader.GetModSystem<VariablesModSystem>();
        if (variables?.GetPlayerVariable(player.PlayerUID, TraderDialogue.PrimerVar) == "1")
        {
            Notify(player, Lang.Get("prospectorsdispatch:trade-already"));
            return -1;
        }

        int price = mod.Config.Prices.Primer;
        if (!TryCharge(api, triggeringEntity, player, price)) return -1;

        DispatchJournal.FilePrimer(api, player, mod.Sampler);

        MarkBought(api, variables, player, TraderDialogue.PrimerVar);
        NotifyFiled(player, price);
        return -1;
    }

    // Takes the price in gears, or tells the player they cannot afford it.
    private static bool TryCharge(ICoreServerAPI api, EntityAgent buyer, IServerPlayer player, int price)
    {
        if (price <= 0) return true;

        if (InventoryTrader.GetPlayerAssets(buyer) < price)
        {
            Notify(player, Lang.Get("prospectorsdispatch:trade-nofunds", price));
            return false;
        }

        InventoryTrader.DeductFromEntity(api, buyer, price);
        return true;
    }

    // Records the purchase in a player variable, which is saved with the world and read by the menu
    // conditions. Variables only sync to the client when the player joins, so they are pushed to the
    // buyer here to hide the purchased option straight away.
    private static void MarkBought(ICoreServerAPI api, VariablesModSystem? variables, IServerPlayer player, string variable)
    {
        if (variables == null) return;

        variables.SetPlayerVariable(player.PlayerUID, variable, "1");
        api.Network.GetChannel("variable").SendPacket(variables.VarData, player);
    }

    private static void NotifyFiled(IServerPlayer player, int price)
    {
        Notify(player, price > 0
            ? Lang.Get("prospectorsdispatch:trade-filed-paid", price)
            : Lang.Get("prospectorsdispatch:trade-filed-free"));
    }

    private static void Notify(IServerPlayer player, string message)
        => player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

    // The highest tier already bought from this trader for this category: 0 none, 1 Rumor, 2 Survey.
    private static int BoughtRank(VariablesModSystem? variables, string playerUid, long traderEntityId, ResourceCategory category)
    {
        if (variables == null) return 0;
        if (variables.GetPlayerVariable(playerUid, TraderDialogue.BoughtVar(traderEntityId, category, DispatchTier.Survey)) == "1") return 2;
        if (variables.GetPlayerVariable(playerUid, TraderDialogue.BoughtVar(traderEntityId, category, DispatchTier.Rumor)) == "1") return 1;
        return 0;
    }
}
