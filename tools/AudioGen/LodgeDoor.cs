namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 23's end, the wendigo coming down off the balcony and out through the lodge's front doors (2026-10-04; the
/// owner: "a longer more intimate scene where he jumps off the top balcony and crashes through the door and you see
/// him fully do all of it"):
/// <list type="bullet">
/// <item>the rail: the balcony's log handrail taking its weight as it crouches on it, a slow loaded groan;</item>
/// <item>the landing: nearly five metres of it coming down on the floorboards, a body's thump through the joists,
/// the boards' groan and its claws clattering on them;</item>
/// <item>the ram: its shoulder into the great doors, a deep boom through the leaves, the iron straps rattling, the
/// frame cracking;</item>
/// <item>the rake: its claws dragging down through the wood.</item>
/// </list>
/// </summary>
public static class LodgeDoor
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] RailGroan(Rng r, int sr)
	{
		double dur = r.R(1.6, 2.0);
		var x = Buf(sr, dur + 0.5);
		(double f, double q, double a)[] modes = { (r.R(120, 150), 10, 1.0), (r.R(240, 290), 9, 0.6), (r.R(430, 520), 7, 0.3) };
		var res = new Biquad[modes.Length];
		for (int k = 0; k < modes.Length; k++) res[k] = Biquad.Bp(sr, modes[k].f, modes[k].q);
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (14 + 30 * Math.Sin(Math.PI * u)) / sr;   // the slips crowding in as the weight comes on, easing as it settles
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.2, 0.3); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].a * res[k].P(imp);
			x[i] += v * 5.0;
		}
		for (double t = 0.2; t < dur; t += r.R(0.12, 0.4))
			Slap(x, r, sr, t, r.R(0.02, 0.06), 900, 4000, r.R(0.003, 0.008));
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.6, 0.5, 1.0), sr, -3, 150);
	}

	public static double[] Land(Rng r, int sr)
	{
		var x = Buf(sr, 2.8);
		var l1 = Biquad.Lp(sr, r.R(55, 70)); var l2 = Biquad.Lp(sr, 90);
		var b1 = Biquad.Bp(sr, r.R(120, 160), 1.6); var b2 = Biquad.Bp(sr, r.R(260, 340), 2.0);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			// the feet, then a beat later the hands slamming down
			x[i] += l2.P(l1.P(r.W())) * (Perc(t, 0.004, 0.16) + 0.7 * Perc(t - 0.09, 0.004, 0.12)) * 12
				+ b1.P(r.W()) * (Perc(t, 0.002, 0.08) + 0.6 * Perc(t - 0.09, 0.002, 0.07)) * 7
				+ b2.P(r.W()) * Perc(t - 0.005, 0.002, 0.05) * 3;
		}
		// the floor's own knock, and the boards groaning under it as it settles
		Knock(x, r, sr, 0.0, 0.6, r.R(85, 110), 5, 0.18);
		Knock(x, r, sr, 0.09, 0.4, r.R(180, 230), 6, 0.12);
		for (double t = 0.25; t < 1.6; t += r.R(0.05, 0.18))
			Knock(x, r, sr, t, r.R(0.05, 0.14) * (1.7 - t), r.R(140, 320), 12, r.R(0.03, 0.07));
		// the claws on the boards
		for (int k = 0; k < 7; k++)
			Slap(x, r, sr, 0.02 + k * r.R(0.012, 0.03), r.R(0.12, 0.3), 1800, 6500, r.R(0.002, 0.005), 0.0002);
		LowPass(x, sr, 7000);
		return FinishOneShot(Wet(x, sr, 1.0, 0.4, 2.2, 0.55, 1.3), sr, -3, 300);
	}

	public static double[] Ram(Rng r, int sr, bool burst)
	{
		var x = Buf(sr, burst ? 3.2 : 2.6);
		var l1 = Biquad.Lp(sr, r.R(60, 75)); var l2 = Biquad.Lp(sr, 100);
		var bp = Biquad.Bp(sr, r.R(95, 125), 2.5);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] += l2.P(l1.P(r.W())) * Perc(t, 0.005, 0.22) * 12 + bp.P(r.W()) * Perc(t, 0.003, 0.3) * 8;
		}
		// the leaves' hollow boom, the iron straps and hinges ringing and rattling
		Knock(x, r, sr, 0.0, 0.8, r.R(70, 90), 4, 0.35);
		Knock(x, r, sr, 0.004, 0.5, r.R(210, 260), 5, 0.2);
		foreach (double f in new[] { r.R(1150, 1350), r.R(2300, 2700), r.R(3600, 4100) })
			Knock(x, r, sr, r.R(0.0, 0.01), 0.12, f, 40, r.R(0.12, 0.25));
		for (int k = 0; k < 6; k++)
			Slap(x, r, sr, 0.04 + k * r.R(0.03, 0.08), r.R(0.06, 0.15) * (1 - k / 7.0), 2000, 6000, r.R(0.003, 0.008), 0.0002);
		// the frame cracking: one or two reports and their splinters (a lot more of them as they give)
		int cracks = burst ? 3 : 1;
		for (int c = 0; c < cracks; c++)
		{
			double t0 = r.R(0.01, 0.06) + c * 0.05;
			Slap(x, r, sr, t0, burst ? 0.9 : 0.45, 250, 7000, 0.03, 0.0004);
			double t = t0 + 0.02;
			for (int k = 0; k < (burst ? 16 : 6); k++)
			{
				Slap(x, r, sr, t, 0.4 * Math.Pow(0.85, k) * r.R(0.5, 1.0), 600, r.R(3000, 6500), r.R(0.004, 0.012));
				t += r.R(0.015, 0.06);
			}
		}
		// the strain after: the doors groaning back against the snow
		if (!burst)
			for (double t = 0.4; t < 1.6; t += r.R(0.04, 0.12))
				Knock(x, r, sr, t, r.R(0.04, 0.1) * (1.8 - t), r.R(110, 260), 12, r.R(0.03, 0.06));
		LowPass(x, sr, 7500);
		return FinishOneShot(Wet(x, sr, 1.0, 0.4, 2.4, 0.5, 1.4), sr, -3, 300);
	}

	public static double[] Rake(Rng r, int sr)
	{
		double dur = r.R(0.7, 0.9);
		var x = Buf(sr, dur + 0.6);
		// four claws, a stick-slip judder each, gouging down through the grain
		for (int c = 0; c < 4; c++)
		{
			var bp = Biquad.Bp(sr, r.R(900, 1600), 3); var bp2 = Biquad.Bp(sr, r.R(2600, 4200), 4);
			double off = c * r.R(0.01, 0.03), rate = r.R(55, 90), ph = 0;
			for (int i = (int)(off * sr); i < x.Length; i++)
			{
				double t = (double)i / sr - off, u = t / dur;
				if (u >= 1) break;
				ph += rate * (1 - 0.4 * u) / sr;
				double imp = 0;
				if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1.0); }
				x[i] += (bp.P(imp + r.W() * 0.08) * 1.2 + bp2.P(imp * 0.6)) * Env(u, 0.05, 0.3);
			}
		}
		// splinters lifting off
		for (double t = 0.05; t < dur; t += r.R(0.04, 0.14))
			Slap(x, r, sr, t, r.R(0.08, 0.2), 1500, 6000, r.R(0.003, 0.007), 0.0002);
		return FinishOneShot(Wet(x, sr, 1.0, 0.3, 1.4, 0.5, 1.0), sr, -3, 150);
	}

	/// <summary>Flies round the dining hall's skeleton (2026-10-04): three of them, circling, the buzz rising and falling as
	/// each comes near and goes off. Exactly periodic over the loop (every frequency and every swell a whole number of cycles
	/// in it), so it loops without a seam.</summary>
	public static double[] FlyBuzz(Rng r, int sr)
	{
		const double L = 20.0;
		int n = (int)(L * sr);
		var x = new double[n];
		(double f, double vib, double vibRate, double swell, double ph)[] flies =
		{
			(212, 9, 1.5, 0.5, r.R(0, 6.28)), (238, 12, 2.25, 0.75, r.R(0, 6.28)), (196, 7, 1.0, 0.25, r.R(0, 6.28)),
		};
		foreach (var (f, vib, vibRate, swell, ph) in flies)
		{
			for (int i = 0; i < n; i++)
			{
				double t = (double)i / sr;
				double phase = f * t + vib / (2 * Math.PI * vibRate) * Math.Sin(2 * Math.PI * vibRate * t + ph);
				double a = 0.25 + 0.75 * Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * swell * t + ph * 1.7), 2);
				double v = 0;
				for (int h = 1; h <= 6; h++) v += Math.Sin(2 * Math.PI * h * phase) / h * (h % 2 == 0 ? 0.7 : 1.0);
				x[i] += v * a * 0.33;
			}
		}
		double peak = 0;
		foreach (var v in x) peak = Math.Max(peak, Math.Abs(v));
		double g = Math.Pow(10, -9.0 / 20) / Math.Max(peak, 1e-9);
		for (int i = 0; i < n; i++) x[i] *= g;
		return x;
	}
}
