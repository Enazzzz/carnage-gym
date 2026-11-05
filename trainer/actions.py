"""Discrete stick and button table shared with the BepInEx plugin."""

from dataclasses import dataclass


# (move_x, move_y). Index order is part of the wire contract.
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
