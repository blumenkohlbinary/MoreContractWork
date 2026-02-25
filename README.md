# MoreContractWork

A BepInEx plugin for **Mad Games Tycoon 2** that allows multiple contract work offers to spawn per week, fully configurable via the in-game ConfigurationManager.

## Features

- Spawn multiple contract work offers per in-game week (configurable)
- Raise the maximum number of active offers on the board
- Adjust the spawn threshold, offer lifetime, reward multiplier, and penalty multiplier
- Covers all studio types:
  - Development
  - Quality Assurance
  - Graphics Studios
  - Sound Studios
  - Productions
  - Workshop
  - Console Development / Motion Capture

## Requirements

| Dependency | Version |
|---|---|
| Mad Games Tycoon 2 | tested on current Steam build |
| BepInEx | 5.4.x (Mono) |
| BepInEx.ConfigurationManager | any recent version (optional, for in-game UI) |

## Installation

1. Install [BepInEx 5.x (Mono)](https://github.com/BepInEx/BepInEx/releases) into your Mad Games Tycoon 2 game folder.
2. Download `MoreContractWork.dll` from the [Releases](../../releases) page.
3. Place the DLL into `BepInEx/plugins/MoreContractWork/`.
4. Launch the game. Settings appear in the ConfigurationManager (`F1` by default).

## Configuration

All options are available in `BepInEx/config/tobi.Mad_Games_Tycoon_2.plugins.MoreContractWork.cfg` or via the in-game ConfigurationManager.

| Key | Default | Description |
|---|---|---|
| `Max Active Contracts` | 40 | Maximum number of contract offers on the board at once |
| `Contracts Per Week` | 5 | How many new contracts can spawn per in-game week |
| `Spawn Threshold` | 40 | Reputation score required before contracts begin spawning |
| `Offer Lifetime (Weeks)` | 32 | How many weeks an unaccepted offer remains on the board |
| `Reward Multiplier` | 1.0 | Multiplier applied to contract payment |
| `Penalty Multiplier` | 1.0 | Multiplier applied to contract penalty for failure |

## Building from Source

### Prerequisites

- .NET SDK (any version supporting `net46` target)
- The following DLLs copied into the `lib/` folder:
  - `Assembly-CSharp.dll` — from `Mad Games Tycoon 2_Data/Managed/`
  - `netstandard.dll` — from `Mad Games Tycoon 2_Data/Managed/`

```
lib/
  Assembly-CSharp.dll
  netstandard.dll
```

### Build

```bash
dotnet build -c Release
```

The compiled DLL is automatically copied to `BepInEx/plugins/MoreContractWork/` after a successful build.

## How It Works

Vanilla `contractWorkMain.UpdateContractWork()` is called once per in-game week and creates **at most one** new contract offer per call. This plugin adds a Postfix patch that spawns up to `ContractsPerWeek - 1` additional contracts after the vanilla call, replicating the exact initialization logic from the original method (including research unlock checks for each studio type).

## License

This project is released under the [MIT License](LICENSE).
