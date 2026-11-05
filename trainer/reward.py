"""Round reward from two on-screen observations.

The plugin does not compute this. Pass the previous observation, or None on
the first decision of a round.
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
