"""CPU level window."""

from trainer.curriculum import Curriculum


def test_climbs_after_thirty_wins():
	"""A 65% window raises the level by one."""
	curriculum = Curriculum(level=1, minimum=0, maximum=5)
	for _round in range(20):
		curriculum.record("agent")
	for _round in range(10):
		curriculum.record("cpu")
	assert curriculum.level == 2
	assert curriculum.win_rate_30 == 0.0


def test_drops_after_a_cold_window():
	"""A window at or under 25% lowers the level by one."""
	curriculum = Curriculum(level=2, minimum=0, maximum=5)
	for _round in range(7):
		curriculum.record("agent")
	for _round in range(23):
		curriculum.record("timeout")
	assert curriculum.level == 1


def test_timeout_is_not_a_win():
	"""Draws and timeouts stay in the denominator only."""
	curriculum = Curriculum(level=0, minimum=0, maximum=3)
	for _round in range(30):
		curriculum.record("draw")
	assert curriculum.level == 0
