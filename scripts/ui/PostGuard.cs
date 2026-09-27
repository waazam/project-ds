using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Keeps the full-screen finish at its resting look outside of cutscenes. The post material (with its
/// vignette) is one resource shared by every level, and a blink or pass-out that ended with a level
/// change could leave its heavy vignette behind, a black "eye" round the view for the rest of the game
/// (the owner's lake screenshot). Whenever no cutscene is running, the vignette eases back to rest.
/// </summary>
public partial class PostGuard : Node
{
	/// <summary>The finish's resting vignette (ps2_post.gdshader's default).</summary>
	public const float RestVignette = 0.38f;

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		// not during a cutscene, or while the player is held (a wake-up's eyelids are this vignette)
		if (Cutscene.ActiveCount > 0 || StoryBeat.Player(this) is not { } player || !player.PlayerInput.Enabled) return;
		var post = StoryBeat.PostMaterial(this);
		if (post == null) return;
		float v = post.GetShaderParameter("vignette").AsSingle();
		if (v > RestVignette + 0.001f) post.SetShaderParameter("vignette", Mathf.MoveToward(v, RestVignette, (float)delta * 1.5f));
	}
}
