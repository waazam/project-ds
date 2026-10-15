namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Act 1's trail walk, livened (2026-10-15, the owner: "do some more research on forest hiking walk audio from hunting
/// games and see if we can spruce up the act 1 audio"). What the hunting and hiking games do (theHunter: Call of the
/// Wild, Red Dead Redemption 2, Firewatch): the forest is built from events placed in the world, not loops; distant
/// wildlife at long, uneven intervals and from all round; and the walker's own Foley (steps by ground, cloth, the kit
/// they carry, the brush they push through) carries a lot of the walk. All of it events, many takes, no beds:
/// <list type="bullet">
/// <item><b>woodpecker</b>: a far drum on a dead trunk, a fast roll of dry taps that speeds and slows, once or twice.</item>
/// <item><b>crow</b>: a far caw, two to four, harsh and nasal, from across the valley.</item>
/// <item><b>squirrel</b>: a scolding chatter from up a tree nearby, quick chucks and a trill.</item>
/// <item><b>gear_creak</b> / <b>gear_clink</b>: the walker's own kit: a strap or a pack creaking as it shifts, a
/// buckle or a zip pull ticking.</item>
/// <item><b>brush</b>: ferns and low brush against the legs, off the trail: a leafy swish with a crackle of stems.</item>
/// </list>
/// </summary>
public static class Trail
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	static double[] Far(double[] dry, int sr, double lpHz, double wet, double size)
	{
		LowPass(dry, sr, lpHz);
		var rv = new Reverb(sr, size, 0.55);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] * 0.8 + rv.P(dry[i]) * wet;
		return o;
	}

	/// <summary>A woodpecker drumming on a dead trunk, far off: 10-24 dry taps at 14-22 a second, quickening then
	/// slowing, once or twice.</summary>
	public static double[] Woodpecker(Rng r, int sr)
	{
		int rolls = r.Chance(0.35) ? 2 : 1;
		var x = Buf(sr, 4.2);
		double t = 0.05;
		double body = r.R(650, 950);   // the trunk's hollow: one dull band, low Q (a knock, not a pitched block)
		for (int k = 0; k < rolls; k++)
		{
			int hits = r.I(10, 25);
			double rate = r.R(14, 22), amp = r.R(0.75, 1.0);
			var bp = Biquad.Bp(sr, body * r.R(0.95, 1.05), 2.2);
			var click = Biquad.Hp(sr, 1800);
			for (int h = 0; h < hits; h++)
			{
				double u = (double)h / Math.Max(1, hits - 1);
				// louder in the middle of the roll, quicker at its start
				double a = amp * (0.55 + 0.45 * Math.Sin(Math.PI * Math.Min(1, u * 1.2))) * r.R(0.85, 1.0);
				int s0 = (int)(t * sr);
				for (int i = 0; i < sr / 25 && s0 + i < x.Length; i++)
				{
					double tt = (double)i / sr, w = r.W();
					x[s0 + i] += (bp.P(w) * Perc(tt, 0.0002, 0.006) * 2.2 + click.P(w) * Perc(tt, 0.00005, 0.0009) * 0.6) * a;
				}
				t += 1.0 / (rate * (1 + 0.25 * (0.5 - Math.Abs(u - 0.35))));
			}
			t += r.R(0.6, 1.2);
		}
		return FinishOneShot(Far(x, sr, r.R(2600, 3600), 0.45, 0.8), sr, -3, 250);
	}

	/// <summary>A crow far off: two to four harsh caws, nasal and falling at the end, with the valley's echo.</summary>
	public static double[] Crow(Rng r, int sr)
	{
		var x = Buf(sr, 3.4);
		int k = r.I(2, 5);
		double t = 0.05, f0 = r.R(380, 520);
		var f1 = Biquad.Bp(sr, r.R(1100, 1400), 3); var f2 = Biquad.Bp(sr, r.R(2000, 2500), 4); var f3 = Biquad.Bp(sr, r.R(3100, 3600), 5);
		var rough = new Smooth(r, 4, 0.006);
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.24, 0.4), a = r.R(0.7, 1.0) * (c == k - 1 ? r.R(0.7, 1.0) : 1), fc = f0 * r.R(0.96, 1.04);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len && s0 + i < x.Length; i++)
			{
				double u = (double)i / len, tt = (double)(s0 + i) / sr;
				double f = fc * (1 + 0.08 * Math.Sin(Math.PI * Math.Min(1, u * 1.6)) - 0.18 * Math.Max(0, u - 0.6));
				ph += 2 * Math.PI * f / sr;
				// a rough, buzzing source: a pulse train with jittered amplitude, and breath noise in it
				double src = 0;
				for (int h = 1; h <= 16; h++) if (f * h < sr * 0.45) src += Math.Sin(ph * h) / h;
				src = src * (0.6 + 0.8 * rough.At(tt)) + r.W() * 0.35;
				double v = f1.P(src) + 0.8 * f2.P(src) + 0.35 * f3.P(src);
				x[s0 + i] += Math.Tanh(v * 2.5) * Env(u, 0.12, 0.35) * a;
			}
			t += d + r.R(0.22, 0.45);
		}
		return FinishOneShot(Far(x, sr, r.R(3000, 4200), 0.55, 0.85), sr, -3, 300);
	}

	/// <summary>A red squirrel scolding from up a tree: a run of quick "chuk"s, sometimes ending in a trill.</summary>
	public static double[] Squirrel(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		int n = r.I(6, 18);
		double t = 0.03, rate = r.R(7, 11);
		for (int c = 0; c < n; c++)
		{
			double d = r.R(0.025, 0.045), fTop = r.R(3200, 4600), fEnd = fTop * r.R(0.55, 0.7), a = r.R(0.6, 1.0);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			var nb = Biquad.Bp(sr, fTop * 0.8, 2);
			for (int i = 0; i < len && s0 + i < x.Length; i++)
			{
				double u = (double)i / len, f = fTop + (fEnd - fTop) * u;
				ph += 2 * Math.PI * f / sr;
				x[s0 + i] += (Math.Sin(ph) * 0.7 + Math.Sin(ph * 2) * 0.15 + nb.P(r.W()) * 0.6) * Env(u, 0.1, 0.6) * a;
			}
			t += 1.0 / (rate * r.R(0.85, 1.15));
		}
		if (r.Chance(0.5))
		{
			// the trill: a fast warble falling away
			double d = r.R(0.35, 0.6), f = r.R(3600, 4400);
			int s0 = (int)((t + 0.05) * sr), len = (int)(d * sr);
			double ph = 0, wob = r.R(22, 30);
			for (int i = 0; i < len && s0 + i < x.Length; i++)
			{
				double u = (double)i / len, tt = (double)i / sr;
				ph += 2 * Math.PI * f * (1 - 0.25 * u) * (1 + 0.06 * Math.Sin(2 * Math.PI * wob * tt)) / sr;
				x[s0 + i] += Math.Sin(ph) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * wob * tt)) * Env(u, 0.05, 0.5) * 0.55;
			}
		}
		return FinishOneShot(Far(x, sr, 7000, 0.3, 0.6), sr, -3, 150);
	}

	/// <summary>A strap or the pack shifting as they walk: a short stick-slip creak of webbing and leather.</summary>
	public static double[] GearCreak(Rng r, int sr)
	{
		double dur = r.R(0.12, 0.3);
		var x = Buf(sr, dur + 0.08);
		var rate = new Smooth(r, dur + 0.1, r.R(0.03, 0.07));
		double rLo = r.R(55, 90), rHi = r.R(120, 200);
		var res = new[] { Biquad.Bp(sr, r.R(700, 1000), 6), Biquad.Bp(sr, r.R(1500, 2100), 5), Biquad.Bp(sr, r.R(2800, 3600), 4) };
		double[] ra = { 1.0, 0.6, 0.25 };
		var soft = Biquad.Lp(sr, 3200);
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (rLo + (rHi - rLo) * rate.At((double)i / sr)) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1.0) * Env(u, 0.3, 0.4); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += ra[k] * res[k].P(imp);
			x[i] = soft.P(v);
		}
		return FinishOneShot(x, sr, -3, 30);
	}

	/// <summary>A buckle, a zip pull or a ring on the pack ticking: one to three tiny metal ticks.</summary>
	public static double[] GearClink(Rng r, int sr)
	{
		var x = Buf(sr, 0.35);
		int k = r.I(1, 4);
		double t = 0.005;
		double[] modes = { r.R(3200, 4200), r.R(5300, 6400), r.R(7400, 8800) };
		for (int c = 0; c < k; c++)
		{
			int s0 = (int)(t * sr);
			double a = r.R(0.5, 1.0) * (c == 0 ? 1 : 0.6);
			for (int i = 0; i < sr / 12 && s0 + i < x.Length; i++)
			{
				double tt = (double)i / sr, v = 0;
				for (int m = 0; m < modes.Length; m++) v += Math.Sin(2 * Math.PI * modes[m] * r.R(0.998, 1.002) * tt) * Math.Exp(-tt / (0.025 / (m + 1))) / (m + 1);
				v += r.W() * Perc(tt, 0.00005, 0.0006) * 0.8;
				x[s0 + i] += v * a;
			}
			t += r.R(0.04, 0.11);
		}
		return FinishOneShot(x, sr, -3, 20);
	}

	/// <summary>Ferns and low brush against the legs: a leafy swish with a crackle of stems and dry fronds.</summary>
	public static double[] Brush(Rng r, int sr)
	{
		double dur = r.R(0.35, 0.7);
		var x = Buf(sr, dur + 0.08);
		var swish = Biquad.Bp(sr, r.R(1800, 2800), 0.7);
		var hp = Biquad.Hp(sr, 600);
		var crk = Biquad.Bp(sr, r.R(3500, 5500), 1.2);
		var tex = new Smooth(r, dur + 0.1, 0.02);
		double peak = r.R(0.25, 0.45);
		int n = (int)(dur * sr);
		// the crackle: many tiny leaf and stem ticks, densest where the leg's push is
		double rate = r.R(120, 260);
		for (int i = 0; i < n; i++)
		{
			double u = (double)i / n;
			double env = u < peak ? Math.Sin(0.5 * Math.PI * u / peak) : Math.Pow(1 - (u - peak) / (1 - peak), 1.6);
			double v = hp.P(swish.P(r.W())) * env * (0.55 + 0.45 * tex.At((double)i / sr));
			if (r.U() < rate * env / sr) { int len = r.I(sr / 2500, sr / 600); double a = r.R(0.3, 1.0); for (int j = 0; j < len && i + j < x.Length; j++) x[i + j] += crk.P(r.W()) * a * (1 - (double)j / len) * 1.6; }
			x[i] += v;
		}
		LowPass(x, sr, 7500);
		return FinishOneShot(x, sr, -3, 40);
	}
}
