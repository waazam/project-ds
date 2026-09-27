namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 21: the church in the winter woods. Soft footsteps in snow (the owner: softer steps for the
/// winter), the wind worrying round a big stone building (gusts, a hollow moan in the windows, a thin
/// whistle through the leading), and the church's own air: a very low, steady hush with the stone
/// ticking and a far-off creak of the roof now and then.
/// </summary>
public static class ChurchSounds
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A footstep in fresh snow: a muffled press, the dry squeak of it packing under the sole.</summary>
	public static double[] StepSnow(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		var lp = Biquad.Lp(sr, r.R(500, 700));
		int n = (int)(0.18 * sr);
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr;
			x[i] += lp.P(r.W()) * Perc(t, 0.012, 0.06) * 0.9;
		}
		// the squeak: a fast run of tiny bright grains, softly
		var bp = Biquad.Bp(sr, r.R(2200, 3200), 2.2);
		double start = r.R(0.02, 0.05), dur = r.R(0.08, 0.14);
		for (int i = (int)(start * sr); i < (int)((start + dur) * sr) && i < x.Length; i++)
		{
			double u = (i / (double)sr - start) / dur;
			double g = r.Chance(0.35) ? r.W() : 0;
			x[i] += bp.P(g) * 0.5 * Env(u, 0.2, 0.6);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.7, 0.5), sr, -3, 60);
	}

	/// <summary>Winter wind round the church: slow gusts of filtered air, a hollow tone that swells in the
	/// windows with them, a faint thin whistle. Loops seamlessly.</summary>
	public static double[] WinterWind(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		var lp = Biquad.Lp(sr, 700); var lp2 = Biquad.Lp(sr, 1200);
		var bp = Biquad.Bp(sr, 380, 6); var hi = Biquad.Bp(sr, 1850, 14);
		double gustF = System.Math.Round(0.07 * sec) / sec, gust2 = System.Math.Round(0.19 * sec) / sec;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double g = 0.55 + 0.3 * System.Math.Sin(TwoPi * gustF * t) + 0.15 * System.Math.Sin(TwoPi * gust2 * t + 1.3);
			double w = r.W();
			double air = lp2.P(lp.P(w)) * g * 1.6;
			double moan = bp.P(w) * g * g * 2.2;
			double whistle = hi.P(w) * System.Math.Max(0, g - 0.7) * 3.0;
			x[i] = air + moan + whistle;
		}
		var h = new Hall(sr, 3.0, 0.6, 30, 1.6);
		for (int i = 0; i < x.Length; i++) x[i] = x[i] * 0.8 + h.P(x[i]) * 0.4;
		// the filters and the room start cold: cut a loop from the settled middle, crossfaded at its seam
		var o = MakeLoop(x, sr * 2, (int)((sec - 4) * sr), sr * 2);
		NormRms(o, -22);
		return o;
	}

	/// <summary>The church's hush: a deep steady air tone, the building ticking with cold now and then, a
	/// far roof beam's creak. Loops seamlessly.</summary>
	public static double[] ChurchTone(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double f0 = System.Math.Round(41 * sec) / sec, f1 = System.Math.Round(61.5 * sec) / sec;
		var lp = Biquad.Lp(sr, 220);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] = 0.2 * System.Math.Sin(TwoPi * f0 * t) + 0.08 * System.Math.Sin(TwoPi * f1 * t) + lp.P(r.W()) * 0.25;
		}
		for (double t = 1.0; t < sec - 1.0; t += r.R(2.0, 5.0))
			Knock(x, r, sr, t, r.R(0.05, 0.15), r.LogR(900, 2600), 18, 0.02, true);
		for (double t = r.R(4, 9); t < sec - 3; t += r.R(9, 16))
			for (int k = 0; k < 6; k++) Knock(x, r, sr, t + k * r.R(0.04, 0.09), r.R(0.08, 0.2), r.R(150, 260), 10, 0.05, true);
		var h = new Hall(sr, 5.5, 0.55, 90, 2.2);
		var o = Circular(x, v => v * 0.6 + h.P(v) * 0.7);
		NormRms(o, -26);
		return o;
	}
}
