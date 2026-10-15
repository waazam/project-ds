using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The library's bookcases and their books (2026-10-14h, the interiors pass: the look survey's "every bay the same row,
/// the spines candy stripes at even height, no gaps or leaning books"). Each bay of a run is its own: its shelves set at
/// one of a few heights (adjustable shelves, moved over the years), some standing on a panelled cupboard (one or two
/// of its doors left open), so no two neighbours line up. Each shelf is filled its own way: packed tight with a set of
/// matching volumes in it now and then; half empty, the last few leaning over on their neighbours; with piles of books
/// laid flat among the upright ones; or nearly bare. The books are old: faded cloth and scuffed calf in a muddy range
/// (madder, bottle green, navy gone grey, tan buckram, black), in three bindings (banded calf, plain cloth, cloth with a
/// paper label), their heights from octavo to folio, pulled forward to the shelf's edge or pushed back, the dust grey
/// on whatever faces up (<c>book_cloth.gdshader</c>).
/// </summary>
public partial class Library
{
	/// <summary>Books to draw: per binding, where each one is and its cloth.</summary>
	private sealed class BookList
	{
		public readonly List<Transform3D>[] Xf = { new(), new(), new() };
		public readonly List<Color>[] Col = { new(), new(), new() };
		public void Add(Transform3D xf, Color c, int kind) { Xf[kind].Add(xf); Col[kind].Add(c); }
		public void RemoveWhere(System.Func<Vector3, bool> f)
		{
			for (int k = 0; k < 3; k++)
				for (int i = Xf[k].Count - 1; i >= 0; i--)
					if (f(Xf[k][i].Origin)) { Xf[k].RemoveAt(i); Col[k].RemoveAt(i); }
		}
		public int Count => Xf[0].Count + Xf[1].Count + Xf[2].Count;
	}

	/// <summary>The bays' shelf heights: the standard (the journal's bay and the hinged case keep it), taller gaps low
	/// down, folios at the foot, and shelves over a cupboard (the first number is the cupboard's top).</summary>
	private static readonly float[] Rows = { 0.08f, 0.52f, 0.96f, 1.4f, 1.84f, 2.28f, 2.72f, 3.16f };
	private static readonly float[][] BayRows =
	{
		Rows,
		new[] { 0.08f, 0.56f, 1.02f, 1.46f, 1.9f, 2.38f, 2.86f, 3.24f },
		new[] { 0.08f, 0.62f, 1.16f, 1.6f, 2.04f, 2.46f, 2.9f },
		new[] { 0.9f, 1.34f, 1.78f, 2.22f, 2.66f, 3.1f },
	};
	/// <summary>The crown's underside: the top shelf's books stand under it.</summary>
	private const float CrownUnder = Height - 0.26f;

	/// <summary>Bookcases on every wall, floor to ceiling, filled.</summary>
	private void BuildShelves()
	{
		var runs = new List<(Vector3 a, Vector3 b, Vector3 inward)>
		{
			(new Vector3(-HalfW, 0, 0.3f), new Vector3(-HalfW, 0, Depth - 0.3f), Vector3.Right),
			(new Vector3(HalfW, 0, 0.3f), new Vector3(HalfW, 0, 5.4f), Vector3.Left),
			(new Vector3(HalfW, 0, 7.6f), new Vector3(HalfW, 0, Depth - 0.3f), Vector3.Left),
			(new Vector3(-HalfW + 0.3f, 0, Depth), new Vector3(-0.66f, 0, Depth), Vector3.Forward),
			(new Vector3(0.66f, 0, Depth), new Vector3(HalfW - 0.3f, 0, Depth), Vector3.Forward),
			(new Vector3(-HalfW + 0.3f, 0, 0), new Vector3(-0.95f, 0, 0), Vector3.Back),
			(new Vector3(0.95f, 0, 0), new Vector3(HalfW - 0.3f, 0, 0), Vector3.Back),
		};
		var frame = new MeshKit();
		frame.Mat(_wood);
		var books = new BookList();
		foreach (var (a, b, inw) in runs)
			Shelves(frame, books, a, b, inw, _body, false);
		frame.CommitTo(this, "Bookcases", true);
		// the gap where the journal lies
		books.RemoveWhere(InJournalSlot);
		// and the books that have come down: piles on the floor at the shelves' feet, a few lying where they fell
		FloorBooks(books);
		AddBooks(this, books, "Books");
	}

