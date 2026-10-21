using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ProspectorsDispatch;

/// <summary>Loads the config, applies the Harmony patches and initializes the samplers.</summary>
public sealed class ProspectorsDispatchModSystem : ModSystem
{
    private const string HarmonyId = "prospectorsdispatch";
    private const string ConfigFilename = "ProspectorsDispatch.json";

    // Static because Harmony patches apply to the whole process. In single-player the client and server
    // each create this mod system in the same process; patching from both would run every postfix twice
    // and charge players twice per purchase.
    private static Harmony? s_harmony;

    /// <summary>Reads the vanilla ore maps. Server side.</summary>
    public OreMapSampler Sampler { get; } = new();

    /// <summary>Locates Interesting Ore Gen 2.x districts. Server side, inactive without IOG 2.x.</summary>
    public IogDistrictSampler DistrictSampler { get; } = new();

    /// <summary>How Interesting Ore Gen affects the trader dialogue. Set on both sides once assets load.</summary>
    public IogSupport Iog { get; private set; } = IogSupport.Absent;

    /// <summary>Server settings. Never null; the client always uses the defaults.</summary>
    public ProspectorsDispatchConfig Config { get; private set; } = new();

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // The patches must be applied on both sides. Client and server number the trader's dialogue
        // options separately, and the client sends only the chosen option's ID. If only one side added
        // the dispatch branch, the IDs would differ and every choice, including the vanilla trade
        // option, would resolve to the wrong option on the server. Purchases still happen server side.
        if (s_harmony != null) return;

        s_harmony = new Harmony(HarmonyId);
        s_harmony.PatchAll(typeof(ProspectorsDispatchModSystem).Assembly);
        Mod.Logger.Notification("[ProspectorsDispatch] Harmony patches applied: {0}",
            string.Join(", ", s_harmony.GetPatchedMethods().Select(m => m.Name)));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Runs on both sides before any trader dialogue loads.
        Iog = IogDistrictSampler.DetectSupport(api);
        if (Iog == IogSupport.Unsupported)
        {
            Mod.Logger.Warning(
                "[ProspectorsDispatch] Interesting Ore Gen {0} has no hydrothermal districts and disables vanilla ore generation, so traders have nothing to point at. Dispatches are disabled.",
                api.ModLoader.GetMod(IogDistrictSampler.IogModId)?.Info.Version);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        // Load the config, creating it with defaults on first run. Storing it again adds any settings
        // introduced since the file was written.
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

        // Worldgen deposits are ready by the RunGame phase, the same phase the vanilla prospecting pick
        // uses. Purchases attempted before then are declined because the sampler is not ready.
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            Sampler.Init(api);
            DistrictSampler.Init(api);
        });
    }

    public override void Dispose()
    {
        s_harmony?.UnpatchAll(HarmonyId);
        s_harmony = null;
    }
}
