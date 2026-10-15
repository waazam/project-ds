using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Act 12's rowing prompt, low on the screen in the game's quiet UI style (bone outlines, eye-yellow
/// for "doing it now"): three groups of keycaps, read left to right as the whole of the controls.
///   [W] row        [Shift] row hard       [A][D] steer
/// The letters come from the real bindings (move_forward, run, move_left / move_right), or the pad's
/// stick and trigger once a gamepad has been used. A cap is lit while its key is doing something and
/// dips on each stroke. Under "row hard" a thin line is the rower's breath: hard rowing spends it,
/// easy rowing gives it back, and spent it goes dull red until it has come back a way.
///
/// Above the caps, a small arrow reads the boat's way through the water: up in bone while it makes
/// headway, down and dull red when the current is winning. <see cref="Danger"/> (the thing behind
/// the boat closing in) warms "row hard" toward red, so the prompt itself says what to do. Nothing
/// here flashes: every change is an ease.
/// </summary>
public partial class RowingPrompt : CanvasLayer
{
	/// <summary>Kept for older callers: -1 left oar, +1 right oar, 0 either (it lights that steer cap).</summary>
	public int Next { get; set; }
	/// <summary>Net headway in m/s (negative = losing ground). Only drawn while <see cref="ShowDrift"/>.</summary>
	public float Drift { get; set; }
	public bool ShowDrift { get; set; }
	/// <summary>The creature is up: the caps sit a little bigger and brighter.</summary>
	public bool Urgent { get; set; }
	/// <summary>Rowing (W held): the row cap is lit.</summary>
	public bool Rowing { get; set; }
	/// <summary>Rowing hard (Shift with W): the row-hard cap is lit.</summary>
	public bool Hard { get; set; }
	/// <summary>-1 steering left, +1 right, 0 straight on.</summary>
	public float Steer { get; set; }
	/// <summary>0..1 the rower's breath for hard rowing; <see cref="Spent"/> once it ran out.</summary>
	public float Breath { get; set; } = 1f;
	public bool Spent { get; set; }
	/// <summary>0..1 how close the thing behind is: "row hard" warms toward red as it closes.</summary>
	public float Danger { get; set; }

	private Control _root;
	private float _alpha, _target;
	private float _pressRow, _pressL, _pressR, _litRow, _litHard, _litL, _litR, _urgent, _drift, _driftAlpha, _breath, _danger;
	private string _fwd = "W", _run = "Shift", _left = "A", _right = "D";
	private bool _pad;
	private static readonly Color Blood = new(0.78f, 0.17f, 0.12f);

	public override void _Ready()
	{
		Layer = 14;
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.Draw += DrawCaps;
		AddChild(_root);
		_fwd = KeyName("move_forward", "W");
		_run = KeyName("run", "Shift", 5);
		_left = KeyName("move_left", "A");
		_right = KeyName("move_right", "D");
	}

	/// <summary>Fade the prompt in or out.</summary>
	public void SetShown(bool show) => _target = show ? 1f : 0f;

	public bool Showing => _target > 0.5f;

	/// <summary>A stroke landed: 0 both oars (the row cap dips), -1 / +1 one oar (that steer cap dips).</summary>
	public void Pressed(int side)
	{
		if (side < 0) _pressL = 1f; else if (side > 0) _pressR = 1f; else _pressRow = 1f;
	}

	private static string KeyName(string action, string fallback, int max = 3)
	{
		foreach (var e in InputMap.ActionGetEvents(action))
			if (e is InputEventKey k)
			{
				var code = k.PhysicalKeycode != Key.None ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode) : k.Keycode;
				string s = OS.GetKeycodeString(code);
				if (!string.IsNullOrEmpty(s)) return s.Length > max ? s[..max] : s;
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
		_pressRow = Mathf.MoveToward(_pressRow, 0f, dt * 6f);
		_pressL = Mathf.MoveToward(_pressL, 0f, dt * 7f);
		_pressR = Mathf.MoveToward(_pressR, 0f, dt * 7f);
		_litRow = Mathf.MoveToward(_litRow, Rowing ? 1f : 0f, dt * 9f);
		_litHard = Mathf.MoveToward(_litHard, Hard ? 1f : 0f, dt * 9f);
		_litL = Mathf.MoveToward(_litL, Steer < -0.3f || Next < 0 ? 1f : 0f, dt * 9f);
		_litR = Mathf.MoveToward(_litR, Steer > 0.3f || Next > 0 ? 1f : 0f, dt * 9f);
		_urgent = Mathf.MoveToward(_urgent, Urgent ? 1f : 0f, dt * 2f);
		_danger = Mathf.MoveToward(_danger, Mathf.Clamp(Danger, 0f, 1f), dt * 1.5f);
		_breath = Mathf.Lerp(_breath, Mathf.Clamp(Breath, 0f, 1f), 1f - Mathf.Exp(-dt * 10f));
		_drift = Mathf.Lerp(_drift, Drift, 1f - Mathf.Exp(-dt * 4f));
		_driftAlpha = Mathf.MoveToward(_driftAlpha, ShowDrift ? 1f : 0f, dt * 2f);
		_root.Visible = _alpha > 0.01f;
		if (_root.Visible) _root.QueueRedraw();
	}

	private void DrawCaps()
	{
		var size = _root.Size;
		float cx = size.X * 0.5f, y = size.Y - 50f;
		float spread = 92f + 8f * _urgent;
		DrawCap(new Vector2(cx - spread, y), _pad ? "LS" : _fwd, "row", _litRow, _pressRow, 0f);
		var hard = new Vector2(cx, y);
		DrawCap(hard, _pad ? "LT" : _run, "row hard", _litHard, 0f, _danger);
		DrawBreath(hard + new Vector2(0, 24f));
		DrawCap(new Vector2(cx + spread - 14f, y), _pad ? "<" : _left, "", _litL, _pressL, 0f);
		DrawCap(new Vector2(cx + spread + 14f, y), _pad ? ">" : _right, "", _litR, _pressR, 0f);
		DrawLabel(new Vector2(cx + spread, y), "steer", (_litL + _litR) * 0.5f, 0f);
		if (_driftAlpha > 0.01f) DrawDrift(new Vector2(cx, y - 30f));
	}

	private void DrawCap(Vector2 c, string key, string label, float lit, float press, float danger)
	{
		float a = _alpha;
		float h = 20f + 3f * _urgent;
		var font = HintStyle.Font;   // (2026-10-15: the hints' face, the inventory's)
		int fs = UiKit.SmallSize + 3;
		var textSize = font.GetStringSize(key, HorizontalAlignment.Center, -1, fs);
		float w = Mathf.Max(h, textSize.X + 10f);
		var rect = new Rect2(c - new Vector2(w, h) * 0.5f + new Vector2(0, 2f * press), new Vector2(w, h));
		var on = new Color(UiKit.Eye, 0.9f).Lerp(new Color(Blood, 0.95f), danger);
		var edge = new Color(UiKit.Bone, 0.35f + 0.2f * _urgent).Lerp(new Color(Blood, 0.8f), danger * 0.8f).Lerp(on, lit);
		// the key's body: a dark cap with a lighter lower lip (depth), sinking into it when pressed
		_root.DrawRect(new Rect2(rect.Position + new Vector2(0, 3f - 2f * press), rect.Size), new Color(0, 0, 0, 0.45f * a));
		_root.DrawRect(rect, new Color(0.05f, 0.05f, 0.06f, (0.55f + 0.15f * lit) * a));
		_root.DrawRect(rect, new Color(edge, edge.A * a), false, 1f + danger);
		var textPos = new Vector2(rect.GetCenter().X - textSize.X * 0.5f, rect.GetCenter().Y + textSize.Y * 0.3f);
		var keyCol = new Color(UiKit.Bone, 0.6f * a).Lerp(new Color(on, a), Mathf.Max(lit, danger * 0.7f));
		_root.DrawString(font, textPos, key, HorizontalAlignment.Left, -1, fs, keyCol);
		if (label.Length > 0) DrawLabel(c, label, lit, danger);
	}

	private void DrawLabel(Vector2 c, string label, float lit, float danger)
	{
		var serif = HintStyle.Font;
		int ls = UiKit.SmallSize;
		var lsz = serif.GetStringSize(label, HorizontalAlignment.Center, -1, ls);
		var col = new Color(UiKit.Muted, (0.5f + 0.3f * lit) * _alpha).Lerp(new Color(Blood, 0.9f * _alpha), danger * 0.8f);
		_root.DrawString(serif, new Vector2(c.X - lsz.X * 0.5f, c.Y + 12f + 11f + 3f * _urgent), label, HorizontalAlignment.Left, -1, ls, col);
	}

	/// <summary>The rower's breath, a thin line under "row hard": bone while there's breath to spend,
	/// dull red once it ran out.</summary>
	private void DrawBreath(Vector2 c)
	{
		float a = _alpha;
		const float w = 44f;
		var back = new Rect2(c + new Vector2(-w * 0.5f, 10f), new Vector2(w, 2f));
		_root.DrawRect(back, new Color(0, 0, 0, 0.45f * a));
		var col = Spent ? new Color(Blood, 0.85f * a) : new Color(UiKit.Bone, (0.45f + 0.35f * _litHard) * a);
		_root.DrawRect(new Rect2(back.Position, new Vector2(w * _breath, 2f)), col);
	}

	/// <summary>A small triangle: up and bone while making headway, down and red while being pushed
	/// back, its size following how hard.</summary>
	private void DrawDrift(Vector2 c)
	{
		float a = _alpha * _driftAlpha;
		float v = Mathf.Clamp(_drift / 3f, -1f, 1f);
		float h = 4f + 5f * Mathf.Abs(v);
		float dir = v >= 0f ? -1f : 1f;   // screen up = forward
		var col = new Color(UiKit.Bone, 0.6f * a).Lerp(new Color(Blood, 0.9f * a), Mathf.Clamp(-v * 2f, 0f, 1f));
		var pts = new[] { c + new Vector2(0, dir * h), c + new Vector2(-h * 0.8f, -dir * h * 0.6f), c + new Vector2(h * 0.8f, -dir * h * 0.6f) };
		_root.DrawColoredPolygon(pts, col);
	}
}
