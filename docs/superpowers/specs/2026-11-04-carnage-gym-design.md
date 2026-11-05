# Carnage Gym Design

Train a small PPO policy to play Lincoln in the installed Steam build of Electoral Carnage. The game stays the simulator. A BepInEx plugin is the eyes and hands. A Python process is the brain. Training is local only.

## Goal

Lincoln beats the built-in Lincoln CPU in local training mode, starting at the easiest difficulty that the game will accept and climbing while it keeps winning. The policy sees only state a player can see, and it presses the same buttons a player presses.

The first milestone is plumbing plus a real result: a greedy policy wins at least 70% of 50 rounds at the easiest CPU level. Climbing the remaining levels is the same loop, not a second project.

## Out of scope

- Online matches, rollback input, and the network input path.
- Any character other than Lincoln versus Lincoln.
- Self-play, and a mode whose job is fighting a local human. The local input path must not block those later.
- Menu automation. The person training opens local training, selects Lincoln versus the Lincoln CPU, and leaves that match up.
- Rebuilding the game, copying its assets, or committing decompiled game code.
- Reading the CPU's planned commands, the opponent's raw input packet, or numeric lag and frame data.

## Architecture

Two processes on one machine.

1. Electoral Carnage, Mono build, with BepInEx 5.4.23.2 (x64) and the `CarnageGym` plugin.
2. A Python 3.10 trainer using Gymnasium and Stable-Baselines3 PPO.

They talk newline-delimited JSON over TCP `127.0.0.1:47831`. The plugin listens. The trainer connects. One client at a time.

On reset, the plugin asks the game's own training-reset object to restart the round, then returns the first observation. On step, the trainer sends one discrete action. The plugin holds that button state for 3 rendered frames and replies with the new observation, a reward, and whether the round ended.

`Time.timeScale` is set to the trainer's requested scale, clamped to `[1, 8]`. Training requests 3. Scoring runs request 1. If the game ignores time scale, decisions are still counted in game frames, so learning stays valid at 1x. The log records the scale actually set.

The policy is a 2-layer MLP, 128 units each. It runs in Python. This version does not load the network inside the game.

## Match control

The plugin does not click through menus. Reset succeeds only when a match is already loaded and all of the following are true:

- Exactly two `PlayerInfo` objects exist.
- Exactly one of them has `npc == false` (the agent) and one has `npc == true` (the CPU).
- Both `characterID` values are equal. The operator is responsible for that pair being Lincoln. A mismatch returns error `character_mismatch`.
- A `trainingModeReset` behaviour is loaded.

Reset enables that behaviour's GameObject so the game runs its own `OnEnable` / `FixedUpdate` reset, then waits up to 600 rendered frames for both health fractions to be at least 0.99. If that wait expires, the response is `ok: false` with error `reset_timeout`.

CPU difficulty is `PlayerInfo.cpuLevel` on the NPC. The trainer sends an integer. The plugin writes it on reset. Startup probing tries the integers 0 through 10 and keeps each value that reads back unchanged. The minimum and maximum accepted values bound the curriculum. `cpuLevel` is never part of the policy input.

## Observation

Each decision produces 29 floats. Values that would be outside the clamps below are clamped. Missing bools become 0. A non-finite value becomes 0.

Positions come from each fighter's `transform.position`. The scale `S` is `PlayerInfo.movementBound` when that field is a positive float, or its `x` component when it is a vector with non-zero `x`. If no positive scale exists, `S` is 8 and the plugin logs `stage_scale_fallback` once. Vertical scale is the vector's `y` when that component is non-zero, otherwise `S`.

| Index | Value | Clamp |
| --- | --- | --- |
| 0, 1 | Agent x, y divided by stage scale | [-2, 2] |
| 2, 3 | CPU x, y divided by stage scale | [-2, 2] |
| 4, 5 | Agent velocity: change in scaled position since the previous decision | [-2, 2] |
| 6, 7 | CPU velocity, same definition | [-2, 2] |
| 8, 9 | Facing, agent then CPU. Right is 1, left is -1 | -1 or 1 |
| 10, 11 | `health / maxHealth`, agent then CPU. Denominator is at least 1e-6 | [0, 1] |
| 12, 13 | Super fraction, agent then CPU. `superCharge / superCost` when `superCost > 0`, otherwise `superCharge / 100` | [0, 1] |
| 14, 15 | `superReady`, agent then CPU, as 0 or 1 | 0 or 1 |
| 16, 17 | `airborn`, agent then CPU | 0 or 1 |
| 18, 19 | `dashing`, agent then CPU | 0 or 1 |
| 20, 21 | `PlayerInfo.hit`, agent then CPU | 0 or 1 |
| 22, 23 | `PlayerInfo.hitting`, agent then CPU | 0 or 1 |
| 24, 25 | `running`, agent then CPU | 0 or 1 |
| 26, 27 | Relative position, CPU minus agent, in scaled units | [-4, 4] |
| 28 | Euclidean length of the relative position | [0, 6] |

