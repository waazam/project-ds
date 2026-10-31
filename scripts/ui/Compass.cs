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
///
/// (2026-10-25, the owner, with Oblivion's framed bar as the example: "make our own custom model that fits the theme of the
/// game for the compass ... smooth out the animations in the compass") Now an instrument of its own: a band of old dark
/// leather in a tarnished brass rim, drawn out to points at both ends, a brass lubber pointer hanging over its middle
/// (assets/ui/hud, painted by tools/Textures/make_hud.py). Its marks are engraved into the leather, bone-coloured with a
/// dark line under each; north in red enamel. The objective keeps the HUD's neon (the colour setting), the one lit thing
/// on it. The heading and the marker are eased once a frame in _Process (the draw only reads them), the marks fade into
/// the band's ends instead of popping, and the bearing's marker slides on its own spring.
/// </summary>
public partial class Compass : CanvasLayer
{
	[Export] public float StripWidth = 260f;
	[Export] public float StripHeight = 17f;
	/// <summary>The band's inside, where the marks run (the painting's leather, inside the rim and short of the points).</summary>
	private const float BandInset = 13f, BandTop = 3.3f, BandBottom = 13.7f;
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
	// (2026-10-24, the openings' pass) the first time it's in hand at R.H.'s camp, the needle swings off into the trees,
	// holds there a moment, trembling, then comes round to the cabin
	public const string TwitchFlag = "act3_compass_twitched";
	[Export] public float TwitchDegrees = 65f, TwitchSeconds = 2.6f;
	private double _twitchAt = -1;
	private bool _twitchChecked;
	/// <summary>For tests: the swing off into the trees has happened (or is happening), and how far off it is now (deg).</summary>
	public bool Twitched { get; private set; }
	public float TwitchNow { get; private set; }

