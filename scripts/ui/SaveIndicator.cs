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
		// (2026-10-24: a touch bigger than it was, the painted canister wants its detail seen; inside the top bar)
		_draw.Size = new Vector2(84, 60);
		_draw.Scale = Vector2.One * 0.52f;
		_draw.Position = new Vector2(-50, 2);
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

	private static Texture2D _canister;

	private void Paint()
	{
		if (_t < 0f) return;
		// (2026-10-24, the owner: "make the saving icon ... look weathered, old and faded and sort of cracked inside the
		// green", with photos of old film canisters and tins) the canister is painted now (tools/Textures/make_save_icon.py:
		// dull tarnished tin, rust on the rims, worn black caps, a faded green label crazed with fine cracks and flaked in
		// patches, its print worn half off); the leader, scuffed and faded too, still winds into it while the save runs.
		// In and out softly; never a flicker.
		float fade = Mathf.Min(Mathf.Clamp(_t / 0.35f, 0f, 1f), Mathf.Clamp((Seconds - _t) / 0.5f, 0f, 1f));
		float a = fade * 0.95f;
		var c = _draw;
		float wind = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_t / (Seconds - 0.3f), 0f, 1f));
		float len = Mathf.Lerp(34f, 10f, wind);
		float travel = _t * 12f + wind * 18f;
		// the leader: an old, faded amber-brown, darker and scuffed at its edges, the holes punched through
		var strip = new Rect2(34f, 17, len + 5f, 21);
		var amber = new Color(0.48f, 0.27f, 0.12f, a * 0.85f);
		c.DrawRect(strip, amber);
		c.DrawRect(new Rect2(strip.Position, new Vector2(strip.Size.X, 2)), new Color(0.26f, 0.14f, 0.06f, a));
		c.DrawRect(new Rect2(strip.Position + new Vector2(0, strip.Size.Y - 2), new Vector2(strip.Size.X, 2)), new Color(0.26f, 0.14f, 0.06f, a));
		for (int k = 0; k < 5; k++)   // (scratches down its length, travelling with it)
		{
			float sx = strip.Position.X + Mathf.PosMod(k * 9.7f - travel * 0.9f, Mathf.Max(strip.Size.X, 1f));
			c.DrawLine(new Vector2(sx, strip.Position.Y + 3 + k * 3.1f), new Vector2(Mathf.Min(sx + 6f, strip.End.X), strip.Position.Y + 3.6f + k * 3.1f), new Color(0.75f, 0.55f, 0.32f, a * 0.35f), 0.6f);
		}
		const float pitch = 6.5f;
		float first = strip.Position.X + 5f - Mathf.PosMod(travel, pitch) - pitch;
		for (float x = first; x < strip.End.X; x += pitch)
		{
			if (x < strip.Position.X + 1.5f || x + 3.2f > strip.End.X - 1f) continue;
			c.DrawRect(new Rect2(x, strip.Position.Y + 2.6f, 3.2f, 3f), new Color(0.02f, 0.015f, 0.01f, a));
			c.DrawRect(new Rect2(x, strip.End.Y - 5.6f, 3.2f, 3f), new Color(0.02f, 0.015f, 0.01f, a));
		}
		c.DrawColoredPolygon(new[] { new Vector2(strip.End.X, strip.Position.Y), new Vector2(strip.End.X + 5, strip.Position.Y + 7), new Vector2(strip.End.X + 5, strip.End.Y), new Vector2(strip.End.X, strip.End.Y) }, amber);
		// the canister over the leader's root, a slow dull glint going down its tin (it's too old to shine)
		_canister ??= ResourceLoader.Exists("res://assets/ui/save_canister.png") ? GD.Load<Texture2D>("res://assets/ui/save_canister.png") : null;
		var body = new Rect2(4, 0, 40, 58);
		if (_canister != null) c.DrawTextureRect(_canister, body, false, new Color(1, 1, 1, a));
		float gy = body.Position.Y + 8 + Mathf.PosMod(_t * 16f, body.Size.Y + 8f) - 8f;
		if (gy > body.Position.Y + 9 && gy < body.End.Y - 8) c.DrawRect(new Rect2(body.Position.X + 9, gy, 7, 2f), new Color(1f, 0.95f, 0.85f, a * 0.12f));
		// the knob turning with the spool (a notch going round on its top)
		float turn = travel / 18f * Mathf.Tau;
		float nx = body.Position.X + 16.5f + 3.2f * Mathf.Sin(turn);
		if (Mathf.Cos(turn) > -0.2f) c.DrawRect(new Rect2(nx - 0.6f, body.Position.Y + 2.2f, 1.2f, 3f), new Color(0.55f, 0.52f, 0.46f, a * 0.8f));
	}
}
