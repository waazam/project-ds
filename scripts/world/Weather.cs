using Godot;

namespace ProjectDS.World;

/// <summary>
/// The weather (the weather pass, 2026-10-03: the owner, "the snow and other particle effects in the game need to be
/// drastically improved"; the flakes our own, baked from snowflake_generator.gdshader). One per level, made on demand
/// (<see cref="Get"/>): the wind every effect shares, and the snow, the blizzard and the rain.
///
/// - <b>The wind:</b> a direction and a speed (m/s), and its gusts, slow swells that roll through; published as the
///   shader globals `wind` and `wind_gust`, so the snow, the rain, the far curtain and the trees' sway all move with
///   the same air. <see cref="Wind"/> for code (the fires' smoke).
/// - <b>The snow,</b> in three layers by distance (weather_particles.gdshader: each in a box that wraps round the
///   camera, so it's equally thick wherever you are):
///   - near: a few hundred big flakes, the generator's crystals, tumbling and fluttering, faded out at
///     the eye;
///   - middle: tens of thousands of smaller ones out to fifteen metres;
///   - far: a moving curtain of them on rings round the camera (snow_curtain.gdshader), sinking into the fog.
///   What lands (the ground, a roof, a bough) starts again at the top: a heightfield collider rides with the camera,
///   so it never snows indoors, under a roof or under the firs.
/// - <b>The blizzard</b> (<see cref="Blizzard"/>): the snow at its thickest and driven sideways by a gale, the far
///   curtain a whiteout, and (through ForestAtmosphere.Blizzard) the fog closing the view in to a few dozen metres:
///   a mechanic for the story to use, like the fog, to take the player's sight away.
/// - <b>The rain</b> (<see cref="Rain"/>, RainVfx's): the same wrapping streaks, slanting with the wind, never under a
///   roof.
/// All of it slow and soft (the owner's rules): nothing streaks across the eye, nothing flickers, the snow's a cold
/// grey-blue lit by what light there is, never a white glare.
/// </summary>
public partial class Weather : Node3D
{
	public static Weather Instance { get; private set; }

	/// <summary>The level's weather (made under the current scene the first time it's asked for).</summary>
	public static Weather Get(Node from)
	{
		if (Instance != null && IsInstanceValid(Instance) && Instance.IsInsideTree()) return Instance;
		var root = from.GetTree().CurrentScene ?? from.GetTree().Root;
		var w = new Weather { Name = "Weather" };
		root.AddChild(w);
		return w;
	}

	/// <summary>The wind right now, gusts and all (m/s): for code (the fires' smoke drifts with it).</summary>
	public static Vector3 Wind { get; private set; } = new(0.8f, 0, 0.6f);
	public static float Gust { get; private set; }

	[ExportGroup("Wind")]
	/// <summary>The wind's direction and speed when nothing asks otherwise (m/s; 1 m/s is the woods' breeze).</summary>
	[Export] public Vector3 BaseWind = new(0.8f, 0, 0.6f);
	/// <summary>How hard it gusts (0 steady .. 1 wild).</summary>
	[Export] public float Gustiness = 0.35f;
	/// <summary>The blizzard's gale (m/s) and which way it blows (the story can turn it).</summary>
	[Export] public float GaleSpeed = 6.5f;
	[Export] public Vector3 GaleDirection = new(0.9f, 0, 0.42f);

	[ExportGroup("Snow")]
	[Export] public int NearFlakes = 1400;
	[Export] public int MidFlakes = 42000;
	[Export] public int RainDrops = 2600;

	/// <summary>0..1: how heavily it's snowing (eased toward <see cref="SetSnow"/>'s target).</summary>
	public float Snow { get; private set; }
	/// <summary>0..1: the blizzard.</summary>
	public float Blizzard { get; private set; }
	/// <summary>0..1: the rain (RainVfx sets it).</summary>
	public float Rain { get; set; }
	/// <summary>How far the player can see right now (metres, from the fog), for tests and the story.</summary>
	public float VisibilityMetres => (GetTree().GetFirstNodeInGroup("atmosphere") as ForestAtmosphere)?.VisibilityMetres ?? 999f;

