# RoundsMidJoin

A [BepInEx](https://thunderstore.io/c/rounds/p/BepInEx/BepInExPack_ROUNDS/) mod
for the game **Rounds** that allows players to leave and join while a match is
in progress without locking the remaining players out.

## Features

| Scenario | Vanilla behaviour | With RoundsMidJoin |
|---|---|---|
| Player disconnects during a round | Game session ends immediately | Round continues; disconnected player is removed gracefully |
| Disconnected player's card-pick turn arrives | Card-selection freezes indefinitely | Master client auto-picks the first card on their behalf |
| Master client disconnects | Host migration may break the session | Photon host migration remains available |
| Player joins mid-game | Not supported | Player is network-spawned and activated at the next round |

## Requirements

- [BepInExPack for ROUNDS](https://thunderstore.io/c/rounds/p/BepInEx/BepInExPack_ROUNDS/)
- [UnboundLib](https://thunderstore.io/c/rounds/p/willis81808/UnboundLib/)

## Optional compatibility

- [RoundsWithFriends](https://thunderstore.io/c/rounds/p/olavim/RoundsWithFriends/) —
  declared as a soft dependency.  Both mods can be installed together.

## Installation (r2modman / Thunderstore)

1. Open **r2modman** and select the Rounds profile.
2. Click **Online** → search for **RoundsMidJoin** → **Install**.
3. Launch the game through r2modman.

## Manual installation

1. Download the latest release zip.
2. Extract `RoundsMidJoin.dll` into  
   `<Rounds install>\BepInEx\plugins\RoundsMidJoin\`
3. Launch the game.

## Building from source

You need the game installed.  Set the `ROUNDS_PATH` environment variable to your
Rounds installation directory (defaults to the default Steam path on Windows):

```shell
# Windows (PowerShell)
$env:ROUNDS_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Rounds"
dotnet build -c Release

# Linux / macOS
ROUNDS_PATH="$HOME/.steam/steam/steamapps/common/Rounds" dotnet build -c Release
```

The compiled DLL will appear in `bin/Release/net472/RoundsMidJoin.dll`.

## How it works

### Leave handling

When `NetworkConnectionHandler.OnPlayerLeftRoom` fires, the mod registers the
departing Photon actor number in `MidJoinManager` **before** any vanilla game
logic runs.  The player is removed from `PlayerManager.instance.players` after
the Photon callback returns, so alive/team checks never retain a disconnected
participant.

### Card-selection

`CardChoice.DoPlayerSelect` is prefix-patched: if its picker has disconnected,
the master client selects the first available card and broadcasts the result so
the selection phase cannot wait forever.

### Mid-game joins

Players who enter the Photon room while a match is already in progress are queued
in `MidJoinManager`.  The joining client uses the game's own `PlayerAssigner`
network-spawn path, while all clients hold the new player out of the active round
until `GM_ArmsRace.RoundTransition` activates them.  The host keeps the Photon
room open and visible for Steam/Photon discovery and invites.

## Limitations

- New players begin with no catch-up cards; they participate from the next round.
- Score totals are not recalculated when a player leaves.

## License

MIT — see [LICENSE](LICENSE) for details.
