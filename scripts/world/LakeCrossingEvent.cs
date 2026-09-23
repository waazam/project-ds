using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 12: the rowboat crossing. A sibling of <see cref="Lake"/>, which builds the shore, the
/// water and the far station; this class owns the boat itself and the whole story beat once the
/// player boards it (E, <see cref="Interactable"/>).
///
/// The crossing is on-rails but player-paced: the body's physics is frozen (as in
/// <see cref="FirstClimbEvent"/>'s glide) so nothing but the paddling below moves the boat, but
/// <see cref="PlayerInput"/> keeps running, so the mouse is free to look around throughout — the
/// player reads as sitting in a boat, not a passenger with a bag over their head. Paddling is read
/// straight off <see cref="PlayerInput.Move"/>.X (bound to A/D by default), alternating sides: a
/// stroke only counts when it switches side from the last one counted, so holding one key does
/// nothing and the player has to actually row.
///
/// Sequence: board (a short glide from the shore into the seat) -&gt; paddle to the breach point ->
/// the creature surfaces (a <see cref="LakeCreature"/>, a foghorn-like blast, a red flash, the
/// water shader's wave_intensity ramps up) -&gt; paddle again, fighting a current that drags progress
/// back unless the player keeps alternating fast -&gt; land at the far shore (control comes back) -&gt;
/// walking into the rescue station's doorway reaches checkpoint 10 and rolls
/// <see cref="Act11Ending.Credits"/>.
/// </summary>
public partial class LakeCrossingEvent : Node3D
{
	[Export] public NodePath LakePath = "../Lake";
	[Export] public float BoardSeconds = 1.6f;
	[Export] public float LandSeconds = 1.8f;
	/// <summary>Metres of progress a single alternating paddle stroke adds.</summary>
	[Export] public float StrokeProgress = 3.4f;
	/// <summary>Fraction (0..1) of the crossing where the creature breaches.</summary>
	[Export] public float BreachAtFraction = 0.48f;
	[Export] public double BreachHoldSeconds = 8.0;
	/// <summary>Metres per second the current drags the boat back once the water turns choppy, whenever
	/// the player isn't out-pacing it with alternating strokes.</summary>
	[Export] public float CurrentPushbackPerSecond = 2.1f;
	[Export] public float PaddleSideThreshold = 0.45f;

	// ---- test hooks (mirrors the public bools other story beats expose) ----
	public bool Boarded { get; private set; }
	public bool Paddling { get; private set; }
	public bool InBreach { get; private set; }
	public bool InCurrent { get; private set; }
	public bool Landed { get; private set; }
	public bool Arrived { get; private set; }
	public int StrokeCount { get; private set; }
	/// <summary>0..1 across the whole crossing.</summary>
	public float Progress { get; private set; }
	public LakeCreature LastCreature { get; private set; }
	/// <summary>For tests: the "get in the boat" prompt.</summary>
	public Interactable BoardPrompt => _boardPrompt;

	private Lake _lake;
	private Node3D _boat;
	private Interactable _boardPrompt;
	private ColorRect _flash;
	private bool _running, _arrived;
	private bool? _lastStrokeWasLeft;
	private bool _leftHeld, _rightHeld;
	private float _totalDistance;
	/// <summary>Set while the player's own body should follow the boat's seat each frame (paddling).</summary>
	private PlayerController _riding;

	public override void _Ready()
	{
		Callable.From(Setup).CallDeferred();
	}

