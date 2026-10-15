using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// `-- --autotest --walkthrough`: the story test made watchable. Every place the test would teleport to it walks to
/// instead (along the trail where both ends are near it, straight across otherwise, sidestepping when blocked), and
/// the game runs at its real speed (no 3x on the cutscenes and waits; the waits' real-time limits stretch to match).
/// A walk that can't get there (another floor, a wall in the way) still hops, and says so in the log.
/// </summary>
public partial class StoryTest
{
	private static readonly bool Walkthrough = OS.GetCmdlineUserArgs().Contains("--walkthrough");

	/// <summary>Walks to <paramref name="at"/> as a player would; false when it couldn't get there (the caller hops).</summary>
	private async Task<bool> WalkThere(Vector3 at, Vector3 faceToward, CancellationToken ct)
	{
		Vector3 from = _player.GlobalPosition;
		float flat = Flat(at).DistanceTo(Flat(from));
		if (flat < 1.0f && Mathf.Abs(at.Y - from.Y) < 1.5f) return Face(faceToward);
		// another floor (the stairwell, the crypt, the maze 160 m down): no straight walk gets there
		if (Mathf.Abs(at.Y - from.Y) > 6f && Mathf.Abs(at.Y - from.Y) > flat * 0.6f) return Hop(at, "another floor");
		ulong deadline = Time.GetTicksMsec() + (ulong)(Mathf.Max(30f, flat * 0.6f) * 1000f);
		bool Late() => Time.GetTicksMsec() > deadline;

		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null
			&& terrain.TrailDistance(from.X, from.Z, out float s0) < 15f
			&& terrain.TrailDistance(at.X, at.Z, out float s1) < 15f
			&& Mathf.Abs(s1 - s0) > 12f)
		{
			float step = s1 > s0 ? 6f : -6f;
			for (float s = s0 + step; step > 0 ? s < s1 : s > s1; s += step)
				if (!await WalkTo(terrain.TrailPoint(s, out _), 2.5f, ct, Late, giveUp: 5f) || Late()) break;
		}
		if (!Late()) await WalkTo(at, 0.8f, ct, Late, giveUp: 5f);
		float left = Flat(_player.GlobalPosition).DistanceTo(Flat(at));
		if (left > 2.5f || Mathf.Abs(_player.GlobalPosition.Y - at.Y) > 2.5f) return Hop(at, $"stuck {left:0.0} m short");
		GD.Print($"[walkthrough] walked {flat:0} m to {at.Round()}");
		return Face(faceToward);
	}

	private bool Face(Vector3 faceToward)
	{
		var d = Flat(faceToward - _player.GlobalPosition);
		if (d.LengthSquared() > 0.01f) _player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_lastPos = _player.GlobalPosition;
		return true;
	}

	private bool Hop(Vector3 at, string why)
	{
		GD.Print($"[walkthrough] hopped to {at.Round()} ({why}) from {_player.GlobalPosition.Round()}");
		return false;
	}
}

public partial class StoryTest
{
	private static readonly bool SoundLog = OS.GetCmdlineUserArgs().Contains("--soundlog");
	private readonly System.Collections.Generic.HashSet<ulong> _soundsOn = new();
	private double _soundScan;

	/// <summary>`--soundlog`: every sound that starts, with what it is, its bus, the node playing it and how far off.</summary>
	private void LogSounds(double delta)
	{
		_soundScan -= delta;
		if (_soundScan > 0) return;
		_soundScan = 0.05;
		var now = new System.Collections.Generic.HashSet<ulong>();
		var stack = new System.Collections.Generic.Stack<Node>();
		stack.Push(GetTree().Root);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var c in n.GetChildren()) stack.Push(c);
			string path = null, bus = null; bool playing = false; float db = 0; Vector3? at = null;
			if (n is AudioStreamPlayer3D p3) { playing = p3.Playing; path = p3.Stream?.ResourcePath; bus = p3.Bus; db = p3.VolumeDb; at = p3.GlobalPosition; }
			else if (n is AudioStreamPlayer p2) { playing = p2.Playing; path = p2.Stream?.ResourcePath; bus = p2.Bus; db = p2.VolumeDb; }
			if (!playing) continue;
			ulong id = n.GetInstanceId();
			now.Add(id);
			if (_soundsOn.Contains(id)) continue;
			float d = at.HasValue && _player != null ? at.Value.DistanceTo(_player.GlobalPosition) : -1f;
			GD.Print($"[sound] {Time.GetTicksMsec() / 1000.0:0.00} {System.IO.Path.GetFileNameWithoutExtension(path ?? "?")} bus={bus} db={db:0} dist={d:0.0} by={n.GetParent()?.Name}/{n.Name}");
		}
		_soundsOn.Clear();
		foreach (var id in now) _soundsOn.Add(id);
	}
}
