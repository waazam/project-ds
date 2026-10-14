using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>What kind of hand left a print in the ink, and how it was put down.</summary>
public enum HandKind
{
	/// <summary>A right hand pressed flat, fingers together-ish: the plain print.</summary>
	Pressed,
	/// <summary>Fingers and thumb spread wide (pushing, bracing).</summary>
	Splayed,
	/// <summary>A grip on an edge: fingers and the top of the palm, the heel barely touching.</summary>
	Partial,
	/// <summary>Only the fingertips and the thumb's tip, each dragged a little (feeling the way in the dark).</summary>
	Fingertips,
	/// <summary>Pressed, then slid down: the print smeared into streaks below it.</summary>
	Dragged,
	/// <summary>A child's: short fingers, a round palm.</summary>
	Small,
	/// <summary>The thing's: a narrow palm, long knuckled fingers, hooked talons that scored past the tips.</summary>
	Claw,
	/// <summary>The thing's, spread wide, the talons dug in.</summary>
	ClawSplayed,
	/// <summary>The thing's, raked down: four talon gouges below a smeared print.</summary>
	ClawDragged,
}

/// <summary>
/// The handprints (2026-10-13, the owner: "the blacklight hand prints don't look like hands, look up references").
/// Built from how a real palm print is made (forensic palm-print anatomy, Jain &amp; Feng's latent palmprint work): what
/// prints are the raised pads, and what doesn't are the creases and the hollow between them:
/// <list type="bullet">
/// <item>the palm: three interdigital pads in a row under the fingers, the thenar pad (the ball of the thumb, big,
/// low on the thumb side), the hypothenar pad (the long one down the outside edge), the heel; the hollow in the middle
/// barely prints;</item>
/// <item>the creases as gaps: the heart line under the pads, the head line across, the life line curving round the
/// thenar;</item>
/// <item>four fingers of three pads each, with gaps at the joints, the fingertip pads the strongest (they press
/// hardest); the thumb set low on the thenar and angled out, two pads;</item>
/// <item>pressure: hard pads print solid, light ones break up into blotches; prints are often partial or smeared.</item>
/// </list>
/// All right hands, palm to the surface, fingers up (the placement mirrors them for a left). The creature's
/// (<see cref="HandKind.Claw"/> and its kin) are the same anatomy gone wrong: a narrow palm, fingers half again as
/// long with an extra joint, knobbed knuckles that print harder than the pads, and a hooked talon on every finger that
/// scored the surface past the tip.
/// </summary>
public static partial class UvInk
{
	/// <summary>A tapered capsule of pressure: a pad, a finger segment, a talon.</summary>
	/// <summary><see cref="Flat"/> &gt; 0: a rounded rectangle from A to B (a finger's segment between its creases), its
	/// corners rounder the smaller it is; 0: a capsule (a pad, a talon).</summary>
	private readonly record struct Pad(Vector2 A, Vector2 B, float R0, float R1, float P, float Flat = 0f);