	private float _snowTarget, _snowRate = 0.2f, _blizTarget, _blizRate = 0.1f;
	private Vector3 _windNow;
	private double _t;
	private GpuParticles3D _near, _mid, _rain;
	private ShaderMaterial _nearPm, _midPm, _rainPm, _nearDraw, _midDraw, _rainDraw;
	private MeshInstance3D[] _curtains;
	private ShaderMaterial[] _curtainMats;
	private GpuParticlesCollisionHeightField3D _shelter;
	private AudioStreamPlayer _gale;
	private Vector3 _lastCam;
	private bool _hasCam;
	private double _roofCheck;
	private Color _hazeTint = new(0.3f, 0.32f, 0.36f);
	private float _outdoors = 1f;
	/// <summary>True while a roof is over the camera (the far curtain is drawn only out in the open: a great hall's
	/// ring would otherwise stand inside the hall).</summary>
	public bool UnderRoof { get; private set; }

	public const uint CurtainLayer = 1u << 19;

	private readonly System.Collections.Generic.Dictionary<object, float> _snowWants = new();

	private static readonly System.Collections.Generic.List<(Node3D owner, Aabb box)> _shelters = new();

	/// <summary>An interior (a box in <paramref name="owner"/>'s space): no snow or rain shows inside it. The roof
	/// collider sees roofs from above; this is for the buildings whose roofs it can't (a vault faced inward).</summary>
	public static void RegisterShelter(Node3D owner, Aabb localBox)
	{
		_shelters.Add((owner, localBox));
		owner.TreeExiting += () => _shelters.RemoveAll(s => s.owner == owner);
	}

	/// <summary>Hands the nearest interiors (up to eight) to a layer's process shader.</summary>
	private static void Shelters(ShaderMaterial pm, Vector3 cam)
	{
		var inv = new Projection[8];
		var half = new Vector3[8];
		int n = 0;
		foreach (var (owner, box) in _shelters)
		{
			if (n >= 8 || !IsInstanceValid(owner) || !owner.IsInsideTree()) continue;
			var xf = owner.GlobalTransform * new Transform3D(Basis.Identity, box.GetCenter());
			// (only those within reach of the boxes round the camera)
			if (xf.Origin.DistanceTo(cam) > box.Size.Length() * 0.5f + 40f) continue;
			inv[n] = new Projection(xf.AffineInverse());
			half[n] = box.Size * 0.5f;
			n++;
		}
		pm.SetShaderParameter("shelter_count", n);
		var arr = new Godot.Collections.Array();
		foreach (var m in inv) arr.Add(m);
		pm.SetShaderParameter("shelter_inv", arr);
		pm.SetShaderParameter("shelter_half", half);
	}

	/// <summary>A place asking for snow (the church's glade, the road): the heaviest asked for falls. 0 withdraws.</summary>
	public void RequestSnow(object who, float amount, float seconds = 4f)
	{
		if (amount <= 0f) _snowWants.Remove(who); else _snowWants[who] = Mathf.Clamp(amount, 0f, 1f);
		float m = 0f;
		foreach (var v in _snowWants.Values) m = Mathf.Max(m, v);
		SetSnow(m, seconds);
	}

	/// <summary>Snow to <paramref name="amount"/> (0..1) over <paramref name="seconds"/>.</summary>
	public void SetSnow(float amount, float seconds = 4f)
	{
		_snowTarget = Mathf.Clamp(amount, 0f, 1f);
		_snowRate = seconds <= 0.01f ? 1000f : 1f / seconds;
	}