	public override void _Ready()
	{
		Layer = 12;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, CustomMinimumSize = new Vector2(StripWidth, StripHeight) };
		_draw.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_draw.OffsetLeft = -StripWidth * 0.5f; _draw.OffsetRight = StripWidth * 0.5f;
		_draw.OffsetTop = 5; _draw.OffsetBottom = 5 + StripHeight;
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
		// (2026-10-31, the owner: "make the compass stop appearing on top after we descend the stairwell and get to the red light
		// green light section ... pop back up when the player leaves the church in act 21") gone from the hallway to the snow
		bool hidden = Systems.StoryManager.Instance is { } sm0 && sm0.Current >= Systems.Checkpoint.Act14Finished && sm0.Current < Systems.Checkpoint.Act21Finished;
		bool show = _inv != null && IsInstanceValid(_inv) && _inv.HasCompass && !hidden;
		_draw.Visible = show;
		Dead = Systems.StoryManager.Instance?.CompassDead ?? false;   // (kept up to date even while it's put away)
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
		if (!_twitchChecked && Systems.StoryManager.Instance is { } st)
		{
			_twitchChecked = true;
			if (st.Current == Systems.Checkpoint.Act3DoorBoarded && !st.HasFlag(TwitchFlag)) { _twitchAt = _clock + 1.6; st.SetFlag(TwitchFlag); }
		}
		_wasShown = true;
		// (2026-10-23, the owner: "after you pick it up, it disappears, should be on the top of the screen the whole time") always
		// up once carried; it faded out whenever they walked (the contextual HUD), and read as lost
		bool wanted = true;
		_presence = HudPresence.Ease(_presence, wanted, delta);
		_draw.Modulate = new Color(1, 1, 1, _presence);
		if (Dead) UpdateDead((float)delta);
		UpdateShownBearing((float)delta);
		// the heading, eased here once a frame (it was eased in the draw, with whatever delta the draw happened to see)
		if (_player.CameraRig != null) _drawYaw = SmoothYaw(Dead ? _deadYaw : Mathf.RadToDeg(_player.CameraRig.Yaw), (float)delta);
		// the marker on its own soft spring after the bearing (no stepping when the bearing settles in jumps)
		if (_hasBearing)
		{
			if (!_markSet) { _markDeg = _shownBearing; _markVel = 0f; _markSet = true; }
			float md = Mathf.Wrap(_shownBearing - _markDeg, -180f, 180f);
			if (Mathf.Abs(md) > 60f) { _markDeg = _shownBearing; _markVel = 0f; }
			else
			{
				const float mw = 14f;
				_markVel += (md * mw * mw - _markVel * 2f * mw) * (float)delta;
				_markDeg = Mathf.Wrap(_markDeg + _markVel * (float)delta, 0f, 360f);
			}
		}
		else _markSet = false;
		_markAlpha = Mathf.MoveToward(_markAlpha, _hasBearing ? 1f : 0f, (float)delta * 3f);
		_draw.QueueRedraw();
	}

	// (2026-10-10c, the owner: "the ui for the compass needs to match the colors of the other ui elements ... the animation
	// for the compass needs to be smoothed out when the player is turning, it looks very janky") The ribbon in the HUD's
	// neon (NeonKit, the colour chosen in the settings). Its heading follows the view on a critically damped spring (fast
	// enough never to lag a turn, smooth enough that a flick of the mouse doesn't stutter it across), and nothing is snapped
	// to whole pixels any more (at the game's 640 px every tick stepped a pixel at a time as you turned).
	private float _shownYaw, _yawVel, _drawYaw;
	private bool _yawSet;
	private float _markDeg, _markVel, _markAlpha;
	private bool _markSet;
	private static Texture2D _barTex, _pointerTex;
	private static bool _barFrost;

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

	private static readonly Color Engrave = new(0.86f, 0.79f, 0.62f);
	private static readonly Color EngraveShadow = new(0f, 0f, 0f, 0.7f);
	// (2026-10-31, the owner: north "green instead of red" and the marker "Purple", the green a nebula's)
	private static readonly Color NorthGreen = new(0.36f, 0.95f, 0.46f);
	private static readonly Color MarkCore = new(0.9f, 0.78f, 1f), MarkGlow = new(0.62f, 0.25f, 1f);

	/// <summary>A purple tube: the quest marker's (its own colour now, whatever the HUD's).</summary>
	private void Purple(Vector2[] pts, float width, float lit, bool closed = false)
	{
		if (closed) { var p = new Vector2[pts.Length + 1]; pts.CopyTo(p, 0); p[^1] = pts[0]; pts = p; }
		_draw.DrawPolyline(pts, new Color(MarkGlow, 0.35f * lit), width * 3f, true);
		_draw.DrawPolyline(pts, new Color(MarkCore, 0.97f * lit), width, true);
	}

	private void OnDraw()
	{
		if (_player == null || !IsInstanceValid(_player) || _player.CameraRig == null) return;
		var size = _draw.Size;
		float cx = size.X * 0.5f, cy = (BandTop + BandBottom) * 0.5f;
		float yawDeg = _drawYaw;
		float jitter = Dead ? 0.35f : _jitter;
		float t = (float)_clock;

		// the instrument itself
		// (2026-10-31, the owner: "give it a tree bark texture ... some of that mossy green texture on the bottom ... a frosted
		// effect" in the snow) a length of mossy oak branch; frosted from the church's door to the maze's end
		bool frost = Systems.StoryManager.Instance is { } sm1 && sm1.Current >= Systems.Checkpoint.Act21Finished && sm1.Current < Systems.Checkpoint.Act24Finished;
		if (_barFrost != frost || _barTex == null)
		{
			_barFrost = frost;
			string bar = frost ? "compass_bark_frost" : "compass_bark";
			_barTex = ResourceLoader.Exists($"res://assets/ui/hud/{bar}.png") ? GD.Load<Texture2D>($"res://assets/ui/hud/{bar}.png")
				: ResourceLoader.Exists("res://assets/ui/hud/compass_bar.png") ? GD.Load<Texture2D>("res://assets/ui/hud/compass_bar.png") : null;
		}
		_pointerTex ??= ResourceLoader.Exists("res://assets/ui/hud/compass_pointer.png") ? GD.Load<Texture2D>("res://assets/ui/hud/compass_pointer.png") : null;
		if (_barTex != null) _draw.DrawTextureRect(_barTex, new Rect2(Vector2.Zero, size), false);

		float half = cx - BandInset;
		// (2026-10-31: "the lettering ... a little thinner it is too bold") Anton thinned
		var font = _font ??= new FontVariation { BaseFont = ChapterCard.Anton ?? UiKit.Serif, SpacingGlyph = 1, VariationEmbolden = -0.45f };
		// every 15 degrees a mark; the cardinals and the half-winds named, engraved: bone, a dark line under each
		for (int d = 0; d < 360; d += 15)
		{
			float shown = d;
			if (jitter > 0.5f && _rng.Randf() < 0.02f) shown += _rng.RandfRange(-40f, 40f);   // a mark briefly lies
			float delta = Mathf.Wrap(yawDeg - shown, -180f, 180f);
			if (Mathf.Abs(delta) > VisibleDegrees * 0.5f) continue;
			float x = cx + delta / (VisibleDegrees * 0.5f) * half + JitterPx(jitter, 2f);
			float off = Mathf.Abs(x - cx) / half;
			if (off > 1f) continue;
			float edge = 1f - Mathf.Pow(off, 4f);                 // fading into the band's ends, never popping
			string label = null;
			foreach (var (l, deg) in Ticks) if (Mathf.IsEqualApprox(deg, d)) label = l;
			bool major = label is "N" or "S" or "E" or "W";
			var ink = new Color(label == "N" ? NorthGreen : Engrave, (major ? 0.95f : label != null ? 0.7f : 0.45f) * edge);
			if (label == null)
			{
				Mark(new Vector2(x, cy + 1.6f), new Vector2(x, BandBottom - 1.4f), 0.6f, ink);
				continue;
			}
			int fs = major ? 8 : 6;
			var tsz = font.GetStringSize(label, HorizontalAlignment.Left, -1, fs);
			var at = new Vector2(x - tsz.X * 0.5f, cy + fs * 0.43f);   // (the capitals centred on the band: Anton's stand 0.86 of the size)
			_draw.DrawString(font, at + new Vector2(0, 0.6f), label, HorizontalAlignment.Left, -1, fs, new Color(EngraveShadow, EngraveShadow.A * edge));
			_draw.DrawString(font, at, label, HorizontalAlignment.Left, -1, fs, ink);
		}

		// the objective: the one lit thing on it, a neon diamond riding the band (or a chevron in the point it's past)
		if (_hasBearing || _markAlpha > 0.01f)
		{
			float bearing = _markDeg;
			if (jitter > 0.05f)
			{
				if (_clock >= _nextFalseBearing) { _falseBearing = _rng.RandfRange(0f, 360f); _nextFalseBearing = _clock + Mathf.Lerp(3.0, 0.4, jitter); }
				bearing = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(bearing), Mathf.DegToRad(_falseBearing), jitter));
			}
			float bx = cx + Mathf.Wrap(yawDeg - bearing, -180f, 180f) / (VisibleDegrees * 0.5f) * half + JitterPx(jitter, 6f);
			float mlit = Mathf.Lerp(1f, 0.45f, jitter) * (0.88f + 0.12f * Mathf.Sin(t * 3.1f)) * _markAlpha;
			if (Mathf.Abs(bx - cx) <= half)
			{
				var dia = new[] { new Vector2(bx, cy - 3.8f), new Vector2(bx + 2.8f, cy), new Vector2(bx, cy + 3.8f), new Vector2(bx - 2.8f, cy) };
				_draw.DrawColoredPolygon(new[] { dia[0] + new Vector2(0, 0.8f), dia[1] + new Vector2(0.4f, 0.8f), dia[2] + new Vector2(0, 0.8f), dia[3] + new Vector2(-0.4f, 0.8f) }, new Color(0, 0, 0, 0.5f * mlit));
				Purple(dia, 0.8f, mlit, true);
				_draw.DrawColoredPolygon(new[] { new Vector2(bx, cy - 2.2f), new Vector2(bx + 1.6f, cy), new Vector2(bx, cy + 2.2f), new Vector2(bx - 1.6f, cy) }, new Color(MarkCore, 0.85f * mlit));
			}
			else
			{
				// behind them: a lit chevron in the point on that side
				float dir = bx < cx ? -1f : 1f;
				float ex = cx + dir * (half + 6f);
				Purple(new[] { new Vector2(ex - dir * 2.5f, cy - 3.5f), new Vector2(ex + dir * 1.5f, cy), new Vector2(ex - dir * 2.5f, cy + 3.5f) }, 0.8f, mlit);
			}
		}

		// the lubber pointer over the middle, last (over the marks), a slight sway on a turn
		if (_pointerTex != null)
		{
			float sway = Mathf.Clamp(_yawVel * 0.004f, -1.2f, 1.2f);
			_draw.DrawTextureRect(_pointerTex, new Rect2(cx - 4f + sway, -2.4f, 8f, 6f), false);
		}
		else _draw.DrawColoredPolygon(new[] { new Vector2(cx - 3f, 0f), new Vector2(cx + 3f, 0f), new Vector2(cx, 4f) }, Engrave);
	}

	/// <summary>An engraved line: the bone stroke, a dark line a hair under it.</summary>
	private void Mark(Vector2 a, Vector2 b, float w, Color ink)
	{
		_draw.DrawLine(a + new Vector2(0.45f, 0.6f), b + new Vector2(0.45f, 0.6f), new Color(EngraveShadow, EngraveShadow.A * ink.A), w, true);
		_draw.DrawLine(a, b, ink, w, true);
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
		bool twitching = _twitchAt >= 0 && _clock >= _twitchAt && _clock < _twitchAt + TwitchSeconds;
		if (twitching)
		{
			if (!Twitched) { Twitched = true; GD.Print("[story] Act 3: the compass's needle swings off into the trees"); }
			float u = (float)(_clock - _twitchAt);
			float env = Mathf.SmoothStep(0f, 0.25f, u) * (1f - Mathf.SmoothStep(TwitchSeconds - 0.9f, TwitchSeconds, u));
			TwitchNow = TwitchDegrees * env + 6f * Mathf.Sin(u * 23f) * env;
			trueDeg = Mathf.Wrap(trueDeg + TwitchNow, 0f, 360f);
		}
		else TwitchNow = 0f;
		if (!_hasBearing) { _shownBearing = trueDeg; _hasBearing = true; return; }
		float diff = Mathf.Wrap(trueDeg - _shownBearing, -180f, 180f);
		// A new objective in another direction altogether (the story moved it): snap rather than sweep a half-turn.
		if (Mathf.Abs(diff) > 60f && !twitching && !(_twitchAt >= 0 && _clock < _twitchAt + TwitchSeconds + 1.0)) { _shownBearing = trueDeg; return; }
		// Otherwise close the gap fast when it is big and calmly when it is small (an exponential settle plus the
		// linear sweep): within a third of a second of any change the marker is within a degree or two.
		float step = MaxSweepDegPerSec * delta + Mathf.Abs(diff) * Mathf.Min(1f, 8f * delta);
		_shownBearing = Mathf.Wrap(_shownBearing + Mathf.Clamp(diff, -step, step), 0f, 360f);
	}
}
