using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CarnageGym
{
	/// <summary>
	/// One-client NDJSON server. Requests are handed to the Unity thread.
	/// </summary>
	public sealed class IpcServer
	{
		readonly object _gate = new object();
		readonly ManualResetEvent _responseReady = new ManualResetEvent(false);
		TcpListener _listener;
		Thread _thread;
		volatile bool _running;
		string _pending;
		string _response;

		/// <summary>
		/// Bind 127.0.0.1:47831. Fails when the port is already taken.
		/// </summary>
		public void Start()
		{
			_listener = new TcpListener(IPAddress.Loopback, 47831);
			_listener.Start();
			_running = true;
			_thread = new Thread(AcceptLoop);
			_thread.IsBackground = true;
			_thread.Name = "CarnageGym.Ipc";
			_thread.Start();
		}

		/// <summary>
		/// Stop listening. In-flight clients drop on the next read.
		/// </summary>
		public void Stop()
		{
			_running = false;
			try
			{
				if (_listener != null)
					_listener.Stop();
			}
			catch (Exception)
			{
				// The listener is already closed.
			}
			_responseReady.Set();
		}

		/// <summary>
		/// Take the next request line, or null when the trainer is idle.
		/// </summary>
		public string TakeRequest()
		{
			lock (_gate)
			{
				var line = _pending;
				_pending = null;
				return line;
			}
		}

		/// <summary>
		/// Unblock the socket thread with one response line.
		/// </summary>
		public void Publish(string line)
		{
			lock (_gate)
			{
				_response = line;
			}
			_responseReady.Set();
		}

		void AcceptLoop()
		{
			while (_running)
			{
				TcpClient client = null;
				try
				{
					client = _listener.AcceptTcpClient();
				}
				catch (Exception)
				{
					if (!_running)
						break;
					continue;
				}
				Serve(client);
			}
		}

		void Serve(TcpClient client)
		{
			client.NoDelay = true;
			using (client)
			using (var stream = client.GetStream())
			{
				var buffer = new byte[8192];
				var text = new StringBuilder();
				while (_running && client.Connected)
				{
					int count;
					try
					{
						count = stream.Read(buffer, 0, buffer.Length);
					}
					catch (Exception)
					{
						break;
					}
					if (count <= 0)
						break;
					text.Append(Encoding.UTF8.GetString(buffer, 0, count));
					while (true)
					{
						var all = text.ToString();
						int newline = all.IndexOf('\n');
						if (newline < 0)
							break;
						var line = all.Substring(0, newline).Trim();
						text.Remove(0, newline + 1);
						if (line.Length == 0)
							continue;
						_responseReady.Reset();
						lock (_gate)
						{
							_pending = line;
							_response = null;
						}
						if (!_responseReady.WaitOne(120000))
						{
							var timeout = Protocol.Error("timeout");
							var bytes = Encoding.UTF8.GetBytes(timeout + "\n");
							stream.Write(bytes, 0, bytes.Length);
							continue;
						}
						string response;
						lock (_gate)
						{
							response = _response ?? Protocol.Error("timeout");
						}
						var payload = Encoding.UTF8.GetBytes(response + "\n");
						stream.Write(payload, 0, payload.Length);
					}
				}
			}
		}
	}

	/// <summary>
	/// Tiny reader and writer for the flat request and response objects.
	/// </summary>
	public static class Protocol
	{
		/// <summary>
		/// A parsed trainer command.
		/// </summary>
		public sealed class Command
		{
			/// <summary>ping, reset, or step.</summary>
			public string Name;
			/// <summary>Step action index. Zero when absent.</summary>
			public int Action;
			/// <summary>True when the action field was present.</summary>
			public bool HasAction;
			/// <summary>Requested CPU level.</summary>
			public int CpuLevel;
			/// <summary>Requested time scale.</summary>
			public float TimeScale = 1f;
			/// <summary>Set when the line was not a command object.</summary>
			public bool BadJson;
		}

		/// <summary>
		/// Parse one request line.
		/// </summary>
		public static Command Parse(string line)
		{
			var command = new Command();
			if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{"))
			{
				command.BadJson = true;
				return command;
			}
			command.Name = StringField(line, "cmd");
			if (command.Name == null)
			{
				command.BadJson = true;
				return command;
			}
			double number;
			if (TryNumber(line, "action", out number))
			{
				command.HasAction = true;
				command.Action = (int)number;
			}
			if (TryNumber(line, "cpu_level", out number))
				command.CpuLevel = (int)number;
			if (TryNumber(line, "time_scale", out number))
				command.TimeScale = (float)number;
			return command;
		}

		/// <summary>
		/// Failure response. The observation is 29 zeros and the episode is done.
		/// </summary>
		public static string Error(string error)
		{
			return Ok(false, Zeros(), true, error, null, 0, 0, 0, 0f, 0f, 0f, 0f);
		}

		/// <summary>
		/// Success or failure response. Reward is always 0; Python scores the transition.
		/// </summary>
		public static string Ok(
			bool ok,
			float[] obs,
			bool done,
			string error,
			string winner,
			int cpuLevel,
			int cpuMin,
			int cpuMax,
			float timeScale,
			float selfHealth,
			float oppHealth,
			float gameSeconds)
		{
			var builder = new StringBuilder();
			builder.Append("{\"ok\":").Append(ok ? "true" : "false");
			if (!ok && error != null)
				builder.Append(",\"error\":\"").Append(error).Append("\"");
			builder.Append(",\"obs\":[");
			for (int i = 0; i < obs.Length; i++)
			{
				if (i > 0)
					builder.Append(',');
				builder.Append(obs[i].ToString("R", CultureInfo.InvariantCulture));
			}
			builder.Append("],\"reward\":0");
			builder.Append(",\"done\":").Append(done ? "true" : "false");
			builder.Append(",\"info\":{");
			builder.Append("\"cpu_level\":").Append(cpuLevel.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"cpu_min\":").Append(cpuMin.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"cpu_max\":").Append(cpuMax.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"time_scale\":").Append(timeScale.ToString("R", CultureInfo.InvariantCulture));
			builder.Append(",\"self_health\":").Append(selfHealth.ToString("R", CultureInfo.InvariantCulture));
			builder.Append(",\"opp_health\":").Append(oppHealth.ToString("R", CultureInfo.InvariantCulture));
			builder.Append(",\"winner\":");
			if (winner == null)
				builder.Append("null");
			else
				builder.Append("\"").Append(winner).Append("\"");
			builder.Append(",\"game_seconds\":").Append(gameSeconds.ToString("R", CultureInfo.InvariantCulture));
			builder.Append("}}");
			return builder.ToString();
		}

		static float[] Zeros()
		{
			return new float[29];
		}

		static string StringField(string json, string key)
		{
			var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : null;
		}

		static bool TryNumber(string json, string key, out double number)
		{
			var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
			if (!match.Success)
			{
				number = 0;
				return false;
			}
			return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}
	}
}
