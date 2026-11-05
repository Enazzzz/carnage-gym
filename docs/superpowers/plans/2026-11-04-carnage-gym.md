# Carnage Gym Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Train a small PPO policy to play Lincoln against the built-in Lincoln CPU in local Electoral Carnage training, from on-screen state only.

**Architecture:** A BepInEx 5 plugin exposes observations and local buttons over `127.0.0.1:47831`. Python owns the action table, the reward, the frame stack, and PPO. The game stays the simulator.

**Tech Stack:** Python 3.10, Gymnasium, Stable-Baselines3, PyTorch, C# `net472` BepInEx 5.4.23.2 plugin, `net6.0` unit tests for the pure action codec.

## Global Constraints

- Local training only. Do not patch `InputGod` network receivers, `RBPInputSync`, `ReceiveInputs`, or online play.
- Observation length is 29. Policy input is 116 floats: four observations, oldest first, first observation repeated on reset.
- Actions are integers 0 through 53: `action = direction_index * 6 + button_index`.
- Directions in order: `(0,0), (-1,0), (1,0), (0,1), (0,-1), (-1,1), (1,1), (-1,-1), (1,-1)`.
- Buttons in order: none, attack, special, jump, jump+attack, super.
- `start` and `tapJump` stay false.
- The plugin always sends `"reward": 0`. `trainer/reward.py` is the only reward implementation.
- Forbidden observation fields: `groundCPUCommands`, `airCPUCommands`, `cpuCommand`, `cpuOption`, `inputs`, `holding`, `InputPacket` contents, `lag`, clash frame counters, rollback state.
- CPU level is info for the curriculum only. It is never part of `obs`.
- Time scale is clamped to `[1, 8]`. Training requests 3. Scoring requests 1.
- A decision is held for 3 `Update` frames. A round ends at 900 decisions.
- Implementation commits go on branch `lincoln-trainer`, not `master`.
- Indent Python and C# with tabs. Every function and class gets a docstring or XML doc comment.

---

### Task 1: Action table

**Files:**
- Create: `trainer/__init__.py`
- Create: `trainer/actions.py`
- Create: `tests/test_actions.py`
- Create: `tests/fixtures/actions.json`

**Interfaces:**
- Consumes: nothing
- Produces: `trainer.actions.N_ACTIONS` (`int`, 54), `trainer.actions.decode_action(action: int) -> Packet`, `Packet.as_dict() -> dict` with keys `move_x`, `move_y`, `attack`, `special`, `jump`, `super`

- [ ] **Step 1: Write the failing test**

```python
import json
from pathlib import Path

import pytest

from trainer.actions import N_ACTIONS, decode_action


FIXTURE = Path(__file__).parent / "fixtures" / "actions.json"


def test_table_matches_fixture():
	rows = json.loads(FIXTURE.read_text(encoding="utf-8"))
	assert len(rows) == 54
	assert N_ACTIONS == 54
	for row in rows:
		packet = decode_action(row["action"]).as_dict()
		assert packet == {
			"move_x": row["move_x"],
			"move_y": row["move_y"],
			"attack": row["attack"],
			"special": row["special"],
			"jump": row["jump"],
			"super": row["super"],
		}


def test_rejects_out_of_range():
	with pytest.raises(ValueError, match="bad_action"):
		decode_action(-1)
	with pytest.raises(ValueError, match="bad_action"):
		decode_action(54)


def test_named_examples():
	neutral = decode_action(0).as_dict()
	assert neutral["move_x"] == 0 and neutral["move_y"] == 0
	assert neutral["attack"] is False and neutral["super"] is False
	up_jump_attack = decode_action(22).as_dict()
	assert (up_jump_attack["move_x"], up_jump_attack["move_y"]) == (0, 1)
	assert up_jump_attack["jump"] is True and up_jump_attack["attack"] is True
	down_right_super = decode_action(53).as_dict()
	assert (down_right_super["move_x"], down_right_super["move_y"]) == (1, -1)
	assert down_right_super["super"] is True
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `python -m pytest tests/test_actions.py -v`

Expected: FAIL because `trainer.actions` does not exist, or because the fixture is missing.

- [ ] **Step 3: Write the fixture and the implementation**

Create `tests/fixtures/actions.json` with 54 objects, action 0 through 53, using the direction and button order in Global Constraints. `jump_attack` sets `attack` and `jump` true. `super` sets `super` true. All other buttons false. `move_x` and `move_y` come from the direction list.

Create `trainer/__init__.py` as an empty module docstring file:

```python
"""Carnage Gym trainer."""
```

Create `trainer/actions.py`:

```python
"""Discrete stick and button table shared with the BepInEx plugin."""

