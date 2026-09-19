using static ProjectDS.Tools.AudioGen.Dsp;

namespace ProjectDS.Tools.AudioGen;

/// <summary>
/// Listening samples for the background-music direction (not game assets yet).
/// Written by `dotnet run --project tools/AudioGen -- --music-samples` to test-output/music/.
/// </summary>
public static class Music
{
	/// <summary>
	/// Option 1, reactive score, as a scripted 75 s demo of its arc: near-nothing
	/// at the trailhead (0-15 s), a slow build as you go deeper (15-55 s), a peak,
	/// then a hard cut to silence (~64 s), as it would drop out at the stairs.
	/// No melody: a bowed-metal hum, a few distant sour notes, and a slow two-note
	/// fall on something glassy.
	/// </summary>
	public static double[] ReactiveDemo(Rng r, int sr)
	{
		double T = 75, cutAt = 64.5;
		int n = (int)(T * sr);
		var dry = new double[n];

		double Intensity(double t) =>
			t < 15 ? 0.1 : t < 55 ? 0.1 + 0.9 * SmoothStep((t - 15) / 40) : 1.0;

		// A. Bowed metal: a low inharmonic hum with slowly breathing partials and bow noise.
		double f0 = 55;
		double[] ratio = { 1, 2.0, 2.76, 4.07, 5.4, 8.93, 13.2 };
		double[] amp = { 1, 0.45, 0.35, 0.2, 0.12, 0.06, 0.04 };
		var per = ratio.Select(_ => r.R(7, 19)).ToArray();
		var ph0 = ratio.Select(_ => r.R(0, TwoPi)).ToArray();
		var bow = Biquad.Bp(sr, f0 * 2.76, 8);
		var bowLp = Biquad.Lp(sr, 1500);
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr, k = Intensity(t), v = 0;
			for (int p = 0; p < ratio.Length; p++)
			{
				double breathe = 0.55 + 0.45 * Math.Sin(TwoPi * t / per[p] + ph0[p]);
				double upper = p >= 5 ? SmoothStep((k - 0.5) / 0.4) : 1;   // shimmer only as it builds
				v += amp[p] * breathe * upper * Math.Sin(TwoPi * f0 * ratio[p] * t + 0.3 * Math.Sin(TwoPi * 0.07 * t + p));
			}
			v += bowLp.P(bow.P(r.W())) * 0.9;
			dry[i] += v * (0.12 + 0.88 * k) * 0.35;
		}

		// B. Distant, slightly sour sustained notes (a second voice 18 cents sharp).
		foreach (var (at, hz) in new[] { (8.0, 220.0), (26.0, 174.6), (41.0, 233.1), (52.0, 207.7) })
			AddNote(dry, sr, at, hz, 9.0, 0.22, sour: true);

		// C. A slow two-note fall on something glassy, far away.
		foreach (double at in new[] { 33.0, 58.0 })
		{
			AddBell(dry, sr, at, 659.3, 0.12);
			AddBell(dry, sr, at + 1.6, 554.4, 0.12);
		}

