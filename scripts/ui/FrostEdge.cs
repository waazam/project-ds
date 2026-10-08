using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Frost creeping in at the screen's edges while the wendigo is near in Act 24's maze (2026-10-07): it grows as it
/// comes closer (from about 16 m) and as the hunt's menace rises, and thaws away as it goes. Eased both ways over
/// seconds; still crystals, no flicker. Over the 3D picture, under the HUD.
/// </summary>
public partial class FrostEdge : CanvasLayer
{
	/// <summary>0..1 how near it is (its owner sets it each frame); the frost eases toward it.</summary>
	public float Target { get; set; }
	public float Amount { get; private set; }
	private ShaderMaterial _mat;

	public override void _Ready()
	{
		Layer = 1;
		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/frost_edge.gdshader") };
		_mat.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, Noise = new FastNoiseLite { Frequency = 0.03f, FractalOctaves = 4, Seed = 2471 } });
		var rect = new ColorRect { Name = "Frost", Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(rect);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		// in over about four seconds, out over about three
		Amount = Mathf.MoveToward(Amount, Mathf.Clamp(Target, 0f, 1f), dt * (Target > Amount ? 0.25f : 0.33f));
		var vp = GetViewport().GetVisibleRect().Size;
		_mat.SetShaderParameter("aspect", new Vector2(vp.X / Mathf.Max(vp.Y, 1f), 1f));
		_mat.SetShaderParameter("amount", Amount);
	}
}
