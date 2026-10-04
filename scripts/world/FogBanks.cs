using Godot;

namespace ProjectDS.World;

/// <summary>
/// Fog that moves (2026-10-04): banks and wisps drifting through the woods, the lake and the winter woods on a slow
/// wind, in the volumetric fog (fog_banks.gdshader). One fog volume rides with the camera; the noise is the world's, so
/// the banks hold still in the world and drift past as the wind carries them. <see cref="ForestAtmosphere"/> sets how
/// thick they are by place (none indoors or underground; more as Act 1's fog closes in).
/// </summary>
public partial class FogBanks : FogVolume
{
	private ShaderMaterial _mat;
	private static NoiseTexture3D _noise;
	private float _strength = -1f;

	/// <summary>The banks' density at full strength.</summary>
	public const float Density = 0.05f;

	public override void _Ready()
	{
		_noise ??= new NoiseTexture3D
		{
			Width = 64, Height = 64, Depth = 64, Seamless = true, Normalize = true,
			Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.06f, FractalOctaves = 3, Seed = 4417 },
		};
		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/fog_banks.gdshader") };
		_mat.SetShaderParameter("noise_tex", _noise);
		Material = _mat;
		Shape = RenderingServer.FogVolumeShape.Box;
		Size = new Vector3(84f, 16f, 84f);
		TopLevel = true;
		Visible = false;
	}

	/// <summary>Rides with the camera; <paramref name="strength"/> 0 (gone) .. 1 (and up, for Act 1's thickest).</summary>
	public void Follow(Camera3D cam, float strength)
	{
		if (cam == null) return;
		bool on = strength > 0.01f;
		if (Visible != on) Visible = on;
		if (!on) return;
		var p = cam.GlobalPosition;
		GlobalPosition = new Vector3(p.X, p.Y + 3f, p.Z);
		_mat.SetShaderParameter("ground_y", p.Y - 1.7f);
		if (Mathf.Abs(strength - _strength) > 0.01f)
		{
			_strength = strength;
			_mat.SetShaderParameter("density", Density * strength);
		}
	}

	/// <summary>The banks' tint, from the scene's fog (a pale grey of its hue).</summary>
	public void Tint(Color c) => _mat?.SetShaderParameter("tint", new Vector3(c.R, c.G, c.B));
}
