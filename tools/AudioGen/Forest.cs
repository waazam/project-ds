namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Second batch of forest sounds: twig snaps, falling branches, trunk creaks, a lone cricket, a distant raven,
/// small-animal rustles, and the heartbeat loop. One-shots end in Dsp.FinishOneShot (zero ends, -3 dBFS peak).
/// </summary>
public static class Forest
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	static double[] Space(double[] dry, int sr, double dryGain, double wet, double size, double damp)
	{
		var rv = new Reverb(sr, size, damp);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] * dryGain + rv.P(dry[i]) * wet;
		return o;
	}

	/// <summary>
	/// One woody crack at <paramref name="t"/>: a broadband noise burst plus a short untuned splinter band
	/// (no pitched modes, which read as drums). <paramref name="scale"/> lowers every pitch (bigger wood) and lengthens every decay.
	/// </summary>
	static void Crack(double[] x, Rng r, int sr, double t, double amp, double scale)
	{
		int s0 = (int)(t * sr);
		var hp = Biquad.Hp(sr, r.R(700, 1200));
		double tauN = r.R(0.0006, 0.002) * scale;
		// No tuned modes: pitched resonances read as a wood block or drum. Splinter = untuned noise.
		var splinter = Biquad.Bp(sr, r.R(2200, 4200) / scale, 0.9);
		int len = (int)(0.12 * scale * sr);
		for (int i = 0; i < len && s0 + i < x.Length; i++)
		{
			double tt = (double)i / sr;
			double v = hp.P(r.W()) * Perc(tt, 0.00008, tauN) * 1.4;
			v += splinter.P(r.W()) * Perc(tt, 0.0001, 0.004 * scale) * 0.8;
			x[s0 + i] += v * amp;
		}
	}

	/// <summary>A small cluster of dry leaf-crunch micro grains starting at t.</summary>
	static void LeafCrunch(double[] x, Rng r, int sr, double t, double amp, int grains, double spread, double lo = 1800, double hi = 7000)
	{
		var hpf = Biquad.Hp(sr, lo); var lpf = Biquad.Lp(sr, hi);
		int span = (int)((spread + 0.03) * sr), s0 = (int)(t * sr);
		var band = new double[span];
		for (int i = 0; i < span; i++) band[i] = lpf.P(hpf.P(r.W()));
		for (int g = 0; g < grains; g++)
		{
			int gs = (int)(spread * Math.Pow(r.U(), 1.5) * sr);
			double tau = r.LogR(0.0007, 0.005), a = amp * r.R(0.3, 1.0);
			for (int i = 0; gs + i < span && s0 + gs + i < x.Length && i < 0.03 * sr; i++)
			{
				double tt = (double)i / sr;
				x[s0 + gs + i] += band[gs + i] * a * (Math.Exp(-tt / tau) - Math.Exp(-tt / 0.00015));
			}
		}
	}

	/// <summary>Soft low thump (a branch or body landing on forest floor).</summary>
	static void Thump(double[] x, Rng r, int sr, double t, double amp, double f0)
	{
		int s0 = (int)(t * sr);
		var lp = Biquad.Lp(sr, 220);
		double ph = 0;
		for (int i = 0; i < 0.25 * sr && s0 + i < x.Length; i++)
		{
			double tt = (double)i / sr, f = f0 * (0.6 + 0.4 * Math.Exp(-tt / 0.03));
			ph += TwoPi * f / sr;
			x[s0 + i] += amp * (Math.Sin(ph) * Perc(tt, 0.003, 0.05) + lp.P(r.W()) * Perc(tt, 0.002, 0.035) * 2.0);
		}
	}

	// ---------------------------------------------------------------- one-shots

	/// <summary>Dry twig breaking underfoot nearby: 1-3 splintering sub-cracks within ~40 ms, then a little leaf rustle.</summary>
	public static double[] TwigSnap(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		int n = r.I(1, 4);
		double t = 0.005;
		for (int k = 0; k < n; k++)
		{
			Crack(x, r, sr, t, k == 0 ? 1.0 : r.R(0.4, 0.8), r.R(0.8, 1.2));
			t += r.R(0.006, 0.018);
		}
		// Splinter tail: a few tiny ticks as the fibres finish tearing.
		LeafCrunch(x, r, sr, 0.02, 0.25, r.I(3, 7), r.R(0.03, 0.07), 2500, 8000);
		// Leaves shifting under the foot.
		LeafCrunch(x, r, sr, r.R(0.02, 0.05), 0.12, r.I(6, 14), r.R(0.12, 0.25));
		return FinishOneShot(Space(x, sr, 1, 0.06, 0.4, 0.7), sr, -3, 60, r.R(0.3, 0.45), 0.56);
	}

	/// <summary>Dead branch falling from the canopy: a crack, a tumble through leaves, 1-2 soft thumps on the floor.</summary>
	public static double[] BranchDrop(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		Crack(x, r, sr, 0.01, 1.0, r.R(1.4, 1.8));
		Crack(x, r, sr, r.R(0.02, 0.04), 0.5, r.R(1.2, 1.6));
		// Tumble: bursts of leaf rustle and small woody knocks as it hits twigs on the way down.
		double t = r.R(0.12, 0.2), fall = r.R(0.55, 0.9);
		while (t < fall)
		{
			LeafCrunch(x, r, sr, t, r.R(0.2, 0.45), r.I(5, 14), r.R(0.05, 0.12), 1500, 6000);
			if (r.Chance(0.5)) Crack(x, r, sr, t + r.R(0, 0.04), r.R(0.12, 0.3), r.R(1.5, 2.2));
			t += r.R(0.06, 0.16);
		}
		// Landing: one or two soft thumps plus a leaf crunch.
		double land = fall + r.R(0.05, 0.15);
		Thump(x, r, sr, land, 0.55, r.R(70, 95));
		LeafCrunch(x, r, sr, land, 0.35, r.I(10, 20), 0.12, 1500, 6000);
		if (r.Chance(0.7))
		{
			double bounce = land + r.R(0.12, 0.25);
			Thump(x, r, sr, bounce, r.R(0.2, 0.3), r.R(80, 110));
			LeafCrunch(x, r, sr, bounce, 0.15, r.I(4, 9), 0.08, 1500, 6000);
		}
		LowPass(x, sr, 6500);
		return FinishOneShot(Space(x, sr, 0.8, 0.45, 0.85, 0.55), sr, -3, 150, land + 0.5);
	}

	/// <summary>
	/// Tall trunk creaking in the wind: stick-slip friction impulses at a slow, wandering rate driving low woody
	/// resonances (150-450 Hz). A groan, not a squeaky hinge.
	/// </summary>
	public static double[] TrunkCreak(Rng r, int sr)
	{
		double dur = r.R(1.1, 2.1);
		var x = Buf(sr, dur + 0.5);
		var rate = new Smooth(r, dur + 1, r.R(0.2, 0.4));
		double rLo = r.R(14, 22), rHi = r.R(35, 60);
		var res = new[]
		{
			Biquad.Bp(sr, r.R(150, 200), r.R(14, 20)),
			Biquad.Bp(sr, r.R(260, 330), r.R(12, 18)),
			Biquad.Bp(sr, r.R(380, 450), r.R(10, 16)),
		};
		double[] ra = { 1.0, 0.7, 0.35 };
		var body = Biquad.Lp(sr, 900);
		// Occasional stalls: the rate can dip right down for a moment, which is what makes it sound like wood.
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n, rt = rLo + (rHi - rLo) * rate.At((double)i / sr);
				ph += rt / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.25, 0.35); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += ra[k] * res[k].P(imp);
			x[i] = body.P(v);
		}
		return FinishOneShot(Space(x, sr, 1, 0.3, 0.75, 0.5), sr, -3, 120, dur);
	}

	/// <summary>One close cricket: two or three chirps of 3-5 pulses around 4-5 kHz.</summary>
	public static double[] CricketChirp(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		double f = r.R(4200, 4900), pp = 1 / r.R(26, 34), gap = r.R(0.15, 0.24);
		int chirps = r.I(2, 4), pulses = r.I(3, 6);
		double t = 0.01;
		for (int c = 0; c < chirps && t + pulses * pp < 0.62; c++)
		{
			for (int p = 0; p < pulses; p++)
			{
				double pa = (p == 0 ? 0.7 : 1.0) * r.R(0.85, 1.0), ff = f * r.R(0.997, 1.003);
				AddTone(x, sr, t + p * pp, pp * 0.62, u => ff * (1 - 0.02 * u), u => pa * Env(u, 0.25, 0.55), new[] { 1.0, 0.06 });
			}
			t += pulses * pp + gap * r.R(0.9, 1.1);
		}
		HighPass(x, sr, 1500);
		return FinishOneShot(Space(x, sr, 1, 0.15, 0.5, 0.5), sr, -3, 60, 0, 0.78);
	}

	/// <summary>Distant raven: two or three deep, gurgling croaks, dulled by distance, with a slight tail.</summary>
	public static double[] Raven(Rng r, int sr)
	{
		var src = Buf(sr, 1.8);
		int k = r.I(2, 4);
		double t = 0.02, f0 = r.R(240, 320);
		var jit = new Smooth(r, 2, 0.01);
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.2, 0.3), a = r.R(0.8, 1.0), fc = f0 * r.R(0.95, 1.05), gurgle = r.R(22, 34);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len && s0 + i < src.Length; i++)
			{
				double u = (double)i / len, tt = (double)(s0 + i) / sr;
				double f = fc * (1 + 0.1 * Math.Sin(Math.PI * u) - 0.12 * u) * (1 + 0.06 * (jit.At(tt) - 0.5));
				ph += TwoPi * f / sr;
				double v = 0;
				for (int h = 1; h <= 16; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / h;
				double g = 0.5 + 0.5 * Math.Cos(TwoPi * gurgle * tt);
				src[s0 + i] += (v * 0.5 + r.W() * 0.25) * (0.35 + 0.65 * g * g) * a * Env(u, 0.1, 0.4);
			}
			t += d + r.R(0.14, 0.24);
		}
		var f1 = Biquad.Bp(sr, r.R(650, 800), 3); var f2 = Biquad.Bp(sr, r.R(1150, 1400), 4);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = f1.P(src[i]) + 0.6 * f2.P(src[i]);
		// Distance: highs gone, more room than voice.
		LowPass(x, sr, 1700); LowPass(x, sr, 2200);
		return FinishOneShot(Space(x, sr, 0.6, 0.9, 0.85, 0.6), sr, -3, 150, t + 0.15);
	}

	/// <summary>Small animal scurrying through dry litter: an uneven run of 6-20 crunch steps that starts and stops.</summary>
	public static double[] Rustle(Rng r, int sr)
	{
		var x = Buf(sr, 1.3);
		int steps = r.I(6, 21);
		double t = 0.01, rate = r.R(12, 20);
		for (int s = 0; s < steps && t < 1.05; s++)
		{
			double a = r.R(0.4, 1.0) * (s == 0 ? 0.6 : 1.0);
			LeafCrunch(x, r, sr, t, a, r.I(2, 6), r.R(0.015, 0.04));
			// Uneven: mostly quick patters, sometimes a stop.
			t += r.Chance(0.15) ? r.R(0.12, 0.3) : (1 / rate) * r.R(0.6, 1.5);
		}
		return FinishOneShot(Space(x, sr, 1, 0.2, 0.6, 0.5), sr, -3, 60, Math.Max(0.4, Math.Min(t, 1.0)), 1.15);
	}

	// ---------------------------------------------------------------- loop

	/// <summary>
	/// Resting heartbeat heard from inside the head: lub-dub at exactly 60 bpm (one beat per second), each beat
	/// rendered, low-passed and wrapped into a circular buffer so the loop is exact.
	/// </summary>
	public static double[] Heartbeat(Rng r, int sr, int beats)
	{
		var x = new double[beats * sr];
		for (int b = 0; b < beats; b++)
		{
			var ev = new double[(int)(0.9 * sr)];
			double la = r.R(0.93, 1.0), da = la * r.R(0.55, 0.7), dubAt = r.R(0.28, 0.31);
			BeatSound(ev, r, sr, 0, la, r.R(46, 52), 0.055);
			BeatSound(ev, r, sr, dubAt, da, r.R(58, 66), 0.04);
			var lp1 = Biquad.Lp(sr, 110); var lp2 = Biquad.Lp(sr, 140); var hp = Biquad.Hp(sr, 22);
			for (int i = 0; i < ev.Length; i++) ev[i] = hp.P(lp2.P(lp1.P(ev[i])));
			AddWrapped(x, ev, b * sr);
		}
		NormRms(x, -22, -6);
		return x;
	}

	static void BeatSound(double[] ev, Rng r, int sr, double t, double amp, double f0, double tau)
	{
		int s0 = (int)(t * sr);
		var lp = Biquad.Lp(sr, 90);
		double ph = 0;
		for (int i = 0; s0 + i < ev.Length && i < 0.4 * sr; i++)
		{
			double tt = (double)i / sr, f = f0 * (0.75 + 0.25 * Math.Exp(-tt / 0.03));
			ph += TwoPi * f / sr;
			ev[s0 + i] += amp * (Math.Sin(ph) * Perc(tt, 0.012, tau) + lp.P(r.W()) * Perc(tt, 0.008, tau * 0.7) * 1.5);
		}
	}
}
