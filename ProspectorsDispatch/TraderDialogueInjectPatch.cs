using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.GameContent;

namespace ProspectorsDispatch;

/// <summary>
/// Adds the dispatch branch to every trader's dialogue as it loads:
/// category, then tier with price, then confirmation, then purchase. This is done in code because
/// the config/dialogue assets cannot be changed with JSON patches.
/// </summary>
/// <remarks>
/// Client and server load and number the dialogue independently, and the client sends only the ID of
/// the option the player picked. The injected structure, and so every option ID, must therefore come
/// out the same on both sides. Everything that shapes it (the Interesting Ore Gen mode and the tier
/// switches) is either read from assets both sides have or left at the defaults on the client.
/// </remarks>
[HarmonyPatch(typeof(EntityBehaviorConversable), "loadDialogue")]
public static class TraderDialogueInjectPatch
{
    public static void Postfix(EntityBehaviorConversable __instance, ref DialogueConfig __result)
    {
        if (__result?.components == null) return;
        if (__instance.entity is not EntityTradingHumanoid) return;

        var mod = __instance.entity.Api.ModLoader.GetModSystem<ProspectorsDispatchModSystem>();
        if (mod?.Config is not ProspectorsDispatchConfig config) return;

        // IOG 3.0 and later has nothing to point at, so traders offer no dispatches at all.
        if (mod.Iog == IogSupport.Unsupported) return;

        Inject(__result, config, mod.Iog == IogSupport.Districts, __instance.entity.EntityId);
    }

    /// <summary>
    /// Adds the "ask about deposits" option to the main menu and the branch behind it. Does nothing if
    /// the branch is already present. Returns true if the dialogue was changed.
    /// </summary>
    /// <param name="iogDistricts">
    /// True when IOG 2.x is installed. Traders then sell only mining-grounds dispatches, since the
    /// vanilla categories would point at ore that IOG never places, and they also offer the primer.
    /// </param>
    public static bool Inject(DialogueConfig result, ProspectorsDispatchConfig config, bool iogDistricts = false,
        long traderEntityId = 0)
    {
        if (result?.components == null) return false;
        if (result.components.Any(c => c.Code == "pd-deposits")) return false;

        var tiers = new List<DispatchTier>();
        if (config.OfferRumorTier) tiers.Add(DispatchTier.Rumor);
        if (config.OfferSurveyTier) tiers.Add(DispatchTier.Survey);
        if (tiers.Count == 0) return false;

        var categories = iogDistricts
            ? new[] { ResourceCategory.Districts }
            : new[] { ResourceCategory.Ores, ResourceCategory.Gems, ResourceCategory.Minerals };

        if (result.components.FirstOrDefault(c => c.Code == "main") is not DlgTalkComponent main) return false;
        main.Text = main.Text.Append(Option("Heard of any ore deposits worth the walk?", "pd-deposits")).ToArray();

        var added = new List<DialogueComponent>
        {
            TraderLine("pd-deposits", "Deposits? Aye, word gets around. What are you after?", "pd-deposits-menu"),
            CategoryMenu(categories, iogDistricts),
            TraderLine("pd-bought", "Anything else I can help you with?", "pd-deposits-menu"),
        };

        if (iogDistricts) added.AddRange(PrimerBranch(config));

        foreach (var category in categories)
        {
            string c = TraderDialogue.Lower(category);
            added.Add(TraderLine($"pd-cat-{c}",
                $"What sort of word on the {TraderDialogue.CategoryWord(category)} do you want?", $"pd-cat-{c}-menu"));
            added.Add(TierMenu(category, tiers, config, traderEntityId));

            foreach (var tier in tiers)
            {
                int price = config.PriceFor(category, tier);
                string t = TraderDialogue.Lower(tier);

                added.Add(TraderLine($"pd-confirm-{c}-{t}",
                    $"A {t} on the {TraderDialogue.CategoryWord(category)} runs {TraderDialogue.PriceLabel(price)}. Shall I share what I've heard?",
                    $"pd-confirm-{c}-{t}-menu"));
                added.Add(PlayerMenu($"pd-confirm-{c}-{t}-menu",
                    Option("Aye, here's the coin.", $"pd-buy-{c}-{t}"),
                    Option("On second thought, no.", "pd-deposits-menu")));
                added.Add(new DlgGenericComponent
                {
                    Code = $"pd-buy-{c}-{t}",
                    Owner = "trader",
                    Trigger = TraderDialogue.TriggerFor(category, tier),
                    JumpTo = "pd-bought"
                });
            }
        }

        result.components = result.components.Concat(added).ToArray();
        AssignOptionIds(result, main, added);
        return true;
    }

