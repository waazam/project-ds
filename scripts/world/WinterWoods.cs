using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 22: the winter woods (the owner's "Real Act 22"). Out of the church's great door the player is back in a
/// forest like the whole first part of the game, but it is winter and softly snowing, and someone has plowed a
/// snaking road through the trees. The compass works again and points down it to the ski lodge, about 1.3 km
/// away. The further they go the colder it gets: the snowfall thins and stops, the ground glazes over, and the
/// trees are caked in ice and hung with icicles. All the way, something is keeping pace in the trees
/// (<see cref="Wendigo"/>, stalking from the director in WinterWoods.Stalking.cs). The lodge's front door is locked;
/// the back door is iced in (<see cref="SkiLodge"/>).
///
/// Built in the church's local space (a child of it, like <see cref="WinterGlade"/>, whose clearing it continues:
/// all the snow ground round the church is this one's). The ground is a coarse 4 m grid of chunks (each with its
/// own trimesh collision), and the road a finer ribbon laid along it (the grid ducks under the ribbon so the
/// road's ruts and windrows are exact). Styled PS2: small cold textures (Poly Haven's snow, tools/Textures/make_snow.py),
/// low-poly trees, and a dusk that never lets the snow glare.
/// </summary>
public partial class WinterWoods : Node3D
{
	public static WinterWoods Instance { get; private set; }
	public const float Cell = 4f, ChunkCells = 16;
	public SkiLodge Lodge { get; private set; }
	public Church Church { get; set; }
	public int Chunks { get; private set; }
	public int TreeCount { get; private set; }

	private ShaderMaterial _groundMat;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2222 };

	public override void _Ready()
	{
		Instance = this;
		EnsurePath();
		Lodge = new SkiLodge { Name = "SkiLodge", Position = SkiLodge.OriginLocal };
		AddChild(Lodge);
		_groundMat = GroundMat();
		BuildGround();
		BuildRibbon();
		BuildTrees();
		BuildProps();
		BuildWeather();
		BuildStalker();
		SetProcess(true);
		GD.Print($"[story] Act 22: the winter woods - {Length:0} m of plowed road to the lodge, {Chunks} ground chunks, {TreeCount} trees");
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (_silenced) ForestAmbienceManager.Instance?.ReleaseSilence(this);
	}

	// ------------------------------------------------------------------ materials

	public static ShaderMaterial GroundMat()
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/winter_ground.gdshader") };
		m.SetShaderParameter("tex_snow", GD.Load<Texture2D>("res://assets/textures/snow/snow_albedo.png"));
		m.SetShaderParameter("tex_plowed", GD.Load<Texture2D>("res://assets/textures/snow/plowed_albedo.png"));
		m.SetShaderParameter("tex_packed", GD.Load<Texture2D>("res://assets/textures/snow/packed_albedo.png"));
		m.SetShaderParameter("nor_snow", GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"));
		m.SetShaderParameter("nor_plowed", GD.Load<Texture2D>("res://assets/textures/snow/plowed_normal.png"));
		m.SetShaderParameter("detail_tex", DetailKit.Albedo(DetailKit.Kind.Snow));
		return m;
	}

	/// <summary>Vertex colour for the ground shader at a church-local point: R the plowed road, G shade, A frozen.</summary>
	private static Color GroundColor(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		float ice = FrozenAt(s);
		if (d > RegionHalf * 0.5f) ice *= 1f - Mathf.SmoothStep(RegionHalf * 0.5f, RegionHalf, d) * 0.5f;
		return new Color(PlowedWeight(x, z), 0f, 0f, ice);
	}

	// ------------------------------------------------------------------ the ground

	/// <summary>Whether the ground grid covers a church-local point: the church's clearing, or near the road, or round the lodge.</summary>
	private static bool Covered(float x, float z)
	{
		if (new Vector2(x - WinterGlade.Centre.X, z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius) return true;
		if (SkiLodge.NearLodge(x, z, 90f)) return true;
		return Nearest(x, z, out _, out _) < RegionHalf;
	}

	/// <summary>The grid's height: the ground, ducked under the road's ribbon (which draws the road itself).</summary>
	private static float GridHeight(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		// only alongside the road (the ribbon only covers that): past its ends, at the church's step and the
		// lodge's turning circle, the grid is the ground itself
		if (s <= 0.01f || s >= Length - 0.01f) return Height(x, z);
		float duck = 0.7f * (1f - Mathf.SmoothStep(BlendOut, BlendOut + 4f, d));
		return Height(x, z) - duck;
	}

	private void BuildGround()
	{
		// bounds of everything the grid might cover
		float minX = WinterGlade.Centre.X - WinterGlade.Radius, maxX = WinterGlade.Centre.X + WinterGlade.Radius;
		float minZ = WinterGlade.Centre.Y - WinterGlade.Radius, maxZ = WinterGlade.Centre.Y + WinterGlade.Radius;
		for (int i = 0; i < Points; i++)
		{
			var p = RoadPoint(i);
			minX = Mathf.Min(minX, p.X - RegionHalf); maxX = Mathf.Max(maxX, p.X + RegionHalf);
			minZ = Mathf.Min(minZ, p.Y - RegionHalf); maxZ = Mathf.Max(maxZ, p.Y + RegionHalf);
		}
		var lodge = SkiLodge.OriginLocal;
		minZ = Mathf.Min(minZ, lodge.Z - 100f);
		// aligned to the clearing's old 5 m grid? no: a fresh 4 m grid from here
		minX = Mathf.Floor(minX / Cell) * Cell; minZ = Mathf.Floor(minZ / Cell) * Cell;
		float chunk = Cell * ChunkCells;
		int nx = Mathf.CeilToInt((maxX - minX) / chunk), nz = Mathf.CeilToInt((maxZ - minZ) / chunk);
		for (int cx = 0; cx < nx; cx++)
			for (int cz = 0; cz < nz; cz++)
				BuildChunk(minX + cx * chunk, minZ + cz * chunk);
	}

	private void BuildChunk(float x0, float z0)
	{
		int n = (int)ChunkCells;
		// heights and colours at the grid's corners, once each
		var h = new float[n + 1, n + 1];
		var col = new Color[n + 1, n + 1];
		var inside = new bool[n + 1, n + 1];
		bool any = false;
		for (int i = 0; i <= n; i++)
			for (int j = 0; j <= n; j++)
			{
				float x = x0 + i * Cell, z = z0 + j * Cell;
				inside[i, j] = Covered(x, z);
				if (!inside[i, j]) continue;
				any = true;
				h[i, j] = GridHeight(x, z);
				col[i, j] = GroundColor(x, z);
			}
		if (!any) return;
		var k = new MeshKit();
		k.Mat(_groundMat);
		var faces = new List<Vector3>();
		for (int i = 0; i < n; i++)
			for (int j = 0; j < n; j++)
			{
				if (!inside[i, j] || !inside[i + 1, j] || !inside[i + 1, j + 1] || !inside[i, j + 1]) continue;
				float x = x0 + i * Cell, z = z0 + j * Cell;
				// under the church (its nave floor and its crypt) there is no snow
				if (WinterGlade.OutsideChurch(x, z) <= 0f && WinterGlade.OutsideChurch(x + Cell, z + Cell) <= 0f
					&& WinterGlade.OutsideChurch(x + Cell, z) <= 0f && WinterGlade.OutsideChurch(x, z + Cell) <= 0f) continue;
				Vector3 a = new(x, h[i, j], z), b = new(x + Cell, h[i + 1, j], z), c = new(x + Cell, h[i + 1, j + 1], z + Cell), d = new(x, h[i, j + 1], z + Cell);
				Vector3 na = GridNormal(x, z), nb = GridNormal(x + Cell, z), nc = GridNormal(x + Cell, z + Cell), nd = GridNormal(x, z + Cell);
				k.Color = col[i, j];
				// per-corner colours: two triangles, each corner its own
				TriC(k, a, b, c, na, nb, nc, col[i, j], col[i + 1, j], col[i + 1, j + 1]);
				TriC(k, a, c, d, na, nc, nd, col[i, j], col[i + 1, j + 1], col[i, j + 1]);
				faces.Add(a); faces.Add(b); faces.Add(c);
				faces.Add(a); faces.Add(c); faces.Add(d);
			}
		if (faces.Count == 0) return;
		var mi = k.CommitTo(this, $"Ground_{Chunks}", false);
		mi.VisibilityRangeEnd = 170f;   // (the fog is solid by 60 m: nothing further shows)
		mi.VisibilityRangeEndMargin = 10f;
		var body = new StaticBody3D { Name = $"GroundBody_{Chunks}", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces(faces.ToArray());
		body.AddChild(new CollisionShape3D { Shape = shape });
		AddChild(body);
		Chunks++;
	}

	private static Vector3 GridNormal(float x, float z)
	{
		const float e = 1.5f;
		float hx = GridHeight(x + e, z) - GridHeight(x - e, z), hz = GridHeight(x, z + e) - GridHeight(x, z - e);
		return new Vector3(-hx, 2f * e, -hz).Normalized();
	}

	private static void TriC(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc)
		=> k.TriC(a, b, c, na, nb, nc, new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(c.X, c.Z), ca, cb, cc);

	// ------------------------------------------------------------------ the road

	private static readonly float[] Across =
	{
		-RibbonHalf, -12.5f, -10.5f, -8.5f, -6.8f, -5.8f, -5.0f, -4.6f, -4.2f, -3.8f, -3.4f, -2.4f, -1.5f, -1.0f, -0.5f, 0f,
		0.5f, 1.0f, 1.5f, 2.4f, 3.4f, 3.8f, 4.2f, 4.6f, 5.0f, 5.8f, 6.8f, 8.5f, 10.5f, 12.5f, RibbonHalf,
	};

	/// <summary>The road itself: a ribbon along the centreline, every Step metres a cross-section out to the ribbon's
	/// edges (which dip under the grid), with its own collision.</summary>
	private void BuildRibbon()
	{
		int segs = 60;   // cross-sections per piece (90 m), so each piece is culled on its own
		for (int start = 0; start < Points - 1; start += segs)
		{
			int end = Mathf.Min(start + segs, Points - 1);
			var k = new MeshKit();
			k.Mat(_groundMat);
			var faces = new List<Vector3>();
			Vector3[] prev = null; Color[] prevC = null;
			for (int i = start; i <= end; i++)
			{
				var p = RoadPoint(i);
				var t = RoadDir(i);
				var right = new Vector2(-t.Y, t.X);   // (x, z): the right-hand side walking along the road
				var row = new Vector3[Across.Length];
				var rc = new Color[Across.Length];
				for (int a = 0; a < Across.Length; a++)
				{
					var q = p + right * Across[a];
					float y = Height(q.X, q.Y);
					if (Mathf.Abs(Across[a]) >= RibbonHalf - 0.01f) y -= 0.8f;   // the edge tucks under the grid
					row[a] = new Vector3(q.X, y, q.Y);
					rc[a] = GroundColor(q.X, q.Y);
				}
				if (prev != null)
					for (int a = 0; a < Across.Length - 1; a++)
					{
						Vector3 v0 = prev[a], v1 = prev[a + 1], v2 = row[a + 1], v3 = row[a];
						var n0 = RoadNormal(v0); var n1 = RoadNormal(v1); var n2 = RoadNormal(v2); var n3 = RoadNormal(v3);
						TriC(k, v0, v1, v2, n0, n1, n2, prevC[a], prevC[a + 1], rc[a + 1]);
						TriC(k, v0, v2, v3, n0, n2, n3, prevC[a], rc[a + 1], rc[a]);
						if (Mathf.Abs(Across[a]) < RibbonHalf - 1f || Mathf.Abs(Across[a + 1]) < RibbonHalf - 1f)
						{
							faces.Add(v0); faces.Add(v1); faces.Add(v2);
							faces.Add(v0); faces.Add(v2); faces.Add(v3);
						}
					}
				prev = row; prevC = rc;
			}
			var mi = k.CommitTo(this, $"Road_{start / segs}", false);
			mi.VisibilityRangeEnd = 170f;
			var body = new StaticBody3D { Name = $"RoadBody_{start / segs}", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "snow");
			var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
			shape.SetFaces(faces.ToArray());
			body.AddChild(new CollisionShape3D { Shape = shape });
			AddChild(body);
		}
	}

	private static Vector3 RoadNormal(Vector3 v)
	{
		const float e = 0.35f;
		float hx = Height(v.X + e, v.Z) - Height(v.X - e, v.Z), hz = Height(v.X, v.Z + e) - Height(v.X, v.Z - e);
		return new Vector3(-hx, 2f * e, -hz).Normalized();
	}

	private static float Flats(float x, float z, float h) => SkiLodge.Flatten(x, z, h);

	// ------------------------------------------------------------------ the weather, the light, the sound

	private GpuParticles3D _snowfall, _diamondDust;
	private AudioStreamPlayer _wind;
	private bool _silenced;
	private double _clock, _nextTinkle = 8;
	/// <summary>For tests: where the player is along the road (0 at the church, 1 at the lodge), and whether they're out in the woods.</summary>
	public float PlayerProgress { get; private set; }
	public bool PlayerOutside { get; private set; }
	public float SnowRatio => _snowfall?.AmountRatio ?? 0f;

	private void BuildWeather()
	{
		var flake = new QuadMesh { Size = new Vector2(0.045f, 0.045f) };
		flake.Material = new StandardMaterial3D
		{
			// lit (in the dark the flakes show in the lantern's light, faint past it; unshaded they were a static of white dots)
			AlbedoColor = new Color(0.82f, 0.85f, 0.92f, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true, Emission = new Color(0.2f, 0.2f, 0.23f),
		};
		// soft snow round the player (world space: the flakes don't follow them, only the cloud they fall from)
		_snowfall = new GpuParticles3D
		{
			Name = "Snowfall", Amount = 5000, Lifetime = 14f, Preprocess = 14f, LocalCoords = false, DrawPass1 = flake, Emitting = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(34f, 1f, 34f),
				Direction = new Vector3(0.1f, -1f, 0.04f), Spread = 10f, InitialVelocityMin = 0.9f, InitialVelocityMax = 1.5f,
				Gravity = new Vector3(0.06f, -0.2f, 0), TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.5f, TurbulenceNoiseScale = 5f,
				ScaleMin = 0.6f, ScaleMax = 1.3f,
			},
			VisibilityAabb = new Aabb(new Vector3(-40f, -30f, -40f), new Vector3(80f, 36f, 80f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_snowfall);
		// near the lodge the snow stops and the air freezes: a few ice crystals hang and drift, glinting faintly (slow, never a flicker)
		var glint = new QuadMesh { Size = new Vector2(0.025f, 0.025f) };
		glint.Material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.75f, 0.85f, 1f, 0.55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
		};
		_diamondDust = new GpuParticles3D
		{
			Name = "DiamondDust", Amount = 500, Lifetime = 18f, Preprocess = 18f, LocalCoords = false, DrawPass1 = glint, Emitting = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(20f, 5f, 20f),
				Direction = Vector3.Down, Spread = 80f, InitialVelocityMin = 0.02f, InitialVelocityMax = 0.12f, Gravity = new Vector3(0, -0.02f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.2f, ScaleMin = 0.5f, ScaleMax = 1.2f,
			},
			VisibilityAabb = new Aabb(new Vector3(-25f, -10f, -25f), new Vector3(50f, 20f, 50f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_diamondDust);
		_wind = new AudioStreamPlayer { Name = "WinterWind", Stream = GD.Load<AudioStream>("res://assets/audio/ambient/winter_wind_loop.wav"), Bus = "Weather", VolumeDb = -60f };
		if (_wind.Stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		AddChild(_wind);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += delta;
		var s = StoryManager.Instance;
		var player = StoryBeat.Player(this);
		var cam = GetViewport()?.GetCamera3D();
		bool act = s != null && s.Current >= Checkpoint.Act21Finished;
		bool outside = act && player != null && Church != null && !Church.Inside(player.GlobalPosition) && !(Lodge?.Inside(player.GlobalPosition) ?? false);
		if (outside && player != null)
		{
			var l = ToLocal(player.GlobalPosition);
			float d = Nearest(l.X, l.Z, out float sAlong, out _);
			outside = d < RegionHalf + 40f || new Vector2(l.X - WinterGlade.Centre.X, l.Z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius + 20f;
			if (outside) { PlayerProgress = Mathf.Clamp(sAlong / Length, 0f, 1f); KeepInBounds(player, l, d); }
		}
		PlayerOutside = outside;
		float frozen = FrozenAt(PlayerProgress * Length);
		// the light: dusk out here, colder and clearer toward the lodge
		var atmo = StoryBeat.Atmosphere(this);
		if (atmo != null)
		{
			atmo.WinterDusk = Mathf.MoveToward(atmo.WinterDusk, outside ? 1f : (act ? 0.35f : 0f), dt * 0.25f);
			atmo.Frost = Mathf.MoveToward(atmo.Frost, outside ? frozen : 0f, dt * 0.2f);
		}
		// the snow: soft round the church, thinning, gone by the lodge
		if (_snowfall != null)
		{
			// (never over the church: its roof keeps the snow off, and the clearing's own sheets fall round it)
			float offChurch = 0f;
			if (player != null) { var pl = ToLocal(player.GlobalPosition); offChurch = WinterGlade.OutsideChurch(pl.X, pl.Z); }
			bool snow = outside && frozen < 0.97f && offChurch > 40f;
			if (_snowfall.Emitting != snow) { _snowfall.Emitting = snow; if (snow) _snowfall.Restart(); }
			_snowfall.AmountRatio = Mathf.Clamp(1f - frozen * 1.05f, 0.02f, 1f);
			if (cam != null) _snowfall.GlobalPosition = cam.GlobalPosition + Vector3.Up * 14f;
		}
		if (_diamondDust != null)
		{
			bool dust = outside && frozen > 0.4f;
			if (_diamondDust.Emitting != dust) _diamondDust.Emitting = dust;
			_diamondDust.AmountRatio = Mathf.Clamp(frozen, 0.05f, 1f);
			if (cam != null) _diamondDust.GlobalPosition = cam.GlobalPosition;
		}
		// the sound: the summer forest has no place here: a thin wind that drops away to a frozen hush by the lodge
		var amb = ForestAmbienceManager.Instance;
		if (amb != null && outside != _silenced)
		{
			_silenced = outside;
			if (outside) amb.RequestSilence(this, 1f, 4); else amb.ReleaseSilence(this);
		}
		if (_wind != null)
		{
			if (outside && !_wind.Playing) _wind.Play();
			float want = outside ? Mathf.Lerp(-11f, -24f, frozen) : -60f;
			_wind.VolumeDb = Mathf.MoveToward(_wind.VolumeDb, want, dt * 8f);
			if (!outside && _wind.Playing && _wind.VolumeDb <= -59f) _wind.Stop();
		}
		if (outside && frozen > 0.35f && _clock >= _nextTinkle && player != null)
		{
			_nextTinkle = _clock + _rng.RandfRange(6f, 14f);
			var at = player.GlobalPosition + new Vector3(_rng.RandfRange(-14f, 14f), 4f, _rng.RandfRange(-14f, 14f));
			AudioDirector.OneShot(this, "ice_tinkle", 3, at, Mathf.Lerp(-22f, -14f, frozen), "Events", 5f, 0.08f);
		}
		UpdateStalker(dt, player, cam, outside);
	}

	/// <summary>The woods go on for ever into the dark, but the ground doesn't: past ~95 m from the road the
	/// snow gets deeper and deeper (a gentle push back, never a wall).</summary>
	private void KeepInBounds(PlayerController player, Vector3 local, float d)
	{
		if (SkiLodge.NearLodge(local.X, local.Z, 80f)) return;
		if (new Vector2(local.X - WinterGlade.Centre.X, local.Z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius - 15f) return;
		if (d < 95f) return;
		Nearest(local.X, local.Z, out float s, out _);
		var road = RoadAt(s, out _);
		var back = new Vector3(road.X - local.X, 0, road.Z - local.Z).Normalized();
		float push = Mathf.Clamp((d - 95f) / 10f, 0f, 1.5f);
		var g = ToGlobal(local + back * push * 0.06f) - ToGlobal(local);
		player.GlobalPosition += g;
	}
}
