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
