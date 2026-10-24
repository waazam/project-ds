using Godot;

namespace ProjectDS.World;

/// <summary>A soft round puff (white in the middle, gone at the edge) for steam and smoke billboards, so they never show as
/// squares (2026-10-24: the hearth's steam read as grey cards).</summary>
public static class SoftPuff
{
	private static Texture2D _tex;

	public static Texture2D Texture => _tex ??= new GradientTexture2D
	{
		Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
		FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
		Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.45f, 1f } },
	};
}
