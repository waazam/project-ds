using System.Collections.Generic;
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
///
/// Lookups are cached: the player and inventory once (re-acquired only if
/// freed), the silence zones refreshed once a second, and the distortion is
/// computed once per frame in _Process rather than in the draw.
/// </summary>
public partial class Compass : CanvasLayer
{
	[Export] public float StripWidth = 220f;
	[Export] public float StripHeight = 13f;
	/// <summary>Total degrees of heading shown across the strip.</summary>
	[Export] public float VisibleDegrees = 140f;
	/// <summary>Fastest the objective marker may sweep around the ribbon, in degrees per second.</summary>
	[Export] public float MaxSweepDegPerSec = 40f;

	private float _shownBearing;
	private bool _hasBearing;

	private static readonly (string Label, float Deg)[] Ticks =
	{
		("N", 0), ("NE", 45), ("E", 90), ("SE", 135), ("S", 180), ("SW", 225), ("W", 270), ("NW", 315),
	};

	private static readonly Color LineColor = new(UiKit.Bone, 0.3f);
	private static readonly Color NorthColor = new(UiKit.Bone, 0.9f);

	/// <summary>For the autotest: whether the compass ribbon is currently drawn.</summary>
	public bool ShowingCompass => _draw?.Visible ?? false;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private readonly List<SilenceZone> _zones = new();
	private double _nextZoneRefresh;
	private float _jitter;
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
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_inv = _player?.GetNodeOrNull<PlayerInventory>("Inventory");
		}
		bool show = _inv != null && IsInstanceValid(_inv) && _inv.HasCompass;
		_draw.Visible = show;
		if (!show) return;

		if (_clock >= _nextZoneRefresh)
		{
			_nextZoneRefresh = _clock + 1.0;
			_zones.Clear();
			foreach (var node in GetTree().GetNodesInGroup("silence_zones"))
				if (node is SilenceZone zone) _zones.Add(zone);
		}
		// Distance to the stairs specifically (not the storm's forced silence, which would peg this at max).
		float distortion = 0f;
		foreach (var zone in _zones)
			if (IsInstanceValid(zone)) distortion = Mathf.Max(distortion, zone.SilenceAt(_player.GlobalPosition));
		_jitter = distortion > 0.15f ? (distortion - 0.15f) / 0.85f : 0f;

		_glitchPhase += (float)delta;
		UpdateShownBearing((float)delta);
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		if (_player == null || !IsInstanceValid(_player) || _player.CameraRig == null) return;
		var size = _draw.Size;
		float cx = size.X * 0.5f, cy = size.Y * 0.5f;
		float yawDeg = Mathf.RadToDeg(_player.CameraRig.Yaw);
		float jitter = _jitter;

		// A thin baseline, Skyrim-style but fainter — no backing bar, just the line and its ticks,
		// fading out at both ends.
		const int segments = 8;
		for (int i = 0; i < segments; i++)
		{
			float x0 = size.X * i / segments, x1 = size.X * (i + 1) / segments;
			float mid = (i + 0.5f) / segments;
			float fade = 1f - Mathf.Pow(Mathf.Abs(mid - 0.5f) * 2f, 2f);
			_draw.DrawLine(new Vector2(x0, cy), new Vector2(x1, cy), new Color(LineColor, LineColor.A * fade), 1f);
		}

		float PxFor(float deg)
		{
			float delta = Mathf.Wrap(deg - yawDeg, -180f, 180f);
			return cx + delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f);
		}

		var font = UiKit.Serif;
		foreach (var (label, deg) in Ticks)
		{
			float shown = deg;
			if (jitter > 0.5f && _rng.Randf() < 0.02f) shown += _rng.RandfRange(-40f, 40f);   // a tick briefly lies
			float delta = Mathf.Wrap(shown - yawDeg, -180f, 180f);
			if (Mathf.Abs(delta) > VisibleDegrees * 0.5f) continue;
			float x = cx + delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f) + JitterPx(jitter, 2f);
			if (x < 0 || x > size.X) continue;
			bool major = label is "N" or "S" or "E" or "W";
			float edge = 1f - Mathf.Pow(Mathf.Abs(x - cx) / cx, 3f);   // fade toward the ends
			var col = label == "N" ? NorthColor : new Color(UiKit.Bone, major ? 0.7f : 0.38f);
			col.A *= edge;
			float half = major ? 3.5f : 2f;
			_draw.DrawLine(new Vector2(Mathf.Round(x) + 0.5f, cy - half), new Vector2(Mathf.Round(x) + 0.5f, cy + half), col, 1f);
			if (major)
			{
				var p = new Vector2(Mathf.Round(x) - 10f, cy - half - 1.5f);
				_draw.DrawString(font, p + new Vector2(1, 1), label, HorizontalAlignment.Center, 21, 8, new Color(0, 0, 0, 0.5f * edge));
				_draw.DrawString(font, p, label, HorizontalAlignment.Center, 21, 8, col);
			}
		}

		// Objective marker: a small eye-yellow diamond riding the line, or an arrow pinned to the edge if it's behind us.
		if (!_hasBearing) return;
		float bearing = _shownBearing;
		if (jitter > 0.05f)
		{
			if (_clock >= _nextFalseBearing) { _falseBearing = _rng.RandfRange(0f, 360f); _nextFalseBearing = _clock + Mathf.Lerp(3.0, 0.4, jitter); }
			bearing = Mathf.LerpAngle(Mathf.DegToRad(bearing), Mathf.DegToRad(_falseBearing), jitter);
			bearing = Mathf.RadToDeg(bearing);
		}
		float bx = PxFor(bearing) + JitterPx(jitter, 6f);
		var markColor = new Color(UiKit.Eye, Mathf.Lerp(0.9f, 0.4f, jitter));
		if (bx >= 4 && bx <= size.X - 4)
			_draw.DrawColoredPolygon(new[] { new Vector2(bx, cy - 4f), new Vector2(bx + 3f, cy), new Vector2(bx, cy + 4f), new Vector2(bx - 3f, cy) }, markColor);
		else
		{
			float ex = Mathf.Clamp(bx, 4, size.X - 4);
			float dir = bx < 4 ? -1f : 1f;
			_draw.DrawColoredPolygon(new[] { new Vector2(ex, cy - 3.5f), new Vector2(ex + dir * 4.5f, cy), new Vector2(ex, cy + 3.5f) }, markColor);
		}
	}

	private float JitterPx(float jitter, float amount) => jitter <= 0f ? 0f : Mathf.Sin(_glitchPhase * 41f + amount) * amount * jitter;

	/// <summary>
	/// The marker never snaps. When the objective changes (or the player moves), the shown bearing
	/// sweeps toward the true one at no more than <see cref="MaxSweepDegPerSec"/>: a full about-face
	/// takes a few seconds to read, which is calm rather than disorienting. It snaps only when an
	/// objective first appears after there was none.
	/// </summary>
	private void UpdateShownBearing(float delta)
	{
		var target = Systems.StoryManager.Instance?.ObjectivePosition;
		if (target == null || _player == null) { _hasBearing = false; return; }
		Vector3 d = target.Value - _player.GlobalPosition; d.Y = 0;
		if (d.LengthSquared() < 0.01f) return;   // standing on it: keep the last heading
		float trueDeg = Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z));
		if (!_hasBearing) { _shownBearing = trueDeg; _hasBearing = true; return; }
		float diff = Mathf.Wrap(trueDeg - _shownBearing, -180f, 180f);
		float step = MaxSweepDegPerSec * delta;
		_shownBearing = Mathf.Wrap(_shownBearing + Mathf.Clamp(diff, -step, step), 0f, 360f);
	}
}
