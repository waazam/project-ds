namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 24 and 25 (2026-10-04): the flamethrower and the snow cave, and the car at the end.
/// <list type="bullet">
/// <item>the flamethrower: the igniter's crack and the fuel catching in a whoomph; the roar of the stream (a loop: a deep
/// rushing burn, its low throb, crackle all through it); the valve shut, the stream sputtering out; the barrel locking
/// hot (a hard clank, steam venting); ready again (a click, a breath of gas);</item>
/// <item>the cave: hardly anything (the snow muffles everything): a low moan of air far off through the tunnels, now and
/// then the ice ticking, a drip;</item>
/// <item>the car: the starter turning over, and the engine catching into a rough idle.</item>
/// </list>
/// </summary>
public static class Act24Sounds
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A burn's body: rushing noise through a few bands, with a slow throb and crackle (shared by the roar and the whoomph).</summary>
	static void Burn(double[] x, Rng r, int sr, double t0, double t1, System.Func<double, double> env, double amp)
	{
		var lo = Biquad.Lp(sr, 260); var lo2 = Biquad.Lp(sr, 380);
		var mid = Biquad.Bp(sr, 700, 0.8); var hi = Biquad.Bp(sr, 2400, 0.9);
		var thr = new Smooth(r, t1 + 1, 0.09);
		for (int i = (int)(t0 * sr); i < x.Length && i < (int)(t1 * sr); i++)
		{
			double t = (double)i / sr, u = (t - t0) / (t1 - t0);
			double e = env(u);
			double throb = 0.75 + 0.25 * thr.At(t);
			x[i] += (lo2.P(lo.P(r.W())) * 3.2 + mid.P(r.W()) * 0.9 + hi.P(r.W()) * 0.25) * e * throb * amp;
		}
		for (double t = t0 + 0.02; t < t1; t += r.R(0.008, 0.05))
			Slap(x, r, sr, t, r.R(0.02, 0.07) * amp * env((t - t0) / (t1 - t0)), 900, r.R(3000, 6000), r.R(0.002, 0.005));
	}

	public static double[] Ignite(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		// the igniter's crack, then the fuel going up: a swell of burn, a deep thump under it
		Slap(x, r, sr, 0.0, 0.45, 1500, 7000, 0.01, 0.0003);
		Knock(x, r, sr, 0.04, 0.8, r.R(55, 70), 3, 0.25);
		Burn(x, r, sr, 0.03, 1.5, u => Perc(u * 1.5, 0.08, 0.35), 1.0);
		return FinishOneShot(Wet(x, sr, 1.0, 0.3, 1.4, 0.5, 1.0), sr, -3, 120);
	}

	public static double[] Roar(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		var x = new double[tot];
		Burn(x, r, sr, 0.0, (double)tot / sr, _ => 1.0, 1.0);
		var o = MakeLoop(x, pre, n, xf);
		double peak = 0;
		foreach (var v in o) peak = System.Math.Max(peak, System.Math.Abs(v));
		double g = FromDb(-6) / System.Math.Max(peak, 1e-9);
		for (int i = 0; i < o.Length; i++) o[i] *= g;
		return o;
	}

	public static double[] Stop(Rng r, int sr)
	{
		var x = Buf(sr, 1.3);
		// the valve shut: the stream gutters, coughs twice, pops
		Burn(x, r, sr, 0.0, 0.7, u => (1 - u) * (1 - u) * (0.6 + 0.4 * System.Math.Sin(u * 40)), 0.8);
		Slap(x, r, sr, 0.42, 0.3, 300, 3000, 0.03);
		Slap(x, r, sr, 0.58, 0.2, 300, 3000, 0.025);
		Knock(x, r, sr, 0.7, 0.25, r.R(140, 180), 4, 0.06);
		return FinishOneShot(Wet(x, sr, 1.0, 0.3, 1.2, 0.5, 1.0), sr, -3, 120);
	}

	public static double[] Overheat(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		// a hard metal clank (the lockout), then steam hissing out of the vent, dying away
		Knock(x, r, sr, 0.0, 1.0, r.R(900, 1100), 30, 0.25);
		Knock(x, r, sr, 0.0, 0.6, r.R(2200, 2600), 40, 0.18);
		Slap(x, r, sr, 0.0, 0.5, 600, 5000, 0.02, 0.0003);
		var hp = Biquad.Hp(sr, 2500); var lp = Biquad.Lp(sr, 9000);
		for (int i = (int)(0.08 * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - 0.08;
			x[i] += lp.P(hp.P(r.W())) * Perc(t, 0.05, 0.9) * 0.9;
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.25, 1.0, 0.5, 0.8), sr, -3, 200);
	}

	public static double[] Ready(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		Knock(x, r, sr, 0.0, 0.8, r.R(1400, 1700), 25, 0.05);
		Slap(x, r, sr, 0.0, 0.4, 2000, 7000, 0.006, 0.0003);
		var hp = Biquad.Hp(sr, 3000);
		for (int i = (int)(0.05 * sr); i < x.Length; i++) { double t = (double)i / sr - 0.05; x[i] += hp.P(r.W()) * Perc(t, 0.02, 0.15) * 0.25; }
		return FinishOneShot(x, sr, -3, 80);
	}

	public static double[] Cave(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		var x = new double[tot];
		double T = (double)tot / sr;
		// a low moan of air far off down the tunnels, rising and falling slowly
		var b1 = Biquad.Bp(sr, 110, 3); var b2 = Biquad.Bp(sr, 170, 4); var lp = Biquad.Lp(sr, 400);
		var swell = new Smooth(r, T, 7.0);
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double s = System.Math.Max(0, swell.At(t));
			x[i] += lp.P(b1.P(r.W()) * 1.2 + b2.P(r.W()) * 0.8) * (0.15 + 0.85 * s * s) * 2.5;
		}
		// the ice ticking, a drip, now and then
		for (double t = r.R(1, 3); t < T - 1; t += r.R(2.5, 7))
		{
			if (r.Chance(0.5)) Knock(x, r, sr, t, r.R(0.05, 0.12), r.R(1800, 3200), 20, r.R(0.02, 0.05));
			else Bubble(x, sr, t, r.R(900, 1500), r.R(0.05, 0.1), 0.02);
		}
		var o = MakeLoop(Wet(x, sr, 1.0, 0.5, 3.5, 0.6, 1.6), pre, n, xf);
		double peak = 0;
		foreach (var v in o) peak = System.Math.Max(peak, System.Math.Abs(v));
		double g = FromDb(-14) / System.Math.Max(peak, 1e-9);
		for (int i = 0; i < o.Length; i++) o[i] *= g;
		return o;
	}

	public static double[] CarStart(Rng r, int sr)
	{
		var x = Buf(sr, 5.0);
		// the starter: a whirring grind, chugging as it turns the engine over
		var bp = Biquad.Bp(sr, 220, 3);
		for (int i = 0; i < (int)(1.4 * sr); i++)
		{
			double t = (double)i / sr;
			double chug = 0.55 + 0.45 * System.Math.Sin(t * 2 * System.Math.PI * 6.5);
			x[i] += bp.P(r.W()) * chug * 2.2 * Env(t / 1.4, 0.05, 0.1);
		}
		// it catches: a cough, the revs up, settling into a rough idle
		var lo = Biquad.Lp(sr, 180); var lo2 = Biquad.Lp(sr, 260);
		double ph = 0;
		for (int i = (int)(1.35 * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - 1.35;
			double rpm = 14 + 30 * System.Math.Exp(-System.Math.Pow((t - 0.5) / 0.35, 2)) + 2 * System.Math.Sin(t * 3.1);
			ph += rpm / sr;
			double firing = System.Math.Pow(0.5 + 0.5 * System.Math.Cos(2 * System.Math.PI * ph), 6);
			x[i] += lo2.P(lo.P(r.W() * firing * 8 + firing * 1.5)) * System.Math.Min(1, t * 8) * (t > 3.3 ? System.Math.Max(0, 1 - (t - 3.3) / 0.35) : 1);
		}
		Knock(x, r, sr, 1.36, 0.6, 70, 3, 0.2);
		return FinishOneShot(x, sr, -3, 200);
	}
}
