using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
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
/// about two minutes' walk (one at a run). Then it tapers out into a stretched, cavernous version of
/// itself, green lamps hanging on long cables out of the dark overhead.
///
/// In the middle of it stands the shadow man (<see cref="ShadowMan"/>), frozen, because the lights
/// are green. Once the player is past him, the lights play red light, green light: every 20-30 s they
/// flicker three times and turn red with a low siren, and he is right behind the player. Anyone still
/// moving while it is red is taken, and wakes at Act 15's start. About eight minutes of it to the door
/// at the end: a janitor's closet, whose door swings shut behind them (<see cref="Checkpoint.Act15Finished"/>).
///
/// The flicker is three slow blinks, not a strobe; with Reduce Flashing it is three soft dips.
///
/// Local space: the floor is y=0; the hallway runs +Z from z=0 (the chamber's passage), centred on x=0.
/// </summary>
public partial class Act15Hallway : Node3D
{
	public const float Part1 = 324f, Taper = 30f, Part2 = 950f;
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
	public ShadowMan Shadow => _shadow;
	public float PlayerZ { get; private set; }
	public Vector3 DoorWorld => ToGlobal(new Vector3(0, 0.05f, End - 0.8f));
	public Vector3 ClosetWorld => ToGlobal(ClosetCentre + Vector3.Up * 0.05f);
	public Vector3 ShadowSpotWorld => ToGlobal(new Vector3(0, 0, ShadowZ));
	public Vector3 AlongWorld(float z) => ToGlobal(new Vector3(0, 0.05f, z));
	/// <summary>Seconds until the lights next change (tests use it to play fair).</summary>
	public float Timer => _timer;

	private ShadowMan _shadow;
	private StandardMaterial3D _lampMat, _stripMat2;
	private readonly List<OmniLight3D> _lamps = new();
	private float _timer, _flickT, _redT;
	private int _flickStep;
	private bool _dying, _firstRed = true, _closing;
	private Node3D _doorHinge;
	private Interactable _doorUse;
	private AudioStreamPlayer _hum;
	private readonly RandomNumberGenerator _rng = new();
	private static readonly Color Green = new(0.3f, 1f, 0.42f), Red = new(1f, 0.07f, 0.04f);
	private static readonly Color FogGreen = new(0.01f, 0.035f, 0.016f), FogRed = new(0.045f, 0.004f, 0.004f);

	public override void _Ready() => Callable.From(Build).CallDeferred();

	// ------------------------------------------------------------------ geometry

	private static StandardMaterial3D Concrete(string key, Texture2D tex, float scale) => new()
	{
		AlbedoTexture = tex, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * scale,
		Roughness = 0.9f, MetallicSpecular = 0.25f, VertexColorUseAsAlbedo = true,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, ResourceName = key,
	};

	private static StandardMaterial3D Glow(Color c, float energy) => new()
	{
		AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
	};