    // Gives IDs to the new options only, continuing after the highest existing ID.
    //
    // DialogueConfig.Init() must not be called here. It renumbers every option from a counter that
    // loadDialogue has already advanced, which would shift the vanilla options' IDs on this side only.
    // The client and server would then disagree on every pick, including the vanilla trade option.
    private static void AssignOptionIds(DialogueConfig result, DlgTalkComponent main, List<DialogueComponent> added)
    {
        int nextId = result.components
            .OfType<DlgTalkComponent>()
            .SelectMany(c => c.Text ?? Enumerable.Empty<DialogeTextElement>())
            .Select(e => e.Id)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        main.Text[^1].Id = nextId++;
        foreach (var component in added.OfType<DlgTalkComponent>())
        {
            foreach (var element in component.Text) element.Id = nextId++;
        }
    }

    private static DlgTalkComponent CategoryMenu(ResourceCategory[] categories, bool iogDistricts)
    {
        var options = categories
            .Select(category => Option(
                category == ResourceCategory.Districts ? "Rich mining grounds." : category + ".",
                $"pd-cat-{TraderDialogue.Lower(category)}"))
            .ToList();

        if (iogDistricts)
        {
            var primer = Option("What ore hides in the rocks hereabouts?", "pd-primer");
            primer.Conditions = new[] { NotSet(TraderDialogue.PrimerVar) };
            options.Add(primer);
        }

        options.Add(Option("Never mind.", "main"));
        return PlayerMenu("pd-deposits-menu", options.ToArray());
    }

    // The primer: a cheap, one-time purchase of which rocks host which ores. Its menu option is added
    // in CategoryMenu and hidden at every trader once bought.
    private static IEnumerable<DialogueComponent> PrimerBranch(ProspectorsDispatchConfig config)
    {
        yield return TraderLine("pd-primer",
            $"Ha, the old prospector's primer. Which stone carries which ore, all writ down. It runs {TraderDialogue.PriceLabel(config.Prices.Primer)}. Care for a copy?",
            "pd-primer-menu");
        yield return PlayerMenu("pd-primer-menu",
            Option("Aye, here's the coin.", "pd-buy-primer"),
            Option("Not today.", "pd-deposits-menu"));
        yield return new DlgGenericComponent
        {
            Code = "pd-buy-primer",
            Owner = "trader",
            Trigger = TraderDialogue.PrimerTrigger,
            JumpTo = "pd-bought"
        };
    }

    private static DlgTalkComponent TierMenu(ResourceCategory category, List<DispatchTier> tiers,
        ProspectorsDispatchConfig config, long traderEntityId)
    {
        string c = TraderDialogue.Lower(category);
        var options = tiers
            .Select(tier =>
            {
                var option = Option(
                    $"{TraderDialogue.TierName(tier)} ({TraderDialogue.PriceLabel(config.PriceFor(category, tier))}).",
                    $"pd-confirm-{c}-{TraderDialogue.Lower(tier)}");

                // Hide a tier once bought from this trader, and hide the Rumor once the Survey (which
                // covers it) is owned. Conditions only affect what is shown; every option still gets an
                // ID, so hidden options cannot put client and server out of step.
                var conditions = new List<ConditionElement>
                {
                    NotSet(TraderDialogue.BoughtVar(traderEntityId, category, tier))
                };
                if (tier == DispatchTier.Rumor)
                {
                    conditions.Add(NotSet(TraderDialogue.BoughtVar(traderEntityId, category, DispatchTier.Survey)));
                }
                option.Conditions = conditions.ToArray();
                return option;
            })
            .Append(Option("Back.", "pd-deposits-menu"))
            .ToArray();

        return PlayerMenu($"pd-cat-{c}-menu", options);
    }

    // Condition that passes while the given player variable is not "1".
    private static ConditionElement NotSet(string playerVariable) => new()
    {
        Variable = "player." + playerVariable,
        IsValue = "1",
        Invert = true
    };

    private static DlgTalkComponent TraderLine(string code, string line, string jumpTo) => new()
    {
        Code = code,
        Owner = "trader",
        Type = "talk",
        JumpTo = jumpTo,
        Text = new[] { new DialogeTextElement { Value = line } }
    };

    private static DlgTalkComponent PlayerMenu(string code, params DialogeTextElement[] options) => new()
    {
        Code = code,
        Owner = "player",
        Type = "talk",
        Text = options
    };

    private static DialogeTextElement Option(string value, string jumpTo) => new()
    {
        Value = value,
        JumpTo = jumpTo
    };
}
