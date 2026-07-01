# Changelog

## 1.0.1

- Fixed: on multiplayer servers, talking to a trader could break the trader's
  menu so players were unable to buy or sell anything. The dispatch dialogue is
  now injected on both the client and the server, keeping the conversation's
  answer options in sync so every menu choice (including the vanilla "I would
  like to trade" option) works correctly. Single-player was unaffected.

## 1.0.0

- Initial release. Buy resource-location dispatches from traders; readings are
  recorded in your journal.
