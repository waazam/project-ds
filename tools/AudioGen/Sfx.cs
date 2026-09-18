namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>One-shots: birds, footsteps, cloth. Every result goes through Dsp.FinishOneShot (zero ends, -3 dBFS peak).</summary>
public static class Sfx
{
	// ---------------------------------------------------------------- birds

	static double[] Space(double[] dry, int sr, double mix, double size = 0.75, double damp = 0.45, double lpHz = 0)
	{
		if (lpHz > 0) LowPass(dry, sr, lpHz);
		var rv = new Reverb(sr, size, damp);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] + rv.P(dry[i]) * mix;
		return o;
	}

	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Bird(int index, Rng r, int sr) => index switch
	{
		1 => ChirpSequence(r, sr),
		2 => TwoNoteWhistle(r, sr),
		3 => Warble(r, sr),
		4 => Woodpecker(r, sr),
		5 => Caw(r, sr),
		6 => Coo(r, sr),
		7 => DistantScreech(r, sr),
		_ => SongPhrase(r, sr),
	};

	/// <summary>Quick run of descending chirps.</summary>
	static double[] ChirpSequence(Rng r, int sr)
	{
		var x = Buf(sr, 1.8);
		int k = r.I(5, 8);
		double t = 0.02, top = r.R(4000, 4600);
		for (int i = 0; i < k; i++)
		{
			double d = r.R(0.045, 0.07), f0 = top * (1 - 0.03 * i) * r.R(0.97, 1.03), f1 = f0 * r.R(0.55, 0.68);
			double a = (i == 0 ? 0.6 : 1.0) * r.R(0.75, 1.0) * (1 - 0.05 * i);
			AddTone(x, sr, t, d, u => f0 * Math.Pow(f1 / f0, Math.Pow(u, 0.7)), u => a * Env(u, 0.12, 0.45), new[] { 1.0, 0.12, 0.03 });
			t += d + r.R(0.055, 0.085);
		}
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 60);
	}

	/// <summary>Clear two-note "fee-bee" whistle, slight vibrato.</summary>
	static double[] TwoNoteWhistle(Rng r, int sr)
	{
		var x = Buf(sr, 1.9);
		double f1 = r.R(3250, 3500), f2 = f1 * r.R(0.84, 0.88);
		double d1 = r.R(0.28, 0.34), d2 = r.R(0.36, 0.44), gap = r.R(0.05, 0.08);
		AddTone(x, sr, 0.02, d1, u => f1 * (1 - 0.015 * u) * (1 + 0.004 * Math.Sin(TwoPi * 7 * u * d1)), u => 0.9 * Env(u, 0.18, 0.3), new[] { 1.0, 0.04 });
		AddTone(x, sr, 0.02 + d1 + gap, d2, u => f2 * (1 - 0.02 * u) * (1 + 0.005 * Math.Sin(TwoPi * 6 * u * d2)), u => Env(u, 0.12, 0.4), new[] { 1.0, 0.04 });
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	/// <summary>Fast warbling trill with a rise-and-fall contour.</summary>
	static double[] Warble(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		double d = r.R(1.1, 1.4), c = r.R(3000, 3500), rate = r.R(18, 24), depth = r.R(500, 750);
		AddTone(x, sr, 0.02, d, u =>
		{
			double contour = c * (1 + 0.18 * Math.Sin(Math.PI * u) - 0.08 * u);
			return contour + depth * Math.Sin(TwoPi * rate * u * d);
		}, u =>
		{
			double s = Math.Cos(Math.PI * rate * u * d);
			return Env(u, 0.12, 0.3) * (0.35 + 0.65 * s * s);
		}, new[] { 1.0, 0.12 });
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	/// <summary>Woodpecker drum: a quick burst of hollow knocks on a trunk, fading.</summary>
	static double[] Woodpecker(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		int k = r.I(13, 19);
		double t = 0.01, per = 1 / r.R(16, 19);
		double m1 = r.R(850, 1050), m2 = r.R(420, 520), m3 = r.R(1600, 1900);
		var nb = Biquad.Bp(sr, 1400, 1.5);
		for (int i = 0; i < k; i++)
		{
			double a = (1 - 0.45 * i / k) * r.R(0.85, 1.0);
			int s0 = (int)(t * sr);
			for (int j = 0; j < (int)(0.06 * sr) && s0 + j < x.Length; j++)
			{
				double tt = (double)j / sr;
				double v = nb.P(r.W() * Perc(tt, 0.0003, 0.0025)) * 1.4
					+ 0.8 * Math.Sin(TwoPi * m1 * tt) * Perc(tt, 0.0004, 0.009)
					+ 0.6 * Math.Sin(TwoPi * m2 * tt) * Perc(tt, 0.0004, 0.014)
					+ 0.25 * Math.Sin(TwoPi * m3 * tt) * Perc(tt, 0.0003, 0.005);
				x[s0 + j] += v * a;
			}
			t += per * (1 - 0.012 * i) * r.R(0.97, 1.03);
		}
		return FinishOneShot(Space(x, sr, 0.5, 0.85, 0.5, 5000), sr, -3, 100);
	}

	/// <summary>Crow-like harsh caws: rough harmonic-rich source through beak/throat formants.</summary>
	static double[] Caw(Rng r, int sr)
	{
		var src = Buf(sr, 2.2);
		int k = r.I(2, 4);
		double t = 0.02;
		var jit = new Smooth(r, 3, 0.012);
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.28, 0.4), f0 = r.R(480, 560) * (1 - 0.04 * c), a = r.R(0.85, 1.0);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len && s0 + i < src.Length; i++)
			{
				double u = (double)i / len, tt = (double)(s0 + i) / sr;
				double f = f0 * (1 + 0.12 * Math.Sin(Math.PI * Math.Min(1, u * 1.6)) - 0.18 * u) * (1 + 0.05 * (jit.At(tt) - 0.5));
				ph += TwoPi * f / sr;
				double v = 0;
				for (int h = 1; h <= 14; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / Math.Pow(h, 0.8);
				double rough = 0.7 + 0.3 * Math.Sin(TwoPi * 73 * tt + 3 * jit.At(tt * 3));
				src[s0 + i] += (v * 0.35 + r.W() * 0.5) * rough * a * Env(u, 0.08, 0.45);
			}
			t += d + r.R(0.18, 0.28);
		}
		var f1 = Biquad.Bp(sr, 1150, 2.5); var f2 = Biquad.Bp(sr, 1900, 3.5); var f3 = Biquad.Bp(sr, 3000, 3);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = f1.P(src[i]) + 0.7 * f2.P(src[i]) + 0.25 * f3.P(src[i]);
		return FinishOneShot(Space(x, sr, 0.45, 0.85, 0.5, 4200), sr, -3, 100);
	}

	/// <summary>Dove-like soft low "coo-OO-oo-oo".</summary>
	static double[] Coo(Rng r, int sr)
	{
		var x = Buf(sr, 2.45);
		double b = r.R(470, 530);
		(double d, double f0, double f1, double a, double gap)[] notes =
		{
			(0.24, b * 1.02, b * 1.1, 0.55, 0.09),
			(0.55, b * 1.12, b * 1.02, 1.0, 0.28),
			(0.3, b * 0.98, b * 0.95, 0.7, 0.1),
			(0.32, b * 0.96, b * 0.93, 0.6, 0),
		};
		double t = 0.03;
		foreach (var nt in notes)
		{
			double d = nt.d * r.R(0.92, 1.08), f0 = nt.f0, f1 = nt.f1, a = nt.a;
			AddTone(x, sr, t, d, u => (f0 + (f1 - f0) * Math.Sin(0.5 * Math.PI * u)) * (1 + 0.006 * Math.Sin(TwoPi * 5 * u * d)),
				u => a * Env(u, 0.3, 0.45), new[] { 1.0, 0.22, 0.07, 0.02 });
			t += d + nt.gap;
		}
		// A touch of breathy noise following the voice.
		var n = Biquad.Bp(sr, 700, 1.2);
		var env = x.Select(Math.Abs).ToArray();
		LowPass(env, sr, 20);
		for (int i = 0; i < x.Length; i++) x[i] += n.P(r.W()) * env[i] * 0.35;
		LowPass(x, sr, 1800);
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 100);
	}

	/// <summary>Distant jay-like screech: harsh noisy tone, dulled by distance and washed in reverb.</summary>
	static double[] DistantScreech(Rng r, int sr)
	{
		var src = Buf(sr, 2.4);
		int k = r.I(1, 3);
		double t = 0.03;
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.3, 0.42), f0 = r.R(2000, 2300), a = r.R(0.8, 1);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len; i++)
			{
				double u = (double)i / len;
				double f = f0 * (1 + 0.2 * Math.Sin(Math.PI * Math.Min(1, u * 1.4)) - 0.12 * u) + 60 * r.W();
				ph += TwoPi * f / sr;
				double v = Math.Sin(ph) + 0.55 * Math.Sin(2 * ph) + 0.3 * Math.Sin(3 * ph);
				v = Math.Tanh(1.6 * v) + 0.45 * r.W();
				src[s0 + i] += v * a * Env(u, 0.1, 0.5);
			}
			t += d + r.R(0.15, 0.25);
		}
		var bp = Biquad.Bp(sr, 2300, 1.2);
		for (int i = 0; i < src.Length; i++) src[i] = bp.P(src[i]);
		LowPass(src, sr, 2800); LowPass(src, sr, 3200);
		// Distance: more reverb than dry.
		var rv = new Reverb(sr, 0.9, 0.55);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = src[i] * 0.55 + rv.P(src[i]) * 0.9;
		return FinishOneShot(x, sr, -3, 150);
	}

	/// <summary>Robin-like phrase of varied syllables (sweeps, hooks).</summary>
	static double[] SongPhrase(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		int k = r.I(6, 9);
		double t = 0.02;
		for (int i = 0; i < k; i++)
		{
			double d = r.R(0.07, 0.17), f0 = r.R(2200, 3600), a = r.R(0.6, 1.0);
			int shape = r.I(0, 4);
			double f1 = shape switch { 0 => f0 * r.R(1.2, 1.45), 1 => f0 * r.R(0.65, 0.8), _ => f0 * r.R(0.95, 1.05) };
			double hook = r.R(0.2, 0.35);
			Func<double, double> fr = shape switch
			{
				2 => u => f0 + (f1 - f0) * u + f0 * hook * Math.Sin(Math.PI * u), // arch
				3 => u => f0 - f0 * hook * 0.8 * Math.Sin(Math.PI * u), // dip
				_ => u => f0 * Math.Pow(f1 / f0, u),
			};
			AddTone(x, sr, t, d, fr, u => a * Env(u, 0.15, 0.4), new[] { 1.0, 0.1, 0.02 });
			t += d + r.R(0.035, 0.1);
			if (t > 1.9) break;
		}
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	// ---------------------------------------------------------------- footsteps

	/// <summary>Dirt / leaf litter: low heel thump + crunchy grain cluster + leaf swish. Some steps roll heel-to-toe.</summary>
	public static double[] StepDirt(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		double thumpF = r.R(95, 130), heavy = r.R(0.7, 1.0);
		bool roll = r.Chance(0.5);
		double[] contacts = roll ? new[] { 0.0, r.R(0.035, 0.07) } : new[] { 0.0 };
		var lpn = Biquad.Lp(sr, 260);
		for (int c = 0; c < contacts.Length; c++)
		{
			int s0 = (int)(contacts[c] * sr);
			double a = c == 0 ? heavy : heavy * r.R(0.35, 0.55), ph = 0;
			for (int i = 0; i + s0 < x.Length && i < 0.15 * sr; i++)
			{
				double tt = (double)i / sr, f = thumpF * (0.55 + 0.45 * Math.Exp(-tt / 0.02));
				ph += TwoPi * f / sr;
				x[s0 + i] += a * (Math.Sin(ph) * Perc(tt, 0.002, 0.03) * 0.9 + lpn.P(r.W()) * Perc(tt, 0.001, 0.022) * 1.6);
			}
		}
		// Crunch: short bright grains, dense at first then thinning.
		var ghp = Biquad.Hp(sr, 1700); var glp = Biquad.Lp(sr, r.R(5500, 8000));
		var gmid = Biquad.Bp(sr, r.R(900, 1400), 1.2);
		double win = r.R(0.14, 0.24);
		int ng = r.I(14, 34);
		var grains = new List<(int s, double tau, double a, bool mid)>();
		for (int g = 0; g < ng; g++)
		{
			double gt = win * Math.Pow(r.U(), 1.8) + r.R(0.0, 0.01);
			grains.Add(((int)(gt * sr), r.LogR(0.0008, 0.006), r.R(0.2, 1.0) * (1 - 0.6 * gt / win), r.Chance(0.3)));
		}
		var hi = new double[x.Length]; var mid = new double[x.Length];
		for (int i = 0; i < x.Length; i++) { double w = r.W(); hi[i] = glp.P(ghp.P(w)); mid[i] = gmid.P(r.W()); }
		double crunch = r.R(0.5, 0.9);
		foreach (var g in grains)
			for (int i = 0; i < 0.04 * sr && g.s + i < x.Length; i++)
			{
				double tt = (double)i / sr, e = Math.Exp(-tt / g.tau) - Math.Exp(-tt / 0.0002);
				x[g.s + i] += e * g.a * crunch * (g.mid ? mid[g.s + i] * 1.5 : hi[g.s + i]) * 1.8;
			}
		// Leaf swish under it.
		var sw = Biquad.Bp(sr, r.R(2000, 3200), 0.8);
		double swDur = r.R(0.08, 0.16);
		for (int i = 0; i < swDur * sr; i++) x[i] += sw.P(r.W()) * 0.12 * Env((double)i / (swDur * sr), 0.2, 0.7);
		return FinishOneShot(x, sr, -3, 40, r.R(0.24, 0.3));
	}

	// A boot on planks: almost all damped noise, no ringing partials (sine
	// modes read as hand drums). Heel strike = dull weight + a low-Q board knock
	// + a dry click; the toe lands softer 60-110 ms later; a little grit scuffs
	// between them. Even-numbered variants add a faint board creak.
	public static double[] StepWood(Rng r, int sr, bool creak)
	{
		var x = Buf(sr, creak ? 0.5 : 0.38);
		double knockF = r.R(330, 480), clickF = r.R(1400, 2200);
		double[] contacts = { 0.0, r.R(0.06, 0.11) };
		double[] gains = { 1.0, r.R(0.35, 0.5) };
		for (int c = 0; c < contacts.Length; c++)
		{
			var weight = Biquad.Lp(sr, 160);
			var knock = Biquad.Bp(sr, knockF * r.R(0.95, 1.05), 1.1);
			var click = Biquad.Bp(sr, clickF * r.R(0.9, 1.1), 1.4);
			int s0 = (int)(contacts[c] * sr);
			double a = gains[c];
			for (int i = 0; i + s0 < x.Length && i < 0.09 * sr; i++)
			{
				double tt = (double)i / sr;
				double v = weight.P(r.W()) * Perc(tt, 0.002, 0.018) * 2.2
					+ knock.P(r.W()) * Perc(tt, 0.0006, 0.014) * 2.4
					+ click.P(r.W()) * Perc(tt, 0.0002, 0.004) * 1.2;
				x[s0 + i] += a * v;
			}
		}
		// Grit under the sole.
		var grit = Biquad.Hp(sr, 2600);
		int g0 = (int)(r.R(0.01, 0.03) * sr), gLen = (int)(r.R(0.05, 0.09) * sr);
		for (int i = 0; i < gLen && g0 + i < x.Length; i++)
			x[g0 + i] += grit.P(r.W()) * 0.1 * Env((double)i / gLen, 0.3, 0.6) * (r.Chance(0.15) ? 2.5 : 1.0);
		if (creak)
		{
			var c1 = Biquad.Bp(sr, r.R(520, 700), 9); var c2 = Biquad.Bp(sr, r.R(1100, 1400), 7);
			double cs = r.R(0.08, 0.14), cd = r.R(0.12, 0.2), rate0 = r.R(55, 80), rate1 = rate0 * r.R(1.2, 1.5);
			double ph = 0;
			for (int i = 0; i < cd * sr; i++)
			{
				double u = i / (cd * sr);
				ph += (rate0 + (rate1 - rate0) * u) / sr;
				double imp = 0;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0); }
				double v = (c1.P(imp) + 0.5 * c2.P(imp)) * Env(u, 0.3, 0.5);
				int idx = (int)(cs * sr) + i;
				if (idx < x.Length) x[idx] += v * 0.5;
			}
		}
		return FinishOneShot(x, sr, -3, 30, 0.24);
	}

	// ---------------------------------------------------------------- cloth

	/// <summary>Jacket rustle: soft band noise in 2-3 overlapping swells, with a little fabric friction grain.</summary>
	public static double[] Cloth(Rng r, int sr)
	{
		double dur = r.R(0.28, 0.5);
		var x = Buf(sr, dur + 0.05);
		var bp = Biquad.Bp(sr, r.R(1500, 2400), 0.6); var hp = Biquad.Hp(sr, 500); var lp = Biquad.Lp(sr, 6000);
		var low = Biquad.Bp(sr, r.R(400, 600), 0.8);
		var fr = Biquad.Hp(sr, 3000);
		int k = r.I(2, 4);
		var bumps = Enumerable.Range(0, k).Select(_ => (c: r.R(0.15, 0.7), w: r.R(0.18, 0.4), a: r.R(0.5, 1.0))).ToArray();
		var tex = new Smooth(r, dur + 0.1, 0.012);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / (dur * sr), e = 0;
			foreach (var b in bumps)
			{
				double d = (u - b.c) / b.w;
				if (Math.Abs(d) < 1) e += b.a * 0.5 * (1 + Math.Cos(Math.PI * d));
			}
			double w = r.W();
			double v = lp.P(hp.P(bp.P(w))) + 0.35 * low.P(w) + 0.25 * fr.P(r.W()) * Math.Pow(tex.At((double)i / sr), 3);
			x[i] = v * e * (0.7 + 0.3 * tex.At((double)i / sr)) * Env(u, 0.05, 0.1);
		}
		return FinishOneShot(x, sr, -3, 40, dur);
	}
}
