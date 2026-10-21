# Prospector's Dispatch

A Vintage Story 1.22 code mod (.NET 10) that lets players buy leads on resource locations from
traders instead of searching thousands of blocks blind. It gives guidance, not answers: a heading and,
for the more expensive tier, a rough distance, but never coordinates.

## Gameplay

Talk to any trader and choose "Heard of any ore deposits worth the walk?". Pick a category, pick a tier
(the price is shown), confirm, and the trader takes the gears and writes the dispatch into your journal
(default key J). Each dispatch is reckoned from the trader's position, so traders in different places
give different leads.

- **Categories:** Ores (metals and coal), Gems and Minerals. A dispatch lists the nearest resources in
  its category, five by default, so players have a reason to visit other traders for the rest.
- **Tiers:** a Rumor gives a 4-point heading (N/E/S/W) and no distance. A Survey gives an 8-point
  heading and a distance rounded to 100 paces.
- **Availability:** every trader sells both tiers of every category that has something in range. There
  is no random chance and no trader-type restriction; price is the only gate.
- **Entry details:** each resource shows a curated rarity and a vein size taken from its worldgen
  radius, from "scattered specks" to "large masses".
- **Bookkeeping:** a dispatch already bought from a trader disappears from that trader's menu, and
  buying a Survey also removes the Rumor.

## How it works

Vanilla ore placement follows seeded noise maps, so they can be read for land nobody has visited.
`OreMapSampler` calls each deposit's `OreMapLayer.GenLayer(...)` over the search area and returns the
direction and distance to the nearest strongest concentration. It avoids `GetOreMapFactor`, which
returns 0 for regions that have not been generated.

Delivery is two Harmony patches:

- `TraderDialogueInjectPatch` adds the dispatch branch to each trader's dialogue as it loads. It runs on
  both client and server so that the dialogue option IDs match on both sides; the client sends only the
  chosen option's ID, so a mismatch breaks every menu choice, including vanilla trading.
- `TraderDialogueTriggerPatch` handles the purchase on the server: it charges the gears, files the
  journal entry and records the purchase in a player variable.

Journal entries are keyed by the trader's entity ID, since traders wander, and titled with the purchase
location relative to world spawn.

The dialogue patch only adds its own options and handles its own `pddispatch-*` triggers, so it works
alongside other trader mods.

## Interesting Ore Gen

[Interesting Ore Gen](https://mods.vintagestory.at/interestingoregen) turns off the vanilla ore-map
deposits, so the vanilla dispatches would point at ore that was never placed.

- **IOG 2.x:** traders sell "Rich mining grounds" dispatches instead, pointing at IOG's hydrothermal
  districts. District positions are worked out from the world seed by repeating IOG 2.3.8's own roll
  (`IogDistrictSampler`). Each trader knows only some of the districts in range. Traders also sell the
  Prospector's Primer, a one-time list of which rocks host which scattered ores.
- **IOG 3.0 and later:** IOG 3 no longer has districts, so traders offer no dispatches and the server
  log explains why. Support for IOG 3's deposits is planned.

## Project layout

| File | Purpose |
|---|---|
| `ProspectorsDispatchModSystem.cs` | Entry point: config, Harmony patches, sampler setup, IOG detection. |
| `TraderDialogueInjectPatch.cs` | Adds the dispatch branch to trader dialogue. |
| `TraderDialogueTriggerPatch.cs` | Handles purchases: charging, journal filing, purchase records. |
| `TraderDialogue.cs` | Trigger codes, variable names and labels shared by the two patches. |
| `OreMapSampler.cs` | Reads the vanilla ore maps; also collects host-rock data for the primer. |
| `IogDistrictSampler.cs` | Finds IOG 2.x districts; detects which IOG mode applies. |
| `DispatchReport.cs` | Renders dispatches and the primer as journal text. |
| `DispatchJournal.cs` | Files reports into the journal. |
| `ResourceCatalog.cs` | Curated category and rarity per resource. |
| `ProspectorsDispatchConfig.cs`, `DispatchPriceTable.cs` | Server settings and prices. |
| `CompassMath.cs` | Floor division and compass bearing helpers. |
| `assets/prospectorsdispatch/lang/en.json` | Chat messages. |

The remaining files are small data types: `ResourceCategory`, `DispatchTier`, `Rarity`, `ResourceEntry`,
`VeinSize`, `OreReading`, `DistrictReading` and `IogSupport`.

## Configuration

Settings are in `VintagestoryData/ModConfig/ProspectorsDispatch.json`, created with defaults on first
run. New settings are added to an existing file automatically. The file is read on the server, so on a
multiplayer server the host's settings apply. Restart after editing.

| Setting | Default | Effect |
|---|---|---|
| `SearchRadius` | 5000 | How far, in blocks, a trader knows of resources. |
| `DistrictSearchRadius` | 12000 | How far a trader knows of IOG 2.x districts. |
| `DistrictKnowledgeChance` | 0.6 | Chance a trader knows each district beyond the nearest. 1 means all. |
| `MaxResourcesPerDispatch` | 5 | Most entries listed per dispatch. |
| `OfferRumorTier`, `OfferSurveyTier` | true | Turn either tier off. |
| `Prices` | see below | Gear price per category and tier. 0 makes it free. |

Default prices in gears:

| Category | Rumor | Survey |
|---|---|---|
| Minerals | 1 | 4 |
| Ores | 1 | 6 |
| Gems | 2 | 10 |
| Mining grounds (IOG 2.x) | 2 | 8 |

The primer (IOG 2.x) costs 1 gear.

## Building and testing

Requires the .NET 10 SDK and a `VINTAGE_STORY` environment variable pointing at the game install.

`run.ps1` builds the mod and starts the game with it loaded:

```powershell
.\run.ps1                 # build, then open the main menu
.\run.ps1 -NoBuild        # start without building
.\run.ps1 -World pdtest   # also open the named world, creating it if needed
```

Close the game before rebuilding, because it locks the mod DLL. To do the same by hand:

```powershell
dotnet build ProspectorsDispatch/ProspectorsDispatch.csproj -c Debug

# --addModPath is resolved against the game's install folder, so it must be an absolute path.
& "$env:VINTAGE_STORY\Vintagestory.exe" --addModPath "<repo>\ProspectorsDispatch\bin\Debug\Mods"
```

Test in a world with normal terrain; superflat worlds have no ore maps. Find or spawn a trader, buy a
dispatch, and read it in the journal.

`dotnet run --project CakeBuild/CakeBuild.csproj` builds the release zip in `Releases/`.
