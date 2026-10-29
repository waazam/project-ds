using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 11's four lights (the owner, 2026-10-29: "four spotlights beaming straight down from above, in order blue, purple,
/// a sickly green, blood red, along the way to the big staircase. The stalker can't enter a spotlight's beam ... Reaching
/// each light in order is a checkpoint (safe haven, maybe a save after the first?)").
///
/// Strung out between the radio spot in front of the bunker and the foot of the last staircase, weaving a little either
/// side of the straight line. Dark until the thing comes out of the bunker after them (<see cref="SwitchOn"/>: one after
/// another, each with a breaker's clunk, so the way is drawn in coloured light); then each is a pool of light on the
/// ground under a shaft lost up in the dark, a cone the player can stand in and the pursuer cannot enter
/// (<see cref="BeamAt"/>). Stepping into one saves (a flag within checkpoint 8, <see cref="Flag"/>): a death after it
/// comes back standing in the furthest one reached (RespawnPoints), facing on toward the stairs. The scatter keeps its
/// trees out of the pools (a <see cref="ClearZone"/> at each).
///
/// Made by <see cref="Act11Ending"/> at load (so Continue's respawn finds the markers).
/// </summary>
public partial class Act11Lights : Node3D
{
	public const int Count = 4;
	public static readonly string[] Names = { "blue", "purple", "green", "red" };
	public static readonly Color[] Colours =
	{
		new(0.28f, 0.48f, 1f),     // blue
		new(0.62f, 0.22f, 1f),     // purple
		new(0.58f, 0.86f, 0.16f),  // a sickly green
		new(0.85f, 0.03f, 0.02f),  // blood red
	};
	/// <summary>The save flag for having stood in light <paramref name="i"/> (0-based).</summary>
	public static string Flag(int i) => $"act11_light_{i + 1}";
	/// <summary>The respawn marker's group for light <paramref name="i"/>.</summary>
	public static string RespawnGroup(int i) => $"respawn_Act11Light{i + 1}";

	/// <summary>The pool's radius on the ground (m): inside it the player is safe, and the pursuer never comes in.</summary>
	[Export] public float BeamRadius = 2.6f;
	/// <summary>How high above the ground the lamps hang, out of sight in the dark (m).</summary>
	[Export] public float Height = 14f;
	/// <summary>Where along the way from the radio spot to the stairs' foot each one stands (0..1), and how far it is
	/// set off the straight line (m, + to the right walking toward the stairs).</summary>
	[Export] public float[] Along = { 0.17f, 0.39f, 0.61f, 0.83f };
	[Export] public float[] Aside = { 2.5f, -3.5f, 3f, -2f };

	public static Act11Lights Instance { get; private set; }
	/// <summary>The lit pools' centres on the ground (world).</summary>
	public Vector3[] Centres { get; } = new Vector3[Count];
	/// <summary>Lit (the chase is on).</summary>
	public bool On { get; private set; }
	/// <summary>How many have been reached (in order: the furthest index stood in, plus one).</summary>
	public int Reached { get; private set; }
	/// <summary>For tests: which light the player is standing in (-1: none).</summary>
	public int PlayerIn { get; private set; } = -1;

	private readonly SpotLight3D[] _spots = new SpotLight3D[Count];
	private readonly MeshInstance3D[] _cones = new MeshInstance3D[Count];
	private readonly AudioStreamPlayer3D[] _hums = new AudioStreamPlayer3D[Count];

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	/// <summary>Lays the four out between <paramref name="from"/> (the radio spot) and <paramref name="to"/> (the stairs' foot).</summary>
	public void Build(Vector3 from, Vector3 to)
	{
		var terrain = GroundSnap.FindTerrain(this);
		Vector3 line = (to - from) with { Y = 0 };
		Vector3 fwd = line.Normalized();
		Vector3 right = fwd.Cross(Vector3.Up).Normalized();
		var s = StoryManager.Instance;
		for (int i = 0; i < Count; i++)
		{
			Vector3 c = from + line * Along[i] + right * Aside[i];
			if (terrain != null) c.Y = terrain.HeightAt(c.X, c.Z);
			Centres[i] = c;
			if (s != null && s.HasFlag(Flag(i))) Reached = Mathf.Max(Reached, i + 1);
			// the trees kept out of the pool (the scatter builds deferred, after this)
			var clear = new ClearZone { Name = $"Clear{i}", Radius = BeamRadius + 2.5f, ClearFoliage = false };
			AddChild(clear);
			clear.GlobalPosition = c;
			// where a death after this one comes back: in its pool, facing on toward the next (or the stairs)
			Vector3 next = i + 1 < Count ? from + line * Along[i + 1] + right * Aside[i + 1] : to;
			var marker = new Node3D { Name = $"Respawn{i}" };
			AddChild(marker);
			marker.GlobalPosition = c;
			Vector3 look = (next - c) with { Y = 0 };
			marker.GlobalRotation = new Vector3(0, Mathf.Atan2(-look.X, -look.Z), 0);
			marker.AddToGroup(RespawnGroup(i));
			BuildLight(i, c);
		}
		GD.Print($"[story] Act 11: the four lights laid out, {line.Length():0} m from the radio spot to the stairs: "
			+ string.Join(", ", System.Linq.Enumerable.Select(Centres, p => $"({p.X:0},{p.Z:0})")));
	}

