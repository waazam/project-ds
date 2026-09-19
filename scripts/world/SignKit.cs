using Godot;

namespace ProjectDS.World;

/// <summary>
/// Shared pieces for the park's routed wooden signs: arrow-ended boards,
/// square posts with pyramid caps, routed arrows and routed (carved) lettering.
/// All sizes in metres, in the caller's local frame; a board's face points
/// along its basis +Z.
/// </summary>
public static class SignKit
{
	/// <summary>Pale "fresh wood" colour of routed letters.</summary>
	public static readonly Color Carve = new(0.66f, 0.61f, 0.50f);
	/// <summary>Colour of the groove shadow drawn just under the letters.</summary>
	public static readonly Color CarveShadow = new(0.05f, 0.04f, 0.03f);

	private static Font _font;

	/// <summary>A slab serif like the routed lettering on real park signs; falls back to any serif.</summary>
	public static Font RoutedFont
	{
		get
		{
			if (_font != null) return _font;
			_font = new SystemFont
			{
				FontNames = new[] { "Rockwell", "Roboto Slab", "Georgia", "DejaVu Serif", "serif" },
				FontWeight = 600,
				Antialiasing = TextServer.FontAntialiasing.Gray,
				GenerateMipmaps = true,
			};
			return _font;
		}
	}

	/// <summary>
	/// Board with one pointed end. dir = +1 points toward +X, -1 toward -X, 0 = plain rectangle.
	/// Built as a pentagonal prism, thickness along b.Z.
	/// </summary>
	public static void ArrowBoard(MeshKit k, Vector3 c, Basis b, float len, float h, float t, int dir, float uv = 1.2f)
	{
		float hl = len * 0.5f, hh = h * 0.5f, ht = t * 0.5f;
		float tip = dir == 0 ? 0f : Mathf.Min(h * 0.55f, len * 0.3f);
		// outline in the board plane (x, y), counter-clockwise
		Vector2[] o = dir switch
		{
			> 0 => new[] { new Vector2(-hl, -hh), new Vector2(hl - tip, -hh), new Vector2(hl, 0), new Vector2(hl - tip, hh), new Vector2(-hl, hh) },
			< 0 => new[] { new Vector2(-hl, 0), new Vector2(-hl + tip, -hh), new Vector2(hl, -hh), new Vector2(hl, hh), new Vector2(-hl + tip, hh) },
			_ => new[] { new Vector2(-hl, -hh), new Vector2(hl, -hh), new Vector2(hl, hh), new Vector2(-hl, hh) },
		};
		Vector3 P(Vector2 p, float z) => c + b * new Vector3(p.X, p.Y, z);
		Vector2 U(Vector2 p) => new Vector2(p.X * uv, -p.Y * uv);
		Vector3 fz = b.Z.Normalized();
		// faces: fan from the first vertex (the outline is convex)
		for (int i = 1; i < o.Length - 1; i++)
		{
			k.Tri(P(o[0], ht), P(o[i], ht), P(o[i + 1], ht), fz, U(o[0]), U(o[i]), U(o[i + 1]));
			k.Tri(P(o[0], -ht), P(o[i], -ht), P(o[i + 1], -ht), -fz, U(o[0]), U(o[i]), U(o[i + 1]));
		}
		// edges
		for (int i = 0; i < o.Length; i++)
		{
			Vector2 a = o[i], e = o[(i + 1) % o.Length];
			Vector2 d = e - a;
			Vector3 n = (b * new Vector3(d.Y, -d.X, 0)).Normalized();
			float el = d.Length() * uv;
			k.Quad(P(a, ht), P(e, ht), P(e, -ht), P(a, -ht), n,
				new Vector2(0, 0), new Vector2(el, 0), new Vector2(el, t * uv), new Vector2(0, t * uv));
		}
	}

	/// <summary>Routed arrow (shaft + head) lying just proud of a board face at c. len includes the head.</summary>
	public static void RoutedArrow(MeshKit k, Vector3 c, Basis b, float len, float h, int dir)
	{
		if (dir == 0) return;
		float s = dir;
		float head = h * 0.9f, shaftH = h * 0.2f;
		Vector3 fz = b.Z.Normalized();
		Vector3 P(float x, float y) => c + b * new Vector3(x * s, y, 0);
		float x0 = -len * 0.5f, xh = len * 0.5f - head * 0.8f, x1 = len * 0.5f;
		var uv = Vector2.Zero;
		k.Quad(P(x0, -shaftH * 0.5f), P(xh, -shaftH * 0.5f), P(xh, shaftH * 0.5f), P(x0, shaftH * 0.5f), fz,
			uv, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		// open chevron head, like a router would cut it
		float w = shaftH * 0.9f;
		for (int side = -1; side <= 1; side += 2)
		{
			Vector3 a0 = P(x1, 0), a1 = P(xh, side * head * 0.5f);
			Vector3 d = (a1 - a0).Normalized();
			Vector3 perp = fz.Cross(d).Normalized() * w * 0.5f;
			k.Quad(a0 - perp, a1 - perp, a1 + perp, a0 + perp, fz, uv, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		}
	}

	/// <summary>
	/// Square post, base at p (its bottom), with a low pyramid cap.
	/// </summary>
	public static void Post(MeshKit k, Vector3 p, float height, float w, float uv = 2f, float capH = -1f)
	{
		if (capH < 0f) capH = w * 0.45f;
		k.Box(p + new Vector3(0, height * 0.5f, 0), new Vector3(w, height, w), uv);
		k.Cylinder(p + new Vector3(0, height, 0), p + new Vector3(0, height + capH, 0), w * 0.5f * Mathf.Sqrt2, 0f, 4, true, uv, Mathf.Pi / 4f);
	}

	/// <summary>
	/// Routed lettering: a pale label with a thin dark groove shadow just above-left of it.
	/// emHeight is the font em size in metres (cap height is about 0.7 of it).
	/// </summary>
	public static Label3D Text(Node parent, string text, Vector3 pos, Basis b, float emHeight, Color? color = null,
		HorizontalAlignment align = HorizontalAlignment.Center, bool shadow = true)
	{
		const int size = 64;
		float px = emHeight / size;
		Vector3 fz = b.Z.Normalized();
		Label3D Make(Color c, Vector3 at)
		{
			var l = new Label3D
			{
				Text = text, FontSize = size, PixelSize = px, Modulate = c, Font = RoutedFont,
				Transform = new Transform3D(b, at),
				Shaded = true, AlphaCut = Label3D.AlphaCutMode.Discard, OutlineSize = 0,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				HorizontalAlignment = align, DoubleSided = false,
			};
			parent.AddChild(l);
			return l;
		}
		if (shadow)
		{
			// the groove's shaded wall: offset up-left by a few percent of the em
			Vector3 off = b * new Vector3(-emHeight * 0.035f, emHeight * 0.045f, 0);
			var sh = Make(CarveShadow, pos + off + fz * 0.0015f);
			sh.RenderPriority = 0;
		}
		var main = Make(color ?? Carve, pos + fz * 0.003f);
		main.RenderPriority = 1;
		return main;
	}
}
