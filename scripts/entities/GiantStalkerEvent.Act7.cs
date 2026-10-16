using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// Act 7's giant, its wrongness (2026-10-16, STORY.md Rev 5): it "snaps its head and body round in the unnatural way it
/// will at the top of the last staircase", and the trees fall where it looks (<see cref="World.Act7TreeFalls"/> calls
/// <see cref="LookToward"/> as each one begins to creak). Stop-motion, as GiantRise's: its body jerks round toward the
/// point in a few held steps (never a smooth turn), its head with it, held there a few seconds, then jerked back to its
/// walk; and now and then, looking at nothing, its head snaps over on its side, holds, and snaps back. Its pace, stride,
/// rattle and lane are the crossing's own (unchanged: Dan, 2026-10-13).
/// </summary>
public partial class GiantStalkerEvent
{
	/// <summary>How long it holds its look at a point before it jerks back to its walk.</summary>
	[Export] public float LookHoldSeconds = 3.5f;
	/// <summary>Seconds between its idle head snaps (random in this range), and how long each is held.</summary>
	[Export] public Vector2 HeadSnapEvery = new(4f, 9f), HeadSnapHold = new(0.6f, 1.8f);

	/// <summary>For tests: how many times it has been turned to look somewhere (a falling tree), and its idle head snaps.</summary>
	public int Looks { get; private set; }
	public int HeadSnaps { get; private set; }

	private Vector3? _lookAt;
	private double _snapClock, _lookUntil, _nextSnap, _nextHeadSnap = 3.0;
	private float _snapYaw, _headRoll;

	/// <summary>It turns to look at <paramref name="world"/> (a tree about to fall). Nothing while it isn't out there.</summary>
	public void LookToward(Vector3 world)
	{
		if (Body == null) return;
		_lookAt = world;
		_lookUntil = _snapClock + LookHoldSeconds;
		Looks++;
	}

	/// <summary>The crossing's per-frame wrongness: the turn (a world yaw to put on its walking pose) and the head's snaps.</summary>
	private Basis Act7Snap(StalkerBody body, Vector3 tangent, float dt)
	{
		_snapClock += dt;
		float want = 0f;
		if (_lookAt is { } t && _snapClock < _lookUntil)
		{
			Vector3 to = t - body.GlobalPosition;
			to.Y = 0f;
			if (to.LengthSquared() > 1f)
			{
				to = to.Normalized();
				want = Mathf.Atan2(tangent.Z * to.X - tangent.X * to.Z, tangent.Dot(to));
				want = Mathf.Clamp(want, -2.4f, 2.4f);
			}
			body.LookTarget = t;
		}
		else if (_lookAt != null)
		{
			_lookAt = null;
			body.LookTarget = null;
		}
		// stop-motion: jumps toward the wanted turn, each held a moment
		if (_snapClock >= _nextSnap && Mathf.Abs(want - _snapYaw) > 0.03f)
		{
			_snapYaw += Mathf.Clamp(want - _snapYaw, -0.65f, 0.65f);
			_nextSnap = _snapClock + _rng.RandfRange(0.14f, 0.32f);
		}
		// the head over on its side, held, and back (when it isn't looking at something)
		if (_snapClock >= _nextHeadSnap)
		{
			bool over = _headRoll == 0f && _lookAt == null;
			_headRoll = over ? (_rng.Randf() < 0.5f ? -1f : 1f) * _rng.RandfRange(0.4f, 0.75f) : 0f;
			if (over) HeadSnaps++;
			_nextHeadSnap = _snapClock + (over ? _rng.RandfRange(HeadSnapHold.X, HeadSnapHold.Y) : _rng.RandfRange(HeadSnapEvery.X, HeadSnapEvery.Y));
			body.HeadJolt = over ? new Vector3(_rng.RandfRange(-0.12f, 0.12f), _rng.RandfRange(-0.2f, 0.2f), 0f) : Vector3.Zero;
		}
		body.HeadRoll = _lookAt != null ? Mathf.Clamp(_snapYaw * 0.3f, -0.4f, 0.4f) : _headRoll;
		return new Basis(Vector3.Up, _snapYaw);
	}
}
