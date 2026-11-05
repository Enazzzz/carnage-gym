import json
from pathlib import Path

import pytest

from trainer.actions import N_ACTIONS, decode_action


FIXTURE = Path(__file__).parent / "fixtures" / "actions.json"


def test_table_matches_fixture():
	"""Every legal index matches the checked-in packet table."""
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
	"""Indexes outside 0..53 are rejected."""
	with pytest.raises(ValueError, match="bad_action"):
		decode_action(-1)
	with pytest.raises(ValueError, match="bad_action"):
		decode_action(54)
	with pytest.raises(ValueError, match="bad_action"):
		decode_action(True)


def test_named_examples():
	"""Lock the three packets the C# tests also name."""
	neutral = decode_action(0).as_dict()
	assert neutral["move_x"] == 0 and neutral["move_y"] == 0
	assert neutral["attack"] is False and neutral["super"] is False
	up_jump_attack = decode_action(22).as_dict()
	assert (up_jump_attack["move_x"], up_jump_attack["move_y"]) == (0, 1)
	assert up_jump_attack["jump"] is True and up_jump_attack["attack"] is True
	assert up_jump_attack["special"] is False and up_jump_attack["super"] is False
	down_right_super = decode_action(53).as_dict()
	assert (down_right_super["move_x"], down_right_super["move_y"]) == (1, -1)
	assert down_right_super["super"] is True
	assert down_right_super["attack"] is False
