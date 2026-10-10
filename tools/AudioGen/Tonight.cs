namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// 2026-10-10: the flares in the snow maze, things to throw and knock over, and the dread.
/// <list type="bullet">
/// <item>the flare: struck (a scratch, a pop, and it catches into its fizz); its burn (a loop: a hard sputtering hiss,
/// crackling); going out (the fizz guttering, a last few spits);</item>
/// <item>a tin can hitting the floor and rattling away; a glass bottle clinking down and rolling; a chair scraped and
/// bumped;</item>
/// <item>the dread: a low, slow drone, two notes not quite agreeing (a loop, faded in only when something is near and
/// unseen); a save point's hum (a strip light's buzz, steady);</item>
/// <item>frogs by the lake at night (a loop: croaks here and there, a chorus far off).</item>
/// </list>
/// </summary>
public static class Tonight
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	static double[] Loop(double[] x, int pre, int n, int xf, double db)
	{
		var o = MakeLoop(x, pre, n, xf);
		double g = FromDb(db) / System.Math.Max(Peak(o), 1e-9);
		for (int i = 0; i < o.Length; i++) o[i] *= g;
		return o;
	}

	/// <summary>The flare's fizz over [t0, t1): bright hissing noise, sputtering, with spits of crackle.</summary>
	static void Fizz(double[] x, Rng r, int sr, double t0, double t1, System.Func<double, double> env, double amp)
	{
		var hp = Biquad.Hp(sr, 1800); var bp = Biquad.Bp(sr, 4200, 0.7); var lo = Biquad.Bp(sr, 600, 0.9);
		var sput = new Smooth(r, t1 + 1, 0.035);
		var slow = new Smooth(r, t1 + 1, 0.6);
		for (int i = (int)(t0 * sr); i < x.Length && i < (int)(t1 * sr); i++)
		{
			double t = (double)i / sr, u = (t - t0) / System.Math.Max(t1 - t0, 1e-6);
			double s = 0.55 + 0.45 * sput.At(t);
			double e = env(u) * s * (0.8 + 0.2 * slow.At(t));
			x[i] += (hp.P(r.W()) * 0.6 + bp.P(r.W()) * 1.1 + lo.P(r.W()) * 0.35) * e * amp;
		}
		for (double t = t0 + 0.01; t < t1; t += r.R(0.01, 0.07))
			Slap(x, r, sr, t, r.R(0.03, 0.09) * amp * env((t - t0) / (t1 - t0)), 1500, r.R(5000, 9000), r.R(0.001, 0.004));
	}

	public static double[] FlareStrike(Rng r, int sr)
	{
		var x = Buf(sr, 1.8);
		// the cap's striker dragged across the end (a rough scratch), the pop as it lights, and it roars up into its fizz
		var bp = Biquad.Bp(sr, 2600, 1.2);
		for (int i = 0; i < (int)(0.22 * sr); i++) { double t = (double)i / sr; x[i] += bp.P(r.W()) * (0.4 + 0.6 * System.Math.Abs(System.Math.Sin(t * 70))) * Env(t / 0.22, 0.1, 0.2) * 0.6; }
		Slap(x, r, sr, 0.22, 0.7, 300, 6000, 0.03, 0.0004);
		Knock(x, r, sr, 0.23, 0.35, r.R(160, 220), 3, 0.05);
		Fizz(x, r, sr, 0.24, 1.8, u => System.Math.Min(1, u * 8) * (1 - 0.3 * u), 0.8);
		return FinishOneShot(x, sr, -3, 150);
	}

	public static double[] FlareBurn(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = sr, pre = sr / 2, tot = pre + n + xf;
		var x = new double[tot];
		Fizz(x, r, sr, 0, (double)tot / sr, _ => 1.0, 1.0);
		return Loop(x, pre, n, xf, -6);
	}

	public static double[] FlareOut(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		// guttering: the fizz breaks up, coughs, spits, and is gone (a wisp of a hiss after)
		Fizz(x, r, sr, 0, 1.6, u => (1 - u) * (1 - u) * (0.5 + 0.5 * System.Math.Abs(System.Math.Sin(u * 23))), 1.0);
		for (int k = 0; k < 4; k++) Slap(x, r, sr, 0.9 + k * r.R(0.12, 0.25), r.R(0.1, 0.25), 800, 6000, 0.01);
		var hp = Biquad.Hp(sr, 3000);
		for (int i = (int)(1.3 * sr); i < x.Length; i++) { double t = (double)i / sr - 1.3; x[i] += hp.P(r.W()) * Perc(t, 0.05, 0.25) * 0.08; }
		return FinishOneShot(x, sr, -3, 150);
	}

	public static double[] CanClatter(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		// a tin can: the first hard hit, a bounce, then a rattling roll away, each hit a thin metal ring
		double t = 0;
		double a = 1.0;
		for (int k = 0; k < 3; k++)
		{
			Knock(x, r, sr, t, a, r.R(1800, 2400), 18, 0.05);
			Knock(x, r, sr, t, a * 0.6, r.R(3400, 4400), 25, 0.035);
			Slap(x, r, sr, t, a * 0.5, 1500, 8000, 0.006);
			t += r.R(0.12, 0.22) * (1 - k * 0.25);
			a *= 0.55;
		}
		for (; t < 1.4; t += r.R(0.03, 0.07))
		{
			double f = System.Math.Max(0, 1 - (t - 0.3) / 1.1);
			Knock(x, r, sr, t, 0.12 * f, r.R(2000, 3600), 20, 0.02);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.2, 0.8, 0.5, 0.8), sr, -3, 100);
	}

	public static double[] BottleClink(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		// glass: high clear pings (no break), a dull clonk of the body, and a roll
		double f0 = r.R(1900, 2500);
		Knock(x, r, sr, 0, 0.9, f0, 60, 0.12);
		Knock(x, r, sr, 0, 0.5, f0 * 2.76, 80, 0.07);
		Knock(x, r, sr, 0, 0.5, r.R(500, 700), 8, 0.03);
		Knock(x, r, sr, r.R(0.14, 0.2), 0.45, f0 * 1.02, 60, 0.09);
		var lp = Biquad.Bp(sr, 900, 1.5);
		for (int i = (int)(0.25 * sr); i < (int)(1.4 * sr); i++) { double t = (double)i / sr; x[i] += lp.P(r.W()) * 0.06 * System.Math.Max(0, 1 - (t - 0.25) / 1.15) * (0.6 + 0.4 * System.Math.Sin(t * 30)); }
		return FinishOneShot(Wet(x, sr, 1.0, 0.25, 0.9, 0.5, 0.8), sr, -3, 100);
	}

	public static double[] ChairScrape(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		// legs dragged a hand's width over boards (a juddering stick-slip groan), then knocked down onto them
		var bp = Biquad.Bp(sr, r.R(380, 520), 4); var bp2 = Biquad.Bp(sr, r.R(900, 1200), 5);
		for (int i = 0; i < (int)(0.5 * sr); i++)
		{
			double t = (double)i / sr;
			double judder = System.Math.Max(0, System.Math.Sin(t * r.R(60, 80) * System.Math.PI * 2));
			x[i] += (bp.P(r.W()) * 1.0 + bp2.P(r.W()) * 0.5) * judder * Env(t / 0.5, 0.1, 0.3) * 1.5;
		}
		Knock(x, r, sr, 0.52, 0.8, r.R(160, 220), 4, 0.06);
		Knock(x, r, sr, 0.58, 0.4, r.R(240, 300), 4, 0.05);
		return FinishOneShot(Wet(x, sr, 1.0, 0.25, 0.7, 0.5, 0.8), sr, -3, 100);
	}

	public static double[] DreadDrone(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		var x = new double[tot];
		double T = (double)tot / sr;
		// two low notes a little out of tune with each other, beating slowly; a breath of noise over them that swells
		var sw = new Smooth(r, T, 5.0);
		double p1 = 0, p2 = 0, p3 = 0;
		var nb = Biquad.Bp(sr, 220, 2); var nl = Biquad.Lp(sr, 500);
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			p1 += 2 * System.Math.PI * 41.2 / sr;
			p2 += 2 * System.Math.PI * (43.65 + 0.3 * System.Math.Sin(t * 0.21)) / sr;
			p3 += 2 * System.Math.PI * 82.0 / sr;
			double s = 0.6 + 0.4 * sw.At(t);
			x[i] = (System.Math.Sin(p1) * 0.5 + System.Math.Sin(p2) * 0.45 + System.Math.Sin(p3) * 0.12 * s) * s + nl.P(nb.P(r.W())) * 0.9 * s * s;
		}
		return Loop(Wet(x, sr, 1.0, 0.4, 3.0, 0.6, 1.4), pre, n, xf, -8);
	}

	public static double[] SaveHum(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = sr, pre = sr / 2, tot = pre + n + xf;
		var x = new double[tot];
		// a strip light's hum: 60 Hz and its odd harmonics, a little buzz, steady and close
		double p = 0;
		var hp = Biquad.Bp(sr, 3000, 1.0);
		for (int i = 0; i < tot; i++)
		{
			p += 2 * System.Math.PI * 60 / sr;
			double v = System.Math.Sin(p) * 0.3 + System.Math.Sin(p * 2) * 0.5 + System.Math.Sin(p * 3) * 0.2 + System.Math.Sin(p * 5) * 0.08;
			double buzz = System.Math.Pow(System.Math.Abs(System.Math.Sin(p * 2)), 18) * 0.25;
			x[i] = v + buzz + hp.P(r.W()) * 0.02;
		}
		return Loop(x, pre, n, xf, -10);
	}

	public static double[] Frogs(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr, tot = pre + n + xf;
		var x = new double[tot];
		double T = (double)tot / sr;
		// croaks: a pulsed low buzz, a few each, scattered; nearer ones louder
		for (double t = r.R(0.2, 1); t < T - 1; t += r.R(0.15, 0.9))
		{
			double f = r.R(180, 420), near = r.Chance(0.3) ? 1.0 : r.R(0.15, 0.45);
			int pulses = r.I(2, 6);
			double pr = r.R(18, 32);
			int s0 = (int)(t * sr), len = (int)((pulses / pr + 0.05) * sr);
			var bp = Biquad.Bp(sr, f * 2, 3);
			double ph = 0;
			for (int i = 0; i < len && s0 + i < tot; i++)
			{
				double tt = (double)i / sr;
				ph += 2 * System.Math.PI * f / sr;
				double gate = System.Math.Max(0, System.Math.Sin(tt * pr * System.Math.PI * 2));
				double src = (System.Math.Sin(ph) + 0.6 * System.Math.Sin(ph * 2) + 0.3 * r.W()) * gate * gate;
				x[s0 + i] += bp.P(src) * near * Env(tt / (len / (double)sr), 0.05, 0.3);
			}
		}
		return Loop(Wet(x, sr, 1.0, 0.35, 1.2, 0.5, 1.2), pre, n, xf, -12);
	}
}
