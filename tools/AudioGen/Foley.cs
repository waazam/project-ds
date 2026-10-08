namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The audio sweep's Foley (the things the player does that were silent: notes, pickups, the lantern,
/// locked doors, gravel underfoot) and its haunting (sounds for the interiors' dark corners: floorboards
/// taking a slow weight somewhere overhead, a moan of air through brick, water dripping in a long space, a
/// chain shifting far off). All events; no sustained noise beds (the owner's rule).
/// </summary>
public static class Foley
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>Stick-slip creak through resonant modes (wood, rope, a hinge).</summary>
	static void Creak(double[] x, Rng r, int sr, double t0, double dur, double rateLo, double rateHi, (double f, double q, double a)[] modes, double amp)
	{
		var res = modes.Select(m => Biquad.Bp(sr, m.f, m.q)).ToArray();
		var rate = new Smooth(r, dur + 1, r.R(0.06, 0.14));
		double ph = 0;
		int s0 = (int)(t0 * sr), n = (int)(dur * sr);
		for (int i = 0; i < n + sr / 2 && s0 + i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (rateLo + (rateHi - rateLo) * rate.At((double)i / sr)) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.2, 0.35); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].a * res[k].P(imp);
			x[s0 + i] += v * amp;
		}
	}

	/// <summary>Band-limited crinkle: a burst of tiny paper/leaf clicks.</summary>
	static void Crinkle(double[] x, Rng r, int sr, double t0, double t1, double rate, double lo, double hi, double amp)
	{
		for (double t = t0; t < t1; t += r.R(0.2, 1.8) / rate)
			Slap(x, r, sr, t, amp * r.R(0.3, 1.0) * Env((t - t0) / (t1 - t0), 0.2, 0.4), lo, hi, r.R(0.002, 0.006), 0.0003);
	}

	/// <summary>An old note unfolded and lifted: dry paper crinkling, a soft flap.</summary>
	public static double[] PaperRustle(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Crinkle(x, r, sr, 0.0, r.R(0.35, 0.55), 70, 1800, 9000, 0.6);
		Slap(x, r, sr, r.R(0.3, 0.5), 0.35, 300, 2500, 0.04, 0.01);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>Something taken up in the hand: a scuff off the surface, a cloth rustle into a pocket.</summary>
	public static double[] ItemTake(Rng r, int sr)
	{
		var x = Buf(sr, 0.8);
		Slap(x, r, sr, 0.0, 0.5, 400, 4000, 0.02, 0.003);
		Knock(x, r, sr, 0.005, 0.35, r.R(700, 1200), 10, 0.03);
		Crinkle(x, r, sr, 0.12, 0.45, 55, 600, 4000, 0.35);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>The lantern on: the latch clicks, the shutter scrapes up, the flame catches with a soft fwump.</summary>
	public static double[] LanternOn(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		Knock(x, r, sr, 0.0, 0.8, r.R(2600, 3200), 22, 0.012);
		Slap(x, r, sr, 0.0, 0.4, 2000, 8000, 0.004, 0.0003);
		Slap(x, r, sr, 0.06, 0.25, 900, 5000, 0.05, 0.01);
		// the flame taking: a low breath of air
		Slap(x, r, sr, 0.14, 0.45, 60, 500, 0.12, 0.04);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>The lantern off: the shutter drops with a clack, the flame gutters out.</summary>
	public static double[] LanternOff(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		Knock(x, r, sr, 0.0, 0.8, r.R(2000, 2500), 18, 0.015);
		Slap(x, r, sr, 0.0, 0.5, 1500, 7000, 0.006, 0.0003);
		Slap(x, r, sr, 0.05, 0.25, 80, 700, 0.07, 0.02);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>A locked door tried: the handle turned hard against the latch, the leaf rattling in its frame.</summary>
	public static double[] DoorLocked(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		double t = 0;
		for (int i = 0; i < 3; i++)
		{
			Knock(x, r, sr, t, 0.9, r.R(1400, 1900), 14, 0.02);
			Knock(x, r, sr, t + 0.012, 0.6, r.R(180, 240), 6, 0.06);
			Slap(x, r, sr, t, 0.3, 300, 3000, 0.03, 0.002);
			t += r.R(0.12, 0.2);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.18, 0.6, 0.5, 0.6), sr, -3, 100);
	}

	/// <summary>A step on gravel: a crunch of many small stones grinding under the weight.</summary>
	public static double[] StepGravel(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Slap(x, r, sr, 0.0, 0.6, 70, 400, 0.05, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.12, 0.2), 260, 1200, 7000, 0.7);
		Crinkle(x, r, sr, 0.03, 0.22, 90, 600, 3000, 0.4);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step in wet mud (the storm walk): the heel's soft slap, a suck as it sinks, and the wet pull out of it.</summary>
	public static double[] StepMud(Rng r, int sr)
	{
		var x = Buf(sr, 0.55);
		Slap(x, r, sr, 0.0, 0.75, 60, 380, 0.05, 0.006);
		// the suck: a short, falling, wet tone burst
		double f0 = r.R(170, 240);
		AddTone(x, sr, r.R(0.05, 0.08), r.R(0.08, 0.12), u => f0 * (1.0 - 0.45 * u), u => 0.28 * Env(u, 0.15, 0.6), new[] { 1.0, 0.35, 0.12 });
		// the pull out: wet crackle
		Crinkle(x, r, sr, r.R(0.16, 0.22), r.R(0.3, 0.38), 140, 500, 2600, 0.35);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step on wet fallen leaves: a soft pat and a damp, matted rustle (not the dry crackle of autumn).</summary>
	public static double[] StepLeaves(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		Slap(x, r, sr, 0.0, 0.5, 80, 500, 0.04, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.16, 0.24), 320, 900, 4200, 0.55);
		Crinkle(x, r, sr, 0.05, r.R(0.25, 0.32), 120, 400, 1800, 0.3);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step onto a root: a hollow wooden knock under the sole, a scuff of bark.</summary>
	public static double[] StepRoot(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Knock(x, r, sr, 0.0, 0.7, r.R(160, 230), 6, 0.05);
		Knock(x, r, sr, 0.004, 0.35, r.R(480, 640), 9, 0.025);
		Crinkle(x, r, sr, 0.01, r.R(0.08, 0.13), 200, 700, 3000, 0.3);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>Rain gathering on the boughs and falling off in fat drops onto the leaves below: two or three heavy taps,
	/// spaced, with their small splash.</summary>
	public static double[] CanopyDrip(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		double t = 0.0;
		int n = r.I(2, 4);
		for (int k = 0; k < n; k++)
		{
			double a = r.R(0.5, 1.0);
			Slap(x, r, sr, t, a * 0.7, 300, 1600, r.R(0.008, 0.014), 0.0008);
			Knock(x, r, sr, t, a * 0.25, r.R(900, 1500), 12, 0.012);
			Crinkle(x, r, sr, t + 0.004, t + 0.05, 400, 2000, 6000, a * 0.15);
			t += r.R(0.18, 0.45);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.45, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>The lantern's blacklight coming on: a switch click and the tube's electric buzz swelling in (tonal).</summary>
	public static double[] UvHum(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Knock(x, r, sr, 0.0, 0.8, r.R(2400, 2900), 20, 0.01);
		AddTone(x, sr, 0.03, 0.8, u => 120 * (1 + 0.002 * Math.Sin(u * 90)), u => 0.4 * Env(u, 0.3, 0.5), new[] { 0.5, 1.0, 0.7, 0.5, 0.35, 0.25, 0.15 });
		return FinishOneShot(Wet(x, sr, 1.0, 0.08, 0.3, 0.5, 0.3), sr, -3, 150);
	}

	// ------------------------------------------------------------------ the haunting

	/// <summary>Somewhere overhead, or in the next room: old boards taking a slow weight, one step,
	/// a pause, another. Heard through a ceiling: low, dull, no top end.</summary>
	public static double[] BoardsOverhead(Rng r, int sr)
	{
		var x = Buf(sr, 4.5);
		int steps = r.I(2, 4);
		double t = 0.1;
		for (int i = 0; i < steps; i++)
		{
			Knock(x, r, sr, t, 0.8, r.R(70, 110), 5, 0.12);
			Creak(x, r, sr, t + 0.05, r.R(0.35, 0.8), 14, 30, new[] { (r.R(160, 230), 12.0, 1.0), (r.R(310, 420), 12.0, 0.6) }, 1.0);
			t += r.R(0.9, 1.5);
		}
		LowPass(x, sr, 900);
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.4, 0.6, 1.0), sr, -3, 300);
	}

	/// <summary>Air moaning through brickwork high up: a hollow, wavering tone rising and falling (tonal, never hiss).</summary>
	public static double[] WindMoan(Rng r, int sr)
	{
		double dur = r.R(5, 7);
		var x = Buf(sr, dur + 2);
		double f0 = r.R(150, 220);
		var drift = new Smooth(r, dur + 1, 0.7);
		AddTone(x, sr, 0, dur, u => f0 * (0.9 + 0.2 * drift.At(u * dur)) * (1 + 0.012 * Math.Sin(u * dur * 5.3)), u => Env(u, 0.35, 0.45), new[] { 1.0, 0.3, 0.12, 0.05 });
		AddTone(x, sr, 0.4, dur - 0.4, u => f0 * 1.498 * (0.92 + 0.16 * drift.At(u * dur + 3)), u => 0.35 * Env(u, 0.4, 0.4), new[] { 1.0, 0.2 });
		LowPass(x, sr, 1400);
		return FinishOneShot(Wet(x, sr, 0.5, 0.9, 3.5, 0.5, 1.6), sr, -3, 600);
	}

	/// <summary>Water dripping in a long, hard space: one drop, a pause, another, ringing off the walls.</summary>
	public static double[] DripEcho(Rng r, int sr)
	{
		var x = Buf(sr, 3.2);
		double t = 0.05;
		int n = r.I(2, 4);
		for (int i = 0; i < n; i++)
		{
			Bubble(x, sr, t, r.LogR(900, 1800), 0.9, r.R(0.006, 0.012));
			Slap(x, r, sr, t, 0.2, 1500, 7000, 0.003, 0.0003);
			t += r.R(0.5, 1.1);
		}
		return FinishOneShot(Wet(x, sr, 0.6, 0.9, 2.6, 0.4, 1.4), sr, -3, 400);
	}

	/// <summary>A heavy chain shifting somewhere far off in the dark: links grinding and clinking, settling.</summary>
	public static double[] ChainShift(Rng r, int sr)
	{
		var x = Buf(sr, 3.0);
		double t = 0;
		int n = r.I(9, 15);
		for (int i = 0; i < n; i++)
		{
			double a = Env((double)i / n, 0.2, 0.5);
			Knock(x, r, sr, t, 0.7 * a, r.R(1800, 3200), 30, 0.05);
			Knock(x, r, sr, t + 0.004, 0.4 * a, r.R(600, 900), 18, 0.04);
			t += r.R(0.05, 0.16);
		}
		LowPass(x, sr, 3500);
		return FinishOneShot(Wet(x, sr, 0.5, 0.9, 2.8, 0.5, 1.4), sr, -3, 400);
	}
}
