using Godot;

namespace ProjectDS.UI;

/// <summary>
/// The main menu's moving background: a pre-rendered, dithered frame of the
/// bunker's TV room, every screen showing the stairs (a BunkerPreview capture,
/// menu_e_desk_low, saved as res://assets/ui/menu_backdrop.png) with a slow
/// push-in, faint haze and flickering screen glow, all in one shader. Loads instantly; no world is instanced.
/// Falls back to the website's night gradient if the capture is missing.
/// </summary>
public partial class MenuBackdrop : ColorRect
{
	public const string BackdropPath = "res://assets/ui/menu_backdrop.png";
	private const string ShaderPath = "res://assets/ui/menu_backdrop.gdshader";
	private const string VignettePath = "res://assets/ui/vignette.gdshader";

	/// <summary>A live image to show instead of the pre-rendered capture (the title TV).</summary>
	public Texture2D Source { get; set; }

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Color = UiKit.Night;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		var tex = Source ?? (ResourceLoader.Exists(BackdropPath) ? GD.Load<Texture2D>(BackdropPath) : null);
		mat.SetShaderParameter("has_backdrop", tex != null);
		if (tex != null) mat.SetShaderParameter("backdrop", tex);
		if (Source != null)
		{
			// A live scene lights and flickers itself: just a slow push-in toward the set and the finish.
			mat.SetShaderParameter("flicker", 0f);
			mat.SetShaderParameter("fog_far", 0f);
			mat.SetShaderParameter("zoom_amount", 0.05f);
			mat.SetShaderParameter("focus", new Vector2(0.6f, 0.5f));
		}
		Material = mat;
	}

	/// <summary>A full-rect near-black vignette overlay (menus, pause).</summary>
	public static ColorRect MakeVignette(float strength)
	{
		var v = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.White };
		v.SetAnchorsPreset(LayoutPreset.FullRect);
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>(VignettePath) };
		mat.SetShaderParameter("strength", strength);
		v.Material = mat;
		return v;
	}
}
