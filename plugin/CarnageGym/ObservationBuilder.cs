using System;
using HarmonyLib;
using UnityEngine;

namespace CarnageGym
{
	/// <summary>
	/// Builds the 29-float allowlisted observation.
	/// CPU command lists and raw input packets are never read.
	/// </summary>
	public sealed class ObservationBuilder
	{
		const int Length = 29;

		bool _hasPrevious;
		float _agentX;
		float _agentY;
		float _cpuX;
		float _cpuY;
		int _dirtyStreak;
		bool _loggedScaleFallback;

		/// <summary>
		/// Forget velocity history. The next build reports zero velocity.
		/// </summary>
		public void ResetHistory()
		{
			_hasPrevious = false;
			_dirtyStreak = 0;
		}

		/// <summary>
		/// True after five builds in a row replaced a non-finite component.
		/// </summary>
		public bool ForceDraw { get; private set; }

		/// <summary>
		/// Build one observation from the agent and CPU player objects.
		/// </summary>
		public float[] Build(object agent, object cpu, object agentBasic, object cpuBasic)
		{
			ForceDraw = false;
			var values = new float[Length];
			float scaleX;
			float scaleY;
			Scales(Fields.Read(agent, "movementBound"), out scaleX, out scaleY);
			var agentPos = Position(agent);
			var cpuPos = Position(cpu);
			float agentScaledX = agentPos.x / scaleX;
			float agentScaledY = agentPos.y / scaleY;
			float cpuScaledX = cpuPos.x / scaleX;
			float cpuScaledY = cpuPos.y / scaleY;
			float agentVelocityX = 0f;
			float agentVelocityY = 0f;
			float cpuVelocityX = 0f;
			float cpuVelocityY = 0f;
			if (_hasPrevious)
			{
				agentVelocityX = agentScaledX - _agentX;
				agentVelocityY = agentScaledY - _agentY;
				cpuVelocityX = cpuScaledX - _cpuX;
				cpuVelocityY = cpuScaledY - _cpuY;
			}
			_agentX = agentScaledX;
			_agentY = agentScaledY;
			_cpuX = cpuScaledX;
			_cpuY = cpuScaledY;
			_hasPrevious = true;

			values[0] = Clamp(agentScaledX, -2f, 2f);
			values[1] = Clamp(agentScaledY, -2f, 2f);
			values[2] = Clamp(cpuScaledX, -2f, 2f);
			values[3] = Clamp(cpuScaledY, -2f, 2f);
			values[4] = Clamp(agentVelocityX, -2f, 2f);
			values[5] = Clamp(agentVelocityY, -2f, 2f);
			values[6] = Clamp(cpuVelocityX, -2f, 2f);
			values[7] = Clamp(cpuVelocityY, -2f, 2f);
			values[8] = Fields.Facing(Fields.Read(agent, "facing"));
			values[9] = Fields.Facing(Fields.Read(cpu, "facing"));
			values[10] = Health(agent);
			values[11] = Health(cpu);
			values[12] = Super(agent);
			values[13] = Super(cpu);
			values[14] = Flag(agent, "superReady");
			values[15] = Flag(cpu, "superReady");
			values[16] = Flag(agentBasic, "airborn");
			values[17] = Flag(cpuBasic, "airborn");
			values[18] = Flag(agentBasic, "dashing");
			values[19] = Flag(cpuBasic, "dashing");
			values[20] = Flag(agent, "hit");
			values[21] = Flag(cpu, "hit");
			values[22] = Flag(agentBasic, "hitting");
			values[23] = Flag(cpuBasic, "hitting");
			values[24] = Flag(agentBasic, "running");
			values[25] = Flag(cpuBasic, "running");
			float relX = cpuScaledX - agentScaledX;
			float relY = cpuScaledY - agentScaledY;
			values[26] = Clamp(relX, -4f, 4f);
			values[27] = Clamp(relY, -4f, 4f);
			values[28] = Clamp(Mathf.Sqrt(relX * relX + relY * relY), 0f, 6f);

			bool dirty = false;
			for (int i = 0; i < values.Length; i++)
			{
				if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
				{
					values[i] = 0f;
					dirty = true;
				}
			}
			_dirtyStreak = dirty ? _dirtyStreak + 1 : 0;
			if (_dirtyStreak >= 5)
				ForceDraw = true;
			return values;
		}

		/// <summary>
		/// Health fraction. A non-positive max health uses a tiny denominator.
		/// </summary>
		public static float Health(object player)
		{
			float max = Fields.AsFloat(Fields.Read(player, "maxHealth"));
			if (max < 1e-6f)
				max = 1e-6f;
			return Clamp(Fields.AsFloat(Fields.Read(player, "health")) / max, 0f, 1f);
		}

		/// <summary>
		/// True when this fighter should count as defeated for the winner rule.
		/// </summary>
		public static bool Defeated(object player)
		{
			if (Fields.AsBool(Fields.Read(player, "zeroHealth")))
				return true;
			return Health(player) <= 0f;
		}

		void Scales(object raw, out float scaleX, out float scaleY)
		{
			scaleX = 8f;
			scaleY = 8f;
			bool fallback = true;
			if (raw is float number && number > 0f)
			{
				scaleX = number;
				scaleY = number;
				fallback = false;
			}
			else if (raw is Vector2 vector && Mathf.Abs(vector.x) > 0f)
			{
				scaleX = Mathf.Abs(vector.x);
				scaleY = Mathf.Abs(vector.y) > 0f ? Mathf.Abs(vector.y) : scaleX;
				fallback = false;
			}
			else if (raw is Vector3 vector3 && Mathf.Abs(vector3.x) > 0f)
			{
				scaleX = Mathf.Abs(vector3.x);
				scaleY = Mathf.Abs(vector3.y) > 0f ? Mathf.Abs(vector3.y) : scaleX;
				fallback = false;
			}
			if (fallback && !_loggedScaleFallback)
			{
				_loggedScaleFallback = true;
				Debug.Log("stage_scale_fallback");
			}
		}

		static Vector3 Position(object player)
		{
			var component = player as Component;
			if (component == null)
				return Vector3.zero;
			return component.transform.position;
		}

		static float Super(object player)
		{
			float cost = Fields.AsFloat(Fields.Read(player, "superCost"));
			float charge = Fields.AsFloat(Fields.Read(player, "superCharge"));
			float denom = cost > 0f ? cost : 100f;
			return Clamp(charge / denom, 0f, 1f);
		}

		static float Flag(object instance, string fieldName)
		{
			return Fields.AsBool(Fields.Read(instance, fieldName)) ? 1f : 0f;
		}

		static float Clamp(float value, float low, float high)
		{
			if (value < low)
				return low;
			if (value > high)
				return high;
			return value;
		}
	}
}