The policy input is four of these vectors concatenated, oldest first, 116 floats. On reset, the first vector is repeated four times so the stack is full immediately. Velocity on the first decision is 0.

Allowlist of game fields the observation may read: transforms' positions, `facing`, `health`, `maxHealth`, `superCharge`, `superCost`, `superReady`, `airborn`, `dashing`, `running`, `hit`, `hitting`, and `movementBound` used only as a scale. The NPC's `cpuLevel` may be read into the step info for the curriculum. These fields are forbidden in the observation and in the policy input: `groundCPUCommands`, `airCPUCommands`, anything on `cpuCommand` or `cpuOption`, either fighter's `InputPacket` / `inputs` / `holding`, `lag`, clash frame counters, and rollback state.

## Actions

54 discrete actions. `action = direction_index * 6 + button_index`.

Directions, in order, as `(move_x, move_y)`: neutral `(0, 0)`, left `(-1, 0)`, right `(1, 0)`, up `(0, 1)`, down `(0, -1)`, up-left `(-1, 1)`, up-right `(1, 1)`, down-left `(-1, -1)`, down-right `(1, -1)`.

Buttons, in order: none, attack, special, jump, jump+attack, super.

The plugin writes the agent's local `InputPacket` fields `moveVectorx`, `moveVectory`, `attack`, `special`, `jump`, and `super`. `start` and `tapJump` stay false. Jump+attack sets both `jump` and `attack`. The stick is applied together with the button. The write goes through the local player only. `InputGod` network receivers, `RBPInputSync`, and `ReceiveInputs` are not patched.

The held packet is rewritten at the end of every `Update` on the plugin, and again from a Harmony postfix on `BasicCharacterScript.GetInput` for the agent instance, so the game cannot replace it later in the same frame. After 3 `Update` calls the plugin stops forcing and lets the fields clear. A step waits for those 3 frames before it replies.

## Reward

Health and super fractions use the same definitions as the observation. Deltas are previous decision minus current for health (positive when that fighter lost health), and current minus previous for super fraction.

```
reward = damage_dealt - damage_taken
       + 0.15 * agent_meter_gain
       - 0.15 * cpu_meter_gain
       - 0.002
```

`damage_dealt` is the drop in the CPU health fraction. `damage_taken` is the drop in the agent health fraction. Meter gains are the increases in super fraction. The first decision of a round has deltas of 0.

Terminal addition, applied once when the round ends:

- Agent health fraction is 0 or below, CPU is above 0: `-1`, winner `cpu`.
- CPU health fraction is 0 or below, agent is above 0: `+1`, winner `agent`.
- Both are 0 or below: `0`, winner `draw`.
- 900 decisions elapse first: `agent_health_fraction - cpu_health_fraction`, winner `timeout`.

`PlayerInfo.zeroHealth == true` counts as that fighter's health fraction being 0 for the winner rule. The step penalty is included on the terminal step too.

The plugin always sends `"reward": 0`. `trainer/reward.py` is the only implementation of the formula. It takes the previous observation, the current observation, `done`, and `winner`. The plugin sets `winner` from the rules above. Python tests call the same function the environment uses.

## Curriculum

Rounds used for learning update a window of 30 finished rounds. Timeouts and draws are not wins. When the window fills:

- Win rate at least 0.65, and the level is below the probed maximum: level increases by 1.
- Win rate at most 0.25, and the level is above the probed minimum: level decreases by 1.

The window clears after an adjustment or after a full window that changes nothing. Greedy scoring runs do not enter the window and do not change the level.

## Trainer

One game, one environment. There is no second process sharing the match.

PPO hyperparameters:

- Policy: `MlpPolicy`, `net_arch` `[128, 128]`
- `learning_rate` `3e-4`
- `n_steps` `2048`
- `batch_size` `256`
- `n_epochs` `10`
- `gamma` `0.995`
- `gae_lambda` `0.95`
- `clip_range` `0.2`
- `ent_coef` `0.01`
- `vf_coef` `0.5`
- `max_grad_norm` `0.5`
- `seed` `0`
- Device: CUDA when PyTorch can see it, otherwise CPU
- First serious run: `2_000_000` timesteps

Every 10,000 timesteps the trainer pauses learning, runs 10 deterministic rounds at time scale 1, appends the win rate, then resumes at the training scale. Checkpoints save every 20,480 timesteps to `checkpoints/lincoln_ppo`. Progress rows append to `logs/progress.csv` with columns `timestep`, `cpu_level`, `win_rate_30`, `eval_win_rate`, `ep_reward_mean`, `time_scale`.

## Protocol

