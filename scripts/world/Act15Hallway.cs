using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.HallwayParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 15: the long hallway, out of the chamber under the stairwell. Its save is Act 14's end
/// (<see cref="Checkpoint.Act14Finished"/>).
///
/// First a claustrophobic industrial corridor, a body's width across and so tall its ceiling is lost:
/// raw concrete, strip lights in the floor, a dim green haze from somewhere far ahead. It is too long:
/// about 45 s at a walk (2026-10-13). Then it tapers out into a stretched, cavernous version of
/// itself, green lamps hanging on long cables out of the dark overhead.
///
/// In the middle of it stands the stalker (<see cref="HallStalker"/>; it replaced the shadow man, 2026-10-15), frozen, because the lights
/// are green. Once the player is past him, the lights play red light, green light: every 20-30 s they
/// flicker three times and turn red with a low siren, and he is right behind the player. Anyone still
/// moving while it is red is taken, and wakes just past him. About three minutes of it at a walk to the door
/// at the end: a janitor's closet, whose door swings shut behind them (<see cref="Checkpoint.Act15Finished"/>).
///
/// The flicker is three slow blinks, not a strobe; with Reduce Flashing it is three soft dips.
///
/// Local space: the floor is y=0; the hallway runs +Z from z=0 (the chamber's passage), centred on x=0.
/// </summary>
public partial class Act15Hallway : Node3D
{
	// (2026-10-12, the owner: "way too long", 45% off: was 324 and 950; 2026-10-13, "some of the hallways are way too long":
	// about a third off again, was 178 and 523; the red light, green light rule itself is unchanged)
	public const float Part1 = 120f, Taper = 30f, Part2 = 330f;
	public const float W1 = 1.6f, H1 = 22f, W2 = 3.8f, H2 = 34f;
	public const float ShadowZ = Part1 + Taper + 20f;
	public const float End = Part1 + Taper + Part2;
	public const float ClosetDepth = 2.6f, ClosetHalf = 1.2f, DoorWidth = 1.0f;
	/// <summary>The middle of the closet (Act 16's start), in this node's space.</summary>
	public static readonly Vector3 ClosetCentre = new(0, 0, End + ClosetDepth * 0.5f + 0.1f);

	public enum Phase { Waiting, Green, Flicker, Red, Done }

	// ---- for tests ----
	public Phase State { get; private set; } = Phase.Waiting;
	public int Reds { get; private set; }
	public bool DoorOpen { get; private set; }
	public bool InCloset { get; private set; }
	public bool Finished { get; private set; }
	public HallStalker Shadow => _shadow;
	public float PlayerZ { get; private set; }
	public Vector3 DoorWorld => ToGlobal(new Vector3(0, 0.05f, End - 0.8f));
	public Vector3 ClosetWorld => ToGlobal(ClosetCentre + Vector3.Up * 0.05f);
	public Vector3 ShadowSpotWorld => ToGlobal(new Vector3(0, 0, ShadowZ));
	public Vector3 AlongWorld(float z) => ToGlobal(new Vector3(0, 0.05f, z));
	/// <summary>Seconds until the lights next change (tests use it to play fair).</summary>
	public float Timer => _timer;

	private HallStalker _shadow;
	private StandardMaterial3D _lampMat, _stripMat2, _centreMat;
	// the centre row's colour and level (the phase's), and its failing: a soft dip now and then (see CentreFlicker)
	private Color _centreColor = Green;
	private float _centreLevel = 1f, _centreDip = 1f;
	private double _centreNext = 2.0, _centreT = -1.0;
	private bool _centreDouble;
	private readonly List<OmniLight3D> _lamps = new();
	private float _timer, _flickT, _redT;
	private int _flickStep;
	private bool _dying, _firstRed = true, _closing;
	private Node3D _doorHinge;
	private Interactable _doorUse;
	private AudioStreamPlayer _hum;
	private readonly RandomNumberGenerator _rng = new();
	// (2026-10-14h, the old-and-abandoned pass: the same green and red, meaning the same thing at the same moments, a step
	// less neon: an old lamp's green, not a laser's; and the air a dirty dim haze with only a breath of green in it, the
	// red still swinging it dark red)
	private static readonly Color Green = new(0.4f, 0.95f, 0.42f), Red = new(1f, 0.1f, 0.05f);
	private static readonly Color FogGreen = new(0.017f, 0.024f, 0.016f), FogRed = new(0.045f, 0.006f, 0.004f);

	public override void _Ready() => Callable.From(Build).CallDeferred();

	// ------------------------------------------------------------------ geometry

	private static StandardMaterial3D Concrete(string key, Texture2D tex, float scale) => new()
	{
		AlbedoTexture = tex, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * scale,
		Roughness = 0.9f, MetallicSpecular = 0.25f, VertexColorUseAsAlbedo = true,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, ResourceName = key,
	};

