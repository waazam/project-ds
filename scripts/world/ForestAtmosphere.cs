using Godot;

namespace ProjectDS.World;

/// <summary>
/// Slowly thickens and darkens the fog and dims the light as the camera goes
/// deeper into the woods (by distance north of the trailhead). Owns only the
/// WorldEnvironment/sun it is pointed at; no post effects. From Act 6 onward,
/// <see cref="SetMood"/> can override this distance-based blend entirely with
/// a scripted lighting transition (dawn, the menacing deep-forest tone, night).
///
/// On top of whichever base is active, every frame it layers:
/// - a fog-coloured ambient floor, so night, the storm and the menacing tone
///   stay dark and scary but never crush the trail and tree silhouettes to pure black;
/// - moonlight at night (the sun becomes a faint cold key light, so shapes keep a lit side);
/// - <see cref="Storm"/> (overcast: thicker, greyer fog, weaker sun) and
///   <see cref="Wetness"/> (set by <see cref="RainVfx"/>);
/// - <see cref="Flash"/>: a lightning flash's sky and ambient boost (RainVfx drives it).
///
/// The Act 1 day look also carries an "open trail" grade: at the parking lot, the
/// trailhead and the first stretch of the trail it is a bright, clear, warm late
/// afternoon (fog almost gone, trees clear 100 m off, a strong warm sun with real
/// shading, a light warm sky, more ambient light); it fades out along the trail and
/// is gone by the footbridge, from where the usual look stands. It is a grade on
/// the auto blend only, and only in Act 1: never during a storm, a scripted mood or
/// once the first stairs have been climbed.
/// </summary>
[GlobalClass]
public partial class ForestAtmosphere : Node
{
	public enum Mood { Auto, Dawn, Menacing, Night }

	/// <summary>For the autotest and other callers: which mood is currently active (or blending toward).</summary>
	public Mood CurrentMood => _mood;
	/// <summary>For tests: the environment's distance fog density right now.</summary>
	public float FogDensity => _env?.FogDensity ?? 0f;
	/// <summary>For tests: the height fog band's density (negative = thickening above fog_height; 0 = none).</summary>
	public float HeightFogDensity => _env?.FogHeightDensity ?? 0f;
	[Export] public NodePath EnvironmentPath = "../WorldEnvironment";
	[Export] public NodePath SunPath = "../Sun";
	/// <summary>World Z where "deep woods" begins / is fully reached (north is -Z).</summary>
	[Export] public float DeepStartZ = -150f;
	[Export] public float DeepFullZ = -250f;
	[Export] public float FogDensityOpen = 0.024f;
	[Export] public float FogDensityDeep = 0.038f;
	[Export] public Color FogColorOpen = new(0.31f, 0.32f, 0.345f);
	[Export] public Color FogColorDeep = new(0.25f, 0.26f, 0.29f);
	/// <summary>How much the fog hides the sky (and its distant ridges); deep woods swallow them.</summary>
	[Export] public float SkyFogOpen = 0.0f;
	[Export] public float SkyFogDeep = 0.75f;
	[Export] public float AmbientOpen = 0.85f;
	[Export] public float AmbientDeep = 0.7f;
	[Export] public float SunOpen = 0.75f;
	[Export] public float SunDeep = 0.5f;
	/// <summary>Sky (background) energy scale in the deep woods: the sky dims as the trees close in, so
	/// it never sits as a bright cut-out behind dark trunks (Dan, 2026-09-22).</summary>
	[Export] public float SkyEnergyDeep = 0.62f;
	/// <summary>An area that opens up a little again (the stairs' gap).</summary>
	[Export] public Vector3 ClearingCenter = new(0, 0, -302);
	[Export] public float ClearingRadius = 26f;
	/// <summary>How much of the deep-woods effect the clearing takes back (0 = none).</summary>
	[Export] public float ClearingRelief = 0.5f;

	[ExportGroup("Open trail (Act 1 day)")]
	[Export] public bool OpenTrailGrade = true;
	/// <summary>Fully open until this far along the trail; fades out from here to the footbridge.</summary>
	[Export] public float OpenHoldMeters = 55f;
	/// <summary>Where the grade is gone, in metres along the trail (the "bridge_marker" node overrides it).</summary>
	[Export] public float OpenFadeEndMeters = 180f;
	[Export] public float OpenSunScale = 3.2f;
	[Export] public Color OpenSunColor = new(1f, 0.82f, 0.55f);
	[Export] public float OpenFogDensityScale = 0.04f;
	/// <summary>A warm, pale haze: at this density it only softens the far trees.</summary>
	[Export] public Color OpenFogColor = new(0.6f, 0.6f, 0.58f);
	[Export] public float OpenAmbientScale = 2.1f;
	/// <summary>Ambient light colour when fully open (warmer than the scene's grey-blue).</summary>
	[Export] public Color OpenAmbientColor = new(0.55f, 0.53f, 0.5f);
	/// <summary>Extra sky (background) energy when fully open.</summary>
	[Export] public float OpenSkyBoost = 0.12f;
	/// <summary>Warm the ambient light and the sky with the grade (off reproduces the old, subtler grade for comparisons).</summary>
	[Export] public bool OpenWarmAmbientAndSky = true;
	/// <summary>The sun stands this much higher when fully open, so its light reaches the ground between the trees.</summary>
	[Export] public float OpenSunRaiseDegrees = 14f;
	/// <summary>Added to the tonemap exposure when fully open.</summary>
	[Export] public float OpenExposureBoost = 0.1f;
	/// <summary>Seconds for the grade to cover ~63% of a change (so a teleport never snaps it).</summary>
	[Export] public float OpenSmoothing = 2f;
	/// <summary>0..1 how much of the open-trail grade is applied right now (for tests and the HUD).</summary>
	public float OpenAmount => _open;

