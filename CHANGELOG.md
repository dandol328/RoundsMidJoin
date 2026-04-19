# Changelog

All notable changes to **RoundsMidJoin** will be documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.1.0] — 2026-04-19

### Added
- **Leave handling** — when a player disconnects mid-game the session continues
  instead of ending immediately.
- **Disconnect tracking** — each departing Photon actor is registered in
  `MidJoinManager` before any vanilla game logic runs, so win-condition checks
  and death handlers can distinguish a disconnect from a normal in-game death.
- **`GM_ArmsRace.PlayerDied` patch** — disconnected players are removed from
  `PlayerManager.instance.players` at the next safe point rather than triggering
  an erroneous game-over for the opposing team.
- **`NetworkConnectionHandler` patches** — intercepts `OnPlayerLeftRoom`,
  `OnPlayerEnteredRoom`, and `OnMasterClientSwitched` to manage state and log
  host migration.
- **`CardChoice.StartPicking` patch** — when a disconnected player's card-pick
  turn arrives the master client auto-picks the first available card on their
  behalf so the selection phase never freezes.
- **`GameModeHandler` patches** — resets all tracking at the start of a new match
  and fires the mid-game join hook at the beginning of each round.
- **Mid-game join framework** — queues players who join while a game is in
  progress and attempts to reactivate their existing Unity `Player` object at the
  next round start.
- **RoundsWithFriends soft dependency** — declared as a soft dependency so the
  mod loads whether or not RoundsWithFriends is installed.
