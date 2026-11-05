using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarnageGym
{
	/// <summary>
	/// Finds the local fighter and the CPU, then presses the match's Rematch object.
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
		/// Check the loaded match. On success, Rematch is pressed so the game restarts the round.
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
			var rematch = FindRematch();
			if (rematch == null)
				return "rematch_missing";
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
			Press(rematch);
			Debug.Log("rematch character_ids " + Fields.Read(agent, "characterID") + " " + Fields.Read(cpu, "characterID"));
			return null;
		}

		/// <summary>
		/// The local Rematch button in the active scene. Online rematch objects are left alone.
		/// </summary>
		static GameObject FindRematch()
		{
			GameObject fallback = null;
			var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
			{
				if (obj == null || obj.name != "Rematch" || !obj.scene.IsValid())
					continue;
				if (obj.scene == active)
					return obj;
				fallback = obj;
			}
			return fallback;
		}

		/// <summary>
		/// Wake the button's parents, then toggle it so its OnEnable handler runs.
		/// </summary>
		static void Press(GameObject rematch)
		{
			var chain = new List<GameObject>();
			var current = rematch.transform;
			while (current != null)
			{
				chain.Add(current.gameObject);
				current = current.parent;
			}
			for (int i = chain.Count - 1; i >= 0; i--)
			{
				if (!chain[i].activeSelf)
					chain[i].SetActive(true);
			}
			rematch.SetActive(false);
			rematch.SetActive(true);
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
