using Godot;

namespace ProjectDS.World;

/// <summary>
/// Slowly thickens and darkens the fog and dims the light as the camera goes
/// deeper into the woods (by distance north of the trailhead). Owns only the
/// WorldEnvironment/sun it is pointed at; no post effects.
/// </summary>
[GlobalClass]
public partial class ForestAtmosphere : Node
{
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

	private Environment _env;
	private DirectionalLight3D _sun;
	private float _t = -1;

	public override void _Ready()
	{
		_env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
		_sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
	}

	public override void _Process(double delta)
	{
		if (_env == null) return;
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
}
