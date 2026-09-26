using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13's basement, under the station. The door off the lobby is duct-taped round its whole frame
/// (the knife cuts it: <see cref="TapeCutOverlay"/>, one drag down the latch-side slit). Behind it, red
/// brick stairs go down — thirteen of them, nothing like the stairs in the woods, and yet the same low
/// hum rises as the player goes down them. At the bottom the two caged bulbs struggle for ten seconds
/// and die (a slow, soft struggle: no strobe; Reduce Flashing makes it one fade), leaving a thin grey
/// light from a slit window and the lantern.
///
/// The room is flooded thigh-deep with lake water, debris turning on it. A great iron wheel on the far
/// wall drives the pumps: three turns (E) and the drain in the middle of the floor takes the water in a
/// seven-second whirlpool (the view held on it), the debris spiralling in — and at the end something
/// comes round with it and lodges in the grate: one of the lake thing's eyes, dead, lid hanging slack.
/// It only ever moves when the player isn't looking (<see cref="DeadEye"/>). Then the grandfather clock
/// in the corner chimes its noon, drowned and gurgling, spits out a key and bursts.
///
/// Local space: the door's hinge line is the lobby's front wall (z=0), centred on its gap; the stairs
/// run down toward -Z to the basement floor at y=-3.2.
/// </summary>
public partial class StationBasement : Node3D
{
	public const float Floor = -3.2f, RoomHeight = 2.8f, StairTop = -1.2f;
	public const int Steps = 13;
	public const float Run = 0.33f;
	public static float StairBottom => StairTop - Steps * Run;   // -5.49
	/// <summary>The room: x in [MinX, MaxX], z in [MinZ, StairBottom].</summary>
	public const float MinX = -4.4f, MaxX = 3.4f, MinZ = -12.6f;
	public static readonly Vector2 Drain = new(-0.5f, -9.2f);
	[Export] public float FloodDepth = 0.75f;
	[Export] public int TurnsNeeded = 3;
	[Export] public float DrainSeconds = 7f;

	/// <summary>For tests: the tape has been cut and the door is open.</summary>
	public bool TapeCut { get; private set; }
	public int Turns { get; private set; }
	public bool Drained { get; private set; }
	public bool ClockBroken { get; private set; }
	public bool LightsDead { get; private set; }
	public DeadEye Eye { get; private set; }
	/// <summary>World point of the wheel's grip, and of the bottom of the stairs (for tests).</summary>
	public Vector3 WheelWorld => _wheel?.GlobalPosition ?? GlobalPosition;
	public Vector3 StairFootWorld => ToGlobal(new Vector3(0, Floor + 0.05f, StairBottom - 0.8f));
	public Vector3 RoomCentreWorld => ToGlobal(new Vector3(Drain.X, Floor + 0.05f, Drain.Y + 1.8f));

	private StationDoor _door;
	private MeshInstance3D _water, _tape;
	private ShaderMaterial _waterMat;
	private Node3D _wheel, _wheelSpokes, _clock;
	private StaticBody3D _clockBody, _floorBody;
	private readonly List<(OmniLight3D light, MeshInstance3D bulb, float energy)> _bulbs = new();
	private readonly List<(Node3D node, Vector3 home, float phase)> _debris = new();
	private AudioStreamPlayer3D _hum, _buzz;
	private bool _draining, _lightsStarted;
	private Node3D _eyeMark;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var s = StoryManager.Instance;
		BuildDoor();
		BuildStairwell();
		BuildRoom();
		BuildLights();
		BuildWater();
		BuildWheel(new Vector3(-0.5f, Floor + 1.45f, MinZ + 0.12f));
		BuildClock(new Vector3(MinX + 0.55f, Floor, MinZ + 0.6f));
		Eye = new DeadEye { Name = "DeadEye" };
		AddChild(Eye);
		Eye.Position = new Vector3(Drain.X, Floor + 0.1f, Drain.Y);
		Eye.Visible = false;
		float midZ = (MinZ + StairBottom) * 0.5f;
		Eye.Anchors = new List<Vector3>
		{
			new(Drain.X, Floor + 0.08f, Drain.Y),                 // back in its grate
			new(-0.5f, Floor + 2.3f, MinZ + 0.3f),                 // on top of the pump housing
			new(MinX + 1.4f, Floor + 0.1f, MinZ + 1.3f),           // in the clock's wreckage
			new(0.3f, Floor + 0.3f, StairBottom + 0.15f),          // on the bottom stair
			new(MinX + 0.3f, Floor + 2.15f, midZ + 1f),            // on the slit window's ledge
			new(MaxX - 0.4f, Floor + 0.1f, MinZ + 0.4f),           // in the far corner
			new(MinX + 4.2f, Floor + 2.58f, -8.2f),                // up on a ceiling beam
			new(MaxX - 0.35f, Floor + 0.1f, StairBottom - 0.4f),   // tucked by the stairwell
		};

		// the breadcrumb once the iron door has been read: an eye drawn in blood on the door
		_eyeMark = new Node3D { Name = "EyeMark", Visible = false };
		_door.AddChild(_eyeMark);
		StationProps.Decal(_eyeMark, StationTextures.BloodMat, new Vector3(1.05f, 1.5f, 0.035f), Vector3.Back, new Vector2(0.55f, 0.35f));
		SignKit.Text(_eyeMark, "( o )", new Vector3(1.05f, 1.5f, 0.04f), Basis.Identity, 0.16f, new Color(0.3f, 0.02f, 0.02f), shadow: false);