	private const float ShelfDepth = 0.34f;

	/// <summary>One run of bookcase from a to b against a wall (inw points into the room), and its books.
	/// <paramref name="hinged"/>: the bookcase that swings open (its bay the standard one, packed full, its books at
	/// the back of the shelf as they always were, so the one that juts still juts).</summary>
	private void Shelves(MeshKit frame, BookList books, Vector3 a, Vector3 b, Vector3 inw, StaticBody3D body, bool hinged)
	{
		Vector3 along = (b - a).Normalized();
		float len = (b - a).Length();
		Vector3 mid = (a + b) * 0.5f + inw * ShelfDepth * 0.5f;
		Vector3 alongSize = along.Abs() * len, depthSize = inw.Abs() * ShelfDepth;
		// (2026-10-15) the crown, plinth, uprights and shelves modelled in Blender (church_props.py lib_cornice, lib_upright,
		// lib_shelf: a metre of moulding or board scaled along its run; X along, the face to -Z, turned to face the room);
		// the boxes are their fallback. The back stays a board.
		var face = new Basis(Vector3.Up.Cross(-inw), Vector3.Up, -inw);
		bool model = FixtureModels.Have("lib_cornice", "lib_upright", "lib_shelf");
		frame.Color = Colors.White;
		BuildKit.Box(frame, mid + Vector3.Up * (Height * 0.5f - 0.1f) - inw * (ShelfDepth * 0.5f - 0.01f), alongSize + inw.Abs() * 0.02f + Vector3.Up * (Height - 0.2f));   // the back
		if (model)
			FurnitureKit.Add(frame, "lib_cornice", new Transform3D(face * Basis.FromScale(new Vector3(len, 1, 1)), (a + b) * 0.5f + inw * ShelfDepth * 0.5f),
				new Dictionary<string, (Material, Color)> { ["wood"] = (_wood, Colors.White), ["lip"] = (_wood, Colors.White * 0.85f), ["plinth"] = (_wood, Colors.White * 0.7f) });
		else
		{
		BuildKit.Box(frame, mid + Vector3.Up * (Height - 0.2f), alongSize + depthSize + inw.Abs() * 0.05f + Vector3.Up * 0.12f);   // the crown
		// a moulded lip under the crown and a plinth along the foot
		frame.Color = Colors.White * 0.85f;
		BuildKit.Box(frame, mid + inw * (ShelfDepth * 0.5f + 0.02f) + Vector3.Up * (Height - 0.285f), alongSize + inw.Abs() * 0.03f + Vector3.Up * 0.05f);
		frame.Color = Colors.White * 0.7f;
		BuildKit.Box(frame, mid + inw * (ShelfDepth * 0.5f - 0.012f) + Vector3.Up * 0.045f, alongSize + inw.Abs() * 0.024f + Vector3.Up * 0.09f);
		frame.Color = Colors.White;
		}
		int uprights = Mathf.Max(1, Mathf.RoundToInt(len / 1.0f));
		float seg = len / uprights;
		for (int u = 0; u <= uprights; u++)
		{
			Vector3 p = a + along * (seg * u) + inw * ShelfDepth * 0.5f + Vector3.Up * ((Height - 0.2f) * 0.5f);
			if (model)
			{
				FurnitureKit.Add(frame, "lib_upright", new Transform3D(face, p with { Y = 0 }),
					new Dictionary<string, (Material, Color)> { ["wood"] = (_wood, Colors.White), ["rubbed"] = (_wood, Colors.White * 1.08f) });
				continue;
			}
			BuildKit.Box(frame, p, along.Abs() * 0.045f + depthSize + Vector3.Up * (Height - 0.2f));
			// the upright's front face, a shade paler where hands have rubbed it
			frame.Color = Colors.White * 1.08f;
			BuildKit.Box(frame, p + inw * (ShelfDepth * 0.5f + 0.006f), along.Abs() * 0.055f + inw.Abs() * 0.012f + Vector3.Up * (Height - 0.2f));
			frame.Color = Colors.White;
		}
		body?.AddChild(new CollisionShape3D { Position = mid + Vector3.Up * (Height * 0.5f), Shape = new BoxShape3D { Size = alongSize + depthSize + Vector3.Up * Height } });
		var doorBasis = Basis.LookingAt(inw, Vector3.Up);
		for (int u = 0; u < uprights; u++)
		{
			float s0 = seg * u + 0.0225f, s1 = seg * (u + 1) - 0.0225f, w = s1 - s0;
			Vector3 bayMid = a + along * ((s0 + s1) * 0.5f) + inw * ShelfDepth * 0.5f;
			bool journal = InJournalSlot(bayMid with { Y = 1.5f } - inw * 0.1f) || (Mathf.Abs(bayMid.Z - JournalZ) < seg * 0.5f && inw == Vector3.Right);
			int layout = hinged || journal ? 0 : PickLayout();
			float[] rows = BayRows[layout];
			// the shelves: each bay's its own boards, a hair different in tone
			frame.Color = Colors.White * (0.88f + 0.16f * _rng.Randf());
			foreach (float y in rows)
				if (model)
					FurnitureKit.Add(frame, "lib_shelf", new Transform3D(face * Basis.FromScale(new Vector3(w, 1, 1)), bayMid + Vector3.Up * (y - 0.012f)),
						new Dictionary<string, (Material, Color)> { ["wood"] = (_wood, frame.Color) });
				else
					BuildKit.Box(frame, bayMid + Vector3.Up * (y - 0.012f), along.Abs() * w + depthSize + Vector3.Up * 0.024f);
			frame.Color = Colors.White;
			if (layout == 3) Cupboard(frame, bayMid, along, inw, w, rows[0], doorBasis);
			// the books, a shelf at a time
			for (int r = 0; r < rows.Length; r++)
			{
				float y = rows[r], top = r + 1 < rows.Length ? rows[r + 1] - 0.024f : CrownUnder;
				if (top - y < 0.16f) continue;
				FillShelf(books, a, along, inw, s0 + 0.004f, s1 - 0.004f, y, top - y, hinged, !hinged);
			}
		}
	}

