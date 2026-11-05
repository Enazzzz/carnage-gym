using System;
using System.Reflection;
using HarmonyLib;

namespace CarnageGym
{
	/// <summary>
	/// Holds the agent's local button packet for a few rendered frames.
	/// </summary>
	public static class InputForcer
	{
		/// <summary>BasicCharacterScript instance controlled by the trainer.</summary>
		public static object Agent;

		/// <summary>True while a decision packet should be rewritten.</summary>
		public static bool Holding;

		static int _moveX;
		static int _moveY;
		static bool _attack;
		static bool _special;
		static bool _jump;
		static bool _super;

		/// <summary>
		/// Remember the packet that subsequent writes will force.
		/// </summary>
		public static void SetPacket(int moveX, int moveY, bool attack, bool special, bool jump, bool super)
		{
			_moveX = moveX;
			_moveY = moveY;
			_attack = attack;
			_special = special;
			_jump = jump;
			_super = super;
			Holding = true;
		}

		/// <summary>
		/// Stop forcing buttons at the end of a decision.
		/// </summary>
		public static void Release()
		{
			Holding = false;
		}

		/// <summary>
		/// Rewrite the agent packet when GetInput just ran for that instance.
		/// </summary>
		public static void Apply(object basic)
		{
			if (!Holding || basic == null || !ReferenceEquals(basic, Agent))
				return;
			var inputsField = AccessTools.Field(basic.GetType(), "inputs");
			if (inputsField == null)
				return;
			var inputs = inputsField.GetValue(basic);
			if (inputs == null)
			{
				inputs = Activator.CreateInstance(inputsField.FieldType);
				inputsField.SetValue(basic, inputs);
			}
			Fields.Write(inputs, "moveVectorx", _moveX);
			Fields.Write(inputs, "moveVectory", _moveY);
			Fields.Write(inputs, "attack", _attack);
			Fields.Write(inputs, "special", _special);
			Fields.Write(inputs, "jump", _jump);
			Fields.Write(inputs, "super", _super);
			Fields.Write(inputs, "start", false);
			Fields.Write(inputs, "tapJump", false);
		}
	}

	/// <summary>
	/// Re-applies the held packet after the game samples local input.
	/// </summary>
	[HarmonyPatch]
	static class GetInputPatch
	{
		/// <summary>
		/// Skip the patch when this build has no BasicCharacterScript.GetInput.
		/// </summary>
		static bool Prepare()
		{
			var type = AccessTools.TypeByName("BasicCharacterScript");
			return type != null && AccessTools.Method(type, "GetInput") != null;
		}

		/// <summary>
		/// Target the local input sampler.
		/// </summary>
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(AccessTools.TypeByName("BasicCharacterScript"), "GetInput");
		}

		/// <summary>
		/// Force the trainer packet onto the agent after the game wrote its own.
		/// </summary>
		static void Postfix(object __instance)
		{
			InputForcer.Apply(__instance);
		}
	}
}
