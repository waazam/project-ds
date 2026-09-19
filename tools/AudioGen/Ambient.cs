namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Looping ambient beds. Two loop strategies:
///  * noise beds render linearly past the loop end and crossfade the tail into the head (Dsp.MakeLoop);
///  * tonal / event beds are built to be exactly periodic (frequencies are whole multiples of 1/length,
///    events are written into a circular buffer).
/// </summary>
public static class Ambient
{
	/// <summary>Soft forest wind: brown/pink noise under a gust-driven low-pass, plus a faint moving whistle band.</summary>
	public static double[] Wind(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		double T = (double)tot / sr;
		var gust = new Smooth(r, T, 2.6);
		var surge = new Smooth(r, T, 8.5);
		var flutter = new Smooth(r, T, 0.3);
		var br = new Brown(); var pk = new Pink();
		var lp1 = new Biquad(sr); var lp2 = new Biquad(sr); var wh = new Biquad(sr);
		var hp = Biquad.Hp(sr, 75);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double g = 0.55 * gust.At(t) + 0.45 * surge.At(t);
			g = g * g * (3 - 2 * g);
			double fl = flutter.At(t);
			if (i % 16 == 0)
			{
				double fc = 260 + 950 * g + 220 * fl * g;
				lp1.SetLp(fc, 0.6); lp2.SetLp(fc * 1.4, 0.6);
				wh.SetBp(260 + 300 * g + 70 * fl, 4.5);
			}
			double w = r.W();
			double body = lp2.P(lp1.P(br.P(w) * 0.25 + pk.P(w) * 0.9));
			double whistle = wh.P(r.W()) * 0.18 * g * g;
			x[i] = hp.P(body * (0.22 + 0.78 * g) + whistle);
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -21);
		return o;
	}

	/// <summary>
	/// Leaves moving in the wind: smooth, airy "shhh" swells. No crackle grains:
	/// thousands of tiny clicks a second read as a fire, not a forest.
	/// </summary>
	public static double[] Leaves(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var swell = new Smooth(r, T, 1.6);
		var slow = new Smooth(r, T, 5.0);
		var hp = Biquad.Hp(sr, 700); var lp1 = Biquad.Lp(sr, 3800); var lp2 = Biquad.Lp(sr, 4500);
		var air = Biquad.Bp(sr, 2400, 0.6);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double s = Math.Pow(0.55 * swell.At(t) + 0.45 * slow.At(t), 1.6);
			double n0 = r.W();
			// Mostly quiet, with occasional rustles: a steady hiss reads as a jet overhead.
			x[i] = (lp2.P(lp1.P(hp.P(n0))) * 0.8 + air.P(n0) * 0.3) * (0.015 + s * s * 1.6);
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -27);
		return o;
	}

	/// <summary>
	/// Cricket / katydid chorus: individual chirpers (3.5-6 kHz pulses) with their own rhythms and
	/// fading activity, plus a faint band-limited distant buzz.
	/// </summary>
	public static double[] Insects(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var chirps = new double[tot];
		int[] types = { 0, 0, 0, 1, 1, 2, 2, 0 }; // 0 cricket, 1 tree-cricket trill, 2 katydid rasp
		for (int c = 0; c < types.Length; c++)
		{
			var cr = r.Fork();
			var act = new Smooth(cr, T, cr.R(3.5, 8));
			double f = types[c] switch { 0 => cr.R(4000, 5200), 1 => cr.R(3500, 4200), _ => cr.R(5000, 6000) };
			double amp = cr.R(0.25, 1.0);
			double Act(double t) { double a = Math.Clamp((act.At(t) - 0.2) / 0.5, 0, 1); return a * a * (3 - 2 * a); }
			double t0 = cr.R(0, 1.5);
			if (types[c] == 0)
			{
				double period = 1 / cr.R(1.3, 2.8), pp = 1 / cr.R(24, 38);
				int pulses = cr.I(3, 6);
				for (double t = t0; t < T; t += period * cr.R(0.96, 1.04))
				{
					double a = amp * Act(t);
					if (a < 0.02) continue;
					for (int p = 0; p < pulses; p++)
					{
						double pa = a * (p == 0 ? 0.7 : 1.0) * cr.R(0.85, 1.0), ff = f * cr.R(0.995, 1.005);
						AddTone(chirps, sr, t + p * pp, pp * 0.62, u => ff * (1 - 0.025 * u), u => pa * Env(u, 0.25, 0.55), new[] { 1.0, 0.07 });
					}
				}
			}
			else if (types[c] == 1)
			{
				double pr = cr.R(42, 58), t = t0;
				while (t < T)
				{
					double phrase = cr.R(2.0, 6.0), end = Math.Min(T, t + phrase);
					for (double tp = t; tp < end; tp += 1 / pr)
					{
						double env = Env((tp - t) / phrase, 0.12, 0.2), pa = amp * 0.6 * Act(tp) * env;
						if (pa < 0.01) continue;
						AddTone(chirps, sr, tp, 0.55 / pr, u => f, u => pa * Env(u, 0.3, 0.5), new[] { 1.0, 0.05 });
					}
					t = end + cr.R(0.4, 2.0);
				}
			}
			else
			{
				double period = cr.R(0.8, 1.3), rasp = cr.R(160, 240);
				for (double t = t0; t < T; t += period * cr.R(0.95, 1.05))
				{
					double a = amp * 0.8 * Act(t);
					if (a < 0.02) continue;
					int syl = cr.I(2, 4);
					for (int s = 0; s < syl; s++)
					{
						double d = cr.R(0.05, 0.085), st = t + s * (d + 0.06), sa = a * cr.R(0.8, 1.0);
						AddTone(chirps, sr, st, d, u =>
						{
							double ph = Math.Cos(Math.PI * rasp * u * d);
							return f * (1 + 0.01 * ph);
						}, u =>
						{
							double ph = Math.Cos(Math.PI * rasp * u * d);
							return sa * Env(u, 0.12, 0.35) * (0.15 + 0.85 * ph * ph * ph * ph);
						}, new[] { 1.0, 0.1 });
					}
				}
			}
		}

		// Faint distant chorus: narrow noise bands, gently pulsed.
		var buzz = new double[tot];
		var b1 = Biquad.Bp(sr, 4600, 4); var b1b = Biquad.Bp(sr, 4600, 4);
		var b2 = Biquad.Bp(sr, 5500, 5); var b2b = Biquad.Bp(sr, 5500, 5);
		var bm = new Smooth(r, T, 3.0);
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double am1 = 0.65 + 0.35 * Math.Sin(TwoPi * 46 * t), am2 = 0.7 + 0.3 * Math.Sin(TwoPi * 31 * t + 1);
			buzz[i] = (b1b.P(b1.P(r.W())) * am1 + 0.7 * b2b.P(b2.P(r.W())) * am2) * (0.6 + 0.4 * bm.At(t));
		}
		Scale(buzz, 0.3 * Rms(chirps) / Rms(buzz));

		var x = new double[tot];
		for (int i = 0; i < tot; i++) x[i] = chirps[i] + buzz[i];
		HighPass(x, sr, 1500);
		LowPass(x, sr, 9500);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -29);
		return o;
	}

	/// <summary>Far-off bed: sub rumble + low air. (No bird texture: dulled synthetic chirps read as flutes.)</summary>
	public static double[] Distant(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		double T = (double)tot / sr;
		var m1 = new Smooth(r, T, 7); var m2 = new Smooth(r, T, 12.5);
		var br = new Brown(); var pk = new Pink();
		var rl1 = Biquad.Lp(sr, 70); var rl2 = Biquad.Lp(sr, 110);
		var air = Biquad.Bp(sr, 280, 0.6);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double rum = rl2.P(rl1.P(br.P(r.W())));
			double a = air.P(pk.P(r.W()));
			x[i] = rum * (0.6 + 0.4 * m1.At(t)) + a * 0.35 * (0.4 + 0.6 * m2.At(t));
		}
		HighPass(x, sr, 16);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -30);
		return o;
	}

	/// <summary>Small creek: dense population of rising-pitch "bubble" resonances over band noise and gurgle.</summary>
	public static double[] Stream(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var turb = new Smooth(r, T, 0.07); var flow = new Smooth(r, T, 1.1);
		var nb = Biquad.Bp(sr, 900, 0.5); var gl = Biquad.Lp(sr, 500);
		var br = new Brown();
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr, tu = turb.At(t), fl = flow.At(t);
			x[i] = nb.P(r.W()) * 0.09 * (0.4 + tu) + gl.P(br.P(r.W())) * 0.12 * (0.4 + 0.6 * fl);
		}
		var bub = new double[tot];
		double tb = 0;
		while (tb < T)
		{
			double fl = flow.At(tb);
			tb += -Math.Log(1 - r.U()) / (70 + 150 * fl * fl);
			double f0 = 320 * Math.Pow(2600.0 / 320, Math.Pow(r.U(), 1.5));
			double d = r.R(0.012, 0.045) * Math.Pow(1000 / f0, 0.35);
			double rise = r.R(0.15, 0.9), a = Math.Pow(r.U(), 2) * r.R(0.3, 1) * Math.Pow(1000 / f0, 0.25);
			AddTone(bub, sr, tb, d, u => f0 * (1 + rise * u), u =>
			{
				double at = u < 0.05 ? Math.Pow(Math.Sin(0.5 * Math.PI * u / 0.05), 2) : 1;
				return a * at * Math.Exp(-4 * u) * (1 - u);
			});
		}
		for (int i = 0; i < tot; i++) x[i] += bub[i] * 0.4;
		HighPass(x, sr, 90);
		LowPass(x, sr, 7000);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -24);
		return o;
	}

	/// <summary>"Silence ringing": thin ~8.2 kHz sine, a 0.25 Hz beating partner for the wobble, all periodic.</summary>
	public static double[] Ringing(int sr, int sec)
	{
		int n = sec * sr;
		double q = 1.0 / sec;
		var x = new double[n];
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr;
			double v = Math.Sin(TwoPi * 8200 * t + 0.9 * Math.Sin(TwoPi * 3 * q * t)) * (1 + 0.12 * Math.Sin(TwoPi * 2 * q * t))
				+ 0.18 * Math.Sin(TwoPi * (8200 + 5 * q) * t)
				+ 0.05 * (0.5 + 0.5 * Math.Sin(TwoPi * q * t)) * Math.Sin(TwoPi * 7340 * t);
			x[i] = v;
		}
		NormPeak(x, -26);
		return x;
	}

	static double[] Inhale(Rng r, int sr, double dur)
	{
		int len = (int)(dur * sr), warm = 2048;
		var hp = Biquad.Hp(sr, 450); var b1 = Biquad.Bp(sr, r.R(1900, 2400), 0.8); var b2 = Biquad.Bp(sr, r.R(1000, 1250), 1.6);
		var lp = Biquad.Lp(sr, 5000);
		var tex = new Smooth(r, dur + 1, 0.07);
		var o = new double[len];
		for (int i = -warm; i < len; i++)
		{
			double w = hp.P(r.W());
			double v = lp.P(b1.P(w) + 0.6 * b2.P(w));
			if (i < 0) continue;
			double u = (double)i / len;
			// Swells up, peaks late, then stops fairly quickly (the lungs fill).
			double env = Env(u, 0.7, 0.28) * (1 + 0.25 * u);
			o[i] = v * env * (0.82 + 0.18 * tex.At((double)i / sr));
		}
		return o;
	}

	static double[] Exhale(Rng r, int sr, double dur)
	{
		int len = (int)(dur * sr), warm = 2048;
		var lp1 = Biquad.Lp(sr, r.R(1300, 1700)); var b1 = Biquad.Bp(sr, r.R(550, 750), 0.7);
		var hp = Biquad.Hp(sr, 180);
		var tex = new Smooth(r, dur + 1, 0.09);
		var o = new double[len];
		for (int i = -warm; i < len; i++)
		{
			double w = r.W();
			double v = hp.P(lp1.P(w) * 0.8 + b1.P(w) * 0.9);
			if (i < 0) continue;
			double u = (double)i / len;
			// Quick-ish onset, long relaxed decay.
			double env = Env(u, 0.18, 0.8) * (1.1 - 0.4 * u);
			o[i] = v * env * (0.85 + 0.15 * tex.At((double)i / sr));
		}
		return o;
	}

	/// <summary>
	/// The air near a staircase: a shapeless low pressure, more felt than heard.
	/// Deliberately has no pulse. The old drone's 0.22 Hz beating read as
	/// breathing, so nothing here repeats faster than every 13 s, and the tones
	/// never sit close enough together to beat.
	/// </summary>
	public static double[] Pressure(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;   // whole cycles per loop
		double f1 = F(29.0), f2 = F(43.6), f3 = F(61.3);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi), a3 = r.R(0, TwoPi), a4 = r.R(0, TwoPi);
		var lp1 = Biquad.Lp(sr, 48); var lp2 = Biquad.Lp(sr, 55);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			// Slow, uneven swells built from 40 s and 13.3 s components only.
			double swellA = 0.55 + 0.28 * Math.Sin(TwoPi * q * t + a1) + 0.17 * Math.Sin(TwoPi * 3 * q * t + a2);
			double swellB = 0.5 + 0.3 * Math.Sin(TwoPi * 2 * q * t + a3) + 0.2 * Math.Sin(TwoPi * q * t + a4);
			double rumble = lp2.P(lp1.P(r.W())) * 6.0;
			double tones = 0.5 * Math.Sin(TwoPi * f1 * t)
				+ 0.28 * swellB * Math.Sin(TwoPi * f2 * t + 0.4 * Math.Sin(TwoPi * q * t))
				+ 0.08 * (1 - swellB) * Math.Sin(TwoPi * f3 * t);
			x[i] = rumble * swellA + tones * (0.35 + 0.65 * swellA * swellB);
		}
		HighPass(x, sr, 16);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -24, -8);
		return o;
	}

	/// <summary>
	/// A low, constant wrongness under the whole forest: two tones a tritone apart
	/// (they never resolve and never beat into a pulse), faint octaves so small
	/// speakers still carry it, and a dark rumble. Swells are 16 s or slower.
	/// </summary>
	public static double[] Undertone(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;   // whole cycles per loop
		double f1 = F(55.0), f2 = F(77.8), f3 = F(110.0), f4 = F(155.6);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi), a3 = r.R(0, TwoPi);
		var lp1 = Biquad.Lp(sr, 90); var lp2 = Biquad.Lp(sr, 110); var hp = Biquad.Hp(sr, 30);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			double sA = 0.6 + 0.25 * Math.Sin(TwoPi * q * t + a1) + 0.15 * Math.Sin(TwoPi * 3 * q * t + a2);
			double sB = 0.55 + 0.3 * Math.Sin(TwoPi * 2 * q * t + a3) + 0.15 * Math.Sin(TwoPi * q * t + a2);
			double tones = 0.55 * sA * Math.Sin(TwoPi * f1 * t)
				+ 0.4 * sB * Math.Sin(TwoPi * f2 * t)
				+ 0.12 * sA * Math.Sin(TwoPi * f3 * t)
				+ 0.08 * sB * Math.Sin(TwoPi * f4 * t);
			double rumble = hp.P(lp2.P(lp1.P(r.W()))) * 5.0 * (0.5 + 0.5 * sB);
			x[i] = tones + rumble;
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -22, -6);
		return o;
	}
}
