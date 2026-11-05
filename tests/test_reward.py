"""Reward numbers from the design spec."""

import pytest

from trainer.reward import reward_from_transition


def _obs(agent_hp=1.0, cpu_hp=1.0, agent_meter=0.0, cpu_meter=0.0):
	"""Build a 29-float frame with health and meter filled in."""
	values = [0.0] * 29
	values[10] = agent_hp
	values[11] = cpu_hp
	values[12] = agent_meter
	values[13] = cpu_meter
	return values


def test_first_step_is_only_the_time_penalty():
	"""The opening decision has no previous frame, so only the step penalty applies."""
	assert reward_from_transition(None, _obs(), False, None) == pytest.approx(-0.002)


def test_damage_and_meter():
	"""Health drops and meter changes use the spec weights."""
	previous = _obs(agent_hp=1.0, cpu_hp=1.0, agent_meter=0.2, cpu_meter=0.2)
	current = _obs(agent_hp=0.9, cpu_hp=0.7, agent_meter=0.4, cpu_meter=0.3)
	expected = 0.3 - 0.1 + 0.15 * 0.2 - 0.15 * 0.1 - 0.002
	assert reward_from_transition(previous, current, False, None) == pytest.approx(expected)


def test_terminal_bonuses():
	"""Win, loss, timeout, and draw add the spec's terminal term."""
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
	"""A finished round without a winner is a protocol error."""
	with pytest.raises(ValueError, match="winner"):
		reward_from_transition(_obs(), _obs(), True, None)
