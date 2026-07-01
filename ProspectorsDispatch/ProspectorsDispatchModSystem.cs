using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace ProspectorsDispatch;

public class ProspectorsDispatchModSystem : ModSystem
{
    private const string HarmonyId = "prospectorsdispatch";
    private const string ConfigFilename = "ProspectorsDispatch.json";

    private readonly OreMapSampler sampler = new OreMapSampler();

    // STATIC: Harmony patches are process-global, and in single-player the client and server each get
    // their own ModSystem instance in the ONE shared process, so Start() runs twice. A per-instance field
    // let both instances patch, applying every postfix twice — which double-fired the purchase handler and
    // charged the player twice. A static handle patches the process exactly once, covering both sides.
    private static Harmony? harmony;

    /// <summary>Server-side ore-map sampler, used by the dialogue handler to build readings.</summary>
    public OreMapSampler Sampler => sampler;

    /// <summary>Player-editable settings (radius, prices, tier toggles). Never null.</summary>
    public ProspectorsDispatchConfig Config { get; private set; } = new ProspectorsDispatchConfig();

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Patch the trader dialogue so traders offer dispatches. This MUST run on BOTH sides.
        //
        // The trader conversation (including the vanilla "I would like to trade" option) is loaded and
        // Id-numbered independently on the client and the server (EntityBehaviorConversable.loadDialogue).
        // When a player picks an option the client sends only its Id to the server, which resolves it with
        // DlgTalkComponent.SelectAnswerById. If we inject the dispatch branch on only one side, that side's
        // option Ids shift relative to the other, so every menu pick -- vanilla trade included -- maps to
        // the wrong option (or none) on the server and the player can no longer buy or sell. Patching only
        // in StartServerSide worked in single-player (one process = both sides) but broke multiplayer,
        // where the client is a separate, unpatched process. Start() runs on both sides, before any trader
        // dialogue loads. Side effects (charging gears, filing the journal) still guard on the server side.
        if (harmony == null)
        {
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(ProspectorsDispatchModSystem).Assembly);
            Mod.Logger.Notification("[ProspectorsDispatch] Harmony patches applied: {0}",
                string.Join(", ", harmony.GetPatchedMethods().Select(m => m.Name)));
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        // Load player-editable config (creates the file with defaults on first run; re-writing it also
        // adds any newly-introduced settings to an existing file). Server-only: prices/tiers govern the
        // server-side handler, and the client injects with defaults (structure matches; see Inject).
        try
        {
            Config = api.LoadModConfig<ProspectorsDispatchConfig>(ConfigFilename) ?? new ProspectorsDispatchConfig();
        }
        catch (Exception e)
        {
            Mod.Logger.Error("[ProspectorsDispatch] failed to load config, using defaults: {0}", e.Message);
            Config = new ProspectorsDispatchConfig();
        }
        api.StoreModConfig(Config, ConfigFilename);

        // Initialize the sampler once worldgen is ready. Deposits/worldgen are live by the RunGame phase
        // (the same phase the vanilla prospecting pick uses to read GenDeposits), and the dialogue handler
        // guards on sampler.Ready, so any trade attempted before this point is simply declined.
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => sampler.Init(api));
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }
}