	private int PickLayout()
	{
		float r = _rng.Randf();
		return r < 0.36f ? 0 : r < 0.58f ? 1 : r < 0.78f ? 2 : 3;
	}

	/// <summary>A bay's foot as a panelled cupboard: two doors under its first shelf, now and then one stood open on
	/// the dark inside.</summary>
	private void Cupboard(MeshKit frame, Vector3 bayMid, Vector3 along, Vector3 inw, float w, float topY, Basis doorBasis)
	{
		float dh = topY - 0.12f, dw = (w - 0.02f) * 0.5f;
		Vector3 front = bayMid + inw * (ShelfDepth * 0.5f - 0.015f);
		bool open = _rng.Randf() < 0.35f;
		int openSide = _rng.Randf() < 0.5f ? -1 : 1;
		frame.Color = Colors.White * 0.95f;
		foreach (int sd in new[] { -1, 1 })
		{
			Vector3 c = front + along * (sd * (dw * 0.5f + 0.005f)) + Vector3.Up * (0.09f + dh * 0.5f);
			if (open && sd == openSide)
			{
				// swung out on its hinge at the bay's side
				float ang = _rng.RandfRange(0.7f, 1.6f);
				Vector3 hinge = front + along * (sd * (dw + 0.01f));
				var rot = new Basis(Vector3.Up, sd * (inw.Cross(along).Y > 0 ? 1f : -1f) * ang);
				Vector3 off = rot * (along * (-sd * dw * 0.5f));
				StationKit.PanelDoor(frame, hinge + off + inw * 0.012f + Vector3.Up * (0.09f + dh * 0.5f), dw, dh, 0.022f, rot * doorBasis, 1);
				// the dark inside
				frame.Color = Colors.White * 0.25f;
				BuildKit.Box(frame, bayMid + along * (sd * dw * 0.5f) + Vector3.Up * (0.09f + dh * 0.5f) - inw * 0.02f, along.Abs() * (dw - 0.02f) + inw.Abs() * 0.24f + Vector3.Up * (dh - 0.02f));
				frame.Color = Colors.White * 0.95f;
				continue;
			}
			StationKit.PanelDoor(frame, c, dw, dh, 0.022f, doorBasis, 1);
			// its knob
			frame.Color = Colors.White * 0.6f;
			frame.Cylinder(c + along * (-sd * (dw * 0.5f - 0.06f)) + inw * 0.012f, c + along * (-sd * (dw * 0.5f - 0.06f)) + inw * 0.04f, 0.014f, 0.016f, 6, true);
			frame.Color = Colors.White * 0.95f;
		}
		frame.Color = Colors.White;
	}

