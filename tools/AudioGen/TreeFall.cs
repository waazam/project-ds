namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// A tree coming down (Act 1's falling trees, 2026-10-03; the owner: "a creaking noise when falling then an impactful
/// thud when they hit the ground ... they need to seem heavy"):
/// <list type="bullet">
/// <item>the creak: the trunk giving way at its foot, a deep stick-slip groan through the wood's low modes, faster and
/// higher as it goes, fibres ticking and popping more and more;</item>
/// <item>the crack: the last of the hinge wood splitting, a hard broadband report and a splintering tail;</item>
/// <item>the rush: the crown coming through the air, a swelling roar of boughs and needles;</item>
/// <item>the impact: a sub-heavy slam into the ground, the trunk's own dull knock, branches snapping all along it, and
/// the debris and the earth settling after.</item>
/// </list>
/// </summary>
public static class TreeFall
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Creak(Rng r, int sr)
	{
		double dur = r.R(2.6, 3.2);
		var x = Buf(sr, dur + 0.6);
		// the groan: impulses through the trunk's low modes, the slip rate climbing as it gives
		(double f, double q, double a)[] modes = { (r.R(70, 95), 9, 1.0), (r.R(150, 190), 8, 0.7), (r.R(290, 360), 7, 0.45), (r.R(560, 680), 6, 0.2) };
		var res = new Biquad[modes.Length];
		for (int k = 0; k < modes.Length; k++) res[k] = Biquad.Bp(sr, modes[k].f, modes[k].q);
		var wob = new Smooth(r, dur + 1, r.R(0.08, 0.16));
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				double rate = 9 + 55 * u * u + 18 * wob.At((double)i / sr);   // slips a second
				ph += rate / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1.0) * Env(u, 0.12, 0.08); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].a * res[k].P(imp);
			x[i] += v * 6.0;
		}
		// the fibres: ticks and pops, sparse at first, then crowding in
		for (double t = 0.2; t < dur; t += r.R(0.03, 0.3) * (1.2 - 0.9 * t / dur))
			Slap(x, r, sr, t, r.R(0.02, 0.09) * (0.3 + t / dur), 700, r.R(2500, 5000), r.R(0.002, 0.008));
		return FinishOneShot(Wet(x, sr, 1.0, 0.25, 1.2, 0.5, 0.8), sr, -3, 120);
	}

	public static double[] Crack(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		// the report
		Slap(x, r, sr, 0.0, 1.0, 180, 7000, 0.035, 0.0004);
		Knock(x, r, sr, 0.0, 0.7, r.R(110, 160), 6, 0.12);
		// the splintering: a run of snaps tailing off
		double t = 0.03;
		for (int k = 0; k < 14; k++)
		{
			Slap(x, r, sr, t, 0.55 * Math.Pow(0.82, k) * r.R(0.6, 1.0), 500, r.R(3000, 6500), r.R(0.004, 0.012));
			t += r.R(0.015, 0.07);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.6, 0.45, 1.0), sr, -3, 150);
	}

	public static double[] Rush(Rng r, int sr)
	{
		var x = Buf(sr, 2.4);
		var lpA = Biquad.Lp(sr, 900); var hpA = Biquad.Hp(sr, 120);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / 2.4;
			double swell = Math.Pow(Math.Sin(Math.PI * Math.Min(u * 1.15, 1.0)), 1.6);
			x[i] = hpA.P(lpA.P(r.W())) * swell * (0.6 + 0.4 * Math.Sin(t * 13 + Math.Sin(t * 5)));
		}
		// the needles and twigs whipping through it
		for (double t = 0.3; t < 2.1; t += r.R(0.01, 0.05))
			Slap(x, r, sr, t, r.R(0.05, 0.16) * Math.Sin(Math.PI * t / 2.4), 1500, 7000, r.R(0.002, 0.006));
		return FinishOneShot(x, sr, -3, 200);
	}

	public static double[] Impact(Rng r, int sr)
	{
		var x = Buf(sr, 3.8);
		// the slam: sub weight into the ground, the trunk's own dull knock
		var l1 = Biquad.Lp(sr, r.R(45, 60)); var l2 = Biquad.Lp(sr, r.R(60, 80)); var l3 = Biquad.Lp(sr, 110);
		var b1 = Biquad.Bp(sr, r.R(110, 170), 1.2); var b2 = Biquad.Bp(sr, r.R(220, 320), 1.4);
		var rum1 = Biquad.Lp(sr, 45); var rum2 = Biquad.Lp(sr, 70);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] += l3.P(l2.P(l1.P(r.W()))) * Perc(t, 0.006, 0.28) * 12
				+ b1.P(r.W()) * Perc(t, 0.003, 0.12) * 9
				+ b2.P(r.W()) * Perc(t - 0.01, 0.002, 0.07) * 5
				+ rum2.P(rum1.P(r.W())) * Perc(t - 0.05, 0.12, 1.1) * 9;
		}
		// a second, smaller slam: the crown coming down a beat after the trunk
		double crown = r.R(0.12, 0.2);
		for (int i = (int)(crown * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - crown;
			x[i] += l3.P(l2.P(r.W())) * Perc(t, 0.01, 0.18) * 7;
		}
		// branches snapping all along it, crowded at first, then a few late ones
		for (double t = 0.0; t < 0.9; t += r.R(0.008, 0.06) * (1 + 3 * t))
			Slap(x, r, sr, t, r.R(0.15, 0.5) * (1 - t), 600, r.R(2500, 6000), r.R(0.003, 0.012));
		// debris settling: twigs, needles, earth pattering down
		for (double t = 0.3; t < 2.6; t += r.R(0.02, 0.12))
			Slap(x, r, sr, t, r.R(0.03, 0.1) * (1 - t / 2.6), 800, 5000, r.R(0.002, 0.006));
		LowPass(x, sr, 7000);
		return FinishOneShot(Wet(x, sr, 1.0, 0.3, 2.0, 0.55, 1.2), sr, -3, 300);
	}
}
