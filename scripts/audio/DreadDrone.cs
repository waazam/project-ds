using Godot;
using ProjectDS.Entities;

namespace ProjectDS.Audio;

/// <summary>
/// The dread (2026-10-10): a low drone, two notes not quite agreeing, that's heard only when something is near you and
/// you can't see it: it swells as it comes closer unseen, and falls away the moment you've looked at it (or it's gone).
/// Never a sting, never sudden: in over seconds, out over seconds.
/// The things it listens for put themselves in the group <see cref="Group"/>: the wendigo in the maze, the crawler, the
/// thing that follows you through the woods (only while it's really out there at its tree), the pursuer by the lake.
/// </summary>
public partial class DreadDrone : Node
{
	public const string Group = "dread_source";
	/// <summary>How near (metres) something has to be for it to be felt at all.</summary>
	public const float Reach = 20f;
	public float Level { get; private set; }
	public float Target { get; private set; }
	private AudioStreamPlayer _p;
	private float _poll;

	public override void _Ready()
	{
		if (!ResourceLoader.Exists("res://assets/audio/ambient/dread_drone_loop.wav")) return;
		var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/dread_drone_loop.wav").Duplicate();
		wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
		_p = new AudioStreamPlayer { Name = "Drone", Stream = wav, Bus = "Unnatural", VolumeDb = -80f };
		AddChild(_p);
	}

	public override void _Process(double delta)
	{
		if (_p == null) return;
		float dt = (float)delta;
		_poll -= dt;
		if (_poll <= 0f) { _poll = 0.25f; Target = Measure(); }
		// in over about four seconds, out over about three
		Level = Mathf.MoveToward(Level, Target, dt * (Target > Level ? 0.25f : 0.33f));
		_p.VolumeDb = Level < 0.01f ? -80f : Mathf.LinearToDb(Mathf.Pow(Level, 1.5f)) - 8f;
		if (Level > 0.01f && !_p.Playing) _p.Play();
		else if (Level <= 0.01f && _p.Playing) _p.Stop();
	}

	/// <summary>The nearest thing in reach that isn't in sight: 0 nothing .. 1 on top of you.</summary>
	private float Measure()
	{
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return 0f;
		float best = 0f;
		foreach (var n in GetTree().GetNodesInGroup(Group))
		{
			if (n is not Node3D s || !s.IsVisibleInTree()) continue;
			if (n is Stalker st && st.Current is not (Stalker.State.Peeking or Stalker.State.Vanishing)) continue;
			if (n is WendigoHunterBodyTag tag && !tag.Live) continue;
			var at = s.GlobalPosition + Vector3.Up * 1.2f;
			float d = at.DistanceTo(cam.GlobalPosition);
			if (d > Reach || (n is Stalker && d > 14f)) continue;
			if (InSight(cam, at)) continue;
			best = Mathf.Max(best, 1f - d / Reach);
		}
		return best;
	}

	private bool InSight(Camera3D cam, Vector3 at)
	{
		if (!cam.IsPositionInFrustum(at)) return false;
		var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, at, 1);
		return cam.GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}
}

/// <summary>Marks the maze wendigo's body as a dread source while it's alive and hunting.</summary>
public partial class WendigoHunterBodyTag : Node3D
{
	public WendigoHunter Hunter;
	public bool Live => Hunter != null && IsInstanceValid(Hunter) && !Hunter.Dead && Hunter.State != WendigoHunter.Mode.Off;
}