	private void BuildLight(int i, Vector3 ground)
	{
		Color col = Colours[i];
		var spot = new SpotLight3D
		{
			Name = $"Spot{i}", LightColor = col, LightEnergy = i == 3 ? 16f : 11f, SpotRange = Height + 6f,
			// the pool's edge just past the safe radius: a soft rim, the middle full
			SpotAngle = Mathf.RadToDeg(Mathf.Atan((BeamRadius + 0.6f) / Height)), SpotAngleAttenuation = 0.35f, SpotAttenuation = 0.2f,
			ShadowEnabled = true, LightVolumetricFogEnergy = 0.35f, Visible = false,
		};
		AddChild(spot);
		spot.GlobalPosition = ground + Vector3.Up * Height;
		spot.GlobalRotation = new Vector3(-Mathf.Pi / 2f, 0, 0);
		_spots[i] = spot;
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/chase_beam.gdshader") };
		mat.SetShaderParameter("colour", col);
		mat.SetShaderParameter("strength", i == 3 ? 0.5f : 0.32f);
		mat.SetShaderParameter("height", Height);
		var cone = new MeshInstance3D
		{
			Name = $"Shaft{i}", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = BeamRadius, Height = Height, RadialSegments = 24, Rings = 6, CapTop = false, CapBottom = false },
			MaterialOverride = mat,
		};
		AddChild(cone);
		cone.GlobalPosition = ground + Vector3.Up * Height * 0.5f;
		_cones[i] = cone;
		// the lamp itself, a hot point lost up in the dark
		var lamp = new MeshInstance3D
		{
			Name = $"Lamp{i}", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Mesh = new SphereMesh { Radius = 0.3f, Height = 0.4f, RadialSegments = 10, Rings = 5 },
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = col.Lightened(0.6f) },
		};
		cone.AddChild(lamp);
		lamp.Position = Vector3.Up * Height * 0.5f;
		const string hum = "res://assets/audio/sfx/bulb_buzz_loop.wav";
		if (ResourceLoader.Exists(hum))
		{
			var p = new AudioStreamPlayer3D { Name = $"Hum{i}", Stream = GD.Load<AudioStream>(hum), Bus = "Events", VolumeDb = -14f, UnitSize = 3f, MaxDistance = 24f, PitchScale = 0.55f + 0.08f * i };
			AddChild(p);
			p.GlobalPosition = ground + Vector3.Up * 2.5f;
			_hums[i] = p;
		}
	}

	/// <summary>Lit, all four: one after another from the nearest (<paramref name="instant"/>: all at once, a Continue).</summary>
	public void SwitchOn(bool instant)
	{
		if (On) return;
		On = true;
		GD.Print("[story] Act 11: the four lights come on, blue, purple, green, red");
		if (instant) { for (int i = 0; i < Count; i++) Light(i); return; }
		_ = Cutscene.Run(this, async ct =>
		{
			for (int i = 0; i < Count; i++)
			{
				Light(i);
				Audio.AudioDirector.OneShot(this, "relay_clunk", 1, Centres[i] + Vector3.Up * 6f, i == 0 ? 2f : -2f, "Events", 12f, 0.06f);
				await Cutscene.Wait(this, 0.55, ct);
			}
		});
	}

	private void Light(int i)
	{
		if (_spots[i] != null) _spots[i].Visible = true;
		if (_cones[i] != null) { _cones[i].Visible = true; _cones[i].GetChild<MeshInstance3D>(0).Visible = true; }
		if (_hums[i] != null && !_hums[i].Playing) _hums[i].Play();
	}

	/// <summary>Which lit pool <paramref name="world"/> stands in (flat distance under its radius plus
	/// <paramref name="margin"/>), or -1.</summary>
	public int BeamAt(Vector3 world, float margin = 0f)
	{
		if (!On) return -1;
		for (int i = 0; i < Count; i++)
			if (new Vector2(world.X - Centres[i].X, world.Z - Centres[i].Z).Length() < BeamRadius + margin) return i;
		return -1;
	}

	public override void _Process(double delta)
	{
		if (!On) return;
		var p = StoryBeat.Player(this);
		if (p == null) return;
		PlayerIn = BeamAt(p.GlobalPosition);
		// (2026-10-29, the first look shots) the shaft is drawn from outside: stood in it, its inside faces washed the whole
		// view in its colour; there, the pool and the light on them are enough
		if (p.CameraRig?.Camera is { } cam)
			for (int i = 0; i < Count; i++)
				if (_cones[i] != null)
					_cones[i].Visible = new Vector2(cam.GlobalPosition.X - Centres[i].X, cam.GlobalPosition.Z - Centres[i].Z).Length() > BeamRadius + 0.6f;
		// stood in: a save (and every one before it counts as passed)
		if (PlayerIn >= 0 && PlayerIn + 1 > Reached && !PlayerDeath.Dying)
		{
			var s = StoryManager.Instance;
			for (int i = Reached; i <= PlayerIn; i++) s?.SetFlag(Flag(i));
			Reached = PlayerIn + 1;
			GD.Print($"[story] Act 11: into the {Names[PlayerIn]} light - a save");
		}
	}
}
