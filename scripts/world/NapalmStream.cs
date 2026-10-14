using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The flamethrower's fire (Act 24; the owner: "the flame needs to be really well done ... a napalm stream of fire just
/// like ww2"; 2026-10-14d: "flamethrower scene needs some work it doesn't look great"). Built from footage of the M2-2 and
/// of napalm:
/// <list type="bullet">
/// <item><b>The rope.</b> Globs of burning fuel thrown from the nozzle twenty metres a second, arcing down under their
/// weight and slowed by the air (napalm_fire.gdshader): a narrow, bright jet at the nozzle, swelling as it flies into a
/// rolling, torn orange body, yellow-white only in its thick young core, deep red and then sooty black at its edges as it
/// burns out. Premultiplied: the fire adds its light, the soot darkens, so it never sums to a flat yellow sheet.</item>
/// <item><b>The splash and the cling.</b> Where a glob strikes, it splashes (a bloom of fire, a spray of burning
/// droplets) and the fuel sticks: patches of napalm (merged where they land close) that burn on for several seconds as
/// tongues of flame standing up off the surface (napalm_tongue.gdshader), each fed by every glob that lands on it, dying
/// down as its fuel goes.</item>
/// <item><b>The smoke.</b> Thick, oily, near-black smoke rolls off the stream's end and climbs off every patch, lit from
/// below by the fire (napalm_smoke.gdshader); on the snow and the ice, steam bursts off where it lands.</item>
/// <item><b>Embers.</b> Burning droplets thrown off the stream and the splashes, and sparks rising off the patches
/// (napalm_spark.gdshader).</item>
/// <item><b>Scorch.</b> Each patch leaves a char of soot and melt where it burned (a decal, its embers glowing as it
/// burns and cooling to black after; the oldest reused).</item>
/// <item><b>Heat shimmer</b> over the burning patches, and <b>the light</b>: a warm light riding the stream (casting
/// shadows: the trench posts' shadows swing on the walls as the stream sweeps), and one on the patches, both breathing
/// unevenly with the fire (slow, never a strobe: the owner's comfort).</item>
/// </list>
/// Five draws (the stream, the tongues, the smoke, the sparks, the haze) and the decals, a handful of ray tests a frame.
/// <see cref="Heat"/> lets anything ask how much fire is on it.
/// </summary>
public partial class NapalmStream : Node3D
{
	public const int MaxGlobs = 300, MaxSmoke = 260, MaxPatches = 36, TonguesPer = 3, MaxSparks = 220, MaxScorch = 28;
	[Export] public float Speed = 21f, Rate = 120f, Gravity = 8.5f, Drag = 0.42f, Life = 1.3f;
	/// <summary>The stream's light casts shadows (the posts' shadows swing on the walls).</summary>
	[Export] public bool LightShadows = true;