		var o = WithReverb(dry, sr, 0.92, 0.45, 0.55);
		// Hard cut: everything, reverb tail included, gone in 30 ms.
		int cut = (int)(cutAt * sr), fade = (int)(0.03 * sr);
		for (int i = cut; i < n; i++) o[i] *= i < cut + fade ? 1 - (i - cut) / (double)fade : 0;
		NormRms(o, -20, -3);
		return o;
	}

	/// <summary>
	/// Option 3, a classic dark-ambient bed: a detuned low pad (A, E, C and a
	/// sour D-sharp) under a slowly sweeping filter, a moving breathy texture, and
	/// a few far-off glassy glints. 60 s, seamless.
	/// </summary>
	public static double[] DarkAmbientLoop(Rng r, int sr)
	{
		int sec = 60, n = sec * sr, pre = 2 * sr, xf = 4 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		var x = new double[tot];

		double[] chord = { 55.0, 82.41, 130.81, 155.56 };
		double[] vAmp = { 1.0, 0.7, 0.45, 0.35 };
		double[] detune = { -0.004, 0, 0.0041 };
		var phases = new double[chord.Length, detune.Length];
		var lp1 = new Biquad(sr); var lp2 = new Biquad(sr);
		var tex = new Biquad(sr); var texLp = Biquad.Lp(sr, 2500);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi), a3 = r.R(0, TwoPi);

		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			if (i % 64 == 0)
			{
				double sweep = 0.5 + 0.35 * Math.Sin(TwoPi * q * t + a1) + 0.15 * Math.Sin(TwoPi * 3 * q * t + a2);
				double cutoff = 180 * Math.Pow(900 / 180.0, sweep);
				lp1.SetLp(cutoff, 1.4); lp2.SetLp(cutoff * 1.3, 0.7);
				tex.SetBp(300 * Math.Pow(5, 0.5 + 0.5 * Math.Sin(TwoPi * 2 * q * t + a3)), 4);
			}
			double pad = 0;
			for (int c = 0; c < chord.Length; c++)
			{
				double swell = 0.6 + 0.4 * Math.Sin(TwoPi * (c % 2 == 0 ? 1 : 2) * q * t + c * 1.7);
				for (int d = 0; d < detune.Length; d++)
				{
					double f = chord[c] * (1 + detune[d]);
					phases[c, d] += f / sr;
					double s = 0;
					for (int h = 1; h <= 12 && f * h < 2200; h++) s += Math.Sin(TwoPi * h * phases[c, d]) / h;   // soft saw
					pad += s * vAmp[c] * swell;
				}
			}
			double texture = texLp.P(tex.P(r.W())) * (0.4 + 0.3 * Math.Sin(TwoPi * 3 * q * t + a2));
			x[i] = lp2.P(lp1.P(pad)) * 0.25 + texture * 0.5;
		}

		// A few far-off glassy glints.
		foreach (var (at, hz) in new[] { (9.0, 1318.5), (27.5, 1108.7), (44.0, 1396.9) })
			AddBell(x, sr, at + 2.0, hz, 0.06);

		var wet = WithReverb(x, sr, 0.9, 0.5, 0.45);
		var o = MakeLoop(wet, pre, n, xf);
		NormRms(o, -21, -3);
		return o;
	}


	// ------------------------------------------------ in-game score (option 1)

	/// <summary>
	/// The score's bed: a deep bowed-metal hum on low E (41.2 Hz) with slowly
	/// breathing inharmonic partials and a little bow noise. Every partial and
	/// modulator completes whole cycles over the loop, so it repeats exactly.
	/// </summary>
	public static double[] ScoreHumLoop(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;
		double f0 = 41.2;
		double[] ratio = { 1, 2.0, 2.76, 4.07, 5.4 }, amp = { 1, 0.5, 0.38, 0.22, 0.12 };
		var fr = ratio.Select(k => F(f0 * k)).ToArray();
		int[] cycles = { 3, 2, 1, 4, 3 };   // swells of 10-40 s: nothing near a breathing rate
		var ph = ratio.Select(_ => r.R(0, TwoPi)).ToArray();
		var bow = Biquad.Bp(sr, f0 * 2.76, 8); var bowLp = Biquad.Lp(sr, 900);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr, v = 0;
			for (int p = 0; p < fr.Length; p++)
				v += amp[p] * (0.55 + 0.45 * Math.Sin(TwoPi * cycles[p] * q * t + ph[p])) * Math.Sin(TwoPi * fr[p] * t);
			x[i] = v + bowLp.P(bow.P(r.W())) * 0.9;
		}
		var o = MakeLoop(WithReverb(x, sr, 0.9, 0.5, 0.45), pre, n, xf);
		NormRms(o, -20, -3);
		return o;
	}


	/// <summary>
	/// A deep, sustained dark chord for under the whole walk: E1, B1, E2, B2 and a sour F2
	/// (it rubs against the E an octave up), each as three slightly detuned soft
	/// saws, under a filter that opens and closes over tens of seconds. No
	/// attacks anywhere. Every frequency is a whole number of cycles per loop.
	/// </summary>
	public static double[] ScorePadLoop(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 4 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Max(q, Math.Round(hz / q) * q);
		double[] chord = { 41.2, 61.74, 82.41, 87.31, 123.47 }, vAmp = { 1.0, 0.65, 0.55, 0.4, 0.35 };   // + E2, B2 so small speakers carry it
		double[] detune = { -0.0045, 0, 0.004 };
		var freqs = new double[chord.Length, detune.Length];
		for (int c = 0; c < chord.Length; c++) for (int d = 0; d < detune.Length; d++) freqs[c, d] = F(chord[c] * (1 + detune[d]));
		var lp1 = new Biquad(sr); var lp2 = new Biquad(sr);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			if (i % 64 == 0)
			{
				double sweep = 0.5 + 0.35 * Math.Sin(TwoPi * q * t + a1) + 0.15 * Math.Sin(TwoPi * 2 * q * t + a2);
				double cutoff = 180 * Math.Pow(800 / 180.0, sweep);   // stays dark: 180-800 Hz
				lp1.SetLp(cutoff, 1.2); lp2.SetLp(cutoff * 1.4, 0.7);
			}
			double pad = 0;
			for (int c = 0; c < chord.Length; c++)
			{
				double swell = 0.7 + 0.3 * Math.Sin(TwoPi * (c + 1) * q * t + c * 2.1);
				for (int d = 0; d < detune.Length; d++)
				{
					double f = freqs[c, d], s = 0;
					for (int h = 1; h <= 12 && f * h < 1600; h++) s += Math.Sin(TwoPi * h * f * t + d) / h;
					pad += s * vAmp[c] * swell;
				}
			}
			x[i] = lp2.P(lp1.P(pad));
		}
		var o = MakeLoop(WithReverb(x, sr, 0.92, 0.5, 0.5), pre, n, xf);
		NormRms(o, -20, -3);
		return o;
	}
	/// <summary>The hum's upper, metallic partials on their own, to fade in as things intensify.</summary>
	public static double[] ScoreShimmerLoop(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;
		double f0 = 41.2;
		double[] ratio = { 8.93, 13.2, 17.1, 21.4 }, amp = { 1, 0.6, 0.35, 0.2 };
		int[] cycles = { 2, 3, 1, 4 };
		var ph = ratio.Select(_ => r.R(0, TwoPi)).ToArray();
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr, v = 0;
			for (int p = 0; p < ratio.Length; p++)
				v += amp[p] * Math.Pow(0.5 + 0.5 * Math.Sin(TwoPi * cycles[p] * q * t + ph[p]), 2) * Math.Sin(TwoPi * F(f0 * ratio[p]) * t);
			x[i] = v;
		}
		var o = MakeLoop(WithReverb(x, sr, 0.95, 0.4, 0.6), pre, n, xf);
		NormRms(o, -24, -3);
		return o;
	}


	static double SmoothStep(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }

	static void AddNote(double[] x, int sr, double at, double hz, double dur, double amp, bool sour)
	{
		var lp = Biquad.Lp(sr, 1200);
		int s0 = (int)(at * sr);
		for (int i = 0; i < dur * sr && s0 + i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double env = Env(u, 0.2, 0.7);
			double v = Math.Sin(TwoPi * hz * t) + 0.3 * Math.Sin(TwoPi * 2 * hz * t) + 0.1 * Math.Sin(TwoPi * 3 * hz * t);
			if (sour) v += 0.8 * Math.Sin(TwoPi * hz * Math.Pow(2, 18 / 1200.0) * t);
			x[s0 + i] += lp.P(v) * env * amp;
		}
	}

	static void AddBell(double[] x, int sr, double at, double hz, double amp)
	{
		double[] ratio = { 1, 2.76, 5.4, 8.93 }, dec = { 3.0, 1.6, 0.8, 0.4 }, pa = { 1, 0.4, 0.2, 0.1 };
		int s0 = (int)(at * sr);
		for (int i = 0; i < 4 * sr && s0 + i < x.Length; i++)
		{
			double t = (double)i / sr, v = 0;
			for (int p = 0; p < ratio.Length; p++) v += pa[p] * Math.Sin(TwoPi * hz * ratio[p] * t) * Perc(t, 0.004, dec[p]);
			x[s0 + i] += v * amp;
		}
	}

	static double[] WithReverb(double[] dry, int sr, double size, double damp, double wet)
	{
		var rv = new Reverb(sr, size, damp);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] * (1 - wet * 0.5) + rv.P(dry[i]) * wet;
		return o;
	}
}