	// ------------------------------------------------------------------ the books

	// (sRGB, as a book's cloth would be picked: converted to the linear the instance colour wants in BookColor)
	private static readonly Color[] Cloths =
	{
		new(0.45f, 0.14f, 0.11f), new(0.48f, 0.26f, 0.22f), new(0.26f, 0.32f, 0.24f), new(0.4f, 0.38f, 0.24f),
		new(0.22f, 0.27f, 0.37f), new(0.34f, 0.35f, 0.37f), new(0.58f, 0.47f, 0.33f), new(0.66f, 0.57f, 0.43f),
		new(0.12f, 0.11f, 0.1f), new(0.6f, 0.47f, 0.22f), new(0.55f, 0.38f, 0.34f), new(0.46f, 0.44f, 0.39f),
		new(0.3f, 0.21f, 0.28f), new(0.36f, 0.24f, 0.16f), new(0.17f, 0.2f, 0.15f), new(0.42f, 0.2f, 0.12f),
	};
	private static readonly Color[] Calfs =
	{
		new(0.38f, 0.2f, 0.11f), new(0.42f, 0.13f, 0.09f), new(0.18f, 0.12f, 0.09f), new(0.2f, 0.24f, 0.16f),
		new(0.52f, 0.35f, 0.2f), new(0.27f, 0.15f, 0.1f),
	};

	/// <summary>An old binding's colour: its cloth, a little lighter or darker, its saturation drained, faded toward a
	/// dusty brown by <paramref name="fade"/> and a bit more by chance.</summary>
	private Color BookColor(int kind, float fade)
	{
		var pal = kind == 0 ? Calfs : Cloths;
		var c = pal[_rng.RandiRange(0, pal.Length - 1)] * _rng.RandfRange(0.8f, 1.1f);
		float l = (c.R + c.G + c.B) / 3f;
		c = c.Lerp(new Color(l, l, l), 0.2f);
		c = c.Lerp(new Color(0.45f, 0.4f, 0.33f), Mathf.Clamp(fade + _rng.RandfRange(0f, 0.22f), 0f, 0.6f));
		return c.SrgbToLinear();
	}

	private int BookKind() { float r = _rng.Randf(); return r < 0.24f ? 0 : r < 0.74f ? 1 : 2; }

	/// <summary>A book's height for a shelf of clear height <paramref name="gap"/>: mostly octavos, some quartos, the
	/// odd folio where it fits.</summary>
	private float BookHeight(float gap)
	{
		float r = _rng.Randf();
		float h = r < 0.55f ? _rng.RandfRange(0.19f, 0.245f) : r < 0.86f ? _rng.RandfRange(0.25f, 0.3f) : _rng.RandfRange(0.31f, 0.4f);
		float max = gap - 0.025f;
		if (h > max) h = max - _rng.RandfRange(0f, 0.04f);
		return Mathf.Max(h, 0.14f);
	}