	private struct Glob { public Vector3 P, V; public float Age, Life, Size, Seed; public bool Alive, Splash; }
	private struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed, Glow; public bool Alive, Steam; }
	private struct Patch { public Vector3 P, N; public float Fuel, Age, Burn, Seed, Spread; public bool Alive; public int Scorch; }
	private struct Spark { public Vector3 P, V; public float Age, Life, Heat, Seed; public bool Alive, Float; }

	private readonly Glob[] _g = new Glob[MaxGlobs];
	private readonly Puff[] _s = new Puff[MaxSmoke];
	private readonly Patch[] _patch = new Patch[MaxPatches];
	private readonly Spark[] _spark = new Spark[MaxSparks];
	private MultiMesh _flameMM, _smokeMM, _tongueMM, _sparkMM;
	private MultiMeshInstance3D _flameMI, _smokeMI, _tongueMI, _sparkMI;
	private OmniLight3D _light, _splashLight;
	private MeshInstance3D _haze;
	public static bool HazeOn = true;
	private ShaderMaterial _hazeMat;
	private readonly Decal[] _scorch = new Decal[MaxScorch];
	private readonly float[] _scorchAge = new float[MaxScorch];
	private int _scorchNext;
	private float _splashLevel, _emitAcc, _lightLevel, _clock;
	private int _rayCursor, _warmFrames = -1;
	private readonly RandomNumberGenerator _rng = new();
	private static ShaderMaterial _fireMat, _smokeMat, _tongueMat, _sparkMat;
	private static FastNoiseLite _flick;
	private static Texture2D _scorchAlb, _scorchEmit;

	/// <summary>The number of globs alight in the stream (tests, and the light).</summary>
	public int Alight { get; private set; }
	/// <summary>The patches of napalm burning where it landed.</summary>
	public int Burning { get; private set; }
	/// <summary>Raised where a glob strikes something (a point, in the world).</summary>
	public event System.Action<Vector3> Struck;

	public override void _Ready()
	{
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		_rng.Randomize();
		MakeMaterials();
		_flick ??= new FastNoiseLite { Frequency = 1f, FractalOctaves = 2, Seed = 77 };
		_flameMM = Multi(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 6 }, MaxGlobs);
		_tongueMM = Multi(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 8 }, MaxPatches * TonguesPer);
		_smokeMM = Multi(new QuadMesh { Size = Vector2.One }, MaxSmoke);
		_sparkMM = Multi(new QuadMesh { Size = Vector2.One }, MaxSparks);
		// (the smoke drawn first, the fire over it; their bounds follow the fire each frame: a fixed box about the world's
		// origin culled the whole stream away in the maze, far from it)
		_smokeMI = Instance("Smoke", _smokeMM, _smokeMat, -1f);
		_tongueMI = Instance("Clinging", _tongueMM, _tongueMat, 0f);
		_flameMI = Instance("Flames", _flameMM, _fireMat, 0.5f);
		_sparkMI = Instance("Sparks", _sparkMM, _sparkMat, 1f);
		_light = new OmniLight3D { Name = "FireLight", LightColor = new Color(1f, 0.52f, 0.2f), OmniRange = 13f, OmniAttenuation = 1.4f, LightEnergy = 0f, ShadowEnabled = LightShadows, ShadowBias = 0.08f, OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid, LightVolumetricFogEnergy = 2f, Visible = false };
		AddChild(_light);
		// the light of the patches burning where it landed: the snow round them lit orange, wavering with the burning
		_splashLight = new OmniLight3D { Name = "SplashLight", LightColor = new Color(1f, 0.46f, 0.15f), OmniRange = 8f, OmniAttenuation = 1.3f, LightEnergy = 0f, ShadowEnabled = false, LightVolumetricFogEnergy = 1.5f, Visible = false };
		AddChild(_splashLight);
		// the shimmer over the patches (heat_haze.gdshader, as FireVfx's)
		_hazeMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/heat_haze.gdshader"), RenderPriority = -1 };
		_hazeMat.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 64, Height = 64, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.08f, Seed = 913 } });
		_hazeMat.SetShaderParameter("strength", 0.006f);
		_haze = new MeshInstance3D { Name = "Haze", Mesh = new QuadMesh { Size = new Vector2(1f, 1f), CenterOffset = new Vector3(0, 0.5f, 0) }, MaterialOverride = _hazeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
		AddChild(_haze);
		for (int i = 0; i < MaxScorch; i++)
		{
			_scorch[i] = new Decal
			{
				Name = $"Scorch{i}", TextureAlbedo = ScorchAlbedo(), TextureEmission = ScorchEmission(), EmissionEnergy = 0f, AlbedoMix = 1f,
				Size = new Vector3(1.4f, 0.8f, 1.4f), UpperFade = 0.3f, LowerFade = 0.3f, NormalFade = 0f, Visible = false,
				Modulate = new Color(1, 1, 1, 0), CullMask = 0xFFFFFu & ~Player.HeldItem.HeldLayer, DistanceFadeEnabled = true, DistanceFadeBegin = 30f, DistanceFadeLength = 8f,
			};
			AddChild(_scorch[i]);
		}
		_warmFrames = 0;
	}

	private static MultiMesh Multi(Mesh mesh, int n)
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = n };
		for (int i = 0; i < n; i++) mm.SetInstanceTransform(i, Hidden);
		return mm;
	}

	private MultiMeshInstance3D Instance(string name, MultiMesh mm, Material mat, float sort)
	{
		var mi = new MultiMeshInstance3D { Name = name, Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, SortingOffset = sort, GIMode = GeometryInstance3D.GIModeEnum.Disabled };
		AddChild(mi);
		return mi;
	}

	private static readonly Transform3D Hidden = new(Basis.FromScale(Vector3.Zero), Vector3.Zero);

	private static void MakeMaterials()
	{
		if (_fireMat != null) return;
		var noise = new NoiseTexture2D { Width = 128, Height = 128, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.03f, FractalOctaves = 3, Seed = 9001 } };
		_fireMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/napalm_fire.gdshader"), ResourceName = "napalm_fire" };
		_tongueMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/napalm_tongue.gdshader"), ResourceName = "napalm_tongue" };
		_sparkMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/napalm_spark.gdshader"), ResourceName = "napalm_spark" };
		_smokeMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/napalm_smoke.gdshader"), ResourceName = "napalm_smoke" };
		_smokeMat.SetShaderParameter("sheet", GD.Load<Texture2D>("res://assets/textures/weather/smoke_sheet.png"));
		_smokeMat.SetShaderParameter("noise_tex", noise);
	}

	/// <summary>The clinging fire's material (napalm_tongue.gdshader), for fire on a body (<see cref="NapalmBurn"/>).</summary>
	public static ShaderMaterial TongueMaterial() { MakeMaterials(); return _tongueMat; }

	/// <summary>The fire's materials, for a load's shader warm-up.</summary>
	public static IEnumerable<Material> WarmMaterials()
	{
		MakeMaterials();
		return new Material[] { _fireMat, _tongueMat, _sparkMat, _smokeMat };
	}

	/// <summary>The igniter's pilot: two small licks of flame at the nozzle's mouth (a few centimetres; it was a FireVfx,
	/// whose least tongue is 35 cm wide: a flat disc of fire over the nozzle). Its material is the clinging fire's, not faded
	/// close to the eye.</summary>
	public static MultiMeshInstance3D PilotFlame()
	{
		MakeMaterials();
		var mat = (ShaderMaterial)_tongueMat.Duplicate();
		mat.SetShaderParameter("near_start", 0f);
		mat.SetShaderParameter("near_end", 0.01f);
		mat.SetShaderParameter("brightness", 1.6f);
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 6 }, InstanceCount = 2 };
		mm.SetInstanceTransform(0, new Transform3D(Basis.FromScale(new Vector3(0.4f, 1.1f, 0.4f)), Vector3.Zero));
		mm.SetInstanceCustomData(0, new Color(0.31f, 0.9f, 0.01f, 0.9f));
		mm.SetInstanceTransform(1, new Transform3D(new Basis(Vector3.Forward, 0.3f) * Basis.FromScale(new Vector3(0.26f, 0.75f, 0.26f)), new Vector3(0.12f, 0f, -0.08f)));
		mm.SetInstanceCustomData(1, new Color(0.77f, 1.1f, 0.01f, 0.7f));
		return new MultiMeshInstance3D { Name = "PilotFlame", Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(-Vector3.One * 2f, Vector3.One * 4f), Scale = Vector3.One * 0.05f };
	}

	// ------------------------------------------------------------------ the scorch's textures

	/// <summary>A char where napalm burned: black soot at its heart, a ring of brown scorch and dark melt round it, torn at
	/// the edge, flecked (drawn once, in code).</summary>
	private static Texture2D ScorchAlbedo()
	{
		if (_scorchAlb != null) return _scorchAlb;
		const int S = 128;
		var n = new FastNoiseLite { Frequency = 0.06f, FractalOctaves = 4, Seed = 4242 };
		var f = new FastNoiseLite { Frequency = 0.35f, Seed = 99 };
		var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		var emit = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v);
				float nn = n.GetNoise2D(x, y) * 0.5f + 0.5f;
				float fl = f.GetNoise2D(x, y) * 0.5f + 0.5f;
				float edge = r + (nn - 0.5f) * 0.55f;
				float a = 1f - Mathf.SmoothStep(0.55f, 0.95f, edge);
				float core = 1f - Mathf.SmoothStep(0.15f, 0.6f, edge);
				// soot black at the heart, a brown scorch toward the rim, a little lighter grey ash in flecks
				var soot = new Color(0.025f, 0.022f, 0.02f);
				var scorch = new Color(0.16f, 0.1f, 0.06f);
				var c = scorch.Lerp(soot, core);
				if (fl > 0.78f && core > 0.4f) c = c.Lerp(new Color(0.2f, 0.19f, 0.18f), (fl - 0.78f) * 3f);
				img.SetPixel(x, y, new Color(c.R, c.G, c.B, Mathf.Clamp(a * (0.75f + 0.25f * nn), 0f, 1f) * 0.92f));
				// embers glowing in the char while it burns: specks and threads in the heart
				float ember = Mathf.Clamp((fl - 0.62f) * 3.5f, 0f, 1f) * core;
				emit.SetPixel(x, y, new Color(1f * ember, 0.32f * ember, 0.05f * ember, 1f));
			}
		img.GenerateMipmaps();
		emit.GenerateMipmaps();
		_scorchAlb = ImageTexture.CreateFromImage(img);
		_scorchEmit = ImageTexture.CreateFromImage(emit);
		return _scorchAlb;
	}

	private static Texture2D ScorchEmission() { ScorchAlbedo(); return _scorchEmit; }

	// ------------------------------------------------------------------ firing

	/// <summary>Fire this frame (or not): from <paramref name="nozzle"/> along <paramref name="dir"/>, carried on by
	/// <paramref name="carry"/> (the firer's own motion).</summary>
	public void Emit(bool firing, Vector3 nozzle, Vector3 dir, Vector3 carry, float dt)
	{
		if (!firing) { _emitAcc = 0f; return; }
		_emitAcc += Rate * dt;
		while (_emitAcc >= 1f)
		{
			_emitAcc -= 1f;
			int i = Free(_g);
			if (i < 0) break;
			var spread = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * 0.02f;
			float back = _rng.Randf() * dt;   // (spread along the frame, not bunched at its start)
			var v = (dir + spread).Normalized() * Speed * _rng.RandfRange(0.94f, 1.05f) + carry;
			_g[i] = new Glob { P = nozzle + v * back, V = v, Age = 0f, Life = Life * _rng.RandfRange(0.85f, 1.15f), Size = _rng.RandfRange(0.07f, 0.1f), Seed = _rng.Randf(), Alive = true };
			// now and then a burning droplet flung off the jet
			if (_rng.Randf() < 0.12f) AddSpark(nozzle + v * 0.08f, v * 0.6f + RandDir() * 2.5f, 0.5f, 1f, false);
		}
	}

	private static int Free(Glob[] a) { for (int i = 0; i < a.Length; i++) if (!a[i].Alive) return i; return -1; }
	private Vector3 RandDir() => new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)).LimitLength(1f);

	public override void _Process(double delta)
	{
		float dt = Mathf.Min((float)delta, 0.05f);
		_clock += dt;
		var space = GetWorld3D().DirectSpaceState;
		var cam = GetViewport()?.GetCamera3D();
		Vector3 eye = cam?.GlobalPosition ?? Vector3.Zero;
		Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
		void Grow(Vector3 p, float r) { lo = lo.Min(p - Vector3.One * r); hi = hi.Max(p + Vector3.One * r); }
		int rays = 0, alight = 0;
		Vector3 sum = Vector3.Zero;
		// ---- the stream
		for (int i = 0; i < MaxGlobs; i++)
		{
			ref var g = ref _g[i];
			if (!g.Alive) continue;
			g.Age += dt;
			float u = g.Age / g.Life;
			if (u >= 1f)
			{
				g.Alive = false;
				_flameMM.SetInstanceTransform(i, Hidden);
				if (!g.Splash && _rng.Randf() < 0.55f) Smoke(g.P + Vector3.Up * 0.2f, g.Size * 3.2f, 0.8f, false);
				continue;
			}
			if (!g.Splash)
			{
				var prev = g.P;
				g.V += Vector3.Down * Gravity * dt;
				g.V -= g.V * Drag * dt;
				g.V += Vector3.Up * 1.4f * u * dt;   // (the hot gas round it rising as it slows)
				g.P += g.V * dt;
				// a ray along its step (half of them each frame, in turn: a glob between its tests can't go far)
				if ((i + _rayCursor) % 2 == 0 && rays < 110)
				{
					rays++;
					var q = PhysicsRayQueryParameters3D.Create(prev - g.V * dt, g.P, 1);
					var hit = space.IntersectRay(q);
					if (hit.Count > 0)
					{
						var hp = (Vector3)hit["position"];
						var hn = (Vector3)hit["normal"];
						// the splash: the glob blooms against it for a moment; the fuel sticks and burns on
						g.P = hp + hn * 0.15f;
						g.V = hn * 0.6f + Vector3.Up * 0.4f;
						g.Splash = true;
						g.Life = g.Age + _rng.RandfRange(0.35f, 0.55f);
						Land(hp, hn, hit["collider"].AsGodotObject() as Node);
						Struck?.Invoke(hp + hn * 0.12f);
					}
				}
			}
			else g.P += g.V * dt;
			u = g.Age / g.Life;
			float size, heat;
			Basis basis;
			if (g.Splash)
			{
				// a splash's bloom: it spreads out flat against the surface and rolls up
				float s = Mathf.Min(g.Age * 2.2f, 1f);
				size = Mathf.Max(g.Size * 10f, 0.7f) * (0.6f + 0.7f * s);
				heat = 0.9f;
				basis = Basis.FromScale(new Vector3(size, size * 0.8f, size));
			}
			else
			{
				// a narrow jet at the nozzle, swelling into billows at its end
				float grow = Mathf.Pow(Mathf.Min(g.Age / 0.6f, 1f), 1.1f);
				size = g.Size * (1f + 12f * grow);
				heat = Mathf.Clamp(1f - g.Age * 0.75f, 0.15f, 1f);
				float stretch = 1f + Mathf.Min(g.V.Length() * 0.09f, 2.0f) * (1f - 0.5f * grow);
				// (young, it's drawn out into one rope: each glob long enough to reach well into the next, no string of beads)
				float len = Mathf.Max(size * stretch, Mathf.Lerp(0.55f, 0f, Mathf.Clamp(g.Age / 0.35f, 0f, 1f)));
				basis = Volume(g.V, len, size * 0.85f);
			}
			_flameMM.SetInstanceTransform(i, new Transform3D(basis, g.P));
			_flameMM.SetInstanceCustomData(i, new Color(u, heat, g.Seed, g.Splash ? 1f : 0f));
			alight++;
			sum += g.P;
			Grow(g.P, size * 2f);
			// the stream's end trails black smoke, thickening as it burns out
			if (!g.Splash && u > 0.45f && _rng.Randf() < dt * 3.2f) Smoke(g.P + Vector3.Up * size * 0.4f, size * 1.1f, 0.9f, false);
		}
		_rayCursor++;
		Alight = alight;
		// ---- the patches burning where it landed
		int burning = 0;
		float fuelSum = 0f;
		Vector3 patchSum = Vector3.Zero;
		for (int i = 0; i < MaxPatches; i++)
		{
			ref var pt = ref _patch[i];
			if (!pt.Alive) { for (int k = 0; k < TonguesPer; k++) _tongueMM.SetInstanceTransform(i * TonguesPer + k, Hidden); continue; }
			pt.Age += dt;
			pt.Fuel -= dt / pt.Burn;
			if (pt.Fuel <= 0f)
			{
				pt.Alive = false;
				for (int k = 0; k < TonguesPer; k++) _tongueMM.SetInstanceTransform(i * TonguesPer + k, Hidden);
				Smoke(pt.P + Vector3.Up * 0.3f, 0.7f, 0.2f, false);
				continue;
			}
			float strength = Mathf.Clamp(pt.Fuel * 1.6f, 0f, 1f) * Mathf.Clamp(pt.Age * 5f, 0f, 1f);
			for (int k = 0; k < TonguesPer; k++)
			{
				float a = pt.Seed * 40f + k * 2.1f;
				var off = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (0.12f + 0.45f * pt.Spread) * (k == 0 ? 0.15f : 1f);
				// tongues stand up whatever they're on (on a wall, up its face)
				off -= pt.N * pt.N.Dot(off);
				// (the first a low, wide carpet of fire over the splash; the rest licks of every height off it, rising and
				// falling unevenly)
				float lick = 0.75f + 0.25f * Mathf.Sin(_clock * (1.3f + 0.4f * k) + a) + 0.15f * Mathf.Sin(_clock * 3.1f + a * 2f);
				float h = k == 0 ? (0.3f + 0.35f * strength) * (0.9f + 0.1f * lick) : (0.3f + 0.6f * strength) * (0.6f + 0.2f * k) * lick;
				float w = k == 0 ? (0.25f + 0.3f * pt.Spread) * (0.6f + 0.4f * strength) : (0.2f + 0.15f * pt.Spread) * (0.6f + 0.4f * strength);
				var at = pt.P + off + pt.N * 0.04f;
				_tongueMM.SetInstanceTransform(i * TonguesPer + k, new Transform3D(new Basis(Vector3.Up, a) * Basis.FromScale(new Vector3(w, h, w)), at + Vector3.Down * 0.04f));
				_tongueMM.SetInstanceCustomData(i * TonguesPer + k, new Color(Mathf.PosMod(pt.Seed + k * 0.37f, 1f), 0.3f + 0.2f * k, 0.03f, strength));
				Grow(at + Vector3.Up * h * 0.5f, h);
			}
			burning++;
			fuelSum += strength;
			patchSum += pt.P * strength;
			// it smokes as it burns (black, lit from below), steams off the snow early on, throws up the odd spark
			if (_rng.Randf() < dt * (1.2f + 2.2f * strength)) Smoke(pt.P + Vector3.Up * (0.5f + 0.6f * strength) + RandDir() * 0.15f, 0.45f + 0.4f * strength, strength, false);
			if (pt.Age < 0.9f && pt.Fuel > 0.5f && _rng.Randf() < dt * 1.2f) Smoke(pt.P + pt.N * 0.1f + RandDir() * 0.3f, 0.5f, 0.3f, true);
			if (_rng.Randf() < dt * 4f * strength) AddSpark(pt.P + RandDir() * 0.2f + Vector3.Up * 0.2f, Vector3.Up * _rng.RandfRange(0.6f, 1.6f) + RandDir() * 0.4f, _rng.RandfRange(1.2f, 2.4f), 0.8f, true);
			// its scorch: glowing while it burns, cooling after
			if (pt.Scorch >= 0)
			{
				var d = _scorch[pt.Scorch];
				d.EmissionEnergy = Mathf.Max(d.EmissionEnergy, 1.6f * strength);
				_scorchAge[pt.Scorch] = 0f;
			}
		}
		Burning = burning;
		// ---- the scorch: darkening in as it burns, its embers cooling, fading only after a long while
		for (int i = 0; i < MaxScorch; i++)
		{
			var d = _scorch[i];
			if (!d.Visible) continue;
			_scorchAge[i] += dt;
			float t = _scorchAge[i];
			var m = d.Modulate;
			m.A = Mathf.MoveToward(m.A, t > 120f ? 0f : 1f, dt * (t > 120f ? 0.05f : 0.9f));
			d.Modulate = m;
			d.EmissionEnergy = Mathf.MoveToward(d.EmissionEnergy, 0f, dt * 0.35f);
			if (t > 120f && m.A <= 0.001f) d.Visible = false;
		}
		// ---- smoke and steam
		var wind = Weather.Wind;
		for (int i = 0; i < MaxSmoke; i++)
		{
			ref var s = ref _s[i];
			if (!s.Alive) continue;
			s.Age += dt;
			float u = s.Age / s.Life;
			if (u >= 1f) { s.Alive = false; _smokeMM.SetInstanceTransform(i, Hidden); continue; }
			var rise = s.Steam ? new Vector3(0, 0.9f, 0) : new Vector3(wind.X * 0.15f, 1.3f, wind.Z * 0.15f);
			s.V = s.V.Lerp(rise, dt * 0.9f);
			s.P += s.V * dt;
			float size = s.Size * (s.Steam ? 0.8f + 1.8f * u : 1f + 2.6f * Mathf.Sqrt(u));
			_smokeMM.SetInstanceTransform(i, new Transform3D(Facing(eye, s.P, Vector3.Up, size, size), s.P));
			_smokeMM.SetInstanceCustomData(i, new Color(u, s.Steam ? 1f : 0f, s.Seed, s.Glow));
			Grow(s.P, size);
		}
		// ---- sparks
		for (int i = 0; i < MaxSparks; i++)
		{
			ref var e = ref _spark[i];
			if (!e.Alive) continue;
			e.Age += dt;
			float u = e.Age / e.Life;
			if (u >= 1f) { e.Alive = false; _sparkMM.SetInstanceTransform(i, Hidden); continue; }
			if (e.Float)
			{
				// rising off the fire on its heat, wandering
				e.V += (new Vector3(Mathf.Sin(_clock * 3f + e.Seed * 30f), 0.6f, Mathf.Cos(_clock * 2.6f + e.Seed * 21f)) * 1.2f - e.V * 0.8f) * dt;
			}
			else e.V += Vector3.Down * 6f * dt - e.V * 0.6f * dt;
			e.P += e.V * dt;
			float len = Mathf.Clamp(e.V.Length() * 0.03f, 0.02f, 0.18f), wid = 0.018f;
			_sparkMM.SetInstanceTransform(i, new Transform3D(Facing(eye, e.P, e.V.LengthSquared() > 1e-4f ? e.V.Normalized() : Vector3.Up, len, wid), e.P));
			_sparkMM.SetInstanceCustomData(i, new Color(u, e.Heat, e.Seed, 0f));
			Grow(e.P, 0.2f);
		}
		if (hi.X >= lo.X)
		{
			var box = new Aabb(lo - Vector3.One, hi - lo + Vector3.One * 2f);
			_flameMI.CustomAabb = box; _smokeMI.CustomAabb = box; _tongueMI.CustomAabb = box; _sparkMI.CustomAabb = box;
		}
		WarmStep(cam);
		// ---- the light: uneven, breathing with the fire (smooth noise, a few hertz: alive, never a strobe)
		float flick = 1f + 0.16f * _flick.GetNoise1D(_clock * 7f) + 0.08f * _flick.GetNoise1D(_clock * 17f + 40f);
		float flick2 = 1f + 0.18f * _flick.GetNoise1D(_clock * 5f + 90f) + 0.07f * _flick.GetNoise1D(_clock * 13f + 140f);
		float want = Mathf.Clamp(alight / 40f, 0f, 1f);
		_lightLevel = Mathf.Lerp(_lightLevel, want, 1f - Mathf.Exp(-dt * 8f));
		_light.Visible = _lightLevel > 0.02f;
		// (in the middle of the stream, a little toward its near end: the near walls and the hands take the most of it)
		if (alight > 0) _light.GlobalPosition = _light.GlobalPosition.Lerp(sum / alight + Vector3.Up * 0.3f, 1f - Mathf.Exp(-dt * 12f));
		_light.LightEnergy = _lightLevel * 3.2f * flick;
		_splashLevel = Mathf.Lerp(_splashLevel, Mathf.Clamp(fuelSum / 4f, 0f, 1f), 1f - Mathf.Exp(-dt * 3f));
		_splashLight.Visible = _splashLevel > 0.02f;
		if (fuelSum > 0.01f) _splashLight.GlobalPosition = _splashLight.GlobalPosition.Lerp(patchSum / fuelSum + Vector3.Up * 0.7f, 1f - Mathf.Exp(-dt * 4f));
		_splashLight.LightEnergy = _splashLevel * 2.4f * flick2;
		_splashLight.OmniRange = 5f + 4f * _splashLevel;
		// the shimmer over the patches
		_haze.Visible = HazeOn && _splashLevel > 0.08f;
		if (_haze.Visible)
		{
			_haze.GlobalTransform = new Transform3D(Basis.FromScale(new Vector3(1.6f + 2f * _splashLevel, 1.8f + 1.6f * _splashLevel, 1f)), _splashLight.GlobalPosition + Vector3.Down * 0.2f);
			_hazeMat.SetShaderParameter("intensity", Mathf.Clamp(_splashLevel * 1.4f, 0f, 1f));
		}
	}

	/// <summary>A glob has struck: the fuel sticks (fed into a patch close by, or a new one), a spray of burning droplets,
	/// a burst of steam off the snow.</summary>
	private void Land(Vector3 at, Vector3 normal, Node what)
	{
		// (a near-level surface taken as level: the patch's tongues and its char lie on it, not on a board's bevel)
		if (normal.Y > 0.55f) normal = Vector3.Up;
		int best = -1;
		float bd = 0.5f * 0.5f;
		for (int i = 0; i < MaxPatches; i++)
		{
			if (!_patch[i].Alive) continue;
			float d = _patch[i].P.DistanceSquaredTo(at);
			if (d < bd) { bd = d; best = i; }
		}
		if (best >= 0)
		{
			ref var p = ref _patch[best];
			p.Fuel = Mathf.Min(p.Fuel + 0.09f, 1.2f);
			p.Spread = Mathf.Min(p.Spread + 0.03f, 1f);
			p.P = p.P.Lerp(at, 0.08f);
			// a well-fed splash throws its fuel on: burning gobs splashed out across the surface round it, catching where
			// they land (the fire spreads out from where the stream strikes, as napalm does)
			if (p.Spread > 0.45f && _rng.Randf() < 0.12f)
			{
				var t = RandDir();
				t = (t - normal * normal.Dot(t)).Normalized() * _rng.RandfRange(0.6f, 1.5f);
				var from = at + normal * 0.4f + t;
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from - normal * 1.4f + Vector3.Down * 0.6f, 1));
				if (hit.Count > 0) NewPatch((Vector3)hit["position"], (Vector3)hit["normal"], 0.45f);
			}
		}
		else NewPatch(at, normal, 0.6f);
		// the spray: droplets of burning fuel kicked off along the surface
		int n = _rng.Randf() < 0.5f ? 1 : 2;
		for (int k = 0; k < n; k++)
		{
			var d = (RandDir() + normal * 0.8f).Normalized();
			AddSpark(at + normal * 0.05f, d * _rng.RandfRange(2f, 5f), _rng.RandfRange(0.4f, 0.9f), 1f, false);
		}
		if (_rng.Randf() < 0.18f) Smoke(at + normal * 0.15f, 0.4f, 0.4f, true);
	}

	/// <summary>A new patch of burning napalm (or the weakest replaced).</summary>
	private void NewPatch(Vector3 at, Vector3 normal, float fuel)
	{
		if (normal.Y > 0.55f) normal = Vector3.Up;
		int slot = -1;
		float weakest = float.MaxValue;
		for (int i = 0; i < MaxPatches; i++)
		{
			if (!_patch[i].Alive) { slot = i; break; }
			if (_patch[i].Fuel < weakest) { weakest = _patch[i].Fuel; slot = i; }
		}
		_patch[slot] = new Patch { P = at, N = normal, Fuel = fuel, Age = 0f, Burn = _rng.RandfRange(12f, 16f), Seed = _rng.Randf(), Spread = 0.3f, Alive = true, Scorch = PlaceScorch(at, normal) };
	}

	private int PlaceScorch(Vector3 at, Vector3 normal)
	{
		// (an old scorch close by takes it instead: one char spreading, not a heap of them)
		for (int i = 0; i < MaxScorch; i++)
			if (_scorch[i].Visible && _scorch[i].GlobalPosition.DistanceSquaredTo(at) < 0.8f * 0.8f)
			{
				var s = _scorch[i].Size;
				_scorch[i].Size = new Vector3(Mathf.Min(s.X + 0.25f, 2.4f), s.Y, Mathf.Min(s.Z + 0.25f, 2.4f));
				return i;
			}
		int k = _scorchNext;
		_scorchNext = (_scorchNext + 1) % MaxScorch;
		var dcl = _scorch[k];
		var y = normal.Normalized();
		var x = Mathf.Abs(y.Y) < 0.9f ? y.Cross(Vector3.Up).Normalized() : y.Cross(Vector3.Right).Normalized();
		var z = x.Cross(y);
		dcl.GlobalTransform = new Transform3D(new Basis(x, y, z) * new Basis(Vector3.Up, _rng.Randf() * Mathf.Tau), at);
		float sz = _rng.RandfRange(1.1f, 1.5f);
		dcl.Size = new Vector3(sz, 0.9f, sz);
		dcl.Modulate = new Color(1, 1, 1, 0);
		dcl.EmissionEnergy = 1.2f;
		dcl.Visible = true;
		_scorchAge[k] = 0f;
		return k;
	}

	private void AddSpark(Vector3 at, Vector3 v, float life, float heat, bool floating)
	{
		for (int i = 0; i < MaxSparks; i++)
		{
			if (_spark[i].Alive) continue;
			_spark[i] = new Spark { P = at, V = v, Age = 0f, Life = life, Heat = heat * _rng.RandfRange(0.7f, 1f), Seed = _rng.Randf(), Alive = true, Float = floating };
			return;
		}
	}

	private void Smoke(Vector3 at, float size, float glow, bool steam)
	{
		for (int i = 0; i < MaxSmoke; i++)
		{
			if (_s[i].Alive) continue;
			_s[i] = new Puff
			{
				P = at, V = new Vector3(_rng.RandfRange(-0.3f, 0.3f), steam ? 0.9f : 0.6f, _rng.RandfRange(-0.3f, 0.3f)), Age = 0f,
				Life = steam ? _rng.RandfRange(1.0f, 1.8f) : _rng.RandfRange(2.6f, 4.0f), Size = Mathf.Max(size, steam ? 0.35f : 0.5f),
				Seed = _rng.Randf(), Glow = glow, Alive = true, Steam = steam,
			};
			return;
		}
	}

	/// <summary>The first few frames: one of each of the fire's pieces drawn tiny before the eye, so their shaders are built
	/// during the load, not at the first shot (the lag pass).</summary>
	private void WarmStep(Camera3D cam)
	{
		if (_warmFrames < 0 || cam == null) return;
		if (_warmFrames++ > 5)
		{
			_warmFrames = -1;
			foreach (var mm in new[] { _flameMM, _tongueMM, _smokeMM, _sparkMM }) if (!InUse(mm)) mm.SetInstanceTransform(0, Hidden);
			_haze.Visible = false;
			return;
		}
		var at = cam.GlobalPosition - cam.GlobalBasis.Z * 0.6f;
		var tiny = new Transform3D(Basis.FromScale(Vector3.One * 0.001f), at);
		_flameMM.SetInstanceTransform(0, tiny);
		_tongueMM.SetInstanceTransform(0, tiny);
		_smokeMM.SetInstanceTransform(0, tiny);
		_sparkMM.SetInstanceTransform(0, tiny);
		foreach (var mi in new[] { _flameMI, _smokeMI, _tongueMI, _sparkMI }) mi.CustomAabb = new Aabb(at - Vector3.One, Vector3.One * 2f);
		_haze.GlobalTransform = tiny;
		_haze.Visible = true;
	}

	private bool InUse(MultiMesh mm) =>
		mm == _flameMM ? _g[0].Alive : mm == _smokeMM ? _s[0].Alive : mm == _sparkMM ? _spark[0].Alive : _patch[0].Alive;

	/// <summary>A glob's basis as a volume: its length along its flight, up as near world up as that allows, its width across.</summary>
	private static Basis Volume(Vector3 along, float length, float width)
	{
		if (along.LengthSquared() < 1e-6f) along = Vector3.Forward;
		var x = along.Normalized();
		if (Mathf.Abs(x.Y) > 0.97f) x = (x + new Vector3(0.1f, 0, 0.1f)).Normalized();
		var z = x.Cross(Vector3.Up).Normalized();
		var y = z.Cross(x).Normalized();
		return new Basis(x * length, y * width, z * width);
	}

	/// <summary>A quad's basis facing <paramref name="eye"/> from <paramref name="at"/>, its length (x) along
	/// <paramref name="along"/> as seen from there.</summary>
	private static Basis Facing(Vector3 eye, Vector3 at, Vector3 along, float length, float width)
	{
		var f = eye - at;
		if (f.LengthSquared() < 1e-6f) f = Vector3.Back;
		f = f.Normalized();
		var x = along - f * along.Dot(f);
		if (x.LengthSquared() < 1e-6f) x = Mathf.Abs(f.Y) < 0.9f ? Vector3.Up.Cross(f) : Vector3.Right;
		x = x.Normalized();
		var y = f.Cross(x).Normalized();
		return new Basis(x * length, y * width, f * width);
	}

	/// <summary>How much fire is on a sphere: the burning globs inside it (the hotter flying ones counting more) and the
	/// patches clinging there.</summary>
	public float Heat(Vector3 centre, float radius)
	{
		float h = 0f, r2 = radius * radius;
		for (int i = 0; i < MaxGlobs; i++)
		{
			ref var g = ref _g[i];
			if (!g.Alive) continue;
			if (g.P.DistanceSquaredTo(centre) < r2) h += g.Splash ? 0.5f : 1f;
		}
		for (int i = 0; i < MaxPatches; i++)
			if (_patch[i].Alive && _patch[i].P.DistanceSquaredTo(centre) < r2) h += 0.4f * Mathf.Clamp(_patch[i].Fuel * 1.6f, 0f, 1f);
		return h;
	}
}
