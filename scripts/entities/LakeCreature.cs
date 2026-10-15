using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// Act 12's lake creature: never a body, only tentacles â€” thick, wet, jointed limbs studded all
/// over with bloodshot eyeballs â€” that come up out of the lake around the boat.
///
/// Each tentacle is a chain of tapering segments (a node per joint, so it moves like a limb, not a
/// stiff pole): it rises bodily out of the water with a plume of spray, writhes (a travelling wave
/// down the joints), and sinks back. Its eyes open only once it is up, all at once, and from then on
/// every one of them turns in its socket to stare at the camera.
///
/// <see cref="Breach"/> starts the rise around a point; <see cref="Slam"/> brings one limb up and
/// over and down into the water beside the boat (<see cref="Slammed"/> reports the impact);
/// <see cref="Release"/> sinks all but a couple of watchers, which stay up, swaying and staring,
/// until <see cref="SinkAll"/>. The node frees itself once everything is back under.
/// </summary>
public partial class LakeCreature : Node3D
{
	[Export] public int TentacleCount = 6;
	[Export] public int Seed = 6013;

	/// <summary>For tests: true from the moment the first limb starts rising until the last has sunk.</summary>
	public bool Breaching { get; private set; }
	/// <summary>For tests: 0 (all submerged) .. 1 (the tallest limb fully up).</summary>
	public float Life { get; private set; }
	/// <summary>How many eyes have opened (for tests and the crossing's squelch cue).</summary>
	public int EyesOpen { get; private set; }

	/// <summary>A slam hit the water (world position).</summary>
	public event Action<Vector3> Slammed;
	/// <summary>A limb broke the surface (world position of its root).</summary>
	public event Action<Vector3> Surfaced;

	private enum State { Under, Rising, Up, Sinking }

	private sealed class Limb
	{
		public Node3D Root;
		public readonly List<Node3D> Joints = new();
		public readonly List<Node3D> Eyes = new();
		public readonly List<float> EyeSize = new();
		public float Length, Phase, Curl, Speed;
		public State State = State.Under;
		public float T, Delay, Rise;
		public bool Watcher, Surfaced;
		public float SlamT = -1f;
		public bool SlamHit;
		public Vector3 SlamTarget;
		public float EyeScale;
		/// <summary>Seconds this limb takes to come up and go back down (the colossus is slowest).</summary>
		public float RiseTime = 1.6f, SinkTime = 2.4f;
		public bool Colossus;
		/// <summary>0..1 how far it has curled over the boat to look down into it (<see cref="Loom"/>).</summary>
		public float Loom, LoomTarget;
		/// <summary>The root's yaw facing the boat (the loom twists it off that, so the arch reads side-on).</summary>
		public float BaseYaw;
		public GpuParticles3D Drips;
		public float UpFor;
		/// <summary>The one that follows the boat after the breach: placed and raised from outside.</summary>
		public bool Hunter;
		public float HuntRise;
		/// <summary>A lone limb (<see cref="Surface"/>): its eyes are its own, open as soon as it's up, and it
		/// goes back under by itself — after <see cref="SinkAfter"/> seconds up, or once it has struck.</summary>
		public bool Lone;
		public float SinkAfter = -1f, StrikeAfter = -1f;
		public bool Struck;
		public Skeleton3D Skel;
		public MeshInstance3D Flesh;
		/// <summary>Per eye: its gaze now (it turns to you at its own pace), its rate, its next blink, its wander.</summary>
		public readonly List<Quaternion> EyeLook = new();
		public readonly List<float> EyeRate = new(), EyeBlink = new();
		public readonly List<Vector3> EyeOff = new();
		/// <summary>Its own tempo (two waves of unrelated speed down it), so no two limbs move in step.</summary>
		public float FreqA = 1f, FreqB = 1f, Phase2, Twist, NextRipple;
		/// <summary>The follow-through: each joint's bend lagging a little behind the one before (secondary motion).</summary>
		public Vector3[] Lag;
	}

	private readonly List<Limb> _limbs = new();
	private RandomNumberGenerator _rng;
	private double _time;
	private Camera3D _cam;
	private static ShaderMaterial _skin;
	private static StandardMaterial3D _eyeMat;
	private const int Segments = 9;
	private float _blinkT = -1f;
	private bool _eyesOpened;

	/// <summary>For tests: the colossus, the limb that dwarfs the rest, came up.</summary>
	public bool ColossusUp => _limbs.Exists(l => l.Colossus && l.State == State.Up);
	/// <summary>For tests: a limb is curled over the boat.</summary>
	public bool Looming => _limbs.Exists(l => l.Loom > 0.85f);

	public override void _Ready()
	{
		CreatureRim.Apply(this, far: true);
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		Visible = false;
		if (WarmUpOnly) BuildLimb(Vector3.Zero, 6f, 0.7f, 4);
	}

	/// <summary>A throwaway for the load's shader warm-up (<see cref="Systems.ShaderWarmup.Prewarm"/>): one limb, its eyes,
	/// mouth and drips, never shown (the lag pass, 2026-10-13: the breach's first sight compiled 23 pipelines, 1.4 s).</summary>
	public bool WarmUpOnly;

	// ------------------------------------------------------------------ building

