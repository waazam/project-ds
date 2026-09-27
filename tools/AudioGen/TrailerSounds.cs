namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The teaser's score (about 90 seconds), timed to <c>TrailerDirector</c>'s cuts:
/// <list type="bullet">
/// <item>three seconds of nothing, then a low drone that swells for over a minute;</item>
/// <item>under each glide through the woods, a wind that rises with it;</item>
/// <item>a single deep toll on the cabin;</item>
/// <item>a thin dissonant cluster for the glimpse in the trees, and again for the eyes in the crypt;</item>
/// <item>soft booms on the cuts;</item>
/// <item>a heartbeat from the bunker on, slow, quickening to the door;</item>
/// <item>a riser from the church into the rush at the door;</item>
/// <item>the slam, and silence; then the first staircase in the fog, a cold air and one toll;</item>
/// <item>under the title, one great hit with a long tail, and a breath of a whisper.</item>
/// </list>
/// Tonal and low, never shrill, nothing faster than the cuts.
/// </summary>
public static class TrailerSounds
{
	// the cut (keep in step with TrailerDirector)
	const double Dur = 97.5, Slam = 82.0, Stairs = 85.0, Title = 92.5;
	static readonly double[] Cuts = { 13, 19, 22, 30, 37, 44, 51, 58, 66, 74, 78 };

	public static double[] Score(Rng r, int sr)
	{
		var x = new double[(int)(Dur * sr)];
		double droneLen = Slam - 2.5;
		// the drone: a low fifth and a slow beating, swelling to the slam
		AddTone(x, sr, 2.5, droneLen, u => 41.2 * (1 + 0.002 * System.Math.Sin(u * 90)), u => 0.3 * Env(u, 0.08, 0.005) * (0.5 + 0.5 * u), new[] { 1.0, 0.45, 0.2, 0.08 });
		AddTone(x, sr, 2.5, droneLen, u => 61.9, u => 0.13 * Env(u, 0.12, 0.005) * (0.4 + 0.6 * u), new[] { 1.0, 0.3 });
		AddTone(x, sr, 2.5, droneLen, u => 41.9, u => 0.15 * Env(u, 0.1, 0.005) * u, new[] { 1.0, 0.3 });
		// the glides through the woods
		Rush(x, r, sr, 3.0, 10.0, 0.45);
		Rush(x, r, sr, 58.0, 8.0, 0.55);
		Rush(x, r, sr, 78.0, 4.0, 0.8);
		// the cabin: a deep, slow toll
		AddTone(x, sr, 13.05, 5.5, u => 73.4, u => 0.4 * System.Math.Exp(-u * 3.0), new[] { 1.0, 0.6, 0.35, 0.28, 0.2, 0.12, 0.08 });
		// the glimpse in the trees, and the eyes in the crypt: a thin beating cluster, very quiet
		foreach (var (t0, len) in new[] { (19.4, 2.8), (56.4, 1.8) })
			foreach (double f in new[] { 880.0, 932.3, 987.8 })
				AddTone(x, sr, t0, len, u => f * (1 + 0.004 * System.Math.Sin(u * 30)), u => 0.05 * Env(u, 0.3, 0.4), new[] { 1.0, 0.2 });
		// soft booms on the cuts
		foreach (double t in Cuts)
		{
			Knock(x, r, sr, t, 0.5, r.R(42, 50), 4, 0.4);
			Slap(x, r, sr, t, 0.22, 30, 400, 0.12, 0.004);
		}
		// the heartbeat: slow from the bunker on, quickening to the door
		double beat = 30.0, span = Slam - 0.3 - 30.0;
		while (beat < Slam - 0.3)
		{
			double u = (beat - 30.0) / span;
			double gap = 60.0 / (52 + 70 * u * u);
			Knock(x, r, sr, beat, 0.4 + 0.35 * u, 52, 5, 0.09);
			Knock(x, r, sr, beat + gap * 0.28, 0.28 + 0.25 * u, 58, 5, 0.08);
			beat += gap;
		}
		// the riser: slowly climbing and thickening from the church into the door
		AddTone(x, sr, 66.0, Slam - 66.0, u => 98 * System.Math.Pow(2, u * 1.8), u => 0.17 * u * u, new[] { 1.0, 0.5, 0.3, 0.2 });
		// the slam, then nothing
		Knock(x, r, sr, Slam, 1.0, r.R(55, 65), 4, 0.5);
		Slap(x, r, sr, Slam, 1.0, 40, 1800, 0.12, 0.002);
		Knock(x, r, sr, Slam + 0.01, 0.6, 180, 8, 0.12);
		for (int i = (int)((Slam + 0.9) * sr); i < (int)(Stairs * sr); i++) x[i] *= 0.2;
		// the last shot: the first staircase out of the fog. A cold air, a thin low drone, one toll as it shows
		Rush(x, r, sr, Stairs, Title - 0.5 - Stairs, 0.12);
		AddTone(x, sr, Stairs, Title - Stairs, u => 55.0, u => 0.12 * Env(u, 0.4, 0.1), new[] { 1.0, 0.4, 0.15 });
		AddTone(x, sr, Stairs + 4.0, 4.5, u => 65.4, u => 0.35 * System.Math.Exp(-u * 3.0), new[] { 1.0, 0.6, 0.35, 0.28, 0.2, 0.12 });
		// the title: one great hit, a long dark tail, a breath of a whisper
		Knock(x, r, sr, Title, 1.0, 36, 3, 1.4);
		AddTone(x, sr, Title, 5.2, u => 36.7, u => 0.35 * System.Math.Exp(-u * 1.1), new[] { 1.0, 0.5, 0.25 });
		var bp = Biquad.Bp(sr, 1400, 1.4); var bp2 = Biquad.Bp(sr, 2600, 3);
		for (int i = (int)((Title + 1.2) * sr); i < (int)((Title + 3.0) * sr); i++)
		{
			double u = (i / (double)sr - Title - 1.2) / 1.8;
			double w = r.W();
			x[i] += (bp.P(w) * 0.5 + bp2.P(w) * 0.2) * Env(u, 0.3, 0.5) * 0.35;
		}
		LowPass(x, sr, 9000);
		var o = Wet(x, sr, 1.0, 0.35, 2.4, 0.5, 1.4);
		int n = System.Math.Min(o.Length, (int)(Dur * sr));
		var outp = new double[n];
		for (int i = 0; i < n; i++) outp[i] = o[i] * System.Math.Min(1.0, (Dur - (double)i / sr) / 0.8);
		NormPeak(outp, -3);
		return outp;
	}

	static void Rush(double[] x, Rng r, int sr, double t0, double len, double amp)
	{
		var lp = Biquad.Lp(sr, 600); var bp = Biquad.Bp(sr, 900, 1.2);
		int a = (int)(t0 * sr), b = System.Math.Min(x.Length, (int)((t0 + len) * sr));
		for (int i = a; i < b; i++)
		{
			double u = (i - a) / (double)(b - a);
			double w = r.W();
			double g = amp * System.Math.Min(1.0, u * 3) * (0.55 + 0.45 * u) * System.Math.Min(1.0, (1 - u) * 20);
			x[i] += (lp.P(w) * 1.2 + bp.P(w) * 0.5 * u) * g;
		}
	}
}
