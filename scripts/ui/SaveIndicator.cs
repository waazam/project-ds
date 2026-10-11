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
		// small, up in the top bar's corner, where it sits on the black (a touch larger than it was: it's drawn finely now)
		_draw.Size = new Vector2(60, 44);
		_draw.Scale = Vector2.One * 0.62f;
		_draw.Position = new Vector2(-50, 4);
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
		// (2026-10-10c, the owner: "we need the saving animated icon to not be gray and be more realistic") a real 35mm
		// canister now: brushed metal shaded round its curve, a printed label (a film stock's yellow and black, no maker),
		// the knob and the lip catching the light, and the film's leader, amber and translucent, its sprocket holes,
		// winding in while the save runs. In and out softly; a slow glint travels down the metal (never a flicker).
		float fade = Mathf.Min(Mathf.Clamp(_t / 0.35f, 0f, 1f), Mathf.Clamp((Seconds - _t) / 0.5f, 0f, 1f));
		float a = fade * 0.92f;
		var c = _draw;
		float wind = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_t / (Seconds - 0.3f), 0f, 1f));
		float len = Mathf.Lerp(30f, 12f, wind);
		float travel = _t * 12f + wind * 18f;
		// the leader: amber-brown base, darker at its edges, the holes punched through (they show the black behind)
		var strip = new Rect2(24, 12, len, 20);
		var amber = new Color(0.55f, 0.27f, 0.08f, a * 0.85f);
		c.DrawRect(strip, amber);
		c.DrawRect(new Rect2(strip.Position, new Vector2(strip.Size.X, 2)), new Color(0.32f, 0.14f, 0.04f, a));
		c.DrawRect(new Rect2(strip.Position + new Vector2(0, strip.Size.Y - 2), new Vector2(strip.Size.X, 2)), new Color(0.32f, 0.14f, 0.04f, a));
		c.DrawRect(new Rect2(strip.Position + new Vector2(0, 7), new Vector2(strip.Size.X, 1)), new Color(0.85f, 0.5f, 0.2f, a * 0.35f));   // a sheen across it
		const float pitch = 6.5f;
		float first = strip.Position.X + 5f - Mathf.PosMod(travel, pitch) - pitch;
		for (float x = first; x < strip.End.X; x += pitch)
		{
			if (x < strip.Position.X + 1.5f || x + 3.2f > strip.End.X - 1f) continue;
			c.DrawRect(new Rect2(x, strip.Position.Y + 2.4f, 3.2f, 2.8f), new Color(0.02f, 0.015f, 0.01f, a));
			c.DrawRect(new Rect2(x, strip.End.Y - 5.2f, 3.2f, 2.8f), new Color(0.02f, 0.015f, 0.01f, a));
		}
		c.DrawColoredPolygon(new[] { new Vector2(strip.End.X, strip.Position.Y), new Vector2(strip.End.X + 5, strip.Position.Y + 6), new Vector2(strip.End.X + 5, strip.End.Y), new Vector2(strip.End.X, strip.End.Y) }, amber);
		// the body: metal shaded across its curve, column by column (dark at the edges, a highlight a third in)
		void Cyl(float x0, float y0, float w, float h, Color tint, float glint)
		{
			int n = Mathf.Max(4, Mathf.RoundToInt(w));
			for (int k = 0; k < n; k++)
			{
				float u = (k + 0.5f) / n;
				float shade = 0.45f + 0.55f * Mathf.Sin(u * Mathf.Pi);
				float hl = Mathf.Exp(-Mathf.Pow((u - 0.32f) / 0.08f, 2f)) * 0.55f;
				var col = new Color(tint.R * shade + hl, tint.G * shade + hl, tint.B * shade + hl, a);
				c.DrawRect(new Rect2(x0 + w * k / n, y0, w / n + 0.05f, h), col);
			}
			// the slow glint running down it
			float gy = y0 + Mathf.PosMod(_t * 14f, h + 10f) - 5f;
			if (glint > 0f && gy > y0 && gy < y0 + h) c.DrawRect(new Rect2(x0 + w * 0.22f, gy, w * 0.2f, 2.5f), new Color(1, 1, 1, a * 0.25f * glint));
		}
		var metal = new Color(0.62f, 0.62f, 0.6f);
		// the knob, turning with the spool (a notch going round)
		Cyl(12, 2.5f, 7, 5, new Color(0.32f, 0.31f, 0.3f), 0f);
		Cyl(13.5f, 0.5f, 4, 3, new Color(0.38f, 0.37f, 0.35f), 0f);
		float turn = travel / 18f * Mathf.Tau;
		float nx = 15.5f + 2.6f * Mathf.Sin(turn);
		if (Mathf.Cos(turn) > -0.2f) c.DrawRect(new Rect2(nx - 0.6f, 3f, 1.2f, 4f), new Color(0.1f, 0.1f, 0.1f, a));
		Cyl(5, 7, 21, 3, metal, 0f);                                // the top lip
		Cyl(6, 10, 19, 26, metal, 1f);                              // the body
		Cyl(5, 36, 21, 3, metal, 0f);                               // the bottom lip
		// the label: a film stock's yellow, a black band, a red stripe, a few marks like the print on it
		Cyl(6, 13, 19, 20, new Color(0.95f, 0.72f, 0.12f), 0.6f);
		Cyl(6, 20, 19, 6, new Color(0.08f, 0.07f, 0.06f), 0f);
		Cyl(6, 27, 19, 2, new Color(0.75f, 0.12f, 0.08f), 0f);
		var print = new Color(0.95f, 0.85f, 0.6f, a);
		c.DrawRect(new Rect2(9, 22, 9, 1.2f), print);      // the stock's name, a line of small print
		c.DrawRect(new Rect2(9, 24, 6, 0.9f), print);
		c.DrawRect(new Rect2(19, 21.5f, 3, 3.5f), new Color(0.95f, 0.72f, 0.12f, a));   // a little yellow box: its speed
		c.DrawRect(new Rect2(9, 16, 12, 1.5f), new Color(0.2f, 0.12f, 0.04f, a * 0.8f));
		// the light-trap the film runs into: black felt down its right edge
		c.DrawRect(new Rect2(23.5f, 11, 1.8f, 22), new Color(0.03f, 0.03f, 0.03f, a));
		// outlined in the HUD's neon (the inventory's and the compass's colour), breathing with the save
		float glow = fade * (0.55f + 0.25f * Mathf.Sin(_t * 4f));
		NeonKit.Line(c, NeonKit.Box(new Vector2(4.2f, 6.2f), new Vector2(22.6f, 33.6f), 1.5f), 0.7f, glow, true);
		NeonKit.Line(c, new[] { new Vector2(12, 6.2f), new Vector2(12, 2), new Vector2(19, 2), new Vector2(19, 6.2f) }, 0.6f, glow * 0.8f);
	}
}