	private float BookThick() { float r = _rng.Randf(); return r < 0.15f ? _rng.RandfRange(0.014f, 0.022f) : r < 0.85f ? _rng.RandfRange(0.022f, 0.045f) : _rng.RandfRange(0.045f, 0.07f); }

	/// <summary>A book standing on a shelf: its footprint along the shelf starts at <paramref name="sx"/>; tipped by
	/// <paramref name="tilt"/> (its top toward +along when positive), resting on its lower corner. Returns its transform
	/// and how far along the shelf it reaches.</summary>
	private static Transform3D Stand(Vector3 a, Vector3 along, Vector3 inw, float sx, float y, float t, float h, float d, float depthC, float tilt, out float end)
	{
		float c = Mathf.Cos(tilt), s = Mathf.Abs(Mathf.Sin(tilt));
		float width = t * c + h * s;
		end = sx + width;
		Vector3 upAx = Vector3.Up * c + along * Mathf.Sin(tilt);
		Vector3 zAx = -inw;
		Vector3 xAx = upAx.Cross(zAx).Normalized();
		Vector3 centre = a + along * (sx + width * 0.5f) + inw * depthC + Vector3.Up * (y + (t * s + h * c) * 0.5f);
		return new Transform3D(new Basis(xAx * t, upAx * h, zAx * d), centre);
	}

	/// <summary>A book lying flat, its spine toward <paramref name="spineDir"/>, at <paramref name="centre"/> (its
	/// middle), turned <paramref name="spin"/> about the vertical.</summary>
	private static Transform3D Lie(Vector3 centre, Vector3 spineDir, float t, float h, float d, float spin = 0f)
	{
		var r = new Basis(Vector3.Up, spin);
		Vector3 zAx = -(r * spineDir).Normalized();
		Vector3 xAx = Vector3.Up;
		Vector3 yAx = zAx.Cross(xAx).Normalized();   // (x cross y = z)
		return new Transform3D(new Basis(xAx * t, yAx * h, zAx * d), centre);
	}