	[ExportGroup("Act 1 fog (the walk to the fallen tree)")]
	/// <summary>The owner's Act 1 fog: hardly there at the trailhead, closing in the further they walk,
	/// until at the fallen tree they can see only about twenty feet (a see-through bubble round them) and
	/// past it the fog is a wall. Depth fog: clear up to the begin distance, fully thick by the end one.</summary>
	[Export] public bool Act1Fog = true;
	[Export] public float Act1FarBegin = 70f, Act1FarEnd = 650f;
	// (the bubble at the trail's end widened and the fog darkened, 2026-10-03: the trees coming down there 15-22 m off were
	// lost in a pale grey wall; now they read against a darker one)
	[Export] public float Act1NearBegin = 6f, Act1NearEnd = 32f;
	[Export] public Color Act1FogColor = new(0.3f, 0.31f, 0.33f);
	/// <summary>0..1 how far the Act 1 fog has closed in (0 at the trailhead, 1 at the fallen tree); -1 when it isn't Act 1.</summary>
	public float Act1FogAmount => _act1On ? _act1 : -1f;
	/// <summary>The trailer holds the Act 1 fog where it wants it (null: the fog follows the camera along the trail).</summary>
	public float? Act1FogOverride { get; set; }
	private float _act1, _act1Target;
	private bool _act1On, _act1Applied;
	/// <summary>Past the fallen fir the fog keeps thickening the nearer the staircase (the owner, 2026-10-04: "more dense the
	/// closer you get to the staircase, it needs like a 35% increase"): denser by this share at the stairs' foot, from
	/// <see cref="StairsFogFrom"/> metres out (the way's end, <see cref="FriendTrail"/>).</summary>
	[Export] public float StairsFogExtra = 0.35f, StairsFogFrom = 60f, StairsFogFull = 6f;
	private float _stairs, _stairsTarget;
	private FriendTrail _way;
	private bool _waySearched;
	/// <summary>0..1 how near the staircase the Act 1 fog has thickened (tests).</summary>
	public float Act1StairsFog => _act1On ? _stairs : 0f;
	private Environment.FogModeEnum _levelFogMode;
	private float _levelDepthBegin, _levelDepthEnd, _levelDepthCurve;

	// Scripted mood targets (Act 6 onward). Fog colour/density, sky-fog, ambient and sun energy
	// all cross-fade from whatever the auto system last set toward these over SetMood's duration.
	[Export] public Color FogColorDawn = new(0.46f, 0.36f, 0.31f);
	[Export] public float FogDensityDawn = 0.003f;   // the far shore's treeline reads green-grey through the haze, not a white wall (the owner, 2026-09-27)
	[Export] public float AmbientDawn = 1.0f;
	[Export] public float SunEnergyDawn = 0.95f;
	[Export] public Color SunColorDawn = new(1f, 0.72f, 0.5f);

	[Export] public Color FogColorMenacing = new(0.13f, 0.085f, 0.1f);
	[Export] public float FogDensityMenacing = 0.05f;
	[Export] public float AmbientMenacing = 0.32f;
	[Export] public float SunEnergyMenacing = 0.22f;

	/// <summary>Night fog is a deep blue-grey, not black: trees read as dark shapes against it.</summary>
	[Export] public Color FogColorNight = new(0.06f, 0.07f, 0.1f);
	[Export] public float FogDensityNight = 0.032f;
	[Export] public float AmbientNight = 0.2f;
	[Export] public float SunEnergyNight = 0.1f;
	/// <summary>At night the sun becomes a weak, cold moon.</summary>
	[Export] public Color MoonColor = new(0.55f, 0.64f, 0.9f);

	/// <summary>
	/// The ambient floor: the ambient light never drops below this luminance (ambient colour x energy),
	/// and is tinted toward the current fog colour, so dark moods stay readable. 0 disables it.
	/// </summary>
	[Export] public float AmbientFloorLuminance = 0.28f;
	/// <summary>How much of the ambient light's hue comes from the fog colour (0 = keep the scene's ambient colour).</summary>
	[Export] public float AmbientFogTint = 0.55f;

	/// <summary>0..1 overcast storm weight (RainVfx sets this from its intensity).</summary>
	public float Storm { get; set; }
	/// <summary>0..1 how wet everything is (RainVfx raises it with the rain; it dries slowly afterwards).</summary>
	public float Wetness { get; set; }
	/// <summary>0..~1.5 current lightning flash (RainVfx drives this for a fraction of a second).</summary>
	public float Flash { get; set; }
	/// <summary>0..1: the fog light goes dark red (Act 11's blood rain). Blended in after the mood each frame.</summary>
	public float BloodTint { get; set; }
	/// <summary>0..1: below ground (Act 14's stairwell). Sky, sun and ambient go and the fog turns black.</summary>
	public float Underground { get; set; }
	/// <summary>Act 21 on: back on the surface, and it is winter (0..1): a pale, cold overcast, a thin
	/// blue-grey haze, cool flat ambient light and a weak white sun, whatever the mood was.</summary>
	public float Winter { get; set; }
	/// <summary>Act 22's woods (0..1): the winter light going to dusk under a low snow sky: a dark blue-grey murk
	/// that closes in at a hundred metres, little ambient, a weak sun (the owner: isolated and creepy; and a
	/// field of snow must never glare).</summary>
	public float WinterDusk { get; set; }
	/// <summary>Act 22, nearing the ski lodge (0..1): the snow stops and the air freezes still: the murk thins a
	/// little and goes a colder, clearer blue.</summary>
	public float Frost { get; set; }
	/// <summary>Inside the ski lodge (0..1, Act 23): noon behind the snow, every lamp lit: a clean warm interior, only
	/// a faint haze, a warm ambient, no sun through the roof.</summary>
	public float Lodge { get; set; }
	/// <summary>Act 23, the lodge frozen over (0..1): the warm haze gone cold and blue-grey, the ambient chilled and lower.</summary>
	public float LodgeCold { get; set; }
	[Export] public Color LodgeColdFogColor = new(0.12f, 0.14f, 0.18f);
	[Export] public float LodgeColdFogDensity = 0.011f;
	[Export] public Color LodgeColdAmbientColor = new(0.55f, 0.62f, 0.76f);
	[Export] public float LodgeColdAmbient = 0.36f;
	[Export] public Color LodgeFogColor = new(0.1f, 0.08f, 0.065f);
	[Export] public float LodgeFogDensity = 0.004f;
	[Export] public Color LodgeAmbientColor = new(0.78f, 0.68f, 0.56f);
	[Export] public float LodgeAmbient = 0.42f;
	// (2026-09-30, the owner: dark, the lantern needed, seen only so far round it; and past that not black but a
	// whitish haze. Before first light: little ambient, the sun a faint glow, and a pale depth fog that lets the near
	// dark alone and closes in white-grey at forty-odd metres, the trees going into it as silhouettes; held a step
	// under white so it never glares)
	[Export] public Color DuskFogColor = new(0.35f, 0.35f, 0.4f);
	[Export] public float DuskFogDensity = 0.02f;
	[Export] public Color DuskAmbientColor = new(0.4f, 0.47f, 0.64f);
	[Export] public float DuskAmbient = 0.2f;
	[Export] public float DuskSunScale = 0.1f;
	[Export] public float DuskFogBegin = 4.5f;
	[Export] public float DuskFogEnd = 46f;
	[Export] public float DuskFogCurve = 0.9f;
	[Export] public Color FrostFogColor = new(0.33f, 0.36f, 0.43f);
	[Export] public float FrostFogDensity = 0.015f;
	[Export] public float FrostFogEnd = 58f;
	/// <summary>Inside the church (0..1): a dark, thin, smoky haze and little ambient light, so the candles,
	/// the stained glass and the lantern do the lighting (the owner: the white glare hurt; more gothic).</summary>
	public float Interior { get; set; }
	[Export] public Color InteriorFogColor = new(0.03f, 0.026f, 0.026f);
	[Export] public float InteriorFogDensity = 0.014f;
	[Export] public float InteriorAmbientScale = 0.22f;
	// the winter's early morning (the owner, 2026-09-28: the trees were white and flat against the sky; it wants
	// contrast, and pink): a lilac haze a step darker than the horizon, a cool violet ambient kept low so the
	// trees keep their shading, and a low rose-gold sun that tints the snow
	[Export] public Color WinterFogColor = new(0.4f, 0.32f, 0.42f);
	[Export] public float WinterFogDensity = 0.011f;
	[Export] public Color WinterAmbientColor = new(0.6f, 0.5f, 0.68f);
	[Export] public float WinterAmbient = 0.72f;
	[Export] public float WinterSun = 0.78f;
	[Export] public Color WinterSunColor = new(1f, 0.7f, 0.64f);
	/// <summary>The black fog's density fully underground: a couple of turns of the stair and it's gone.</summary>
	[Export] public float UndergroundFogDensity = 0.085f;
	/// <summary>The fog's colour fully underground (black in the stairwell; Act 15's hallway tints it).</summary>
	[Export] public Color UndergroundFogColor = new(0.004f, 0.004f, 0.005f);