from dataclasses import dataclass


# (move_x, move_y), index order is part of the wire contract.
DIRECTIONS = (
	(0, 0),
	(-1, 0),
	(1, 0),
	(0, 1),
	(0, -1),
	(-1, 1),
	(1, 1),
	(-1, -1),
	(1, -1),
)

# Button index order is part of the wire contract.
BUTTONS = ("none", "attack", "special", "jump", "jump_attack", "super")

N_ACTIONS = len(DIRECTIONS) * len(BUTTONS)


@dataclass(frozen=True)
class Packet:
	"""Buttons held for one decision."""

	move_x: int
	move_y: int
	attack: bool
	special: bool
	jump: bool
	super_button: bool

	def as_dict(self) -> dict:
		"""Return the JSON field names the plugin expects."""
		return {
			"move_x": self.move_x,
			"move_y": self.move_y,
			"attack": self.attack,
			"special": self.special,
			"jump": self.jump,
			"super": self.super_button,
		}


def decode_action(action: int) -> Packet:
	"""Map an action index to a packet.

	Raises:
		ValueError: the index is outside 0..53.
	"""
	if not isinstance(action, int) or isinstance(action, bool) or action < 0 or action >= N_ACTIONS:
		raise ValueError(f"bad_action:{action}")
	direction_index, button_index = divmod(action, len(BUTTONS))
	move_x, move_y = DIRECTIONS[direction_index]
	name = BUTTONS[button_index]
	return Packet(
		move_x=move_x,
		move_y=move_y,
		attack=name in ("attack", "jump_attack"),
		special=name == "special",
		jump=name in ("jump", "jump_attack"),
		super_button=name == "super",
	)
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `python -m pytest tests/test_actions.py -v`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add trainer/__init__.py trainer/actions.py tests/test_actions.py tests/fixtures/actions.json
git commit -m "Add the shared 54-action button table."
```

---

### Task 2: Reward

**Files:**
- Create: `trainer/reward.py`
- Create: `tests/test_reward.py`

**Interfaces:**
- Consumes: observation indexes from the spec (health at 10 and 11, super at 12 and 13)
- Produces: `reward_from_transition(previous, current, done, winner) -> float`

- [ ] **Step 1: Write the failing test**

```python
import pytest

from trainer.reward import reward_from_transition


def _obs(agent_hp=1.0, cpu_hp=1.0, agent_meter=0.0, cpu_meter=0.0):
	values = [0.0] * 29
	values[10] = agent_hp
	values[11] = cpu_hp
	values[12] = agent_meter
	values[13] = cpu_meter
	return values


def test_first_step_is_only_the_time_penalty():
	assert reward_from_transition(None, _obs(), False, None) == pytest.approx(-0.002)


def test_damage_and_meter():
	previous = _obs(agent_hp=1.0, cpu_hp=1.0, agent_meter=0.2, cpu_meter=0.2)
	current = _obs(agent_hp=0.9, cpu_hp=0.7, agent_meter=0.4, cpu_meter=0.3)
	# dealt 0.3, taken 0.1, meter self +0.2, meter cpu +0.1, penalty 0.002
	expected = 0.3 - 0.1 + 0.15 * 0.2 - 0.15 * 0.1 - 0.002
	assert reward_from_transition(previous, current, False, None) == pytest.approx(expected)


def test_terminal_bonuses():
	previous = _obs()
	win = _obs(agent_hp=0.4, cpu_hp=0.0)
	loss = _obs(agent_hp=0.0, cpu_hp=0.5)
	assert reward_from_transition(previous, win, True, "agent") == pytest.approx(
		(1.0 - 0.0) - (1.0 - 0.4) - 0.002 + 1.0
	)
	assert reward_from_transition(previous, loss, True, "cpu") == pytest.approx(
		(1.0 - 0.5) - (1.0 - 0.0) - 0.002 - 1.0
	)
	timeout = _obs(agent_hp=0.25, cpu_hp=0.75)
	assert reward_from_transition(previous, timeout, True, "timeout") == pytest.approx(
		(1.0 - 0.75) - (1.0 - 0.25) - 0.002 + (0.25 - 0.75)
	)
	draw = _obs(agent_hp=0.0, cpu_hp=0.0)
	assert reward_from_transition(previous, draw, True, "draw") == pytest.approx(
		(1.0 - 0.0) - (1.0 - 0.0) - 0.002
	)


