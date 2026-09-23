using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 13's basement: flooded, reached through a duct-taped door off the lobby. Cutting the tape
/// (<see cref="TapeCutOverlay"/>, one vertical drag down the door frame's slit) opens it; inside, a
/// giant iron valve wheel (E, three times) pumps the flood out; once dry, the grandfather clock in
/// the corner sounds its noon chime, drowned and gurgling, then breaks apart and leaves a key
/// behind. That key opens Room 1 back in the lobby (<see cref="StationInterior"/> owns the actual
/// door and listens for <see cref="Drained"/>/the key drop itself via <see cref="ClockBroken"/>).
///
/// Local space: floor y=0; the doorway back to the lobby is the +Z wall's gap (StationInterior
/// places this room so that gap lines up with the lobby's own basement-door gap — see its layout
/// comment for the shared coordinate scheme).
/// </summary>
public partial class StationBasement : Node3D
{
	[Export] public float HalfWidth = 3f;
	[Export] public float HalfDepth = 2.5f;
	[Export] public float Height = 2.6f;
	[Export] public float DoorGapX = 2f;
	[Export] public float FloodedLevel = 1.55f;
	[Export] public float DrainSeconds = 4.5f;
	[Export] public int TurnsNeeded = 3;

	/// <summary>For tests: the tape has been cut and the door is open.</summary>
	public bool TapeCut { get; private set; }
	/// <summary>For tests: how many times the wheel has been turned.</summary>
	public int Turns { get; private set; }
	/// <summary>For tests: the basement has finished draining.</summary>
	public bool Drained { get; private set; }
	/// <summary>For tests: the clock has chimed and broken, and the key is out.</summary>
	public bool ClockBroken { get; private set; }