	private Environment _env;
	private DirectionalLight3D _sun;
	private Tween _heightFogTween;

	private float _open;
	private float _openTarget;
	private float _openSampleTimer;
	private ForestTerrain _terrain;
	private Node3D _spawn;
	private bool _trailSearched;
	private float _openFadeEnd = -1f;

	private Mood _mood = Mood.Auto;
	private float _moodBlend;
	private float _moodBlendSpeed = 1f;
	private Color _fromFog, _fromSunColor;
	private float _fromDensity, _fromSkyFog, _fromAmbient, _fromSunEnergy;
	private Color _sunBaseColor;
	private Color _ambientBaseColor;
	private float _bgEnergyBase = 1f;
	private float _exposureBase = 1f;
	private Basis _sunBaseBasis;
	private float _sunRaiseApplied = -1f;
	private ShaderMaterial _skyMat;
	private readonly System.Collections.Generic.Dictionary<string, Vector3> _skyBase = new();
	private float _skyOpenApplied = -1f;

	/// <summary>The sky shader's colours for the open-trail grade: a clear, light, warm late afternoon.</summary>
	private static readonly (string name, Vector3 open)[] OpenSky =
	{
		// Toned down a step (2026-09-22): the sky was a bright cut-out behind the dark trees.
		("zenith_color", new Vector3(0.33f, 0.45f, 0.64f)),
		("horizon_color", new Vector3(0.62f, 0.64f, 0.63f)),
		("glow_color", new Vector3(0.92f, 0.76f, 0.52f)),
		("cloud_dark", new Vector3(0.5f, 0.5f, 0.54f)),
		("cloud_light", new Vector3(0.84f, 0.8f, 0.74f)),
		("ridge_far", new Vector3(0.52f, 0.56f, 0.62f)),
		("ridge_mid", new Vector3(0.42f, 0.46f, 0.51f)),
		("ridge_near", new Vector3(0.32f, 0.36f, 0.39f)),
		("haze_color", new Vector3(0.68f, 0.65f, 0.58f)),
		("below_color", new Vector3(0.58f, 0.55f, 0.48f)),
	};
	private float _skyGlowBase = 0.55f, _skyCloudBase = 0.55f;
	private float _skyWinterApplied = -1f;

	/// <summary>The winter's sky (Acts 21-22, the owner's reference: early morning, pink): a deep violet-blue
	/// zenith over a rose-pink horizon, a salmon glow where the sun's coming up, pink-lit cloud and lilac ridges.
	/// Held a step under the references' brightness so the snow never glares.</summary>
	private static readonly (string name, Vector3 dawn)[] WinterDawnSky =
	{
		("zenith_color", new Vector3(0.2f, 0.19f, 0.36f)),
		("horizon_color", new Vector3(0.8f, 0.52f, 0.62f)),
		("glow_color", new Vector3(0.98f, 0.6f, 0.56f)),
		("cloud_dark", new Vector3(0.42f, 0.33f, 0.48f)),
		("cloud_light", new Vector3(0.86f, 0.6f, 0.68f)),
		("ridge_far", new Vector3(0.5f, 0.4f, 0.55f)),
		("ridge_mid", new Vector3(0.37f, 0.3f, 0.44f)),
		("ridge_near", new Vector3(0.25f, 0.21f, 0.32f)),
		("haze_color", new Vector3(0.7f, 0.52f, 0.62f)),
		("below_color", new Vector3(0.5f, 0.4f, 0.5f)),
	};
	/// <summary>The winter dawn's sun: low (degrees above the horizon), a little east of north, rose-gold.</summary>
	[Export] public float WinterSunElevation = 13f;
	[Export] public float WinterSunAzimuth = 28f;
	private Basis _winterSunBasis;
	private bool _winterSunOn;

	// The last "base" values (before the per-frame layers), so SetMood blends from what the mood system
	// had, not from a value that already includes a lightning flash or the ambient floor.
	private Color _baseFog, _baseSunColor;
	private float _baseDensity, _baseSkyFog, _baseAmbient, _baseSunEnergy;
	private float _baseSkyEnergy = 1f, _fromSkyEnergy = 1f;

