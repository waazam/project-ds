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
	[Export] public float MaxSweepDegPerSec = 120f;   // 40 lagged for seconds after the loop's hand-offs (2026-09-22)

	private float _shownBearing;
	private bool _hasBearing;
	private Vector3 _lastPlayerPos;

	private static readonly (string Label, float Deg)[] Ticks =
	{
		("N", 0), ("NE", 45), ("E", 90), ("SE", 135), ("S", 180), ("SW", 225), ("W", 270), ("NW", 315),
	};

	private static readonly Color LineColor = new(UiKit.Bone, 0.3f);
	private static readonly Color NorthColor = new(UiKit.Bone, 0.9f);

	/// <summary>For the autotest: whether the compass ribbon is currently drawn.</summary>
	public bool ShowingCompass => _draw?.Visible ?? false;
	/// <summary>For tests: how far faded in it is (0..1).</summary>
	public float Presence => _presence;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private readonly List<SilenceZone> _zones = new();
	private double _nextZoneRefresh;
	private float _jitter;
	private readonly RandomNumberGenerator _rng = new();
	private float _glitchPhase;
	// dead (Act 14's stairwell to the church's great door): the strip shows a heading that wanders on its own
	private float _deadYaw, _deadVel, _deadGoal;
	private double _deadNextSwing;
	/// <summary>For tests: the compass is dead (no heading holds, no objective).</summary>
	public bool Dead { get; private set; }
	private float _falseBearing;
	private double _nextFalseBearing;
	private double _clock;
	private float _presence = 1f;
	private double _wantedUntil;
	private bool _wasShown;
	private Vector3? _lastObjective;

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
		// shown when wanted (HudPresence): standing still a moment, a new objective, its first few seconds, or going wrong
		var obj = Systems.StoryManager.Instance?.ObjectivePosition;
		if (obj is { } o && (_lastObjective == null || _lastObjective.Value.DistanceTo(o) > 3f)) _wantedUntil = _clock + 6.0;
		_lastObjective = obj;
		if (!_wasShown) _wantedUntil = _clock + 6.0;
		_wasShown = true;
		// (2026-10-23, the owner: "after you pick it up, it disappears, should be on the top of the screen the whole time") always
		// up once carried; it faded out whenever they walked (the contextual HUD), and read as lost
		bool wanted = true;
		_presence = HudPresence.Ease(_presence, wanted, delta);
		_draw.Modulate = new Color(1, 1, 1, _presence);
		Dead = Systems.StoryManager.Instance?.CompassDead ?? false;
		if (Dead) UpdateDead((float)delta);
		UpdateShownBearing((float)delta);
		_draw.QueueRedraw();
	}

	// (2026-10-10c, the owner: "the ui for the compass needs to match the colors of the other ui elements ... the animation
	// for the compass needs to be smoothed out when the player is turning, it looks very janky") The ribbon in the HUD's
	// neon (NeonKit, the colour chosen in the settings). Its heading follows the view on a critically damped spring (fast
	// enough never to lag a turn, smooth enough that a flick of the mouse doesn't stutter it across), and nothing is snapped
	// to whole pixels any more (at the game's 640 px every tick stepped a pixel at a time as you turned).
	private float _shownYaw, _yawVel;
	private bool _yawSet;

	private float SmoothYaw(float want, float dt)
	{
		if (!_yawSet) { _shownYaw = want; _yawVel = 0f; _yawSet = true; return want; }
		float diff = Mathf.Wrap(want - _shownYaw, -180f, 180f);
		if (Mathf.Abs(diff) > 90f) { _shownYaw = want; _yawVel = 0f; return want; }   // (a teleport, a cut: no long sweep round)
		const float w = 22f;
		_yawVel += (diff * w * w - _yawVel * 2f * w) * dt;
		_shownYaw = Mathf.Wrap(_shownYaw + _yawVel * dt, -180f, 180f);
		return _shownYaw;
	}

	private void OnDraw()
	{
		if (_player == null || !IsInstanceValid(_player) || _player.CameraRig == null) return;
		var size = _draw.Size;
		float cx = size.X * 0.5f, cy = size.Y * 0.5f;
		float rawYaw = Dead ? _deadYaw : Mathf.RadToDeg(_player.CameraRig.Yaw);
		float yawDeg = SmoothYaw(rawYaw, (float)GetProcessDeltaTime());
		float jitter = Dead ? 0.35f : _jitter;
		float t = (float)_clock;
		float breath = 0.82f + 0.12f * Mathf.Sin(t * 1.7f) - (Dead ? 0.25f * Mathf.Abs(Mathf.Sin(t * 0.8f)) : 0f);

		// the baseline: a neon tube fading out at both ends
		const int segments = 14;
		for (int i = 0; i < segments; i++)
		{
			float x0 = size.X * i / segments, x1 = size.X * (i + 1) / segments;
			float mid = (i + 0.5f) / segments;
			float fade = 1f - Mathf.Pow(Mathf.Abs(mid - 0.5f) * 2f, 2f);
			NeonKit.Seg(_draw, new Vector2(x0, cy), new Vector2(x1, cy), 0.5f, 0.32f * fade * breath);
		}
		// the centre mark, a small notch over the line
		NeonKit.Line(_draw, new[] { new Vector2(cx - 2.5f, cy - 6.5f), new Vector2(cx, cy - 4f), new Vector2(cx + 2.5f, cy - 6.5f) }, 0.6f, 0.55f * breath);

		float PxFor(float deg) => cx + OffsetPx(deg, yawDeg);
		var font = _font ??= new FontVariation { BaseFont = ChapterCard.Anton ?? UiKit.Serif, SpacingGlyph = 1 };
		// every 15 degrees a tick; the cardinals and the half-winds named
		for (int d = 0; d < 360; d += 15)
		{
			float shown = d;
			if (jitter > 0.5f && _rng.Randf() < 0.02f) shown += _rng.RandfRange(-40f, 40f);   // a tick briefly lies
			float delta = Mathf.Wrap(yawDeg - shown, -180f, 180f);
			if (Mathf.Abs(delta) > VisibleDegrees * 0.5f) continue;
			float x = cx + delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f) + JitterPx(jitter, 2f);
			if (x < 0 || x > size.X) continue;
			string label = null;
			foreach (var (l, deg) in Ticks) if (Mathf.IsEqualApprox(deg, d)) label = l;
			bool major = label is "N" or "S" or "E" or "W";
			float edge = 1f - Mathf.Pow(Mathf.Abs(x - cx) / cx, 3f);
			float lit = (label == "N" ? 1f : major ? 0.8f : label != null ? 0.5f : 0.28f) * edge * breath;
			float half = major ? 3.5f : label != null ? 2.5f : 1.5f;
			NeonKit.Seg(_draw, new Vector2(x, cy - half), new Vector2(x, cy + half), 0.55f, lit);
			if (major)
				NeonKit.Text(_draw, font, new Vector2(x - 10.5f, cy - half - 1.8f), label, HorizontalAlignment.Center, 21, 8, lit);
		}

		// the objective: a neon diamond riding the line (its core filled, breathing a little brighter), or an arrow at
		// the edge when it's behind
		if (!_hasBearing) return;
		float bearing = _shownBearing;
		if (jitter > 0.05f)
		{
			if (_clock >= _nextFalseBearing) { _falseBearing = _rng.RandfRange(0f, 360f); _nextFalseBearing = _clock + Mathf.Lerp(3.0, 0.4, jitter); }
			bearing = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(bearing), Mathf.DegToRad(_falseBearing), jitter));
		}
		float bx = PxFor(bearing) + JitterPx(jitter, 6f);
		float mlit = Mathf.Lerp(1f, 0.45f, jitter) * (0.88f + 0.12f * Mathf.Sin(t * 3.1f));
		if (bx >= 4 && bx <= size.X - 4)
		{
			var dia = new[] { new Vector2(bx, cy - 4.2f), new Vector2(bx + 3.2f, cy), new Vector2(bx, cy + 4.2f), new Vector2(bx - 3.2f, cy) };
			NeonKit.Line(_draw, dia, 0.7f, mlit, true);
			_draw.DrawColoredPolygon(new[] { new Vector2(bx, cy - 2f), new Vector2(bx + 1.5f, cy), new Vector2(bx, cy + 2f), new Vector2(bx - 1.5f, cy) }, new Color(NeonKit.Core, 0.85f * mlit));
		}
		else
		{
			float ex = Mathf.Clamp(bx, 4, size.X - 4);
			float dir = bx < 4 ? -1f : 1f;
			NeonKit.Line(_draw, new[] { new Vector2(ex, cy - 3.5f), new Vector2(ex + dir * 4.5f, cy), new Vector2(ex, cy + 3.5f) }, 0.7f, mlit, true);
		}
	}

	private static FontVariation _font;

	/// <summary>The dead compass: it drifts, hunts back and forth, and now and then swings round to somewhere new
	/// and settles there, as if something else were pulling it; turning the player does nothing to it.</summary>
	private void UpdateDead(float dt)
	{
		if (_clock >= _deadNextSwing)
		{
			_deadNextSwing = _clock + _rng.RandfRange(2.5f, 7f);
			_deadGoal = _deadYaw + _rng.RandfRange(-160f, 160f);
		}
		float wander = Mathf.Sin((float)_clock * 0.7f) * 12f + Mathf.Sin((float)_clock * 1.9f + 1.3f) * 4f;
		_deadVel = Mathf.Lerp(_deadVel, (_deadGoal + wander - _deadYaw) * 1.4f, 1f - Mathf.Exp(-dt * 2.5f));
		_deadVel = Mathf.Clamp(_deadVel, -90f, 90f);
		_deadYaw = Mathf.Wrap(_deadYaw + _deadVel * dt, -180f, 180f);
		_deadGoal = _deadYaw + Mathf.Wrap(_deadGoal - _deadYaw, -180f, 180f);
	}

	private float JitterPx(float jitter, float amount) => jitter <= 0f ? 0f : Mathf.Sin(_glitchPhase * 41f + amount) * amount * jitter;

	/// <summary>
	/// Where a heading sits on the ribbon, in pixels from the centre. The camera's yaw grows turning LEFT
	/// (Godot rotates counter-clockwise about +Y, and the bearing uses the same atan2(-x, -z) space), so a
	/// heading greater than the yaw is on the player's left and must draw left of centre. Until 2026-09-22
	/// this was the other way round: the marker sat on the right when the objective was to the left, and the
	/// ticks slid the wrong way as you turned (the "broken compass").
	/// </summary>
	private float OffsetPx(float deg, float yawDeg)
	{
		float delta = Mathf.Wrap(yawDeg - deg, -180f, 180f);
		return delta / (VisibleDegrees * 0.5f) * (StripWidth * 0.5f);
	}

	/// <summary>For tests: the bearing (degrees, yaw space) the marker is drawn at right now, or null when there is none.</summary>
	public float? ShownBearing => _hasBearing ? _shownBearing : null;
	/// <summary>For tests: the marker's pixel offset from the ribbon's centre (negative = left of centre = to the player's left).</summary>
	public float? MarkerOffsetPx => _hasBearing && _player != null && IsInstanceValid(_player) && _player.CameraRig != null
		? OffsetPx(_shownBearing, Mathf.RadToDeg(_player.CameraRig.Yaw)) : null;
	/// <summary>The bearing (degrees, yaw space) from one point to another, as the compass computes it.</summary>
	public static float BearingDeg(Vector3 from, Vector3 to)
	{
		Vector3 d = to - from; d.Y = 0;
		return Mathf.Wrap(Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z)), 0f, 360f);
	}

	/// <summary>
	/// The shown bearing sweeps toward the true one at no more than <see cref="MaxSweepDegPerSec"/> while
	/// the player walks and turns (calm, never twitchy). It snaps when an objective first appears, after any
	/// teleport (a 6 m jump in one frame), and when the objective moves to a different direction altogether:
	/// the ribbon must never point at a place the story has already left (Dan, 2026-09-22).
	/// </summary>
	private void UpdateShownBearing(float delta)
	{
		var target = Systems.StoryManager.Instance?.ObjectivePosition;
		if (target == null || _player == null) { _hasBearing = false; return; }
		// A teleport (the loop's hand-offs, the fall's wake, the bunker's door) is not a walk: the marker snaps with it
		// instead of sweeping round for seconds from a place the player no longer stands.
		if (_hasBearing && _lastPlayerPos.DistanceTo(_player.GlobalPosition) > 2f) _hasBearing = false;   // any jump in one frame is a teleport
		_lastPlayerPos = _player.GlobalPosition;
		Vector3 d = target.Value - _player.GlobalPosition; d.Y = 0;
		if (d.LengthSquared() < 0.01f) return;   // standing on it: keep the last heading
		float trueDeg = Mathf.Wrap(Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z)), 0f, 360f);
		if (!_hasBearing) { _shownBearing = trueDeg; _hasBearing = true; return; }
		float diff = Mathf.Wrap(trueDeg - _shownBearing, -180f, 180f);
		// A new objective in another direction altogether (the story moved it): snap rather than sweep a half-turn.
		if (Mathf.Abs(diff) > 60f) { _shownBearing = trueDeg; return; }
		// Otherwise close the gap fast when it is big and calmly when it is small (an exponential settle plus the
		// linear sweep): within a third of a second of any change the marker is within a degree or two.
		float step = MaxSweepDegPerSec * delta + Mathf.Abs(diff) * Mathf.Min(1f, 8f * delta);
		_shownBearing = Mathf.Wrap(_shownBearing + Mathf.Clamp(diff, -step, step), 0f, 360f);
	}
}
