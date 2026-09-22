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
/// The Act 1 day look also carries an "open trail" grade: at the parking lot,
/// the cabin and the trailhead the sun is a little brighter and warmer, the fog
/// thinner and lighter, the sky clearer; it fades out along the trail and is
/// gone by the footbridge, from where the usual look stands. It is a grade on
/// the auto blend only: never during a storm or a scripted mood.
/// </summary>
[GlobalClass]
public partial class ForestAtmosphere : Node
{
	public enum Mood { Auto, Dawn, Menacing, Night }

	/// <summary>For the autotest and other callers: which mood is currently active (or blending toward).</summary>
	public Mood CurrentMood => _mood;
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
	[Export] public float SkyFogDeep = 0.6f;
	[Export] public float AmbientOpen = 0.85f;
	[Export] public float AmbientDeep = 0.6f;
	[Export] public float SunOpen = 0.75f;
	[Export] public float SunDeep = 0.4f;
	/// <summary>An area that opens up a little again (the stairs' gap).</summary>
	[Export] public Vector3 ClearingCenter = new(0, 0, -302);
	[Export] public float ClearingRadius = 26f;
	/// <summary>How much of the deep-woods effect the clearing takes back (0 = none).</summary>
	[Export] public float ClearingRelief = 0.5f;

	[ExportGroup("Open trail (Act 1 day)")]
	[Export] public bool OpenTrailGrade = true;
	/// <summary>Fully open until this far along the trail; fades out from here to the footbridge.</summary>
	[Export] public float OpenHoldMeters = 40f;
	/// <summary>Where the grade is gone, in metres along the trail (the "bridge_marker" node overrides it).</summary>
	[Export] public float OpenFadeEndMeters = 180f;
	[Export] public float OpenSunScale = 1.5f;
	[Export] public Color OpenSunColor = new(1f, 0.92f, 0.72f);
	[Export] public float OpenFogDensityScale = 0.4f;
	[Export] public Color OpenFogColor = new(0.47f, 0.48f, 0.5f);
	[Export] public float OpenAmbientScale = 1.2f;
	/// <summary>Extra sky (background) energy when fully open.</summary>
	[Export] public float OpenSkyBoost = 0.3f;
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

	// The last "base" values (before the per-frame layers), so SetMood blends from what the mood system
	// had, not from a value that already includes a lightning flash or the ambient floor.
	private Color _baseFog, _baseSunColor;
	private float _baseDensity, _baseSkyFog, _baseAmbient, _baseSunEnergy;

	public override void _Ready()
	{
		AddToGroup("atmosphere");
		_env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
		_sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
		if (_sun != null) _sunBaseColor = _sun.LightColor;
		if (_env != null)
		{
			_ambientBaseColor = _env.AmbientLightColor;
			_bgEnergyBase = _env.BackgroundEnergyMultiplier;
			_baseFog = _env.FogLightColor;
			_baseDensity = _env.FogDensity;
			_baseSkyFog = _env.FogSkyAffect;
			_baseAmbient = _env.AmbientLightEnergy;
		}
		_baseSunEnergy = _sun?.LightEnergy ?? 0f;
		_baseSunColor = _sun?.LightColor ?? Colors.White;
	}

	/// <summary>Cross-fades from the current lighting to a fixed mood over <paramref name="seconds"/>, then holds it (no more auto distance blend).</summary>
	public void SetMood(Mood mood, float seconds = 6f)
	{
		if (_env == null) return;
		_fromFog = _baseFog;
		_fromDensity = _baseDensity;
		_fromSkyFog = _baseSkyFog;
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
		(Color fog, float density, float skyFog, float ambient, float sunEnergy, Color sunColor) target = _mood switch
		{
			Mood.Dawn => (FogColorDawn, FogDensityDawn, 0.1f, AmbientDawn, SunEnergyDawn, SunColorDawn),
			Mood.Menacing => (FogColorMenacing, FogDensityMenacing, 0.6f, AmbientMenacing, SunEnergyMenacing, _sunBaseColor),
			Mood.Night => (FogColorNight, FogDensityNight, 0.35f, AmbientNight, SunEnergyNight, MoonColor),
			_ => (_fromFog, _fromDensity, _fromSkyFog, _fromAmbient, _fromSunEnergy, _fromSunColor),
		};
		_baseFog = _fromFog.Lerp(target.fog, u);
		_baseDensity = Mathf.Lerp(_fromDensity, target.density, u);
		_baseSkyFog = Mathf.Lerp(_fromSkyFog, target.skyFog, u);
		_baseAmbient = Mathf.Lerp(_fromAmbient, target.ambient, u);
		_baseSunEnergy = Mathf.Lerp(_fromSunEnergy, target.sunEnergy, u);
		_baseSunColor = _fromSunColor.Lerp(target.sunColor, u);
	}

	private static float Lum(Color c) => c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;

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
		Color ambColor = _ambientBaseColor.Lerp(fogHue, AmbientFogTint);
		float ambient = _baseAmbient;
		float lum = Lum(ambColor);
		if (AmbientFloorLuminance > 0f && lum > 0.001f)
			ambient = Mathf.Max(ambient, AmbientFloorLuminance / lum);

		// Lightning: the whole sky and the fog light up for an instant (the directional light is RainVfx's).
		ambient += flash * 1.4f;
		fog += new Color(0.35f, 0.37f, 0.42f) * flash * 0.6f;

		_env.FogLightColor = fog;
		_env.FogDensity = density;
		_env.FogSkyAffect = _baseSkyFog;
		_env.AmbientLightColor = ambColor;
		_env.AmbientLightEnergy = ambient;
		_env.BackgroundEnergyMultiplier = _bgEnergyBase * (1f + OpenSkyBoost * _open) * (1f - 0.35f * storm) + flash * 2.5f;
		if (_sun == null) return;
		_sun.LightEnergy = sunEnergy;
		_sun.LightColor = _baseSunColor;
	}
}
