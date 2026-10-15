namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The omen bird's scream as it flies at you (2026-10-15, the owner: "use alexs saved sound for the blackbird from earlier.
/// but make it louder and more sudden like we have it now"). Alex's sound is the old far-off scream
/// (<see cref="Sfx.DistantScream"/>, kept in assets/audio/storage): the same voice — the rising, breaking cry through
/// the same two resonances, the same 37 Hz rasp — brought right up to the face: it starts already high and at full
/// force (a ~6 ms attack, not a tenth of its length), a hit under its first instant (a low thump and a crack of air),
/// close (a breath of room, not the distance's wash), and pressed loud (driven and levelled, RMS about -12 dBFS).
/// Three takes, each its own pitch and break.
/// </summary>
public static class AlexScream
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Make(Rng r, int sr)
	{
		double dur = r.R(1.25, 1.5);
		var src = Buf(sr, dur);
		double f0 = r.R(340, 420);
		double ph = 0;
		double attack = 0.006 / dur;
		for (int i = 0; i < src.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			// already up in its cry from the first instant (the old one climbed into it), breaking and falling at the end
			double rise = 0.55 + 0.45 * Math.Pow(Math.Min(1, u * 2.2), 0.6);
			double f = f0 * (1 + 1.3 * rise) * (1 - 0.25 * Math.Max(0, u - 0.7) / 0.3);
			ph += TwoPi * f / sr;
			double v = 0;
			for (int h = 1; h <= 10; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / Math.Pow(h, 0.65);
			double rough = 0.6 + 0.4 * Math.Sin(TwoPi * 37 * t);
			double env = Env(u, attack, 0.45) * (u > 0.8 ? Math.Max(0, 1 - (u - 0.8) / 0.2) : 1.0);
			src[i] = Math.Tanh(v * 2.2) * rough * env;
		}
		var bp1 = Biquad.Bp(sr, 900, 1.1); var bp2 = Biquad.Bp(sr, 2200, 1.3);
		var x = new double[src.Length + sr / 3];
		for (int i = 0; i < src.Length; i++) x[i] = bp1.P(src[i]) * 0.8 + bp2.P(src[i]) * 0.5;

		// the hit under its first instant: a low thump and a crack of air
		for (int i = 0; i < sr * 0.25; i++)
		{
			double t = (double)i / sr;
			x[i] += Math.Sin(TwoPi * r.R(58, 72) * t) * Perc(t, 0.002, 0.07) * 0.9;
			x[i] += r.W() * Perc(t, 0.0005, 0.012) * 0.7;
		}

		// close: mostly dry, a breath of room
		var rv = new Reverb(sr, 0.6, 0.5);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 1.0 + rv.P(x[i]) * 0.22;
		LowPass(o, sr, 5200);
		// pressed loud: driven, then levelled
		NormPeak(o, 0);
		for (int i = 0; i < o.Length; i++) o[i] = Math.Tanh(o[i] * 2.4);
		NormRms(o, -12, -3);
		return FinishOneShot(o, sr, -3, 180);
	}
}