def test_done_without_winner_raises():
	with pytest.raises(ValueError, match="winner"):
		reward_from_transition(_obs(), _obs(), True, None)
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `python -m pytest tests/test_reward.py -v`

Expected: FAIL with import error.

- [ ] **Step 3: Implement `trainer/reward.py`**

```python
"""Round reward from two on-screen observations.

The plugin does not compute this. Callers pass the previous observation,
or None on the first decision of a round.
"""

AGENT_HEALTH = 10
CPU_HEALTH = 11
AGENT_SUPER = 12
CPU_SUPER = 13

STEP_PENALTY = 0.002
METER_WEIGHT = 0.15


def reward_from_transition(previous, current, done, winner) -> float:
	"""Return the scalar reward for one decision.

	Raises:
		ValueError: ``done`` is true and ``winner`` is not a known terminal.
	"""
	if previous is None:
		damage_dealt = 0.0
		damage_taken = 0.0
		meter_self = 0.0
		meter_opp = 0.0
	else:
		damage_dealt = float(previous[CPU_HEALTH] - current[CPU_HEALTH])
		damage_taken = float(previous[AGENT_HEALTH] - current[AGENT_HEALTH])
		meter_self = float(current[AGENT_SUPER] - previous[AGENT_SUPER])
		meter_opp = float(current[CPU_SUPER] - previous[CPU_SUPER])
	reward = damage_dealt - damage_taken
	reward += METER_WEIGHT * meter_self
	reward -= METER_WEIGHT * meter_opp
	reward -= STEP_PENALTY
	if not done:
		return reward
	if winner == "agent":
		return reward + 1.0
	if winner == "cpu":
		return reward - 1.0
	if winner == "draw":
		return reward
	if winner == "timeout":
		return reward + float(current[AGENT_HEALTH] - current[CPU_HEALTH])
	raise ValueError(f"winner required when done, got {winner!r}")
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `python -m pytest tests/test_reward.py -v`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add trainer/reward.py tests/test_reward.py
git commit -m "Score rounds from visible health and super meter."
```

---

### Task 3: Observation stack

**Files:**
- Create: `trainer/stack.py`
- Create: `tests/test_stack.py`

**Interfaces:**
- Consumes: observation length 29
- Produces: `ObsStack.reset(obs) -> list[float]` length 116, `ObsStack.push(obs) -> list[float]`

- [ ] **Step 1: Write the failing test**

```python
from trainer.stack import ObsStack


def test_reset_repeats_the_first_frame():
	stack = ObsStack()
	first = [float(i) for i in range(29)]
	vector = stack.reset(first)
	assert len(vector) == 116
	assert vector[:29] == first
	assert vector[-29:] == first


def test_push_drops_the_oldest():
	stack = ObsStack()
	stack.reset([0.0] * 29)
	for step in range(1, 4):
		stack.push([float(step)] * 29)
	vector = stack.push([4.0] * 29)
	assert vector[:29] == [1.0] * 29
	assert vector[-29:] == [4.0] * 29


def test_rejects_wrong_length():
	stack = ObsStack()
	try:
		stack.reset([0.0] * 28)
	except ValueError as exc:
		assert "29" in str(exc)
	else:
		raise AssertionError("expected ValueError")
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `python -m pytest tests/test_stack.py -v`

Expected: FAIL with import error.

- [ ] **Step 3: Implement `trainer/stack.py`**

```python
"""Four-frame stack of 29-float observations."""

OBS_LEN = 29
STACK = 4


