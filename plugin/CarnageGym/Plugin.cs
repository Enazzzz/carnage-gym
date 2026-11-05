using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CarnageGym
{
	/// <summary>
	/// Local training bridge. It listens on 127.0.0.1:47831 and never touches online input.
	/// </summary>
	[BepInPlugin(Plugin.Id, Plugin.DisplayName, Plugin.Version)]
	public sealed class Plugin : BaseUnityPlugin
	{
		/// <summary>BepInEx plugin id.</summary>
		public const string Id = "local.carnagegym";

		/// <summary>BepInEx display name.</summary>
		public const string DisplayName = "CarnageGym";

		/// <summary>Plugin version.</summary>
		public const string Version = "0.1.0";

		readonly MatchDriver _match = new MatchDriver();
		readonly ObservationBuilder _eyes = new ObservationBuilder();
		readonly IpcServer _ipc = new IpcServer();

		bool _socketOpen;
		bool _working;
		int _holdLeft;
		int _resetFrames;
		int _decisions;
		bool _inRound;

		/// <summary>
		/// Check the game's fields, patch local input, and open the socket.
		/// </summary>
		void Awake()
		{
			if (!RequiredFields())
			{
				Logger.LogError("CarnageGym did not open the socket because a required field is missing.");
				return;
			}
			var harmony = new Harmony(Id);
			harmony.PatchAll(typeof(Plugin).Assembly);
			try
			{
				_ipc.Start();
				_socketOpen = true;
				Logger.LogInfo("CarnageGym listening on 127.0.0.1:47831");
			}
			catch (System.Exception exc)
			{
				Logger.LogError("CarnageGym could not bind 127.0.0.1:47831: " + exc.Message);
			}
		}

		/// <summary>
		/// Advance reset and button holds, then answer one trainer request.
		/// </summary>
		void Update()
		{
			if (!_socketOpen)
				return;
			if (_working)
			{
				var pending = Continue();
				if (pending != null)
				{
					_working = false;
					_ipc.Publish(pending);
				}
				return;
			}
			var line = _ipc.TakeRequest();
			if (line == null)
				return;
			var immediate = Begin(line);
			if (immediate != null)
			{
				_ipc.Publish(immediate);
				return;
			}
			_working = true;
			var sameFrame = Continue();
			if (sameFrame != null)
			{
				_working = false;
				_ipc.Publish(sameFrame);
			}
		}

		/// <summary>
		/// Close the listener when the game unloads the plugin.
		/// </summary>
		void OnDestroy()
		{
			if (_socketOpen)
				_ipc.Stop();
		}

		string Begin(string line)
		{
			var command = Protocol.Parse(line);
			if (command.BadJson)
				return Protocol.Error("bad_json");
			if (command.Name == "ping")
				return Protocol.Ok(true, new float[29], false, null, null, 0, _match.CpuMin, _match.CpuMax, 1f, 0f, 0f, 0f);
			if (command.Name == "reset")
			{
				var error = _match.BeginReset(command.CpuLevel, command.TimeScale);
				if (error != null)
					return Protocol.Error(error);
				_eyes.ResetHistory();
				_resetFrames = 0;
				_holdLeft = 0;
				_decisions = 0;
				_inRound = false;
				InputForcer.Release();
				return null;
			}
			if (command.Name == "step")
			{
				if (!_inRound || _match.Agent == null)
					return Protocol.Error("players_not_ready");
				if (!command.HasAction || !ActionCodec.Decode(
					command.Action,
					out var moveX,
					out var moveY,
					out var attack,
					out var special,
					out var jump,
					out var super))
					return Protocol.Error("bad_action");
				InputForcer.SetPacket(moveX, moveY, attack, special, jump, super);
				_holdLeft = 3;
				return null;
			}
			return Protocol.Error("bad_json");
		}

		string Continue()
		{
			if (_holdLeft > 0)
			{
				InputForcer.Apply(_match.AgentBasic);
				_holdLeft -= 1;
				if (_holdLeft > 0)
					return null;
				InputForcer.Release();
				_decisions += 1;
				return FinishStep();
			}
			_resetFrames += 1;
			// Give the game's own FixedUpdate a couple of frames before trusting full health.
			if (_resetFrames < 2)
				return null;
			if (_match.HealthRecovered())
			{
				_inRound = true;
				_eyes.ResetHistory();
				return Live(false, null);
			}
			if (_resetFrames >= 600)
				return Protocol.Error("reset_timeout");
			return null;
		}

		string FinishStep()
		{
			var obs = _eyes.Build(_match.Agent, _match.Cpu, _match.AgentBasic, _match.CpuBasic);
			bool agentDown = ObservationBuilder.Defeated(_match.Agent);
			bool cpuDown = ObservationBuilder.Defeated(_match.Cpu);
			bool timeout = _decisions >= 900;
			string winner = null;
			bool done = false;
			if (_eyes.ForceDraw)
			{
				done = true;
				winner = "draw";
			}
			else if (agentDown || cpuDown)
			{
				done = true;
				if (agentDown && cpuDown)
					winner = "draw";
				else if (cpuDown)
					winner = "agent";
				else
					winner = "cpu";
			}
			else if (timeout)
			{
				done = true;
				winner = "timeout";
			}
			if (done)
				_inRound = false;
			return Live(done, winner, obs);
		}

		string Live(bool done, string winner, float[] obs = null)
		{
			if (obs == null && _match.Agent != null && _match.Cpu != null)
				obs = _eyes.Build(_match.Agent, _match.Cpu, _match.AgentBasic, _match.CpuBasic);
			if (obs == null)
				obs = new float[29];
			float selfHealth = _match.Agent == null ? 0f : ObservationBuilder.Health(_match.Agent);
			float oppHealth = _match.Cpu == null ? 0f : ObservationBuilder.Health(_match.Cpu);
			float seconds = _decisions * 3f / 60f;
			return Protocol.Ok(
				true,
				obs,
				done,
				null,
				winner,
				_match.Cpu == null ? 0 : Mathf.RoundToInt(Fields.AsFloat(Fields.Read(_match.Cpu, "cpuLevel"))),
				_match.CpuMin,
				_match.CpuMax,
				_match.Agent == null ? 1f : _match.TimeScale,
				selfHealth,
				oppHealth,
				seconds);
		}

		bool RequiredFields()
		{
			string[] playerFields = {
				"health", "maxHealth", "superCharge", "superCost", "superReady", "hit", "hitting",
				"facing", "movementBound", "cpuLevel", "npc", "characterID", "zeroHealth"
			};
			foreach (var field in playerFields)
			{
				if (!Fields.Exists("PlayerInfo", field))
				{
					Logger.LogError("Missing field PlayerInfo." + field);
					return false;
				}
			}
			string[] basicFields = { "airborn", "dashing", "running", "inputs" };
			foreach (var field in basicFields)
			{
				if (!Fields.Exists("BasicCharacterScript", field))
				{
					Logger.LogError("Missing field BasicCharacterScript." + field);
					return false;
				}
			}
			if (AccessTools.TypeByName("trainingModeReset") == null)
			{
				Logger.LogError("Missing type trainingModeReset");
				return false;
			}
			return true;
		}
	}
}
