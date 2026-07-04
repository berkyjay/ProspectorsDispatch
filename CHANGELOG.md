# Changelog

## 1.2.0

- Interesting Ore Gen compatibility: with IOG installed, traders now sell directions to
  IOG's hydrothermal mining districts ("Rich mining grounds") instead of the vanilla
  ore-map dispatches, which IOG's worldgen makes meaningless. District dispatches name
  the grounds' kind, heading, distance (Survey tier) and the ores they carry.
- New: the Prospector's Primer (IOG worlds) - a cheap one-time purchase recording which
  rock types host which scattered ores, including where the top "bountiful" grade forms.
- Traders now keep books: a dispatch you've already bought disappears from that trader's
  menu (buying a Survey also retires the lesser Rumour), and a stale purchase attempt is
  politely refused instead of charged. A Rumour can no longer overwrite a better Survey
  in your journal.
- Each trader has heard of their own subset of the mining districts in range (nearest
  always known), so visiting multiple traders is worth it. Configurable via
  DistrictKnowledgeChance.
- District dispatches list the nearest grounds of each kind, so a far-but-unique
  district type is never crowded out by closer ones.
- Journal entry titles now lead with the trader's coordinates so entries are
  distinguishable in the journal list.
- Text cleanup: clearer district ore names (no more duplicate "Quartz" entries) and
  punctuation fixes throughout.

## 1.1.0

- Dispatches now cover any ore that uses the game's ore-distribution maps, including
  ores added by other worldgen mods (e.g. Geology Additions) — no longer limited to a
  fixed list. Categories (Ores/Gems/Minerals) are worked out from each ore's worldgen
  data automatically.
- Ores that PD doesn't have a curated rarity for now show an honest "traces detected"
  note instead of a placeholder, while still giving the direction, distance, and vein size.
- Fixed: in single-player, buying a dispatch charged the player twice. (Introduced in
  1.0.1, which applied its trader fix once per side in the shared single-player process.)
- Fixed: long dispatch entries no longer run underneath the journal's page buttons — the
  text now pages cleanly.

## 1.0.1

- Fixed: on multiplayer servers, talking to a trader could break the trader's
  menu so players were unable to buy or sell anything. The dispatch dialogue is
  now injected on both the client and the server, keeping the conversation's
  answer options in sync so every menu choice (including the vanilla "I would
  like to trade" option) works correctly. Single-player was unaffected.

## 1.0.0

- Initial release. Buy resource-location dispatches from traders; readings are
  recorded in your journal.