	/// <summary>One shelf's books, from s0 to s1 along the run, standing on y with <paramref name="gap"/> clear above.</summary>
	private void FillShelf(BookList bl, Vector3 a, Vector3 along, Vector3 inw, float s0, float s1, float y, float gap, bool packed, bool front)
	{
		float fade = _rng.RandfRange(0f, 0.2f) + (y > 2.6f ? 0.12f : 0f);   // (the top shelves, nearest the light and the dust)
		float r = _rng.Randf();
		int mode = packed ? 0 : r < 0.52f ? 0 : r < 0.7f ? 1 : r < 0.83f ? 2 : r < 0.91f ? 3 : 4;
		float Dc(float d) => front ? ShelfDepth - 0.015f - d * 0.5f - (_rng.Randf() < 0.07f ? _rng.RandfRange(0.04f, 0.09f) : _rng.RandfRange(0f, 0.025f)) : 0.03f + d * 0.5f;
		// a set of matching volumes, or one book
		int setLeft = 0; float setH = 0, setT = 0; Color setC = default; int setK = 1;
		float s = s0, prevH = 0f;
		void Upright(float limit, ref float sRef)
		{
			while (sRef < limit - 0.015f)
			{
				if (setLeft == 0 && mode != 4 && _rng.Randf() < 0.07f)
				{
					setLeft = _rng.RandiRange(4, 11); setH = BookHeight(gap); setT = _rng.RandfRange(0.026f, 0.042f); setK = _rng.Randf() < 0.4f ? 0 : 1; setC = BookColor(setK, fade);
				}
				if (mode == 4 && setLeft == 0) { setLeft = _rng.RandiRange(6, 16); setH = BookHeight(gap); setT = _rng.RandfRange(0.024f, 0.05f); setK = _rng.Randf() < 0.5f ? 0 : 1; setC = BookColor(setK, fade); }
				bool inSet = setLeft > 0;
				float t = inSet ? setT : BookThick(), h = inSet ? setH : BookHeight(gap);
				float d = Mathf.Min(h * _rng.RandfRange(0.62f, 0.8f), ShelfDepth - 0.04f);
				if (sRef + t > limit) break;
				if (!inSet && !packed && _rng.Randf() < 0.025f) { sRef += _rng.RandfRange(0.05f, 0.16f); continue; }   // one taken out
				// (2026-10-15: the hinged case's packed books lean a hair too, now and then; they stood dead upright, every
				// one, beside shelves that sag and lean: a giveaway)
				float tilt = _rng.Randf() < (packed ? 0.16f : 0.04f) ? _rng.RandfRange(-0.06f, 0.06f) : 0f;
				int kind = inSet ? setK : BookKind();
				var c = inSet ? setC * _rng.RandfRange(0.94f, 1.04f) : BookColor(kind, fade);
				bl.Add(Stand(a, along, inw, sRef, y, t, h, d, Dc(d), tilt, out float end), c, kind);
				sRef = end + 0.002f;
				prevH = h;
				if (inSet) setLeft--;
			}
		}
		// a few leaning over on what stands before them (toward -along)
		void Leaners(int n, ref float sRef, float limit)
		{
			for (int i = 0; i < n; i++)
			{
				float t = BookThick(), h = BookHeight(gap), d = Mathf.Min(h * _rng.RandfRange(0.62f, 0.8f), ShelfDepth - 0.04f);
				float tilt = -_rng.RandfRange(0.14f, 0.42f) * (i == 0 ? 1f : 0.85f);
				float width = t * Mathf.Cos(tilt) + h * Mathf.Abs(Mathf.Sin(tilt));
				// its top against the one before (that one's height caps how far it can lean)
				float start = sRef + 0.002f - Mathf.Max(0f, h * Mathf.Abs(Mathf.Sin(tilt)) - (prevH > 0f ? 0.004f : 0f)) * (prevH > 0f ? 0.85f : 0f);
				if (start + width > limit) break;
				int kind = BookKind();
				bl.Add(Stand(a, along, inw, Mathf.Max(start, s0), y, t, h, d, Dc(d), tilt, out float end), BookColor(kind, fade), kind);
				sRef = end + 0.002f;
				prevH = h * Mathf.Cos(tilt);
			}
		}
		// a pile laid flat, spines out, a little askew
		void Pile(float at, int n)
		{
			float yy = y;
			float h0 = _rng.RandfRange(0.2f, 0.3f);
			for (int i = 0; i < n; i++)
			{
				float t = BookThick(), h = Mathf.Clamp(h0 + _rng.RandfRange(-0.04f, 0.03f), 0.16f, 0.32f), d = Mathf.Min(h * _rng.RandfRange(0.66f, 0.8f), ShelfDepth - 0.05f);
				if (yy + t > y + gap - 0.01f) break;
				int kind = BookKind();
				Vector3 c = a + along * at + inw * (front ? ShelfDepth - 0.02f - d * 0.5f : 0.03f + d * 0.5f) + Vector3.Up * (yy + t * 0.5f);
				bl.Add(Lie(c, inw, t, h, d, _rng.RandfRange(-0.12f, 0.12f)), BookColor(kind, fade + 0.05f), kind);
				yy += t;
			}
		}
		float len = s1 - s0;
		switch (mode)
		{
			case 0:   // packed, the end of it leaning over into whatever room is left
				Upright(s1, ref s);
				if (s1 - s > 0.05f) Leaners(1, ref s, s1);
				break;
			case 1:   // part full, the last few leaning over
			{
				float stop = s0 + len * _rng.RandfRange(0.3f, 0.72f);
				Upright(stop, ref s);
				Leaners(_rng.RandiRange(1, 3), ref s, s1);
				if (s1 - s > 0.32f && _rng.Randf() < 0.5f) Pile(s1 - 0.17f, _rng.RandiRange(2, 5));
				break;
			}
			case 2:   // piles laid flat, uprights between
			{
				float p0 = s0 + 0.16f;
				Pile(p0, _rng.RandiRange(3, 7));
				s = s0 + 0.33f;
				float stop = s1 - (len > 0.7f && _rng.Randf() < 0.6f ? 0.34f : 0f);
				Upright(stop, ref s);
				if (stop < s1) Pile(s1 - 0.16f, _rng.RandiRange(2, 6));
				break;
			}
			case 3:   // nearly bare: a pile, perhaps one leaning on the side of the case
			{
				if (_rng.Randf() < 0.7f) Pile(s0 + len * _rng.RandfRange(0.25f, 0.75f), _rng.RandiRange(1, 4));
				if (_rng.Randf() < 0.6f)
				{
					float t = BookThick(), h = BookHeight(gap), d = Mathf.Min(h * 0.7f, ShelfDepth - 0.04f);
					// leaning the other way, on the case's upright at the shelf's end
					float tilt = _rng.RandfRange(0.15f, 0.4f);
					float width = t * Mathf.Cos(tilt) + h * Mathf.Sin(tilt);
					int kind = BookKind();
					bl.Add(Stand(a, along, inw, s1 - width - 0.002f, y, t, h, d, Dc(d), tilt, out _), BookColor(kind, fade + 0.1f), kind);
				}
				break;
			}
			default:   // a long set of matching volumes
				Upright(s1, ref s);
				break;
		}
	}

