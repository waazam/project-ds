using Godot;

namespace ProjectDS.UI;

/// <summary>Holding the breath (2026-10-10, <see cref="Player.PlayerBreathing"/>): the edges of the view close in a little,
/// darker the longer it's held. Eased in and out; nothing pulses.</summary>
public partial class BreathHoldOverlay : CanvasLayer
{
	public float Amount { get; set; }
	private float _shown;
	private ShaderMaterial _mat;
	private ColorRect _rect;

	private const string Code = @"shader_type canvas_item;
uniform float amount = 0.0;
uniform vec2 aspect = vec2(1.78, 1.0);
void fragment() {
	vec2 p = (UV - 0.5) * aspect;
	float r = length(p) / length(aspect * 0.5);
	float v = smoothstep(0.72 - amount * 0.35, 1.12 - amount * 0.25, r);
	COLOR = vec4(0.0, 0.0, 0.01, v * (0.5 + 0.4 * amount) * step(0.001, amount));
}";

	public override void _Ready()
	{
		Layer = 1;
		_mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
		_rect = new ColorRect { Name = "Vignette", Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
	}

	public override void _Process(double delta)
	{
		_shown = Mathf.MoveToward(_shown, Mathf.Clamp(Amount, 0f, 1f), (float)delta * 1.5f);
		_rect.Visible = _shown > 0.001f;
		if (!_rect.Visible) return;
		var vp = GetViewport().GetVisibleRect().Size;
		_mat.SetShaderParameter("aspect", new Vector2(vp.X / Mathf.Max(vp.Y, 1f), 1f));
		_mat.SetShaderParameter("amount", _shown);
	}
}
