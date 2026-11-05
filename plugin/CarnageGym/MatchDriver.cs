using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarnageGym
{
	/// <summary>
	/// Finds the local Lincoln and the CPU, then fires the game's training reset.
	/// </summary>
	public sealed class MatchDriver
	{
		/// <summary>Local player PlayerInfo.</summary>
		public object Agent;

		/// <summary>CPU PlayerInfo.</summary>
		public object Cpu;

		/// <summary>Local BasicCharacterScript, when one sits on the player object.</summary>
		public object AgentBasic;

		/// <summary>CPU BasicCharacterScript, when one sits on the player object.</summary>
		public object CpuBasic;

		/// <summary>Lowest CPU level that read back unchanged.</summary>
		public int CpuMin { get; private set; }

		/// <summary>Highest CPU level that read back unchanged.</summary>
		public int CpuMax { get; private set; }

		bool _probed;

		/// <summary>
		/// Check the loaded match. On success, the reset object is toggled so OnEnable runs.
		/// </summary>
		public string BeginReset(int cpuLevel, float timeScale)
		{
			Agent = null;
			Cpu = null;
			AgentBasic = null;
			CpuBasic = null;
			var players = SceneComponents("PlayerInfo");
			object agent = null;
			object cpu = null;
			int locals = 0;
			int npcs = 0;
			foreach (var player in players)
			{
				bool npc = Fields.AsBool(Fields.Read(player, "npc"));
				if (npc)
				{
					npcs += 1;
					cpu = player;
				}
				else
				{
					locals += 1;
					agent = player;
				}
			}
			if (locals != 1 || npcs != 1)
				return "need_one_cpu";
			if (!SameCharacter(agent, cpu))
				return "character_mismatch";
			var resets = SceneComponents("trainingModeReset");
			if (resets.Count == 0)
				return "training_reset_missing";
			Agent = agent;
			Cpu = cpu;
			AgentBasic = BasicOn(agent);
			CpuBasic = BasicOn(cpu);
			InputForcer.Agent = AgentBasic;
			if (!_probed)
			{
				Probe(cpu);
				_probed = true;
			}
			int level = cpuLevel;
			if (level < CpuMin)
				level = CpuMin;
			if (level > CpuMax)
				level = CpuMax;
			AssignLevel(cpu, level);
			Time.timeScale = Mathf.Clamp(timeScale, 1f, 8f);
			var resetObject = ((Component)resets[0]).gameObject;
			resetObject.SetActive(false);
			resetObject.SetActive(true);
			Debug.Log("character_ids " + Fields.Read(agent, "characterID") + " " + Fields.Read(cpu, "characterID"));
			return null;
		}

		/// <summary>
		/// True when both fighters are back to a full health bar.
		/// </summary>
		public bool HealthRecovered()
		{
			if (Agent == null || Cpu == null)
				return false;
			return ObservationBuilder.Health(Agent) >= 0.99f && ObservationBuilder.Health(Cpu) >= 0.99f;
		}

		/// <summary>
		/// The time scale actually applied, after clamping.
		/// </summary>
		public float TimeScale
		{
			get { return Time.timeScale; }
		}

		void Probe(object cpu)
		{
			int original = Mathf.RoundToInt(Fields.AsFloat(Fields.Read(cpu, "cpuLevel")));
			int min = original;
			int max = original;
			bool any = false;
			for (int level = 0; level <= 10; level++)
			{
				if (!AssignLevel(cpu, level))
					continue;
				if (!any || level < min)
					min = level;
				if (!any || level > max)
					max = level;
				any = true;
			}
			CpuMin = min;
			CpuMax = max;
			AssignLevel(cpu, original);
		}

		static bool AssignLevel(object cpu, int level)
		{
			var field = AccessTools.Field(cpu.GetType(), "cpuLevel");
			if (field == null)
				return false;
			object written = level;
			if (field.FieldType == typeof(float))
				written = (float)level;
			else if (field.FieldType == typeof(double))
				written = (double)level;
			else if (field.FieldType.IsEnum)
				written = System.Enum.ToObject(field.FieldType, level);
			else if (field.FieldType != typeof(int))
				return false;
			field.SetValue(cpu, written);
			foreach (var menu in SceneComponents("cpuLevel"))
				Fields.Write(menu, "integer", level);
			int readBack = Mathf.RoundToInt(Fields.AsFloat(field.GetValue(cpu)));
			return readBack == level;
		}

		static bool SameCharacter(object agent, object cpu)
		{
			var left = Fields.Read(agent, "characterID");
			var right = Fields.Read(cpu, "characterID");
			if (left == null || right == null)
				return false;
			return left.ToString() == right.ToString();
		}

		static object BasicOn(object player)
		{
			var component = player as Component;
			if (component == null)
				return null;
			var type = AccessTools.TypeByName("BasicCharacterScript");
			if (type == null)
				return null;
			var basic = component.GetComponent(type);
			if (basic == null)
				basic = component.GetComponentInChildren(type, true);
			return basic;
		}

		static List<object> SceneComponents(string typeName)
		{
			var found = new List<object>();
			var type = AccessTools.TypeByName(typeName);
			if (type == null)
				return found;
			var all = Resources.FindObjectsOfTypeAll(type);
			foreach (var obj in all)
			{
				var component = obj as Component;
				if (component == null)
					continue;
				if (!component.gameObject.scene.IsValid())
					continue;
				found.Add(component);
			}
			return found;
		}
	}
}