	/// <summary>Books that have come off the shelves: piles at their feet (by the walls, out of the ways through) and a
	/// few lying where they fell, one open on its face.</summary>
	private void FloorBooks(BookList bl)
	{
		// the piles: (where, how many, which way their spines face)
		var piles = new (Vector3 at, int n, float spin)[]
		{
			(new Vector3(-4.62f, 0, 12.2f), 9, 0.3f), (new Vector3(-4.75f, 0, 11.7f), 5, -0.5f),
			(new Vector3(4.82f, 0, 10.7f), 7, 1.9f), (new Vector3(-3.3f, 0, 0.6f), 6, 2.8f),
			(new Vector3(2.95f, 0, 0.62f), 8, -2.9f), (new Vector3(-4.78f, 0, 3.1f), 6, 1.4f),
			(new Vector3(-2.05f, 0, 12.35f), 4, 0.1f), (new Vector3(4.78f, 0, 3.6f), 5, -1.2f),
		};
		foreach (var (at, n, spin) in piles)
		{
			float y = 0f, h0 = _rng.RandfRange(0.22f, 0.32f);
			Vector3 drift = Vector3.Zero;
			for (int i = 0; i < n; i++)
			{
				float t = BookThick(), h = Mathf.Clamp(h0 + _rng.RandfRange(-0.06f, 0.04f) - i * 0.006f, 0.15f, 0.42f), d = h * _rng.RandfRange(0.66f, 0.8f);
				drift += new Vector3(_rng.RandfRange(-0.02f, 0.02f), 0, _rng.RandfRange(-0.02f, 0.02f));
				int kind = BookKind();
				bl.Add(Lie(at + drift + Vector3.Up * (y + t * 0.5f), Vector3.Forward, t, h, d, spin + _rng.RandfRange(-0.25f, 0.25f)), BookColor(kind, 0.3f), kind);
				y += t;
			}
		}
		// fallen singly: lying flat at odd angles, one on its spine
		foreach (var at in new[] { new Vector3(-4.45f, 0, 3.75f), new Vector3(2.3f, 0, 12.2f), new Vector3(4.45f, 0, 11.3f), new Vector3(-3.9f, 0, 0.75f), new Vector3(3.6f, 0, 1.05f) })
		{
			float t = BookThick(), h = BookHeight(0.4f), d = h * 0.72f;
			int kind = BookKind();
			bl.Add(Lie(at + Vector3.Up * (t * 0.5f), Vector3.Forward, t, h, d, _rng.RandfRange(0f, Mathf.Tau)), BookColor(kind, 0.3f), kind);
		}
	}

