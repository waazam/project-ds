namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 14, the stairwell: steel stairs in a concrete shaft that goes down much further than it should.
/// Boots on steel treads (a dull thump and the plate ringing under it), the shaft's own low tone, the
/// structure groaning under its own weight, something falling past down the well that never lands, far
/// clangs from below, and at the bottom the flight giving way. Tonal and event-built like the rest; the
/// one loop is written circularly.
/// </summary>
public static class Stairwell
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A boot coming down on a steel tread: the heel's thump, the plate ringing briefly under
	/// it, a little grit, all in a tall hard shaft.</summary>
	public static double[] StepMetal(Rng r, int sr)
	{
		var x = Buf(sr, 1.1);
		Slap(x, r, sr, 0.0, 0.9, 70, 900, r.R(0.02, 0.035), 0.002);
		Knock(x, r, sr, 0.001, r.R(0.25, 0.35), r.R(360, 520), 18, r.R(0.14, 0.22));
		Knock(x, r, sr, 0.002, r.R(0.12, 0.2), r.R(880, 1250), 24, r.R(0.08, 0.14));
		Knock(x, r, sr, 0.002, r.R(0.05, 0.1), r.R(2000, 2700), 30, r.R(0.04, 0.07));
		Slap(x, r, sr, 0.004, 0.12, 2000, 6500, 0.004, 0.0003);
		if (r.Chance(0.5)) Slap(x, r, sr, r.R(0.05, 0.09), 0.25, 90, 700, 0.02, 0.002);   // the toe after the heel
		return FinishOneShot(Wet(x, sr, 1.0, 0.3, 1.6, 0.55, 1.1), sr, -3, 120);
	}

	/// <summary>The shaft's air: a very low tone and a sour partial beating slowly against it, a faint
	/// high whistle of air moving somewhere far below, and the odd distant tick of the steel settling.</summary>
	public static double[] ShaftDrone(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double W(double f) => Math.Round(f * sec) / sec;
		double f0 = W(34), f1 = W(34 * 1.5 + 0.35), f2 = W(612), swell = W(0.06);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double s = 0.6 + 0.4 * Math.Sin(TwoPi * swell * t);
			double v = Math.Sin(TwoPi * f0 * t) + 0.45 * Math.Sin(TwoPi * f1 * t + 1) + 0.2 * Math.Sin(TwoPi * f0 * 3 * t + 2);
			v += 0.035 * Math.Sin(TwoPi * f2 * t) * (0.5 + 0.5 * Math.Sin(TwoPi * W(0.11) * t + 0.7));
			x[i] = Math.Tanh(v * 0.6) * s;
		}
		for (double t = r.R(0, 3); t < sec; t += r.R(3, 8))
			Knock(x, r, sr, t, r.R(0.05, 0.18), r.LogR(700, 1900), 30, r.R(0.2, 0.5), true);
		var h = new Hall(sr, 3.2, 0.6, 30, 1.4);
		var o = Circular(x, v => v * 0.7 + h.P(v) * 0.6);
		NormRms(o, -26);
		return o;
	}

	/// <summary>The steel structure taking its load: a long, low, uneven groan.</summary>
	public static double[] Groan(Rng r, int sr)
	{
		double dur = r.R(1.4, 2.2);
		var x = Buf(sr, dur + 1.6);
		var rate = new Smooth(r, dur + 1, r.R(0.2, 0.4));
		var modes = new[] { (r.R(95, 130), 16.0, 1.0), (r.R(210, 260), 18.0, 0.6), (r.R(420, 520), 22.0, 0.35), (r.R(900, 1100), 26.0, 0.15) };
		var res = modes.Select(m => Biquad.Bp(sr, m.Item1, m.Item2)).ToArray();
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (9 + 22 * rate.At((double)i / sr)) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1.0) * Env(u, 0.3, 0.4); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].Item3 * res[k].P(imp);
			x[i] = v;
		}
		return FinishOneShot(Wet(x, sr, 0.8, 0.5, 2.4, 0.6, 1.3), sr, -3, 300);
	}

	/// <summary>Something falling past down the well: air tearing past, rising and falling as it
	/// passes, dropping in pitch as it goes away below. It never lands.</summary>
	public static double[] FallPast(Rng r, int sr)
	{
		double dur = 2.4;
		var x = Buf(sr, dur + 0.6);
		var bp = new Biquad(sr);
		for (int i = 0; i < (int)(dur * sr); i++)
		{
			double u = (double)i / sr / dur;
			if (i % 32 == 0) bp.SetBp(1400 * Math.Pow(0.25, u), 1.6);
			double pass = Math.Exp(-Math.Pow((u - 0.28) / 0.16, 2)) + 0.4 * Math.Exp(-u * 3) * (u < 0.28 ? u / 0.28 : 1);
			x[i] = bp.P(r.W()) * pass;
		}
		// the thing itself tumbling: a soft flapping tick as it turns over
		for (double t = 0.2; t < dur * 0.7; t += r.R(0.09, 0.14))
			Slap(x, r, sr, t, 0.15 * Math.Exp(-Math.Pow((t / dur - 0.28) / 0.2, 2)), 300, 1600, 0.01, 0.002);
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 2.0, 0.6, 1.2), sr, -3, 400);
	}

	/// <summary>A heavy steel door, or something like one, struck far below: a dull boom and a long ring
	/// coming up the shaft.</summary>
	public static double[] FarClang(Rng r, int sr)
	{
		var x = Buf(sr, 3.5);
		Slap(x, r, sr, 0.0, 0.8, 40, 400, 0.06, 0.004);
		Knock(x, r, sr, 0.002, 0.6, r.R(150, 210), 20, 0.9);
		Knock(x, r, sr, 0.002, 0.35, r.R(430, 560), 26, 0.6);
		Knock(x, r, sr, 0.003, 0.15, r.R(1100, 1400), 30, 0.3);
		LowPass(x, sr, 1400);
		return FinishOneShot(Wet(x, sr, 0.5, 0.8, 3.0, 0.7, 1.5), sr, -3, 500);
	}

	/// <summary>The flight giving way: a bolt shearing with a crack, steel screaming as it bends,
	/// concrete breaking out of the wall, and the whole flight going.</summary>
	public static double[] StairBreak(Rng r, int sr)
	{
		var x = Buf(sr, 4.0);
		Slap(x, r, sr, 0.0, 1.0, 900, 9000, 0.006, 0.0001);            // the bolt
		Knock(x, r, sr, 0.001, 0.7, 2900, 20, 0.08);
		// the bend: a stick-slip screech gliding down
		var bp = Biquad.Bp(sr, 700, 12);
		double ph = 0;
		for (int i = (int)(0.08 * sr); i < (int)(1.3 * sr); i++)
		{
			double u = (i - 0.08 * sr) / (1.22 * sr);
			ph += (110 - 60 * u) / sr;
			double imp = 0;
			if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1) * Env(u, 0.1, 0.4); }
			if (i % 64 == 0) bp.SetBp(900 - 450 * u, 12);
			x[i] += bp.P(imp) * 3;
		}
		// concrete breaking out: a crumble of low slaps and grit
		for (double t = 0.3; t < 2.2; t += r.R(0.01, 0.06))
			Slap(x, r, sr, t, r.LogR(0.1, 0.6) * Env((t - 0.3) / 1.9, 0.1, 0.6), 60, 1800, r.R(0.01, 0.04), 0.001);
		// the flight dropping away: two big clanging hits
		foreach (double t in new[] { 0.9, 1.55 })
		{
			Slap(x, r, sr, t, 0.9, 50, 900, 0.05, 0.002);
			Knock(x, r, sr, t + 0.002, 0.5, r.R(180, 260), 16, 0.7);
			Knock(x, r, sr, t + 0.002, 0.3, r.R(600, 800), 22, 0.4);
		}
		return FinishOneShot(Wet(x, sr, 0.9, 0.5, 2.6, 0.6, 1.3), sr, -3, 600);
	}
}