		// Continue: put everything back the way the story left it.
		if (s != null)
		{
			if (s.HasFlag(StoryManager.Flag.StationBasementTapeCut)) { TapeCut = true; _tape.Visible = false; _door.Unlock(); }
			if (s.HasFlag(StoryManager.Flag.StationLightsDead)) { LightsDead = true; _lightsStarted = true; SetBulbs(0f); }
			if (s.HasFlag(StoryManager.Flag.StationBasementDrained)) { Drained = true; Turns = TurnsNeeded; SetDrained(); }
			if (s.HasFlag(StoryManager.Flag.StationClockBroken)) { ClockBroken = true; BreakClock(false); }
		}
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		var s = StoryManager.Instance;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		Vector3 l = ToLocal(player.GlobalPosition);
		// the hum rises as they go down the stairs, and stays low in the room
		if (_hum != null)
		{
			float down = Mathf.Clamp(-(l.Y) / -Floor, 0f, 1f);
			bool inside = TapeCut && l.Z < 0.2f && l.Z > MinZ - 0.5f && Mathf.Abs(l.X) < 5f && l.Y > Floor - 0.5f && l.Y < 2.5f;
			_hum.VolumeDb = inside ? Mathf.Lerp(-34f, -16f, down) : -80f;
			if (inside && !_hum.Playing) _hum.Play();
		}
		// at the bottom: the lights give up. Only for someone who came down the stairs — the basement
		// sits under the lakeshore, and a player dragged under the lake in Act 12 sinks past this depth.
		if (!_lightsStarted && TapeCut && l.Y < Floor + 1f && l.Y > Floor - 0.5f && l.Z < StairBottom + 0.4f
			&& l.Z > MinZ - 0.5f && l.X > MinX - 0.5f && l.X < MaxX + 0.5f)
		{
			_lightsStarted = true;
			_ = Cutscene.Run(this, ct => LightsDie(player, ct));
		}
		if (_eyeMark != null && s != null) _eyeMark.Visible = s.HasFlag(StoryManager.Flag.StationDoor3Seen) && !s.HasFlag(StoryManager.Flag.StationEyeTaken);
		// the iron door wants it: it isn't staying in the grate any more
		if (Eye != null && s != null) Eye.Wandering = Drained && s.HasFlag(StoryManager.Flag.StationDoor3Seen) && !s.HasFlag(StoryManager.Flag.StationEyeTaken);
		BobDebris((float)delta);
	}

	// ------------------------------------------------------------------ the taped door

	private void BuildDoor()
	{
		_door = new StationDoor { Name = "Door", Position = new Vector3(-1.1f, 0, 0), LockedPrompt = "Taped shut. Every inch of the frame." };
		AddChild(_door);
		_door.Setup(new Vector3(1.05f, 1.1f, 0), new Vector3(2.1f, 2.2f, 0.3f), new Vector3(1.05f, 1.1f, 0));
		var leaf = new MeshKit();
		leaf.Mat(BuildingTextures.BoardsMat);
		leaf.Color = new Color(0.36f, 0.27f, 0.18f);
		BuildKit.Box(leaf, new Vector3(1.05f, 1.08f, 0), new Vector3(2.08f, 2.16f, 0.05f), 1.2f);
		leaf.Mat(ItemTextures.BrassMat);
		leaf.Color = new Color(0.8f, 0.65f, 0.4f);
		leaf.Cylinder(new Vector3(1.85f, 1.02f, 0), new Vector3(1.85f, 1.02f, 0.08f), 0.03f, 0.03f, 8, true);
		leaf.Color = Colors.White;
		leaf.CommitTo(_door, "Leaf", true);

		// Tape round the whole frame, and across it in an X: someone wanted this shut.
		var k = new MeshKit();
		var tape = BuildingTextures.Plain("s_tape", new Color(0.62f, 0.6f, 0.56f), 0.6f);
		k.Mat(tape);
		k.Box(new Vector3(1.05f, 2.16f, 0.04f), new Vector3(2.2f, 0.1f, 0.012f), 2f);
		foreach (float x in new[] { 0.03f, 2.07f })
			k.Box(new Vector3(x, 1.1f, 0.04f), new Vector3(0.1f, 2.2f, 0.012f), 2f);
		for (int i = 0; i < 2; i++)
			k.Box(new Vector3(1.05f, 1.1f, 0.045f), new Vector3(2.7f, 0.09f, 0.012f), 2f, new Basis(Vector3.Back, (i == 0 ? 1 : -1) * 0.8f));
		for (int i = 0; i < 4; i++)
			k.Box(new Vector3(2.02f, 0.5f + i * 0.45f, 0.046f), new Vector3(0.34f, 0.08f, 0.012f), 2f, new Basis(Vector3.Back, 0.1f * (i - 1.5f)));
		k.Color = Colors.White;
		_tape = k.CommitTo(_door, "Tape", false);

		var use = _door.GetNode<PickupInteractable>("Use");
		use.PromptFor = p => TapeCut ? "Open the door" : p?.Inventory is { } inv && inv.HasTool(ToolKind.Knife) ? "Cut the tape" : "Taped shut. Every inch of the frame.";
		use.Interacted += player =>
		{
			if (TapeCut || TapeCutOverlay.Instance == null) return;
			if (player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Knife)) return;
			// the camera right by the handle, looking down the slit between door and frame
			Vector3 focus = _door.ToGlobal(new Vector3(2.0f, 1.25f, 0.05f));
			Vector3 eye = focus + _door.GlobalBasis * new Vector3(-0.25f, 0.05f, 0.6f);
			var view = new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
			TapeCutOverlay.Instance.Open(player, new()
			{
				new TapeCutOverlay.CutSpec
				{
					CameraView = view,
					Direction = Vector2.Down,
					PixelsNeeded = 300f,
					OnProgress = p => { if (IsInstanceValid(_tape)) _tape.Scale = new Vector3(1f, Mathf.Lerp(1f, 0.02f, p), 1f); },
				},
			}, () => OnTapeCut());
		};
	}

	private void OnTapeCut()
	{
		TapeCut = true;
		if (IsInstanceValid(_tape)) _tape.Visible = false;
		_door.Unlock();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationBasementTapeCut);
		GD.Print("[story] Act 13: the basement tape is cut");
	}

	// ------------------------------------------------------------------ the brick stairs

	private void BuildStairwell()
	{
		var body = new StaticBody3D { Name = "Stairwell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		var k = new MeshKit();
		k.Mat(StationTextures.BrickMat);
		k.Color = Colors.White;
		const float hw = 0.95f;
		// the landing just inside the door: solid brick right down, so there's no slit under its lip
		BuildKit.Box(k, new Vector3(0, Floor * 0.5f, StairTop * 0.5f), new Vector3(hw * 2f, -Floor, -StairTop), 2f);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.06f, StairTop * 0.5f), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.12f, -StairTop) } });
		float rise = -Floor / Steps;
		for (int i = 0; i < Steps; i++)
		{
			float top = -(i + 1) * rise, z0 = StairTop - i * Run, z1 = z0 - Run;
			float h = top - Floor;
			k.Color = new Color(0.9f + 0.1f * (i % 2), 1f, 1f);
			BuildKit.Box(k, new Vector3(0, Floor + h * 0.5f, (z0 + z1) * 0.5f), new Vector3(hw * 2f, h, Run), 2f);
			// a worn stone nosing on each step
			k.Mat(ProcTextures.ConcreteMat);
			k.Color = new Color(0.45f, 0.42f, 0.4f);
			BuildKit.Box(k, new Vector3(0, top + 0.01f, z0 - 0.04f), new Vector3(hw * 2f, 0.03f, 0.09f), 2f);
			k.Mat(StationTextures.BrickMat);
			k.Color = Colors.White;
		}
		// the ramp the body actually walks (it can't climb risers)
		float len = Steps * Run;
		var ramp = new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.1f, Mathf.Sqrt(len * len + Floor * Floor)) },
			Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Atan2(-Floor, len)), new Vector3(0, Floor * 0.5f - 0.05f, StairTop - len * 0.5f)),
		};
		body.AddChild(ramp);
		// brick walls either side and a stepped brick ceiling following the stairs down
		foreach (int s in new[] { -1, 1 })
		{
			float x = s * (hw + 0.1f);
			BuildKit.Box(k, new Vector3(x, (Floor + 2.4f) * 0.5f, StairBottom * 0.5f), new Vector3(0.2f, 2.4f - Floor, -StairBottom), 2f);
			body.AddChild(new CollisionShape3D { Position = new Vector3(x, (Floor + 2.4f) * 0.5f, StairBottom * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.2f, 2.4f - Floor, -StairBottom) } });
		}
		for (int i = -1; i < Steps + 1; i++)
		{
			float z0 = i < 0 ? 0f : StairTop - i * Run, z1 = i < 0 ? StairTop : z0 - Run;
			float y = (i < 0 ? 0f : -(i + 1) * rise) + 2.3f;
			// thick enough that each step of it overlaps the next (no slits between them)
			BuildKit.Box(k, new Vector3(0, y + 0.2f, (z0 + z1) * 0.5f), new Vector3(hw * 2f + 0.4f, 0.4f, Mathf.Abs(z1 - z0) + 0.02f), 2f);
		}
		k.Color = Colors.White;
		k.CommitTo(this, "Stairwell", true);

		_hum = Loop("res://assets/audio/ambient/stairs_hum_loop.wav", new Vector3(0, Floor, StairBottom), "Unnatural", -80f, 6f);
	}

	// ------------------------------------------------------------------ the room

	private void BuildRoom()
	{
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		_floorBody = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 };
		_floorBody.SetMeta("surface", "water");
		AddChild(_floorBody);
		var k = new MeshKit();
		k.Mat(StationTextures.BrickMat);
		k.Color = new Color(0.8f, 0.78f, 0.76f);
		float cx = (MinX + MaxX) * 0.5f, cz = (MinZ + StairBottom) * 0.5f, hx = (MaxX - MinX) * 0.5f, hz = (StairBottom - MinZ) * 0.5f;
		float top = Floor + RoomHeight;
		// the +Z wall with the stairwell's opening (x in [-0.95, 0.95])
		StationKit.WallAlongX(k, body, StairBottom, MinX, MaxX, RoomHeight, Floor, (0f, 1.9f), 2.3f, 0.3f);
		StationKit.WallAlongX(k, body, MinZ, MinX, MaxX, RoomHeight, Floor, null, 2.2f, 0.3f);
		StationKit.WallAlongZ(k, body, MinX, MinZ, StairBottom, RoomHeight, Floor, null, 2.2f, 0.3f);
		StationKit.WallAlongZ(k, body, MaxX, MinZ, StairBottom, RoomHeight, Floor, null, 2.2f, 0.3f);
		k.Mat(ProcTextures.ConcreteMat);
		k.Color = new Color(0.3f, 0.3f, 0.28f);
		BuildKit.Box(k, new Vector3(cx, Floor - 0.05f, cz), new Vector3(hx * 2f, 0.1f, hz * 2f), 2f);
		_floorBody.AddChild(new CollisionShape3D { Position = new Vector3(cx, Floor - 0.05f, cz), Shape = new BoxShape3D { Size = new Vector3(hx * 2f, 0.1f, hz * 2f) } });
		k.Color = new Color(0.22f, 0.22f, 0.21f);
		BuildKit.Box(k, new Vector3(cx, top + 0.05f, cz), new Vector3(hx * 2f, 0.1f, hz * 2f), 2f, BuildKit.Face.PY);
		// ceiling beams and old pipes
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.3f, 0.24f, 0.18f);
		for (float x = MinX + 1f; x < MaxX; x += 1.6f)
			BuildKit.Box(k, new Vector3(x, top - 0.1f, cz), new Vector3(0.18f, 0.2f, hz * 2f), 1.2f);
		k.Color = Colors.White;
		k.CommitTo(this, "Room", true);
		var pipes = new MeshKit();
		StationProps.Pipe(pipes, new Vector3(MinX + 0.3f, top - 0.35f, MinZ + 0.3f), new Vector3(MaxX - 0.3f, top - 0.35f, MinZ + 0.3f), 0.1f);
		StationProps.Pipe(pipes, new Vector3(-0.5f - 0.6f, Floor, MinZ + 0.3f), new Vector3(-0.5f - 0.6f, top, MinZ + 0.3f), 0.09f);
		StationProps.Pipe(pipes, new Vector3(-0.5f + 0.6f, Floor, MinZ + 0.3f), new Vector3(-0.5f + 0.6f, top, MinZ + 0.3f), 0.09f);
		// the drain: an iron grate set in the floor
		pipes.Mat(BuildingTextures.IronMat);
		pipes.Color = new Color(0.2f, 0.19f, 0.18f);
		for (int i = -3; i <= 3; i++)
			BuildKit.Box(pipes, new Vector3(Drain.X + i * 0.07f, Floor + 0.012f, Drain.Y), new Vector3(0.025f, 0.02f, 0.5f), 2f);
		ItemMeshes.Torus(pipes, new Vector3(Drain.X, Floor + 0.015f, Drain.Y), Vector3.Up, 0.27f, 0.03f, 16, 4);
		pipes.Color = Colors.White;
		pipes.CommitTo(this, "Pipes", true);
		// a slit window up by the ceiling: a little grey daylight, all that's left once the bulbs go
		var win = new MeshInstance3D
		{
			Name = "SlitWindow", Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.25f) },
			MaterialOverride = StationTextures.Glow("st_daylight", new Color(0.55f, 0.58f, 0.6f), 0.8f),
			Position = new Vector3(MinX + 0.16f, top - 0.35f, cz + 1f), Rotation = new Vector3(0, Mathf.Pi * 0.5f, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(win);
		AddChild(new OmniLight3D
		{
			Name = "WindowLight", LightColor = new Color(0.55f, 0.6f, 0.65f), LightEnergy = 0.35f, OmniRange = 7f,
			Position = win.Position + new Vector3(0.6f, -0.4f, 0),
		});
	}

	// ------------------------------------------------------------------ the lights

	private void BuildLights()
	{
		float top = Floor + RoomHeight;
		foreach (var at in new[] { new Vector3(-2.2f, top - 0.35f, -7.4f), new Vector3(1.4f, top - 0.35f, -10.6f) })
		{
			var k = new MeshKit();
			k.Mat(BuildingTextures.IronMat);
			k.Color = new Color(0.2f, 0.2f, 0.19f);
			for (int i = 0; i < 6; i++)
			{
				float a = Mathf.Tau * i / 6f;
				k.Cylinder(at + Vector3.Up * 0.3f, at + new Vector3(Mathf.Cos(a) * 0.1f, -0.08f, Mathf.Sin(a) * 0.1f), 0.006f, 0.006f, 3, false);
			}
			k.Cylinder(at + Vector3.Up * 0.35f, at + Vector3.Up * 0.25f, 0.05f, 0.05f, 8, true);
			k.CommitTo(this, "Cage", false);
			var bulb = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.06f, Height = 0.14f }, Position = at,
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.9f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.6f), EmissionEnergyMultiplier = 2f },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(bulb);
			var light = new OmniLight3D { LightColor = new Color(1f, 0.82f, 0.58f), LightEnergy = 1.6f, OmniRange = 7f, Position = at + Vector3.Down * 0.1f, ShadowEnabled = true };
			AddChild(light);
			_bulbs.Add((light, bulb, 1.6f));
		}
		_buzz = Loop("res://assets/audio/sfx/bulb_buzz_loop.wav", new Vector3(-0.4f, Floor + RoomHeight - 0.4f, -9f), "Events", -18f, 4f);
		_buzz?.Play();
	}

	private void SetBulbs(float level)
	{
		foreach (var (light, bulb, energy) in _bulbs)
		{
			light.LightEnergy = energy * level;
			light.Visible = level > 0.01f;
			if (bulb.MaterialOverride is StandardMaterial3D m) m.EmissionEnergyMultiplier = 2f * level;
		}
		if (_buzz != null) { _buzz.VolumeDb = Mathf.Lerp(-40f, -18f, level); if (level <= 0.01f) _buzz.Stop(); }
	}

	/// <summary>The bulbs' ten-second struggle: slow dips and recoveries (never faster than about one
	/// change a second, each an ease), a last long sag, a pop, dark. Reduce Flashing: one smooth fade.</summary>
	private async Task LightsDie(PlayerController player, CancellationToken ct)
	{
		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		(float t, float to)[] path = reduce
			? new[] { (1.5f, 1f), (6.5f, 0.2f), (9.5f, 0f) }
			: new[] { (1.2f, 1f), (1.8f, 0.4f), (2.6f, 0.9f), (4.0f, 0.85f), (4.8f, 0.2f), (5.9f, 0.7f), (6.8f, 0.55f), (7.5f, 0.12f), (8.4f, 0.45f), (9.6f, 0.05f), (10f, 0f) };
		float from = 1f;
		double clock = 0, segStart = 0;
		int seg = 0;
		while (seg < path.Length)
		{
			await Cutscene.Frame(this, ct);
			clock += GetProcessDeltaTime();
			var (t, to) = path[seg];
			float u = Mathf.Clamp((float)((clock - segStart) / Mathf.Max(0.01, t - segStart)), 0f, 1f);
			SetBulbs(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u)));
			if (u >= 1f)
			{
				if (to < from - 0.3f) PlaySfx("bulb_sputter", 3, _bulbs[seg % _bulbs.Count].bulb.GlobalPosition, -6f);
				from = to; segStart = t; seg++;
			}
		}
		PlaySfx("bulb_pop", 1, _bulbs[0].bulb.GlobalPosition, -2f);
		SetBulbs(0f);
		LightsDead = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationLightsDead);
		GD.Print("[story] Act 13: the basement lights die");
		if (player.GetNodeOrNull<Lantern>("Lantern") is { IsOn: false } && player.Inventory is { HasLantern: true })
			_ = StoryBeat.Caption(this, "Too dark to see.   [F] lantern", 0.4f, 2.6f, 1f);
	}

	// ------------------------------------------------------------------ the flood

	private void BuildWater()
	{
		_waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/basement_water.gdshader") };
		_waterMat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		_waterMat.SetShaderParameter("drain", Drain);
		var plane = new PlaneMesh { Size = new Vector2(MaxX - MinX - 0.3f, StairBottom - MinZ + 1.5f), SubdivideWidth = 24, SubdivideDepth = 24 };
		_water = new MeshInstance3D { Name = "Water", Mesh = plane, MaterialOverride = _waterMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_water);
		// the mesh's local XZ must be the room's plan for the drain maths: centre it with no offset
		_water.Position = new Vector3((MinX + MaxX) * 0.5f, Floor + FloodDepth, (MinZ + StairBottom + 1.5f) * 0.5f);
		_waterMat.SetShaderParameter("drain", Drain - new Vector2(_water.Position.X, _water.Position.Z));

		// debris turning on the surface
		var rng = new RandomNumberGenerator { Seed = 1331 };
		void Float(string name, System.Action<MeshKit> build, Vector3 at)
		{
			var n = new Node3D { Name = name, Position = at };
			AddChild(n);
			var k = new MeshKit();
			build(k);
			k.CommitTo(n, "Mesh", true);
			_debris.Add((n, at, rng.RandfRange(0, Mathf.Tau)));
		}
		float wy = Floor + FloodDepth;
		for (int i = 0; i < 5; i++)
			Float($"Plank{i}", k => { k.Mat(BuildingTextures.BoardsMat); k.Color = new Color(0.4f, 0.32f, 0.24f); BuildKit.Box(k, Vector3.Zero, new Vector3(rng.RandfRange(0.6f, 1.3f), 0.04f, 0.14f), 1.2f); },
				new Vector3(rng.RandfRange(MinX + 0.8f, MaxX - 0.8f), wy, rng.RandfRange(MinZ + 0.8f, StairBottom - 0.8f)));
		Float("Chair", k => { k.Mat(PropTextures.DeckMat); k.Color = new Color(0.35f, 0.25f, 0.16f); BuildKit.Box(k, new Vector3(0, 0.02f, 0), new Vector3(0.42f, 0.04f, 0.4f)); BuildKit.Box(k, new Vector3(0, 0.02f, 0.3f), new Vector3(0.42f, 0.04f, 0.3f), 1f, BuildKit.Face.None, new Basis(Vector3.Right, 0.4f)); },
			new Vector3(1.8f, wy, -7.2f));
		for (int i = 0; i < 4; i++)
			Float($"Bottle{i}", k => { k.Mat(ItemTextures.GlassMat); k.Color = new Color(0.3f, 0.45f, 0.3f, 0.6f); k.Cylinder(new Vector3(-0.1f, 0, 0), new Vector3(0.1f, 0, 0), 0.035f, 0.035f, 8, true); k.Cylinder(new Vector3(0.1f, 0, 0), new Vector3(0.17f, 0, 0), 0.014f, 0.012f, 6, true); },
				new Vector3(rng.RandfRange(MinX + 0.6f, MaxX - 0.6f), wy, rng.RandfRange(MinZ + 0.6f, StairBottom - 0.6f)));
		for (int i = 0; i < 6; i++)
			Float($"Paper{i}", k => { k.Mat(StationTextures.Flat("st_paper", new Color(0.8f, 0.76f, 0.64f), 0.9f, 0.05f)); k.Color = Colors.White; BuildKit.Box(k, Vector3.Zero, new Vector3(0.21f, 0.004f, 0.28f)); },
				new Vector3(rng.RandfRange(MinX + 0.5f, MaxX - 0.5f), wy, rng.RandfRange(MinZ + 0.5f, StairBottom - 0.5f)));
		// a doll's head, face up, eyes open
		Float("DollHead", k =>
		{
			k.Mat(StationTextures.Flat("st_doll", new Color(0.82f, 0.72f, 0.64f), 0.4f, 0.4f)); k.Color = Colors.White;
			k.Blob(Vector3.Zero, new Vector3(0.08f, 0.09f, 0.08f), 5, 0.04f, false);
			k.Mat(StationTextures.Flat("st_doll_eye", new Color(0.05f, 0.08f, 0.15f), 0.2f, 0.6f));
			foreach (int sx in new[] { -1, 1 }) k.Blob(new Vector3(sx * 0.028f, 0.075f, -0.03f), Vector3.One * 0.013f, 9, 0f, false);
		}, new Vector3(-2.6f, wy, -10.4f));
	}

	private void BobDebris(float dt)
	{
		if (_draining) return;
		float t = (float)Time.GetTicksMsec() / 1000f;
		foreach (var (node, home, phase) in _debris)
		{
			if (Drained) break;
			node.Position = home + new Vector3(Mathf.Sin(t * 0.13f + phase) * 0.3f, Mathf.Sin(t * 0.9f + phase) * 0.015f, Mathf.Cos(t * 0.11f + phase) * 0.3f);
			node.Rotation = new Vector3(Mathf.Sin(t * 0.7f + phase) * 0.05f, t * 0.05f + phase, Mathf.Cos(t * 0.6f + phase) * 0.05f);
		}
	}

	/// <summary>Straight to dry (Continue): no water, debris lying where it fell, the eye in the grate.</summary>
	private void SetDrained()
	{
		_water.Visible = false;
		_floorBody.SetMeta("surface", "stone");
		var rng = new RandomNumberGenerator { Seed = 1332 };
		foreach (var (node, home, _) in _debris)
		{
			Vector2 toward = (Drain - new Vector2(home.X, home.Z)) * rng.RandfRange(0.3f, 0.7f);
			node.Position = new Vector3(home.X + toward.X, Floor + 0.03f, home.Z + toward.Y);
			node.Rotation = new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0);
		}
		if (StoryManager.Instance?.HasFlag(StoryManager.Flag.StationEyeTaken) != true)
		{
			Eye.Visible = true;
			Eye.Position = new Vector3(Drain.X, Floor + 0.08f, Drain.Y);
		}
		LeaveWetFloor();
	}

	private void LeaveWetFloor()
	{
		var rng = new RandomNumberGenerator { Seed = 1333 };
		for (int i = 0; i < 7; i++)
		{
			var d = StationProps.Decal(this, StationTextures.Flat("st_puddle", new Color(0.05f, 0.05f, 0.04f), 0.02f, 0.9f),
				new Vector3(rng.RandfRange(MinX + 1f, MaxX - 1f), Floor + 0.004f, rng.RandfRange(MinZ + 1f, StairBottom - 1f)), Vector3.Up,
				new Vector2(rng.RandfRange(0.7f, 1.6f), rng.RandfRange(0.5f, 1.1f)), rng.RandfRange(0, 3f), "Puddle");
			d.Transparency = 0.3f;
		}
	}

	// ------------------------------------------------------------------ the wheel, and the drain

	private void BuildWheel(Vector3 at)
	{
		_wheel = new Node3D { Name = "Wheel", Position = at };
		AddChild(_wheel);
		var k = new MeshKit();
		k.Mat(ItemTextures.SteelMat);
		k.Color = new Color(0.33f, 0.3f, 0.28f);
		k.Cylinder(new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.1f), 0.1f, 0.1f, 8, true);
		// the pump housing it sits on, with pipes running down into the floor
		k.Mat(StationTextures.RustPlateMat);
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, -0.2f, -0.2f), new Vector3(1.0f, 1.3f, 0.3f), 1.5f);
		k.CommitTo(_wheel, "Housing", true);
		_wheelSpokes = new Node3D { Name = "Spokes" };
		_wheel.AddChild(_wheelSpokes);
		var sk = new MeshKit();
		sk.Mat(ItemTextures.SteelMat);
		sk.Color = new Color(0.4f, 0.14f, 0.1f);   // painted red once
		const float rimR = 0.55f;
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			sk.Cylinder(new Vector3(0, 0, 0.12f), new Vector3(Mathf.Cos(a) * rimR, Mathf.Sin(a) * rimR, 0.12f), 0.035f, 0.03f, 5, false);
		}
		ItemMeshes.Torus(sk, new Vector3(0, 0, 0.12f), Vector3.Back, rimR, 0.05f, 16, 6);
		sk.Color = Colors.White;
		sk.CommitTo(_wheelSpokes, "Wheel", true);

		var body = new StaticBody3D { Name = "WheelBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.2f, -0.2f), Shape = new BoxShape3D { Size = new Vector3(1f, 1.3f, 0.3f) } });
		_wheel.AddChild(body);
		var use = new Interactable { Name = "Use", Prompt = "Turn the wheel", PickRadius = 0.7f, MaxDistance = 3f, Position = new Vector3(0, 0, 0.3f) };
		use.Interacted += OnWheelTurned;
		_wheel.AddChild(use);
	}

	private void OnWheelTurned(PlayerController player)
	{
		if (Drained || _draining || !TapeCut) return;
		if (_wheelTurning) return;
		_wheelTurning = true;
		Turns++;
		PlayOneShot("res://assets/audio/sfx/wheel_turn_01.wav", _wheel.GlobalPosition, -2f, 0.9f + 0.05f * Turns);
		var tween = CreateTween();
		tween.TweenProperty(_wheelSpokes, "rotation:z", _wheelSpokes.Rotation.Z - Mathf.Tau / 2f, 1.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tween.TweenCallback(Callable.From(() =>
		{
			_wheelTurning = false;
			// each turn the pumps cough further into life
			PlayOneShot("res://assets/audio/sfx/underwater_thoom_01.wav", ToGlobal(new Vector3(Drain.X, Floor, Drain.Y)), -14f + Turns * 3f, 1.4f);
			if (Turns >= TurnsNeeded) _ = Cutscene.Run(this, ct => DrainSequence(player, ct), lockInput: true);
		}));
	}
	private bool _wheelTurning;

	/// <summary>Seven seconds, the view held on the drain: the water winds round it faster and faster and
	/// goes down, dragging the debris in circles after it — and out of the last of it, something turns
	/// in the whirlpool and lodges in the grate.</summary>
	private async Task DrainSequence(PlayerController player, CancellationToken ct)
	{
		_draining = true;
		if (_wheel.GetNodeOrNull<Interactable>("Use") is { } use) use.Enabled = false;
		Vector3 drainW = ToGlobal(new Vector3(Drain.X, Floor, Drain.Y));
		PlayOneShot("res://assets/audio/sfx/drain_gurgle_01.wav", drainW, 2f, 1f);
		var rig = player.CameraRig;
		float startY = _water.Position.Y;
		var starts = new List<(Node3D n, Vector3 p, float ang, float r)>();
		foreach (var (node, _, _) in _debris)
		{
			Vector2 d = new Vector2(node.Position.X, node.Position.Z) - Drain;
			starts.Add((node, node.Position, Mathf.Atan2(d.Y, d.X), d.Length()));
		}
		double t = 0;
		bool eyeIn = false;
		while (t < DrainSeconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / DrainSeconds);
			_waterMat.SetShaderParameter("swirl", Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u * 3f)) * (1f - Mathf.SmoothStep(0.85f, 1f, u)));
			float level = Mathf.Lerp(startY, Floor + 0.01f, Mathf.SmoothStep(0.1f, 1f, u));
			_water.Position = _water.Position with { Y = level };
			// debris: round and in, lower with the water
			foreach (var (n, p, ang, r) in starts)
			{
				float rr = r * (1f - 0.75f * Mathf.SmoothStep(0.1f, 1f, u));
				float a = ang + u * u * 9f / (0.4f + rr * 0.3f);
				n.Position = new Vector3(Drain.X + Mathf.Cos(a) * rr, Mathf.Max(level, Floor + 0.03f), Drain.Y + Mathf.Sin(a) * rr);
				n.RotateY(dt * 2f * (1f - rr / Mathf.Max(r, 0.01f)));
			}
			// the eye comes round in the last of it
			if (!eyeIn && u > 0.62f)
			{
				eyeIn = true;
				Eye.Visible = true;
				Eye.Frozen = true;
			}
			if (eyeIn)
			{
				float e = Mathf.SmoothStep(0.62f, 0.95f, u);
				float er = Mathf.Lerp(1.6f, 0f, e), ea = e * 14f;
				Eye.Position = new Vector3(Drain.X + Mathf.Cos(ea) * er, Mathf.Max(level, Floor) + 0.08f, Drain.Y + Mathf.Sin(ea) * er);
				Eye.Rotation = new Vector3(0, ea * 1.3f, 0);
			}
			// the view is held on the drain, pitched down at it
			Vector3 to = drainW - rig.Camera.GlobalPosition;
			float yawWant = Mathf.Atan2(-to.X, -to.Z);
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, yawWant) * Mathf.Min(1f, dt * 3f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 3f)));
		}
		PlayOneShot("res://assets/audio/sfx/squelch_close_01.wav", drainW, -2f, 0.8f);
		Eye.Position = new Vector3(Drain.X, Floor + 0.08f, Drain.Y);
		Eye.Frozen = false;
		_draining = false;
		Drained = true;
		_water.Visible = false;
		_floorBody.SetMeta("surface", "stone");
		LeaveWetFloor();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationBasementDrained);
		GD.Print("[story] Act 13: the basement is drained");
		_ = Cutscene.Run(this, RunClockSequence);
	}

	// ------------------------------------------------------------------ the grandfather clock

	private void BuildClock(Vector3 at)
	{
		_clock = new Node3D { Name = "Clock", Position = at, Rotation = new Vector3(0, Mathf.Pi * 0.25f, 0) };
		AddChild(_clock);
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.2f, 0.12f, 0.08f);
		k.Box(new Vector3(0, 1.0f, 0), new Vector3(0.6f, 1.7f, 0.32f), 1.2f);
		k.Box(new Vector3(0, 0.15f, 0), new Vector3(0.72f, 0.3f, 0.4f), 1.2f);
		k.Box(new Vector3(0, 1.95f, 0), new Vector3(0.7f, 0.3f, 0.4f), 1.2f);
		k.Color = new Color(0.14f, 0.09f, 0.06f);
		k.Box(new Vector3(0, 2.22f, 0), new Vector3(0.5f, 0.16f, 0.3f), 1.4f);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.7f, 0.6f, 0.4f);
		ItemMeshes.Disc(k, new Vector3(0, 1.85f, 0.17f), Vector3.Back, 0.22f, 16);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.08f, 0.08f, 0.08f);
		// both hands at twelve
		k.Box(new Vector3(0, 1.92f, 0.19f), new Vector3(0.02f, 0.15f, 0.01f), 4f);
		k.Box(new Vector3(0.005f, 1.9f, 0.192f), new Vector3(0.015f, 0.11f, 0.01f), 4f);
		k.Mat(ItemTextures.GlassMat);
		k.Color = new Color(0.7f, 0.8f, 0.8f, 0.35f);
		k.Box(new Vector3(0, 0.95f, 0.16f), new Vector3(0.42f, 1.3f, 0.02f), 1f);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.75f, 0.65f, 0.4f);
		k.Cylinder(new Vector3(0, 1.6f, 0), new Vector3(0, 0.5f, 0), 0.012f, 0.012f, 6, false);
		ItemMeshes.Disc(k, new Vector3(0, 0.45f, 0), Vector3.Back, 0.13f, 14, 0, true);
		// weed and a tide line of silt from the flood
		k.Mat(StationTextures.Flat("st_silt", new Color(0.2f, 0.2f, 0.12f), 0.9f, 0.1f));
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0.4f, 0.205f), new Vector3(0.74f, 0.8f, 0.003f));
		k.CommitTo(_clock, "ClockMesh", true);
		_clockBody = new StaticBody3D { Name = "ClockBody", CollisionLayer = 1, CollisionMask = 0 };
		_clockBody.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.1f, 0), Shape = new BoxShape3D { Size = new Vector3(0.72f, 2.3f, 0.4f) } });
		_clock.AddChild(_clockBody);
	}

	private async Task RunClockSequence(CancellationToken ct)
	{
		await Cutscene.Wait(this, 1.4, ct);
		PlayOneShot("res://assets/audio/sfx/clock_chime_drowned.wav", _clock.GlobalPosition + Vector3.Up * 1.8f, 3f, 1f);
		GD.Print("[story] Act 13: the clock chimes, drowned");
		await Cutscene.Wait(this, 4.6, ct);
		BreakClock(true);
		ClockBroken = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationClockBroken);
		GD.Print("[story] Act 13: the clock breaks apart and drops a key");
	}

	/// <summary>The clock spits its key out across the floor, then bursts: boards flung out and down, the
	/// case gone (and its collision with it, so nothing stands between the player and the key).</summary>
	private void BreakClock(bool live)
	{
		var rng = new RandomNumberGenerator { Seed = 77 };
		Vector3 at = _clock.Position;
		_clock.Visible = false;
		_clockBody.ProcessMode = ProcessModeEnum.Disabled;
		foreach (var c in _clockBody.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = true;

		var frag = new Node3D { Name = "ClockDebris", Position = at };
		AddChild(frag);
		for (int i = 0; i < 9; i++)
		{
			var pk = new MeshKit();
			pk.Mat(BuildingTextures.BoardsMat);
			pk.Color = new Color(0.18f, 0.11f, 0.08f);
			pk.Box(Vector3.Zero, new Vector3(rng.RandfRange(0.12f, 0.4f), rng.RandfRange(0.08f, 0.3f), rng.RandfRange(0.03f, 0.08f)), 1.5f);
			pk.Color = Colors.White;
			var mi = pk.CommitTo(frag, $"Piece{i}", false);
			Vector3 dest = new Vector3(rng.RandfRange(-1.4f, 1.4f), 0.05f, rng.RandfRange(-0.2f, 1.6f));
			if (!live) { mi.Position = dest; mi.Rotation = new Vector3(rng.RandfRange(-3f, 3f), rng.RandfRange(-3f, 3f), 0); continue; }
			mi.Position = new Vector3(rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(0.4f, 1.9f), 0);
			var tw = CreateTween().SetParallel();
			tw.TweenProperty(mi, "position", dest, rng.RandfRange(0.45f, 0.8f)).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tw.TweenProperty(mi, "rotation", new Vector3(rng.RandfRange(-6f, 6f), rng.RandfRange(-6f, 6f), rng.RandfRange(-6f, 6f)), 0.7f);
		}
		// the key, clear of the wreck, out on the floor toward the room
		Vector3 keyAt = at + new Vector3(1.3f, 0.02f, 1.2f);
		var key = new Pickup { Name = "ClockKey", Kind = ToolKind.Key, TakenId = "station_key", UseSpot = false, SnapToSurface = false, Position = live ? at + new Vector3(0, 1.85f, 0.3f) : keyAt, TakenLine = "Still wet. Warm." };
		AddChild(key);
		if (live)
		{
			PlayOneShot("res://assets/audio/sfx/clock_break.wav", at + Vector3.Up, 3f, 1f);
			var arc = CreateTween();
			arc.TweenMethod(Callable.From<float>(u =>
			{
				if (!IsInstanceValid(key)) return;
				Vector3 p = (at + new Vector3(0, 1.85f, 0.3f)).Lerp(keyAt, u);
				key.Position = p + Vector3.Up * Mathf.Sin(u * Mathf.Pi) * 0.7f;
			}), 0f, 1f, 0.7f);
			var dust = new GpuParticles3D
			{
				Name = "Dust", Amount = 40, Lifetime = 1.6, OneShot = true, Explosiveness = 0.9f, Position = at + Vector3.Up * 1.1f,
				ProcessMaterial = new ParticleProcessMaterial { Spread = 180f, InitialVelocityMin = 0.5f, InitialVelocityMax = 2f, Gravity = new Vector3(0, -1.5f, 0), ScaleMin = 1f, ScaleMax = 2.5f },
				DrawPass1 = new QuadMesh
				{
					Size = Vector2.One * 0.25f,
					Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), AlbedoColor = new Color(0.4f, 0.36f, 0.3f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
				},
			};
			AddChild(dust);
			dust.Emitting = true;
			dust.Finished += dust.QueueFree;
		}
	}

	// ------------------------------------------------------------------ sound

	private void PlayOneShot(string path, Vector3 at, float db, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, PitchScale = pitch, UnitSize = 4f, MaxDistance = 30f };
		AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}

	private void PlaySfx(string name, int variants, Vector3 at, float db)
		=> PlayOneShot($"res://assets/audio/sfx/{name}_{GD.RandRange(1, variants):00}.wav", at, db, (float)GD.RandRange(0.95, 1.05));

	private AudioStreamPlayer3D Loop(string path, Vector3 at, string bus, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = 20f, Position = at };
		AddChild(p);
		return p;
	}
}
