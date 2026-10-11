namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// 2026-10-11: a sound for each stretch of the story, heard often enough to be remembered by (the owner's list: "a lullaby on
/// the radio, a music box in the lodge, church bells in the snow"). One tune runs through them: a slow lullaby in A minor.
/// <list type="bullet">
/// <item>musicbox_lullaby: a wind-up box's steel comb plucked through the tune, a little out of true, the mechanism ticking
/// under it, and at the end winding down: slower, flatter, a last note left hanging;</item>
/// <item>church_bell_far: a church bell tolled three times far off across snow, the hum of it hanging between;</item>
/// <item>radio_lullaby: the tune as if from a far station: thin, band-limited, swimming in and out of static.</item>
/// </list>
/// </summary>
public static class Motifs
{
	// the lullaby: (semitones from A4, beats); 72 bpm
	static readonly (int n, double b)[] Tune =
	{
		(0, 1), (3, 1), (7, 1), (5, 2), (3, 1),
		(2, 1), (3, 1), (-2, 1), (0, 3),
		(7, 1), (8, 1), (7, 1), (5, 1), (3, 1), (2, 1),
		(0, 1), (-2, 1), (-5, 1), (0, 3),
	};

	static double Hz(int n) => 440.0 * System.Math.Pow(2, n / 12.0);

	/// <summary>A tine: a bright struck partial and its inharmonic overtones, decaying fast then long.</summary>
	static void Tine(double[] x, int sr, double at, double f, double amp, Rng r)
	{
		double detune = 1 + r.R(-0.004, 0.004);
		foreach (var (mul, a, tau) in new[] { (1.0, 1.0, 1.6), (2.76, 0.35, 0.5), (5.4, 0.18, 0.18), (8.93, 0.08, 0.08) })
			AddTone(x, sr, at, tau * 4.5, _ => f * mul * detune, t => a * amp * System.Math.Exp(-t / tau) * System.Math.Min(1, t * 900));
	}

	public static double[] MusicBox(Rng r, int sr)
	{
		double beat = 60.0 / 72.0;
		double total = 0; foreach (var (_, b) in Tune) total += b * beat;
		var x = new double[(int)((total + 5.0) * sr)];
		double t = 0.4;
		int i = 0;
		foreach (var (n, b) in Tune)
		{
			// the spring running down over the last phrase: slower, a little flat
			double wind = System.Math.Max(0, (t - total * 0.7) / (total * 0.3));
			double f = Hz(n + 12) * (1 - 0.03 * wind * wind);
			Tine(x, sr, t, f, 0.5, r);
			if (i % 2 == 0) Tine(x, sr, t + 0.004, Hz(n), 0.18, r);   // (a low comb tooth under every other)
			t += b * beat * (1 + 0.6 * wind * wind);
			i++;
			if (i == Tune.Length - 1 && wind > 0.5) break;   // (the last note never comes)
		}
		// the mechanism: a soft tick each half beat
		for (double k = 0.3; k < t; k += beat * 0.5)
			Knock(x, r, sr, k, 0.025, r.R(2200, 2800), 4, 0.004);
		return FinishOneShot(Wet(x, sr, 0.8, 0.35, 1.6, 0.4, 0.7), sr, -3, 400);
	}

	public static double[] ChurchBell(Rng r, int sr)
	{
		var x = new double[(int)(16.0 * sr)];
		foreach (double at in new[] { 0.2, 4.2, 8.2 })
			foreach (var (mul, a, tau) in new[] { (0.5, 0.5, 6.0), (1.0, 0.9, 4.0), (1.19, 0.4, 2.6), (1.5, 0.35, 2.0), (2.0, 0.3, 1.5), (2.52, 0.15, 0.9) })
				AddTone(x, sr, at, tau * 3.5, _ => 196.0 * mul, t => a * System.Math.Exp(-t / tau) * System.Math.Min(1, t * 400));
		// far off: the highs gone, a long snowy room
		LowPass(x, sr, 1800);
		return FinishOneShot(Wet(x, sr, 0.45, 0.8, 4.5, 0.6, 1.8), sr, -3, 1500);
	}

	public static double[] RadioLullaby(Rng r, int sr)
	{
		var box = MusicBox(r, sr);
		var x = new double[box.Length];
		var bp = Biquad.Bp(sr, 1400, 0.9);
		var hp = Biquad.Hp(sr, 2000);
		var swim = new Smooth(r, box.Length / (double)sr + 1, 0.8);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double s = 0.55 + 0.45 * swim.At(t);
			x[i] = bp.P(box[i]) * 1.6 * s + hp.P(r.W()) * 0.12 * (1.2 - s);
		}
		return FinishOneShot(x, sr, -3, 400);
	}
}
