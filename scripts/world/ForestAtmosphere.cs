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

	// Scripted mood targets (Act 6 onward). Fog colour/density, sky-fog, ambient and sun energy
	// all cross-fade from whatever the auto system last set toward these over SetMood's duration.
	[Export] public Color FogColorDawn = new(0.55f, 0.4f, 0.32f);
	[Export] public float FogDensityDawn = 0.014f;
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
	/// <summary>The black fog's density fully underground: a couple of turns of the stair and it's gone.</summary>
	[Export] public float UndergroundFogDensity = 0.085f;

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
		if (Mathf.IsEqualApprox(q, _skyOpenApplied)) return;
		_skyOpenApplied = q;
		foreach (var (name, open) in OpenSky)
			if (_skyBase.TryGetValue(name, out var b)) _skyMat.SetShaderParameter(name, b.Lerp(open, q));
		_skyMat.SetShaderParameter("glow_strength", Mathf.Lerp(_skyGlowBase, 0.95f, q));
		_skyMat.SetShaderParameter("cloud_cover", Mathf.Lerp(_skyCloudBase, 0.3f, q));
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
		}
		_open = Mathf.Lerp(_open, _openTarget, 1f - Mathf.Exp(-dt / Mathf.Max(OpenSmoothing, 0.01f)));
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
		float strength = indoors ? 0f : target.strength * (1f - 0.85f * storm) * ShaftScale;
		float k = 1f - Mathf.Exp(-(float)GetProcessDeltaTime() * 1.2f);
		_shaftTint = _shaftTint.Lerp(target.tint, k);
		_shaftIntensity = Mathf.Lerp(_shaftIntensity, target.intensity, k);
		_shaftStrength = ShaftScale <= 0f ? 0f : Mathf.Lerp(_shaftStrength, strength, k);
		_shafts.Tint = _shaftTint;
		_shafts.Intensity = _shaftIntensity;
		_shafts.Strength = _shaftStrength;
		_shafts.LightDir = -_sun.GlobalBasis.Z;
	}

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
		float under = Mathf.Clamp(Underground, 0f, 1f);
		if (under > 0f)
		{
			fog = fog.Lerp(new Color(0.004f, 0.004f, 0.005f), under);
			density = Mathf.Lerp(density, UndergroundFogDensity, under);
			ambient *= 1f - 0.97f * under;
			sunEnergy *= 1f - under;
		}
		_env.FogLightColor = fog;
		_env.FogDensity = density;
		_env.FogSkyAffect = _baseSkyFog;
		_env.AmbientLightColor = ambColor;
		_env.AmbientLightEnergy = ambient;
		_env.TonemapExposure = _exposureBase + OpenExposureBoost * _open;
		_env.BackgroundEnergyMultiplier = (_bgEnergyBase * _baseSkyEnergy * (1f + OpenSkyBoost * _open) * (1f - 0.35f * storm) + flash * 2.5f) * (1f - under);
		if (_sun == null) return;
		_sun.LightEnergy = sunEnergy;
		_sun.LightColor = _baseSunColor;
		DriveShafts(storm);
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
