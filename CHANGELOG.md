# Changelog

## 1.1.0

- Dispatches now cover any ore that uses the game's ore-distribution maps, including
  ores added by other worldgen mods (e.g. Geology Additions) — no longer limited to a
  fixed list. Categories (Ores/Gems/Minerals) are worked out from each ore's worldgen
  data automatically.
- Ores that PD doesn't have a curated rarity for now show an honest "traces detected"
  note instead of a placeholder, while still giving the direction, distance, and vein size.

## 1.0.1

- Fixed: on multiplayer servers, talking to a trader could break the trader's
  menu so players were unable to buy or sell anything. The dispatch dialogue is
  now injected on both the client and the server, keeping the conversation's
  answer options in sync so every menu choice (including the vanilla "I would
  like to trade" option) works correctly. Single-player was unaffected.

## 1.0.0

- Initial release. Buy resource-location dispatches from traders; readings are
  recorded in your journal.