	/// <summary>(2026-10-14c) The closet's walls: painted concrete block (the scan's courses, about 20 cm), an old
	/// institutional cream gone grey, rough.</summary>
	private static StandardMaterial3D ClosetWallMat => _closetWall ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_closet_wall", Roughness = 0.95f, MetallicSpecular = 0.25f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.45f,
	}, "concrete_wall_004", 1f, 1.2f, new Color(0.78f, 0.74f, 0.62f));
	/// <summary>The closet's floor: worn, stained concrete, darker than the walls.</summary>
	private static StandardMaterial3D ClosetFloorMat => _closetFloor ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_closet_floor", Roughness = 0.9f, MetallicSpecular = 0.3f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.6f,
	}, "concrete_floor_worn_001", 1f, 1f, new Color(0.6f, 0.57f, 0.52f));
	private static StandardMaterial3D CoveMat => _cove ??= new StandardMaterial3D
	{
		ResourceName = "hw_closet_cove", AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.7f, MetallicSpecular = 0.3f, VertexColorUseAsAlbedo = true,
	};
	private static StandardMaterial3D _closetWall, _closetFloor, _cove;

	private static StandardMaterial3D Glow(Color c, float energy) => new()
	{
		AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
	};

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("Act15Hallway");
		_rng.Seed = 1515;
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		var wall = new MeshKit();
		// (2026-10-14h, the old-and-abandoned pass: an old service corridor's painted block, two-tone at a dado line,
		// chipped, scuffed, water-stained; it was smooth concrete, and its second set smeared along the tall walls in lines)
		wall.Mat(HallWallMat(GlobalPosition.Y));
		var dark = new MeshKit();
		// (the floor: old vinyl tile, weathered by the kit; it was the stairwell's grimy concrete)
		dark.Mat(RoomSurface.Of(CorridorTextures.FloorMat, RoomSurface.Concrete with { Streaks = 0f, Tide = 0f, FloorDirt = 0.8f, Wear = 0.7f }, 15));
		_doors.Clear();
		void Box(MeshKit k, Vector3 c, Vector3 s, Basis? rot = null, bool collide = true, float tint = 1f)
		{
			k.Color = Colors.White * tint;
			BuildKit.Box(k, c, s, 1f, BuildKit.Face.None, rot);
			if (collide) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = s }, Transform = new Transform3D(rot ?? Basis.Identity, c) });
		}

		// part 1: the narrow corridor
		for (float z = 0; z < Part1; z += 36f)
		{
			float len = Mathf.Min(36f, Part1 - z), cz = z + len * 0.5f;
			// (2026-10-15: the draw is kept, so red light, green light's dice are as they were, but the tint isn't used: it
			// stepped hard every 36 m. The wall shader drifts the paint's fade along the hall itself now)
			_ = _rng.Randf();
			const float tint = 0.93f;
			Box(wall, new Vector3(-W1 * 0.5f - 0.2f, H1 * 0.5f, cz), new Vector3(0.4f, H1, len), null, true, tint);
			Box(wall, new Vector3(W1 * 0.5f + 0.2f, H1 * 0.5f, cz), new Vector3(0.4f, H1, len), null, true, tint * 0.95f);
			Box(dark, new Vector3(0, -0.1f, cz), new Vector3(W1 + 0.8f, 0.2f, len));
			Box(wall, new Vector3(0, H1 + 0.2f, cz), new Vector3(W1 + 0.8f, 0.4f, len), null, false, 0.5f);
		}
		// pilasters and ledges: the wall's rhythm, so the length reads
		for (float z = 3f; z < Part1; z += 6f)
			foreach (float s in new[] { -1f, 1f })
				Box(wall, new Vector3(s * (W1 * 0.5f - 0.04f), H1 * 0.5f, z), new Vector3(0.08f, H1, 0.45f), null, false, 0.75f);
		foreach (float y in new[] { 3.2f, 9.5f, 16f })
			foreach (float s in new[] { -1f, 1f })
				Box(wall, new Vector3(s * (W1 * 0.5f - 0.05f), y, Part1 * 0.5f), new Vector3(0.1f, 0.12f, Part1), null, false, 0.7f);
		// doors in the walls that go nowhere
		// (2026-10-14h: old steel service doors, shut, a wired-glass panel in each, dark behind; they were black openings. The two
		// draws a door takes from the hall's dice are as they were, in the same order: red light, green light comes after)
		for (float z = 23f; z < Part1 - 10f; z += _rng.RandfRange(28f, 46f))
		{
			float s = _rng.Randf() < 0.5f ? -1f : 1f;
			// (between two pilasters: they're every 6 m from 3 m)
			_doors.Add((s, Mathf.Round(z / 6f) * 6f, W1 * 0.5f));
		}

		// the taper: the walls lean apart and the ceiling climbs
		float z0 = Part1, z1 = Part1 + Taper;
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 a = new(s * (W1 * 0.5f + 0.2f), 0, z0), b = new(s * (W2 * 0.5f + 0.2f), 0, z1);
			Vector3 d = b - a;
			var rot = new Basis(Vector3.Up, Mathf.Atan2(d.X, d.Z));
			Box(wall, (a + b) * 0.5f + Vector3.Up * H2 * 0.5f, new Vector3(0.4f, H2, d.Length() + 0.4f), rot, true, 0.85f);
		}
		Box(dark, new Vector3(0, -0.1f, (z0 + z1) * 0.5f), new Vector3(W2 + 1f, 0.2f, Taper));
		Box(wall, new Vector3(0, H2 + 0.2f, (z0 + z1) * 0.5f), new Vector3(W2 + 1f, 0.4f, Taper), null, false, 0.5f);
		Box(wall, new Vector3(0, (H1 + H2) * 0.5f, z0 + 0.2f), new Vector3(W1 + 0.8f, H2 - H1, 0.4f), null, false, 0.6f);

		// part 2: the stretched hall
		for (float z = z1; z < End; z += 40f)
		{
			float len = Mathf.Min(40f, End - z), cz = z + len * 0.5f;
			_ = _rng.Randf();   // (the draw kept; the tint isn't used, see part 1)
			const float tint = 0.9f;
			Box(wall, new Vector3(-W2 * 0.5f - 0.2f, H2 * 0.5f, cz), new Vector3(0.4f, H2, len), null, true, tint);
			Box(wall, new Vector3(W2 * 0.5f + 0.2f, H2 * 0.5f, cz), new Vector3(0.4f, H2, len), null, true, tint * 0.95f);
			Box(dark, new Vector3(0, -0.1f, cz), new Vector3(W2 + 0.8f, 0.2f, len));
			Box(wall, new Vector3(0, H2 + 0.2f, cz), new Vector3(W2 + 0.8f, 0.4f, len), null, false, 0.5f);
		}
		for (float z = z1 + 4f; z < End; z += 8f)
			foreach (float s in new[] { -1f, 1f })
				Box(wall, new Vector3(s * (W2 * 0.5f - 0.06f), H2 * 0.5f, z), new Vector3(0.12f, H2, 0.7f), null, false, 0.75f);
		foreach (float y in new[] { 5f, 14f, 24f })
			foreach (float s in new[] { -1f, 1f })
				Box(wall, new Vector3(s * (W2 * 0.5f - 0.07f), y, (z1 + End) * 0.5f), new Vector3(0.14f, 0.16f, End - z1), null, false, 0.7f);

		// the end wall, with the closet door in it
		float ew = W2 * 0.5f + 0.4f;
		Box(wall, new Vector3(-(ew + DoorWidth * 0.5f) * 0.5f, H2 * 0.5f, End + 0.1f), new Vector3(ew - DoorWidth * 0.5f, H2, 0.2f), null, true, 0.8f);
		Box(wall, new Vector3((ew + DoorWidth * 0.5f) * 0.5f, H2 * 0.5f, End + 0.1f), new Vector3(ew - DoorWidth * 0.5f, H2, 0.2f), null, true, 0.8f);
		Box(wall, new Vector3(0, (H2 + 2.15f) * 0.5f, End + 0.1f), new Vector3(DoorWidth, H2 - 2.15f, 0.2f), null, true, 0.8f);
		wall.CommitTo(this, "Walls", true);
		dark.CommitTo(this, "Floor", true);

		BuildStrips();
		BuildLights();
		BuildFogLayer();
		BuildDressing(body);
		BuildCloset(body);

		_shadow = new HallStalker { Name = "HallStalker" };
		AddChild(_shadow);
		_shadow.Position = new Vector3(0, 0, ShadowZ);   // facing -Z: the way the player comes
		_hum = new AudioStreamPlayer { Name = "Hum", Bus = "Unnatural", VolumeDb = -80f };
		if (ResourceLoader.Exists("res://assets/audio/ambient/hall_hum_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/hall_hum_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_hum.Stream = wav;
		}
		AddChild(_hum);
		// Continue into Act 16's save: the door is shut behind them and the hall is dark
		if (StoryManager.Instance is { } sm && sm.Current >= Checkpoint.Act15Finished)
		{
			Finished = InCloset = true;
			State = Phase.Done;
			foreach (var l in _lamps) l.Visible = false;
			ArmCloset();
		}
		SetProcess(true);
		// a kilometre of corridor: a tunnel, every sound running away down it and coming back
		AudioDirector.Zone(this, new Vector3(0, H2 * 0.5f, End * 0.5f), new Vector3(W2 + 0.4f, H2, End + 2f), AudioDirector.Space.Tunnel, "HallwayVerb");
		GD.Print($"[story] Act 15: the hallway - {End:0} m to the closet door, the shadow man at {ShadowZ:0} m");
		// (2026-10-14h) the hall's dice once it's built: red light, green light's timings all come after this, so a dressing
		// pass that leaves this number alone has left the game alone
		GD.Print($"[rlgl] hall dice after build: state {_rng.State}");
	}

	/// <summary>The strip lights in the floor, down both sides, three thin bars to a segment.
	/// (2026-10-14h, the old-and-abandoned pass; Dan: keep them, but an old abandoned service corridor's) Each segment its own:
	/// its diffuser yellowed, grime lying over it, some dimmer than the next, a run of them here and there burnt out,
	/// a few sagging and buzzing on their own time, a lens cracked and lying askew. Their colour and level are still
	/// the hall's, red light, green light's (<see cref="SyncStrips"/>): the dirt is drawn over the signal, never instead of it.</summary>
	private void BuildStrips()
	{
		// (what red light, green light writes its colour and level to: kept as they were, read each frame by SyncStrips)
		_stripMat2 = Glow(Green, 1.3f);
		_centreMat = Glow(Green, 1.2f);
		_strip1 = StripMat("hw_strips1", new Color(0.38f, 0.82f, 0.4f), 1.7f);
		_strip2 = StripMat("hw_strips2", Green, 2.1f);
		_stripC = StripMat("hw_strips_centre", Green, 1.9f);
		var s1 = new MeshKit();
		s1.Mat(_strip1);
		var s2 = new MeshKit();
		s2.Mat(_strip2);
		// (2026-10-14c) each segment's bars sit in a low steel channel let into the floor, scuffed at its ends
		var housing = new MeshKit();
		housing.Mat(CorridorTextures.ChannelMat);
		housing.Color = Colors.White;
		var shards = new MeshKit();
		shards.Mat(CorridorTextures.ShardMat);
		shards.Color = Colors.White;
		// (their wear: their own dice, not the hall's)
		var wr = new RandomNumberGenerator { Seed = 151514 };
		var deadRun = new Dictionary<float, int>();
		Color Wear(float side, bool dying = false)
		{
			// a run of them burnt out together now and then; otherwise each its own brightness
			deadRun.TryGetValue(side, out int run);
			if (run == 0 && wr.Randf() < 0.025f) run = wr.RandiRange(2, 6);
			float bright = wr.RandfRange(0.45f, 1f);
			float fail = 0f;
			if (run > 0) { run--; bright = wr.RandfRange(0.0f, 0.05f); }
			else if (dying) bright = wr.RandfRange(0.03f, 0.08f);
			else if (wr.Randf() < 0.05f) { bright = wr.RandfRange(0.01f, 0.06f); }
			else if (wr.Randf() < 0.06f) fail = wr.RandfRange(0.05f, 1f);
			deadRun[side] = run;
			return new Color(bright, fail, wr.RandfRange(0.25f, 1f), 1f);
		}
		void Strip(MeshKit k, float x, float z, Color wear)
		{
			k.Color = wear;
			for (int b = -1; b <= 1; b++)
				BuildKit.Box(k, new Vector3(x + b * 0.06f, 0.006f, z), new Vector3(0.03f, 0.012f, 0.75f));
			housing.Color = Colors.White * wr.RandfRange(0.7f, 1.1f);
			BuildKit.Box(housing, new Vector3(x, 0.005f, z), new Vector3(0.21f, 0.01f, 0.82f), 1f, BuildKit.Face.NY);
			// a cracked lens: a piece of it lying askew over the bars, dimming them under it
			if (wr.Randf() < 0.07f)
			{
				float len = wr.RandfRange(0.12f, 0.4f), dz = wr.RandfRange(-0.3f, 0.3f);
				var rot = new Basis(Vector3.Up, wr.RandfRange(-0.5f, 0.5f)) * new Basis(Vector3.Forward, wr.RandfRange(-0.12f, 0.12f));
				shards.Color = Colors.White * wr.RandfRange(0.6f, 1f);
				BuildKit.Box(shards, new Vector3(x + wr.RandfRange(-0.03f, 0.03f), 0.016f, z + dz), new Vector3(0.17f, 0.004f, len), 1f, BuildKit.Face.NY, rot);
			}
		}
		for (float z = 1f; z < Part1 + Taper; z += 1f)
		{
			float w = z < Part1 ? W1 : Mathf.Lerp(W1, W2, (z - Part1) / Taper);
			foreach (float s in new[] { -1f, 1f }) Strip(s1, s * (w * 0.5f - 0.22f), z, Wear(s));
		}
		for (float z = Part1 + Taper; z < End - 0.5f; z += 1f)
			foreach (float s in new[] { -1f, 1f }) Strip(s2, s * (W2 * 0.5f - 0.5f), z, Wear(s + 10f));
		s1.CommitTo(this, "Strips1", false);
		s2.CommitTo(this, "Strips2", false);
		// (the owner) a row down the middle too, going bad: the whole row dips now and then, and here and there a
		// segment has all but died (which ones: the same dice as before)
		var c1 = new MeshKit();
		c1.Mat(_stripC);
		var rng = new RandomNumberGenerator { Seed = 1515 };
		for (float z = 1.5f; z < End - 0.5f; z += 1f)
			Strip(c1, 0f, z, Wear(0f, rng.Randf() < 0.09f));
		c1.CommitTo(this, "StripsCentre", false);
		housing.CommitTo(this, "StripChannels", false);
		shards.CommitTo(this, "StripShards", false);
		SyncStrips();
		// (and again just before every frame is drawn, after everything has run: a change of light shows on the frame it happens)
		RenderingServer.FramePreDraw += SyncStrips;
	}

	private ShaderMaterial _strip1, _strip2, _stripC;

	public override void _ExitTree()
	{
		if (_strip2 != null) RenderingServer.FramePreDraw -= SyncStrips;
	}
	private static Shader _stripShader;

	private static ShaderMaterial StripMat(string name, Color c, float energy)
	{
		_stripShader ??= GD.Load<Shader>("res://assets/shaders/old_strip_light.gdshader");
		var m = new ShaderMaterial { Shader = _stripShader, ResourceName = name };
		m.SetShaderParameter("signal_color", c);
		m.SetShaderParameter("level", 1f);
		m.SetShaderParameter("energy", energy);
		m.SetShaderParameter("noise_tex", RoomSurface.Noise);
		return m;
	}

	/// <summary>For tests: the fog lying over the floor of the hall's second half.</summary>
	public FogZone FloorFog { get; private set; }
	/// <summary>How strongly the fog over the strips takes their light.</summary>
	[Export] public float FogGlow = 0.7f;

	/// <summary>(2026-10-19, the owner: "a light fog layer ... halfway through act 15 so the lights from the floor can
	/// illuminate the light layer of fog with their colors, obscuring how far the door is away") from where the hall opens
	/// out to the closet door, a fog lying on the floor, about chest deep, banked and drifting: the strips under it light
	/// it their colour (green, then red with the lights; the strips are lit meshes, so the fog's glow is set from their
	/// signal, SyncStrips), the hanging lamps light it from above, and the far end of the hall goes into it.</summary>
	private void BuildFogLayer()
	{
		float z0 = Part1 + Taper, len = End - z0 + 6f;
		FloorFog = new FogZone
		{
			Name = "FloorFog", Mode = FogZone.Kind.Layer, Density = 0.12f, LayerHeight = 2.4f, FloorY = -2.5f,   // (the hall's floor, from the box's middle)
			NoiseAmount = 0.5f, NoiseScale = 0.06f, Wind = new Vector3(0.05f, 0f, -0.25f),   // (a draught down the hall toward them)
			Albedo = new Color(0.55f, 0.58f, 0.55f), Edge = 0.08f, VolumetricLength = 80f,
			Size = new Vector3(W2 + 0.4f, 7f, len), Position = new Vector3(0, -1f + 3.5f, z0 + len * 0.5f - 3f),
		};
		AddChild(FloorFog);
		SyncStrips();
	}

	/// <summary>The strips' signal, from what red light, green light last set (<c>SetLights</c>) and the centre row's dips.</summary>
	private void SyncStrips()
	{
		if (_strip2 == null) return;
		if (FloorFog != null)
		{
			float x = W2 * 0.5f - 0.5f;
			FloorFog.SetGlow(_stripMat2.Emission, _stripMat2.EmissionEnergyMultiplier / 1.3f * FogGlow, new Vector3(-x, 0f, x), 0.45f, 0.7f);
		}
		float flick = (GameSettings.Instance?.ReduceFlashing ?? false) ? 0f : 1f;
		_strip2.SetShaderParameter("signal_color", _stripMat2.Emission);
		_strip2.SetShaderParameter("level", _stripMat2.EmissionEnergyMultiplier / 1.3f);
		_strip2.SetShaderParameter("flicker_ok", flick);
		// (the centre row from the phase's own colour and level and its dip: the same as its carrier gets, a frame sooner)
		_stripC.SetShaderParameter("signal_color", _centreColor);
		_stripC.SetShaderParameter("level", Mathf.Max(0.15f, _centreLevel) * _centreDip);
		_stripC.SetShaderParameter("flicker_ok", flick);
		_strip1.SetShaderParameter("flicker_ok", flick);
	}

	/// <summary>The centre row's failing: every few seconds a soft dip (down to a third and back over a quarter of a
	/// second), sometimes two close together. Slow and never off: a light going bad, not a strobe.</summary>
	private void CentreFlicker(double dt)
	{
		if (_centreMat == null) return;
		if (_centreT < 0.0)
		{
			_centreNext -= dt;
			if (_centreNext <= 0.0) { _centreT = 0.0; _centreDouble = GD.Randf() < 0.35f; }
		}
		else
		{
			_centreT += dt;
			const double dip = 0.28, gap = 0.2;
			double t = _centreT;
			if (_centreDouble && t > dip + gap) t -= dip + gap;
			_centreDip = t < dip ? 1f - 0.68f * Mathf.Sin((float)(t / dip) * Mathf.Pi) : 1f;
			if (_centreT > (_centreDouble ? dip * 2 + gap : dip)) { _centreT = -1.0; _centreDip = 1f; _centreNext = GD.RandRange(1.4, 4.6); }
		}
		float level = Mathf.Max(0.15f, _centreLevel);
		_centreMat.AlbedoColor = _centreColor * level * Mathf.Lerp(0.55f, 1f, _centreDip);
		_centreMat.Emission = _centreColor;
		_centreMat.EmissionEnergyMultiplier = 1.2f * level * _centreDip;
		SyncStrips();
	}

	/// <summary>Part 1: a few weak lights on the walls, and a stronger glow where the hall opens out (the green seen
	/// from far off). Part 2: dome lamps hanging on long cables out of the dark.
	/// (2026-10-14h) Part 1's were green omnis floating in the air; now old caged bulbs on brackets, warm, going bad:
	/// one steady, one flickering, one buzzing, some dead.</summary>
	private void BuildLights()
	{
		var bracket = new MeshKit();
		bracket.Mat(CorridorTextures.ChannelMat);
		bracket.Color = Colors.White * 0.8f;
		FailingBulb.Mode[] modes = { FailingBulb.Mode.Steady, FailingBulb.Mode.Flicker, FailingBulb.Mode.Dead, FailingBulb.Mode.Steady, FailingBulb.Mode.Buzz, FailingBulb.Mode.Dead, FailingBulb.Mode.Steady };
		int bi = 0;
		for (float z = 12f; z < Part1 - 4f; z += 18f, bi++)
		{
			float s = bi % 2 == 0 ? -1f : 1f, wx = s * W1 * 0.5f;
			// between two pilasters, under the first ledge
			float zc = Mathf.Round(z / 6f) * 6f;
			Vector3 at = new(wx - s * 0.2f, 2.78f, zc);
			bracket.Cylinder(new Vector3(wx, 2.95f, zc), at + Vector3.Up * 0.13f, 0.012f, 0.012f, 5, false);
			BuildKit.Box(bracket, new Vector3(wx - s * 0.008f, 2.95f, zc), new Vector3(0.016f, 0.12f, 0.1f));
			var b = new FailingBulb
			{
				Name = $"WallBulb{bi}", Seed = 1500 + bi, State = modes[bi % modes.Length], LightColor = new Color(1f, 0.74f, 0.46f),
				Energy = 0.5f, Range = 8f, Attenuation = 1.2f, ActiveRange = 50f, Position = at,
			};
			AddChild(b);
		}
		// one more on the wall just past where he first stands: the wall behind him lit, so he is a dark shape against it
		{
			float wx = -W2 * 0.5f, zc = ShadowZ + 3.2f;
			Vector3 at = new(wx + 0.2f, 2.9f, zc);
			bracket.Cylinder(new Vector3(wx, 3.07f, zc), at + Vector3.Up * 0.13f, 0.012f, 0.012f, 5, false);
			BuildKit.Box(bracket, new Vector3(wx + 0.008f, 3.07f, zc), new Vector3(0.016f, 0.12f, 0.1f));
			AddChild(new FailingBulb
			{
				Name = "WallBulbShadow", Seed = 1599, State = FailingBulb.Mode.Steady, LightColor = new Color(1f, 0.74f, 0.46f),
				Energy = 0.7f, Range = 7f, Attenuation = 1.1f, ActiveRange = 50f, Position = at,
			});
		}
		bracket.CommitTo(this, "WallBulbBrackets", false);
		foreach (float z in new[] { Part1 + 4f, Part1 + 16f, Part1 + 28f })
			AddChild(new OmniLight3D { Position = new Vector3(0, 6f, z), LightColor = new Color(0.45f, 0.85f, 0.48f), LightEnergy = 2.0f, OmniRange = 22f, ShadowEnabled = false });

		_lampMat = Glow(Green, 3f);
		var cable = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 0.6f };
		var k = new MeshKit();
		var lk = new MeshKit();
		k.Mat(cable); k.Color = Colors.White;
		var sk = new MeshKit();
		// (2026-10-14h: old enamel shades, chipped to rust; they were a clean dark metal)
		sk.Mat(CorridorTextures.LampShadeMat); sk.Color = Colors.White;
		lk.Mat(_lampMat); lk.Color = Colors.White;
		for (float z = Part1 + Taper + 5f; z < End - 3f; z += 10f)
		{
			float y = 5.2f + 0.6f * Mathf.Sin(z * 0.37f);
			Vector3 at = new(0, y, z);
			k.Cylinder(at, new Vector3(0, H2, z), 0.012f, 0.012f, 4, false);
			sk.Color = Colors.White * (0.75f + 0.25f * Mathf.Abs(Mathf.Sin(z * 1.7f)));
			sk.Cylinder(at + Vector3.Down * 0.02f, at + Vector3.Down * 0.32f, 0.08f, 0.42f, 12, false);
			// the shade's rolled rim
			sk.Cylinder(at + Vector3.Down * 0.31f, at + Vector3.Down * 0.34f, 0.43f, 0.43f, 12, false);
			lk.Cylinder(at + Vector3.Down * 0.2f, at + Vector3.Down * 0.3f, 0.09f, 0.09f, 8, true);
			var light = new OmniLight3D
			{
				Position = at + Vector3.Down * 0.6f, LightColor = Green, LightEnergy = 1.5f, OmniRange = 12f, OmniAttenuation = 1.3f,
				ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 45f, DistanceFadeLength = 12f,
			};
			AddChild(light);
			_lamps.Add(light);
		}
		k.CommitTo(this, "Cables", false);
		sk.CommitTo(this, "Shades", false);
		lk.CommitTo(this, "Bulbs", false);
	}

	/// <summary>The closet at the end: tight, dark, shelves of cleaning things, a mop on the floor,
	/// and its door, which opens inward from the hall.</summary>
	private void BuildCloset(StaticBody3D body)
	{
		float z0 = End + 0.2f, z1 = End + 0.2f + ClosetDepth, h = 2.6f;
		// (2026-10-14c: painted block walls, a worn concrete floor and a rubber base where they meet; the walls, floor and
		// ceiling were all one pale board-marked concrete, its pits blown up to the size of coins)
		// (2026-10-14h: weathered and baked by the interiors kit, drawn finely enough for the bake to show)
		var k = new MeshKit();
		k.Mat(RoomSurface.Of(ClosetWallMat, RoomSurface.Concrete with { Streaks = 0.6f, StreakLen = 1.2f }, 16));
		k.Color = Colors.White;
		var fk = new MeshKit();
		fk.Mat(RoomSurface.Of(ClosetFloorMat, RoomSurface.Concrete with { FloorDirt = 0.8f }, 16));
		fk.Color = Colors.White;
		k.Tess = fk.Tess = 0.3f;
		var closetShade = ClosetShade(z0, z1, h);
		void Slab(MeshKit kit, Vector3 c, Vector3 s, float tint = 1f)
		{
			kit.Color = Colors.White * tint;
			BuildKit.Box(kit, c, s, 1f);
			body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		Slab(k, new Vector3(-ClosetHalf - 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, ClosetDepth));
		Slab(k, new Vector3(ClosetHalf + 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, ClosetDepth), 0.94f);
		Slab(k, new Vector3(0, h * 0.5f, z1 + 0.1f), new Vector3(ClosetHalf * 2f + 0.4f, h, 0.2f), 0.97f);
		Slab(k, new Vector3(0, h + 0.1f, (z0 + z1) * 0.5f), new Vector3(ClosetHalf * 2f + 0.4f, 0.2f, ClosetDepth), 0.6f);
		Slab(fk, new Vector3(0, -0.1f, (z0 + z1) * 0.5f), new Vector3(ClosetHalf * 2f + 0.4f, 0.2f, ClosetDepth + 0.2f));
		k.Tess = fk.Tess = 0f;
		closetShade.Apply(k);
		closetShade.Apply(fk);
		k.CommitTo(this, "Closet", true);
		fk.CommitTo(this, "ClosetFloor", true);
		// the black rubber base round the foot of the walls (not across the doorway)
		var cove = new MeshKit();
		cove.Mat(CoveMat);
		cove.Color = Colors.White;
		const float ch = 0.1f, ct = 0.012f;
		BuildKit.Box(cove, new Vector3(-ClosetHalf + ct * 0.5f, ch * 0.5f, (z0 + z1) * 0.5f), new Vector3(ct, ch, ClosetDepth));
		BuildKit.Box(cove, new Vector3(ClosetHalf - ct * 0.5f, ch * 0.5f, (z0 + z1) * 0.5f), new Vector3(ct, ch, ClosetDepth));
		BuildKit.Box(cove, new Vector3(0, ch * 0.5f, z1 - ct * 0.5f), new Vector3(ClosetHalf * 2f - 2f * ct, ch, ct));
		foreach (float s in new[] { -1f, 1f })
			BuildKit.Box(cove, new Vector3(s * (DoorWidth * 0.5f + 0.06f + (ClosetHalf - DoorWidth * 0.5f - 0.06f) * 0.5f), ch * 0.5f, z0 + ct * 0.5f),
				new Vector3(ClosetHalf - DoorWidth * 0.5f - 0.06f - ct, ch, ct));
		cove.CommitTo(this, "ClosetBase", false);
		// the door's wall from inside: the same painted block (it was the hall's raw concrete), round the frame
		var face = new MeshKit();
		face.Mat(RoomSurface.Of(ClosetWallMat, RoomSurface.Concrete with { Streaks = 0.6f, StreakLen = 1.2f }, 16));
		face.Color = Colors.White * 0.97f;
		face.Tess = 0.3f;
		float side = ClosetHalf - DoorWidth * 0.5f;
		foreach (float s in new[] { -1f, 1f })
			BuildKit.Box(face, new Vector3(s * (DoorWidth * 0.5f + side * 0.5f), h * 0.5f, z0 + 0.0035f), new Vector3(side, h, 0.005f));
		BuildKit.Box(face, new Vector3(0, (h + 2.15f) * 0.5f, z0 + 0.0035f), new Vector3(DoorWidth, h - 2.15f, 0.005f));
		face.Tess = 0f;
		closetShade.Apply(face);
		face.CommitTo(this, "ClosetDoorWall", true);
		// shelves down both sides and the back, crowded with bottles, tins and rags
		var sk = new MeshKit();
		sk.Mat(StairwellTextures.SteelMat);
		sk.Color = new Color(0.6f, 0.6f, 0.55f);
		var bk = new MeshKit();
		bk.Mat(new StandardMaterial3D { Roughness = 0.5f, VertexColorUseAsAlbedo = true });
		// (2026-10-13c: the paint cans, jugs, sprays and bottles turned from their real shapes, labelled and aged; they
		// were plain coloured cylinders, placeholders. Their own seed: the same shelves every game)
		var stock = new MeshKit();
		var shadows = new MeshKit();   // (their shadows from plain stand-ins: a tenth of the triangles in every shadow pass)
		var srng = new RandomNumberGenerator { Seed = 1616 };
		JanitorShelf.Kind Pick(float y)
		{
			float r = srng.Randf();
			// the heavy paint low down, the bottles and sprays up high
			if (y < 0.8f) return r < 0.45f ? JanitorShelf.Kind.Gallon : r < 0.75f ? JanitorShelf.Kind.Jug : JanitorShelf.Kind.Quart;
			if (y < 1.3f) return r < 0.3f ? JanitorShelf.Kind.Jug : r < 0.55f ? JanitorShelf.Kind.Quart : r < 0.8f ? JanitorShelf.Kind.Spray : JanitorShelf.Kind.Gallon;
			return r < 0.3f ? JanitorShelf.Kind.Spray : r < 0.55f ? JanitorShelf.Kind.Bottle : r < 0.8f ? JanitorShelf.Kind.Quart : JanitorShelf.Kind.Pint;
		}
		// a row of them along a shelf from a to b, facing `face` (yaw), a gap here and there
		// (2026-10-15: each thing pushed back or pulled forward a little, d0..d1 along the row's back; they stood ruled in line)
		void Row(float y, Vector3 a, Vector3 b, float faceYaw, float gaps = 0.12f, float d0 = -0.015f, float d1 = 0.015f)
		{
			float len = a.DistanceTo(b), u = srng.RandfRange(0f, 0.04f);
			var along = (b - a) / len;
			var back = along.Cross(Vector3.Up);
			while (true)
			{
				var kind = Pick(y);
				float w = JanitorShelf.Footprint(kind);
				if (u + w > len) break;
				if (srng.Randf() < gaps) { u += srng.RandfRange(0.06f, 0.16f); continue; }
				var at = a + along * (u + w * 0.5f) + back * srng.RandfRange(d0, d1) + Vector3.Up * (y + 0.015f);
				JanitorShelf.Add(stock, kind, at, faceYaw + srng.RandfRange(-0.45f, 0.45f), srng);
				JanitorShelf.ShadowProxy(shadows, kind, at);
				u += w + srng.RandfRange(0.005f, 0.04f);
			}
		}
		foreach (float y in new[] { 0.5f, 1.05f, 1.6f, 2.1f })
		{
			// (the hallway's own draws, as the old placeholder shelves made them: what comes after them, red light, green
			// light's timings among it, comes out the same as before; Dan, 2026-10-13: "leave red light green light as it was")
			for (float x = -ClosetHalf + 0.15f; x < ClosetHalf - 0.1f; x += _rng.RandfRange(0.12f, 0.22f))
			{ _rng.RandiRange(0, 4); _rng.RandfRange(0.14f, 0.3f); _rng.RandfRange(0.035f, 0.06f); }
			for (float z = z0 + 0.6f; z < z1 - 0.5f; z += _rng.RandfRange(0.14f, 0.24f))
			{ _rng.RandiRange(0, 4); _rng.RandfRange(0.12f, 0.26f); }
			BuildKit.Box(sk, new Vector3(0, y, z1 - 0.2f), new Vector3(ClosetHalf * 2f, 0.03f, 0.36f));
			BuildKit.Box(sk, new Vector3(-ClosetHalf + 0.18f, y, (z0 + z1) * 0.5f + 0.2f), new Vector3(0.36f, 0.03f, ClosetDepth - 0.8f));
			// the back shelf two deep (the back row a little along, behind the gaps), the side shelf one
			Row(y, new Vector3(-ClosetHalf + 0.4f, 0, z1 - 0.1f), new Vector3(ClosetHalf - 0.05f, 0, z1 - 0.1f), 0f, 0.4f, 0f, 0.008f);
			Row(y, new Vector3(-ClosetHalf + 0.38f, 0, z1 - 0.28f), new Vector3(ClosetHalf - 0.05f, 0, z1 - 0.28f), 0f, 0.12f, -0.035f, 0f);
			Row(y, new Vector3(-ClosetHalf + 0.18f, 0, z0 + 0.62f), new Vector3(-ClosetHalf + 0.18f, 0, z1 - 0.42f), -Mathf.Pi * 0.5f, 0.12f, -0.02f, 0.02f);
		}
		// (2026-10-15: the shelves and their stock can't be walked through: a box round each unit, out to the shelves' fronts;
		// the floor between, the switch, the door and the trigger inside are all clear of them)
		body.AddChild(new CollisionShape3D { Name = "BackShelves", Position = new Vector3(0, 1.15f, z1 - 0.2f), Shape = new BoxShape3D { Size = new Vector3(ClosetHalf * 2f, 2.3f, 0.4f) } });
		body.AddChild(new CollisionShape3D { Name = "SideShelves", Position = new Vector3(-ClosetHalf + 0.19f, 1.15f, (z0 + z1) * 0.5f + 0.2f), Shape = new BoxShape3D { Size = new Vector3(0.38f, 2.3f, ClosetDepth - 0.8f) } });
		foreach (float x in new[] { -ClosetHalf + 0.02f, ClosetHalf - 0.02f })
			foreach (float z in new[] { z1 - 0.02f, z1 - 0.38f })
				sk.Cylinder(new Vector3(x, 0, z), new Vector3(x, 2.2f, z), 0.012f, 0.012f, 4, false);
		// the mop, fallen across the floor, and its bucket
		sk.Color = new Color(0.45f, 0.32f, 0.2f);
		sk.Cylinder(new Vector3(0.9f, 0.04f, z0 + 0.5f), new Vector3(-0.3f, 0.03f, z1 - 0.7f), 0.016f, 0.016f, 6, true);
		var floor = new MeshKit();
		JanitorShelf.MopHead(floor, new Vector3(-0.3f, 0f, z1 - 0.7f), new Vector3(-0.3f, 0f, z1 - 0.7f) - new Vector3(0.9f, 0f, z0 + 0.5f), srng);
		JanitorShelf.Bucket(floor, new Vector3(0.75f, 0, z0 + 0.45f), 0.4f);
		// a tin knocked off the side shelf, in what it spilled
		JanitorShelf.Spilled(floor, new Vector3(-0.62f, 0f, z0 + 1.15f), 2.2f, new Color(0.3f, 0.1f, 0.07f), srng);
		// a light switch by the door (Act 16)
		bk.Color = new Color(0.85f, 0.84f, 0.8f);
		BuildKit.Box(bk, new Vector3(DoorWidth * 0.5f + 0.25f, 1.25f, z0 + 0.02f), new Vector3(0.08f, 0.12f, 0.02f));
		sk.CommitTo(this, "Shelves", true);
		bk.CommitTo(this, "Things", true);
		stock.CommitTo(this, "Stock", false);
		shadows.CommitTo(this, "StockShadows", true).CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
		floor.CommitTo(this, "FloorThings", true);

		// the door: it opens into the closet, and shuts behind whoever goes in
		_doorHinge = new Node3D { Name = "ClosetDoor", Position = new Vector3(-DoorWidth * 0.5f, 0, End + 0.1f) };
		AddChild(_doorHinge);
		// (2026-10-14c: its own painted leaf, frame and hardware; it was a box of the shelves' steel, polka-dotted with rust)
		var dk = new MeshKit();
		dk.Mat(ServiceDoor.LeafMat);
		var dh = new MeshKit();
		dh.Mat(ServiceDoor.HardwareMat);
		dk.Xf = dh.Xf = new Transform3D(Basis.Identity, new Vector3(0.012f, 0, 0));
		ServiceDoor.Leaf(dk, dh, DoorWidth - 0.024f, 2.12f, 0.05f);
		// the name plate the stencil's on
		dh.Color = new Color(0.55f, 0.5f, 0.4f);
		BuildKit.Box(dh, new Vector3(DoorWidth * 0.5f - 0.012f, 1.6f, -0.027f), new Vector3(0.3f, 0.075f, 0.003f));
		dk.CommitTo(_doorHinge, "Door", true);
		dh.CommitTo(_doorHinge, "DoorHardware", true);
		var frame = new MeshKit();
		frame.Mat(ServiceDoor.FrameMat);
		ServiceDoor.Frame(frame, new Vector3(0, 0, 0), DoorWidth, 2.15f, End, End + 0.2f, 0.014f, 0.055f, End + 0.05f);   // (its stop on the hall side: it swings into the closet)
		frame.CommitTo(this, "DoorFrame", true);
		var plate = new Label3D
		{
			Text = "JANITOR", FontSize = 40, PixelSize = 0.0035f, Modulate = new Color(0.12f, 0.11f, 0.1f), OutlineSize = 0,
			Shaded = true, Position = new Vector3(DoorWidth * 0.5f, 1.6f, -0.0295f), Rotation = new Vector3(0, Mathf.Pi, 0),
		};
		_doorHinge.AddChild(plate);
		var db = new StaticBody3D { Name = "DoorBody", CollisionLayer = 1, CollisionMask = 0 };
		db.AddChild(new CollisionShape3D { Position = new Vector3(DoorWidth * 0.5f, 1.05f, 0), Shape = new BoxShape3D { Size = new Vector3(DoorWidth, 2.1f, 0.06f) } });
		_doorHinge.AddChild(db);
		_doorUse = new Interactable { Name = "Use", Prompt = "Open the door", PickRadius = 0.5f, MaxDistance = 2.2f, Position = new Vector3(DoorWidth * 0.5f, 1.1f, -0.1f) };
		_doorUse.Interacted += OnDoorUsed;
		_doorHinge.AddChild(_doorUse);
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(ClosetHalf * 2f - 0.2f, 2f, ClosetDepth - 1.1f) },
			new Vector3(0, 1f, z1 - (ClosetDepth - 1.1f) * 0.5f - 0.05f), OnInCloset, "ClosetInside");
		BuildClosetAct16();
		DecorateCloset(z0, z1, h);
	}
}
