using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CarnageGym.Tests
{
	/// <summary>
	/// Locks the C# decoder to the Python fixture.
	/// </summary>
	public class ActionCodecTests
	{
		/// <summary>
		/// Walk parents until the repo fixture is found.
		/// </summary>
		static string FixturePath()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "actions.json");
				if (File.Exists(candidate))
					return candidate;
				dir = dir.Parent;
			}
			throw new FileNotFoundException("tests/fixtures/actions.json");
		}

		/// <summary>
		/// Action 0, 22, and 53 are the named examples in the spec.
		/// </summary>
		[Fact]
		public void NamedExamples()
		{
			Assert.True(ActionCodec.Decode(0, out var x, out var y, out var attack, out var special, out var jump, out var super));
			Assert.Equal(0, x);
			Assert.Equal(0, y);
			Assert.False(attack);
			Assert.False(special);
			Assert.False(jump);
			Assert.False(super);

			Assert.True(ActionCodec.Decode(22, out x, out y, out attack, out special, out jump, out super));
			Assert.Equal(0, x);
			Assert.Equal(1, y);
			Assert.True(attack);
			Assert.True(jump);
			Assert.False(special);
			Assert.False(super);

			Assert.True(ActionCodec.Decode(53, out x, out y, out attack, out special, out jump, out super));
			Assert.Equal(1, x);
			Assert.Equal(-1, y);
			Assert.True(super);
			Assert.False(attack);
			Assert.False(jump);
		}

		/// <summary>
		/// Out of range is a bad action and a neutral packet.
		/// </summary>
		[Fact]
		public void RejectsOutOfRange()
		{
			Assert.False(ActionCodec.Decode(-1, out _, out _, out _, out _, out _, out _));
			Assert.False(ActionCodec.Decode(54, out _, out _, out _, out _, out _, out _));
		}

		/// <summary>
		/// Every fixture row matches the decoder.
		/// </summary>
		[Fact]
		public void MatchesPythonFixture()
		{
			var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(FixturePath()));
			Assert.Equal(54, rows.Length);
			foreach (var row in rows)
			{
				Assert.True(ActionCodec.Decode(row.action, out var x, out var y, out var attack, out var special, out var jump, out var super));
				Assert.Equal(row.move_x, x);
				Assert.Equal(row.move_y, y);
				Assert.Equal(row.attack, attack);
				Assert.Equal(row.special, special);
				Assert.Equal(row.jump, jump);
				Assert.Equal(row.super, super);
			}
		}

		class Row
		{
			public int action { get; set; }
			public int move_x { get; set; }
			public int move_y { get; set; }
			public bool attack { get; set; }
			public bool special { get; set; }
			public bool jump { get; set; }
			public bool super { get; set; }
		}
	}
}
