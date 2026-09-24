using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Act 12's rowing prompt: two small keycaps low on the screen, left oar and right oar, in the
/// game's quiet UI style (bone outlines, eye-yellow for "this one next"). The key letters come from
/// the real bindings (move_left / move_right, A and D by default), or stick arrows once a gamepad
/// has been used. The cap that should be pressed next is lit; a pressed cap dips for a moment.
///
/// Between them, a small arrow reads the boat's way through the water: pointing up in bone while it
/// makes headway, turning down and dull red when the current is winning — so the player can tell
/// they are being dragged back even while looking straight ahead. Nothing here flashes or pulses:
/// every change is an ease, and <see cref="Urgent"/> only brightens and enlarges the caps a little.
/// </summary>
public partial class RowingPrompt : CanvasLayer
{
	/// <summary>-1 left next, +1 right next, 0 either.</summary>
	public int Next { get; set; }
	/// <summary>Net headway in m/s (negative = losing ground). Only drawn while <see cref="ShowDrift"/>.</summary>
	public float Drift { get; set; }
	public bool ShowDrift { get; set; }
	/// <summary>The current phase: the caps sit a little bigger and brighter.</summary>
	public bool Urgent { get; set; }

	private Control _root;
	private float _alpha, _target;
	private float _pressL, _pressR, _litL, _litR, _urgent, _drift, _driftAlpha;
	private string _left = "A", _right = "D";
	private bool _pad;

	public override void _Ready()
	{
		Layer = 14;
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.Draw += DrawCaps;
		AddChild(_root);
		_left = KeyName("move_left", "A");
		_right = KeyName("move_right", "D");
	}

	/// <summary>Fade the prompt in or out.</summary>
	public void SetShown(bool show) => _target = show ? 1f : 0f;

	public bool Showing => _target > 0.5f;

	/// <summary>A stroke landed on that side (the cap dips).</summary>
	public void Pressed(int side)
	{
		if (side < 0) _pressL = 1f; else _pressR = 1f;
	}

	private static string KeyName(string action, string fallback)
	{
		foreach (var e in InputMap.ActionGetEvents(action))
			if (e is InputEventKey k)
			{
				var code = k.PhysicalKeycode != Key.None ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode) : k.Keycode;
				string s = OS.GetKeycodeString(code);
				if (!string.IsNullOrEmpty(s)) return s.Length > 3 ? s[..3] : s;
			}
		return fallback;
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventJoypadButton || (e is InputEventJoypadMotion m && Mathf.Abs(m.AxisValue) > 0.5f)) _pad = true;
		else if (e is InputEventKey) _pad = false;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_alpha = Mathf.MoveToward(_alpha, _target, dt * (_target > _alpha ? 2.5f : 1.8f));
		_pressL = Mathf.MoveToward(_pressL, 0f, dt * 7f);
		_pressR = Mathf.MoveToward(_pressR, 0f, dt * 7f);
		_litL = Mathf.MoveToward(_litL, Next <= 0 ? 1f : 0f, dt * 9f);
		_litR = Mathf.MoveToward(_litR, Next >= 0 ? 1f : 0f, dt * 9f);
		_urgent = Mathf.MoveToward(_urgent, Urgent ? 1f : 0f, dt * 2f);
		_drift = Mathf.Lerp(_drift, Drift, 1f - Mathf.Exp(-dt * 4f));
		_driftAlpha = Mathf.MoveToward(_driftAlpha, ShowDrift ? 1f : 0f, dt * 2f);
		_root.Visible = _alpha > 0.01f;
		if (_root.Visible) _root.QueueRedraw();
	}

	private void DrawCaps()
	{
		var size = _root.Size;
		float cx = size.X * 0.5f, y = size.Y - 50f;
		float gap = 44f + 6f * _urgent;
		DrawCap(new Vector2(cx - gap, y), _pad ? "<" : _left, "left oar", _litL, _pressL);
		DrawCap(new Vector2(cx + gap, y), _pad ? ">" : _right, "right oar", _litR, _pressR);
		if (_driftAlpha > 0.01f) DrawDrift(new Vector2(cx, y));
	}

	private void DrawCap(Vector2 c, string key, string label, float lit, float press)
	{
		float a = _alpha;
		float s = 20f + 3f * _urgent;
		var rect = new Rect2(c - new Vector2(s, s) * 0.5f + new Vector2(0, 2f * press), new Vector2(s, s));
		var edge = new Color(UiKit.Bone, 0.35f + 0.2f * _urgent).Lerp(new Color(UiKit.Eye, 0.9f), lit);
		// the key's body: a dark cap with a lighter lower lip (depth), sinking into it when pressed
		_root.DrawRect(new Rect2(rect.Position + new Vector2(0, 3f - 2f * press), rect.Size), new Color(0, 0, 0, 0.45f * a));
		_root.DrawRect(rect, new Color(0.05f, 0.05f, 0.06f, (0.55f + 0.15f * lit) * a));
		_root.DrawRect(rect, new Color(edge, edge.A * a), false, 1f);
		var font = UiKit.Mono;
		int fs = UiKit.SmallSize + 3;
		var textSize = font.GetStringSize(key, HorizontalAlignment.Center, -1, fs);
		var textPos = new Vector2(rect.GetCenter().X - textSize.X * 0.5f, rect.GetCenter().Y + textSize.Y * 0.3f);
		var keyCol = new Color(UiKit.Bone, 0.6f * a).Lerp(new Color(UiKit.Eye, a), lit);
		_root.DrawString(font, textPos, key, HorizontalAlignment.Left, -1, fs, keyCol);
		var serif = UiKit.Serif;
		int ls = UiKit.SmallSize;
		var lsz = serif.GetStringSize(label, HorizontalAlignment.Center, -1, ls);
		_root.DrawString(serif, new Vector2(c.X - lsz.X * 0.5f, rect.End.Y + 12f), label, HorizontalAlignment.Left, -1, ls,
			new Color(UiKit.Muted, (0.5f + 0.3f * lit) * a));
	}

	/// <summary>A small triangle between the caps: up and bone while making headway, down and red while
	/// being pushed back, its size following how hard.</summary>
	private void DrawDrift(Vector2 c)
	{
		float a = _alpha * _driftAlpha;
		float v = Mathf.Clamp(_drift / 2.5f, -1f, 1f);
		float h = 4f + 5f * Mathf.Abs(v);
		float dir = v >= 0f ? -1f : 1f;   // screen up = forward
		var col = new Color(UiKit.Bone, 0.6f * a).Lerp(new Color(0.75f, 0.16f, 0.12f, 0.9f * a), Mathf.Clamp(-v * 2f, 0f, 1f));
		var pts = new[] { c + new Vector2(0, dir * h), c + new Vector2(-h * 0.8f, -dir * h * 0.6f), c + new Vector2(h * 0.8f, -dir * h * 0.6f) };
		_root.DrawColoredPolygon(pts, col);
	}
}