	private void Setup()
	{
		_lake = GetNodeOrNull<Lake>(LakePath);
		if (_lake == null) { GD.PushWarning("LakeCrossingEvent: no Lake sibling found"); return; }
		_totalDistance = _lake.NearDockWorld.DistanceTo(_lake.FarDockWorld);
		BuildBoat();
		BuildFlash();

		_boardPrompt = new Interactable
		{
			Name = "BoardBoat",
			Prompt = "Get in the boat",
			PickRadius = 1.4f,
			MaxDistance = 3.2f,
			Position = new Vector3(0, 0.5f, 0.3f),
		};
		_boardPrompt.Interacted += OnBoarded;
		_boat.AddChild(_boardPrompt);

		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(5f, 3f, 5f) },
			ToLocal(_lake.StationDoorWorld), OnArrival, "LakeArrival");
	}

	// ------------------------------------------------------------------ the boat

	/// <summary>A small open wooden rowboat: flat bottom, flared side planks, blunt bow and stern,
	/// a bench seat, and two oars resting across the gunwales. Docked at the near shore, bow toward
	/// the far side.</summary>
	private void BuildBoat()
	{
		_boat = new Node3D { Name = "Rowboat" };
		AddChild(_boat);
		_boat.GlobalPosition = _lake.NearDockWorld;
		LookAtFar(_boat);

		const float len = 3.6f, halfLen = len * 0.5f, width = 1.25f, halfW = width * 0.5f, depth = 0.42f;
		var k = new MeshKit();
		var wood = PropTextures.DeckMat;
		k.Mat(wood);
		k.Color = new Color(0.48f, 0.36f, 0.26f);
		BuildKit.Box(k, new Vector3(0, -depth * 0.5f, 0), new Vector3(width * 0.8f, 0.08f, len * 0.94f), 1.2f);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * (halfW - 0.02f), -depth * 0.25f, 0), new Vector3(0.06f, depth, len * 0.9f), 1.2f, BuildKit.Face.None,
				new Basis(Vector3.Forward, s * 0.18f));
		// blunt bow/stern walls, angled in slightly
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(0, -depth * 0.25f, s * (halfLen - 0.05f)), new Vector3(width * 0.7f, depth, 0.06f), 1.2f, BuildKit.Face.None,
				new Basis(Vector3.Right, -s * 0.22f));
		// a bench seat, roughly amidships-aft (where the rower sits)
		k.Color = new Color(0.5f, 0.4f, 0.3f);
		BuildKit.Box(k, new Vector3(0, 0.05f, halfLen * 0.25f), new Vector3(width * 0.72f, 0.05f, 0.22f), 1.4f);
		// two oars, resting across the gunwales
		var frame = PropTextures.PostMat;
		k.Mat(frame);
		k.Color = new Color(0.42f, 0.35f, 0.26f);
		foreach (int s in new[] { -1, 1 })
		{
			Vector3 a = new(s * (halfW + 0.35f), 0.16f, halfLen * 0.1f), b = new(s * 0.08f, 0.1f, -halfLen * 0.55f);
			k.Cylinder(a, b, 0.03f, 0.045f, 6, true);
			k.Cylinder(b, b + (b - a).Normalized() * 0.5f, 0.09f, 0.005f, 5, true);   // the blade, tapering to nothing
		}
		k.Color = Colors.White;
		k.CommitTo(_boat, "BoatMesh", true);

		var body = new StaticBody3D { Name = "BoatBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		_boat.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -depth * 0.3f, 0), Shape = new BoxShape3D { Size = new Vector3(width, 0.2f, len) } });
	}

	private void LookAtFar(Node3D n)
	{
		Vector3 dir = _lake.FarDockWorld - _lake.NearDockWorld; dir.Y = 0;
		if (dir.LengthSquared() < 0.01f) return;
		n.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z) + Mathf.Pi, 0);
	}

	/// <summary>Where the player sits, in the boat's own local space (a hair above the seat).</summary>
	private static readonly Vector3 SeatLocal = new(0, 0.32f, 0.55f);

	private void BuildFlash()
	{
		var layer = new CanvasLayer { Layer = 25 };
		Cutscene.SceneRoot(this).AddChild(layer);
		_flash = new ColorRect { Color = new Color(0.5f, 0.02f, 0.02f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_flash);
	}

	// ------------------------------------------------------------------ boarding -> crossing -> landing

	private void OnBoarded(PlayerController player)
	{
		if (_running) return;
		_running = true;
		_boardPrompt.Enabled = false;
		_ = Cutscene.Run(this, ct => RunSequence(player, ct), freezeBody: true);
	}

	private async Task RunSequence(PlayerController player, CancellationToken ct)
	{
		_riding = player;
		try
		{
			await Board(player, ct);
			Boarded = true;
			Paddling = true;
			await Cross(player, 0f, BreachAtFraction, ct);
			Paddling = false;
			await Breach(player, ct);
			Paddling = true;
			InCurrent = true;
			await Cross(player, BreachAtFraction, 1f, ct);
			InCurrent = false;
			Paddling = false;
			await Land(player, ct);
			Landed = true;
			GD.Print("[story] Act 12: crossed the lake; the station waits");
		}
		finally
		{
			Paddling = false;
			InCurrent = false;
			_riding = null;
		}
	}

	/// <summary>A short glide from the shore into the seat: the same frozen-body, look-free
	/// mechanism as <see cref="FirstClimbEvent.FloatUp"/>, just for stepping into a boat rather
	/// than up a flight.</summary>
	private async Task Board(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		Vector3 dest = _boat.ToGlobal(SeatLocal);
		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, BoardSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, tween, ct);
	}

	private async Task Land(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", _lake.FarDockWorld, LandSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, tween, ct);
	}

	// ------------------------------------------------------------------ paddling

	/// <summary>Resets the alternation tracker: the next stroke on either side counts.</summary>
	private void ResetStrokeTracking() { _lastStrokeWasLeft = null; _leftHeld = false; _rightHeld = false; }

	/// <summary>Reads one frame of paddling. Returns the stroke progress this frame (0 unless a new,
	/// correctly-alternating side just went down).</summary>
	private float ReadStroke(PlayerController player)
	{
		float mx = player.PlayerInput.Move.X;
		bool leftNow = mx < -PaddleSideThreshold, rightNow = mx > PaddleSideThreshold;
		bool leftEdge = leftNow && !_leftHeld, rightEdge = rightNow && !_rightHeld;
		_leftHeld = leftNow; _rightHeld = rightNow;
		if (leftEdge && _lastStrokeWasLeft != true) { _lastStrokeWasLeft = true; StrokeCount++; return StrokeProgress; }
		if (rightEdge && _lastStrokeWasLeft != false) { _lastStrokeWasLeft = false; StrokeCount++; return StrokeProgress; }
		return 0f;
	}

	/// <summary>Paddles the boat from <paramref name="fromFrac"/> to <paramref name="toFrac"/> of the
	/// crossing. Past <see cref="BreachAtFraction"/> (the current phase) progress also drains on its
	/// own each second unless the player is out-pacing it with real strokes, and never falls back
	/// below where this leg started.</summary>
	private async Task Cross(PlayerController player, float fromFrac, float toFrac, CancellationToken ct)
	{
		ResetStrokeTracking();
		bool current = fromFrac >= BreachAtFraction - 0.001f;
		float dist = Mathf.Max(0.01f, (toFrac - fromFrac) * _totalDistance);
		float floor = fromFrac * _totalDistance;
		float local = 0f;
		while (local < dist)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			local += ReadStroke(player);
			if (current) local -= CurrentPushbackPerSecond * dt;
			local = Mathf.Clamp(local, 0f, dist);
			Progress = Mathf.Clamp((floor + local) / Mathf.Max(_totalDistance, 0.01f), 0f, 1f);
			PositionBoat(Progress, current);
		}
	}

	private void PositionBoat(float t, bool rough)
	{
		Vector3 pos = _lake.NearDockWorld.Lerp(_lake.FarDockWorld, t);
		double time = Time.GetTicksMsec() / 1000.0;
		float bobAmp = rough ? 0.14f : 0.05f;
		float bobHz = rough ? 1.6f : 0.6f;
		pos.Y += Mathf.Sin((float)time * bobHz) * bobAmp;
		_boat.GlobalPosition = pos;
		var rot = _boat.Rotation;
		rot.Z = Mathf.Sin((float)time * bobHz * 1.3f) * (rough ? 0.08f : 0.02f);
		rot.X = Mathf.Sin((float)time * bobHz * 0.8f + 1f) * (rough ? 0.05f : 0.015f);
		_boat.Rotation = rot;
		if (_riding != null && GodotObject.IsInstanceValid(_riding))
			_riding.GlobalPosition = _boat.ToGlobal(SeatLocal);
	}

	// ------------------------------------------------------------------ the breach

	private async Task Breach(PlayerController player, CancellationToken ct)
	{
		InBreach = true;
		Vector3 center = _boat.GlobalPosition + (_lake.FarDockWorld - _lake.NearDockWorld).Normalized() * 6f;

		var creature = new LakeCreature();
		Cutscene.SceneRoot(this).AddChild(creature);
		creature.Breach(center, BreachHoldSeconds);
		LastCreature = creature;
		PlayFoghorn(center);

		var waveTween = CreateTween();
		waveTween.TweenMethod(Callable.From<float>(v => _lake.WaterMaterial?.SetShaderParameter("wave_intensity", v)), 0f, 1f, 2.2f);

		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		_flash.Color = new Color(0.5f, 0.02f, 0.02f, reduce ? 0.16f : 0.7f);
		var flashTween = CreateTween();
		flashTween.TweenProperty(_flash, "color:a", 0f, reduce ? 0.7f : 1.3f);

		await StoryBeat.PanTowards(this, player, center + Vector3.Up * 2f, 1.4f, ct);
		const double already = 1.4;
		if (BreachHoldSeconds > already) await Cutscene.Wait(this, BreachHoldSeconds - already, ct);
		InBreach = false;
	}

	private void PlayFoghorn(Vector3 at)
	{
		string path = $"res://assets/audio/sfx/creature_foghorn_{GD.RandRange(1, 2):00}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = "Unnatural",
			UnitSize = 12f, MaxDistance = 140f, VolumeDb = 4f,
		};
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = at;
		voice.Finished += voice.QueueFree;
		voice.Play();
	}

	// ------------------------------------------------------------------ arrival: on foot, at the door

	/// <summary>Control is back once <see cref="Landed"/>; the player walks the last stretch to the
	/// station themselves. Stepping up to its doorway is the act's real end: checkpoint 10, then
	/// the credits <see cref="Act11Ending"/> already knows how to roll.</summary>
	/// <summary>Reaching the station's door doesn't roll credits any more (Act 13 happens inside
	/// it): checkpoint 10 marks Act 12 done, then a fade carries the player into the lobby, the
	/// same way the bunker's vault door admits them (<see cref="StationInterior"/>). The real
	/// ending now waits on Act 13's own last puzzle.</summary>
	private void OnArrival(PlayerController player)
	{
		if (_arrived || !Landed) return;
		_arrived = true;
		Arrived = true;
		_ = Cutscene.Run(this, async ct =>
		{
			GD.Print("[story] Act 12: reached the rescue station");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act12LakeCrossed);
			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 0.8f, ct);
			var station = StationInterior.Instance;
			if (station != null) player.Teleport(station.EntranceMarkerWorld, station.EntranceYaw);
			if (fader != null) await fader.Fade(0f, 0.9f, ct);
			GD.Print("[story] Act 13: into the forester station");
		}, lockInput: true, freezeBody: true);
	}
}
