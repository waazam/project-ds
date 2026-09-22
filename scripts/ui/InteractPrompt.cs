using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// The centre-screen crosshair dot and the interaction prompt under it.
///
/// Its single source of truth is the player's <see cref="PlayerInteraction"/>:
/// whatever is focused there shows its PromptText here, and a hold-to-use
/// interactable draws its HoldProgress as a thin ring around the dot. The dot
/// is tiny, low-alpha bone, and brightens a little when something is focused;
/// it fades out while the player has no control (cutscenes) or the screen is
/// blacked out.
///
/// Prompts never carry the key hint themselves: this layer prepends "[E]" or
/// "[Hold E]" to a usable interactable's text, and shows a blocked one's text
/// ("Hands full...") plain.
/// </summary>
public partial class InteractPrompt : CanvasLayer
{
	public static InteractPrompt Instance { get; private set; }

	/// <summary>Prompt band, below centre and clear of the ScreenFader caption band.</summary>
	public const float PromptTop = 64f;   // px below screen centre

	private Control _root;
	private Control _dot;
	private RichTextLabel _label;
	private string _shownKey, _shownText;
	private PlayerController _player;
	private PlayerInteraction _interaction;
	private ScreenFader _fader;
	private float _dotAlpha, _focusLit, _labelAlpha, _hold;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 15;
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		_dot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_dot.SetAnchorsPreset(Control.LayoutPreset.Center);
		_dot.OffsetLeft = -10; _dot.OffsetRight = 10; _dot.OffsetTop = -10; _dot.OffsetBottom = 10;
		_dot.Draw += DrawDot;
		_root.AddChild(_dot);

		_label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.Off,
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_label.AddThemeFontOverride("normal_font", UiKit.Serif);
		_label.AddThemeFontSizeOverride("normal_font_size", UiKit.BodySize);
		_label.AddThemeFontSizeOverride("mono_font_size", UiKit.SmallSize + 1);
		_label.AddThemeFontOverride("mono_font", UiKit.Mono);
		_label.AddThemeColorOverride("default_color", UiKit.Bone);
		_label.AddThemeColorOverride("font_shadow_color", UiKit.Shadow);
		_label.AddThemeConstantOverride("shadow_offset_x", 1);
		_label.AddThemeConstantOverride("shadow_offset_y", 1);
		_label.AddThemeConstantOverride("shadow_outline_size", 1);
		_label.AnchorLeft = 0.5f; _label.AnchorRight = 0.5f; _label.AnchorTop = 0.5f; _label.AnchorBottom = 0.5f;
		_label.OffsetLeft = -180; _label.OffsetRight = 180; _label.OffsetTop = PromptTop; _label.OffsetBottom = PromptTop + 16;
		_root.AddChild(_label);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_interaction = _player?.Interaction;
		}

		var focused = _interaction?.Focused;
		if (focused != null && !IsInstanceValid(focused)) focused = null;
		string text = focused != null ? _interaction.PromptText : null;
		// Usable interactables get the key hint; blocked ones ("Hands full...") read as plain text.
		string key = focused != null && !string.IsNullOrEmpty(text) && focused.CanInteract(_player)
			? (focused.HoldSeconds > 0f ? "[Hold E]" : "[E]")
			: null;
		bool hasControl = _player != null && _player.PlayerInput != null && _player.PlayerInput.Enabled;
		_fader ??= GetNodeOrNull<ScreenFader>("../ScreenFader");
		bool dotVisible = _player != null && hasControl && !GetTree().Paused && (_fader == null || _fader.BlackAlpha < 0.5f);

		_dotAlpha = Mathf.MoveToward(_dotAlpha, dotVisible ? 1f : 0f, dt * 3f);
		_focusLit = Mathf.MoveToward(_focusLit, focused != null ? 1f : 0f, dt * 8f);
		float hold = focused != null && focused.HoldSeconds > 0f ? focused.HoldProgress : 0f;
		_hold = hold < _hold ? Mathf.MoveToward(_hold, hold, dt * 4f) : hold;   // snap up, ease back down
		_dot.QueueRedraw();

		bool showLabel = !string.IsNullOrEmpty(text) && hasControl;
		if (showLabel && (text != _shownText || key != _shownKey)) { _shownText = text; _shownKey = key; _label.Text = Format(key, text); }
		_labelAlpha = Mathf.MoveToward(_labelAlpha, showLabel ? 1f : 0f, dt * (showLabel ? 10f : 6f));
		_label.Modulate = new Color(1, 1, 1, _labelAlpha);
	}

	/// <summary>The prompt as BBCode: the key hint ("[E]" / "[Hold E]", if any) in quiet eye-yellow
	/// monospace, the text in bone serif. The text itself is shown verbatim.</summary>
	private static string Format(string key, string text)
	{
		static string Esc(string s) => s.Replace("[", "[lb]");
		if (string.IsNullOrEmpty(key)) return Esc(text);
		return $"[code][color=#{UiKit.Eye.ToHtml(false)}c0]{Esc(key)}[/color][/code] {Esc(text)}";
	}

	private void DrawDot()
	{
		if (_dotAlpha <= 0.01f) return;
		var c = _dot.Size * 0.5f;
		float a = _dotAlpha * Mathf.Lerp(0.28f, 0.6f, _focusLit);
		// A 2x2 dot on the pixel grid (the screen centre sits between pixels at 640x360).
		_dot.DrawRect(new Rect2(c - new Vector2(1, 1), new Vector2(2, 2)), new Color(UiKit.Bone, a));
		_dot.DrawRect(new Rect2(c - new Vector2(2, 2), new Vector2(4, 4)), new Color(0, 0, 0, a * 0.25f), false, 1f);
		if (_hold > 0.005f)
		{
			float r = 5.5f;
			_dot.DrawArc(c, r, 0f, Mathf.Tau, 24, new Color(UiKit.Bone, 0.12f * _dotAlpha), 1f);
			_dot.DrawArc(c, r, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + Mathf.Tau * _hold, 24, new Color(UiKit.Eye, 0.7f * _dotAlpha), 1f);
		}
	}
}
