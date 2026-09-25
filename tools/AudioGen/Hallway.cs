namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 15, the long hallway: its ventilation hum, the relay that throws the lights, the low siren
/// that comes with the red, the shadow man's breath close behind, and the strike when he takes you.
/// Tonal and event-built; the loop is written circularly.
/// </summary>
public static class Hallway
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>The hallway's air: a low mains-and-duct hum, a slow beat in it, and a far thin whistle.</summary>
	public static double[] HallHum(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double W(double f) => Math.Round(f * sec) / sec;
		double f0 = W(50), f1 = W(50.6), f2 = W(900), beat = W(0.09);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double v = Math.Sin(TwoPi * f0 * t) + 0.6 * Math.Sin(TwoPi * f1 * t + 1) + 0.3 * Math.Sin(TwoPi * f0 * 2 * t + 2) + 0.12 * Math.Sin(TwoPi * f0 * 3 * t);
			v *= 0.75 + 0.25 * Math.Sin(TwoPi * beat * t);
			v += 0.03 * Math.Sin(TwoPi * f2 * t) * (0.5 + 0.5 * Math.Sin(TwoPi * W(0.13) * t + 1));
			x[i] = Math.Tanh(v * 0.5);
		}
		for (double t = r.R(0, 3); t < sec; t += r.R(4, 9))
			Knock(x, r, sr, t, r.R(0.05, 0.12), r.LogR(300, 900), 20, r.R(0.2, 0.4), true);
		var h = new Hall(sr, 2.6, 0.6, 30, 1.5);
		var o = Circular(x, v => v * 0.75 + h.P(v) * 0.5);
		NormRms(o, -26);
		return o;
	}

	/// <summary>A heavy relay throwing the lights over: a clack and the thump of the contactor.</summary>
	public static double[] RelayClunk(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		Slap(x, r, sr, 0.0, 1.0, 1500, 7000, 0.004, 0.0002);
		Knock(x, r, sr, 0.003, 0.8, r.R(120, 160), 8, 0.08);
		Knock(x, r, sr, 0.004, 0.4, r.R(700, 900), 16, 0.05);
		return FinishOneShot(Wet(x, sr, 1.0, 0.4, 1.6, 0.6, 1.3), sr, -3, 200);
	}

	/// <summary>A low siren: one long slow rise and fall, deep and hoarse, far off down the hall.</summary>
	public static double[] SirenLow(Rng r, int sr)
	{
		double dur = 3.2;
		var x = Buf(sr, dur + 1.5);
		AddTone(x, sr, 0.0, dur, u => 95 + 70 * Math.Sin(Math.PI * u), u => Env(u, 0.25, 0.35), new[] { 1.0, 0.6, 0.45, 0.3, 0.2, 0.12 });
		AddTone(x, sr, 0.0, dur, u => 96.5 + 70 * Math.Sin(Math.PI * u), u => 0.5 * Env(u, 0.25, 0.35), new[] { 1.0, 0.5, 0.3 });
		LowPass(x, sr, 900);
		return FinishOneShot(Wet(x, sr, 0.6, 0.8, 2.8, 0.6, 1.5), sr, -3, 500);
	}

	/// <summary>Right behind you: a long slow exhale, close and dry, with a faint rattle in it.</summary>
	public static double[] ShadowBreath(Rng r, int sr)
	{
		double dur = 1.8;
		var x = Buf(sr, dur + 0.3);
		var bp = Biquad.Bp(sr, 700, 1.2); var bp2 = Biquad.Bp(sr, 1900, 2.5);
		for (int i = 0; i < (int)(dur * sr); i++)
		{
			double u = (double)i / sr / dur;
			double e = Env(u, 0.2, 0.5);
			double w = r.W();
			x[i] = (bp.P(w) * 0.8 + bp2.P(w) * 0.3) * e;
		}
		for (double t = 0.3; t < dur * 0.8; t += r.R(0.03, 0.06))
			Knock(x, r, sr, t, r.R(0.05, 0.15) * Env(t / dur, 0.3, 0.4), r.R(80, 130), 6, 0.02);
		return FinishOneShot(x, sr, -3, 150);
	}

	/// <summary>The sewer's air: water dripping into water from all over, near and far, a slow trickle
	/// somewhere, and a very low hollow tone of the big room.</summary>
	public static double[] SewerAir(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double W(double f) => Math.Round(f * sec) / sec;
		double f0 = W(38), f1 = W(57.3);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] = 0.06 * (Math.Sin(TwoPi * f0 * t) + 0.5 * Math.Sin(TwoPi * f1 * t + 1)) * (0.7 + 0.3 * Math.Sin(TwoPi * W(0.07) * t));
		}
		for (double t = r.R(0, 0.5); t < sec; t += r.R(0.15, 1.1))
			Bubble(x, sr, t, r.LogR(700, 2600), r.LogR(0.1, 0.8), r.R(0.005, 0.015), true);
		for (double t = 0; t < sec; t += r.R(0.03, 0.09))
			Bubble(x, sr, t, r.LogR(300, 900), r.LogR(0.03, 0.12), r.R(0.006, 0.02), true);
		var h = new Hall(sr, 3.0, 0.55, 30, 1.6);
		var o = Circular(x, v => v * 0.6 + h.P(v) * 0.7);
		NormRms(o, -24);
		return o;
	}

	/// <summary>Taken: a sub drop, a swelling dissonant cluster, and the hit.</summary>
	public static double[] ShadowStrike(Rng r, int sr)
	{
		var x = Buf(sr, 3.2);
		AddTone(x, sr, 0.0, 0.9, u => 70 - 35 * u, u => Env(u, 0.6, 0.1), new[] { 1.0, 0.5, 0.25 });
		foreach (double f in new[] { 220.0, 233.1, 311.1, 329.6 })
			AddTone(x, sr, 0.0, 0.9, u => f * (1 - 0.03 * u), u => 0.25 * Math.Pow(u, 2), new[] { 1.0, 0.4, 0.2 });
		Slap(x, r, sr, 0.9, 1.0, 40, 3000, 0.08, 0.001);
		Knock(x, r, sr, 0.901, 0.7, 55, 4, 0.5);
		return FinishOneShot(Wet(x, sr, 0.8, 0.6, 2.4, 0.6, 1.4), sr, -3, 600);
	}
}
