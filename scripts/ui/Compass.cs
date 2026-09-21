using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// A thin, translucent compass ribbon across the top of the screen, shown
/// once the player has picked up the compass. Ticks scroll with the camera's
/// yaw; a diamond marks the bearing to <see cref="ProjectDS.Systems.StoryManager"/>'s
/// current objective. Near-total forest silence (the stairs' own signal) makes
/// it glitch: the marker jitters and occasionally jumps to a false bearing.
/// </summary>
public partial class Compass : CanvasLayer
{
	[Export] public float StripWidth = 220f;
	[Export] public float StripHeight = 13f;
	/// <summary>Total degrees of heading shown across the strip.</summary>
	[Export] public float VisibleDegrees = 140f;

	private static readonly (string Label, float Deg)[] Ticks =
	{
		("N", 0), ("NE", 45), ("E", 90), ("SE", 135), ("S", 180), ("SW", 225), ("W", 270), ("NW", 315),
	};

	/// <summary>For the autotest: whether the compass ribbon is currently drawn.</summary>
	public bool ShowingCompass => _draw?.Visible ?? false;

	private Control _draw;
	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();
	private float _glitchPhase;
	private float _falseBearing;
	private double _nextFalseBearing;
	private double _clock;

	public override void _Ready()
	{
		Layer = 12;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, CustomMinimumSize = new Vector2(StripWidth, StripHeight) };
		_draw.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_draw.OffsetLeft = -StripWidth * 0.5f; _draw.OffsetRight = StripWidth * 0.5f;
		_draw.OffsetTop = 8; _draw.OffsetBottom = 8 + StripHeight;
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var inv = _player?.GetNodeOrNull<PlayerInventory>("Inventory");
		bool show = inv is { HasCompass: true };
		_draw.Visible = show;
		if (!show) return;
		_glitchPhase += (float)delta;
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		var size = _draw.Size;
		float cx = size.X * 0.5f, cy = size.Y * 0.5f;
		float yawDeg = Mathf.RadToDeg(_player.CameraRig.Yaw);
		// Distance to the stairs specifically (not the storm's forced silence, which would peg this at max).
		float distortion = 0f;
		foreach (var node in GetTree().GetNodesInGroup("silence_zones"))
			if (node is SilenceZone zone) distortion = Mathf.Max(distortion, zone.SilenceAt(_player.GlobalPosition));
		float jitter = distortion > 0.15f ? (distortion - 0.15f) / 0.85f : 0f;

		// A thin baseline, Skyrim-style — no solid backing bar, just the line and its ticks.
		_draw.DrawLine(new Vector2(0, cy), new Vector2(size.X, cy), new Color(0.85f, 0.85f, 0.8f, 0.4f), 1f);

		float PxFor(float deg)
		{
			float delta = Mathf.Wrap(deg - yawDeg, -180f, 180f);
			return cx + delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f);
		}

		foreach (var (label, deg) in Ticks)
		{
			float shown = deg;
			if (jitter > 0.5f && _rng.Randf() < 0.02f) shown += _rng.RandfRange(-40f, 40f);   // a tick briefly lies
			float delta = Mathf.Wrap(shown - yawDeg, -180f, 180f);
			if (Mathf.Abs(delta) > VisibleDegrees * 0.5f) continue;
			float x = cx + delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f) + JitterPx(jitter, 2f);
			if (x < 0 || x > size.X) continue;
			bool major = label is "N" or "S" or "E" or "W";
			var col = label == "N" ? new Color(0.85f, 0.8f, 0.6f, 0.95f) : new Color(0.8f, 0.8f, 0.76f, major ? 0.75f : 0.45f);
			float half = major ? 4f : 2.5f;
			_draw.DrawLine(new Vector2(x, cy - half), new Vector2(x, cy + half), col, 1f);
			if (major)
				_draw.DrawString(ThemeDB.FallbackFont, new Vector2(x - 4, cy - half - 2), label, HorizontalAlignment.Center, -1, 7, col);
		}

		// Objective marker: a small diamond riding the line, or an arrow pinned to the edge if it's behind us.
		float bearing = ObjectiveBearingDeg();
		if (jitter > 0.05f)
		{
			if (_clock >= _nextFalseBearing) { _falseBearing = _rng.RandfRange(0f, 360f); _nextFalseBearing = _clock + Mathf.Lerp(3.0, 0.4, jitter); }
			bearing = Mathf.LerpAngle(Mathf.DegToRad(bearing), Mathf.DegToRad(_falseBearing), jitter);
			bearing = Mathf.RadToDeg(bearing);
		}
		float bx = PxFor(bearing) + JitterPx(jitter, 6f);
		var markColor = new Color(0.9f, 0.75f, 0.3f, Mathf.Lerp(0.95f, 0.4f, jitter));
		if (bx >= 4 && bx <= size.X - 4)
			_draw.DrawColoredPolygon(new[] { new Vector2(bx, cy - 4.5f), new Vector2(bx + 3.5f, cy), new Vector2(bx, cy + 4.5f), new Vector2(bx - 3.5f, cy) }, markColor);
		else
		{
			float ex = Mathf.Clamp(bx, 4, size.X - 4);
			float dir = bx < 4 ? -1f : 1f;
			_draw.DrawColoredPolygon(new[] { new Vector2(ex, cy - 4f), new Vector2(ex + dir * 5f, cy), new Vector2(ex, cy + 4f) }, markColor);
		}
	}

	private float JitterPx(float jitter, float amount) => jitter <= 0f ? 0f : Mathf.Sin(_glitchPhase * 41f + amount) * amount * jitter;

	private float ObjectiveBearingDeg()
	{
		var target = Systems.StoryManager.Instance?.ObjectivePosition;
		if (target == null || _player == null) return 0f;
		Vector3 d = target.Value - _player.GlobalPosition; d.Y = 0;
		if (d.LengthSquared() < 0.01f) return 0f;
		return Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z));
	}
}
