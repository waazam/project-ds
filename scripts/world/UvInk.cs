using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Blacklight ink (the lantern's second mode, <see cref="Player.Lantern"/>): marks that only show where
/// the violet beam falls on them (<c>uv_ink.gdshader</c>). After the owner's references (fluorescent
/// footprints and handprints, a word written in invisible ink, a number smeared on a board), every mark
/// is a hand-drawn stroke mask with the fibrous, speckled edge of real residue:
/// <list type="bullet">
/// <item>R.H.'s digits, brushed big in his hand;</item>
/// <item>decoys: other digits scrawled and struck through, handprints, smears, tally marks, an eye,
/// scratches, drips;</item>
/// <item>bare footprints (left and right).</item>
/// </list>
/// Placing: <see cref="OnSurface"/> puts one on a wall, floor or ceiling, <see cref="OnTrunk"/> on a trunk,
/// <see cref="OnGround"/> on the ground, and <see cref="Scatter"/> many ground marks at once (the footprints).
///
/// (2026-10-13, the owner: marks "floating on nothing ... on an invisible piece of paper") Every mark is a projected
/// decal now (<c>uv_ink.gdshader</c>): a thin box the ink is cast through onto whatever real surface lies inside it,
/// so it wraps bark and boards, never stands off them, and shows nothing where there is nothing. A mark on a wall,
/// floor or ceiling is then checked against the visible geometry a moment after it is placed
/// (<see cref="UvInkSettler"/>): moved onto the surface if it was off it, moved along it if there was none, and
/// dropped if there is no surface near it at all (the words are moved, never dropped).
/// </summary>
public static partial class UvInk
{
	public static readonly Color Cyan = new(0.55f, 0.85f, 1.0f);
	public static readonly Color Green = new(0.45f, 1.0f, 0.4f);
	public static readonly Color Pale = new(0.85f, 0.9f, 1.0f);

	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, ShaderMaterial> _mat = new();
	private static Texture2D _grain;

	/// <summary>One mark in the ink: its node (the decal's box), where on the surface it is and which way that faces, in
	/// the box's own space (for the settling and the audit, <see cref="UvTour"/>), its size, and what it is.</summary>
	public record Mark(GeometryInstance3D Node, Vector3 Local, Vector3 Normal, float Size, string Kind)
	{
		public (Vector3 c, Vector3 n) World() => (Node.GlobalTransform * Local, (Node.GlobalBasis * Normal).Normalized());
	}
	public static readonly List<Mark> Marks = new();

	/// <summary>The decal's box: a unit cube (scaled by each mark's transform).</summary>
	private static BoxMesh _box;
	private static BoxMesh Box => _box ??= new BoxMesh { Size = Vector3.One };
	public static Mesh SharedBox => Box;

	/// <summary>How thick the box is (metres): how far off the surface a mark still finds it, either side.</summary>
	public const float WallDepth = 0.1f, GroundDepth = 0.45f;

	// ------------------------------------------------------------------ strokes

	private static List<Vector2> Arc(float cx, float cy, float rx, float ry, float a0, float a1, int n = 14)
	{
		var l = new List<Vector2>();
		for (int i = 0; i <= n; i++)
		{
			float a = Mathf.Lerp(a0, a1, i / (float)n);
			l.Add(new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry));
		}
		return l;
	}

	private static List<Vector2> L(params float[] xy)
	{
		var l = new List<Vector2>();
		for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1]));
		return l;
	}

	/// <summary>A digit as brush strokes in a unit box (y down), in a quick, uneven hand.</summary>
	public static List<List<Vector2>> DigitStrokes(int d)
	{
		const float P = Mathf.Pi;
		return d switch
		{
			0 => new() { Arc(0.5f, 0.5f, 0.3f, 0.4f, -P * 0.5f, P * 1.55f, 22) },
			1 => new() { L(0.32f, 0.22f, 0.55f, 0.08f, 0.53f, 0.93f) },
			2 => new() { Concat(Arc(0.5f, 0.32f, 0.28f, 0.24f, P * 1.05f, P * 2.2f, 12), L(0.2f, 0.9f, 0.84f, 0.88f)) },
			3 => new() { Arc(0.47f, 0.3f, 0.27f, 0.21f, P * 1.1f, P * 2.45f, 12), Arc(0.47f, 0.7f, 0.31f, 0.22f, P * 1.55f, P * 2.9f, 12) },
			4 => new() { L(0.62f, 0.92f, 0.64f, 0.08f, 0.14f, 0.64f, 0.88f, 0.62f) },
			5 => new() { Concat(L(0.8f, 0.1f, 0.3f, 0.12f, 0.26f, 0.44f), Arc(0.48f, 0.66f, 0.3f, 0.24f, P * 1.2f, P * 2.75f, 12)) },
			6 => new() { Concat(L(0.72f, 0.1f), Arc(0.5f, 0.66f, 0.28f, 0.26f, P * 1.25f, P * 3.2f, 20)) },
			7 => new() { L(0.14f, 0.13f, 0.86f, 0.1f, 0.4f, 0.93f), L(0.3f, 0.52f, 0.72f, 0.5f) },
			8 => new() { Arc(0.5f, 0.29f, 0.24f, 0.2f, 0f, P * 2.05f, 16), Arc(0.5f, 0.71f, 0.3f, 0.22f, 0f, P * 2.05f, 16) },
			_ => new() { Arc(0.48f, 0.32f, 0.27f, 0.23f, 0f, P * 2.05f, 16), L(0.75f, 0.35f, 0.62f, 0.93f) },
		};
	}

	private static List<Vector2> Concat(List<Vector2> a, List<Vector2> b) { a.AddRange(b); return a; }

	private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float t = Mathf.Clamp((p - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
		return p.DistanceTo(a + ab * t);
	}

	private static float Hash(int x, int y, int s)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	/// <summary>Rasterises strokes (unit coords) into an alpha mask with a brushed, fibrous edge.</summary>
	private static Texture2D Strokes(string key, int w, int h, List<List<Vector2>> strokes, float radius, int seed, Func<Vector2, float> extra = null)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var __tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("UvInk_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var p = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
				float d = 99f;
				foreach (var s in strokes)
					for (int i = 0; i + 1 < s.Count; i++) d = Mathf.Min(d, SegDist(p, s[i], s[i + 1]));
				if (strokes.Count > 0 && strokes[0].Count == 1) foreach (var s in strokes) d = Mathf.Min(d, p.DistanceTo(s[0]));
				// the brush: a ragged edge (noise on the radius) and a drier, patchier body
				float rr = radius * (0.75f + 0.5f * Hash(x / 2, y / 2, seed));
				float a = 1f - Mathf.SmoothStep(rr * 0.6f, rr, d);
				if (extra != null) a = Mathf.Max(a, extra(p));
				float fiber = 0.55f + 0.45f * Hash(x, y, seed + 7);
				a *= fiber;
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(a, 0f, 1f)));
			}
		img.GenerateMipmaps();
		Systems.TexCache.Save("UvInk_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		_tex[key] = t;
		return t;
	}

	public static Texture2D Digit(int d, bool struck = false)
	{
		var s = DigitStrokes(d);
		if (struck) { s.Add(L(0.05f, 0.75f, 0.95f, 0.25f)); s.Add(L(0.08f, 0.55f, 0.9f, 0.5f)); }
		return Strokes($"uv_digit{d}{(struck ? "x" : "")}", 48, 64, s, 0.075f, 900 + d);
	}

	/// <summary>A bare footprint: heel and ball, a waist between, five toes.</summary>
	public static Texture2D Foot() => Strokes("uv_foot", 32, 64, new List<List<Vector2>>
	{
		new() { new Vector2(0.3f, 0.12f) }, new() { new Vector2(0.48f, 0.08f) }, new() { new Vector2(0.62f, 0.1f) }, new() { new Vector2(0.74f, 0.15f) }, new() { new Vector2(0.82f, 0.22f) },
	}, 0.09f, 43, p =>
	{
		float ball = 1f - Mathf.SmoothStep(0.26f, 0.32f, ((p - new Vector2(0.55f, 0.36f)) * new Vector2(1f, 1.3f)).Length());
		float heel = 1f - Mathf.SmoothStep(0.19f, 0.25f, ((p - new Vector2(0.45f, 0.8f)) * new Vector2(1.2f, 1.1f)).Length());
		float waist = 1f - Mathf.SmoothStep(0.12f, 0.17f, Mathf.Abs(p.X - (0.42f + 0.05f * Mathf.Sin(p.Y * 6f))) * (p.Y > 0.4f && p.Y < 0.72f ? 1f : 9f));
		return Mathf.Max(ball, Mathf.Max(heel, waist));
	});

	public static Texture2D Smear(int seed) => Strokes($"uv_smear{seed}", 64, 48, new List<List<Vector2>>
	{
		L(0.1f, 0.3f + 0.1f * (seed % 3), 0.35f, 0.25f, 0.6f, 0.4f, 0.9f, 0.35f), L(0.15f, 0.55f, 0.5f, 0.62f, 0.85f, 0.58f),
	}, 0.09f, 60 + seed);

	public static Texture2D Tally() => Strokes("uv_tally", 64, 48, new List<List<Vector2>>
	{
		L(0.2f, 0.15f, 0.22f, 0.85f), L(0.37f, 0.13f, 0.38f, 0.86f), L(0.54f, 0.15f, 0.53f, 0.84f), L(0.7f, 0.12f, 0.71f, 0.86f), L(0.1f, 0.7f, 0.85f, 0.3f),
	}, 0.05f, 71);

	public static Texture2D Eye() => Strokes("uv_eye", 64, 40, new List<List<Vector2>>
	{
		Arc(0.5f, 0.75f, 0.42f, 0.55f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f), Arc(0.5f, 0.25f, 0.42f, 0.55f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f), Arc(0.5f, 0.5f, 0.1f, 0.16f, 0f, Mathf.Tau),
	}, 0.05f, 73);

	public static Texture2D Scratches() => Strokes("uv_scratch", 48, 64, new List<List<Vector2>>
	{
		L(0.2f, 0.1f, 0.3f, 0.9f), L(0.4f, 0.08f, 0.48f, 0.85f), L(0.6f, 0.12f, 0.66f, 0.92f), L(0.78f, 0.1f, 0.82f, 0.8f),
	}, 0.035f, 77);

	/// <summary>(2026-10-15) R.H.'s arrow: a quick brushed shaft pointing up the mask and an open head, in one of three hands
	/// (<paramref name="variant"/>). Turned on the trunk to point the way to his next tree (<see cref="OnTrunk"/>'s turn).</summary>
	public static Texture2D Arrow(int variant)
	{
		int v = ((variant % 3) + 3) % 3;
		float lean = (v - 1) * 0.03f, head = 0.22f + 0.03f * v;
		return Strokes($"uv_arrow{v}", 48, 64, new List<List<Vector2>>
		{
			L(0.5f - lean, 0.92f, 0.5f + lean * 0.3f, 0.5f, 0.5f + lean, 0.1f),
			L(0.5f + lean - head, 0.1f + head * 1.1f, 0.5f + lean, 0.09f, 0.5f + lean + head * 0.95f, 0.1f + head * 1.15f),
		}, 0.08f, 960 + v);
	}

	/// <summary>A decoy chosen by index: a mark that is not a number (or a number struck out).</summary>
	public static Texture2D Decoy(int i, int avoidDigit)
	{
		switch (i % 7)
		{
			// a hand: on the trees, mostly the thing's (it climbs them), now and then a person's
			case 0: return (i / 7 % 4) switch { 0 => Hand(HandKind.Claw, i), 1 => Hand(HandKind.ClawDragged, i), 2 => Hand(HandKind.Pressed, i), _ => Hand(HandKind.ClawSplayed, i) };
			case 1: return Smear(i);
			case 2: return Tally();
			case 3: return Eye();
			case 4: return Scratches();
			case 5: return Digit((avoidDigit + 3 + i) % 10, true);
			default: return Smear(i + 11);
		}
	}

	// ------------------------------------------------------------------ materials

	private static Texture2D Grain()
	{
		if (_grain != null) return _grain;
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
		for (int y = 0; y < 64; y++)
			for (int x = 0; x < 64; x++)
			{
				float v = Hash(x, y, 5) * 0.6f + Hash(x / 3, y / 3, 6) * 0.4f;
				img.SetPixel(x, y, new Color(v, v, v));
			}
		img.GenerateMipmaps();
		return _grain = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
	}

	public static ShaderMaterial Mat(Texture2D mask, Color ink, float strength = 1.6f, bool mirror = false)
	{
		string key = $"{mask.GetRid()}_{ink.ToHtml()}_{strength}_{mirror}";
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/uv_ink.gdshader") };
		m.SetShaderParameter("mask_tex", mask);
		m.SetShaderParameter("grain_tex", Grain());
		m.SetShaderParameter("ink", ink);
		m.SetShaderParameter("strength", strength);
		m.SetShaderParameter("mirror", mirror);
		_mat[key] = m;
		return m;
	}

	// ------------------------------------------------------------------ writing

	private static Font _scrawl;
	private static Font Scrawl => _scrawl ??= new SystemFont { FontNames = new[] { "Ink Free", "Segoe Print", "Chiller", "Comic Sans MS" }, FontWeight = 700 };

	/// <summary>Words in the ink, in a scrawled hand: rendered once (a one-shot viewport) into a mask.</summary>
	public static Texture2D Text(Node owner, string text, int fontSize = 64, int w = 512, int h = 160)
	{
		string key = $"uvtext_{text}_{fontSize}_{w}x{h}";
		if (_tex.TryGetValue(key, out var t)) return t;
		var holder = owner.GetTree().Root.GetNodeOrNull("UvInkText") ?? AddHolder(owner);
		var vp = new SubViewport { Size = new Vector2I(w, h), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Disable3D = true };
		holder.AddChild(vp);
		var label = new Label
		{
			Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart, Size = new Vector2(w, h),
		};
		label.AddThemeFontOverride("font", Scrawl);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", Colors.White);
		vp.AddChild(label);
		t = vp.GetTexture();
		_tex[key] = t;
		return t;
	}

	private static Node AddHolder(Node owner)
	{
		var n = new Node { Name = "UvInkText" };
		owner.GetTree().Root.CallDeferred(Node.MethodName.AddChild, n);
		return n;
	}

	// ------------------------------------------------------------------ placing (projected decals)

	/// <summary>The mark's across (right, as you face it) and down directions on a surface facing <paramref name="n"/>,
	/// turned <paramref name="spin"/> radians in its plane.</summary>
	private static (Vector3 right, Vector3 down) Frame(Vector3 n, float spin)
	{
		Vector3 up = Mathf.Abs(n.Y) > 0.95f ? Vector3.Forward : Vector3.Up;
		Vector3 x = up.Cross(n).Normalized(), y = n.Cross(x).Normalized();
		var rot = new Basis(n, spin);
		return (rot * x, -(rot * y));
	}

	/// <summary>The decal's box: across, the surface's normal (the box's depth), and down.</summary>
	private static MeshInstance3D Decal(Node parent, string name, Vector3 right, Vector3 n, Vector3 down, Vector3 size, Vector3 origin, Material mat, bool global)
	{
		var mi = new MeshInstance3D
		{
			Name = name, Mesh = Box, MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
		};
		var xf = new Transform3D(new Basis(right * size.X, n * size.Y, down * size.Z), origin);
		parent.AddChild(mi);
		if (global && mi.IsInsideTree()) mi.GlobalTransform = xf;
		else mi.Transform = xf;
		return mi;
	}

	/// <summary>A mark flat on a surface: centred at <paramref name="local"/> in <paramref name="parent"/>'s space,
	/// facing along <paramref name="normal"/>, <paramref name="size"/> metres, turned <paramref name="spin"/> radians in its
	/// plane, <paramref name="mirror"/> for the other hand. Checked against the surface a moment later
	/// (<see cref="UvInkSettler"/>): <paramref name="pinned"/> marks (the words, the clues) are moved but never dropped.</summary>
	public static MeshInstance3D OnSurface(Node3D parent, Vector3 local, Vector3 normal, Vector2 size, Texture2D mask, Color ink, float strength = 1.6f, float spin = 0f, bool mirror = false, bool pinned = false, float depth = WallDepth)
	{
		normal = normal.Normalized();
		var (right, down) = Frame(normal, spin);
		var mi = Decal(parent, "UvWriting", right, normal, down, new Vector3(size.X, depth, size.Y), local, Mat(mask, ink, strength, mirror), false);
		var m = new Mark(mi, Vector3.Zero, Vector3.Up, Mathf.Max(size.X, size.Y), pinned ? "words" : "wall");
		Marks.Add(m);
		UvInkSettler.Queue(m, pinned);
		return mi;
	}

	/// <summary>Words in the ink on a surface (never dropped: they say something).</summary>
	public static MeshInstance3D Write(Node3D parent, Vector3 local, Vector3 normal, string text, float width, Color ink, float spin = 0f, float strength = 1.6f)
		=> OnSurface(parent, local, normal, new Vector2(width, width * 160f / 512f), Text(parent, text), ink, strength, spin, false, true);

	/// <summary>A mark on a trunk: centred at height <paramref name="y"/> on the side facing <paramref name="angle"/>
	/// (radians round the trunk's local Y, 0 = +Z), <paramref name="w"/> by <paramref name="h"/> metres,
	/// <paramref name="radius"/> the trunk's radius there, in the trunk node's space. Cast onto the bark from outside it,
	/// so it lies on the trunk's real faces (it wraps round them). <paramref name="turn"/> turns it in its plane (radians,
	/// counter-clockwise as seen facing it: an arrow drawn pointing up then points up and to the left).</summary>
	public static MeshInstance3D OnTrunk(Node3D trunk, float y, float angle, float radius, float w, float h, Texture2D mask, Color ink, float strength = 1.6f, bool mirror = false, float tilt = 0f, float turn = 0f)
	{
		Vector3 d = new(Mathf.Sin(angle), 0, Mathf.Cos(angle));
		var (right, down) = Frame(d, Mathf.Atan2(tilt, Mathf.Max(w, 0.05f)) + turn);
		// the box reaches from well inside the trunk to a little outside its widest (the bark's corners): the far side
		// faces away, so the ink never shows through on it
		float inner = radius * 0.3f, outer = radius + 0.1f;
		float depth = outer - inner, mid = (outer + inner) * 0.5f;
		float width = Mathf.Min(w, radius * 1.9f);
		var mi = Decal(trunk, "UvMark", right, d, down, new Vector3(width, depth, h), d * mid + Vector3.Up * y, Mat(mask, ink, strength, mirror), false);
		Marks.Add(new Mark(mi, new Vector3(0, (radius - mid) / depth, 0), Vector3.Up, Mathf.Max(w, h), "trunk"));
		return mi;
	}

	/// <summary>A ground basis: lying on a slope with normal <paramref name="n"/>, turned to <paramref name="yaw"/> (its Y the normal).</summary>
	public static Basis GroundBasis(Vector3 n, float yaw)
	{
		n = n.Normalized();
		Vector3 fwd = new(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
		Vector3 x = n.Cross(fwd).Normalized(), z = x.Cross(n).Normalized();
		return new Basis(x, n, z);
	}

	/// <summary>One mark lying on the ground at a world point, turned to <paramref name="yaw"/> (on the level).</summary>
	public static MeshInstance3D OnGround(Node parent, Vector3 world, float yaw, Vector2 size, Texture2D mask, Color ink, float strength = 1.4f)
		=> OnGround(parent, world, GroundBasis(Vector3.Up, yaw), size, mask, ink, strength);

	/// <summary>One mark lying on the ground at a world point, along a ground basis (<see cref="GroundBasis"/>: its Y the
	/// ground's normal). The box is deep enough to find the ground's real faces between the height samples.</summary>
	public static MeshInstance3D OnGround(Node parent, Vector3 world, Basis ground, Vector2 size, Texture2D mask, Color ink, float strength = 1.4f, bool mirror = false)
	{
		var mi = Decal(parent, "UvGround", ground.X.Normalized(), ground.Y.Normalized(), ground.Z.Normalized(), new Vector3(size.X, GroundDepth, size.Y), world, Mat(mask, ink, strength, mirror), true);
		Marks.Add(new Mark(mi, Vector3.Zero, Vector3.Up, Mathf.Max(size.X, size.Y), "ground"));
		return mi;
	}

	/// <summary>Many marks of one kind on the ground in one draw (the footprints to the bunker). Each transform is a
	/// ground basis (Y the normal) at the spot; one mirrored (a left foot from a right) mirrors the lot.</summary>
	public static MultiMeshInstance3D Scatter(Node parent, string name, List<Transform3D> xforms, Vector2 size, Texture2D mask, Color ink, float strength = 1.4f)
	{
		bool mirror = false;
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Box, InstanceCount = xforms.Count };
		for (int i = 0; i < xforms.Count; i++)
		{
			var b = xforms[i].Basis;
			// a mirrored basis would turn the box inside out: unmirror it, and mirror the ink instead
			if (b.Determinant() < 0f) { b.X = -b.X; mirror = true; }
			var box = new Basis(b.X.Normalized() * size.X, b.Y.Normalized() * GroundDepth, b.Z.Normalized() * size.Y);
			mm.SetInstanceTransform(i, new Transform3D(box, xforms[i].Origin));
		}
		var mmi = new MultiMeshInstance3D { Name = name, Multimesh = mm, MaterialOverride = Mat(mask, ink, strength, mirror), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled };
		parent.AddChild(mmi);
		return mmi;
	}
}

