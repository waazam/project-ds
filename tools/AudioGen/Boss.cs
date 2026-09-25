namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 18, the boss room. The owner's notes on the first pass ("the blood sounds like bubbles", "the
/// valves sound like someone belching") shaped this one:
/// <list type="bullet">
/// <item>blood is thick: slow, heavy sloshes and gloops, low and slow to decay, sucking, never bubbling;</item>
/// <item>a valve is a squeaky hinge: short, high stick-slip squeals, one per bit of a turn, and a clunk into place;</item>
/// <item>each valve done sets off a siren: a menacing mechanical wail rising and falling through the room;</item>
/// <item>a limb coming down on the catwalk is a wet, heavy slam onto steel grating that rattles and rings.</item>
/// </list>
/// </summary>
public static class Boss
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>One thick "glorp": a low mass of liquid shifting, pitch sagging, swelling in and dying slowly.</summary>
	static void Glorp(double[] x, Rng r, int sr, double t, double amp, bool wrap = false)
	{
		double f0 = r.R(70, 130), dur = r.R(0.25, 0.5), sag = r.R(0.35, 0.55);
		AddTone(x, sr, t, dur, u => f0 * (1 - sag * u) * (1 + 0.04 * Math.Sin(u * 30)), u => amp * Env(u, 0.25, 0.6), new[] { 1.0, 0.35, 0.12 }, wrap);
	}

	/// <summary>A heavy slosh: a low, soft-edged surge of liquid against a wall (swelling, not a slap).</summary>
	static void Slosh(double[] x, Rng r, int sr, double t, double amp, bool wrap = false)
		=> Slap(x, r, sr, t, amp, 40, r.R(240, 420), r.R(0.18, 0.35), r.R(0.06, 0.14), wrap);

	/// <summary>A valve squeaking round a notch: stick-slip at a hinge's high resonances, the rate (and
	/// so the pitch) rising as it gives, a short 0.35-0.6 s squeal.</summary>
	public static double[] ValveSqueak(Rng r, int sr)
	{
		double dur = r.R(0.35, 0.6);
		var x = Buf(sr, dur + 0.5);
		var modes = new[] { (r.R(2300, 2800), 45.0, 1.0), (r.R(3600, 4300), 55.0, 0.6), (r.R(1350, 1600), 35.0, 0.45) };
		var res = modes.Select(m => Biquad.Bp(sr, m.Item1, m.Item2)).ToArray();
		double ph = 0, rate0 = r.R(160, 220), rate1 = rate0 * r.R(1.4, 1.9);
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (rate0 + (rate1 - rate0) * u) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.6, 1.0) * Env(u, 0.08, 0.25); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].Item3 * res[k].P(imp);
			x[i] = v;
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.25, 1.4, 0.55, 1.1), sr, -3, 200);
	}

	/// <summary>The wheel coming up hard against its stop: a knock and a short metal ring.</summary>
	public static double[] ValveClunk(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		Slap(x, r, sr, 0.0, 0.8, 150, 2500, 0.012, 0.0005);
		Knock(x, r, sr, 0.001, 0.8, r.R(420, 520), 25, 0.25);
		Knock(x, r, sr, 0.001, 0.4, r.R(1300, 1600), 30, 0.15);
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.8, 0.6, 1.2), sr, -3, 200);
	}

	/// <summary>A menacing mechanical siren: a rotor's wail winding up, holding, winding down, hoarse,
	/// with a sour second voice under it, far off round the pit.</summary>
	public static double[] Siren(Rng r, int sr)
	{
		double dur = 4.6;
		var x = Buf(sr, dur + 2);
		Func<double, double> sweep = u => u < 0.35 ? 180 + 360 * Math.Sin(u / 0.35 * Math.PI * 0.5) : u < 0.6 ? 540 + 8 * Math.Sin(u * 40) : 540 - 330 * Math.Sin((u - 0.6) / 0.4 * Math.PI * 0.5);
		AddTone(x, sr, 0, dur, sweep, u => Env(u, 0.15, 0.3), new[] { 1.0, 0.7, 0.5, 0.35, 0.25, 0.18, 0.12 });
		AddTone(x, sr, 0, dur, u => sweep(u) * 0.749, u => 0.45 * Env(u, 0.15, 0.3), new[] { 1.0, 0.5, 0.3 });
		LowPass(x, sr, 2600);
		return FinishOneShot(Wet(x, sr, 0.55, 0.9, 3.2, 0.6, 1.6), sr, -3, 600);
	}

	/// <summary>The blood going down: a slow, thick draining, heavy gloops and sucking surges, the
	/// level sagging with a long low groan of liquid through a big pipe.</summary>
	public static double[] BloodDrain(Rng r, int sr)
	{
		double dur = 5.5;
		var x = Buf(sr, dur + 1.5);
		AddTone(x, sr, 0, dur, u => 55 - 20 * u, u => 0.5 * Env(u, 0.2, 0.3), new[] { 1.0, 0.5, 0.25 });
		for (double t = 0.1; t < dur; t += r.R(0.18, 0.45))
		{
			double u = t / dur;
			Glorp(x, r, sr, t, r.R(0.4, 0.9) * Env(u, 0.2, 0.3));
			if (r.Chance(0.5)) Slosh(x, r, sr, t + r.R(0, 0.1), r.R(0.3, 0.7) * Env(u, 0.2, 0.3));
		}
		LowPass(x, sr, 900);
		return FinishOneShot(Wet(x, sr, 0.8, 0.5, 2.6, 0.6, 1.5), sr, -3, 500);
	}

	/// <summary>A limb slammed down on the catwalk: the wet weight of it, the steel grating ringing and
	/// rattling in its frame, the bolts chattering, blood thrown off it.</summary>
	public static double[] CatwalkSlam(Rng r, int sr)
	{
		var x = Buf(sr, 2.4);
		Slap(x, r, sr, 0.0, 1.0, 30, 700, 0.07, 0.003);                  // the weight
		Glorp(x, r, sr, 0.005, 0.8);                                      // wet
		Knock(x, r, sr, 0.002, 0.7, r.R(260, 340), 18, 0.5);             // the grating's big ring
		Knock(x, r, sr, 0.002, 0.45, r.R(780, 950), 26, 0.35);
		Knock(x, r, sr, 0.002, 0.25, r.R(1900, 2300), 32, 0.2);
		for (double t = 0.03; t < 0.6; t += r.R(0.02, 0.05))            // rattling in its frame
			Knock(x, r, sr, t, r.R(0.08, 0.25) * (1 - t / 0.6), r.R(1200, 3000), 20, 0.02);
		for (double t = 0.1; t < 0.9; t += r.R(0.05, 0.15))              // blood thrown off it, splattering
			Slap(x, r, sr, t, r.R(0.08, 0.25), 300, 2500, r.R(0.01, 0.03), 0.004);
		return FinishOneShot(Wet(x, sr, 0.9, 0.45, 2.2, 0.6, 1.4), sr, -3, 500);
	}

	/// <summary>A limb hauling up out of the blood and rearing: thick liquid pouring off it, a slow wet slither.</summary>
	public static double[] LimbRise(Rng r, int sr)
	{
		double dur = 1.6;
		var x = Buf(sr, dur + 1);
		for (double t = 0.0; t < dur; t += r.R(0.12, 0.28))
		{
			double u = t / dur;
			Glorp(x, r, sr, t, r.R(0.3, 0.7) * Env(u, 0.3, 0.4));
			Slosh(x, r, sr, t, r.R(0.2, 0.5) * Env(u, 0.3, 0.4));
		}
		AddTone(x, sr, 0.1, dur - 0.2, u => 38 + 12 * u, u => 0.4 * Env(u, 0.4, 0.4), new[] { 1.0, 0.5 });
		LowPass(x, sr, 800);
		return FinishOneShot(Wet(x, sr, 0.8, 0.45, 2.2, 0.6, 1.4), sr, -3, 400);
	}

	/// <summary>The pit: a slow, huge heartbeat, a low sour drone, and thick blood heaving against the walls below.</summary>
	public static double[] PitAir(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double W(double f) => Math.Round(f * sec) / sec;
		double f0 = W(31), f1 = W(46.5 + 0.4);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] = 0.2 * (Math.Sin(TwoPi * f0 * t) + 0.6 * Math.Sin(TwoPi * f1 * t + 1)) * (0.7 + 0.3 * Math.Sin(TwoPi * W(0.05) * t));
		}
		int beats = (int)Math.Round(sec / 2.2);
		for (int b = 0; b < beats; b++)
		{
			double t = b * sec / beats;
			AddTone(x, sr, t, 0.35, u => 48 - 16 * u, u => 0.9 * Env(u, 0.05, 0.7), new[] { 1.0, 0.4 }, true);
			AddTone(x, sr, t + 0.32, 0.3, u => 42 - 14 * u, u => 0.6 * Env(u, 0.05, 0.7), new[] { 1.0, 0.4 }, true);
		}
		// the blood: heavy, slow, viscous - sloshes and gloops, no bubbles
		for (double t = r.R(0, 1); t < sec; t += r.R(0.6, 1.8))
		{
			Slosh(x, r, sr, t, r.R(0.15, 0.4), true);
			if (r.Chance(0.6)) Glorp(x, r, sr, t + r.R(0.05, 0.3), r.R(0.12, 0.3), true);
		}
		var h = new Hall(sr, 3.5, 0.6, 30, 1.8);
		var o = Circular(x, v => v * 0.6 + h.P(v) * 0.7);
		NormRms(o, -20);
		return o;
	}
}
