"""Frame stack length and order."""

from trainer.stack import ObsStack


def test_reset_repeats_the_first_frame():
	"""Reset fills all four slots with the opening observation."""
	stack = ObsStack()
	first = [float(i) for i in range(29)]
	vector = stack.reset(first)
	assert len(vector) == 116
	assert vector[:29] == first
	assert vector[-29:] == first


def test_push_drops_the_oldest():
	"""The fifth distinct frame pushes the reset frame out of the stack."""
	stack = ObsStack()
	stack.reset([0.0] * 29)
	for step in range(1, 4):
		stack.push([float(step)] * 29)
	vector = stack.push([4.0] * 29)
	assert vector[:29] == [1.0] * 29
	assert vector[-29:] == [4.0] * 29


def test_rejects_wrong_length():
	"""A short frame is an error, not a silently padded policy input."""
	stack = ObsStack()
	try:
		stack.reset([0.0] * 28)
	except ValueError as exc:
		assert "29" in str(exc)
	else:
		raise AssertionError("expected ValueError")
