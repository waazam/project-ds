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
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		Visible = false;
	}

	// ------------------------------------------------------------------ building

	private Limb BuildLimb(Vector3 localRoot, float length, float baseR, int eyes)
	{
		var limb = new Limb
		{
			Length = length, Phase = _rng.RandfRange(0, Mathf.Tau), Curl = _rng.RandfRange(0.09f, 0.17f),
			Speed = _rng.RandfRange(0.8f, 1.25f),
		};
		limb.Root = new Node3D { Name = $"Tentacle{_limbs.Count}", Position = localRoot + Vector3.Down * (length + 1.5f) };
		AddChild(limb.Root);
		float segLen = length / Segments;
		Node3D parent = limb.Root;
		for (int i = 0; i < Segments; i++)
		{
			float f0 = (float)i / Segments, f1 = (float)(i + 1) / Segments;
			float r0 = Radius(baseR, f0), r1 = Radius(baseR, f1);
			var joint = new Node3D { Name = $"J{i}", Position = i == 0 ? Vector3.Zero : Vector3.Up * segLen };
			parent.AddChild(joint);
			var k = new MeshKit();
			k.Mat(Skin());
			k.Color = Colors.White;
			k.Cylinder(Vector3.Zero, Vector3.Up * segLen, r0, r1, 12, false, 1.5f);
			// a knuckle to hide the seam at the next joint
			k.Blob(Vector3.Up * segLen, Vector3.One * r1 * 1.04f, i * 7 + 3, 0.05f, false);
			// ring suckers in two staggered rows down the inner face (the side it curls toward, +Z)
			if (i < Segments - 1) TentacleKit.Suckers(k, segLen, r0, r1, i < 3 ? 2 : 3);
			k.CommitTo(joint, "Seg", true);
			limb.Joints.Add(joint);
			parent = joint;
		}
		// the mouth at the tip (the owner's references): jaws flaring round a toothed throat
		TentacleKit.Maw(limb.Joints[Segments - 1], Radius(baseR, 1f), baseR * 1.45f, Skin(), limb.Joints.Count * 31 + _limbs.Count).Position = Vector3.Up * segLen;
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
			var socket = new Node3D { Name = $"Eye{e}", Position = new Vector3(Mathf.Cos(ang) * r * 1.0f, local, Mathf.Sin(ang) * r * 1.0f) };
			limb.Joints[seg].AddChild(socket);
			BuildEye(socket);
			socket.Scale = Vector3.One * 0.001f;
			limb.Eyes.Add(socket);
			limb.EyeSize.Add(size);
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
		var draw = new QuadMesh { Size = Vector2.One * 0.07f };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = LakeFx.SoftDot(),
			AlbedoColor = new Color(0.95f, 0.88f, 0.8f, 0.7f),
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
	private static ShaderMaterial Skin() => _skin ??= TentacleKit.Flesh(null, 1.1f);

	private static SphereMesh _ball;
	private static TorusMesh _lidMesh;
	private static StandardMaterial3D _irisMat, _pupilMat;

	/// <summary>One eyeball in a unit socket (the socket is scaled to the eye's size and turned so its -Z
	/// faces the camera): a yellowed, veined white ball; on its front, a domed dark-red iris with a faint
	/// glow of its own, and a black pupil. Real geometry, so it reads the same from any angle and at the
	/// game's low resolution.</summary>
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
		socket.AddChild(Part(EyeMat(), Vector3.Zero, Vector3.One));
		socket.AddChild(Part(_irisMat, new Vector3(0, 0, -0.84f), new Vector3(0.5f, 0.5f, 0.2f)));
		socket.AddChild(Part(_pupilMat, new Vector3(0, 0, -0.99f), new Vector3(0.14f, 0.3f, 0.08f)));
		// a swollen, wet lid round it, so it sits in the flesh rather than on it
		_lidMesh ??= new TorusMesh { InnerRadius = 0.72f, OuterRadius = 1.12f, Rings = 14, RingSegments = 8 };
		socket.AddChild(new MeshInstance3D
		{
			Name = "Lid", Mesh = _lidMesh, MaterialOverride = TentacleKit.Lid, Position = new Vector3(0, 0, -0.18f),
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
				Color c = new Color(0.86f, 0.8f, 0.66f).Lerp(new Color(0.75f, 0.55f, 0.45f), Mathf.SmoothStep(0.4f, 1f, v));
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
		Visible = true;
		Breaching = true;
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

	/// <summary>All but the watchers go back under; the watchers stay up, swaying, staring.</summary>
	public void Release()
	{
		foreach (var l in _limbs)
		{
			if (l.Hunter) continue;
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

	public override void _Process(double delta)
	{
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
			if (s < 0.7f) rear = Mathf.SmoothStep(0f, 1f, s / 0.7f);
			else if (s < 0.98f) { rear = 1f - (s - 0.7f) / 0.28f; strike = (s - 0.7f) / 0.28f; }
			else if (s < 1.5f) strike = 1f;
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
			float wave = Mathf.Sin(t * 2.1f + l.Phase - i * 0.62f) * amp * (0.4f + f);
			float side = Mathf.Cos(t * 1.6f + l.Phase * 1.3f - i * 0.5f) * amp * 0.8f * (0.3f + f);
			// lean toward the boat as it rises; the slam rears back past vertical, then curls hard over
			float curl = l.Curl * l.Rise * (0.5f + f);
			curl += -0.16f * rear + 0.34f * strike * Mathf.SmoothStep(0.1f, 0.6f, f + 0.2f);
			// looming: stand up straight, then bend over at the top to hang above the boat
			curl += loom * (-0.08f + 0.62f * Mathf.SmoothStep(0.25f, 0.75f, f));
			l.Joints[i].Rotation = new Vector3(curl + wave, 0f, side);
		}
		UpdateEyes(l, dt);
	}

	private void UpdateEyes(Limb l, float dt)
	{
		bool open = (_eyesOpened || l.Hunter) && (l.State is State.Up or State.Rising) && (l.Rise > 0.6f || l.Hunter);
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
			if (at.DistanceSquaredTo(target) > 0.01f)
			{
				var basis = Basis.LookingAt(target - at, Vector3.Up);
				eye.GlobalBasis = basis * Basis.FromScale(new Vector3(size, size * lid, size));
			}
		}
	}
}