	// The light shafts (see LightShafts): owned here, driven by the mood every frame.
	private LightShafts _shafts;
	private Color _shaftTint = new(1f, 0.88f, 0.66f);
	private float _shaftIntensity = 0.16f, _shaftStrength;
	/// <summary>For tests: the shafts node this atmosphere drives.</summary>
	public LightShafts Shafts => _shafts;
	/// <summary>Scales the light shafts on top of the mood (1 normally). Act 12's lake sets 0: its
	/// sunrise sun is so low the shafts lie almost flat and smear across the sky as grey slabs.</summary>
	public float ShaftScale { get; set; } = 1f;
	/// <summary>The environment and sun this atmosphere drives (for a local override like the lake's sunrise).</summary>
	public Environment Env => _env;
	public DirectionalLight3D Sun => _sun;
	/// <summary>Sky (background) energy scale by mood: dim overcast at night, a little less in the menace.</summary>
	[Export] public float SkyEnergyNight = 0.3f;
	[Export] public float SkyEnergyMenacing = 0.45f;
	[Export] public float SkyEnergyDawn = 0.8f;

	public override void _Ready()
	{
		AddToGroup("atmosphere");
		_env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
		_sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
		if (_sun != null) { _sunBaseColor = _sun.LightColor; _sunBaseBasis = _sun.GlobalBasis; }
		if (_env != null)
		{
			// the visual pass: contact shadows (screen-space ambient occlusion) in every corner, under every root,
			// step and plank, so things sit in the world instead of floating on it
			_env.SsaoEnabled = true;
			_env.SsaoRadius = 1.2f;
			_env.SsaoIntensity = 2.2f;
			_env.SsaoPower = 1.6f;
			_env.SsaoDetail = 0.6f;
			_env.SsaoHorizon = 0.06f;
			_env.SsaoSharpness = 0.98f;
			_env.SsaoLightAffect = 0.15f;
			// light in the fog (ApplyVolumetric sets its density by place): lit only by lights, never by the sky or
			// the ambient (that would grey the whole view), reaching 48 m
			_env.VolumetricFogEmission = Colors.Black;
			_env.VolumetricFogAnisotropy = 0.25f;
			_env.VolumetricFogLength = 48f;
			_env.VolumetricFogDetailSpread = 2f;
			_env.VolumetricFogGIInject = 0f;
			_env.VolumetricFogAmbientInject = 0f;
			_env.VolumetricFogSkyAffect = 0f;
			_ambientBaseColor = _env.AmbientLightColor;
			_bgEnergyBase = _env.BackgroundEnergyMultiplier;
			_exposureBase = _env.TonemapExposure;
			_skyMat = _env.Sky?.SkyMaterial as ShaderMaterial;
			if (_skyMat?.Shader != null)
			{
				foreach (var (name, _) in OpenSky) _skyBase[name] = SkyParam(name).AsVector3();
				_skyGlowBase = SkyParam("glow_strength").AsSingle();
				_skyCloudBase = SkyParam("cloud_cover").AsSingle();
			}
			_baseFog = _env.FogLightColor;
			_baseDensity = _env.FogDensity;
			_baseSkyFog = _env.FogSkyAffect;
			_baseAmbient = _env.AmbientLightEnergy;
		}
		_baseSunEnergy = _sun?.LightEnergy ?? 0f;
		_baseSunColor = _sun?.LightColor ?? Colors.White;
		if (!Engine.IsEditorHint() && _sun != null)
		{
			_shafts = new LightShafts { Name = "LightShafts", Strength = 0f };
			AddChild(_shafts);
		}
	}

	/// <summary>A sky uniform as set on the material, else the shader's default (colours as Vector3).</summary>
	private Variant SkyParam(string name)
	{
		var v = _skyMat.GetShaderParameter(name);
		if (v.VariantType == Variant.Type.Nil) v = RenderingServer.ShaderGetParameterDefault(_skyMat.Shader.GetRid(), name);
		return v.VariantType == Variant.Type.Color ? (Variant)ToV3(v.AsColor()) : v;
	}

	private static Vector3 ToV3(Color c) => new(c.R, c.G, c.B);

	/// <summary>The sky follows the grade in steps (every change rebuilds its radiance map).</summary>
	private void ApplySky()
	{
		if (_skyMat == null || _skyBase.Count == 0) return;
		float q = OpenWarmAmbientAndSky ? Mathf.Snapped(_open, 0.05f) : 0f;
		// the winter's dawn, in steps too
		float w = Mathf.Snapped(Mathf.Clamp(Mathf.Max(Winter, WinterDusk), 0f, 1f), 0.1f);
		if (Mathf.IsEqualApprox(q, _skyOpenApplied) && Mathf.IsEqualApprox(w, _skyWinterApplied)) return;
		_skyOpenApplied = q;
		_skyWinterApplied = w;
		foreach (var (name, open) in OpenSky)
		{
			if (!_skyBase.TryGetValue(name, out var b)) continue;
			var v = b.Lerp(open, q);
			foreach (var (dn, dawn) in WinterDawnSky) if (dn == name) v = v.Lerp(dawn, w);
			_skyMat.SetShaderParameter(name, v);
		}
		_skyMat.SetShaderParameter("glow_strength", Mathf.Lerp(Mathf.Lerp(_skyGlowBase, 0.95f, q), 1.2f, w));
		_skyMat.SetShaderParameter("cloud_cover", Mathf.Lerp(Mathf.Lerp(_skyCloudBase, 0.3f, q), 0.34f, w));
	}

	/// <summary>Cross-fades from the current lighting to a fixed mood over <paramref name="seconds"/>, then holds it (no more auto distance blend).</summary>
	public void SetMood(Mood mood, float seconds = 6f)
	{
		if (_env == null) return;
		_fromFog = _baseFog;
		_fromDensity = _baseDensity;
		_fromSkyFog = _baseSkyFog;
		_fromSkyEnergy = _baseSkyEnergy;
		_fromAmbient = _baseAmbient;
		_fromSunEnergy = _baseSunEnergy;
		_fromSunColor = _baseSunColor;
		_mood = mood;
		_moodBlend = 0f;
		_moodBlendSpeed = 1f / Mathf.Max(seconds, 0.05f);
	}

	/// <summary>
	/// A vertical fog band that has nothing to do with the distance-based mood blend above (this
	/// touches only fog_height/fog_height_density, which the per-frame blend never sets): cross-fades
	/// toward thickening above <paramref name="height"/>, at <paramref name="density"/>, so anything
	/// tall enough gets visually swallowed the higher up it goes. Used for Act 11's stairs, which need
	/// to look like they vanish into the canopy rather than simply being a very tall, fully visible model.
	/// </summary>
	public void SetHeightFog(float height, float density, float seconds)
	{
		if (_env == null) return;
		_heightFogTween?.Kill();
		_heightFogTween = CreateTween().SetParallel();
		_heightFogTween.TweenProperty(_env, "fog_height", height, seconds);
		_heightFogTween.TweenProperty(_env, "fog_height_density", -Mathf.Abs(density), seconds);
	}

