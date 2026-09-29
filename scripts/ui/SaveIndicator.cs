using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The owner: when the game saves, a translucent grey-white roll of film, drawn in the UI's own style,
/// shows in the corner and slowly brightens and darkens in contrast, three seconds at most. A 35mm
/// canister (body, the spool's knob on top, the lip) with its leader strip, sprocket holes and all,
/// running out of its side, winding into it while the save runs (the spool's knob turning, the holes running
/// in, the leader drawing shorter). Every save (checkpoints and flags) plays it from the start again.
/// </summary>
public partial class SaveIndicator : CanvasLayer
{
	public static SaveIndicator Instance { get; private set; }

	/// <summary>How long it shows, all in (the owner: 3 seconds max).</summary>
	public const float Seconds = 2.8f;

	/// <summary>Showing now (tests read it).</summary>
	public bool Showing => _t >= 0f && _t < Seconds;
	public int Shown { get; private set; }

	private Control _draw;
	private float _t = -1f;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 65;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_draw.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		// small, grey and faint (the owner: it was too big and too bright): about half the size it was, up in
		// the top bar's corner, where it sits on the black
		_draw.Size = new Vector2(60, 44);
		_draw.Scale = Vector2.One * 0.5f;
		_draw.Position = new Vector2(-44, 6);
		_draw.Draw += Paint;
		AddChild(_draw);
		Callable.From(() => { if (StoryManager.Instance != null) StoryManager.Instance.Saved += Play; }).CallDeferred();
	}

	public void Play()
	{
		_t = 0f;
		Shown++;
		_draw.Visible = true;
		_draw.QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_t < 0f) return;
		_t += (float)delta;
		if (_t >= Seconds) { _t = -1f; _draw.Visible = false; return; }
		_draw.QueueRedraw();
	}

	private void Paint()
	{
		if (_t < 0f) return;
		// in and out softly; between, a slow swell of contrast (about one and a half breaths: never a flicker)
		float fade = Mathf.Min(Mathf.Clamp(_t / 0.35f, 0f, 1f), Mathf.Clamp((Seconds - _t) / 0.5f, 0f, 1f));
		float breath = 0.5f - 0.5f * Mathf.Cos(_t * Mathf.Tau * 0.6f);   // 0 dark .. 1 bright
		float light = Mathf.Lerp(0.42f, 0.66f, breath), dark = Mathf.Lerp(0.26f, 0.16f, breath);
		float a = fade * 0.42f;
		Color body = new(light, light, light * 0.98f, a);
		Color ink = new(dark, dark, dark, a);
		Color rim = new(light * 0.8f, light * 0.8f, light * 0.78f, a);
		var c = _draw;
		// the leader strip, out of the right side of the canister: film grey, a row of holes top and bottom. It winds
		// in as the save runs (the owner): the holes run steadily into the canister and the leader shortens, as if
		// the spool were being turned
		float wind = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_t / (Seconds - 0.3f), 0f, 1f));
		float len = Mathf.Lerp(30f, 12f, wind);
		float travel = _t * 12f + wind * 18f;   // how far the film has moved in (px): the holes keep pace with the leader
		var strip = new Rect2(24, 12, len, 20);
		Color film = new(light * 0.78f, light * 0.78f, light * 0.76f, a * 0.85f);
		c.DrawRect(strip, film);
		const float pitch = 6.5f;
		float first = strip.Position.X + 5f - Mathf.PosMod(travel, pitch) - pitch;
		for (float x = first; x < strip.End.X; x += pitch)
		{
			// only whole holes, clear of the canister's mouth and the cut end
			if (x < strip.Position.X + 1.5f || x + 3.2f > strip.End.X - 1f) continue;
			c.DrawRect(new Rect2(x, strip.Position.Y + 2, 3.2f, 3.2f), ink);
			c.DrawRect(new Rect2(x, strip.End.Y - 5.2f, 3.2f, 3.2f), ink);
		}
		// the cut end of the leader, angled
		c.DrawColoredPolygon(new[] { new Vector2(strip.End.X, strip.Position.Y), new Vector2(strip.End.X + 5, strip.Position.Y + 6), new Vector2(strip.End.X + 5, strip.End.Y), new Vector2(strip.End.X, strip.End.Y) }, film);
		// the canister: a rounded body, a darker lip top and bottom, the spool's knob on top
		c.DrawRect(new Rect2(12, 3, 7, 5), ink);                 // the knob
		c.DrawRect(new Rect2(13.5f, 1, 4, 3), ink);
		// the knob turning with the spool: a notch going round (a slow turn, about one a second)
		float turn = travel / 18f * Mathf.Tau;
		float nx = 15.5f + 2.6f * Mathf.Sin(turn);
		if (Mathf.Cos(turn) > -0.2f) c.DrawRect(new Rect2(nx - 0.6f, 3.5f, 1.2f, 4f), new Color(light * 0.9f, light * 0.9f, light * 0.88f, a * 0.8f));
		c.DrawRect(new Rect2(5, 7, 21, 3), rim);                   // the top lip
		c.DrawRect(new Rect2(6, 10, 19, 26), body);                // the body
		c.DrawRect(new Rect2(5, 36, 21, 3), rim);                  // the bottom lip
		// its label: a band, and a few marks like print
		c.DrawRect(new Rect2(6, 15, 19, 14), new Color(dark, dark, dark, a * 0.55f));
		c.DrawRect(new Rect2(9, 18, 10, 2), body);
		c.DrawRect(new Rect2(9, 22, 13, 1.5f), body);
		c.DrawRect(new Rect2(9, 25, 7, 1.5f), body);
		// the mouth the film runs into: the felt light-trap, a dark slot down the body's right edge
		c.DrawRect(new Rect2(23.5f, 11, 1.8f, 22), new Color(dark * 0.7f, dark * 0.7f, dark * 0.7f, a));
		// a glint down the body's edge
		c.DrawRect(new Rect2(8, 10, 1.5f, 26), new Color(1, 1, 1, a * 0.35f * breath));
	}
}
