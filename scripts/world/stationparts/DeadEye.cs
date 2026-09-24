using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// The dead eye from the basement drain: one of the lake thing's eyes, lid hanging slack, filmed over.
/// It never moves while it is being looked at. The moment the player's view leaves it, it turns in the
/// grate to face them again — so every time they look back, it is looking at them.
///
/// Once the iron door has asked for it (<see cref="Wandering"/>), it is not in the grate any more: it
/// moves about the dark basement between hiding places, but only while unwatched, and each move is a
/// small wet slap from wherever it has gone. It can only be taken while the player is looking right at
/// it from close by — look away on the way in and it is somewhere else.
/// </summary>
public partial class DeadEye : Node3D
{
	/// <summary>A cutscene is moving it: hands off.</summary>
	public bool Frozen;
	/// <summary>The hunt: it relocates between <see cref="Anchors"/> while unwatched.</summary>
	public bool Wandering { get; set; }
	public bool Taken { get; private set; }
	/// <summary>For tests: how many times it has moved on while the player looked away.</summary>
	public int Hops { get; private set; }
	/// <summary>Where it may hide (parent-local), each with the way it should rest.</summary>
	public List<Vector3> Anchors = new();
	public bool SeenNow { get; private set; }

	private Node3D _model;
	private Interactable _take;
	private float _unseen;
	private readonly RandomNumberGenerator _rng = new() { Seed = 4242 };
	private int _anchor = -1;

	public override void _Ready()
	{
		_model = StationProps.DeadEye(this, 0.14f);
		_take = new Interactable { Name = "Take", Prompt = "Take the eye", PickRadius = 0.3f, MaxDistance = 2.2f, Enabled = false };
		_take.Interacted += OnTaken;
		AddChild(_take);
	}

	public override void _Process(double delta)
	{
		if (Frozen || Taken || !Visible) return;
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		float dt = (float)delta;
		SeenNow = Seen(cam);
		if (!SeenNow)
		{
			_unseen += dt;
			// turn to face them (and settle, lid drooping, looking right at where they'll look back from)
			Vector3 to = cam.GlobalPosition - GlobalPosition;
			if (to.LengthSquared() > 0.01f) _model.GlobalBasis = Basis.LookingAt(to, Vector3.Up);
			if (Wandering && _unseen > 0.6f && Anchors.Count > 1 && _rng.Randf() < dt * 1.4f) Hop(cam);
		}
		else _unseen = 0f;
		_take.Enabled = Wandering && SeenNow && GlobalPosition.DistanceTo(cam.GlobalPosition) < 2.4f;
	}

	/// <summary>In the middle of the view and not behind anything.</summary>
	private bool Seen(Camera3D cam)
	{
		Vector3 to = GlobalPosition - cam.GlobalPosition;
		float dist = to.Length();
		if (dist > 20f) return false;
		float cos = (-cam.GlobalBasis.Z).Dot(to / Mathf.Max(dist, 0.001f));
		if (cos < Mathf.Cos(Mathf.DegToRad(cam.Fov * 0.5f * 0.85f))) return false;
		var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, GlobalPosition, 1u);
		if (StoryBeat.Player(this) is { } p) q.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	/// <summary>Somewhere else, out of the player's view, not too near them.</summary>
	private void Hop(Camera3D cam)
	{
		var parent = GetParent<Node3D>();
		for (int tries = 0; tries < 12; tries++)
		{
			int i = _rng.RandiRange(0, Anchors.Count - 1);
			if (i == _anchor) continue;
			Vector3 w = parent.ToGlobal(Anchors[i]);
			Vector3 to = w - cam.GlobalPosition;
			if (to.Length() < 2.5f) continue;
			if ((-cam.GlobalBasis.Z).Dot(to.Normalized()) > Mathf.Cos(Mathf.DegToRad(cam.Fov * 0.6f))) continue;   // they'd see it land
			_anchor = i;
			Position = Anchors[i];
			_unseen = 0f;
			Hops++;
			Slap();
			return;
		}
	}

	private void Slap()
	{
		string path = $"res://assets/audio/sfx/squelch_close_{_rng.RandiRange(1, 2):00}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Unnatural", VolumeDb = -8f, UnitSize = 3f, MaxDistance = 20f, PitchScale = _rng.RandfRange(0.8f, 1.0f) };
		AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}

	private void OnTaken(PlayerController player)
	{
		if (Taken || !Wandering) return;
		Taken = true;
		Visible = false;
		_take.Enabled = false;
		player?.Inventory?.TryPickup(ToolKind.DeadEye);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationEyeTaken);
		_ = StoryBeat.Caption(this, "It's warm. It is still looking at you.", 0.4f, 2.4f, 1f);
		GD.Print("[story] Act 13: the dead eye is taken");
	}

	/// <summary>For tests: put it in front of the camera, close, as a player who kept their eyes on it would find it.</summary>
	public void TestComeToLook() => _unseen = 0f;
}