	private StationDoor _door;
	private MeshInstance3D _water;
	private Node3D _wheelSpokes;
	private Node3D _clockIntact;
	private bool _draining, _clockRunning;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var k = new MeshKit();
		var floorK = new MeshKit();
		var ceilK = new MeshKit();
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);

		var wall = ProcTextures.ConcreteMat;
		k.Mat(wall);
		k.Color = new Color(0.3f, 0.29f, 0.28f);
		StationKit.WallAlongX(k, body, HalfDepth, -HalfWidth, HalfWidth, Height, 0, (DoorGapX, 2.2f));
		StationKit.WallAlongX(k, body, -HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongZ(k, body, -HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		StationKit.WallAlongZ(k, body, HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		floorK.Mat(ProcTextures.ConcreteMat);
		floorK.Color = new Color(0.22f, 0.22f, 0.21f);
		ceilK.Mat(wall);
		ceilK.Color = new Color(0.18f, 0.18f, 0.17f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, HalfWidth, HalfDepth, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "Walls");
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);

		AddChild(new OmniLight3D
		{
			Name = "BasementLamp", LightColor = new Color(0.78f, 0.85f, 0.82f), LightEnergy = 1.6f,
			OmniRange = 7.5f, OmniAttenuation = 1.2f, Position = new Vector3(0, Height - 0.2f, 0),
		});
		AddChild(new OmniLight3D
		{
			Name = "BasementFill", LightColor = new Color(0.5f, 0.58f, 0.56f), LightEnergy = 0.3f,
			OmniRange = 9f, Position = new Vector3(0, 1.4f, 0),
		});

		BuildDoor();
		BuildWater();
		BuildWheel(new Vector3(0, 1.5f, -HalfDepth + 0.1f));
		BuildClock(new Vector3(-HalfWidth + 0.9f, 0, -1.2f));
	}

	// ------------------------------------------------------------------ the taped door

	private void BuildDoor()
	{
		_door = new StationDoor { Name = "Door", Position = new Vector3(DoorGapX, 0, HalfDepth), LockedPrompt = "Cut the tape" };
		AddChild(_door);
		_door.Setup(new Vector3(0, 1.1f, 0), new Vector3(2.1f, 2.2f, 0.3f), new Vector3(0, 1.1f, 0));

		// A few overlapping strips of tape sealing the frame - cut away once TapeCut.
		var k = new MeshKit();
		var tape = BuildingTextures.Plain("s_tape", new Color(0.72f, 0.62f, 0.36f), 0.7f);
		k.Mat(tape);
		for (int i = 0; i < 3; i++)
		{
			float y = 0.5f + i * 0.7f;
			k.Box(new Vector3(0, y, 0.02f), new Vector3(2.1f, 0.14f, 0.02f), 2f, new Basis(Vector3.Forward, Mathf.DegToRad(6f - i * 4f)));
		}
		k.Color = Colors.White;
		var tapeMesh = k.CommitTo(_door, "Tape", false);

		var use = _door.GetNode<Interactable>("Use");
		use.Interacted += player =>
		{
			if (TapeCut || TapeCutOverlay.Instance == null) return;
			Vector3 focus = _door.ToGlobal(new Vector3(0.5f, 1.1f, 0.1f));
			Vector3 eye = focus + new Vector3(0, 0, 0.9f);
			var view = new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
			TapeCutOverlay.Instance.Open(player, new()
			{
				new TapeCutOverlay.CutSpec
				{
					CameraView = view,
					Direction = Vector2.Down,
					PixelsNeeded = 260f,
					OnProgress = p => tapeMesh.Scale = new Vector3(1f, 1f - p, 1f),
					OnCut = () => { },
				},
			}, () => OnTapeCut(player, tapeMesh));
		};
	}

	private void OnTapeCut(PlayerController player, MeshInstance3D tapeMesh)
	{
		TapeCut = true;
		if (IsInstanceValid(tapeMesh)) tapeMesh.QueueFree();
		_door.Unlock();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationBasementTapeCut);
		GD.Print("[story] Act 13: the basement tape is cut");
	}

	// ------------------------------------------------------------------ the flood

	private void BuildWater()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/lake_water.gdshader") };
		mat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		mat.SetShaderParameter("deep_color", new Color(0.02f, 0.03f, 0.025f));
		mat.SetShaderParameter("wave_intensity", 0f);
		var k = new MeshKit();
		k.Mat(mat);
		k.Quad(new Vector3(-HalfWidth + 0.1f, 0, -HalfDepth + 0.1f), new Vector3(HalfWidth - 0.1f, 0, -HalfDepth + 0.1f),
			new Vector3(HalfWidth - 0.1f, 0, HalfDepth - 0.1f), new Vector3(-HalfWidth + 0.1f, 0, HalfDepth - 0.1f), Vector3.Up);
		_water = k.CommitTo(this, "Water", false);
		_water.Position = new Vector3(0, FloodedLevel, 0);
	}

	private void BuildWheel(Vector3 at)
	{
		var wheel = new Node3D { Name = "Wheel", Position = at };
		AddChild(wheel);
		var k = new MeshKit();
		k.Mat(ItemTextures.SteelMat);
		k.Color = new Color(0.35f, 0.34f, 0.33f);
		// hub, rim, spokes
		k.Cylinder(new Vector3(0, 0, -0.08f), new Vector3(0, 0, 0.08f), 0.09f, 0.09f, 8, true);
		_wheelSpokes = new Node3D();
		wheel.AddChild(_wheelSpokes);
		var sk = new MeshKit { Xf = Transform3D.Identity };
		sk.Mat(ItemTextures.SteelMat);
		sk.Color = new Color(0.32f, 0.31f, 0.3f);
		const float rimR = 0.46f;
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			Vector3 tip = new(Mathf.Cos(a) * rimR, Mathf.Sin(a) * rimR, 0);
			sk.Cylinder(Vector3.Zero, tip, 0.035f, 0.03f, 5, false);
		}
		sk.Color = Colors.White;
		sk.CommitTo(_wheelSpokes, "Spokes", false);
		var rim = new MeshKit();
		rim.Mat(ItemTextures.SteelMat);
		rim.Color = new Color(0.3f, 0.29f, 0.28f);
		ItemMeshes.Torus(rim, Vector3.Zero, Vector3.Back, rimR, 0.05f, 12, 6);
		rim.Color = Colors.White;
		rim.CommitTo(_wheelSpokes, "Rim", false);
		// the pipe it's mounted on
		k.Mat(ItemTextures.SteelMat);
		k.Color = new Color(0.24f, 0.24f, 0.23f);
		k.Cylinder(new Vector3(0, 0, -1.2f), new Vector3(0, 0, -0.15f), 0.13f, 0.13f, 8, false);
		k.Color = Colors.White;
		k.CommitTo(wheel, "Pipe", false);

		// A small block behind the wheel face (the pipe mount), well clear of the pick sphere in
		// front of it - a wide or rotated shape here previously sat in the probe ray's way.
		var body = new StaticBody3D { Name = "WheelBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0, -0.55f), Shape = new BoxShape3D { Size = new Vector3(0.3f, 0.3f, 0.5f) } });
		wheel.AddChild(body);

		var use = new Interactable { Name = "Use", Prompt = "Turn the wheel", PickRadius = 0.9f, MaxDistance = 3.5f };
		use.Interacted += OnWheelTurned;
		wheel.AddChild(use);
	}

	private void OnWheelTurned(PlayerController player)
	{
		if (Drained || _draining) return;
		Turns++;
		PlayOneShot("res://assets/audio/sfx/wheel_turn_01.wav", "Player", 0f, 1f);
		var tween = CreateTween();
		tween.TweenProperty(_wheelSpokes, "rotation:z", _wheelSpokes.Rotation.Z + Mathf.Tau / 3f, 0.6f);
		if (Turns >= TurnsNeeded) StartDraining();
	}

	private void StartDraining()
	{
		_draining = true;
		if (GetNodeOrNull<Interactable>("Wheel/Use") is { } use) use.Enabled = false;
		var tween = CreateTween();
		tween.TweenProperty(_water, "position:y", -0.1f, DrainSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(OnDrained));
	}

	private void OnDrained()
	{
		Drained = true;
		_draining = false;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationBasementDrained);
		GD.Print("[story] Act 13: the basement is drained");
		_ = Cutscene.Run(this, RunClockSequence);
	}

	// ------------------------------------------------------------------ the grandfather clock

	private void BuildClock(Vector3 at)
	{
		_clockIntact = new Node3D { Name = "Clock", Position = at };
		AddChild(_clockIntact);
		var k = new MeshKit();
		var wood = BuildingTextures.BoardsMat;
		k.Mat(wood);
		k.Color = new Color(0.16f, 0.11f, 0.08f);
		// case: a tall narrow box, a wider base, a crowned hood
		k.Box(new Vector3(0, 1.0f, 0), new Vector3(0.6f, 1.7f, 0.32f), 1.2f);
		k.Box(new Vector3(0, 0.15f, 0), new Vector3(0.72f, 0.3f, 0.4f), 1.2f);
		k.Box(new Vector3(0, 1.95f, 0), new Vector3(0.7f, 0.3f, 0.4f), 1.2f);
		k.Color = new Color(0.12f, 0.08f, 0.06f);
		k.Box(new Vector3(0, 2.22f, 0), new Vector3(0.5f, 0.16f, 0.3f), 1.4f, new Basis(Vector3.Right, 0));
		// the dial
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.7f, 0.6f, 0.4f);
		ItemMeshes.Disc(k, new Vector3(0, 1.85f, 0.17f), Vector3.Back, 0.22f, 16);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.08f, 0.08f, 0.08f);
		k.Box(new Vector3(0, 1.9f, 0.19f), new Vector3(0.02f, 0.16f, 0.01f), 4f);
		k.Box(new Vector3(0.09f, 1.85f, 0.19f), new Vector3(0.12f, 0.02f, 0.01f), 4f);
		// glass door over the pendulum
		k.Mat(ItemTextures.GlassMat);
		k.Color = new Color(0.7f, 0.8f, 0.8f, 0.35f);
		k.Box(new Vector3(0, 0.95f, 0.16f), new Vector3(0.42f, 1.3f, 0.02f), 1f);
		// pendulum
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.75f, 0.65f, 0.4f);
		k.Cylinder(new Vector3(0, 1.6f, 0), new Vector3(0, 0.5f, 0), 0.012f, 0.012f, 6, false);
		ItemMeshes.Disc(k, new Vector3(0, 0.45f, 0), Vector3.Back, 0.13f, 14, 0, true);
		k.Color = Colors.White;
		k.CommitTo(_clockIntact, "ClockMesh", true);

		var body = new StaticBody3D { Name = "ClockBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.1f, 0), Shape = new BoxShape3D { Size = new Vector3(0.72f, 2.3f, 0.4f) } });
		_clockIntact.AddChild(body);
	}

	private async System.Threading.Tasks.Task RunClockSequence(System.Threading.CancellationToken ct)
	{
		_clockRunning = true;
		await Cutscene.Wait(this, 1.0, ct);
		PlayOneShot("res://assets/audio/sfx/clock_chime_drowned.wav", "Unnatural", 2f, 1f);
		GD.Print("[story] Act 13: the clock chimes, drowned");
		await Cutscene.Wait(this, 4.4, ct);
		PlayOneShot("res://assets/audio/sfx/clock_break.wav", "Events", 2f, 1f);
		BreakClock();
		ClockBroken = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationClockBroken);
		GD.Print("[story] Act 13: the clock breaks apart and drops a key");
	}

	/// <summary>The intact clock is swapped for a scatter of the same boards, flung outward and down
	/// (a simple tween-driven "explosion" rather than real physics fragments), and a key is left
	/// sitting where it stood.</summary>
	private void BreakClock()
	{
		Vector3 at = _clockIntact.Position;
		var rng = new RandomNumberGenerator { Seed = 77 };
		var frag = new Node3D { Name = "ClockDebris", Position = at };
		AddChild(frag);
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		var pieces = new System.Collections.Generic.List<MeshInstance3D>();
		for (int i = 0; i < 7; i++)
		{
			var pk = new MeshKit();
			pk.Mat(BuildingTextures.BoardsMat);
			pk.Color = new Color(0.16f, 0.11f, 0.08f);
			pk.Box(Vector3.Zero, new Vector3(rng.RandfRange(0.12f, 0.3f), rng.RandfRange(0.1f, 0.25f), rng.RandfRange(0.03f, 0.08f)), 1.5f);
			pk.Color = Colors.White;
			var mi = pk.CommitTo(frag, $"Piece{i}", false);
			mi.Position = new Vector3(rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(0.4f, 1.6f), rng.RandfRange(-0.1f, 0.1f));
			pieces.Add(mi);
		}
		_clockIntact.Visible = false;
		var tween = CreateTween().SetParallel();
		foreach (var mi in pieces)
		{
			Vector3 dir = new(rng.RandfRange(-1f, 1f), rng.RandfRange(0.3f, 1f), rng.RandfRange(-1f, 1f));
			Vector3 dest = mi.Position + dir.Normalized() * rng.RandfRange(0.6f, 1.3f) + Vector3.Down * rng.RandfRange(0.3f, 1.0f);
			tween.TweenProperty(mi, "position", dest, rng.RandfRange(0.5f, 0.8f)).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(mi, "rotation", new Vector3(rng.RandfRange(-6f, 6f), rng.RandfRange(-6f, 6f), rng.RandfRange(-6f, 6f)), rng.RandfRange(0.5f, 0.8f));
		}

		// The key, left where the clock stood.
		var keyPickup = new Pickup { Name = "ClockKey", Kind = ToolKind.Key, Position = at + new Vector3(0, 0.05f, 0), UseSpot = false, SnapToSurface = false };
		AddChild(keyPickup);
	}

	private void PlayOneShot(string path, string bus, float volumeDb, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 3f, MaxDistance = 30f };
		AddChild(s);
		s.Position = new Vector3(0, 1.2f, 0);
		s.Finished += s.QueueFree;
		s.Play();
	}
}