	/// <summary>A handprint of a kind; <paramref name="seed"/> varies it (the angles of the fingers, the pressure).</summary>
	public static Texture2D Hand(HandKind kind = HandKind.Pressed, int seed = 0)
	{
		string key = $"uv_hand3_{kind}_{seed}";
		if (_tex.TryGetValue(key, out var t)) return t;
		var tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("UvInk_" + key) is { } cached) { t = ImageTexture.CreateFromImage(cached); _tex[key] = t; Systems.TexGen.Stop(tg); return t; }

		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + (int)kind * 131 + 17) };
		var pads = new List<Pad>();
		var creases = new List<List<Vector2>>();
		bool claw = kind is HandKind.Claw or HandKind.ClawSplayed or HandKind.ClawDragged;
		if (claw) ClawHand(pads, rng, kind);
		else HumanHand(pads, creases, rng, kind);

		// the dragged ones: the hand small at the top of the mark, the smear running down below it
		bool dragged = kind is HandKind.Dragged or HandKind.ClawDragged;
		float scale = dragged ? 0.6f : 1f;
		Vector2 origin = dragged ? new Vector2(0.5f + rng.RandfRange(-0.06f, 0.06f), 0.3f) : new Vector2(0.5f, 0.5f);
		Vector2 drag = dragged ? new Vector2(rng.RandfRange(-0.08f, 0.08f), 0.58f) : Vector2.Zero;
		if (dragged)
			for (int i = 0; i < pads.Count; i++)
			{
				var pd = pads[i];
				pads[i] = pd with { A = origin + (pd.A - new Vector2(0.5f, 0.5f)) * scale, B = origin + (pd.B - new Vector2(0.5f, 0.5f)) * scale, R0 = pd.R0 * scale, R1 = pd.R1 * scale };
			}
		if (dragged) foreach (var c in creases) for (int i = 0; i < c.Count; i++) c[i] = origin + (c[i] - new Vector2(0.5f, 0.5f)) * scale;
		if (kind == HandKind.ClawDragged)
		{
			// the talons raked on down: four gouges from the tips, wavering, thinning out
			foreach (var pd in pads.ToArray())
			{
				if (pd.R1 > 0.006f || pd.P < 0.95f) continue;   // the talon points (see ClawHand)
				Vector2 at = pd.B;
				float wob = rng.RandfRange(-0.03f, 0.03f);
				int n = 6;
				for (int k = 0; k < n; k++)
				{
					float f0 = k / (float)n, f1 = (k + 1) / (float)n;
					Vector2 a = at + new Vector2(drag.X * f0 + wob * Mathf.Sin(f0 * 5f), 0.7f * f0 * (0.97f - at.Y) / 0.7f);
					Vector2 b = at + new Vector2(drag.X * f1 + wob * Mathf.Sin(f1 * 5f), 0.7f * f1 * (0.97f - at.Y) / 0.7f);
					pads.Add(new Pad(a, b, Mathf.Lerp(0.011f, 0.004f, f0), Mathf.Lerp(0.011f, 0.004f, f1), Mathf.Lerp(0.95f, 0.55f, f1)));
				}
			}
			drag = new Vector2(drag.X, 0.3f);   // the palm smears less far than the talons scored
		}

		const int N = 128;
		var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
		float[] streak = new float[N];
		for (int x = 0; x < N; x++) streak[x] = Mathf.Clamp(0.35f + 0.9f * (Fbm(new Vector2(x * 0.11f, 0.5f), seed + 91) - 0.5f) * 2f, 0f, 1f);
		for (int y = 0; y < N; y++)
			for (int x = 0; x < N; x++)
			{
				var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
				// a ragged edge: the pad outlines wander a little
				float wander = (Fbm(p * 22f, seed + 3) - 0.5f) * 0.016f;
				float pr = Pressure(pads, creases, p, wander);
				if (dragged)
				{
					// the smear: the print slid down; streaky across (some skin pressed harder), fading as it goes
					const int K = 32;
					float sm = 0f;
					for (int k = 1; k <= K; k++)
					{
						float f = k / (float)K;
						float v = Pressure(pads, creases, p - drag * f, wander) * Mathf.Pow(1f - f, 0.8f);
						if (v > sm) sm = v;
					}
					sm *= 0.25f + 0.75f * streak[x];
					pr = Mathf.Max(pr, sm * 0.85f);
				}
				if (kind == HandKind.Fingertips)
				{
					// each tip slid a finger's width down: a short tail under it
					for (int k = 1; k <= 6; k++)
						pr = Mathf.Max(pr, Pressure(pads, creases, p - new Vector2(0f, 0.011f * k), wander) * (1f - k / 11f) * (0.75f + 0.25f * streak[x]));
				}
				// the deposit: hard pressure prints solid, light pressure breaks into blotches, all of it speckled
				float blot = Fbm(p * 11f, seed + 11);
				float ink = pr * (0.72f + 0.62f * (blot - 0.5f));
				float a = Mathf.SmoothStep(0.2f, 0.4f, ink);
				a *= 0.72f + 0.28f * Hash(x, y, seed + 29);
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(a, 0f, 1f)));
			}
		img.GenerateMipmaps();
		Systems.TexCache.Save("UvInk_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(tg);
		_tex[key] = t;
		return t;
	}

	/// <summary>The pressure the hand put on point <paramref name="p"/> (0..1), the creases left out.</summary>
	private static float Pressure(List<Pad> pads, List<List<Vector2>> creases, Vector2 p, float wander)
	{
		float best = 0f;
		foreach (var pd in pads)
		{
			Vector2 ab = pd.B - pd.A;
			float tt = Mathf.Clamp((p - pd.A).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-7f), 0f, 1f);
			float r = Mathf.Lerp(pd.R0, pd.R1, tt);
			float d;
			if (pd.Flat > 0f)
			{
				float len = ab.Length();
				Vector2 ax = ab / Mathf.Max(len, 1e-6f);
				Vector2 rel = p - (pd.A + pd.B) * 0.5f;
				float rc = r * (1f - pd.Flat * 0.75f);
				Vector2 q = new Vector2(Mathf.Abs(rel.Dot(ax)) - len * 0.5f, Mathf.Abs(rel.Dot(new Vector2(-ax.Y, ax.X))) - r) + new Vector2(rc, rc);
				d = new Vector2(Mathf.Max(q.X, 0f), Mathf.Max(q.Y, 0f)).Length() + Mathf.Min(Mathf.Max(q.X, q.Y), 0f) - rc + wander;
			}
			else d = p.DistanceTo(pd.A + ab * tt) - r + wander;
			if (d > 0.006f) continue;
			// fuller in the middle of a pad, lighter at its rim, and a soft edge
			float inside = Mathf.Clamp(-d / (r * 0.55f + 0.004f), 0f, 1f);
			float v = pd.P * (0.45f + 0.55f * inside) * (1f - Mathf.SmoothStep(-0.006f, 0.006f, d));
			if (v > best) best = v;
		}
		if (best <= 0f) return 0f;
		foreach (var c in creases)
		{
			float d = 99f;
			for (int i = 0; i + 1 < c.Count; i++) d = Mathf.Min(d, SegDist(p, c[i], c[i + 1]));
			best *= Mathf.SmoothStep(0.0015f, 0.0055f, d + wander * 0.3f);
		}
		return best;
	}

	private static Vector2 Dir(float degFromUp) { float a = Mathf.DegToRad(degFromUp); return new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); }

	/// <summary>A finger (or thumb) as pads with gaps at the joints, from <paramref name="b"/> along <paramref name="dir"/>.</summary>
	private static void Finger(List<Pad> pads, Vector2 b, Vector2 dir, float len, float r, float[] frac, float[] pres, float bend, float gap = 0.0075f)
	{
		Vector2 at = b;
		float total = 0f;
		foreach (var f in frac) total += f;
		for (int s = 0; s < frac.Length; s++)
		{
			float l = len * frac[s] / total;
			Vector2 end = at + dir * l;
			bool tip = s == frac.Length - 1;
			// the finger narrows a little to its tip
			float rr = r * (1f - 0.08f * s);
			// a pad: the segment between its creases, square-ish where the joints are, the fingertip round
			Vector2 a0 = at + dir * (gap * 0.5f), a1 = end - dir * (gap * 0.5f);
			pads.Add(new Pad(a0, a1, rr, rr * (tip ? 0.95f : 0.97f), pres[s], tip ? 0.05f : 0.6f));
			at = end;
			// each joint turns the finger a little (a relaxed hand's fingers curve)
			float c = Mathf.Cos(bend), sn = Mathf.Sin(bend);
			dir = new Vector2(dir.X * c - dir.Y * sn, dir.X * sn + dir.Y * c);
		}
	}

	private static void HumanHand(List<Pad> pads, List<List<Vector2>> creases, RandomNumberGenerator rng, HandKind kind)
	{
		float splay = kind switch { HandKind.Splayed => 1f, HandKind.Partial => 0.15f, HandKind.Fingertips => 0.5f, _ => 0.3f } + rng.RandfRange(-0.1f, 0.1f);
		bool small = kind == HandKind.Small;
		float fl = small ? 0.78f : 1f, fr = small ? 1.12f : 1f;
		float palmP = kind switch { HandKind.Partial => 0.7f, HandKind.Fingertips => 0f, HandKind.Splayed => 0.75f, _ => 0.85f } * rng.RandfRange(0.9f, 1.05f);
		float tipP = 0.97f, midP = kind == HandKind.Fingertips ? 0.38f : 0.72f, proxP = kind == HandKind.Fingertips ? 0f : 0.74f;
		float yShift = small ? 0.07f : 0f;   // a child's fingers are short: the hand sits lower in its square

		// fingers: base (on the palm's crease), length, radius, angle from straight up (degrees)
		var fingers = new (Vector2 b, float len, float r, float ang, float spread)[]
		{
			(new Vector2(0.385f, 0.478f), 0.335f, 0.040f, -7f, -15f),
			(new Vector2(0.483f, 0.455f), 0.370f, 0.041f, -1f, -4f),
			(new Vector2(0.578f, 0.470f), 0.345f, 0.039f, 5f, 8f),
			(new Vector2(0.664f, 0.514f), 0.270f, 0.034f, 13f, 18f),
		};
		int faint = kind == HandKind.Partial ? rng.RandiRange(0, 3) : -1;
		for (int i = 0; i < 4; i++)
		{
			var (b, len, r, ang, spread) = fingers[i];
			float a = ang + spread * splay + rng.RandfRange(-2.5f, 2.5f);
			float k = i == faint ? 0.45f : 1f;
			b.Y += yShift;
			Finger(pads, b, Dir(a), len * fl, r * fr, new[] { 0.44f, 0.30f, 0.26f }, new[] { proxP * k, midP * k, tipP * (i == faint ? 0.6f : 1f) }, Mathf.DegToRad(rng.RandfRange(-2f, 2f)));
		}
		// the thumb: from the thenar, angled out and up, two pads (only its tip in a fingertip print)
		{
			float a = -48f - 20f * splay + rng.RandfRange(-4f, 4f);
			Vector2 dir = Dir(a);
			Vector2 b = new Vector2(0.352f, 0.648f + yShift);
			float thumbP = kind == HandKind.Partial ? 0.25f : 0.82f;
			Finger(pads, b, dir, 0.27f * fl, 0.047f * fr, new[] { 0.52f, 0.48f }, new[] { kind == HandKind.Fingertips ? 0f : thumbP, kind == HandKind.Partial ? 0.5f : 0.95f }, Mathf.DegToRad(6f));
		}
		if (palmP <= 0f) return;
		Vector2 o = new(0f, yShift);
		float heel = kind == HandKind.Partial ? 0.4f : 1f;
		// the palm's pads: three interdigital under the fingers, the hypothenar, the thenar, the heel, the faint hollow
		pads.Add(new Pad(new Vector2(0.39f, 0.545f) + o, new Vector2(0.455f, 0.535f) + o, 0.058f * fr, 0.058f * fr, palmP * 0.98f));
		pads.Add(new Pad(new Vector2(0.515f, 0.53f) + o, new Vector2(0.575f, 0.54f) + o, 0.056f * fr, 0.056f * fr, palmP * 0.92f));
		pads.Add(new Pad(new Vector2(0.615f, 0.55f) + o, new Vector2(0.665f, 0.585f) + o, 0.054f * fr, 0.054f * fr, palmP * 0.92f));
		pads.Add(new Pad(new Vector2(0.672f, 0.64f) + o, new Vector2(0.645f, 0.85f) + o, 0.07f * fr, 0.064f * fr, palmP * 0.85f * Mathf.Lerp(1f, heel, 0.6f)));
		pads.Add(new Pad(new Vector2(0.395f, 0.70f) + o, new Vector2(0.45f, 0.855f) + o, 0.085f * fr, 0.075f * fr, palmP * 0.9f * Mathf.Lerp(1f, heel, 0.5f)));
		pads.Add(new Pad(new Vector2(0.475f, 0.905f) + o, new Vector2(0.6f, 0.9f) + o, 0.05f, 0.048f, palmP * 0.62f * heel));
		pads.Add(new Pad(new Vector2(0.52f, 0.62f) + o, new Vector2(0.56f, 0.73f) + o, 0.085f, 0.08f, palmP * 0.52f));
		// the creases print as gaps: the heart line, the head line, the life line round the thenar
		creases.Add(Shift(o, new Vector2(0.735f, 0.585f), new Vector2(0.64f, 0.6f), new Vector2(0.53f, 0.58f), new Vector2(0.45f, 0.545f)));
		creases.Add(Shift(o, new Vector2(0.36f, 0.61f), new Vector2(0.47f, 0.632f), new Vector2(0.58f, 0.67f), new Vector2(0.67f, 0.715f)));
		creases.Add(Shift(o, new Vector2(0.37f, 0.6f), new Vector2(0.425f, 0.665f), new Vector2(0.465f, 0.77f), new Vector2(0.485f, 0.92f)));
	}

	private static List<Vector2> Shift(Vector2 o, params Vector2[] pts) { var l = new List<Vector2>(); foreach (var p in pts) l.Add(p + o); return l; }

	private static void ClawHand(List<Pad> pads, RandomNumberGenerator rng, HandKind kind)
	{
		float splay = (kind == HandKind.ClawSplayed ? 1f : 0.35f) + rng.RandfRange(-0.1f, 0.1f);
		// long fingers of four joints, knobbed: thin between the knuckles, the knuckles pressing hardest
		var fingers = new (Vector2 b, float len, float ang, float spread)[]
		{
			(new Vector2(0.415f, 0.64f), 0.37f, -10f, -16f),
			(new Vector2(0.475f, 0.62f), 0.43f, -3f, -5f),
			(new Vector2(0.535f, 0.625f), 0.41f, 4f, 8f),
			(new Vector2(0.592f, 0.655f), 0.33f, 11f, 20f),
		};
		foreach (var (b, len, ang, spread) in fingers)
		{
			Vector2 dir = Dir(ang + spread * splay + rng.RandfRange(-3f, 3f));
			Vector2 at = b;
			float[] frac = { 0.34f, 0.27f, 0.22f, 0.17f };
			for (int s = 0; s < 4; s++)
			{
				float l = len * frac[s];
				Vector2 end = at + dir * l;
				// the shaft: thin and light; the knuckle at its end: a hard knob
				pads.Add(new Pad(at, end, 0.021f - 0.0015f * s, 0.018f - 0.0015f * s, 0.72f));
				if (s < 3) pads.Add(new Pad(end - dir * 0.008f, end + dir * 0.008f, 0.03f - 0.002f * s, 0.03f - 0.002f * s, 0.95f));
				at = end;
				// the joints bend unevenly (crooked, arthritic)
				float bend = Mathf.DegToRad(rng.RandfRange(-9f, 9f));
				float c = Mathf.Cos(bend), sn = Mathf.Sin(bend);
				dir = new Vector2(dir.X * c - dir.Y * sn, dir.X * sn + dir.Y * c);
			}
			// the tip pad, then the talon: hooked (curving in toward the middle finger), scored past the tip
			pads.Add(new Pad(at - dir * 0.018f, at, 0.021f, 0.02f, 0.95f));
			float hook = Mathf.DegToRad((b.X < 0.5f ? 1f : -1f) * rng.RandfRange(8f, 18f));
			Vector2 t1 = at + dir * 0.07f;
			float hc = Mathf.Cos(hook), hs = Mathf.Sin(hook);
			Vector2 d2 = new(dir.X * hc - dir.Y * hs, dir.X * hs + dir.Y * hc);
			Vector2 t2 = t1 + d2 * rng.RandfRange(0.06f, 0.09f);
			pads.Add(new Pad(at, t1, 0.02f, 0.011f, 1f));
			pads.Add(new Pad(t1, t2, 0.011f, 0.003f, 0.97f));
		}
		// the thumb: set low, long, three joints and a talon
		{
			Vector2 dir = Dir(-50f - 16f * splay + rng.RandfRange(-4f, 4f));
			Vector2 at = new Vector2(0.4f, 0.8f) + dir * 0.05f;
			for (int s = 0; s < 3; s++)
			{
				Vector2 end = at + dir * 0.066f;
				pads.Add(new Pad(at, end, 0.023f, 0.02f, 0.72f));
				pads.Add(new Pad(end - dir * 0.008f, end + dir * 0.008f, 0.029f, 0.029f, 0.9f));
				at = end;
				float bend = Mathf.DegToRad(rng.RandfRange(4f, 12f));
				float c = Mathf.Cos(bend), sn = Mathf.Sin(bend);
				dir = new Vector2(dir.X * c - dir.Y * sn, dir.X * sn + dir.Y * c);
			}
			pads.Add(new Pad(at, at + dir * 0.06f, 0.02f, 0.011f, 1f));
			pads.Add(new Pad(at + dir * 0.06f, at + dir * 0.11f, 0.011f, 0.003f, 0.97f));
		}
		// the palm: narrow, knotted, three hard pads and the bones between them showing as gaps
		foreach (var (b, _, _, _) in fingers) pads.Add(new Pad(b + new Vector2(0, 0.035f), b + new Vector2(0, 0.045f), 0.03f, 0.028f, 0.9f));
		pads.Add(new Pad(new Vector2(0.415f, 0.73f), new Vector2(0.455f, 0.89f), 0.05f, 0.04f, 0.82f));
		pads.Add(new Pad(new Vector2(0.6f, 0.73f), new Vector2(0.575f, 0.9f), 0.045f, 0.035f, 0.8f));
		pads.Add(new Pad(new Vector2(0.5f, 0.72f), new Vector2(0.52f, 0.86f), 0.062f, 0.055f, 0.72f));
		pads.Add(new Pad(new Vector2(0.47f, 0.93f), new Vector2(0.56f, 0.93f), 0.035f, 0.03f, 0.6f));
	}

	/// <summary>The handprint before 2026-10-13 (five strokes and an oval), for the before/after sheet only.</summary>
	private static Texture2D OldHand() => Strokes("uv_hand", 64, 64, new List<List<Vector2>>
	{
		L(0.26f, 0.5f, 0.12f, 0.3f), L(0.38f, 0.42f, 0.33f, 0.1f), L(0.5f, 0.4f, 0.5f, 0.06f), L(0.62f, 0.42f, 0.67f, 0.1f), L(0.74f, 0.5f, 0.84f, 0.28f),
	}, 0.055f, 41, p => 1f - Mathf.SmoothStep(0.19f, 0.24f, ((p - new Vector2(0.5f, 0.66f)) * new Vector2(1f, 0.85f)).Length()));

	/// <summary>Dev: every print on one sheet (the old hand first), cyan ink on black as the blacklight shows it, 256 px a cell.</summary>
	public static Image PrintSheet()
	{
		var cells = new List<Texture2D> { OldHand() };
		foreach (HandKind k in System.Enum.GetValues(typeof(HandKind))) { cells.Add(Hand(k, 0)); cells.Add(Hand(k, 3)); }
		cells.Add(Foot());
		const int C = 256, cols = 5;
		int rows = (cells.Count + cols - 1) / cols;
		var sheet = Image.CreateEmpty(C * cols, C * rows, false, Image.Format.Rgb8);
		sheet.Fill(new Color(0.03f, 0.02f, 0.05f));
		for (int i = 0; i < cells.Count; i++)
		{
			var src = cells[i].GetImage();
			if (src == null) continue;
			src = (Image)src.Duplicate();
			if (src.HasMipmaps()) src.ClearMipmaps();
			src.Resize(C - 8, C - 8, Image.Interpolation.Bilinear);
			int ox = (i % cols) * C + 4, oy = (i / cols) * C + 4;
			for (int y = 0; y < C - 8; y++)
				for (int x = 0; x < C - 8; x++)
				{
					float a = src.GetPixel(x, y).A;
					sheet.SetPixel(ox + x, oy + y, new Color(0.03f, 0.02f, 0.05f).Lerp(Cyan * 1.1f, a));
				}
		}
		return sheet;
	}

	/// <summary>Smooth value noise (0..1).</summary>
	private static float VNoise(Vector2 p, int seed)
	{
		int ix = Mathf.FloorToInt(p.X), iy = Mathf.FloorToInt(p.Y);
		float fx = p.X - ix, fy = p.Y - iy;
		fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
		float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	private static float Fbm(Vector2 p, int seed) => 0.55f * VNoise(p, seed) + 0.3f * VNoise(p * 2.13f, seed + 1) + 0.15f * VNoise(p * 4.37f, seed + 2);
}
