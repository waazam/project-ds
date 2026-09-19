using Godot;

namespace ProjectDS.World;

/// <summary>
/// Slowly thickens and darkens the fog and dims the light as the camera goes
/// deeper into the woods (by distance north of the trailhead). Owns only the
/// WorldEnvironment/sun it is pointed at; no post effects. From Act 6 onward,
/// <see cref="SetMood"/> can override this distance-based blend entirely with
/// a scripted lighting transition (dawn, the menacing deep-forest tone, night).
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

	// Scripted mood targets (Act 6 onward). Fog colour/density, sky-fog, ambient and sun energy
	// all cross-fade from whatever the auto system last set toward these over SetMood's duration.
	[Export] public Color FogColorDawn = new(0.55f, 0.4f, 0.32f);
	[Export] public float FogDensityDawn = 0.014f;
	[Export] public float AmbientDawn = 1.0f;
	[Export] public float SunEnergyDawn = 0.95f;
	[Export] public Color SunColorDawn = new(1f, 0.72f, 0.5f);

	[Export] public Color FogColorMenacing = new(0.1f, 0.07f, 0.09f);
	[Export] public float FogDensityMenacing = 0.05f;
	[Export] public float AmbientMenacing = 0.32f;
	[Export] public float SunEnergyMenacing = 0.22f;

	[Export] public Color FogColorNight = new(0.045f, 0.05f, 0.075f);
	[Export] public float FogDensityNight = 0.032f;
	[Export] public float AmbientNight = 0.2f;
	[Export] public float SunEnergyNight = 0.04f;

	private Environment _env;
	private DirectionalLight3D _sun;
	private float _t = -1;

	private Mood _mood = Mood.Auto;
	private float _moodBlend;
	private float _moodBlendSpeed = 1f;
	private Color _fromFog, _fromSunColor;
	private float _fromDensity, _fromSkyFog, _fromAmbient, _fromSunEnergy;
	private Color _sunBaseColor;

	public override void _Ready()
	{
		_env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
		_sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
		if (_sun != null) _sunBaseColor = _sun.LightColor;
	}

	/// <summary>Cross-fades from the current lighting to a fixed mood over <paramref name="seconds"/>, then holds it (no more auto distance blend).</summary>
	public void SetMood(Mood mood, float seconds = 6f)
	{
		if (_env == null) return;
		_fromFog = _env.FogLightColor;
		_fromDensity = _env.FogDensity;
		_fromSkyFog = _env.FogSkyAffect;
		_fromAmbient = _env.AmbientLightEnergy;
		_fromSunEnergy = _sun?.LightEnergy ?? 0f;
		_fromSunColor = _sun?.LightColor ?? Colors.White;
		_mood = mood;
		_moodBlend = 0f;
		_moodBlendSpeed = 1f / Mathf.Max(seconds, 0.05f);
	}

	public override void _Process(double delta)
	{
		if (_env == null) return;
		if (_mood != Mood.Auto) { ProcessMoodBlend(delta); return; }
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		Vector3 p = cam.GlobalPosition;
		float deep = Mathf.Clamp((DeepStartZ - p.Z) / (DeepStartZ - DeepFullZ), 0, 1);
		deep = deep * deep * (3 - 2 * deep);
		float inClearing = 1f - Mathf.Clamp((new Vector2(p.X - ClearingCenter.X, p.Z - ClearingCenter.Z).Length() - ClearingRadius * 0.5f) / (ClearingRadius * 0.5f), 0, 1);
		float t = deep * (1f - ClearingRelief * inClearing);
		if (Mathf.Abs(t - _t) < 0.002f) return;
		_t = t;
		_env.FogDensity = Mathf.Lerp(FogDensityOpen, FogDensityDeep, t) * (1f - 0.35f * ClearingRelief * inClearing);
		_env.FogLightColor = FogColorOpen.Lerp(FogColorDeep, t);
		_env.FogSkyAffect = Mathf.Lerp(SkyFogOpen, SkyFogDeep, t);
		_env.AmbientLightEnergy = Mathf.Lerp(AmbientOpen, AmbientDeep, t);
		if (_sun != null) _sun.LightEnergy = Mathf.Lerp(SunOpen, SunDeep, t);
	}

	private void ProcessMoodBlend(double delta)
	{
		_moodBlend = Mathf.Min(1f, _moodBlend + (float)delta * _moodBlendSpeed);
		float u = Mathf.SmoothStep(0f, 1f, _moodBlend);
		(Color fog, float density, float skyFog, float ambient, float sunEnergy, Color sunColor) target = _mood switch
		{
			Mood.Dawn => (FogColorDawn, FogDensityDawn, 0.1f, AmbientDawn, SunEnergyDawn, SunColorDawn),
			Mood.Menacing => (FogColorMenacing, FogDensityMenacing, 0.6f, AmbientMenacing, SunEnergyMenacing, _sunBaseColor),
			Mood.Night => (FogColorNight, FogDensityNight, 0.35f, AmbientNight, SunEnergyNight, _sunBaseColor),
			_ => (_fromFog, _fromDensity, _fromSkyFog, _fromAmbient, _fromSunEnergy, _fromSunColor),
		};
		_env.FogLightColor = _fromFog.Lerp(target.fog, u);
		_env.FogDensity = Mathf.Lerp(_fromDensity, target.density, u);
		_env.FogSkyAffect = Mathf.Lerp(_fromSkyFog, target.skyFog, u);
		_env.AmbientLightEnergy = Mathf.Lerp(_fromAmbient, target.ambient, u);
		if (_sun == null) return;
		_sun.LightEnergy = Mathf.Lerp(_fromSunEnergy, target.sunEnergy, u);
		_sun.LightColor = _fromSunColor.Lerp(target.sunColor, u);
	}
}