	public void ClearHeightFog(float seconds)
	{
		if (_env == null) return;
		_heightFogTween?.Kill();
		_heightFogTween = CreateTween().SetParallel();
		_heightFogTween.TweenProperty(_env, "fog_height_density", 0f, seconds);
	}

	public override void _Process(double delta)
	{
		if (_env == null) return;
		UpdateOpen((float)delta);
		if (_mood != Mood.Auto) ProcessMoodBlend(delta);
		else ProcessAuto();
		ApplyLayers();
		ApplySky();
	}

	/// <summary>Smoothly tracks how "open" the trail is where the camera stands: 1 at the trailhead,
	/// 0 from the footbridge on, and 0 whenever a storm or a scripted mood is on.</summary>
	private void UpdateOpen(float dt)
	{
		_openSampleTimer -= dt;
		if (_openSampleTimer <= 0f)
		{
			_openSampleTimer = 0.2f;   // the trail lookup needn't run every frame
			var cam = GetViewport().GetCamera3D();
			_openTarget = cam == null ? 0f : OpenTarget(cam.GlobalPosition);
			_act1On = cam != null && Act1FogActive();
			if (_act1On)
			{
				float along = TrailAlong(cam.GlobalPosition);
				float end = _terrain != null && IsInstanceValid(_terrain) ? _terrain.TrailLength : 480f;
				_act1Target = Mathf.SmoothStep(OpenHoldMeters * 0.5f, Mathf.Max(end - 15f, OpenHoldMeters + 20f), along);
				if (Act1FogOverride is float f) _act1Target = f;
				if (!_waySearched) { _waySearched = true; _way = GetTree().CurrentScene?.FindChild("FriendTrail", true, false) as FriendTrail; }
				_stairsTarget = 0f;
				if (_way != null && IsInstanceValid(_way) && _way.Length > 1f && Act1FogOverride == null)
				{
					var foot = _way.At(_way.Length, out _);
					float d = new Vector2(cam.GlobalPosition.X, cam.GlobalPosition.Z).DistanceTo(foot);
					_stairsTarget = 1f - Mathf.SmoothStep(StairsFogFull, StairsFogFrom, d);
				}
			}
		}
		_open = Mathf.Lerp(_open, _openTarget, 1f - Mathf.Exp(-dt / Mathf.Max(OpenSmoothing, 0.01f)));
		_act1 = Mathf.Lerp(_act1, _act1Target, 1f - Mathf.Exp(-dt / 1.5f));
		_stairs = Mathf.Lerp(_stairs, _stairsTarget, 1f - Mathf.Exp(-dt / 1.5f));
	}

	/// <summary>Act 1's walk in (before the first climb), in the woods' own mood, no storm, not underground.</summary>
	private bool Act1FogActive()
	{
		if (Act1FogOverride != null && Systems.StoryManager.Instance is { Current: >= Systems.Checkpoint.Act24Finished }) return true;   // (Act 25's fog)
		if (!Act1Fog || _mood != Mood.Auto || Storm > 0.01f || Underground > 0.01f) return false;
		if (Systems.StoryManager.Instance is not { } story) return false;
		return story.Current < Systems.Checkpoint.Act2StairsClimbed;
	}

	private float OpenTarget(Vector3 p)
	{
		if (!OpenTrailGrade || _mood != Mood.Auto) return 0f;
		// Act 1 only: the grade belongs to the walk in, not to anything after the first climb.
		if (Systems.StoryManager.Instance is { } story && story.Current >= Systems.Checkpoint.Act2StairsClimbed) return 0f;
		float along = TrailAlong(p);
		float end = _openFadeEnd > 0f ? _openFadeEnd : OpenFadeEndMeters;
		float open = 1f - Mathf.SmoothStep(OpenHoldMeters, Mathf.Max(end, OpenHoldMeters + 1f), along);
		return open * (1f - Mathf.Clamp(Storm, 0f, 1f));
	}

	/// <summary>Metres along the trail of the point nearest <paramref name="p"/> (the terrain's trail
	/// helpers; without a terrain, distance north of the spawn). The bridge sets the fade's end.</summary>
	private float TrailAlong(Vector3 p)
	{
		if (!_trailSearched)
		{
			_trailSearched = true;
			_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
			_spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
			if (GetTree().GetFirstNodeInGroup("bridge_marker") is Node3D bridge)
			{
				if (_terrain != null) { _terrain.TrailDistance(bridge.GlobalPosition.X, bridge.GlobalPosition.Z, out _openFadeEnd); }
				else if (_spawn != null) _openFadeEnd = _spawn.GlobalPosition.Z - bridge.GlobalPosition.Z;
			}
		}
		if (_terrain != null && IsInstanceValid(_terrain))
		{
			_terrain.TrailDistance(p.X, p.Z, out float along);
			return along;
		}
		float z0 = _spawn != null && IsInstanceValid(_spawn) ? _spawn.GlobalPosition.Z : 0f;
		return Mathf.Max(0f, z0 - p.Z);
	}

	private void ProcessAuto()
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		Vector3 p = cam.GlobalPosition;
		float deep = Mathf.Clamp((DeepStartZ - p.Z) / (DeepStartZ - DeepFullZ), 0, 1);
		deep = deep * deep * (3 - 2 * deep);
		float inClearing = 1f - Mathf.Clamp((new Vector2(p.X - ClearingCenter.X, p.Z - ClearingCenter.Z).Length() - ClearingRadius * 0.5f) / (ClearingRadius * 0.5f), 0, 1);
		float t = deep * (1f - ClearingRelief * inClearing);
		_baseDensity = Mathf.Lerp(FogDensityOpen, FogDensityDeep, t) * (1f - 0.35f * ClearingRelief * inClearing);
		_baseFog = FogColorOpen.Lerp(FogColorDeep, t);
		_baseSkyFog = Mathf.Lerp(SkyFogOpen, SkyFogDeep, t);
		_baseAmbient = Mathf.Lerp(AmbientOpen, AmbientDeep, t);
		_baseSunEnergy = Mathf.Lerp(SunOpen, SunDeep, t);
		_baseSkyEnergy = Mathf.Lerp(1f, SkyEnergyDeep, t);
		_baseSunColor = _sunBaseColor;

