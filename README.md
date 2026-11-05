# Carnage Gym

A local trainer for Electoral Carnage. The Steam game stays the simulator. A BepInEx plugin reads on-screen state and presses buttons. Python PPO learns in a local match whose opponent is Roosevelt.

Online matches are untouched. The plugin does not patch network input, and it does not click through menus.

## What you do in the game

1. Launch Electoral Carnage with the plugin installed.
2. Set up a local match yourself, with Roosevelt as the CPU.
3. Get the first round actually running.
4. Start the trainer. Between rounds it presses Rematch.

## Install the plugin

Game folder on this machine:

`C:\Program Files (x86)\Steam\steamapps\common\Electoral Carnage`

BepInEx 5.4.23.2 cannot boot this Unity 6 game through `winhttp.dll`. That proxy crashes in `WinHttpWriteProxySettings` before the game window appears. Use Doorstop 4.6.0's `x64/winhttp.dll` renamed to `dxgi.dll` in the game folder, and do not leave a `winhttp.dll` or `version.dll` proxy next to the exe.

Copy into the game folder:

- `dxgi.dll` (Doorstop 4.6.0, the file shipped as `x64/winhttp.dll`)
- `doorstop_config.ini` from BepInEx, with `target_assembly=BepInEx\core\BepInEx.Preloader.dll`
- the `BepInEx` directory

Get the first local match against Roosevelt running, then:

```powershell
python -m trainer.train --timesteps 2000000
```

```powershell
dotnet build plugin/CarnageGym/CarnageGym.csproj -c Release
Copy-Item plugin/CarnageGym/bin/Release/CarnageGym.dll "C:\Program Files (x86)\Steam\steamapps\common\Electoral Carnage\BepInEx\plugins\CarnageGym.dll"
```

Restart the game. The BepInEx log should contain `CarnageGym listening on 127.0.0.1:47831`.

To uninstall, delete `dxgi.dll`, `doorstop_config.ini`, and the `BepInEx` folder from the game directory. If a `winhttp.dll` or `version.dll` proxy is still next to the exe, delete that too. Then restart the game from Steam.

## Train

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
.\.venv\Scripts\python -m trainer.train --timesteps 2000000
```

Checkpoints land in `checkpoints/lincoln_ppo.zip`. Progress rows land in `logs/progress.csv`.

The first milestone is a greedy eval of 50 rounds at the easiest CPU level and time scale 1. Pass is a win rate of at least 0.70. Record that rate in `logs/milestone.txt` when you run it.

## Smoke test

With the local match against Roosevelt already running:

1. A `ping` on port 47831 returns `"ok": true`.
2. `reset` returns 29 finite floats and both health values at least 0.99.
3. Ten random-action rounds each finish with a winner of `agent`, `cpu`, `draw`, or `timeout`.

## Layout

- `trainer/` Python env, reward, and PPO entry point
- `plugin/CarnageGym/` BepInEx plugin
- `docs/superpowers/specs/2026-11-04-carnage-gym-design.md` the contract

The `.cursor` directory at the repo root is empty on purpose. Put project rules, commands, or snippets there yourself. Cursor reads rules from `.cursor/rules` once you add them.
