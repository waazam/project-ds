using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The game's first picture (STORY.md Rev 5, "Opening: The Drive"; 2026-10-16, in place of the two black cards): a
/// parchment map, drawn like the map travel in Indiana Jones. A red dotted line draws itself from the magazine's office
/// to the Blackfern Trailhead in Overlook Park with a small car riding its head. Over the map, typed like a sheet from
/// the office, the assignment. As the car nears the trailhead a pager's small green screen comes up in the corner with
/// the editor's page, and cuts out mid-glow, as if the signal died. Then <see cref="Dissolve"/> lets the trunk show
/// through (OpeningAtCar).
///
/// All of it is drawn here in code over a parchment shader, at the game's 640x360 (the map's own 640x360 sheet is
/// centred in a wider window; the parchment fills it all). It fades up out of the black and never jumps: nothing on it
/// flashes. Pausable: it stops with the pause menu.
/// </summary>
public partial class PrologueMap : CanvasLayer
{
	// The timeline, in seconds from the start.
	private const float MapInAt = 0.4f, MapInSeconds = 1.8f;
	private const float TypeFrom = 2.0f, TypeTo = 6.6f;
	private const float DriveFrom = 3.0f, DriveTo = 12.6f;
	/// <summary>How far along the drive the pager comes up (the car nearing the trailhead).</summary>
	private const float PagerAtDrive = 0.78f;
	private const float PagerIn = 0.6f, PagerHold = 4.2f, PagerCut = 0.25f, PagerOut = 0.8f;
	public const float EndAt = 16.2f;

	private static readonly Vector2 Sheet = new(640, 360);
	private static readonly Color Ink = new(0.23f, 0.16f, 0.10f);
	private static readonly Color Red = new(0.66f, 0.09f, 0.07f);
	private static readonly Color Paper = new(0.94f, 0.91f, 0.83f);
	private static readonly Color Typed = new(0.13f, 0.12f, 0.11f);

	/// <summary>The drive's turns, office to trailhead, on the 640x360 sheet.</summary>
	private static readonly Vector2[] RouteKeys =
	{
		new(150, 292), new(212, 278), new(262, 246), new(300, 252), new(348, 220), new(392, 208),
		new(430, 176), new(468, 166), new(494, 134), new(522, 112),
	};

	private static readonly string[] SheetLines =
	{
		"ASSIGNMENT",
		$"{StoryNames.Magazine}: {StoryNames.Feature}",
		"The October feature.",
		"A nature photographer's trip",
		"into the woods.",
		$"{StoryNames.Trailhead}, {StoryNames.Park}.",
	};
	private static readonly string[] PageLines = { "Take your time up there.", "It's been a rough month." };

	private Control _root;
	private ColorRect _parchment;
	private ShaderMaterial _mat;
	private Control _ink;
	private double _t;
	private readonly List<Vector2> _route = new();
	private readonly List<float> _routeLen = new();
	private readonly List<Vector2> _trees = new();
	private Font _typewriter;
	private float _pagerFrom = -1f;

	/// <summary>Seconds since the map started (for the tests).</summary>
	public float Elapsed => (float)_t;
	/// <summary>The drive's progress, 0 at the office to 1 at the trailhead.</summary>
	/// <summary>(2026-10-24, the openings' pass) The stairs, drawn in the woods north of the trailhead in a hand that
	/// isn't the map's: they ink themselves in as the car arrives and are gone when the pager's signal dies. 0..1 now,
	/// and the most they've shown (for the tests).</summary>
	public float StairsInk { get; private set; }
	public float StairsShown { get; private set; }
	private static readonly Vector2 StairsAt = new(548, 66);

	public float Drive => Mathf.SmoothStep(0f, 1f, Mathf.Clamp(((float)_t - DriveFrom) / (DriveTo - DriveFrom), 0f, 1f));

