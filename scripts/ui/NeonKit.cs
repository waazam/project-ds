using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Neon line-work for the HUD (2026-10-10c, the owner: "we need the inventory ui in the bottom right to have a full overhaul
/// giving it some personality. Instead of grey lets make a glowing green. make custom animations for the green glow to
/// really make them pop. You should have settings in the menu on changing colors for the inventory: white, red, purple,
/// blue, yellow, orange. Make the colors look tasteful in neon.").
///
/// Every stroke is a tube: three soft passes of its glow, wider and fainter outward, and over them the tube's core, near
/// white with the colour in it. The colours are the setting's (<see cref="GameSettings.HudColor"/>), each chosen to read as
/// a lit glass tube in the dark, not a flat fill: never pure, never a glare. The icons are drawn here as line art, one for
/// each thing that can be carried.
/// </summary>
public static class NeonKit
{
	// (white the default, the owner 2026-10-10c: "we should also make the standard ui color the white")
	public static readonly string[] Names = { "White", "Green", "Red", "Purple", "Blue", "Yellow", "Orange" };
	// (core, glow): the core is the lit tube, the glow the light it throws
	private static readonly (Color core, Color glow)[] Palette =
	{
		(new Color(0.96f, 0.97f, 1.0f), new Color(0.72f, 0.8f, 0.95f)),     // white (a cool argon white)
		(new Color(0.72f, 1.0f, 0.78f), new Color(0.2f, 0.95f, 0.42f)),     // green
		(new Color(1.0f, 0.72f, 0.72f), new Color(1.0f, 0.2f, 0.26f)),      // red
		(new Color(0.9f, 0.76f, 1.0f), new Color(0.66f, 0.3f, 1.0f)),       // purple
		(new Color(0.72f, 0.88f, 1.0f), new Color(0.22f, 0.6f, 1.0f)),      // blue
		(new Color(1.0f, 0.96f, 0.72f), new Color(1.0f, 0.82f, 0.22f)),     // yellow
		(new Color(1.0f, 0.84f, 0.68f), new Color(1.0f, 0.5f, 0.16f)),      // orange
	};

	public static int Index => Mathf.Clamp(GameSettings.Instance?.HudColor ?? 0, 0, Palette.Length - 1);
	public static Color Core => Palette[Index].core;
	public static Color Glow => Palette[Index].glow;

	/// <summary>A tube along <paramref name="pts"/>, lit to <paramref name="lit"/> (0 dark .. 1 full).</summary>
	public static void Line(CanvasItem c, Vector2[] pts, float width, float lit, bool closed = false)
	{
		if (pts.Length < 2 || lit <= 0.001f) return;
		if (closed)
		{
			var p = new Vector2[pts.Length + 1];
			pts.CopyTo(p, 0);
			p[^1] = pts[0];
			pts = p;
		}
		var g = Glow;
		// (2026-10-11, the owner: the glow was "almost blurry ... like I have some kind of astigmatism": one faint, close
		// halo now, the tube itself crisp)
		c.DrawPolyline(pts, new Color(g, 0.1f * lit), width * 2.2f, true);
		c.DrawPolyline(pts, new Color(Core, 0.97f * lit), width, true);
	}

	public static void Seg(CanvasItem c, Vector2 a, Vector2 b, float width, float lit) => Line(c, new[] { a, b }, width, lit);

	public static Vector2[] Arc(Vector2 centre, float r, float from, float to, int n = 18)
	{
		var pts = new Vector2[n + 1];
		for (int i = 0; i <= n; i++)
		{
			float a = Mathf.Lerp(from, to, i / (float)n);
			pts[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
		}
		return pts;
	}

	public static void Circle(CanvasItem c, Vector2 centre, float r, float width, float lit) => Line(c, Arc(centre, r, 0f, Mathf.Tau, 24), width, lit);

	public static Vector2[] Box(Vector2 p, Vector2 s, float round = 0f)
	{
		if (round <= 0f) return new[] { p, p + new Vector2(s.X, 0), p + s, p + new Vector2(0, s.Y) };
		var l = new System.Collections.Generic.List<Vector2>();
		void Corner(Vector2 c0, float a0) { foreach (var q in Arc(c0, round, a0, a0 + Mathf.Pi * 0.5f, 4)) l.Add(q); }
		Corner(p + new Vector2(s.X - round, round), -Mathf.Pi * 0.5f);
		Corner(p + new Vector2(s.X - round, s.Y - round), 0f);
		Corner(p + new Vector2(round, s.Y - round), Mathf.Pi * 0.5f);
		Corner(p + new Vector2(round, round), Mathf.Pi);
		return l.ToArray();
	}

	/// <summary>Neon lettering: the glow first (outlines, soft and wide), then the lit letters.</summary>
	public static void Text(CanvasItem c, Font font, Vector2 pos, string text, HorizontalAlignment align, float width, int size, float lit)
	{
		if (lit <= 0.001f) return;
		var g = Glow;
		c.DrawStringOutline(font, pos, text, align, width, size, 2, new Color(g, 0.12f * lit));
		c.DrawString(font, pos, text, align, width, size, new Color(Core, 0.97f * lit));
	}

	private static Texture2D _hand, _handGlow;
	private static bool _handLooked;

	/// <summary>The painted hand (assets/ui/hud/hand.png and its glow), or null.</summary>
	private static Texture2D HandIcon()
	{
		if (_handLooked) return _hand;
		_handLooked = true;
		const string path = "res://assets/ui/hud/hand.png", glow = "res://assets/ui/hud/hand_glow.png";
		_hand = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		_handGlow = ResourceLoader.Exists(glow) ? GD.Load<Texture2D>(glow) : null;
		return _hand;
	}

	/// <summary>The thing's icon, line art in neon, <paramref name="s"/> across, centred on <paramref name="c0"/>;
	/// <paramref name="t"/> runs its little life (the lantern's flame, the compass's needle).</summary>
	public static void Icon(CanvasItem c, ToolKind kind, Vector2 c0, float s, float lit, float t)
	{
		float w = Mathf.Max(0.9f, s * 0.065f);
		Vector2 P(float x, float y) => c0 + new Vector2(x, y) * s;
		string n = kind.ToString();
		switch (kind)
		{
			case ToolKind.Lantern:
				Line(c, Arc(P(0, -0.36f), s * 0.13f, Mathf.Pi, Mathf.Tau, 8), w, lit);                    // the bail
				Line(c, new[] { P(-0.16f, -0.26f), P(0.16f, -0.26f), P(0.2f, -0.18f), P(-0.2f, -0.18f) }, w, lit, true);   // the cap
				Line(c, Box(P(-0.17f, -0.18f), new Vector2(0.34f, 0.46f) * s, s * 0.04f), w, lit, true);   // the glass
				Seg(c, P(-0.06f, -0.16f), P(-0.06f, 0.26f), w * 0.6f, lit * 0.5f);
				Seg(c, P(0.06f, -0.16f), P(0.06f, 0.26f), w * 0.6f, lit * 0.5f);
				Line(c, new[] { P(-0.22f, 0.28f), P(0.22f, 0.28f), P(0.18f, 0.36f), P(-0.18f, 0.36f) }, w, lit, true);      // the base
				// the flame, breathing
				float fl = 0.06f + 0.012f * Mathf.Sin(t * 3.1f);
				Line(c, new[] { P(0, 0.17f), P(-0.045f, 0.1f), P(0, 0.1f - fl * 1.2f), P(0.045f, 0.1f) }, w * 0.9f, lit, true);
				break;
			case ToolKind.Camera:
				Line(c, Box(P(-0.4f, -0.2f), new Vector2(0.8f, 0.48f) * s, s * 0.05f), w, lit, true);
				Line(c, new[] { P(-0.16f, -0.2f), P(-0.1f, -0.3f), P(0.1f, -0.3f), P(0.16f, -0.2f) }, w, lit);
				Circle(c, P(0, 0.04f), s * 0.16f, w, lit);
				Circle(c, P(0, 0.04f), s * 0.07f, w * 0.8f, lit * 0.7f);
				Seg(c, P(0.26f, -0.12f), P(0.32f, -0.12f), w, lit);
				break;
			case ToolKind.Compass:
				Circle(c, P(0, 0), s * 0.36f, w, lit);
				Circle(c, P(0, 0), s * 0.29f, w * 0.5f, lit * 0.4f);
				float a = Mathf.Sin(t * 0.9f) * 0.35f + Mathf.Sin(t * 2.3f) * 0.08f;   // the needle settling, never still
				var d = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
				var sd = new Vector2(d.Y, -d.X);
				Line(c, new[] { c0 + d * s * 0.25f, c0 + sd * s * 0.06f, c0 - d * s * 0.25f, c0 - sd * s * 0.06f }, w, lit, true);
				Seg(c, c0 + d * s * 0.25f, c0 + sd * s * 0.06f, w * 1.6f, lit);   // its north half brighter
				break;
			case ToolKind.Radio:
				Line(c, Box(P(-0.18f, -0.2f), new Vector2(0.36f, 0.58f) * s, s * 0.05f), w, lit, true);
				Seg(c, P(0.1f, -0.2f), P(0.12f, -0.48f), w, lit);
				for (int i = 0; i < 3; i++) Seg(c, P(-0.1f, -0.08f + i * 0.08f), P(0.1f, -0.08f + i * 0.08f), w * 0.7f, lit * 0.7f);
				Circle(c, P(-0.06f, 0.24f), s * 0.04f, w * 0.7f, lit);
				break;
			case ToolKind.Keycard202 or ToolKind.Keycard203 or ToolKind.Keycard201:
				Line(c, Box(P(-0.36f, -0.24f), new Vector2(0.72f, 0.48f) * s, s * 0.06f), w, lit, true);
				Seg(c, P(-0.36f, -0.1f), P(0.36f, -0.1f), w * 1.4f, lit * 0.8f);
				Line(c, Box(P(-0.24f, 0.02f), new Vector2(0.14f, 0.12f) * s), w * 0.7f, lit, true);
				Seg(c, P(0.04f, 0.12f), P(0.26f, 0.12f), w * 0.6f, lit * 0.6f);
				break;
			case ToolKind.Key or ToolKind.PantryKey or ToolKind.DiningKey or ToolKind.FontKey or ToolKind.BunkerKey:
				Circle(c, P(-0.24f, 0), s * 0.13f, w, lit);
				Seg(c, P(-0.11f, 0), P(0.38f, 0), w, lit);
				Line(c, new[] { P(0.2f, 0), P(0.2f, 0.12f), P(0.27f, 0.12f), P(0.27f, 0.05f), P(0.33f, 0.05f), P(0.33f, 0.12f) }, w * 0.9f, lit);
				break;
			case ToolKind.Lighter:
				Line(c, Box(P(-0.14f, -0.06f), new Vector2(0.28f, 0.4f) * s, s * 0.04f), w, lit, true);
				Seg(c, P(-0.14f, 0.06f), P(0.14f, 0.06f), w * 0.6f, lit * 0.6f);
				float lf = 0.12f + 0.02f * Mathf.Sin(t * 3.7f);
				Line(c, new[] { P(0, -0.08f), P(-0.06f, -0.16f), P(0, -0.08f - lf * 1.6f), P(0.06f, -0.16f) }, w, lit, true);
				break;
			case ToolKind.Flamethrower:
				Line(c, Box(P(-0.38f, -0.1f), new Vector2(0.26f, 0.42f) * s, s * 0.1f), w, lit, true);   // the tank
				Line(c, new[] { P(-0.12f, 0.06f), P(0.1f, 0.06f), P(0.38f, -0.06f) }, w, lit);              // the hose and gun
				Line(c, new[] { P(0.1f, 0.06f), P(0.12f, 0.2f) }, w, lit);
				Circle(c, P(0.42f, -0.08f), s * 0.035f, w, lit);
				break;
			case ToolKind.Axe:
				Seg(c, P(-0.3f, 0.36f), P(0.2f, -0.3f), w, lit);
				Line(c, new[] { P(0.08f, -0.36f), P(0.36f, -0.2f), P(0.26f, -0.02f), P(0.14f, -0.16f) }, w, lit, true);
				break;
			case ToolKind.Hammer:
				Seg(c, P(-0.28f, 0.36f), P(0.12f, -0.18f), w, lit);
				Line(c, Box(P(-0.04f, -0.36f), new Vector2(0.36f, 0.16f) * s), w, lit, true);
				break;
			case ToolKind.Knife:
				Line(c, new[] { P(-0.34f, 0.3f), P(-0.14f, 0.1f), P(0.34f, -0.34f), P(0.02f, 0.02f) }, w, lit, true);
				Seg(c, P(-0.22f, 0.04f), P(-0.08f, 0.18f), w, lit);
				break;
			case ToolKind.Crowbar:
				Line(c, new[] { P(-0.3f, 0.36f), P(0.22f, -0.28f), P(0.34f, -0.3f), P(0.36f, -0.2f) }, w * 1.1f, lit);
				Line(c, new[] { P(-0.3f, 0.36f), P(-0.38f, 0.3f) }, w * 1.1f, lit);
				break;
			case ToolKind.Chalice:
				Line(c, new[] { P(-0.22f, -0.32f), P(0.22f, -0.32f), P(0.14f, -0.04f), P(0.03f, 0.04f), P(0.03f, 0.24f), P(0.16f, 0.32f), P(-0.16f, 0.32f), P(-0.03f, 0.24f), P(-0.03f, 0.04f), P(-0.14f, -0.04f) }, w, lit, true);
				break;
			case ToolKind.DeadEye:
				Line(c, new[] { P(-0.38f, 0), P(-0.16f, -0.18f), P(0.16f, -0.18f), P(0.38f, 0), P(0.16f, 0.18f), P(-0.16f, 0.18f) }, w, lit, true);
				Circle(c, P(0, 0), s * 0.1f, w, lit);
				break;
			case ToolKind.Bookmark:
				Line(c, new[] { P(-0.14f, -0.38f), P(0.14f, -0.38f), P(0.14f, 0.38f), P(0, 0.24f), P(-0.14f, 0.38f) }, w, lit, true);
				break;
			case ToolKind.NewelPost:   // (2026-10-13j) the cap: a ball on a neck, the plinth, the broken spigot under it
				Circle(c, P(0, -0.14f), s * 0.2f, w, lit);
				Line(c, new[] { P(-0.07f, 0.06f), P(-0.05f, 0.12f), P(0.05f, 0.12f), P(0.07f, 0.06f) }, w, lit);
				Line(c, Box(P(-0.24f, 0.12f), new Vector2(0.48f, 0.12f) * s), w, lit, true);
				Line(c, new[] { P(-0.1f, 0.24f), P(-0.06f, 0.34f), P(0f, 0.28f), P(0.05f, 0.36f), P(0.1f, 0.24f) }, w * 0.8f, lit * 0.8f);
				break;
			case ToolKind.None:        // an empty hand: an open palm, four fingers and the thumb
				// (2026-10-29, the playtest: the line-drawn hand "looks horrible") a painted hand, a real one's outline as a
				// tube (tools/Textures/make_hand_icon.py), tinted the HUD's colour, its glow under it; the old lines if missing
				if (HandIcon() is { } hand)
				{
					var r = new Rect2(c0 - Vector2.One * s * 0.5f, Vector2.One * s);
					if (_handGlow != null) c.DrawTextureRect(_handGlow, r.Grow(s * 0.06f), false, new Color(Glow, 0.55f * lit));
					c.DrawTextureRect(hand, r, false, new Color(Core, 0.97f * lit));
					break;
				}
				Line(c, new[] { P(-0.2f, 0.36f), P(-0.22f, 0.04f), P(-0.36f, -0.1f), P(-0.3f, -0.15f), P(-0.17f, -0.05f) }, w, lit);
				for (int i = 0; i < 4; i++)
				{
					float x = -0.15f + i * 0.1f, top = -0.36f + Mathf.Abs(i - 1.3f) * 0.06f;
					Line(c, new[] { P(x - 0.03f, 0f), P(x - 0.03f, top), P(x + 0.03f, top), P(x + 0.03f, 0f) }, w * 0.9f, lit);
				}
				Line(c, new[] { P(0.2f, 0f), P(0.2f, 0.2f), P(0.12f, 0.36f), P(-0.2f, 0.36f) }, w, lit);
				break;
			default:
				if (n.Contains("Post") || n.Contains("Tread"))
				{
					Line(c, Box(P(-0.12f, -0.36f), new Vector2(0.24f, 0.72f) * s), w, lit, true);
					Seg(c, P(-0.16f, -0.26f), P(0.16f, -0.26f), w, lit);
					break;
				}
				// a hand, a token: a diamond
				Line(c, new[] { P(0, -0.32f), P(0.26f, 0), P(0, 0.32f), P(-0.26f, 0) }, w, lit, true);
				break;
		}
	}
}
