namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Acts 19 and 20: the library and the round room. Quiet, woody, warm: bamboo pieces taken up and set
/// into a box, a soft chord of light as the solved box goes out of the world, a heavy bookcase swinging
/// on an old hinge, and the dais's slow stone lift (tonal and mechanical: no noise bed).
/// </summary>
public static class LibrarySounds
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A bamboo piece picked up off the table: a light dry tick and a whisper of a slide.</summary>
	public static double[] WoodTake(Rng r, int sr)
	{
		var x = Buf(sr, 0.6);
		Slap(x, r, sr, 0.0, 0.25, 1500, 5000, 0.03, 0.004);
		Knock(x, r, sr, 0.03, 0.5, r.R(1700, 2300), 18, 0.018);
		return FinishOneShot(Wet(x, sr, 1.0, 0.18, 0.7, 0.6, 0.6), sr, -3, 120);
	}

	/// <summary>A piece set into the box: a hollow wooden knock, the box ringing a little under it, and a
	/// second small settle.</summary>
	public static double[] WoodPlace(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		Knock(x, r, sr, 0.0, 1.0, r.R(520, 640), 12, 0.05);
		Knock(x, r, sr, 0.0, 0.55, r.R(1300, 1600), 20, 0.03);
		Slap(x, r, sr, 0.0, 0.3, 400, 3500, 0.01, 0.0005);
		double t2 = r.R(0.06, 0.09);
		Knock(x, r, sr, t2, 0.3, r.R(700, 800), 14, 0.03);
		return FinishOneShot(Wet(x, sr, 1.0, 0.22, 0.8, 0.55, 0.7), sr, -3, 150);
	}

	/// <summary>A piece that doesn't fit knocking against the rim: two quick hard taps.</summary>
	public static double[] WoodBump(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		Knock(x, r, sr, 0.0, 1.0, r.R(900, 1100), 16, 0.025);
		Slap(x, r, sr, 0.0, 0.35, 800, 5000, 0.006, 0.0003);
		Knock(x, r, sr, r.R(0.07, 0.1), 0.55, r.R(950, 1150), 16, 0.02);
		return FinishOneShot(Wet(x, sr, 1.0, 0.2, 0.7, 0.6, 0.6), sr, -3, 120);
	}

	/// <summary>The solved box's light: a soft, warm chord swelling in and slowly fading as it goes out
	/// of the world. Pure tones with a slow shimmer, no hiss.</summary>
	public static double[] PuzzleGlow(Rng r, int sr)
	{
		double dur = 5.0;
		var x = Buf(sr, dur + 2.5);
		double[] notes = { 293.66, 369.99, 440.0, 587.33, 739.99 };
		for (int i = 0; i < notes.Length; i++)
		{
			double f = notes[i], delay = i * 0.18, vib = r.R(4.5, 6.0);
			AddTone(x, sr, delay, dur - delay, u => f * (1 + 0.003 * Math.Sin(u * vib * 5)), u => (0.6 - i * 0.07) * Env(u, 0.35, 0.55), new[] { 1.0, 0.18, 0.05 });
		}
		// a high glassy air over it, rising
		AddTone(x, sr, 0.6, 3.6, u => 1174.66 * (1 + 0.02 * u), u => 0.12 * Env(u, 0.4, 0.5), new[] { 1.0, 0.3 });
		LowPass(x, sr, 5000);
		return FinishOneShot(Wet(x, sr, 0.6, 0.8, 3.0, 0.5, 1.4), sr, -3, 800);
	}

	/// <summary>The secret bookcase swinging open: a long, low wooden groan from an old hinge (stick-slip
	/// at a heavy case's low resonances), books rattling faintly, and a soft thud as it comes to rest.</summary>
	public static double[] BookcaseSwing(Rng r, int sr)
	{
		double dur = 2.5;
		var x = Buf(sr, dur + 1.5);
		var modes = new[] { (r.R(280, 330), 18.0, 1.0), (r.R(520, 600), 22.0, 0.55), (r.R(900, 1000), 26.0, 0.3) };
		var res = modes.Select(m => Biquad.Bp(sr, m.Item1, m.Item2)).ToArray();
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				double rate = 38 + 30 * Math.Sin(u * Math.PI) + 6 * Math.Sin(u * 17);
				ph += rate / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.1, 0.25); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].Item3 * res[k].P(imp);
			x[i] = v;
		}
		for (double t = 0.2; t < dur - 0.3; t += r.R(0.12, 0.35))
			Knock(x, r, sr, t, r.R(0.04, 0.1), r.R(700, 1400), 12, 0.02);
		Knock(x, r, sr, dur, 0.9, r.R(90, 120), 6, 0.12);
		Slap(x, r, sr, dur, 0.4, 60, 900, 0.05, 0.002);
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.4, 0.55, 1.0), sr, -3, 300);
	}

	/// <summary>The dais starting to rise: a deep stone knock, a short grinding give, and the mechanism
	/// under it catching (low clunks).</summary>
	public static double[] LiftStart(Rng r, int sr)
	{
		var x = Buf(sr, 3.0);
		Knock(x, r, sr, 0.0, 1.0, r.R(55, 70), 5, 0.3);
		Slap(x, r, sr, 0.0, 0.5, 40, 700, 0.12, 0.004);
		for (double t = 0.25; t < 1.1; t += r.R(0.05, 0.1))
			Slap(x, r, sr, t, r.R(0.1, 0.22) * (1.2 - t), 120, 1400, 0.03, 0.004);   // a short grind as it gives
		Knock(x, r, sr, 1.0, 0.6, r.R(80, 100), 6, 0.15);
		Knock(x, r, sr, 1.3, 0.4, r.R(140, 170), 8, 0.1);
		return FinishOneShot(Wet(x, sr, 1.0, 0.6, 2.8, 0.55, 1.6), sr, -3, 400);
	}

	/// <summary>The dais rising: a slow, even mechanical labour, low and tonal - a deep hum of the drive,
	/// a regular soft clank of a ratchet, a lower heavier beat of counterweights. Loops seamlessly.</summary>
	public static double[] LiftLoop(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double f0 = Math.Round(49 * sec) / sec, f1 = Math.Round(73.5 * sec) / sec;   // whole cycles, for the seam
		double[] hs = { 1.0, 0.5, 0.3, 0.15, 0.08 };
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, v = 0;
			for (int k = 0; k < hs.Length; k++) v += hs[k] * Math.Sin(TwoPi * f0 * (k + 1) * t);
			x[i] = 0.35 * v + 0.12 * (Math.Sin(TwoPi * f1 * t) + 0.4 * Math.Sin(TwoPi * f1 * 2 * t));
		}
		double beat = sec / Math.Round(sec / 0.42);
		for (double t = 0.07; t < sec - 1e-6; t += beat)
			Knock(x, r, sr, t, r.R(0.25, 0.35), r.R(420, 480), 14, 0.03, true);
		double heavy = sec / Math.Round(sec / 1.68);
		for (double t = 0.2; t < sec; t += heavy)
			Knock(x, r, sr, t, 0.5, r.R(85, 95), 7, 0.14, true);
		var h = new Hall(sr, 2.8, 0.55, 30, 1.6);
		var o = Circular(x, v => v * 0.7 + h.P(v) * 0.6);
		NormRms(o, -18);
		return o;
	}

	/// <summary>Arriving: the dais seating home with a heavy stone thud, the mechanism locking.</summary>
	public static double[] LiftStop(Rng r, int sr)
	{
		var x = Buf(sr, 3.0);
		Knock(x, r, sr, 0.0, 1.0, r.R(50, 62), 5, 0.35);
		Slap(x, r, sr, 0.0, 0.55, 40, 600, 0.1, 0.003);
		Knock(x, r, sr, 0.45, 0.55, r.R(300, 360), 12, 0.06);
		Knock(x, r, sr, 0.6, 0.35, r.R(600, 700), 16, 0.04);
		return FinishOneShot(Wet(x, sr, 1.0, 0.6, 2.6, 0.55, 1.5), sr, -3, 400);
	}
}
