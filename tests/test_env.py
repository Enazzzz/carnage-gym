"""Env behavior against the fake match server."""

import numpy as np
import pytest

from tests.fake_server import FakeMatch
from trainer.env import LAUNCH_MESSAGE, CarnageEnv, GameConnectionError


def _frame(agent_hp=1.0, cpu_hp=1.0):
	"""One plugin observation with health filled in."""
	values = [0.0] * 29
	values[10] = agent_hp
	values[11] = cpu_hp
	return values


def _ok(obs, done=False, winner=None, **info):
	"""A successful plugin response."""
	body = {
		"ok": True,
		"obs": obs,
		"reward": 0.0,
		"done": done,
		"info": {"winner": winner, "cpu_level": 0, "time_scale": 3.0},
	}
	body["info"].update(info)
	return body


def test_scripted_win_scores_damage_and_stacks():
	"""A KO on the second decision pays the damage term and the win bonus."""
	script = [
		_ok(_frame()),
		_ok(_frame()),
		_ok(_frame(cpu_hp=0.0), done=True, winner="agent"),
	]
	with FakeMatch(script) as fake:
		env = CarnageEnv(port=fake.port, connect_timeout=2)
		vector, _info = env.reset()
		assert vector.shape == (116,)
		assert np.all(vector[:29] == vector[-29:])
		_mid, reward_mid, done_mid, _trunc, _info = env.step(4)
		assert done_mid is False
		assert reward_mid == pytest.approx(-0.002)
		_final, reward, done, _trunc, info = env.step(22)
		assert done is True
		assert info["winner"] == "agent"
		assert reward == pytest.approx(1.0 - 0.002 + 1.0)
		assert any(item.get("action") == 22 for item in fake.requests)
		env.close()


def test_scripted_loss():
	"""A lost round includes the -1 terminal."""
	script = [
		_ok(_frame()),
		_ok(_frame(agent_hp=0.0), done=True, winner="cpu"),
	]
	with FakeMatch(script) as fake:
		env = CarnageEnv(port=fake.port, connect_timeout=2)
		env.reset()
		_obs, reward, done, _trunc, info = env.step(0)
		assert done is True
		assert info["winner"] == "cpu"
		# First decision has no delta. Terminal is -1 plus the step penalty.
		assert reward == pytest.approx(-1.0 - 0.002)
		env.close()


def test_three_failed_resets_raise():
	"""The env stops after the spec's three reset attempts."""
	failure = {
		"ok": False,
		"error": "need_one_cpu",
		"obs": [0.0] * 29,
		"reward": 0.0,
		"done": True,
		"info": {},
	}
	with FakeMatch([failure, failure, failure]) as fake:
		env = CarnageEnv(port=fake.port, connect_timeout=2, max_resets=3)
		with pytest.raises(GameConnectionError, match="need_one_cpu"):
			env.reset()
		env.close()


def test_connect_timeout_uses_the_launch_message():
	"""A closed port ends in the operator-facing message."""
	env = CarnageEnv(port=1, connect_timeout=0.3)
	with pytest.raises(GameConnectionError, match=LAUNCH_MESSAGE):
		env.reset()


def test_stall_drops_the_next_reset_to_realtime():
	"""120 motionless decisions force the following reset to time scale 1."""
	still = _ok(_frame())
	with FakeMatch([still], default=still) as fake:
		env = CarnageEnv(port=fake.port, time_scale=3, connect_timeout=2)
		env.reset()
		for _step in range(121):
			env.step(0)
		env.reset()
		resets = [item for item in fake.requests if item.get("cmd") == "reset"]
		assert resets[0]["time_scale"] == 3
		assert resets[1]["time_scale"] == 1
		env.close()