	private void Build()
	{
		_rng.Seed = 1515;
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		var wall = new MeshKit();
		wall.Mat(Concrete("hw_wall", StairwellTextures.StainedConcrete, 0.28f));
		var dark = new MeshKit();
		dark.Mat(Concrete("hw_floor", StairwellTextures.GrimeConcrete, 0.4f));
		var black = new MeshKit();
		black.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.01f, 0.01f), Roughness = 1f });
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
			float tint = 0.8f + 0.3f * _rng.Randf();
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
		// dark doorways in the walls that go nowhere
		for (float z = 23f; z < Part1 - 10f; z += _rng.RandfRange(28f, 46f))
		{
			float s = _rng.Randf() < 0.5f ? -1f : 1f;
			Box(black, new Vector3(s * (W1 * 0.5f - 0.005f), 1.2f, z), new Vector3(0.02f, 2.3f, 1.0f), null, false);
			Box(wall, new Vector3(s * (W1 * 0.5f - 0.06f), 2.4f, z), new Vector3(0.14f, 0.12f, 1.25f), null, false, 0.8f);
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
			float tint = 0.75f + 0.3f * _rng.Randf();
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
		black.CommitTo(this, "Doorways", false);

		BuildStrips();
		BuildLights();
		BuildCloset(body);

		_shadow = new ShadowMan { Name = "ShadowMan" };
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
		GD.Print($"[story] Act 15: the hallway - {End:0} m to the closet door, the shadow man at {ShadowZ:0} m");
	}

	/// <summary>The strip lights in the floor, down both sides, three thin bars to a segment.</summary>
	private void BuildStrips()
	{
		var s1 = new MeshKit();
		s1.Mat(Glow(new Color(0.25f, 0.8f, 0.35f), 1.1f));
		var s2 = new MeshKit();
		_stripMat2 = Glow(Green, 1.3f);
		s2.Mat(_stripMat2);
		s1.Color = s2.Color = Colors.White;
		void Strip(MeshKit k, float x, float z)
		{
			for (int b = -1; b <= 1; b++)
				BuildKit.Box(k, new Vector3(x + b * 0.06f, 0.006f, z), new Vector3(0.03f, 0.012f, 0.75f));
		}
		for (float z = 1f; z < Part1 + Taper; z += 1f)
		{
			float w = z < Part1 ? W1 : Mathf.Lerp(W1, W2, (z - Part1) / Taper);
			foreach (float s in new[] { -1f, 1f }) Strip(s1, s * (w * 0.5f - 0.22f), z);
		}
		for (float z = Part1 + Taper; z < End - 0.5f; z += 1f)
			foreach (float s in new[] { -1f, 1f }) Strip(s2, s * (W2 * 0.5f - 0.5f), z);
		s1.CommitTo(this, "Strips1", false);
		s2.CommitTo(this, "Strips2", false);
	}

	/// <summary>Part 1: a few weak green lights high up, and a stronger glow where the hall opens out
	/// (the green seen from far off). Part 2: dome lamps hanging on long cables out of the dark.</summary>
	private void BuildLights()
	{
		for (float z = 20f; z < Part1; z += 30f)
			AddChild(new OmniLight3D
			{
				Position = new Vector3(0, 4f, z), LightColor = new Color(0.35f, 0.9f, 0.45f), LightEnergy = 0.35f, OmniRange = 9f,
				OmniAttenuation = 1.4f, ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 60f, DistanceFadeLength = 20f,
			});
		foreach (float z in new[] { Part1 + 4f, Part1 + 16f, Part1 + 28f })
			AddChild(new OmniLight3D { Position = new Vector3(0, 6f, z), LightColor = Green, LightEnergy = 2.2f, OmniRange = 22f, ShadowEnabled = false });

		_lampMat = Glow(Green, 3f);
		var cable = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 0.6f };
		var shade = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.14f, 0.12f), Metallic = 0.6f, Roughness = 0.45f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		var k = new MeshKit();
		var lk = new MeshKit();
		k.Mat(cable); k.Color = Colors.White;
		var sk = new MeshKit();
		sk.Mat(shade); sk.Color = Colors.White;
		lk.Mat(_lampMat); lk.Color = Colors.White;
		for (float z = Part1 + Taper + 5f; z < End - 3f; z += 10f)
		{
			float y = 5.2f + 0.6f * Mathf.Sin(z * 0.37f);
			Vector3 at = new(0, y, z);
			k.Cylinder(at, new Vector3(0, H2, z), 0.012f, 0.012f, 4, false);
			sk.Cylinder(at + Vector3.Down * 0.02f, at + Vector3.Down * 0.32f, 0.08f, 0.42f, 12, false);
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
		var k = new MeshKit();
		k.Mat(Concrete("hw_closet", StairwellTextures.CleanConcrete, 0.6f));
		k.Color = new Color(0.75f, 0.73f, 0.68f);
		void Slab(Vector3 c, Vector3 s)
		{
			BuildKit.Box(k, c, s, 1f);
			body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		Slab(new Vector3(-ClosetHalf - 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, ClosetDepth));
		Slab(new Vector3(ClosetHalf + 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, ClosetDepth));
		Slab(new Vector3(0, h * 0.5f, z1 + 0.1f), new Vector3(ClosetHalf * 2f + 0.4f, h, 0.2f));
		Slab(new Vector3(0, h + 0.1f, (z0 + z1) * 0.5f), new Vector3(ClosetHalf * 2f + 0.4f, 0.2f, ClosetDepth));
		Slab(new Vector3(0, -0.1f, (z0 + z1) * 0.5f), new Vector3(ClosetHalf * 2f + 0.4f, 0.2f, ClosetDepth + 0.2f));
		k.CommitTo(this, "Closet", true);
		// shelves down both sides and the back, crowded with bottles, tins and rags
		var sk = new MeshKit();
		sk.Mat(StairwellTextures.SteelMat);
		sk.Color = new Color(0.6f, 0.6f, 0.55f);
		var bk = new MeshKit();
		bk.Mat(new StandardMaterial3D { Roughness = 0.5f, VertexColorUseAsAlbedo = true });
		Color[] bottle = { new(0.75f, 0.7f, 0.2f), new(0.2f, 0.35f, 0.7f), new(0.85f, 0.85f, 0.8f), new(0.6f, 0.15f, 0.12f), new(0.3f, 0.5f, 0.3f) };
		foreach (float y in new[] { 0.5f, 1.05f, 1.6f, 2.1f })
		{
			BuildKit.Box(sk, new Vector3(0, y, z1 - 0.2f), new Vector3(ClosetHalf * 2f, 0.03f, 0.36f));
			BuildKit.Box(sk, new Vector3(-ClosetHalf + 0.18f, y, (z0 + z1) * 0.5f + 0.2f), new Vector3(0.36f, 0.03f, ClosetDepth - 0.8f));
			for (float x = -ClosetHalf + 0.15f; x < ClosetHalf - 0.1f; x += _rng.RandfRange(0.12f, 0.22f))
			{
				bk.Color = bottle[_rng.RandiRange(0, bottle.Length - 1)];
				float bh = _rng.RandfRange(0.14f, 0.3f), br = _rng.RandfRange(0.035f, 0.06f);
				bk.Cylinder(new Vector3(x, y + 0.015f, z1 - 0.2f), new Vector3(x, y + 0.015f + bh, z1 - 0.2f), br, br * 0.9f, 8, true);
			}
			for (float z = z0 + 0.6f; z < z1 - 0.5f; z += _rng.RandfRange(0.14f, 0.24f))
			{
				bk.Color = bottle[_rng.RandiRange(0, bottle.Length - 1)];
				float bh = _rng.RandfRange(0.12f, 0.26f);
				bk.Cylinder(new Vector3(-ClosetHalf + 0.18f, y + 0.015f, z), new Vector3(-ClosetHalf + 0.18f, y + 0.015f + bh, z), 0.045f, 0.04f, 8, true);
			}
		}
		foreach (float x in new[] { -ClosetHalf + 0.02f, ClosetHalf - 0.02f })
			foreach (float z in new[] { z1 - 0.02f, z1 - 0.38f })
				sk.Cylinder(new Vector3(x, 0, z), new Vector3(x, 2.2f, z), 0.012f, 0.012f, 4, false);
		// the mop, fallen across the floor, and its bucket
		sk.Color = new Color(0.45f, 0.32f, 0.2f);
		sk.Cylinder(new Vector3(0.9f, 0.04f, z0 + 0.5f), new Vector3(-0.3f, 0.03f, z1 - 0.7f), 0.016f, 0.016f, 6, true);
		bk.Color = new Color(0.7f, 0.68f, 0.6f);
		bk.Cylinder(new Vector3(-0.35f, 0.02f, z1 - 0.75f), new Vector3(-0.3f, 0.06f, z1 - 0.6f), 0.12f, 0.05f, 8, true);
		bk.Color = new Color(0.55f, 0.52f, 0.1f);
		bk.Cylinder(new Vector3(0.75f, 0, z0 + 0.45f), new Vector3(0.75f, 0.32f, z0 + 0.45f), 0.15f, 0.18f, 12, true);
		// a light switch by the door (Act 16)
		bk.Color = new Color(0.85f, 0.84f, 0.8f);
		BuildKit.Box(bk, new Vector3(DoorWidth * 0.5f + 0.25f, 1.25f, z0 + 0.02f), new Vector3(0.08f, 0.12f, 0.02f));
		sk.CommitTo(this, "Shelves", true);
		bk.CommitTo(this, "Things", true);

		// the door: it opens into the closet, and shuts behind whoever goes in
		_doorHinge = new Node3D { Name = "ClosetDoor", Position = new Vector3(-DoorWidth * 0.5f, 0, End + 0.1f) };
		AddChild(_doorHinge);
		var dk = new MeshKit();
		dk.Mat(StairwellTextures.SteelMat);
		dk.Color = new Color(0.45f, 0.5f, 0.45f);
		BuildKit.Box(dk, new Vector3(DoorWidth * 0.5f, 1.05f, 0), new Vector3(DoorWidth - 0.02f, 2.1f, 0.05f));
		dk.Color = new Color(0.8f, 0.78f, 0.7f);
		BuildKit.Box(dk, new Vector3(DoorWidth - 0.12f, 1.0f, -0.05f), new Vector3(0.1f, 0.03f, 0.04f));
		dk.CommitTo(_doorHinge, "Door", true);
		var plate = new Label3D
		{
			Text = "JANITOR", FontSize = 40, PixelSize = 0.004f, Modulate = new Color(0.85f, 0.83f, 0.75f), OutlineSize = 0,
			Shaded = true, Position = new Vector3(DoorWidth * 0.5f, 1.6f, -0.035f), Rotation = new Vector3(0, Mathf.Pi, 0),
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
	}
}
