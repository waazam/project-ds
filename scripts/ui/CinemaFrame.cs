using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The cinematic frame (the owner, from the teaser): 2.2:1 letterbox bars over the game while it is played.
/// They sit over the picture and its finish (layer 11, above the post at 10) and under every piece of HUD,
/// so prompts, the compass and subtitles can sit on the black. They slide away while the camera is raised,
/// so a photograph is never cropped by them, and come back when it is lowered. Also keeps the finish
/// (ps2_post) told whether the CRT filter is on and how many window rows draw each of the game's lines.
/// Off in the menus (there is no HUD there) and in the trailer, which frames itself.
/// </summary>
public partial class CinemaFrame : CanvasLayer
{
	public const float Aspect = 2.2f;
	private ColorRect _top, _bottom;
	private float _amount;

	public override void _Ready()
	{
		Layer = 11;
		ProcessMode = ProcessModeEnum.Always;
		_top = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		_bottom = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_top);
		AddChild(_bottom);
	}

	public override void _Process(double delta)
	{
		var s = GameSettings.Instance;
		bool inGame = GetTree().GetFirstNodeInGroup("post_screen") != null || GetTree().CurrentScene?.FindChild("PostProcess", false, false) != null;
		var vf = CameraViewfinder.Current;
		bool show = s != null && s.CinemaBars && !s.Trailer && inGame && (vf == null || !IsInstanceValid(vf) || vf.Raise < 0.01f);
		_amount = Mathf.MoveToward(_amount, show ? 1f : 0f, (float)delta / 0.35f);
		float e = _amount * _amount * (3f - 2f * _amount);
		var size = GetViewport().GetVisibleRect().Size;
		float bar = Mathf.Max(0f, (size.Y - size.X / Aspect) * 0.5f) * e;
		_top.Visible = _bottom.Visible = bar > 0.01f;
		// (each bar runs well past its edge of the screen: drawn at the game's 640x360 and scaled to a window that isn't a
		// whole multiple of it, a bar sized to the edge exactly could stop a fraction of a pixel short, a hairline of the
		// game showing under it; the owner saw one at the bottom)
		const float over = 16f;
		bar = Mathf.Ceil(bar);
		_top.Position = new Vector2(-over, -over);
		_top.Size = new Vector2(size.X + over * 2f, bar + over);
		_bottom.Position = new Vector2(-over, size.Y - bar);
		_bottom.Size = new Vector2(size.X + over * 2f, bar + over);

		if (inGame && s != null && StoryBeat.PostMaterial(this) is { } post)
		{
			bool crt = s.CrtFilter && !s.Trailer;
			post.SetShaderParameter("crt", crt ? 1f : 0f);
			post.SetShaderParameter("px_scale", crt ? s.LineScale : 1f);
		}
	}
}