	private Limb BuildLimb(Vector3 localRoot, float length, float baseR, int eyes)
	{
		var limb = new Limb
		{
			Length = length, Phase = _rng.RandfRange(0, Mathf.Tau), Curl = _rng.RandfRange(0.09f, 0.17f),
			Speed = _rng.RandfRange(0.8f, 1.25f),
			FreqA = _rng.RandfRange(0.8f, 1.2f), FreqB = _rng.RandfRange(1.35f, 1.9f), Phase2 = _rng.RandfRange(0, Mathf.Tau),
			Twist = _rng.RandfRange(0.04f, 0.1f),
		};
		limb.Root = new Node3D { Name = $"Tentacle{_limbs.Count}", Position = localRoot + Vector3.Down * (length + 1.5f) };
		AddChild(limb.Root);
		float segLen = length / Segments;
		Node3D parent = limb.Root;
		// (2026-10-12, the model overhaul: the owner's "chunked" tentacles) the flesh is one smooth tube skinned to the joints'
		// chain, not a rigid cylinder to each joint with a knuckle over the seam: it bends as one thing, the suckers with it
		var k = new MeshKit();
		// (2026-10-15, the owner: "less robotic and blocky") one lofted tube up the whole limb, many rings and more sides, its
		// section a little flattened on the sucker side (+Z) as an octopus arm's is, and lumpy: slow swellings and pinches
		// along it and a soft lobing round it, each limb its own, so no two rings are the same and nothing reads turned
		int seed = limb.Joints.Count + _limbs.Count * 101 + (int)(length * 37f);
		var lump = new FastNoiseLite { Seed = seed, Frequency = 0.9f / Mathf.Max(length, 1f) * 6f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
		float lobePh = _rng.RandfRange(0, Mathf.Tau), lobe2 = _rng.RandfRange(0, Mathf.Tau);
		int ringsN = Segments * 6, sides = baseR > 1.2f ? 30 : 22;
		var rings = new List<ItemMeshes.Ring>(ringsN + 1);
		var fs = new float[ringsN + 1];
		for (int q = 0; q <= ringsN; q++)
		{
			float f = (float)q / ringsN;
			fs[q] = f;
			float r = Radius(baseR, f) * (1f + 0.07f * lump.GetNoise1D(f * 10f) * (1f - f * 0.5f));
			rings.Add(new ItemMeshes.Ring(Vector3.Up * f * length, r, r * 0.9f));
		}
		// (2026-10-15, "everything made in blender") the flesh is the Blender limb (tools/Blender/tentacle.py), its cups shaped
		// into its skin, laid along this limb's own length and radius; the code-built tube below only if the model is missing
		var modelFlesh = TentacleModels.Limb("Limb", length, f => Radius(baseR, f), Skin(), TentacleKit.SuckerRim, TentacleKit.SuckerHole);
		k.Mat(Skin());
		k.Color = Colors.White;
		if (modelFlesh == null)
		ItemMeshes.Loft(k, rings, sides, false, false, Vector3.Right, 1.5f / Mathf.Max(baseR, 0.3f),
			radial: (ri, a) =>
			{
				float f = fs[ri];
				// soft lobes round it, drifting along it; the sucker face a little flatter
				float m = 1f + 0.045f * Mathf.Sin(3f * a + lobePh + f * 5f) + 0.03f * Mathf.Sin(5f * a + lobe2 - f * 7f);
				float under = Mathf.Max(0f, Mathf.Sin(a));
				return m * (1f - 0.07f * under * under);
			});
		for (int i = 0; i < Segments; i++)
		{
			var joint = new Node3D { Name = $"J{i}", Position = i == 0 ? Vector3.Zero : Vector3.Up * segLen };
			parent.AddChild(joint);
			// suckers in two loose rows down the inner face (the side it curls toward, +Z)
			if (i < Segments - 1 && modelFlesh == null)
			{
				k.Xf = new Transform3D(Basis.Identity, Vector3.Up * i * segLen);
				TentacleKit.Suckers(k, segLen, Radius(baseR, (float)i / Segments), Radius(baseR, (float)(i + 1) / Segments), i < 3 ? 2 : 3, 0.42f, seed * 13 + i);
			}
			limb.Joints.Add(joint);
			parent = joint;
		}
		k.Xf = Transform3D.Identity;
		limb.Skel = AnimKit.ChainSkeleton(limb.Root, AnimKit.SkinAlongY(modelFlesh ?? k.Commit(), segLen, Segments), segLen, Segments);
		// its skin's detail and pattern read from the rest pose, so they ride the flesh as it bends instead of swimming over it
		if (limb.Skel.GetNodeOrNull<MeshInstance3D>("Flesh") is { } fleshMi)
		{
			CreatureDetail.Prepare(fleshMi);
			fleshMi.SetInstanceShaderParameter("pulse_phase", _rng.RandfRange(0f, 50f));
			limb.Flesh = fleshMi;
		}
		// the mouth at the tip (the owner's references): jaws flaring round a toothed throat
		(TentacleModels.Maw(limb.Joints[Segments - 1], baseR * 1.45f, MawSkin())
			?? TentacleKit.Maw(limb.Joints[Segments - 1], Radius(baseR, 1f), baseR * 1.45f, MawSkin(), limb.Joints.Count * 31 + _limbs.Count)).Position = Vector3.Up * segLen;
		// eyes all the way up it, crowded and huge at the root where it meets the body, shrinking to
		// small ones toward the tip (never on the sucker side)
		for (int e = 0; e < eyes; e++)
		{
			float f = Mathf.Clamp(Mathf.Pow(_rng.Randf(), 1.35f) * 0.9f + 0.03f, 0.03f, 0.9f);
			int seg = Mathf.Clamp((int)(f * Segments), 0, Segments - 1);
			float local = (f * Segments - seg) * segLen;
			float r = Radius(baseR, f);
			float ang = Mathf.Pi * 0.5f + _rng.RandfRange(0.75f, Mathf.Tau - 0.75f);
			float size = Mathf.Min(TentacleKit.EyeSize(baseR * 0.9f, baseR * 0.14f, f) * _rng.RandfRange(0.85f, 1.15f), r * 0.95f);
			// (2026-10-15) sunk into the limb by a third of its size, so the flesh closes round it instead of it sitting on top
			float sink = Mathf.Max(r * 0.7f, r - size * 0.18f);
			var socket = new Node3D { Name = $"Eye{e}", Position = new Vector3(Mathf.Cos(ang) * sink, local, Mathf.Sin(ang) * sink) };
			limb.Joints[seg].AddChild(socket);
			BuildEye(socket, 1 + _rng.RandiRange(0, 11));
			socket.Scale = Vector3.One * 0.001f;
			limb.Eyes.Add(socket);
			limb.EyeSize.Add(size);
			// each eye its own: how quickly it turns to you, where it wanders off to, when it blinks
			limb.EyeLook.Add(Quaternion.Identity);
			limb.EyeRate.Add(_rng.RandfRange(2.5f, 9f));
			limb.EyeBlink.Add(_rng.RandfRange(2f, 12f));
			limb.EyeOff.Add(new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.6f, 0.6f), 0f));
		}
		limb.Drips = Drips(limb.Joints[Mathf.Min(3, Segments - 1)], baseR);
		_limbs.Add(limb);
		return limb;
	}

	/// <summary>Water streaming off a limb as it comes up: drops falling from around a joint, in world
	/// space so they fall straight while the limb sways.</summary>
	private static GpuParticles3D Drips(Node3D joint, float r)
	{
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
			EmissionRingAxis = Vector3.Up, EmissionRingRadius = r * 1.05f, EmissionRingInnerRadius = r * 0.8f, EmissionRingHeight = r * 6f,
			Direction = Vector3.Down, Spread = 12f,
			InitialVelocityMin = 0.2f, InitialVelocityMax = 1.2f,
			Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.6f, ScaleMax = 1.3f,
		};
		var draw = new QuadMesh { Size = Vector2.One * 0.03f };   // (2026-10-15: fine drops, not white pearls)
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = LakeFx.SoftDot(),
			AlbedoColor = new Color(0.78f, 0.8f, 0.82f, 0.4f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
		};
		var p = new GpuParticles3D
		{
			Name = "Drips", Amount = 60, Lifetime = 1.4, LocalCoords = false, Emitting = false,
			ProcessMaterial = pm, DrawPass1 = draw,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-6, -14, -6), new Vector3(12, 20, 12)),
		};
		joint.AddChild(p);
		return p;
	}

	/// <summary>The limb's radius a fraction f of the way up: thick most of the way, narrowing late
	/// into a blunt, curling tip (a tentacle, not a spike).</summary>
	private static float Radius(float baseR, float f) => Mathf.Lerp(baseR, Mathf.Max(0.12f, baseR * 0.3f), Mathf.Pow(f, 1.7f));   // thick enough at the tip to carry its mouth

	/// <summary>Gooey, grimy octopus flesh (<see cref="TentacleKit.Flesh"/>): maroon underneath, green-teal down the back.</summary>
	private static ShaderMaterial Skin()
	{
		if (_skin != null) return _skin;
		_skin = TentacleKit.Flesh(null, 1.1f);
		// (2026-10-15) read from the rest pose (the pattern rides the flesh as it bends), a slow swallowing pulse travelling up
		// each limb (each its own phase), and the thin flesh toward the tips glowing red when lit from behind
		_skin.SetShaderParameter("rest_space", true);
		_skin.SetShaderParameter("pulse_amp", 0.035f);
		_skin.SetShaderParameter("thin_glow", 1f);
		_skin.SetShaderParameter("vc_suckers", TentacleModels.Available);   // (the Blender limb paints its cups in)
		return _skin;
	}
	private static ShaderMaterial _mawSkin;
	/// <summary>The mouths aren't skinned (no rest pose in them): the same flesh, read from where it is.</summary>
	private static ShaderMaterial MawSkin() => _mawSkin ??= TentacleKit.Flesh(null, 1.1f);

	private static SphereMesh _ball;
	private static TorusMesh _lidMesh;
	private static StandardMaterial3D _irisMat, _pupilMat;

	/// <summary>One eyeball in a unit socket (the socket is scaled to the eye's size and turned so its -Z
	/// faces the camera): a yellowed, veined white ball; on its front, a domed dark-red iris with a faint
	/// glow of its own, and a black pupil. Real geometry, so it reads the same from any angle and at the
	/// game's low resolution.</summary>
	internal static void BuildEye(Node3D socket, int variant)
	{
		if (variant <= 0) { BuildEye(socket); return; }
		// (2026-10-15, the owner: "less robotic") the lake's eyes aren't stamped from one: each its own iris (blood red, amber,
		// a sick yellow-green, a milky blind one), its own pupil (a slit, a round black, a goat's bar), set in a thin swollen
		// lid of the flesh's own colour rather than a dark ring round it
		_ball ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 8 };
		_irisVar ??= new StandardMaterial3D[4];
		// (dull, dark, sick colours: a lit eye in the dark, not a sweet)
		Color[] iris = { new(0.3f, 0.035f, 0.03f), new(0.3f, 0.16f, 0.04f), new(0.17f, 0.18f, 0.06f), new(0.42f, 0.4f, 0.35f) };
		Color[] glow = { new(0.45f, 0.04f, 0.02f), new(0.3f, 0.12f, 0.02f), new(0.12f, 0.14f, 0.03f), new(0.12f, 0.11f, 0.09f) };
		int vi = (variant - 1) % 4;
		_irisVar[vi] ??= new StandardMaterial3D
		{
			AlbedoColor = iris[vi], Roughness = 0.08f, MetallicSpecular = 0.85f,
			EmissionEnabled = true, Emission = glow[vi], EmissionEnergyMultiplier = vi == 3 ? 0.15f : 0.35f,
			ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.03f,
		};
		_pupilMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.005f, 0.005f), Roughness = 0.05f, MetallicSpecular = 0.9f };
		MeshInstance3D Part(Material m, Vector3 pos, Vector3 scale) => new()
		{
			Mesh = _ball, MaterialOverride = m, Position = pos, Scale = scale,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		// the white duller and darker than the stock eye's (in the dark a turned-away ball read as a lit bulb)
		_dimWhite ??= (StandardMaterial3D)EyeMat().Duplicate();
		_dimWhite.AlbedoColor = new Color(0.62f, 0.5f, 0.42f);
		_dimWhite.Emission = new Color(0.05f, 0.03f, 0.02f);
		_dimWhite.Roughness = 0.18f;
		float irisR = 0.42f + 0.1f * ((variant * 7) % 3);
		// (2026-10-15) the Blender eye's parts (tools/Blender/tentacle.py), in the same order: the white, the iris, the pupil
		if (TentacleModels.EyePart(socket, "Ball", _dimWhite) != null)
		{
			TentacleModels.EyePart(socket, "Iris", _irisVar[vi], new Vector3(irisR / 0.5f, irisR / 0.5f, 1f));
			if (vi != 3) TentacleModels.EyePart(socket, ((variant * 5) % 3) switch { 0 => "Pupil_Slit", 1 => "Pupil_Round", _ => "Pupil_Bar" }, _pupilMat);
			return;
		}
		socket.AddChild(Part(_dimWhite, Vector3.Zero, Vector3.One));
		socket.AddChild(Part(_irisVar[vi], new Vector3(0, 0, -0.86f), new Vector3(irisR, irisR, 0.18f)));
		if (vi != 3)
		{
			int pk = (variant * 5) % 3;
			var ps = pk == 0 ? new Vector3(0.1f, 0.3f, 0.08f) : pk == 1 ? new Vector3(0.18f, 0.18f, 0.08f) : new Vector3(0.3f, 0.09f, 0.08f);
			socket.AddChild(Part(_pupilMat, new Vector3(0, 0, -0.99f), ps));
		}
		// no lid ring: the ball sits sunk in the limb, the flesh closing round it (a torus round it read as a band cut across it)
	}

	private static StandardMaterial3D[] _irisVar;
	private static StandardMaterial3D _dimWhite;

	internal static void BuildEye(Node3D socket)
	{
		_ball ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 8 };
		_irisMat ??= new StandardMaterial3D
		{
			AlbedoColor = new Color(0.32f, 0.03f, 0.02f), Roughness = 0.08f, MetallicSpecular = 0.85f,
			EmissionEnabled = true, Emission = new Color(0.6f, 0.05f, 0.02f), EmissionEnergyMultiplier = 0.55f,
			ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.03f,
		};
		_pupilMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.005f, 0.005f), Roughness = 0.05f, MetallicSpecular = 0.9f };
		MeshInstance3D Part(Material m, Vector3 pos, Vector3 scale) => new()
		{
			Mesh = _ball, MaterialOverride = m, Position = pos, Scale = scale,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		// (2026-10-15) the Blender eye's parts, in the same order (the white, the iris, the pupil, the lid)
		if (TentacleModels.EyePart(socket, "Ball", EyeMat()) != null)
		{
			TentacleModels.EyePart(socket, "Iris", _irisMat);
			TentacleModels.EyePart(socket, "Pupil_Slit", _pupilMat);
			TentacleModels.EyePart(socket, "Lid", TentacleKit.Lid);
			return;
		}
		socket.AddChild(Part(EyeMat(), Vector3.Zero, Vector3.One));
		socket.AddChild(Part(_irisMat, new Vector3(0, 0, -0.84f), new Vector3(0.5f, 0.5f, 0.2f)));
		socket.AddChild(Part(_pupilMat, new Vector3(0, 0, -0.99f), new Vector3(0.14f, 0.3f, 0.08f)));
		// a swollen, wet lid round it, so it sits in the flesh rather than on it
		_lidMesh ??= new TorusMesh { InnerRadius = 0.72f, OuterRadius = 1.12f, Rings = 14, RingSegments = 8 };
		socket.AddChild(new MeshInstance3D
		{
			// (2026-10-12: sat further forward and fuller, the lid hides the ball's round, so the eye is sunk in the flesh)
			Name = "Lid", Mesh = _lidMesh, MaterialOverride = TentacleKit.Lid, Position = new Vector3(0, 0, -0.42f), Scale = new Vector3(1.05f, 1.6f, 1.05f),
			Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	/// <summary>The white of the eye: yellowed, bloodshot, veins wandering across it.</summary>
	private static StandardMaterial3D EyeMat()
	{
		if (_eyeMat != null) return _eyeMat;
		const int w = 96, h = 48;
		var alb = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var emi = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var rng = new RandomNumberGenerator { Seed = 991 };
		// veins: wandering lines along the meridians, from the back of the eye toward the iris
		var veins = new float[w, h];
		for (int v = 0; v < 26; v++)
		{
			float u = rng.Randf() * w, thick = rng.RandfRange(0.6f, 1.4f);
			for (int y = h - 1; y > h * 0.2f; y--)
			{
				u += rng.RandfRange(-1.2f, 1.2f);
				if (rng.Randf() < 0.04f) thick *= 0.8f;
				for (int dx = -2; dx <= 2; dx++)
				{
					int x = ((int)u + dx + w) % w;
					veins[x, y] = Mathf.Max(veins[x, y], Mathf.Clamp(thick - Mathf.Abs(dx) * 0.6f, 0f, 1f) * Mathf.SmoothStep(h * 0.2f, h * 0.45f, y));
				}
			}
		}
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float v = (float)y / h;   // 0 at the pupil pole
				// (2026-10-12, the model overhaul: the eyes read as ping-pong balls) a dull, sickly, bloodshot yellow, never a clean white
				Color c = new Color(0.58f, 0.5f, 0.37f).Lerp(new Color(0.46f, 0.27f, 0.22f), Mathf.SmoothStep(0.3f, 0.9f, v));
				c = c.Lerp(new Color(0.62f, 0.04f, 0.04f), veins[x, y] * 0.9f);
				Color e = c * 0.1f;
				if (v < 0.13f)
				{
					float ring = Mathf.Sin(x * 0.9f) * 0.08f;
					c = new Color(0.42f + ring, 0.03f, 0.03f);
					e = new Color(0.7f, 0.05f, 0.02f);
				}
				if (v < 0.055f) { c = new Color(0.01f, 0.005f, 0.005f); e = Colors.Black; }
				alb.SetPixel(x, y, c);
				emi.SetPixel(x, y, e);
			}
		alb.GenerateMipmaps();
		emi.GenerateMipmaps();
		return _eyeMat = new StandardMaterial3D
		{
			AlbedoTexture = ImageTexture.CreateFromImage(alb),
			AlbedoColor = new Color(0.9f, 0.84f, 0.72f),
			EmissionEnabled = true,
			Emission = new Color(0.16f, 0.12f, 0.09f),
			EmissionEnergyMultiplier = 1f,
			Roughness = 0.12f,
			MetallicSpecular = 0.8f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
	}

	// ------------------------------------------------------------------ the beats

	/// <summary>Brings the limbs up around <paramref name="center"/> (world), spread in an arc beyond
	/// the boat and one or two off to its sides, never closer than a few metres to it. Staggered:
	/// the whole rise takes about two seconds. <paramref name="holdSeconds"/> is kept for API
	/// compatibility; the crossing decides when to <see cref="Release"/>.</summary>
	public void Breach(Vector3 center, Vector3 boat, double holdSeconds = 0)
	{
		World.FrogChorus.Hush(120f);   // (2026-10-10: every frog on the shore goes still)
		_prep ??= Prepare(center, boat).GetEnumerator();
		while (_prep.MoveNext()) { }   // (whatever wasn't built ahead, built now)
		Visible = true;
		Breaching = true;
	}

	/// <summary>The limbs built ahead of the breach, a limb a frame, unseen (the lag pass, 2026-10-13: all built on the
	/// spot as it broke the surface, they held that frame a quarter of a second); <see cref="Breach"/> then only brings
	/// them up. The same arc and limbs as building them there and then (the boat has stopped by now).</summary>
	public async System.Threading.Tasks.Task PrepareOverFrames(Vector3 center, Vector3 boat)
	{
		_prep ??= Prepare(center, boat).GetEnumerator();
		while (IsInstanceValid(this) && IsInsideTree() && _prep.MoveNext())
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		// (the cold rim on every limb, as when they were all built in the frame it went on: it skips what has it)
		if (IsInstanceValid(this)) CreatureRim.Apply(this, far: true);
	}
	private IEnumerator<int> _prep;

	private IEnumerable<int> Prepare(Vector3 center, Vector3 boat)
	{
		GlobalPosition = center;
		Vector3 toBoat = boat - center; toBoat.Y = 0;
		float baseAng = Mathf.Atan2(toBoat.X, toBoat.Z);
		for (int i = 0; i < TentacleCount; i++)
		{
			Vector3 local;
			if (i < TentacleCount - 2)
			{
				// the arc ahead of the boat
				float a = baseAng + Mathf.Pi + Mathf.Lerp(-1.25f, 1.25f, (float)i / Mathf.Max(1, TentacleCount - 3)) + _rng.RandfRange(-0.15f, 0.15f);
				float r = _rng.RandfRange(2.5f, 6.5f);
				local = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
			}
			else
			{
				// flanking the boat, one each side, a little behind it
				Vector3 side = toBoat.Cross(Vector3.Up).Normalized() * (i % 2 == 0 ? 1f : -1f);
				local = toBoat + side * _rng.RandfRange(5f, 6.5f) + toBoat.Normalized() * _rng.RandfRange(-1f, 2f);
			}
			// never on top of the boat
			Vector3 fromBoat = local - toBoat;
			if (new Vector2(fromBoat.X, fromBoat.Z).Length() < 4.5f) local = toBoat + fromBoat.Normalized() * 4.5f;
			bool flank = i >= TentacleCount - 2;
			var limb = BuildLimb(local, _rng.RandfRange(6.5f, 9f) * (flank ? 0.85f : 1f), _rng.RandfRange(0.62f, 0.82f), _rng.RandiRange(16, 24));
			limb.Delay = flank ? _rng.RandfRange(0.9f, 1.4f) : i * 0.22f + _rng.RandfRange(0f, 0.15f);
			limb.State = State.Rising;
			limb.T = -limb.Delay;
			limb.Watcher = i == 0 || i == TentacleCount - 3;   // two in the arc stay up to watch
			// face the boat, so the limbs lean and curl toward it
			Vector3 look = toBoat - local; look.Y = 0;
			limb.BaseYaw = Mathf.Atan2(look.X, look.Z);
			limb.Root.Rotation = new Vector3(0, limb.BaseYaw, 0);
			yield return i;
		}
		// and behind them all, first and slowest, the one that dwarfs the rest
		{
			Vector3 back = -toBoat.Normalized() * 5.5f;
			var c = BuildLimb(back, 16f, 1.9f, 46);
			c.Colossus = true;
			c.RiseTime = 2.6f;
			c.SinkTime = 4.5f;
			c.Curl = 0.07f;
			c.Speed = 0.55f;
			c.Delay = 0f;
			c.State = State.Rising;
			c.T = 0f;
			Vector3 look = toBoat - back; look.Y = 0;
			c.Root.Rotation = new Vector3(0, Mathf.Atan2(look.X, look.Z), 0);
			// the others come up after it
			foreach (var l in _limbs) if (!l.Colossus) { l.Delay += 0.7f; l.T = -l.Delay; }
		}
	}

	/// <summary>The limb nearest the boat curls over it and hangs there, looking down into it.</summary>
	public bool Loom(Vector3 boatWorld)
	{
		Limb best = null;
		float bestD = float.MaxValue;
		foreach (var l in _limbs)
		{
			if (l.State != State.Up || l.Colossus || l.SlamT >= 0f) continue;
			float d = l.Root.GlobalPosition.DistanceTo(boatWorld);
			if (d < bestD) { bestD = d; best = l; }
		}
		if (best == null) return false;
		best.LoomTarget = 1f;
		return true;
	}

	/// <summary>Where the looming limb's tip is (world), for the camera to be drawn to; null if none.</summary>
	public Vector3? LoomTip()
	{
		foreach (var l in _limbs)
			if (l.LoomTarget > 0f) return l.Joints[^1].GlobalTransform * (Vector3.Up * (l.Length / Segments));
		return null;
	}

	/// <summary>A point framing the looming limb's bend (between its crown and its tip), so the
	/// camera sees it arch over rather than staring up it end-on; null if none.</summary>
	public Vector3? LoomBend()
	{
		foreach (var l in _limbs)
			if (l.LoomTarget > 0f) return (l.Joints[Segments * 2 / 3].GlobalPosition + (LoomTip() ?? l.Joints[^1].GlobalPosition)) * 0.5f;
		return null;
	}

	/// <summary>Every eye on every limb opens at once (they stay shut until this).</summary>
	public void OpenEyes() => _eyesOpened = true;

	/// <summary>Every eye it has closes and opens again, together, once.</summary>
	public void Blink() => _blinkT = 0f;

	/// <summary>World position two-thirds of the way up the colossus (for the camera), or null.</summary>
	public Vector3? ColossusTop()
	{
		foreach (var l in _limbs)
			if (l.Colossus) return l.Joints[Segments * 2 / 3].GlobalPosition;
		return null;
	}

	/// <summary>The limb nearest the boat rears back and brings itself down into the water beside it.
	/// Returns false if nothing is up to do it.</summary>
	public bool Slam(Vector3 boatWorld)
	{
		Limb best = null;
		float bestD = float.MaxValue;
		foreach (var l in _limbs)
		{
			if (l.State != State.Up || l.SlamT >= 0f || l.Colossus || l.LoomTarget > 0f) continue;
			float d = l.Root.GlobalPosition.DistanceTo(boatWorld);
			if (d < bestD) { bestD = d; best = l; }
		}
		if (best == null) return false;
		best.SlamT = 0f;
		best.SlamHit = false;
		best.SlamTarget = boatWorld;
		return true;
	}

	private Limb _hunter;

	/// <summary>Adds the hunter: one more limb, kept mostly under the water, that the crossing moves
	/// along behind the boat (<see cref="SetHunter"/>). It survives <see cref="Release"/>.</summary>
	public void AddHunter(Vector3 world)
	{
		if (_hunter != null) return;
		_hunter = BuildLimb(ToLocal(world) with { Y = 0f }, 7.5f, 0.72f, 18);
		_hunter.Hunter = true;
		_hunter.Watcher = true;
		_hunter.State = State.Up;
		_hunter.RiseTime = 1f;
		_hunter.HuntRise = 0f;
		_hunter.Speed = 1.3f;
	}

	/// <summary>Moves the hunter to <paramref name="world"/> (on the water) facing <paramref name="toward"/>;
	/// <paramref name="rise"/> 0 = just a shadow under the surface, 1 = reared right up out of it.</summary>
	public void SetHunter(Vector3 world, Vector3 toward, float rise)
	{
		if (_hunter == null || _hunter.State == State.Sinking || _hunter.State == State.Under) return;
		Vector3 local = ToLocal(world);
		var p = _hunter.Root.Position;
		_hunter.Root.Position = new Vector3(local.X, p.Y, local.Z);
		Vector3 look = toward - world; look.Y = 0;
		if (look.LengthSquared() > 0.01f) _hunter.BaseYaw = Mathf.Atan2(look.X, look.Z);
		_hunter.HuntRise = Mathf.Clamp(rise, 0f, 1f);
	}

	/// <summary>The hunter rears over the boat to take it (the drowning).</summary>
	public void HunterStrike()
	{
		if (_hunter == null) return;
		_hunter.HuntRise = 1f;
		_hunter.LoomTarget = 1f;
	}

	/// <summary>
	/// One limb on its own, out on the open water during the crossing: it comes up at
	/// <paramref name="rootWorld"/> (on the surface) facing <paramref name="toward"/>, every eye on it
	/// open and turned on the boat. With <paramref name="strikeAfter"/> ≥ 0 it holds that many seconds,
	/// then rears and slams down toward <paramref name="toward"/> (<see cref="Slammed"/> reports where)
	/// and sinks; otherwise it stares for <paramref name="sinkAfter"/> seconds and sinks. Survives
	/// <see cref="Release"/>; <see cref="SinkAll"/> takes it down with the rest.
	/// </summary>
	public void Surface(Vector3 rootWorld, Vector3 toward, float length, float sinkAfter, float strikeAfter = -1f)
	{
		if (!Breaching)
		{
			GlobalPosition = rootWorld;
			_time = 0;
		}
		var l = BuildLimb(ToLocal(rootWorld) with { Y = 0f }, length, _rng.RandfRange(0.6f, 0.76f), _rng.RandiRange(14, 20));
		l.Lone = true;
		l.State = State.Rising;
		l.T = 0f;
		l.RiseTime = strikeAfter >= 0f ? 0.85f : 1.4f;
		l.SinkTime = 1.8f;
		l.SinkAfter = sinkAfter;
		l.StrikeAfter = strikeAfter;
		Vector3 look = toward - rootWorld; look.Y = 0;
		l.BaseYaw = Mathf.Atan2(look.X, look.Z);
		l.Root.Rotation = new Vector3(0, l.BaseYaw, 0);
		Visible = true;
		Breaching = true;
	}

	/// <summary>How far out from its root a limb of <paramref name="length"/> brings its tip down when it
	/// slams (the curl of the strike carries it most of the way over). Measured off the pose.</summary>
	public static float SlamReach(float length) => length * 0.65f;

	/// <summary>For tests: how many lone limbs (<see cref="Surface"/>) have come up so far.</summary>
	public int LoneSurfaced => _limbs.FindAll(l => l.Lone && l.Surfaced).Count;

	/// <summary>All but the watchers go back under; the watchers stay up, swaying, staring.</summary>
	public void Release()
	{
		foreach (var l in _limbs)
		{
			if (l.Hunter || l.Lone) continue;
			l.LoomTarget = 0f;
			if (!l.Watcher && l.State is State.Up or State.Rising) { l.State = State.Sinking; l.T = l.Colossus ? -0.6f : _rng.RandfRange(-0.3f, 0f); }
		}
	}

	/// <summary>Everything still up sinks (the boat made the far shore).</summary>
	public void SinkAll()
	{
		foreach (var l in _limbs)
			if (l.State is State.Up or State.Rising) { l.State = State.Sinking; l.T = _rng.RandfRange(-0.8f, 0f); }
	}

	// ------------------------------------------------------------------ animation

	private World.PhotoSubject _shotColossus, _shotLimb;

	/// <summary>The camera's subjects ride the limbs: the colossus, and any limb that is up.</summary>
	private void TrackPhotoSubjects()
	{
		_shotColossus ??= World.PhotoSubject.Attach(this, "leviathan", Vector3.Zero, 3f, 160f, 18f, false);
		_shotLimb ??= World.PhotoSubject.Attach(this, "lake_creature", Vector3.Zero, 2f, 90f, 16f, false);
		Limb col = null, up = null;
		foreach (var l in _limbs)
		{
			if (l.State != State.Up) continue;
			if (l.Colossus) col = l; else up ??= l;
		}
		if (_shotColossus != null) { _shotColossus.Visible = col != null; if (col != null) _shotColossus.GlobalPosition = col.Joints[Segments / 2].GlobalPosition; }
		if (_shotLimb != null) { _shotLimb.Visible = up != null; if (up != null) _shotLimb.GlobalPosition = up.Joints[Segments * 2 / 3].GlobalPosition; }
	}

	public override void _Process(double delta)
	{
		TrackPhotoSubjects();
		if (!Breaching) return;
		float dt = (float)delta;
		_time += dt;
		if (_blinkT >= 0f) { _blinkT += dt; if (_blinkT > 0.5f) _blinkT = -1f; }
		_cam ??= GetViewport().GetCamera3D();
		bool any = false;
		float life = 0f;
		foreach (var l in _limbs)
		{
			l.T += dt;
			switch (l.State)
			{
				case State.Rising:
					if (l.T < 0f) { any = true; continue; }
					l.Rise = l.Colossus ? Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, l.T / l.RiseTime)) : EaseOutBack(Mathf.Min(1f, l.T / l.RiseTime));
					if (l.Drips != null && !l.Drips.Emitting && l.Rise > 0.2f) l.Drips.Emitting = true;
					if (!l.Surfaced && l.Rise > 0.12f)
					{
						l.Surfaced = true;
						Surfaced?.Invoke(l.Root.GlobalPosition with { Y = GlobalPosition.Y });
						LakeFx.Column(GetParent(), l.Root.GlobalPosition with { Y = GlobalPosition.Y }, l.Length * (l.Colossus ? 0.6f : 0.8f));
						if (l.Colossus) LakeFx.Column(GetParent(), l.Root.GlobalPosition with { Y = GlobalPosition.Y } + new Vector3(1.2f, 0, -0.8f), 9f);
					}
					if (l.T >= l.RiseTime) { l.State = State.Up; l.T = 0f; l.UpFor = 0f; }
					break;
				case State.Up:
					l.Rise = l.Hunter ? Mathf.MoveToward(l.Rise, l.HuntRise, dt * 0.8f) : 1f;
					l.UpFor += dt;
					if (l.Drips != null && l.Drips.Emitting && l.UpFor > 3.5f) l.Drips.Emitting = false;
					if (l.Lone)
					{
						// a lone limb: holds, staring; strikes if it was sent to; then goes back under
						if (l.StrikeAfter >= 0f && !l.Struck && l.UpFor >= l.StrikeAfter) { l.Struck = true; l.SlamT = 0f; l.SlamHit = false; }
						bool done = l.Struck ? l.SlamT < 0f : l.SinkAfter >= 0f && l.UpFor >= l.SinkAfter;
						if (done) { l.State = State.Sinking; l.T = 0f; }
					}
					break;
				case State.Sinking:
					if (l.T < 0f) break;
					l.Rise = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, l.T / l.SinkTime));
					if (l.T >= l.SinkTime) { l.State = State.Under; l.Rise = 0f; l.Root.Visible = false; }
					break;
			}
			if (l.State != State.Under) any = true;
			life = Mathf.Max(life, l.Rise);
			Pose(l, dt);
		}
		Life = life;
		if (!any) { Breaching = false; QueueFree(); }
	}

	private static float EaseOutBack(float x)
	{
		const float c1 = 1.2f, c3 = c1 + 1f;
		return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
	}

	private void Pose(Limb l, float dt)
	{
		// up out of the water: the root climbs from well below the surface to just under it
		var p = l.Root.Position;
		p.Y = Mathf.Lerp(-(l.Length + 1.5f), -0.7f, l.Rise);
		l.Root.Position = p;
		if (!l.Colossus) l.Root.Rotation = new Vector3(0, l.BaseYaw + 0.55f * Mathf.SmoothStep(0f, 1f, l.Loom), 0);

		float t = (float)_time * l.Speed;
		l.Loom = Mathf.MoveToward(l.Loom, l.LoomTarget, dt * (l.LoomTarget > l.Loom ? 0.8f : 0.9f));
		float loom = Mathf.SmoothStep(0f, 1f, l.Loom);
		bool watcherOnly = l.Watcher && _limbs.Exists(o => o.State == State.Sinking && !o.Watcher);
		float amp = l.State == State.Rising ? 0.1f : (l.Watcher && watcherOnly ? 0.08f : 0.17f);
		if (l.Colossus) amp *= 0.45f;
		amp *= 1f - 0.7f * loom;   // hanging over the boat it goes almost still
		float rear = 0f, strike = 0f;
		if (l.SlamT >= 0f)
		{
			l.SlamT += dt;
			// wind up (rear back, 0.7 s), strike (0.28 s), hold down a moment, come back up (1.2 s)
			float s = l.SlamT;
			// (2026-10-15, "less robotic") eased, not linear: a slow gathering wind-up, a strike that accelerates into the
			// water (cubic in) and overshoots a little down into it, a heavy settle, a lazy rise back
			if (s < 0.7f) rear = Mathf.SmoothStep(0f, 1f, s / 0.7f);
			else if (s < 0.98f) { float u = (s - 0.7f) / 0.28f; float e = u * u * u; rear = 1f - e; strike = e * 1.12f; }
			else if (s < 1.5f) strike = 1f + 0.12f * Mathf.Exp(-(s - 0.98f) * 7f) * Mathf.Cos((s - 0.98f) * 14f);
			else if (s < 2.7f) strike = 1f - Mathf.SmoothStep(0f, 1f, (s - 1.5f) / 1.2f);
			else l.SlamT = -1f;
			if (!l.SlamHit && s >= 0.98f)
			{
				l.SlamHit = true;
				Vector3 tip = l.Joints[^1].GlobalTransform * (Vector3.Up * (l.Length / Segments));
				Vector3 at = new(tip.X, GlobalPosition.Y, tip.Z);
				LakeFx.Column(GetParent(), at, 4.5f);
				Slammed?.Invoke(at);
			}
		}
		for (int i = 0; i < l.Joints.Count; i++)
		{
			float f = (float)i / Segments;
			// (2026-10-15, "less robotic") two waves of unrelated speed travelling up it (each limb its own pair), so the sway
			// never repeats and no two limbs move in step; a slow breathing of its curl; and a slow roll round its own length
			float wave = (Mathf.Sin(t * 2.1f * l.FreqA + l.Phase - i * 0.62f) * 0.75f
				+ Mathf.Sin(t * 2.1f * l.FreqB + l.Phase2 - i * 0.95f) * 0.35f) * amp * (0.4f + f);
			float side = (Mathf.Cos(t * 1.6f * l.FreqA + l.Phase * 1.3f - i * 0.5f) * 0.7f
				+ Mathf.Sin(t * 1.1f * l.FreqB + l.Phase2 * 0.7f - i * 0.8f) * 0.4f) * amp * 0.8f * (0.3f + f);
			float twist = Mathf.Sin(t * 0.9f * l.FreqA + l.Phase2 - i * 0.45f) * l.Twist * f * (1f - 0.7f * loom);
			// lean toward the boat as it rises; the slam rears back past vertical, then curls hard over
			float curl = l.Curl * l.Rise * (0.5f + f) * (1f + 0.25f * Mathf.Sin(t * 0.55f * l.FreqB + l.Phase));
			curl += -0.16f * rear + 0.34f * strike * Mathf.SmoothStep(0.1f, 0.6f, f + 0.2f);
			// looming: stand up straight, then bend over at the top to hang above the boat
			curl += loom * (-0.08f + 0.62f * Mathf.SmoothStep(0.25f, 0.75f, f));
			// (2026-10-12, secondary motion) each joint eases toward its bend, the further out the looser: the tip trails the
			// root through a sway and whips on past it, as flesh does, not a stiff rod turned in one piece
			l.Lag ??= new Vector3[l.Joints.Count];
			var goal = new Vector3(curl + wave, twist, side);
			float follow = Mathf.Lerp(16f, 5f, f) * (0.4f + 0.5f * l.FreqA);   // (each limb its own stiffness)
			l.Lag[i] = l.Lag[i].Lerp(goal, 1f - Mathf.Exp(-dt * follow));
			l.Joints[i].Rotation = l.State == State.Rising && l.Rise < 0.05f ? goal : l.Lag[i];
		}
		if (l.Skel != null)
			for (int i = 0; i < l.Joints.Count; i++)
			{
				l.Skel.SetBonePoseRotation(i, l.Joints[i].Quaternion);
				l.Skel.SetBonePosePosition(i, l.Joints[i].Position);
			}
		// where it meets the water: rings spreading off it now and then while it's up and moving
		if (l.State is State.Up or State.Rising && l.Rise > 0.3f && _time >= l.NextRipple && GetParent() != null)
		{
			l.NextRipple = (float)_time + _rng.RandfRange(0.7f, 1.6f) / Mathf.Max(0.5f, l.Speed);
			LakeFx.Ripple(GetParent(), l.Root.GlobalPosition with { Y = GlobalPosition.Y + 0.02f }, l.Colossus ? 4.5f : 2.2f, _rng.RandfRange(1.3f, 2.2f), 0.4f);
		}
		UpdateEyes(l, dt);
	}

	private void UpdateEyes(Limb l, float dt)
	{
		bool open = (_eyesOpened || l.Hunter || l.Lone) && (l.State is State.Up or State.Rising) && (l.Rise > 0.6f || l.Hunter);
		if (l.State == State.Sinking) l.EyeScale = Mathf.MoveToward(l.EyeScale, 0f, dt * 1.5f);
		else if (open && l.EyeScale < 1f)
		{
			if (l.EyeScale == 0f) EyesOpen += l.Eyes.Count;
			l.EyeScale = Mathf.Min(1f, l.EyeScale + dt * 5f);
		}
		// a quick overshoot as they bulge open
		float s = l.EyeScale < 1f ? l.EyeScale * (1f + 0.25f * Mathf.Sin(l.EyeScale * Mathf.Pi)) : 1f;
		// the blink: every lid shut and open again together (0.5 s, closed at 0.2)
		float lid = _blinkT >= 0f ? Mathf.Clamp(Mathf.Abs(_blinkT - 0.2f) / 0.2f, 0.06f, 1f) : 1f;
		Vector3 target = _cam != null && IsInstanceValid(_cam) ? _cam.GlobalPosition : GlobalPosition + Vector3.Up * 100f;
		for (int e = 0; e < l.Eyes.Count; e++)
		{
			var eye = l.Eyes[e];
			float size = l.EyeSize[e] * Mathf.Max(0.001f, s);
			Vector3 at = eye.GlobalPosition;
			if (at.DistanceSquaredTo(target) <= 0.01f) continue;
			// (2026-10-15, "less robotic") each eye its own: it turns onto you at its own pace (some snap, some roll round
			// slowly), now and then darts off and back (a saccade), and blinks on its own clock, as well as all together
			bool perEye = e < l.EyeLook.Count;
			Vector3 aim = target;
			float blink = 1f;
			if (perEye)
			{
				l.EyeBlink[e] -= dt;
				if (l.EyeBlink[e] < -0.22f) { l.EyeBlink[e] = _rng.RandfRange(3f, 14f); if (_rng.Randf() < 0.3f) l.EyeOff[e] = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.6f, 0.6f), 0f); }
				if (l.EyeBlink[e] < 0f) blink = Mathf.Clamp(Mathf.Abs(l.EyeBlink[e] + 0.11f) / 0.11f, 0.08f, 1f);
				// the wander: a little off you, held a while, mostly on you
				float off = Mathf.Max(0f, Mathf.Sin((float)_time * 0.37f + e * 1.7f)) * 0.9f;
				aim = target + (_cam != null && IsInstanceValid(_cam) ? (_cam.GlobalBasis.X * l.EyeOff[e].X + _cam.GlobalBasis.Y * l.EyeOff[e].Y) * off : Vector3.Zero);
			}
			// an eye can't look through its own limb: past ~70 degrees off the way its socket faces out of the flesh, it strains
			// as far round as it can instead (an eye on the far side shows you its iris at the edge, not its back)
			Vector3 dir = (aim - at).Normalized();
			if (perEye && eye.GetParent() is Node3D jnt)
			{
				Vector3 outward = (jnt.GlobalBasis * (eye.Position with { Y = 0f })).Normalized();
				float ang = outward.AngleTo(dir);
				const float maxAng = 1.22f;
				if (ang > maxAng && outward.LengthSquared() > 0.5f)
				{
					Vector3 axis = outward.Cross(dir);
					if (axis.LengthSquared() > 1e-6f) dir = outward.Rotated(axis.Normalized(), maxAng);
				}
			}
			var want = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Forward : Vector3.Up).Orthonormalized().GetRotationQuaternion().Normalized();
			Quaternion q = want;
			if (perEye)
			{
				var cur = l.EyeLook[e];
				if (cur == Quaternion.Identity) cur = want;
				q = cur.Normalized().Slerp(want, 1f - Mathf.Exp(-dt * l.EyeRate[e])).Normalized();
				l.EyeLook[e] = q;
			}
			eye.GlobalBasis = new Basis(q) * Basis.FromScale(new Vector3(size, size * lid * blink, size));
		}
	}
}