		// The open-trail grade: sunnier, warmer, thinner and lighter fog near the start of the walk.
		float open = _open;
		if (open <= 0.001f) return;
		_baseDensity *= Mathf.Lerp(1f, OpenFogDensityScale, open);
		_baseFog = _baseFog.Lerp(OpenFogColor, open);
		_baseSkyFog *= 1f - open;
		_baseAmbient *= Mathf.Lerp(1f, OpenAmbientScale, open);
		_baseSunEnergy *= Mathf.Lerp(1f, OpenSunScale, open);
		_baseSunColor = _sunBaseColor.Lerp(OpenSunColor, open);
	}

	private void ProcessMoodBlend(double delta)
	{
		_moodBlend = Mathf.Min(1f, _moodBlend + (float)delta * _moodBlendSpeed);
		float u = Mathf.SmoothStep(0f, 1f, _moodBlend);
		// Sky-fog: at night and in the menace the fog swallows most of the sky, so it reads as a dim
		// overcast only just lighter than the trees, never a bright cut-out behind them.
		(Color fog, float density, float skyFog, float ambient, float sunEnergy, Color sunColor, float skyEnergy) target = _mood switch
		{
			Mood.Dawn => (FogColorDawn, FogDensityDawn, 0.1f, AmbientDawn, SunEnergyDawn, SunColorDawn, SkyEnergyDawn),
			Mood.Menacing => (FogColorMenacing, FogDensityMenacing, 0.78f, AmbientMenacing, SunEnergyMenacing, _sunBaseColor, SkyEnergyMenacing),
			Mood.Night => (FogColorNight, FogDensityNight, 0.72f, AmbientNight, SunEnergyNight, MoonColor, SkyEnergyNight),
			_ => (_fromFog, _fromDensity, _fromSkyFog, _fromAmbient, _fromSunEnergy, _fromSunColor, _fromSkyEnergy),
		};
		_baseFog = _fromFog.Lerp(target.fog, u);
		_baseDensity = Mathf.Lerp(_fromDensity, target.density, u);
		_baseSkyFog = Mathf.Lerp(_fromSkyFog, target.skyFog, u);
		_baseSkyEnergy = Mathf.Lerp(_fromSkyEnergy, target.skyEnergy, u);
		_baseAmbient = Mathf.Lerp(_fromAmbient, target.ambient, u);
		_baseSunEnergy = Mathf.Lerp(_fromSunEnergy, target.sunEnergy, u);
		_baseSunColor = _fromSunColor.Lerp(target.sunColor, u);
	}

	private static float Lum(Color c) => c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;

	/// <summary>
	/// The light shafts follow the mood: warm and strong on the open Act 1 trail, about half in the
	/// deep woods, a faint cold moon at night, a red-tinged trace in the menace; gone indoors and in
	/// heavy rain. Colour and level ease toward their targets so a mood change never pops them.
	/// </summary>
	private void DriveShafts(float storm)
	{
		if (_shafts == null || !IsInstanceValid(_shafts)) return;
		(Color tint, float intensity, float strength) target = _mood switch
		{
			Mood.Night => (new Color(0.5f, 0.62f, 0.9f), 0.02f, 0.3f),
			Mood.Menacing => (new Color(0.75f, 0.45f, 0.5f), 0.018f, 0.2f),
			Mood.Dawn => (new Color(1f, 0.78f, 0.55f), 0.12f, 0.6f),
			_ => (new Color(1f, 0.88f, 0.66f), 0.16f, Mathf.Lerp(0.55f, 1f, _open)),
		};
		bool indoors = Audio.ForestAmbienceManager.Instance is { IsIndoor: true };
		float strength = indoors || _winterSunOn ? 0f : target.strength * (1f - 0.85f * storm) * ShaftScale;   // (the dawn sun lies too low for shafts)
		float k = 1f - Mathf.Exp(-(float)GetProcessDeltaTime() * 1.2f);
		_shaftTint = _shaftTint.Lerp(target.tint, k);
		_shaftIntensity = Mathf.Lerp(_shaftIntensity, target.intensity, k);
		_shaftStrength = ShaftScale <= 0f || _winterSunOn ? 0f : Mathf.Lerp(_shaftStrength, strength, k);
		_shafts.Tint = _shaftTint;
		_shafts.Intensity = _shaftIntensity;
		_shafts.Strength = _shaftStrength;
		_shafts.LightDir = -_sun.GlobalBasis.Z;
	}

	[ExportGroup("Blizzard")]
	/// <summary>0..1, Weather's: the blizzard's snow-fog over whatever the place's fog is.</summary>
	public float Blizzard { get; set; }
	/// <summary>How far the eye reaches in a full blizzard (metres): the fog's end.</summary>
	[Export] public float BlizzardSightMetres = 24f;
	/// <summary>The snow-fog's colour: a cold mid grey (a whiteout, but never a white glare).</summary>
	[Export] public Color BlizzardFogColor = new(0.33f, 0.35f, 0.39f);
	[Export] public float BlizzardAmbient = 0.45f;
	/// <summary>How far the eye reaches now (metres, from the fog): tests and the story.</summary>
	public float VisibilityMetres { get; private set; } = 999f;

	[ExportGroup("Light in the fog")]
	/// <summary>Volumetric fog's density by place (the fidelity pass, 2026-10-02): thin, so only what's lit shows in it
	/// (the lantern's beam, the lamps' haloes, the sun's shafts between the trunks). Kept low: never a glare.</summary>
	[Export] public float VolumetricOpen = 0.011f;
	[Export] public float VolumetricUnderground = 0.022f;
	[Export] public float VolumetricInterior = 0.014f;
	[Export] public float VolumetricWinter = 0.009f;
	/// <summary>How much the sun lights the fog (the rest of its light stays on the ground).</summary>
	[Export] public float SunInFog = 0.22f;

	/// <summary>The volumetric fog, by place, its colour from the scene's fog (a pale grey of its hue: it only shows where
	/// light passes through it).</summary>
	private void ApplyVolumetric(Color fog, float under, float inside, float lodge, float winter, float storm)
	{
		bool on = ProjectDS.Systems.GameSettings.Instance?.FogLighting ?? true;
		if (_env.VolumetricFogEnabled != on) _env.VolumetricFogEnabled = on;
		if (!on) return;
		float d = Mathf.Lerp(VolumetricOpen, VolumetricWinter, winter);
		d = Mathf.Lerp(d, VolumetricInterior, Mathf.Max(inside, lodge));
		d = Mathf.Lerp(d, VolumetricUnderground, under);
		_env.VolumetricFogDensity = d * (1f + 0.4f * storm) + 0.02f * Mathf.Clamp(Blizzard, 0f, 1f);
		Color hue = Lum(fog) > 0.001f ? fog * (0.6f / Mathf.Max(Lum(fog), 0.001f)) : new Color(0.6f, 0.6f, 0.6f);
		_env.VolumetricFogAlbedo = new Color(0.6f, 0.6f, 0.6f).Lerp(hue.Clamp(), 0.35f);
		if (_sun != null) _sun.LightVolumetricFogEnergy = SunInFog;
		// the banks drifting through it: outdoors only, thicker as Act 1's fog closes in and by the stairs, thinner in the
		// winter woods' colder, drier air
		if (DriftingFog)
		{
			if (_banks == null) { _banks = new FogBanks { Name = "FogBanks" }; AddChild(_banks); }
			float act1 = _act1On ? Mathf.Clamp(_act1, 0f, 1f) * (1f + 0.35f * _stairs) : 0f;
			float s = (1f - under) * (1f - inside) * (1f - lodge) * (1f - 0.4f * winter) * (0.7f + 0.6f * act1);
			_banks.Follow(GetViewport().GetCamera3D(), s);
			_banks.Tint(_env.VolumetricFogAlbedo);
		}
	}

	/// <summary>Fog banks drifting through the volumetric fog outdoors (FogBanks).</summary>
	[Export] public bool DriftingFog = true;
	private FogBanks _banks;
	/// <summary>For tests: the fog banks drifting here now.</summary>
	public bool FogBanksOn => _banks != null && _banks.Visible;

	/// <summary>Storm, wetness, ambient floor and lightning, layered over the base every frame.</summary>
	private void ApplyLayers()
	{
		float storm = Mathf.Clamp(Storm, 0f, 1f);
		float flash = Mathf.Max(0f, Flash);

		Color fog = _baseFog.Lerp(new Color(0.2f, 0.215f, 0.24f) * Mathf.Min(1f, Lum(_baseFog) / 0.2f + 0.3f), storm * 0.35f);
		float density = _baseDensity * (1f + 0.22f * storm);
		float sunEnergy = _baseSunEnergy * (1f - 0.45f * storm);

		// Ambient floor: tint toward the fog's hue (at the ambient colour's brightness), then lift the
		// energy until colour x energy reaches the floor luminance. Dark moods keep their colour and
		// most of their gloom, but the ground and trunks never go fully black.
		Color fogHue = Lum(fog) > 0.001f ? fog * (Lum(_ambientBaseColor) / Lum(fog)) : _ambientBaseColor;
		Color ambColor = _ambientBaseColor.Lerp(fogHue, AmbientFogTint).Lerp(OpenAmbientColor, OpenWarmAmbientAndSky ? _open : 0f);
		float ambient = _baseAmbient;
		float lum = Lum(ambColor);
		if (AmbientFloorLuminance > 0f && lum > 0.001f)
			ambient = Mathf.Max(ambient, AmbientFloorLuminance / lum);

		// Lightning: the whole sky and the fog light up for an instant (the directional light is RainVfx's).
		ambient += flash * 1.4f;
		fog += new Color(0.35f, 0.37f, 0.42f) * flash * 0.6f;

		if (BloodTint > 0f) fog = fog.Lerp(new Color(0.30f, 0.04f, 0.03f), Mathf.Clamp(BloodTint, 0f, 1f));
		// Underground (Act 14's stairwell): no sky, no sun, next to no ambient, and a black fog that
		// swallows everything a few metres past the lantern.
		float winter = Mathf.Clamp(Winter, 0f, 1f);
		Color sunColor = _baseSunColor;
		if (winter > 0f)
		{
			fog = fog.Lerp(WinterFogColor, winter);
			density = Mathf.Lerp(density, WinterFogDensity, winter);
			ambColor = ambColor.Lerp(WinterAmbientColor, winter);
			ambient = Mathf.Lerp(ambient, WinterAmbient, winter);
			sunEnergy = Mathf.Lerp(sunEnergy, WinterSun, winter);
			sunColor = sunColor.Lerp(WinterSunColor, winter);
		}
		float dusk = Mathf.Clamp(WinterDusk, 0f, 1f);
		if (dusk > 0f)
		{
			float frost = Mathf.Clamp(Frost, 0f, 1f);
			fog = fog.Lerp(DuskFogColor.Lerp(FrostFogColor, frost), dusk);
			density = Mathf.Lerp(density, Mathf.Lerp(DuskFogDensity, FrostFogDensity, frost), dusk);
			ambColor = ambColor.Lerp(DuskAmbientColor, dusk);
			ambient = Mathf.Lerp(ambient, DuskAmbient, dusk);
			sunEnergy *= Mathf.Lerp(1f, DuskSunScale, dusk);
		}
		float lodge = Mathf.Clamp(Lodge, 0f, 1f);
		if (lodge > 0f)
		{
			float cold = Mathf.Clamp(LodgeCold, 0f, 1f);
			fog = fog.Lerp(LodgeFogColor.Lerp(LodgeColdFogColor, cold), lodge);
			density = Mathf.Lerp(density, Mathf.Lerp(LodgeFogDensity, LodgeColdFogDensity, cold), lodge);
			ambColor = ambColor.Lerp(LodgeAmbientColor.Lerp(LodgeColdAmbientColor, cold), lodge);
			ambient = Mathf.Lerp(ambient, Mathf.Lerp(LodgeAmbient, LodgeColdAmbient, cold), lodge);
			sunEnergy *= 1f - lodge;
		}
		float inside = Mathf.Clamp(Interior, 0f, 1f);
		if (inside > 0f)
		{
			fog = fog.Lerp(InteriorFogColor, inside);
			density = Mathf.Lerp(density, InteriorFogDensity, inside);
			ambient *= Mathf.Lerp(1f, InteriorAmbientScale, inside);
			sunEnergy *= 1f - 0.75f * inside;
		}
		float under = Mathf.Clamp(Underground, 0f, 1f);
		if (under > 0f)
		{
			fog = fog.Lerp(UndergroundFogColor, under);
			density = Mathf.Lerp(density, UndergroundFogDensity, under);
			ambient *= 1f - 0.97f * under;
			sunEnergy *= 1f - under;
		}
		_env.FogLightColor = fog;
		_env.FogDensity = density;
		// Act 22's dusk: the fog swallows the sky's warm glow too (a low snow sky, not a sunset)
		// (the woods' morning: the fog lies over the sky's horizon but leaves its pink showing above)
		_env.FogSkyAffect = Mathf.Lerp(_baseSkyFog, 0.45f, Mathf.Clamp(Mathf.Max(Winter, WinterDusk), 0f, 1f));
		float woods = dusk * (1f - lodge) * (1f - inside) * (1f - under);
		if (_act1On || woods > 0.01f)
		{
			// Act 1: depth fog closing in round them as they walk toward the fallen tree (the level's
			// own fog settings are kept, and put back the moment Act 1's fog is over); and Act 22's woods
			if (!_act1Applied)
			{
				_act1Applied = true;
				_levelFogMode = _env.FogMode;
				_levelDepthBegin = _env.FogDepthBegin; _levelDepthEnd = _env.FogDepthEnd; _levelDepthCurve = _env.FogDepthCurve;
			}
			_env.FogMode = Environment.FogModeEnum.Depth;
			if (_act1On)
			{
				float f = Mathf.Clamp(_act1, 0f, 1f);
				float e = f * f * (3f - 2f * f);
				// (nearing the stairs: the same fog, its distances drawn in by the extra density)
				float k = 1f + StairsFogExtra * Mathf.Clamp(_stairs, 0f, 1f) * e;
				_env.FogDepthBegin = Mathf.Lerp(Act1FarBegin, Act1NearBegin, e) / k;
				_env.FogDepthEnd = Mathf.Lerp(Act1FarEnd, Act1NearEnd, Mathf.Sqrt(e)) / k;
				_env.FogDepthCurve = Mathf.Lerp(1.4f, 0.75f, e);
				_env.FogDensity = Mathf.Lerp(0.35f, 1f, e);
				_env.FogLightColor = fog.Lerp(Act1FogColor, e);
				_env.FogSkyAffect = Mathf.Lerp(_baseSkyFog, 1f, e);
			}
			else
			{
				// the woods: clear dark close in, the pale haze from forty-odd metres (a little further, colder, near the lodge)
				float e = woods * woods * (3f - 2f * woods);
				float frost = Mathf.Clamp(Frost, 0f, 1f);
				_env.FogDepthBegin = Mathf.Lerp(120f, DuskFogBegin, e);
				_env.FogDepthEnd = Mathf.Lerp(400f, Mathf.Lerp(DuskFogEnd, FrostFogEnd, frost), e);
				_env.FogDepthCurve = DuskFogCurve;
				_env.FogDensity = Mathf.Lerp(0.2f, 1f, e);
				_env.FogSkyAffect = Mathf.Lerp(_baseSkyFog, 1f, e);
			}
		}
		else if (_act1Applied)
		{
			_act1Applied = false;
			_env.FogMode = _levelFogMode;
			_env.FogDepthBegin = _levelDepthBegin; _env.FogDepthEnd = _levelDepthEnd; _env.FogDepthCurve = _levelDepthCurve;
		}
		// the blizzard (Weather's): the snow-fog closes the view in, whatever the place's own fog was
		float bliz = Mathf.Clamp(Blizzard, 0f, 1f) * (1f - under) * (1f - inside);
		if (bliz > 0.001f)
		{
			float e = bliz * bliz * (3f - 2f * bliz);
			fog = fog.Lerp(BlizzardFogColor, e);
			ambColor = ambColor.Lerp(BlizzardFogColor * (Lum(ambColor) / Mathf.Max(Lum(BlizzardFogColor), 0.001f)), e * 0.6f);
			ambient = Mathf.Lerp(ambient, Mathf.Max(ambient, BlizzardAmbient), e);
			if (_env.FogMode == Environment.FogModeEnum.Depth)
			{
				_env.FogDepthBegin = Mathf.Lerp(_env.FogDepthBegin, 0.5f, e);
				_env.FogDepthEnd = Mathf.Lerp(_env.FogDepthEnd, BlizzardSightMetres, e);
				_env.FogDepthCurve = Mathf.Lerp(_env.FogDepthCurve, 0.7f, e);
				_env.FogDensity = Mathf.Lerp(_env.FogDensity, 1f, e);
				_env.FogLightColor = _env.FogLightColor.Lerp(BlizzardFogColor, e);
			}
			else
			{
				// exponential: about 3/density metres to all but gone
				density = Mathf.Lerp(density, 3f / BlizzardSightMetres, e);
				_env.FogDensity = density;
				_env.FogLightColor = fog;
			}
			_env.FogSkyAffect = Mathf.Lerp(_env.FogSkyAffect, 1f, e);
			sunEnergy *= 1f - 0.6f * e;
		}
		VisibilityMetres = _env.FogMode == Environment.FogModeEnum.Depth ? _env.FogDepthEnd : 3f / Mathf.Max(_env.FogDensity, 0.0001f);
		ApplyVolumetric(fog, under, inside, lodge, Mathf.Max(winter, dusk), storm);
		_env.AmbientLightColor = ambColor;
		_env.AmbientLightEnergy = ambient;
		_env.TonemapExposure = _exposureBase + OpenExposureBoost * _open;
		_env.BackgroundEnergyMultiplier = (_bgEnergyBase * Mathf.Lerp(_baseSkyEnergy, 0.75f, winter) * (1f + OpenSkyBoost * _open) * (1f - 0.35f * storm) + flash * 2.5f) * (1f - under);
		if (_sun == null) return;
		_sun.LightEnergy = sunEnergy;
		_sun.LightColor = sunColor;
		DriveShafts(storm);
		// the winter's dawn: the sun low over the trees, where the sky's pink glow is
		bool winterSun = Mathf.Max(Winter, WinterDusk) > 0.5f && Underground < 0.5f;
		if (winterSun != _winterSunOn)
		{
			_winterSunOn = winterSun;
			if (winterSun)
			{
				float az = Mathf.DegToRad(WinterSunAzimuth), el = Mathf.DegToRad(WinterSunElevation);
				Vector3 toSun = new(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
				_winterSunBasis = Basis.LookingAt(-toSun, Vector3.Up);
				_sun.GlobalBasis = _winterSunBasis;
			}
			else _sunRaiseApplied = -999f;   // put the grade's sun back below
			_skyWinterApplied = -1f;
		}
		if (winterSun) return;
		// Raise the sun with the grade (about its own horizontal axis), in small steps.
		float raise = Mathf.Snapped(_open * (OpenWarmAmbientAndSky ? OpenSunRaiseDegrees : 0f), 0.25f);
		if (!Mathf.IsEqualApprox(raise, _sunRaiseApplied))
		{
			_sunRaiseApplied = raise;
			Vector3 axis = _sunBaseBasis.X.Normalized();
			_sun.GlobalBasis = new Basis(axis, Mathf.DegToRad(-raise)) * _sunBaseBasis;
		}
	}
}
