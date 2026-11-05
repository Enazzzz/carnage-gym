using System;

namespace CarnageGym
{
	/// <summary>
	/// Maps a discrete action index onto stick and button fields.
	/// The table matches trainer/actions.py and tests/fixtures/actions.json.
	/// </summary>
	public static class ActionCodec
	{
		static readonly int[] DirectionX = { 0, -1, 1, 0, 0, -1, 1, -1, 1 };
		static readonly int[] DirectionY = { 0, 0, 0, 1, -1, 1, 1, -1, -1 };

		/// <summary>
		/// Decode an action in 0..53.
		/// Returns false and sets every output to a neutral packet when the index is illegal.
		/// </summary>
		public static bool Decode(
			int action,
			out int moveX,
			out int moveY,
			out bool attack,
			out bool special,
			out bool jump,
			out bool super)
		{
			moveX = 0;
			moveY = 0;
			attack = false;
			special = false;
			jump = false;
			super = false;
			if (action < 0 || action > 53)
				return false;
			int direction = action / 6;
			int button = action % 6;
			moveX = DirectionX[direction];
			moveY = DirectionY[direction];
			// 0 none, 1 attack, 2 special, 3 jump, 4 jump+attack, 5 super.
			attack = button == 1 || button == 4;
			special = button == 2;
			jump = button == 3 || button == 4;
			super = button == 5;
			return true;
		}
	}
}
