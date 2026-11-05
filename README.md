# Carnage Gym

A local trainer for Lincoln in [Electoral Carnage](https://store.steampowered.com/app/3706810/Electoral_Carnage/). The Steam game stays the simulator. A BepInEx plugin reads on-screen state and presses buttons. Python PPO learns against the built-in CPU.

Online matches are untouched. The plugin does not patch network input.

## What you do in the game

1. Launch Electoral Carnage with the plugin installed.
2. Open local training.
3. Pick Lincoln versus the Lincoln CPU.
4. Leave that match on screen, then start the trainer.

The plugin will not click through menus. Both fighters must share a character id, and exactly one side must be the CPU.

## Install the plugin

Game folder on this machine:

`C:\Program Files (x86)\Steam\steamapps\common\Electoral Carnage`

BepInEx 5.4.23.2 x64 unpacks to `third_party/BepInEx_win_x64_5.4.23.2/`. Copy these into the game folder:

- `winhttp.dll`
- `doorstop_config.ini`
- the `BepInEx` directory

Launch the game once so BepInEx creates `BepInEx/plugins`. Build the plugin and copy the DLL:

```powershell
dotnet build plugin/CarnageGym/CarnageGym.csproj -c Release
Copy-Item plugin/CarnageGym/bin/Release/CarnageGym.dll "C:\Program Files (x86)\Steam\steamapps\common\Electoral Carnage\BepInEx\plugins\CarnageGym.dll"
```

Restart the game. The BepInEx log should contain `CarnageGym listening on 127.0.0.1:47831`.

To uninstall, delete `winhttp.dll`, `doorstop_config.ini`, and the `BepInEx` folder from the game directory, then restart Steam's copy of the game.

## Train

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
.\.venv\Scripts\python -m trainer.train --timesteps 2000000
```

Checkpoints land in `checkpoints/lincoln_ppo.zip`. Progress rows land in `logs/progress.csv`.

The first milestone is a greedy eval of 50 rounds at the easiest CPU level and time scale 1. Pass is a win rate of at least 0.70. Record that rate in `logs/milestone.txt` when you run it.

## Smoke test

With Lincoln versus Lincoln CPU training already open:

1. A `ping` on port 47831 returns `"ok": true`.
2. `reset` returns 29 finite floats and both health values at least 0.99.
3. Ten random-action rounds each finish with a winner of `agent`, `cpu`, `draw`, or `timeout`.

## Layout

- `trainer/` Python env, reward, and PPO entry point
- `plugin/CarnageGym/` BepInEx plugin
- `docs/superpowers/specs/2026-11-04-carnage-gym-design.md` the contract

The `.cursor` directory at the repo root is empty on purpose. Put project rules, commands, or snippets there yourself. Cursor reads rules from `.cursor/rules` once you add them.