Requests, one JSON object per line:

```json
{"cmd":"ping"}
{"cmd":"reset","cpu_level":0,"time_scale":3}
{"cmd":"step","action":0}
```

`action` is an integer from 0 through 53. `cpu_level` is an integer. `time_scale` is a number.

Responses:

```json
{"ok":true,"obs":[0.0],"reward":0.0,"done":false,"info":{"cpu_level":0,"time_scale":3.0,"self_health":1.0,"opp_health":1.0,"winner":null,"game_seconds":0.0}}
{"ok":false,"error":"need_one_cpu","obs":[0.0],"reward":0.0,"done":true,"info":{}}
```

`obs` on success has length 29. Examples above write `[0.0]` as a stand-in for that full array. On failure the array is 29 zeros. `winner` is `null` until the round ends, then `agent`, `cpu`, `draw`, or `timeout`. `game_seconds` is decisions times `3 / 60`. Successful resets also include `cpu_min` and `cpu_max`, the probed difficulty bounds.

Error strings:

| Error | Meaning |
| --- | --- |
| `need_one_cpu` | Not exactly one NPC and one local player |
| `character_mismatch` | The two fighters have different `characterID` values |
| `training_reset_missing` | No `trainingModeReset` behaviour is loaded |
| `reset_timeout` | Health did not recover within 600 frames |
| `bad_action` | Action outside 0..53 |
| `players_not_ready` | A step arrived with no live pair of fighters |

The trainer retries a failed reset up to 3 times. After that it raises and stops. A dropped connection retries for 30 seconds, then raises `GameConnectionError` with the text `Launch Electoral Carnage with CarnageGym and open Lincoln versus Lincoln CPU training.`

## Plugin shape

BepInEx plugin id `local.carnagegym`, name `CarnageGym`, version `0.1.0`. Target `net472`, BepInEx 5.4.23.2. Game types are accessed with Harmony `AccessTools` by name, not by a compile-time reference to `Assembly-CSharp.dll`.

| Type | Responsibility |
| --- | --- |
| `Plugin` | Harmony setup, starts the socket, owns the frame counter |
| `ActionCodec` | Pure function from action index to button fields. No Unity types |
| `ObservationBuilder` | Builds the 29-float allowlisted vector |
| `InputForcer` | Holds the agent's packet for 3 `Update` frames |
| `MatchDriver` | Finds the pair, sets CPU level, fires training reset, waits for full health |
| `IpcServer` | Binds `127.0.0.1:47831`, one client, one request at a time |

If a required field is missing at startup, the plugin logs the type and field name and does not open the socket.

## Error handling

- The observation builder replaces each non-finite component with 0. If a decision required any replacement, it counts. Five such decisions in a row end the round as `draw` and the next trainer step resets.
- `time_scale` outside `[1, 8]` is clamped, and `info.time_scale` reports the clamped value.
- The socket ignores a second client while one is connected.
- A request that is not a single JSON object gets `ok: false` and error `bad_json`, and the round is left running.
- If both fighters' scaled positions stay unchanged and both health fractions stay unchanged for 120 consecutive decisions, the trainer logs `possible_stall` and uses time scale 1 on the next reset. It does this at most once per training process.

## Testing

Python tests cover the action table, the reward numbers, the stack length, and a fake TCP server that speaks the protocol. A Gymnasium env pointed at that fake server must finish a scripted win, a scripted loss, and a timeout.

The C# `ActionCodec` is tested without Unity. Action 0 is neutral with every button false. Action 22 is up plus jump+attack (`3 * 6 + 4`). Action 53 is down-right plus super (`8 * 6 + 5`). A fixture generated by the Python table checks every index 0 through 53.

Game smoke, run by hand once the plugin builds:

1. Launch the game with BepInEx, open Lincoln versus Lincoln CPU training.
2. `ping` returns `ok: true`.
3. `reset` returns 29 finite floats and both health info values at least 0.99.
4. Ten random-action rounds each return `done: true` with a winner string.

The milestone eval is 50 deterministic rounds at the probed minimum CPU level and time scale 1. Pass is a win rate of at least 0.70.

## Repository

```
plugin/CarnageGym/          BepInEx plugin
plugin/CarnageGym.Tests/    ActionCodec tests, no Unity
trainer/                    env, reward, PPO entry point
tests/                      Python tests
checkpoints/                created at train time, gitignored
logs/                       created at train time, gitignored
```

Dependencies, pinned in `requirements.txt`: `gymnasium>=1.0,<2`, `stable-baselines3>=2.3,<3`, and `torch>=2.2`. The install uses a CUDA wheel when one exists for this Python, and CPU torch otherwise. BepInEx is downloaded by the install steps into a `third_party/` directory that is gitignored, then copied into the game folder by a documented command. The repo does not vendor the game.
