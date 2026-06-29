# Prospector's Dispatch

A Vintage Story (1.22, .NET 10) code mod that lets players **buy resource-location leads from traders**
instead of wandering thousands of blocks prospecting blind. Intended as a *game mechanic* — guidance,
not a solver — so it never trivializes exploration.

## The idea

Tech progression often gates on a resource (copper, tin, iron…) that may be far from spawn, with no
in-game hint of which way to go. This mod lets a player visit *any* trader, ask about deposits, pay a
gear, and receive a **dispatch**: a hedged, second-hand rumor pointing toward a resource — a direction,
and (for the pricier tier) an approximate distance. The dispatch is filed as a permanent, re-readable
entry in the player's **Journal**.

### Design rules

- **Direction + distance only — never exact coordinates.** Traders deal in hearsay, not a grid.
- **Two price tiers** (the quality/cost ladder):
  - **Rumour** (cheap): a vague **4-point** heading (N/E/S/W), no distance — the triangulation tier
    (cross bearings from several traders).
  - **Survey** (pricey): a precise **8-point** heading + an approximate **"roughly N paces"** distance,
    plus the trader's logged location — the "skip the legwork" tier.
  - **Availability follows the world, not luck:** *every* trader sells **both** tiers for *every* category
    with resources in range — no trader-type gating, no random rolls. The gate is **price** (Survey costs
    more; pricier categories cost more). This is deliberate: the mod exists to remove bad-spawn dead-ends,
    so guidance is always obtainable — it's the *detailed* guidance that costs gears.
- **Category-scoped** dispatches (Ores / Gems / Minerals), not one per resource. Each dispatch lists the
  **nearest 5** resources in its category, which nudges players to seek out *other* traders for the rest.
- **Per-resource descriptors:** a curated **rarity** tag (Common…Exotic) and a data-derived **vein size**
  (scattered specks → large masses, from the deposit's worldgen `radius`).
- **Hedged, never-definitive prose** ("Word has it… or so I heard"), even for a paid Survey.

## How it works (data source)

Ore distribution is **deterministic seeded worldgen noise**, so it can be computed for *unexplored*
regions without the player ever going there:

- `OreMapSampler` samples each deposit's `OreMapLayer.GenLayer(...)` (from `GenDeposits` in VSEssentials)
  over a radius around an origin, finds the nearest peak of that ore's abundance map, and returns a
  direction + distance. (It deliberately avoids `GetOreMapFactor`, which returns 0 for ungenerated
  regions.) Works server-side, for any coordinates.
- The ore map encodes *where* an ore concentrates, **not** its rarity or richness — so rarity is a
  curated table and vein size comes from the deposit `radius`, not from the ore map.

## How it works (delivery)

Talk to *any* trader → **"Heard of any ore deposits worth the walk?"** → choose a category → choose a
tier (with its gear price shown) → confirm → the journal entry is filed, reckoned from the trader's
position, and gears are deducted. No item and no trade-window slots — this sidesteps the trader's hard
16-slot limit entirely.

It's implemented as two Harmony patches in `TraderDialoguePatch.cs`:

- One injects the nested dialogue branch into each trader's `DialogueConfig` as it loads (in code —
  `config/dialogue/*` assets aren't reachable by the JSON patch system, and the injected options are
  re-`Init()`'d so they get unique answer IDs).
- The other handles the `pddispatch-<category>-<tier>` trigger server-side (charge gears via
  `InventoryTrader`, file the journal).

Journal entries are keyed by the trader's `EntityId` (stable — traders wander) and titled with the buy
coordinates relative to world spawn.

**Mod compatibility:** the dialogue injection is a Harmony *postfix* that only adds its own branch and
handles its own `pddispatch-*` trigger — it doesn't touch other mods' wares or trade slots, so it
coexists cleanly with other trader mods.

## Project layout

| File | Role |
|---|---|
| `OreMapSampler.cs` | Samples worldgen ore maps → direction/distance; derives vein size from deposit radius. |
| `ResourceCatalog.cs` | Curated table: each resource → category (Ores/Gems/Minerals) + rarity. **The main tuning surface.** |
| `DispatchReport.cs` | Renders a (category, tier, origin) into the journal title + hedged narrative body. |
| `DispatchJournal.cs` | Files a report into the native Journal (`ModJournal.AddOrUpdateJournalEntry`), with reflection to set the client-side entry index. |
| `TraderDialoguePatch.cs` | **The delivery layer.** Injects the "ask about deposits" branch into every trader's dialogue (in code) and handles the `pddispatch-<cat>-<tier>` trigger: charges the configured gears and files the journal from the trader's position. |
| `ProspectorsDispatchConfig.cs` | Player-editable settings (radius, prices, tiers). **The pricing/availability tuning surface.** |
| `ProspectorsDispatchModSystem.cs` | Wiring: config load, Harmony bootstrap, and sampler initialization. |
| `assets/prospectorsdispatch/lang/en.json` | Trade message strings. |

## Pricing

Defaults (config `Prices`, in gears):

| Category | Rumour | Survey |
|---|---|---|
| Minerals | 1 | 4 |
| Ores | 1 | 6 |
| Gems | 2 | 10 |

Rumours sit at the everyday-goods floor (the cheap triangulation tier); Surveys sit in the
"location-info" tier, anchored to the treasure hunter's own 12-gear treasure map.

## Configuration

Settings live in `VintagestoryData/ModConfig/ProspectorsDispatch.json` (created with defaults on first run;
updating the mod back-fills any new keys). It is **server-side** — on a multiplayer server the host's config
governs. Edit and restart to apply.

| Setting | Default | Effect |
|---|---|---|
| `SearchRadius` | 5000 | How far a trader "knows", and the radius readings are reckoned over. |
| `MaxResourcesPerDispatch` | 5 | Nearest-N resources listed per dispatch. |
| `OfferRumorTier` / `OfferSurveyTier` | true | Toggle each tier on/off (hides it from the trader dialogue). |
| `Prices` | (table above) | Gear price per category × tier. **Set any to `0` for free.** |

## Build & test

Requires the .NET 10 SDK and the `VINTAGE_STORY` env var pointing at the game install.

**Quick dev loop:** `run.ps1` (repo root) builds the mod and launches the game with it loaded:

```powershell
.\run.ps1                 # build, then launch to the main menu (click your world)
.\run.ps1 -NoBuild        # skip the build, just launch
.\run.ps1 -World pdtest   # also auto-open a named world (creates it if missing)
```

Close the game before re-running — it locks the mod DLL while running. The manual equivalent:

```powershell
# build
dotnet build ProspectorsDispatch/ProspectorsDispatch.csproj -c Debug

# run the game with the dev build loaded. Assets (lang) are bundled into the mod output folder by the
# build, so --addModPath alone loads code + assets. NOTE: Vintagestory.exe resolves --addModPath against
# ITS OWN install dir, not your shell's working directory — so it must be an ABSOLUTE path:
& "$env:VINTAGE_STORY\Vintagestory.exe" --addModPath "<repo>\ProspectorsDispatch\bin\Debug\Mods"
```

In-game, in a **normal-terrain** world (ore maps don't exist in superflat/creative-flat worlds): find or
spawn a trader, ask **"Heard of any ore deposits worth the walk?"**, buy a dispatch, then open the Journal
(default **J**) to read it.

The package build (`dotnet run --project CakeBuild/CakeBuild.csproj`) produces a distributable zip in
`Releases/`.
