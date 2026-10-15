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
