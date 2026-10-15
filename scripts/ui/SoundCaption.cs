using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Captions for the sounds that matter (2026-10-11, accessibility: "subtitles for every voice and important sound (the
/// knocks, the whispers, the howls)"). With <see cref="GameSettings.SoundCaptions"/> on, a short bracketed line,
/// "[knocking]", "[a howl, far off]", comes up small over the subtitles while the sound plays, from the left or the right
/// when it's off to that side. Fed by AudioDirector.OneShot.
/// </summary>
public partial class SoundCaption : CanvasLayer
{
	public static SoundCaption Instance { get; private set; }
	private static readonly Dictionary<string, string> Captions = new()
	{
		["keypad_accept"] = "a beep, and the lock clacks", ["keypad_deny"] = "a buzz", ["wall_knock"] = "knocking",["door_knock"] = "knocking", ["whisper_voice"] = "whispering", ["wendigo_howl_far"] = "a howl, far off",
		["wendigo_howl"] = "a howl", ["wendigo_mimic"] = "a voice that isn't human", ["wendigo_mumble"] = "muttering", ["wendigo_step"] = "heavy footsteps",
		["wendigo_land"] = "something lands", ["wendigo_leap"] = "something leaps", ["creature_snarl"] = "a snarl", ["claw_rake"] = "claws on wood",
		["claw_scrape"] = "scraping", ["door_creak"] = "a door creaks", ["door_slam"] = "a door slams", ["door_ram"] = "a crash against the door",
		["glass_break"] = "glass breaking", ["musicbox_lullaby"] = "a music box", ["church_bell_far"] = "a bell, far off", ["radio_burst"] = "static",
		["radio_lullaby"] = "a tune through the static", ["creature_roar_far"] = "a roar, far off", ["creature_roar_near"] = "a roar",
		["giant_step"] = "something huge walking", ["tree_fall_impact"] = "a tree falls", ["stair_groan"] = "the stairs groan", ["catwalk_slam"] = "a crashing blow",
		["shadow_breath"] = "breathing", ["crawler_step"] = "something on the stairs", ["dust_trickle"] = "dust falling", ["flame_ignite"] = "fire catches",
		["bottle_clink"] = "glass clinking", ["can_clatter"] = "a tin clatters", ["wood_bump"] = "something rolling",
	};

	private Label _label;
	private float _alpha, _hold;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 16;
		ProcessMode = ProcessModeEnum.Always;
		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
		_label.AddThemeFontOverride("font", UiKit.SerifItalic);
		_label.AddThemeFontSizeOverride("font_size", 9);
		_label.AddThemeColorOverride("font_color", new Color(UiKit.Bone, 0.85f));
		_label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
		_label.AddThemeConstantOverride("outline_size", 3);
		_label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_label.OffsetLeft = -200; _label.OffsetRight = 200; _label.OffsetTop = -78; _label.OffsetBottom = -64;
		AddChild(_label);
	}

	/// <summary>A sound by its name (its variant number stripped), heard at <paramref name="at"/> (null: in the head).</summary>
	public static void Heard(Node from, string name, Vector3? at)
	{
		if (Instance == null || !(GameSettings.Instance?.SoundCaptions ?? false)) return;
		string key = name;
		if (!Captions.TryGetValue(key, out var text))
		{
			foreach (var (k, v) in Captions) if (name.StartsWith(k)) { text = v; break; }
			if (text == null) return;
		}
		string side = "";
		if (at is { } w && from.GetViewport()?.GetCamera3D() is { } cam)
		{
			if (cam.GlobalPosition.DistanceTo(w) > 45f) return;
			var local = cam.GlobalBasis.Inverse() * (w - cam.GlobalPosition);
			if (local.Z > 2f && Mathf.Abs(local.X) < Mathf.Abs(local.Z)) side = "behind: ";
			else if (local.X < -2f) side = "← ";
			else if (local.X > 2f) text += " →";
		}
		Instance._label.Text = $"[{side}{text}]";
		Instance._hold = 2.4f;
	}

	public override void _Process(double delta)
	{
		_hold -= (float)delta;
		_alpha = Mathf.MoveToward(_alpha, _hold > 0 ? 1f : 0f, (float)delta * 3f);
		_label.Modulate = new Color(1, 1, 1, _alpha);
		if (_alpha <= 0.001f) return;
		// (2026-10-15) over whatever shows in the low band (a hint, a thought, a spoken line), never across it
		float screenY = _label.GetViewportRect().Size.Y;
		float bottom = -64f;
		if (!float.IsNaN(HintStyle.SubtitleTop)) bottom = Mathf.Min(bottom, HintStyle.SubtitleTop - screenY - 2f);
		if (!float.IsNaN(HintStyle.ThoughtTop)) bottom = Mathf.Min(bottom, HintStyle.ThoughtTop - screenY - 2f);
		_label.OffsetBottom = Mathf.Round(bottom); _label.OffsetTop = _label.OffsetBottom - 14f;
	}
}
