namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The bunker door's keypad (2026-10-13, the owner: "make the bunker keypad an actual keypad"): a weathered stainless
/// 3x4 pad in a cast box on the concrete, out in the woods since 1998.
/// <list type="bullet">
/// <item>a press: the worn key's dome snapping under the thumb, the cast housing's small knock, and the pad's own
/// piezo chirp answering (the chirp is one pitch, as a real pad's is; the key under it is never twice the same);</item>
/// <item>the right code: two rising piezo tones and the lock's solenoid clacking back inside the door;</item>
/// <item>a wrong one: a rasping low buzz, three short pulses.</item>
/// </list>
/// Outdoors, so only a short, small slap-back off the concrete face.
/// </summary>
public static class Keypad
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A piezo square-ish tone: odd harmonics, a hard attack and a quick stop, slightly rough.</summary>
	static void Piezo(double[] x, int sr, double t, double dur, double f, double amp)
	{
		AddTone(x, sr, t, dur, _ => f, u => amp * Env(u, 0.04, 0.12), new[] { 1.0, 0.0, 0.33, 0.0, 0.18, 0.0, 0.1 });
	}

	public static double[] Press(Rng r, int sr)
	{
		var x = Buf(sr, 0.4);
		double t0 = 0.004;
		// the dome giving way: a bright tick, sometimes with a second smaller one where a worn key catches
		Slap(x, r, sr, t0, r.R(0.55, 0.85), r.R(1800, 2600), r.R(6000, 8500), r.R(0.0015, 0.003), 0.0002);
		if (r.Chance(0.45)) Slap(x, r, sr, t0 + r.R(0.004, 0.009), r.R(0.12, 0.3), 2200, 7000, 0.0015, 0.0002);
		// the key bottoming out on the plate, and the cast box's own knock
		Knock(x, r, sr, t0 + r.R(0.002, 0.006), r.R(0.35, 0.55), r.R(620, 980), r.R(10, 16), r.R(0.012, 0.022));
		Knock(x, r, sr, t0 + 0.003, r.R(0.12, 0.25), r.R(1500, 2300), 18, r.R(0.006, 0.01));
		// grit under the cap
		for (int k = 0; k < r.I(1, 4); k++)
			Slap(x, r, sr, t0 + r.R(0.0, 0.02), r.R(0.03, 0.08), 3500, 9000, 0.0008, 0.0001);
		// the chirp
		Piezo(x, sr, t0 + r.R(0.012, 0.022), r.R(0.055, 0.075), 2730 + r.R(-6, 6), r.R(0.09, 0.13));
		LowPass(x, sr, 9500);
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.35, 0.5, 0.5), sr, -3, 40);
	}

	public static double[] Accept(Rng r, int sr)
	{
		var x = Buf(sr, 1.1);
		Piezo(x, sr, 0.01, 0.11, 2048, 0.35);
		Piezo(x, sr, 0.15, 0.2, 2730, 0.35);
		// the solenoid inside the door pulling back: a heavy clack through steel, a little late
		double tc = 0.42;
		Slap(x, r, sr, tc, 1.0, 1200, 6500, 0.004, 0.0002);
		Knock(x, r, sr, tc + 0.002, 0.8, r.R(140, 170), 7, 0.07);
		Knock(x, r, sr, tc + 0.003, 0.4, r.R(780, 900), 18, 0.04);
		Knock(x, r, sr, tc + 0.004, 0.15, r.R(2400, 2800), 30, 0.08);
		LowPass(x, sr, 9000);
		return FinishOneShot(Wet(x, sr, 1.0, 0.2, 0.6, 0.5, 0.7), sr, -3, 120);
	}

	public static double[] Deny(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		double f = r.R(370, 420), on = r.R(0.13, 0.16), gap = r.R(0.05, 0.07);
		for (int k = 0; k < 3; k++)
		{
			double t = 0.01 + k * (on + gap);
			// a low piezo pushed past what it can do: square, rasping (a little roughness in the pitch)
			double fk = f * (1 + r.R(-0.006, 0.006));
			AddTone(x, sr, t, on, u => fk * (1 + 0.012 * Math.Sin(u * 90)), u => 0.4 * Env(u, 0.03, 0.08),
				new[] { 1.0, 0.0, 0.5, 0.0, 0.35, 0.0, 0.25, 0.0, 0.18, 0.0, 0.12 });
		}
		var bp = Biquad.Bp(sr, 1100, 0.7);
		for (int i = 0; i < x.Length; i++) x[i] = 0.6 * x[i] + 0.8 * bp.P(x[i]);
		LowPass(x, sr, 6000);
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.35, 0.5, 0.5), sr, -3, 60);
	}
}