class ObsStack:
	"""Oldest frame first. Reset fills the stack with the opening frame."""

	def __init__(self):
		"""Start empty. Call ``reset`` before reading."""
		self._frames = []

	def reset(self, obs) -> list:
		"""Repeat ``obs`` four times and return the flat policy vector."""
		self._check(obs)
		self._frames = [list(obs) for _ in range(STACK)]
		return self.vector()

	def push(self, obs) -> list:
		"""Append one frame, drop the oldest, and return the flat vector."""
		self._check(obs)
		self._frames.append(list(obs))
		self._frames = self._frames[-STACK:]
		return self.vector()

	def vector(self) -> list:
		"""Flatten the current stack."""
		flat = []
		for frame in self._frames:
			flat.extend(frame)
		return flat

	def _check(self, obs) -> None:
		"""Raise when a frame is not length 29."""
		if len(obs) != OBS_LEN:
			raise ValueError(f"obs length {len(obs)}, expected {OBS_LEN}")
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `python -m pytest tests/test_stack.py -v`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add trainer/stack.py tests/test_stack.py
git commit -m "Stack the last four on-screen observations."
```

---

### Task 4: Gym environment against a fake server

**Files:**
- Create: `trainer/env.py`
- Create: `tests/fake_server.py`
- Create: `tests/test_env.py`
- Create: `requirements.txt`

**Interfaces:**
- Consumes: `decode_action`, `reward_from_transition`, `ObsStack`
- Produces: `CarnageEnv` with `action_space` Discrete(54), `observation_space` Box shape `(116,)`, `reset`, `step`. Constructor args: `host`, `port`, `cpu_level`, `time_scale`, `max_resets`.

- [ ] **Step 1: Add `requirements.txt`**

```
gymnasium>=1.0,<2
stable-baselines3>=2.3,<3
torch>=2.2
pytest>=8,<9
numpy>=1.26,<3
```

Install with `python -m pip install -r requirements.txt`.

- [ ] **Step 2: Write the fake server and the failing env test**

`tests/fake_server.py` listens on an ephemeral port in a thread. A script is a list of response bodies. Each `reset` or `step` pops the next body. `ping` returns `{"ok": true}`. The server stores the last request.

`tests/test_env.py` covers:

- A scripted CPU health drop to 0 produces `done` and a positive terminal reward from `reward_from_transition`, and the policy vector length is 116.
- `winner == "cpu"` produces a negative terminal adjustment.
- Three failed resets raise `GameConnectionError`.
- The step request JSON contains `"action"` equal to the integer passed to `step`.

- [ ] **Step 3: Implement `trainer/env.py`**

Behavior locked by the spec:

- `reset` sends `{"cmd":"reset","cpu_level":<int>,"time_scale":<float>}` and retries up to 3 failures.
- `step` sends `{"cmd":"step","action":<int>}`. It does not trust the JSON reward. It calls `reward_from_transition`.
- Observations are stacked.
- `info` from the server is passed through, with `reward_plugin` omitted because the plugin reward is unused.
- Connection failure while connecting waits and retries until 30 seconds have elapsed, then raises `GameConnectionError` with the exact message `Launch Electoral Carnage with CarnageGym and open Lincoln versus Lincoln CPU training.`

- [ ] **Step 4: Run the tests**

Run: `python -m pytest tests/test_env.py tests/test_actions.py tests/test_reward.py tests/test_stack.py -v`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add requirements.txt trainer/env.py tests/fake_server.py tests/test_env.py
git commit -m "Add the Gym environment and a fake match server."
```

---

### Task 5: C# action codec

**Files:**
- Create: `plugin/CarnageGym/ActionCodec.cs`
- Create: `plugin/CarnageGym.Tests/CarnageGym.Tests.csproj`
- Create: `plugin/CarnageGym.Tests/ActionCodecTests.cs`

**Interfaces:**
- Consumes: `tests/fixtures/actions.json`
- Produces: `CarnageGym.ActionCodec.Decode(int action)` returning move and button fields matching the Python fixture. Out-of-range returns false and leaves an error string `bad_action`.

- [ ] **Step 1: Write `ActionCodec.cs` with XML docs and no Unity references**

The decoder uses the same direction table and button indexes as `trainer/actions.py`. `jump_attack` sets attack and jump. The method signature is:

```csharp
public static bool Decode(int action, out int moveX, out int moveY, out bool attack, out bool special, out bool jump, out bool super)
```

- [ ] **Step 2: Write tests that load `tests/fixtures/actions.json`**

The test project targets `net6.0`. It compiles `../CarnageGym/ActionCodec.cs` as a linked file. Tests assert action 0, action 22, action 53, action -1, action 54, and every fixture row.