	/// <summary>The blizzard to <paramref name="amount"/> (0..1) over <paramref name="seconds"/>: it brings the snow with it.</summary>
	public void SetBlizzard(float amount, float seconds = 8f)
	{
		_blizTarget = Mathf.Clamp(amount, 0f, 1f);
		_blizRate = seconds <= 0.01f ? 1000f : 1f / seconds;
	}

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; Wind = new Vector3(0.8f, 0, 0.6f); Gust = 0f; Publish(Wind, 0f); }

	public override void _Ready()
	{
		TopLevel = true;
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--blizzard") >= 0) { _blizTarget = 1f; _blizRate = 1000f; }
	}

	// ------------------------------------------------------------------ building (on first need)

	private static Texture2D _atlas, _curtainTex;
	public static Texture2D Atlas => _atlas ??= GD.Load<Texture2D>("res://assets/textures/weather/snow_atlas.png");
	private static Texture2D CurtainTex => _curtainTex ??= GD.Load<Texture2D>("res://assets/textures/weather/snow_curtain.png");

	/// <summary>A layer of wrapping particles: its process (weather_particles) and its draw (weather_flake).</summary>
	private GpuParticles3D Layer(string name, int amount, Vector3 half, out ShaderMaterial pm, out ShaderMaterial draw)
		=> MakeLayer(this, name, amount, half, out pm, out draw);

	/// <summary>A layer of wrapping particles under <paramref name="parent"/> (the air's dust and spores use them too):
	/// keep it on the camera with <see cref="Follow"/>.</summary>
	public static GpuParticles3D MakeLayer(Node parent, string name, int amount, Vector3 half, out ShaderMaterial pm, out ShaderMaterial draw)
	{
		pm = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/weather_particles.gdshader") };
		pm.SetShaderParameter("half_extent", half);
		draw = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/weather_flake.gdshader") };
		draw.SetShaderParameter("atlas", Atlas);
		draw.SetShaderParameter("box_half", half);
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = 600.0, Explosiveness = 1f, LocalCoords = false, FixedFps = 0, Interpolate = false,
			ProcessMaterial = pm, DrawPass1 = new QuadMesh { Size = Vector2.One, Material = draw },
			VisibilityAabb = new Aabb(-half - Vector3.One * 2f, half * 2f + Vector3.One * 4f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CollisionBaseSize = 0.01f, Emitting = false, Visible = false,
		};
		parent.AddChild(p);
		return p;
	}

	/// <summary>A layer on (or off), its box on the camera.</summary>
	public static void Follow(GpuParticles3D p, ShaderMaterial pm, Vector3 cam, bool on)
	{
		Show(p, on);
		if (on) Place(p, pm, cam);
	}

	private void BuildSnow()
	{
		if (_near != null) return;
		_near = Layer("SnowNear", NearFlakes, new Vector3(4.5f, 3.5f, 4.5f), out _nearPm, out _nearDraw);
		// the crystals (0-7) and the wet clumps (12-13): big, tumbling
		_nearPm.SetShaderParameter("size_range", new Vector2(0.025f, 0.05f));
		_nearPm.SetShaderParameter("frames", new Vector2(0f, 8f));
		_nearPm.SetShaderParameter("fall", new Vector2(0.6f, 1.1f));
		_nearPm.SetShaderParameter("flutter", 0.35f);
		_nearPm.SetShaderParameter("spin", 1.6f);
		_nearDraw.SetShaderParameter("near_fade", 0.5f);
		_nearDraw.SetShaderParameter("glow", 0.1f);
		_nearDraw.SetShaderParameter("opacity", 0.95f);
		_mid = Layer("SnowMid", MidFlakes, new Vector3(15f, 8f, 15f), out _midPm, out _midDraw);
		// the defocused crystals, the clumps and the specks (8-15): smaller, many
		_midPm.SetShaderParameter("size_range", new Vector2(0.055f, 0.11f));
		_midPm.SetShaderParameter("frames", new Vector2(8f, 8f));
		_midPm.SetShaderParameter("fall", new Vector2(0.8f, 1.4f));
		_midPm.SetShaderParameter("flutter", 0.22f);
		_midPm.SetShaderParameter("spin", 0.8f);
		_midDraw.SetShaderParameter("near_fade", 0.8f);
		_midDraw.SetShaderParameter("opacity", 0.9f);
		// (a little light of their own: at dusk there is little else to show them by, and the snow must read)
		_midDraw.SetShaderParameter("glow", 0.16f);
		_midDraw.SetShaderParameter("edge_band", 0.35f);
		// the far curtain: two rings
		_curtains = new MeshInstance3D[2];
		_curtainMats = new ShaderMaterial[2];
		float[] radii = { 15f, 24f };
		for (int i = 0; i < 2; i++)
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/snow_curtain.gdshader") };
			m.SetShaderParameter("curtain", CurtainTex);
			m.SetShaderParameter("radius", radii[i]);
			m.SetShaderParameter("tile", i == 0 ? 6f : 9f);
			m.SetShaderParameter("glow", 0.18f);
			_curtainMats[i] = m;
			_curtains[i] = new MeshInstance3D
			{
				Name = $"Curtain{i}", Mesh = new CylinderMesh { TopRadius = radii[i], BottomRadius = radii[i], Height = 30f, RadialSegments = 48, Rings = 1, CapTop = false, CapBottom = false },
				MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = CurtainLayer, Visible = false,
				ExtraCullMargin = 40f,
			};
			AddChild(_curtains[i]);
		}
		BuildShelter();
	}

	private void BuildRain()
	{
		if (_rain != null) return;
		_rain = Layer("RainStreaks", RainDrops, new Vector3(11f, 7f, 11f), out _rainPm, out _rainDraw);
		_rainPm.SetShaderParameter("size_range", new Vector2(0.022f, 0.032f));
		_rainPm.SetShaderParameter("frames", new Vector2(14f, 1f));
		_rainPm.SetShaderParameter("fall", new Vector2(13f, 16f));
		_rainPm.SetShaderParameter("wind_carry", 1.4f);
		_rainPm.SetShaderParameter("flutter", 0f);
		_rainPm.SetShaderParameter("spin", 0f);
		_rainPm.SetShaderParameter("gust_swirl", 0.3f);
		// drawn out along their fall: a streak, faint (the owner: the rain is seen through, never a curtain)
		_rainDraw.SetShaderParameter("stretch", 0.038f);
		_rainDraw.SetShaderParameter("opacity", 0.24f);
		_rainDraw.SetShaderParameter("near_fade", 0.6f);
		_rainDraw.SetShaderParameter("tint", new Color(0.62f, 0.66f, 0.72f));
		_rainDraw.SetShaderParameter("glow", 0.12f);
		BuildShelter();
	}

	/// <summary>What the snow and the rain land on (and so never fall under): the ground, roofs, boughs, seen from above
	/// in a heightfield that rides with the camera.</summary>
	private void BuildShelter()
	{
		if (_shelter != null) return;
		_shelter = new GpuParticlesCollisionHeightField3D
		{
			Name = "Shelter", Size = new Vector3(36f, 120f, 36f), FollowCameraEnabled = true,   // (the church's nave vaults to ~38 m: its roof must be inside)
			Resolution = GpuParticlesCollisionHeightField3D.ResolutionEnum.Resolution512,
			UpdateMode = GpuParticlesCollisionHeightField3D.UpdateModeEnum.WhenMoved,
			HeightfieldMask = ~CurtainLayer,
		};
		AddChild(_shelter);
	}

	// ------------------------------------------------------------------ every frame

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += delta;
		Snow = Mathf.MoveToward(Snow, Mathf.Max(_snowTarget, _blizTarget > 0.01f ? 0.85f : 0f), dt * _snowRate);
		Blizzard = Mathf.MoveToward(Blizzard, _blizTarget, dt * _blizRate);
		var cam = GetViewport()?.GetCamera3D();

		// the wind: the breeze, or the gale in a blizzard; gusts rolling through (two slow swells, never a jerk)
		float gust = Mathf.Clamp(0.5f + 0.5f * (Mathf.Sin((float)_t * 0.23f) * 0.6f + Mathf.Sin((float)_t * 0.61f + 1.3f) * 0.4f), 0f, 1f);
		gust = Mathf.Pow(gust, 2.2f) * Mathf.Lerp(Gustiness, 0.8f, Blizzard);
		Vector3 want = BaseWind.Lerp(GaleDirection.Normalized() * GaleSpeed, Mathf.SmoothStep(0f, 1f, Blizzard));
		_windNow = _windNow.LengthSquared() < 1e-6f ? want : _windNow.Lerp(want, 1f - Mathf.Exp(-0.8f * dt));
		Wind = _windNow * (1f + gust * 0.6f);
		Gust = gust;
		Publish(_windNow, gust);

		if (cam == null) return;
		Vector3 cp = cam.GlobalPosition;
		if (!_hasCam || cp.DistanceTo(_lastCam) > 30f) _hasCam = true;   // (a jump: the boxes just follow)
		_lastCam = cp;

		// under a roof? (a ray straight up: the far curtain is for the open air)
		if ((_roofCheck -= delta) <= 0)
		{
			_roofCheck = 0.25;
			var q = PhysicsRayQueryParameters3D.Create(cp + Vector3.Up * 0.3f, cp + Vector3.Up * 40f, 1);
			UnderRoof = GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0;
		}
		_outdoors = Mathf.MoveToward(_outdoors, UnderRoof ? 0f : 1f, dt * 2f);
		// the snow
		if (Snow > 0.001f || Blizzard > 0.001f) BuildSnow();
		if (_near != null)
		{
			bool on = Snow > 0.001f;
			Show(_near, on); Show(_mid, on);
			foreach (var c in _curtains) c.Visible = on && _outdoors > 0.01f;
			if (on)
			{
				float b = Blizzard;
				Place(_near, _nearPm, cp);
				Place(_mid, _midPm, cp);
				_nearPm.SetShaderParameter("density", Mathf.Clamp(Snow * 0.9f + b * 0.3f, 0f, 1f));
				_midPm.SetShaderParameter("density", Mathf.Clamp(0.15f + Snow * 0.65f + b * 0.4f, 0f, 1f) * Mathf.Min(1f, Snow * 4f));
				// in the gale the flakes streak a little (never long: nothing whips across the eye)
				_nearDraw.SetShaderParameter("stretch", 0.035f * b);
				_midDraw.SetShaderParameter("stretch", 0.02f * b);
				_nearPm.SetShaderParameter("flutter", Mathf.Lerp(0.35f, 0.6f, b));
				for (int i = 0; i < _curtains.Length; i++)
				{
					_curtains[i].GlobalPosition = new Vector3(cp.X, cp.Y, cp.Z);
					_curtainMats[i].SetShaderParameter("eye", cp);
					_curtainMats[i].SetShaderParameter("strength", Mathf.Clamp((0.35f + 0.45f * Snow) * Mathf.Min(1f, Snow * 3f) + 0.7f * b, 0f, 1.2f) * (i == 0 ? 1f : 0.8f) * _outdoors);
					_curtainMats[i].SetShaderParameter("fall", Mathf.Lerp(1.0f, 1.6f, b));
				}
			}
		}
		// the flakes' own glow follows the haze (a little over it): they read against it whatever the light
		var env = GetWorld3D()?.Environment ?? GetViewport()?.World3D?.Environment;
		if (env != null && _near != null)
		{
			Color haze = env.FogEnabled ? env.FogLightColor : env.AmbientLightColor * env.AmbientLightEnergy;
			_hazeTint = _hazeTint.Lerp(haze, 1f - Mathf.Exp(-2f * dt));
			foreach (var m in new[] { _nearDraw, _midDraw }) { m.SetShaderParameter("glow_tint", _hazeTint); m.SetShaderParameter("haze_lift", 1.25f); }
			foreach (var m in _curtainMats) { m.SetShaderParameter("glow_tint", _hazeTint); m.SetShaderParameter("haze_lift", 1.15f); }
		}
		// the rain
		if (Rain > 0.001f) BuildRain();
		if (_rain != null)
		{
			Show(_rain, Rain > 0.001f);
			if (Rain > 0.001f)
			{
				Place(_rain, _rainPm, cp);
				_rainPm.SetShaderParameter("density", Rain);
			}
		}
		// the blizzard: the fog closing in (the atmosphere's), the gale's roar
		if (GetTree().GetFirstNodeInGroup("atmosphere") is ForestAtmosphere atmo) atmo.Blizzard = Blizzard;
		UpdateGale(dt);
	}

	private static void Show(GpuParticles3D p, bool on)
	{
		if (p.Visible != on) p.Visible = on;
		if (p.Emitting != on) { p.Emitting = on; if (on) p.Restart(); }
	}

	/// <summary>A layer's box onto the camera (the node too, so its culling box goes with it).</summary>
	private static void Place(GpuParticles3D p, ShaderMaterial pm, Vector3 cp)
	{
		p.GlobalPosition = cp;
		pm.SetShaderParameter("centre", cp);
		Shelters(pm, cp);
	}

	private static void Publish(Vector3 wind, float gust)
	{
		RenderingServer.GlobalShaderParameterSet("wind", wind);
		RenderingServer.GlobalShaderParameterSet("wind_gust", gust);
	}

	private void UpdateGale(float dt)
	{
		if (Blizzard < 0.005f && _gale == null) return;
		if (_gale == null)
		{
			const string path = "res://assets/audio/ambient/storm_wind_loop.wav";
			if (!ResourceLoader.Exists(path)) return;
			_gale = new AudioStreamPlayer { Name = "Gale", Stream = GD.Load<AudioStream>(path), Bus = "Weather", VolumeDb = -60f };
			if (_gale.Stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			AddChild(_gale);
		}
		float want = Blizzard > 0.005f ? Mathf.Lerp(-30f, -7f, Mathf.Sqrt(Blizzard)) + Gust * 3f : -60f;
		if (want > -59f && !_gale.Playing) _gale.Play();
		_gale.VolumeDb = Mathf.MoveToward(_gale.VolumeDb, want, dt * 12f);
		if (_gale.Playing && _gale.VolumeDb <= -59f && Blizzard < 0.005f) _gale.Stop();
	}

	/// <summary>The flakes' material for the small emitters (snow blowing in at a broken window, a drift's spray): the
	/// atlas, its frames picked per particle by the process material's anim offset.</summary>
	public static StandardMaterial3D FlakeMaterial(float alpha = 0.8f)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = new Color(0.78f, 0.81f, 0.87f, alpha), AlbedoTexture = Atlas, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			ParticlesAnimHFrames = 4, ParticlesAnimVFrames = 4, ParticlesAnimLoop = false, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true, Emission = new Color(0.08f, 0.08f, 0.09f),
		};
	}

	/// <summary>Picks frames <paramref name="first"/>..+<paramref name="count"/> of the atlas at random per particle (and
	/// a random spin) on a process material drawn with <see cref="FlakeMaterial"/>.</summary>
	public static void FlakeFrames(ParticleProcessMaterial pm, int first = 0, int count = 16)
	{
		pm.AnimOffsetMin = first / 16f;
		pm.AnimOffsetMax = (first + count - 0.01f) / 16f;
		pm.AnimSpeedMin = pm.AnimSpeedMax = 0f;
		pm.AngleMin = -180f;
		pm.AngleMax = 180f;
	}
}
