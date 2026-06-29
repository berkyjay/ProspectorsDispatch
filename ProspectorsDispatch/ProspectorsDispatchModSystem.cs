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
    private Harmony? harmony;

    /// <summary>Server-side ore-map sampler, used by the dialogue handler to build readings.</summary>
    public OreMapSampler Sampler => sampler;

    /// <summary>Player-editable settings (radius, prices, tier toggles). Never null.</summary>
    public ProspectorsDispatchConfig Config { get; private set; } = new ProspectorsDispatchConfig();

    public override void StartServerSide(ICoreServerAPI api)
    {
        // Load player-editable config (creates the file with defaults on first run; re-writing it also
        // adds any newly-introduced settings to an existing file).
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

        // Patch the trader dialogue so traders offer dispatches. Patching here (server-only) applies the
        // patch once for the process; the patches themselves also guard on the server side.
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll(typeof(ProspectorsDispatchModSystem).Assembly);
        Mod.Logger.Notification("[ProspectorsDispatch] Harmony patches applied: {0}",
            string.Join(", ", harmony.GetPatchedMethods().Select(m => m.Name)));

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