	private static void AddBooks(Node3D owner, BookList bl, string name)
	{
		for (int k = 0; k < 3; k++)
		{
			var xf = bl.Xf[k];
			if (xf.Count == 0) continue;
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BookMeshes[k], InstanceCount = xf.Count };
			for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i]); mm.SetInstanceColor(i, bl.Col[k][i]); }
			owner.AddChild(new MultiMeshInstance3D { Name = $"{name}{k}", Multimesh = mm });
		}
	}

	private static Mesh[] _bookMeshes;
	private static ShaderMaterial _cloth, _pages;

	/// <summary>The binding's cloth and the page block (<c>book_cloth.gdshader</c>: the dust on their tops).</summary>
	private static ShaderMaterial BookMat(bool pages)
	{
		ref var m = ref pages ? ref _pages : ref _cloth;
		if (m != null) return m;
		m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/book_cloth.gdshader"), ResourceName = pages ? "book_pages" : "book_cloth" };
		m.SetShaderParameter("grain", ProcTextures.Grime());
		m.SetShaderParameter("pages", pages);
		if (pages) { m.SetShaderParameter("base", new Color(0.5f, 0.45f, 0.35f)); m.SetShaderParameter("roughness", 0.95f); m.SetShaderParameter("specular", 0.15f); }
		return m;
	}

	/// <summary>The three bindings, each a hardback in a unit box (scaled per book: X its thickness, Y its height, Z its
	/// depth; its spine toward -Z, the room): the boards, the spine rounded over the page block, the page block set in.
	/// 0: calf with a pair of raised bands; 1: plain cloth, its spine flatter; 2: cloth with a paper label at its head.
	/// The cloth takes the book's own colour (the instance colour); the pages don't.</summary>
	private static Mesh[] BookMeshes
	{
		get
		{
			if (_bookMeshes != null) return _bookMeshes;
			_bookMeshes = new Mesh[3];
			for (int kind = 0; kind < 3; kind++)
			{
				// (2026-10-15) the bindings modelled in Blender (tools/Blender/church_props.py lib_book_0..2, the same unit
				// box); the code below is their fallback
				var model = FurnitureKit.Mesh($"lib_book_{kind}", new Dictionary<string, Material> { ["cloth"] = BookMat(false), ["pages"] = BookMat(true) });
				if (model != null) { _bookMeshes[kind] = model; continue; }
				var k = new MeshKit();
				k.Color = Colors.White;
				k.Mat(BookMat(true));
				k.Box(new Vector3(0, 0, 0.03f), new Vector3(0.82f, 0.94f, 0.92f), 1f);
				if (kind == 2) k.Box(new Vector3(0, 0.3f, -0.555f), new Vector3(0.5f, 0.16f, 0.02f), 1f);   // the label
				k.Mat(BookMat(false));
				foreach (float sx in new[] { -0.45f, 0.45f })
					k.Box(new Vector3(sx, 0, 0.0f), new Vector3(0.1f, 1f, 1f), 1f);
				// the spine: a half round across the page block's front (squashed flat: a book's spine bulges only a little)
				k.Xf = new Transform3D(Basis.FromScale(new Vector3(1f, 1f, kind == 0 ? 0.12f : 0.08f)), new Vector3(0, 0, -0.5f));
				k.Cylinder(new Vector3(0, -0.5f, 0), new Vector3(0, 0.5f, 0), 0.5f, 0.5f, 8, true);
				if (kind == 0)
					foreach (float y in new[] { -0.3f, -0.1f, 0.1f, 0.3f })
						k.Cylinder(new Vector3(0, y - 0.02f, 0), new Vector3(0, y + 0.02f, 0), 0.54f, 0.54f, 8, false);
				k.Xf = Transform3D.Identity;
				_bookMeshes[kind] = k.Commit();
			}
			return _bookMeshes;
		}
	}
}
