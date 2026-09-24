using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// A real sunrise over the lake. The Hollow's sky, sun and fog are shared by the whole level and
/// tuned for its one long night; while the camera is at the lake, this swaps in a sunrise: the sun
/// low over the far shore (a little left of the station, so the glitter path runs across the water
/// toward it and the station stands against the glow), a warm peach horizon under a cool blue-violet
/// zenith, gold-lit clouds, purple ridges fading into haze, and more of the sun scattered through
/// the fog. Leaving the lake (into the station's interior) puts everything back as it was.
///
/// The fog colour/density and the light levels are <see cref="ForestAtmosphere"/>'s Dawn mood
/// (the lake is the only place the Hollow uses it); this only owns what the mood doesn't: the sun's
/// direction, the sky shader's palette and the environment's sun scatter. ForestAtmosphere writes
/// the sun's basis and the sky palette once on its first frame, so this re-checks and re-applies
/// while at the lake rather than trusting one write.
/// </summary>
public partial class LakeSunrise : Node
{
	/// <summary>Degrees the sun sits left of straight across the lake, and above the horizon.</summary>
	[Export] public float SunAzimuthLeft = 24f;
	[Export] public float SunElevation = 7.5f;
	[Export] public float FogSunScatter = 0.32f;
	/// <summary>Half-size of the box (world metres, around the Lake node) that counts as "at the lake".</summary>
	[Export] public float Reach = 260f;

	public bool Active { get; private set; }

	private static readonly (string name, Color value)[] Palette =
	{
		("zenith_color", new Color(0.2f, 0.22f, 0.36f)),
		("horizon_color", new Color(0.86f, 0.56f, 0.44f)),
		("glow_color", new Color(1f, 0.66f, 0.34f)),
		("cloud_dark", new Color(0.62f, 0.46f, 0.46f)),
		("cloud_light", new Color(0.88f, 0.64f, 0.52f)),
		("ridge_far", new Color(0.5f, 0.4f, 0.47f)),
		("ridge_mid", new Color(0.38f, 0.3f, 0.38f)),
		("ridge_near", new Color(0.26f, 0.21f, 0.28f)),
		("haze_color", new Color(0.78f, 0.56f, 0.5f)),
		("below_color", new Color(0.6f, 0.45f, 0.42f)),
	};
	// thin, low-contrast cloud: the shared sky's value-noise clouds go blocky when bright and dense
	private const float GlowStrength = 1.15f, CloudCover = 0.22f;

	private Node3D _lake;
	private DirectionalLight3D _sun;
	private Environment _env;
	private ShaderMaterial _sky;
	private readonly Dictionary<string, Variant> _saved = new();
	private Basis _savedSun;
	private float _savedScatter;
	private Basis _sunrise;
	private ForestAtmosphere _atmo;

	public override void _Ready()
	{
		_lake = GetParent<Node3D>();
		float az = Mathf.DegToRad(SunAzimuthLeft), el = Mathf.DegToRad(SunElevation);
		// toward the sun: across the lake (-Z), swung left (-X), lifted
		Vector3 toSun = new(-Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
		_sunrise = Basis.LookingAt(-toSun, Vector3.Up);
	}

	public override void _Process(double delta)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null || _lake == null) return;
		if (_atmo == null)
		{
			// the level's own sun and environment, through the atmosphere that already owns them
			_atmo = GetTree().GetFirstNodeInGroup("atmosphere") as ForestAtmosphere;
			if (_atmo == null) return;
			_sun = _atmo.Sun;
			_env = _atmo.Env;
			_sky = _env?.Sky?.SkyMaterial as ShaderMaterial;
		}
		Vector3 d = cam.GlobalPosition - _lake.GlobalPosition;
		bool here = Mathf.Abs(d.X) < Reach && Mathf.Abs(d.Z) < Reach;
		if (here) Apply();
		else if (Active) Restore();
	}

	private void Apply()
	{
		if (!Active)
		{
			Active = true;
			if (_sun != null) _savedSun = _sun.GlobalBasis;
			if (_env != null) _savedScatter = _env.FogSunScatter;
			_saved.Clear();
			if (_sky != null)
			{
				foreach (var (name, _) in Palette) Save(name);
				Save("glow_strength");
				Save("cloud_cover");
			}
		}
		if (_sun != null && !_sun.GlobalBasis.IsEqualApprox(_sunrise)) _sun.GlobalBasis = _sunrise;
		if (_env != null) _env.FogSunScatter = FogSunScatter;
		_atmo.ShaftScale = 0f;
		// the sky's radiance map rebuilds on every write: only write when something else put it back
		if (_sky != null && !Mathf.IsEqualApprox(_sky.GetShaderParameter("glow_strength").AsSingle(), GlowStrength))
		{
			foreach (var (name, value) in Palette) _sky.SetShaderParameter(name, value);
			_sky.SetShaderParameter("glow_strength", GlowStrength);
			_sky.SetShaderParameter("cloud_cover", CloudCover);
		}
	}

	/// <summary>Remembers a sky uniform as set, or the shader's own default where the material never set it.</summary>
	private void Save(string name)
	{
		var v = _sky.GetShaderParameter(name);
		if (v.VariantType == Variant.Type.Nil) v = RenderingServer.ShaderGetParameterDefault(_sky.Shader.GetRid(), name);
		_saved[name] = v;
	}

	private void Restore()
	{
		Active = false;
		if (_atmo != null) _atmo.ShaftScale = 1f;
		if (_sun != null) _sun.GlobalBasis = _savedSun;
		if (_env != null) _env.FogSunScatter = _savedScatter;
		if (_sky == null) return;
		foreach (var (name, value) in _saved)
			if (value.VariantType != Variant.Type.Nil) _sky.SetShaderParameter(name, value);
	}
}
