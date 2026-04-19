# RoundsMidJoin

A [BepInEx](https://thunderstore.io/c/rounds/p/BepInEx/BepInExPack_ROUNDS/) mod
for the game **Rounds** that allows players to leave and rejoin mid-game without
ending the current session.

## Features

| Scenario | Vanilla behaviour | With RoundsMidJoin |
|---|---|---|
| Player disconnects during a round | Game session ends immediately | Round continues; disconnected player is removed gracefully |
| Disconnected player's card-pick turn arrives | Card-selection freezes indefinitely | Master client auto-picks the first card on their behalf |
| Master client disconnects | Host migration may break the session | Master migration is logged; new host takes over server duties |
| Player joins mid-game | Not supported | Player is queued and reactivated / spawned at the next round start |

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

### Distrobox (Linux — containerised build)

[Distrobox](https://github.com/89luca89/distrobox) lets you build the mod inside
a container without configuring .NET or Mono on your host system.  Your home
directory is shared automatically, so the built DLL is immediately available on
the host.

**1. Create and enter a container**

```sh
distrobox create --name rounds-build --image fedora:latest
distrobox enter rounds-build
```

**2. Install the .NET SDK and Mono inside the container**

The project targets `net472` (.NET Framework 4.7.2), so both the .NET SDK and
Mono (which provides the Framework reference assemblies) are required.

```sh
# Fedora / RHEL
sudo dnf install -y dotnet-sdk-8.0 mono-devel

# Debian / Ubuntu
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0 mono-devel
```

**3. Set `ROUNDS_PATH` to the game directory**

Distrobox mounts your host home directory inside the container, so the default
Steam path works without any extra bind-mounts:

```sh
export ROUNDS_PATH="$HOME/.steam/steam/steamapps/common/Rounds"
```

If the game is installed on a different drive or path, export the correct value
before building.

**4. Build**

From the repository root inside the container:

```sh
dotnet build -c Release
```

**5. Retrieve the built DLL**

The compiled plugin is written to:

```
bin/Release/net472/RoundsMidJoin.dll
```

Because Distrobox shares your home directory, this path is identical on the
host — no manual copy step is needed.

## How it works

### Leave handling

When `NetworkConnectionHandler.OnPlayerLeftRoom` fires, the mod registers the
departing Photon actor number in `MidJoinManager` **before** any vanilla game
logic runs.  The patched `GM_ArmsRace.PlayerDied` then detects that this player's
"death" was caused by a disconnect and removes them from
`PlayerManager.instance.players` at a safe point (after the current frame's
iteration) instead of letting the game declare an erroneous winner.

### Card-selection

`CardChoice.StartPicking` is prefix-patched: if it's a disconnected player's
turn, the master client immediately calls `CardChoice.Pick(0, sendRPC: true)` so
the round can advance without waiting for input that will never arrive.

### Mid-game joins

Players who enter the Photon room while a match is already in progress are queued
in `MidJoinManager`.  At the start of the next round the mod attempts to
reactivate any existing Unity `Player` object for that actor, or logs that a full
spawn is required (full spawn support is on the roadmap).

## Limitations / Roadmap

- **Full mid-game spawn** — bringing a brand-new player into an ongoing match
  requires spawning a character prefab over the network; this is game-mode-
  specific and planned for a future release.
- **Score adjustment** — round scores are not currently recalculated when a player
  leaves.  The remaining players continue with the current point totals.

## License

MIT — see [LICENSE](LICENSE) for details.
