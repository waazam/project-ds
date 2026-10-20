using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A local fog (2026-10-19, the owner: "dense, oppressive ... to rival Silent Hill 2's fog"): a box of the volumetric fog
/// (Godot's froxel fog, ray-marched against the depth buffer, lit by the scene's lights) with its own shape and drift
/// (fog_zone.gdshader), placed by the scene that needs it and faded in and out by the story:
/// <list type="bullet">
/// <item><see cref="Kind.Layer"/>: lying on the floor, thinning to nothing <see cref="LayerHeight"/> above it (the sewer,
/// the long hall's second half; the lake's dawn mist).</item>
/// <item><see cref="Kind.Veil"/>: clear up to <see cref="RevealAbove"/> over the eye, thick above it, the line riding the
/// camera (the last staircase: its top lost until it's climbed).</item>
/// <item><see cref="Kind.Volume"/>: the whole box.</item>
/// </list>
/// The froxels reach <c>VolumetricFogLength</c> from the eye; a zone the camera is in can ask for more
/// (<see cref="WantedLength"/>, read by <see cref="ForestAtmosphere"/>) so its far end is fogged too, not cut off.
/// </summary>
[GlobalClass]
public partial class FogZone : FogVolume
{
	public enum Kind { Layer, Veil, Volume }

	[Export] public Kind Mode = Kind.Layer;
	/// <summary>Its density at full strength (Godot's units: about 1 - exp(-d) of the light lost a metre).</summary>
	[Export] public float Density = 0.06f;
	[Export] public Color Albedo = new(0.7f, 0.7f, 0.72f);
	/// <summary>A glow of its own (a dim body in the dark, never a glare), times its density.</summary>
	[Export] public Color Emission = Colors.Black;
	[Export] public float EmissionEnergy = 0f;
	/// <summary>How much of it is banks and gaps (0 even).</summary>
	[Export] public float NoiseAmount = 0.6f;
	[Export] public float NoiseScale = 0.045f;
	[Export] public Vector3 Wind = new(0.35f, 0f, 0.18f);
	/// <summary>Layer: the floor (local Y) and how high over it the fog reaches.</summary>
	[Export] public float FloorY = 0f, LayerHeight = 2f;
	/// <summary>Veil: clear this far above the eye, thick over <see cref="VeilRamp"/> above that.</summary>
	[Export] public float RevealAbove = 4f, VeilRamp = 10f;
	[Export] public float Edge = 0.15f;
	/// <summary>The fog length (m) to ask for while the camera is in it (0: the scene's own).</summary>
	[Export] public float VolumetricLength = 0f;
	/// <summary>Strength on entering the tree (0 hidden until <see cref="FadeTo"/>).</summary>
	[Export] public float StartStrength = 1f;

	public float Strength { get; private set; }
	private float _target, _rate;
	private ShaderMaterial _mat;
	private static NoiseTexture3D _noise;
	private static Shader _shader;
	private static readonly HashSet<FogZone> _all = new();

	public override void _EnterTree() => _all.Add(this);
	public override void _ExitTree() => _all.Remove(this);

	public override void _Ready()
	{
		_noise ??= new NoiseTexture3D
		{
			Width = 64, Height = 64, Depth = 64, Seamless = true, Normalize = true,
			Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.06f, FractalOctaves = 3, Seed = 6113 },
		};
		_shader ??= GD.Load<Shader>("res://assets/shaders/fog_zone.gdshader");
		_mat = new ShaderMaterial { Shader = _shader };
		_mat.SetShaderParameter("noise_tex", _noise);
		Shape = RenderingServer.FogVolumeShape.Box;
		Material = _mat;
		Strength = _target = StartStrength;
		Apply();
		SetProcess(true);
	}

	/// <summary>To <paramref name="strength"/> (0 gone .. 1) over <paramref name="seconds"/>.</summary>
	public void FadeTo(float strength, float seconds)
	{
		_target = Mathf.Max(0f, strength);
		_rate = seconds <= 0.01f ? float.MaxValue : Mathf.Abs(_target - Strength) / seconds;
		if (seconds <= 0.01f) Strength = _target;
	}

	/// <summary>Light of its own along up to three lines (local X) near the floor: the colour of the strips under it.</summary>
	public void SetGlow(Color color, float energy, Vector3 linesX, float width = 0.6f, float height = 1f)
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("glow_color", new Vector3(color.R, color.G, color.B));
		_mat.SetShaderParameter("glow_energy", energy);
		_mat.SetShaderParameter("glow_x", linesX);
		_mat.SetShaderParameter("glow_width", width);
		_mat.SetShaderParameter("glow_height", height);
	}

	public override void _Process(double delta)
	{
		if (!Mathf.IsEqualApprox(Strength, _target)) Strength = Mathf.MoveToward(Strength, _target, _rate * (float)delta);
		Apply();
	}

	private void Apply()
	{
		if (_mat == null) return;
		bool on = Strength > 0.005f;
		if (Visible != on) Visible = on;
		if (!on) return;
		_mat.SetShaderParameter("density", Density);
		_mat.SetShaderParameter("strength", Strength);
		_mat.SetShaderParameter("albedo", new Vector3(Albedo.R, Albedo.G, Albedo.B));
		_mat.SetShaderParameter("emission", new Vector3(Emission.R, Emission.G, Emission.B));
		_mat.SetShaderParameter("emission_energy", EmissionEnergy);
		_mat.SetShaderParameter("noise_amount", NoiseAmount);
		_mat.SetShaderParameter("noise_scale", NoiseScale);
		_mat.SetShaderParameter("wind", Wind);
		_mat.SetShaderParameter("edge", Edge);
		float floorWorld = GlobalPosition.Y + FloorY;
		_mat.SetShaderParameter("floor_y", floorWorld);
		_mat.SetShaderParameter("layer_height", Mode == Kind.Layer ? LayerHeight : 0f);
		if (Mode == Kind.Veil && GetViewport()?.GetCamera3D() is { } cam)
		{
			_mat.SetShaderParameter("reveal_y", cam.GlobalPosition.Y + RevealAbove);
			_mat.SetShaderParameter("veil_ramp", VeilRamp);
		}
		else _mat.SetShaderParameter("veil_ramp", 0f);
	}

	/// <summary>Whether a world point is inside its box (with a margin).</summary>
	public bool Contains(Vector3 world, float margin = 0f)
	{
		Vector3 l = GlobalTransform.AffineInverse() * world;
		Vector3 h = Size * 0.5f + Vector3.One * margin;
		return Mathf.Abs(l.X) <= h.X && Mathf.Abs(l.Y) <= h.Y && Mathf.Abs(l.Z) <= h.Z;
	}

	/// <summary>The longest fog length asked for by a zone the camera is in (0: none).</summary>
	public static float WantedLength(Vector3 cam)
	{
		float want = 0f;
		foreach (var z in _all)
			if (z.VolumetricLength > 0f && z.Strength > 0.05f && z.IsInsideTree() && z.Contains(cam, 4f)) want = Mathf.Max(want, z.VolumetricLength);
		return want;
	}

	/// <summary>For tests: the zones in the scene now.</summary>
	public static IReadOnlyCollection<FogZone> All => _all;
}