- [ ] **Step 3: Run `dotnet test plugin/CarnageGym.Tests`**

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add plugin/CarnageGym/ActionCodec.cs plugin/CarnageGym.Tests
git commit -m "Mirror the action table in the plugin codec."
```

---

### Task 6: BepInEx plugin

**Files:**
- Create: `plugin/CarnageGym/CarnageGym.csproj`
- Create: `plugin/CarnageGym/Plugin.cs`
- Create: `plugin/CarnageGym/ObservationBuilder.cs`
- Create: `plugin/CarnageGym/InputForcer.cs`
- Create: `plugin/CarnageGym/MatchDriver.cs`
- Create: `plugin/CarnageGym/IpcServer.cs`
- Create: `README.md`

**Interfaces:**
- Consumes: `ActionCodec.Decode`
- Produces: a plugin DLL that speaks the spec protocol on `127.0.0.1:47831`

- [ ] **Step 1: Download BepInEx**

Download `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip` into `third_party/` (gitignored) and unzip it. Reference `BepInEx.dll`, `0Harmony.dll`, and the game's `UnityEngine.dll` plus `UnityEngine.CoreModule.dll` from the Electoral Carnage `Managed` folder. The csproj target is `net472`, output `CarnageGym.dll`.

- [ ] **Step 2: Implement the plugin pieces**

`ObservationBuilder` reads only the allowlisted fields, clamps as the spec table says, and replaces non-finite components with 0. Five dirty decisions in a row set the winner to `draw` and `done` true.

`InputForcer` holds a decoded packet for 3 `Update` calls. A Harmony postfix on `BasicCharacterScript.GetInput` rewrites the agent instance's `inputs` packet (`moveVectorx`, `moveVectory`, `attack`, `special`, `jump`, `super`, with `start` and `tapJump` false) while a hold is active.

`MatchDriver.Reset` requires exactly one `PlayerInfo` with `npc == false` and one with `npc == true`, equal `characterID`, and a loaded `trainingModeReset`. It writes `cpuLevel` using the field's real type (`int` or `float`). It deactivates and reactivates the reset object so `OnEnable` runs, then waits up to 600 frames for both health fractions to reach 0.99. It probes acceptable CPU levels 0 through 10 once.

`IpcServer` accepts one client. Requests run on the Unity main thread. Responses match the spec, with `reward` always 0 and `obs` length 29.

If `PlayerInfo.health` or another required field is missing when the plugin awakes, log the type and field and do not bind the socket.

- [ ] **Step 3: Build**

Run: `dotnet build plugin/CarnageGym/CarnageGym.csproj -c Release`

Expected: build succeeds and writes `CarnageGym.dll`.

- [ ] **Step 4: Document install and the hand smoke test in `README.md`**

Include the game path, the BepInEx copy into the game folder (`winhttp.dll` and the `BepInEx` directory), the plugin copy into `BepInEx/plugins`, the venv command, and the four smoke checks from the spec. State that online play is untouched and the operator opens Lincoln versus Lincoln CPU training before starting the trainer.

- [ ] **Step 5: Commit**

```bash
git add plugin/CarnageGym README.md
git commit -m "Add the local BepInEx bridge for Lincoln training."
```

Do not commit `third_party/` or anything copied into the Steam game folder.

---

### Task 7: PPO entry point

**Files:**
- Create: `trainer/train.py`
- Create: `tests/test_train_config.py`

**Interfaces:**
- Consumes: `CarnageEnv`
- Produces: `build_model(env, seed=0)` returning an SB3 `PPO` with the spec hyperparameters, and a `main` that trains for the requested timestep count.

- [ ] **Step 1: Write a test that builds the model against a tiny gym stub if import of SB3 works, and skips if torch is absent**

Assert `learning_rate == 3e-4`, `n_steps == 2048`, `batch_size == 256`, `gamma == 0.995`, `gae_lambda == 0.95`, `ent_coef == 0.01`, `clip_range` is a constant 0.2, and the policy net arch is `[128, 128]`.

- [ ] **Step 2: Implement `trainer/train.py`**

Arguments: `--timesteps` default `2000000`, `--cpu-start` default the probed minimum once connected (pass the flag as the initial `cpu_level`), `--time-scale` default `3`, `--host` default `127.0.0.1`, `--port` default `47831`.

Every 10,000 timesteps, run 10 deterministic episodes at time scale 1 by constructing those episodes through the same env after setting its scale, and append `logs/progress.csv`. Save `checkpoints/lincoln_ppo` every 20,480 timesteps. Curriculum window is 30 training rounds with the 0.65 / 0.25 rules. A 120-decision stall drops the next reset's time scale to 1 once per process.

- [ ] **Step 3: Run `python -m pytest tests/test_train_config.py -v`**

Expected: PASS, or SKIP with the reason torch/SB3 failed to import. Do not skip if the import works.

- [ ] **Step 4: Commit**

```bash
git add trainer/train.py tests/test_train_config.py
git commit -m "Add the Lincoln PPO training loop."
```

---

### Task 8: Milestone reminder

The 50-round greedy eval at the easiest CPU level is a manual milestone after the game smoke test passes. It is not a unit test. Record the win rate in `logs/milestone.txt` when it is run. Pass is a win rate of at least 0.70.
