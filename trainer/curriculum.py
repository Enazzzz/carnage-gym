"""Difficulty window for the built-in CPU."""


class Curriculum:
	"""Climb or drop one CPU level after every 30 finished rounds."""

	def __init__(self, level, minimum, maximum):
		"""Start at ``level``, clamped to the probed bounds."""
		self.minimum = int(minimum)
		self.maximum = int(maximum)
		self.level = min(max(int(level), self.minimum), self.maximum)
		self._wins = 0
		self._count = 0

	def record(self, winner) -> None:
		"""Count one finished round. Timeouts and draws are not wins."""
		self._count += 1
		if winner == "agent":
			self._wins += 1
		if self._count < 30:
			return
		rate = self._wins / 30.0
		if rate >= 0.65 and self.level < self.maximum:
			self.level += 1
		elif rate <= 0.25 and self.level > self.minimum:
			self.level -= 1
		self._wins = 0
		self._count = 0

	@property
	def win_rate_30(self) -> float:
		"""Win rate inside the open window. Zero before any round is recorded."""
		if self._count == 0:
			return 0.0
		return self._wins / float(self._count)