	public override void _Ready()
	{
		Layer = 22;   // (over the fader's black (20) and the chapter cards (21), under the pause menu (30))
		_typewriter = new SystemFont
		{
			FontNames = new[] { "Courier New", "Courier", "Liberation Mono", "DejaVu Sans Mono", "monospace" },
			Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Normal,
			SubpixelPositioning = TextServer.SubpixelPositioning.Disabled, GenerateMipmaps = false, AllowSystemFallback = true,
		};
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		_mat = new ShaderMaterial { Shader = new Shader { Code = ParchmentShader } };
		_parchment = new ColorRect { Color = Colors.White, Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
		_parchment.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(_parchment);
		_ink = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_ink.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_ink.Draw += DrawMap;
		_root.AddChild(_ink);
		BuildRoute();
		// the woods round the trailhead, the same every game
		var rng = new RandomNumberGenerator { Seed = 1996 };
		for (int i = 0; i < 140; i++)
		{
			var p = new Vector2(rng.RandfRange(440, 620), rng.RandfRange(40, 200));
			if (p.DistanceTo(new Vector2(530, 110)) > 92f || p.DistanceTo(RouteKeys[^1]) < 12f) continue;
			if (p.DistanceTo(StairsAt + new Vector2(0, -8)) < 28f) continue;   // (a gap in the woods round the stairs)
			if (NearRoute(p, 7f) || new Rect2(466, 88, 108, 30).HasPoint(p) || new Rect2(490, 182, 92, 24).HasPoint(p)) continue;   // (clear of the route and the names)
			_trees.Add(p);
		}
		_trees.Sort((a, b) => a.Y.CompareTo(b.Y));
	}

	public override void _Process(double delta)
	{
		_t += delta;
		float t = (float)_t;
		if (t < MapInAt + MapInSeconds + 0.1f)
			_root.Modulate = new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - MapInAt) / MapInSeconds, 0f, 1f)));
		if (_pagerFrom < 0f && Drive >= PagerAtDrive) _pagerFrom = t;
		var size = _root.Size;
		_mat.SetShaderParameter("aspect", size.Y > 0 ? size.X / size.Y : 16f / 9f);
		_ink.QueueRedraw();
	}

	/// <summary>The map's whole run, until the car is parked and the page has gone (pausable).</summary>
	public async Task Play(CancellationToken ct)
	{
		while (_t < EndAt) await Cutscene.Frame(this, ct);
		GD.Print("[story] opening: the map's drive done, at the trailhead");
	}

	/// <summary>The map fades away over whatever is under it (the trunk, the fader already clear under the map).</summary>
	public async Task Dissolve(float seconds, CancellationToken ct)
	{
		var tw = CreateTween();
		tw.TweenProperty(_root, "modulate:a", 0f, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, tw, ct);
	}

	// --- the route -------------------------------------------------------------------------------------------------

	private void BuildRoute()
	{
		// a Catmull-Rom through the turns, sampled fine, with the length so far at each sample
		for (int i = 0; i < RouteKeys.Length - 1; i++)
		{
			var p0 = RouteKeys[Mathf.Max(i - 1, 0)];
			var p1 = RouteKeys[i];
			var p2 = RouteKeys[i + 1];
			var p3 = RouteKeys[Mathf.Min(i + 2, RouteKeys.Length - 1)];
			for (int k = 0; k < 24; k++)
			{
				float u = k / 24f, u2 = u * u, u3 = u2 * u;
				_route.Add(0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3));
			}
		}
		_route.Add(RouteKeys[^1]);
		float len = 0f;
		_routeLen.Add(0f);
		for (int i = 1; i < _route.Count; i++) { len += _route[i].DistanceTo(_route[i - 1]); _routeLen.Add(len); }
	}

	private float RouteLength => _routeLen[^1];

	private bool NearRoute(Vector2 p, float r)
	{
		foreach (var q in _route) if (q.DistanceTo(p) < r) return true;
		return false;
	}

	/// <summary>The point <paramref name="d"/> along the route, and the way it is heading there.</summary>
	private (Vector2 at, Vector2 dir) Along(float d)
	{
		d = Mathf.Clamp(d, 0f, RouteLength);
		int i = 1;
		while (i < _routeLen.Count - 1 && _routeLen[i] < d) i++;
		float seg = _routeLen[i] - _routeLen[i - 1];
		float u = seg > 0.0001f ? (d - _routeLen[i - 1]) / seg : 0f;
		var dir = (_route[i] - _route[i - 1]).Normalized();
		return (_route[i - 1].Lerp(_route[i], u), dir);
	}

	// --- drawing ---------------------------------------------------------------------------------------------------

	private void DrawMap()
	{
		var size = _ink.Size;
		float s = Mathf.Min(size.X / Sheet.X, size.Y / Sheet.Y);
		var origin = (size - Sheet * s) * 0.5f;
		var map = new Transform2D(0f, origin).ScaledLocal(new Vector2(s, s));
		_ink.DrawSetTransformMatrix(map);
		float t = (float)_t;

		DrawFrame();
		DrawRoads();
		DrawRiver();
		DrawMountains();
		DrawWoods();
		DrawCompass(new Vector2(58, 304));
		DrawPlaces();
		DrawStairs(t);
		DrawDrive();
		DrawAssignment(map, t);
		_ink.DrawSetTransformMatrix(Transform2D.Identity);
		DrawPager(size, s, t);
	}

	private void DrawFrame()
	{
		_ink.DrawRect(new Rect2(12, 12, Sheet.X - 24, Sheet.Y - 24), new Color(Ink, 0.55f), false, 1f);
		_ink.DrawRect(new Rect2(16, 16, Sheet.X - 32, Sheet.Y - 32), new Color(Ink, 0.30f), false, 0.6f);
	}

	private void DrawRoads()
	{
		var road = new Color(Ink, 0.22f);
		// the way he drives, under the red line, and a few others that go elsewhere
		_ink.DrawPolyline(_route.ToArray(), road, 1.6f, true);
		_ink.DrawPolyline(new Vector2[] { new(150, 292), new(110, 252), new(70, 226), new(22, 214) }, road, 1.2f, true);
		_ink.DrawPolyline(new Vector2[] { new(150, 292), new(204, 318), new(262, 344) }, road, 1.2f, true);
		_ink.DrawPolyline(new Vector2[] { new(262, 246), new(282, 300), new(330, 322), new(392, 330), new(470, 312) }, road, 1.0f, true);
		_ink.DrawPolyline(new Vector2[] { new(392, 208), new(420, 250), new(468, 268), new(560, 262), new(624, 280) }, road, 1.0f, true);
	}

	private void DrawRiver()
	{
		var pts = new List<Vector2>();
		for (int i = 0; i <= 40; i++)
		{
			float u = i / 40f;
			var a = new Vector2(612, 18).Lerp(new Vector2(364, 346), u);
			pts.Add(a + new Vector2(Mathf.Sin(u * 13f) * 9f, Mathf.Cos(u * 9f) * 4f));
		}
		_ink.DrawPolyline(pts.ToArray(), new Color(0.24f, 0.30f, 0.34f, 0.45f), 1.4f, true);
	}

	private void DrawMountains()
	{
		var rng = new RandomNumberGenerator { Seed = 25 };
		for (int i = 0; i < 9; i++)
		{
			float x = 292 + i * 15 + rng.RandfRange(-4, 4), y = 92 + rng.RandfRange(-10, 10), h = rng.RandfRange(12, 20), w = h * 0.9f;
			var peak = new Vector2(x, y - h);
			_ink.DrawPolyline(new[] { new Vector2(x - w, y), peak, new Vector2(x + w, y) }, new Color(Ink, 0.62f), 1f, true);
			// the shaded side, hatched
			for (int k = 1; k < 4; k++)
			{
				float f = k / 4f;
				var on = peak.Lerp(new Vector2(x + w, y), f);
				_ink.DrawLine(on, on + new Vector2(-2.5f, 3.5f), new Color(Ink, 0.35f), 0.6f, true);
			}
		}
	}

	private void DrawWoods()
	{
		foreach (var p in _trees)
		{
			var c = new Color(Ink, 0.55f);
			_ink.DrawColoredPolygon(new[] { p + new Vector2(0, -6), p + new Vector2(3, 0), p + new Vector2(-3, 0) }, new Color(0.30f, 0.30f, 0.16f, 0.35f));
			_ink.DrawPolyline(new[] { p + new Vector2(-3, 0), p + new Vector2(0, -6), p + new Vector2(3, 0) }, c, 0.7f, true);
			_ink.DrawLine(p, p + new Vector2(0, 2), c, 0.7f);
		}
	}

	private void DrawCompass(Vector2 c)
	{
		var ink = new Color(Ink, 0.6f);
		_ink.DrawArc(c, 15f, 0f, Mathf.Tau, 32, ink, 0.8f, true);
		_ink.DrawLine(c + new Vector2(-19, 0), c + new Vector2(19, 0), new Color(Ink, 0.35f), 0.6f);
		_ink.DrawLine(c + new Vector2(0, -19), c + new Vector2(0, 19), new Color(Ink, 0.35f), 0.6f);
		_ink.DrawColoredPolygon(new[] { c + new Vector2(0, -14), c + new Vector2(3, 0), c + new Vector2(0, 14), c + new Vector2(-3, 0) }, new Color(Ink, 0.25f));
		_ink.DrawColoredPolygon(new[] { c + new Vector2(0, -14), c + new Vector2(3, 0), c + new Vector2(-3, 0) }, ink);
		Text(UiKit.Serif, "N", c + new Vector2(0, -21), 9, ink, center: true);
	}

	private void DrawPlaces()
	{
		var ink = new Color(Ink, 0.85f);
		var start = RouteKeys[0];
		_ink.DrawRect(new Rect2(start - new Vector2(3, 3), new Vector2(6, 6)), ink);
		Text(UiKit.SerifItalic, "the office", start + new Vector2(-8, 16), 10, ink, center: true);
		// two towns he passes
		foreach (var town in new Vector2[] { new(262, 246), new(392, 208) }) _ink.DrawCircle(town, 2f, new Color(Ink, 0.6f));
		var end = RouteKeys[^1];
		_ink.DrawArc(end, 5f, 0f, Mathf.Tau, 20, ink, 1f, true);
		Text(UiKit.SerifItalic, StoryNames.Trailhead, end + new Vector2(-50, -10), 10, ink);
		Text(UiKit.SerifHeading, StoryNames.Park.ToUpperInvariant(), new Vector2(536, 196), 9, new Color(Ink, 0.5f), center: true);
	}

	private void DrawStairs(float t)
	{
		float inAt = DriveFrom + (DriveTo - DriveFrom) * 0.84f;
		float a = Mathf.SmoothStep(0f, 1f, (t - inAt) / 1.6f);
		if (_pagerFrom >= 0f)
		{
			float cut = _pagerFrom + PagerIn + PagerHold;
			a *= 1f - Mathf.SmoothStep(cut, cut + 0.5f, t);
		}
		StairsInk = a;
		StairsShown = Mathf.Max(StairsShown, a);
		if (a <= 0.01f) return;
		// pencil, not the map's ink: a short flight going up to nothing, its two strings, a newel at the foot
		var pencil = new Color(0.22f, 0.22f, 0.25f, 0.8f * a);
		var p = StairsAt;
		var pts = new List<Vector2>();
		for (int i = 0; i < 6; i++)
		{
			pts.Add(p + new Vector2(i * 3.4f, -i * 3.4f));
			pts.Add(p + new Vector2(i * 3.4f, -(i + 1) * 3.4f));
		}
		pts.Add(p + new Vector2(6 * 3.4f, -6 * 3.4f));
		_ink.DrawPolyline(pts.ToArray(), pencil, 0.9f, true);
		_ink.DrawLine(p + new Vector2(0, 2.5f), p + new Vector2(21f, -18.5f), new Color(pencil, pencil.A * 0.6f), 0.6f, true);
		_ink.DrawLine(p, p + new Vector2(0, -6f), pencil, 1.1f, true);
		Text(UiKit.SerifItalic, "the steps", p + new Vector2(-34, -10), 8, new Color(0.3f, 0.3f, 0.33f, 0.75f * a));
	}

	private void DrawDrive()
	{
		float drawn = RouteLength * Drive;
		const float dash = 3.2f, gap = 2.6f;
		var red = new Color(Red, 0.95f);
		_ink.DrawCircle(RouteKeys[0], 2.2f, red);
		for (float d = 0f; d < drawn; d += dash + gap)
		{
			var (a, _) = Along(d);
			var (b, _) = Along(Mathf.Min(d + dash, drawn));
			if (a.DistanceSquaredTo(b) > 0.01f) _ink.DrawLine(a, b, red, 1.9f, true);
		}
		if (_t < DriveFrom - 0.2f) return;
		// the car at the line's head, top down, facing the way it goes
		var (at, dir) = Along(drawn);
		if (drawn > 6f)
		{
			var (behind, _) = Along(drawn - 6f);
			dir = (at - behind).Normalized();
		}
		var car = new Transform2D(dir.Angle(), at);
		var mapXf = CurrentMap();
		_ink.DrawSetTransformMatrix(mapXf * car);
		var body = new Color(0.20f, 0.14f, 0.10f);
		_ink.DrawColoredPolygon(new Vector2[] { new(-5.5f, -2.6f), new(3.4f, -3f), new(5.6f, -1.8f), new(5.6f, 1.8f), new(3.4f, 3f), new(-5.5f, 2.6f) }, body);
		var glass = new Color(0.78f, 0.70f, 0.54f);
		_ink.DrawColoredPolygon(new Vector2[] { new(1.4f, -2.1f), new(3.1f, -2.4f), new(3.1f, 2.4f), new(1.4f, 2.1f) }, glass);
		_ink.DrawColoredPolygon(new Vector2[] { new(-3.9f, -1.9f), new(-2.8f, -2.1f), new(-2.8f, 2.1f), new(-3.9f, 1.9f) }, glass);
		_ink.DrawSetTransformMatrix(mapXf);
	}

	private Transform2D CurrentMap()
	{
		var size = _ink.Size;
		float s = Mathf.Min(size.X / Sheet.X, size.Y / Sheet.Y);
		return new Transform2D(0f, (size - Sheet * s) * 0.5f).ScaledLocal(new Vector2(s, s));
	}

	/// <summary>The office's sheet, taped over the map's top left, typed in as it is read.</summary>
	private void DrawAssignment(Transform2D map, float t)
	{
		var sheetXf = map * new Transform2D(Mathf.DegToRad(-2.2f), new Vector2(30, 30));
		_ink.DrawSetTransformMatrix(sheetXf);
		var size = new Vector2(228, 106);
		_ink.DrawRect(new Rect2(new Vector2(2.5f, 3f), size), new Color(0.10f, 0.06f, 0.03f, 0.22f));
		_ink.DrawRect(new Rect2(Vector2.Zero, size), Paper);
		_ink.DrawRect(new Rect2(Vector2.Zero, size), new Color(Ink, 0.25f), false, 0.6f);
		// a strip of tape across the top
		_ink.DrawSetTransformMatrix(sheetXf * new Transform2D(Mathf.DegToRad(4f), new Vector2(size.X * 0.5f - 22f, -5f)));
		_ink.DrawRect(new Rect2(0, 0, 44, 10), new Color(0.92f, 0.88f, 0.70f, 0.55f));
		_ink.DrawSetTransformMatrix(sheetXf);

		int total = 0;
		foreach (var l in SheetLines) total += l.Length;
		int shown = (int)(total * Mathf.Clamp((t - TypeFrom) / (TypeTo - TypeFrom), 0f, 1f));
		float y = 18f;
		for (int i = 0; i < SheetLines.Length && shown > 0; i++)
		{
			string line = SheetLines[i];
			string part = line[..Mathf.Min(shown, line.Length)];
			shown -= line.Length;
			int fs = i == 1 ? 10 : 9;
			_ink.DrawString(_typewriter, new Vector2(12, y), part, HorizontalAlignment.Left, -1, fs, Typed);
			if (i == 0 && part.Length == line.Length)
				_ink.DrawLine(new Vector2(12, y + 3), new Vector2(12 + _typewriter.GetStringSize(line, HorizontalAlignment.Left, -1, fs).X, y + 3), new Color(Typed, 0.7f), 0.6f);
			y += i == 0 ? 18f : 14f;
		}
		_ink.DrawSetTransformMatrix(map);
	}

	/// <summary>The pager in the picture's bottom right: its little green screen with the page, until it cuts out.</summary>
	private void DrawPager(Vector2 screen, float s, float t)
	{
		if (_pagerFrom < 0f) return;
		float u = t - _pagerFrom;
		float cutAt = PagerIn + PagerHold;
		if (u > cutAt + 0.4f + PagerOut) return;
		float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(u / PagerIn, 0f, 1f));
		float body = u < cutAt + 0.4f ? rise : 1f - Mathf.SmoothStep(0f, 1f, (u - cutAt - 0.4f) / PagerOut);
		// the backlight: up with the pager, then out mid-glow (a quick fall, never a blink)
		float glow = u < cutAt ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp((u - 0.25f) / 0.5f, 0f, 1f)) * (0.94f + 0.06f * Mathf.Sin(u * 2.1f))
			: 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((u - cutAt) / PagerCut, 0f, 1f));
		var pagerSize = new Vector2(152, 46);
		var at = screen / s - pagerSize - new Vector2(16, 14) + new Vector2(0, (1f - rise) * 10f);
		_ink.DrawSetTransformMatrix(new Transform2D(0f, Vector2.Zero).ScaledLocal(new Vector2(s, s)) * new Transform2D(0f, at));
		_ink.DrawRect(new Rect2(new Vector2(2, 3), pagerSize), new Color(0, 0, 0, 0.28f * body));
		_ink.DrawRect(new Rect2(Vector2.Zero, pagerSize), new Color(0.11f, 0.11f, 0.12f, body));
		_ink.DrawRect(new Rect2(Vector2.Zero, pagerSize), new Color(0.30f, 0.30f, 0.32f, body), false, 1f);
		var lcd = new Rect2(9, 8, 134, 30);
		_ink.DrawRect(lcd.Grow(3f), new Color(0.55f, 0.75f, 0.35f, 0.16f * glow * body));
		var unlit = new Color(0.17f, 0.21f, 0.15f);
		var lit = new Color(0.55f, 0.70f, 0.34f);
		var screenCol = unlit.Lerp(lit, glow);
		_ink.DrawRect(lcd, new Color(screenCol, body));
		float words = Mathf.Clamp((u - 0.55f) / 0.2f, 0f, 1f) * glow;
		if (words > 0.01f)
			for (int i = 0; i < PageLines.Length; i++)
				_ink.DrawString(UiKit.Mono, new Vector2(lcd.Position.X + 5, lcd.Position.Y + 12 + i * 11), PageLines[i], HorizontalAlignment.Left, -1, 8,
					new Color(0.07f, 0.12f, 0.05f, words * body));
		_ink.DrawSetTransformMatrix(Transform2D.Identity);
	}

	private void Text(Font f, string text, Vector2 at, int size, Color col, bool center = false)
	{
		if (f == null) return;
		float w = center ? f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X : 0f;
		_ink.DrawString(f, at - new Vector2(w * 0.5f, 0), text, HorizontalAlignment.Left, -1, size, col);
	}

	private const string ParchmentShader = @"
