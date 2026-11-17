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
		/// Drop fighter references after a rematch reload.
		/// </summary>
		public void ReleaseFighters()
		{
			Agent = null;
			Cpu = null;
			AgentBasic = null;
			CpuBasic = null;
			InputForcer.Agent = null;
		}

		/// <summary>
		/// Bind player slot 1 as the agent and slot 2 as Roosevelt.
		/// A local CPU is slot 2. The npc flag is not how that match marks the CPU.
		/// Returns null when both live fighters are bound, otherwise a roster for the log.
		/// </summary>
		public string TryBind()
		{
			ReleaseFighters();
			var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			var roster = Describe(active);
			if (!BindSlots(active, true) && !BindSlots(active, false) && !BindPair(active))
				return roster;
			InputForcer.Agent = AgentBasic;
			return null;
		}

		/// <summary>
		/// Write the CPU level onto the live fighter and the objects rematch copies from.
		/// </summary>
		public void ApplyLevel(int cpuLevel, float timeScale)
		{
			if (Cpu == null)
				return;
			if (!_probed)
			{
				Probe(Cpu);
				_probed = true;
			}
			int level = cpuLevel;
			if (level < CpuMin)
				level = CpuMin;
			if (level > CpuMax)
				level = CpuMax;
			AssignLevel(Cpu, level);
			foreach (var carrier in SceneComponents("bigEnabler"))
				Fields.Write(carrier, "cpuLevel", level);
			foreach (var carrier in SceneComponents("newRematch"))
				Fields.Write(carrier, "cpuLevel", level);
			Time.timeScale = Mathf.Clamp(timeScale, 1f, 8f);
		}

		/// <summary>
		/// Set newRematch.doRematch, which is what the rematch button turns on.
		/// </summary>
		public bool PressRematch()
		{
			bool fired = false;
			foreach (var rematch in SceneComponents("newRematch"))
			{
				var component = rematch as Component;
				if (component == null)
					continue;
				Wake(component.gameObject);
				Fields.Write(rematch, "doRematch", true);
				fired = true;
			}
			if (fired)
			{
				Debug.Log("rematch pressed character_ids " + Fields.Read(Agent, "characterID") + " " + Fields.Read(Cpu, "characterID"));
				return true;
			}
			var button = FindRematch();
			if (button == null)
				return false;
			Press(button);
			Debug.Log("rematch button character_ids " + Fields.Read(Agent, "characterID") + " " + Fields.Read(Cpu, "characterID"));
			return true;
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
			Wake(rematch);
			rematch.SetActive(false);
			rematch.SetActive(true);
		}

		/// <summary>
		/// Enable a button and its parents so its Update can see doRematch.
		/// </summary>
		static void Wake(GameObject obj)
		{
			var chain = new List<GameObject>();
			var current = obj.transform;
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

		bool BindSlots(UnityEngine.SceneManagement.Scene active, bool activeSceneOnly)
		{
			object agent = null;
			object cpu = null;
			int locals = 0;
			int opponents = 0;
			foreach (var player in SceneComponents("PlayerInfo"))
			{
				if (!Live(player, active, activeSceneOnly))
					continue;
				int slot = Mathf.RoundToInt(Fields.AsFloat(Fields.Read(player, "player")));
				if (slot == 1)
				{
					locals += 1;
					agent = player;
				}
				else if (slot == 2)
				{
					opponents += 1;
					cpu = player;
				}
			}
			if (locals != 1 || opponents != 1)
				return false;
			Remember(agent, cpu);
			return true;
		}

		/// <summary>
		/// When the match did not stamp slots, accept exactly two live fighters.
		/// The npc fighter is the CPU. Otherwise the higher player id is the CPU.
		/// </summary>
		bool BindPair(UnityEngine.SceneManagement.Scene active)
		{
			var live = new List<object>();
			foreach (var player in SceneComponents("PlayerInfo"))
			{
				if (Live(player, active, true))
					live.Add(player);
			}
			if (live.Count != 2)
				return false;
			object agent = live[0];
			object cpu = live[1];
			bool firstNpc = Fields.AsBool(Fields.Read(live[0], "npc"));
			bool secondNpc = Fields.AsBool(Fields.Read(live[1], "npc"));
			if (firstNpc != secondNpc)
			{
				agent = firstNpc ? live[1] : live[0];
				cpu = firstNpc ? live[0] : live[1];
			}
			else if (Slot(live[1]) < Slot(live[0]))
			{
				agent = live[1];
				cpu = live[0];
			}
			Remember(agent, cpu);
			return true;
		}

		static int Slot(object player)
		{
			return Mathf.RoundToInt(Fields.AsFloat(Fields.Read(player, "player")));
		}

		void Remember(object agent, object cpu)
		{
			Agent = agent;
			Cpu = cpu;
			AgentBasic = BasicOn(agent);
			CpuBasic = BasicOn(cpu);
		}

		static bool Live(object player, UnityEngine.SceneManagement.Scene active, bool activeSceneOnly)
		{
			var component = player as Component;
			if (component == null || component.gameObject == null)
				return false;
			if (!component.gameObject.scene.IsValid() || !component.gameObject.activeInHierarchy)
				return false;
			if (activeSceneOnly && component.gameObject.scene != active)
				return false;
			return true;
		}

		static string Describe(UnityEngine.SceneManagement.Scene active)
		{
			var text = "need_one_cpu";
			foreach (var player in SceneComponents("PlayerInfo"))
			{
				if (!Live(player, active, false))
					continue;
				int slot = Mathf.RoundToInt(Fields.AsFloat(Fields.Read(player, "player")));
				bool npc = Fields.AsBool(Fields.Read(player, "npc"));
				var id = Fields.Read(player, "characterID");
				text += " slot=" + slot + " npc=" + npc + " id=" + id;
			}
			return text;
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