shader_type canvas_item;
uniform float aspect = 1.777;
float h(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float n(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h(i), h(i + vec2(1.0, 0.0)), f.x), mix(h(i + vec2(0.0, 1.0)), h(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) { float v = 0.0, a = 0.5; for (int k = 0; k < 5; k++) { v += a * n(p); p *= 2.03; a *= 0.5; } return v; }
void fragment() {
	vec2 p = vec2(UV.x * aspect, UV.y);
	float stain = fbm(p * 3.2 + 7.0);
	float fibre = n(p * vec2(220.0, 34.0)) * 0.5 + n(p * vec2(30.0, 260.0)) * 0.5;
	vec3 base = vec3(0.80, 0.70, 0.52);
	vec3 col = mix(base, vec3(0.64, 0.51, 0.34), smoothstep(0.45, 0.85, stain) * 0.7);
	col *= 0.96 + 0.06 * fibre;
	// the folds: one down the middle, one across, a faint crease and its light edge
	float fx = abs(UV.x - 0.5), fy = abs(UV.y - 0.5);
	col *= 1.0 - 0.07 * smoothstep(0.006, 0.0, fx) - 0.06 * smoothstep(0.006, 0.0, fy);
	col *= 1.0 + 0.025 * smoothstep(0.012, 0.006, fx) * step(0.003, fx);
	// the edges, browned and worn
	vec2 c = UV - 0.5;
	float edge = length(c * vec2(1.0, 1.15)) + (fbm(p * 6.0) - 0.5) * 0.12;
	col = mix(col, vec3(0.36, 0.25, 0.14), smoothstep(0.42, 0.78, edge));
	COLOR = vec4(col, 1.0) * COLOR;
}";
}
