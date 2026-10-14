namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The player's footsteps, redone (2026-10-12, the owner: "the footsteps are very bad and sound squeaky like spongebob").
/// What made them cartoonish: tones. The dirt step's heel was a sine whose pitch fell as it struck (a "boing"), the mud's
/// suck a falling tone, the snow's pack a narrow ringing band, and every texture sat bright and light over a heel with no
/// weight. Now every step is built on one thing: a boot coming down, a grown man's weight on it (<see cref="Boot"/>), all
/// filtered noise, never a tone: the heel's thud low in the chest, the sole's slap on top, the toe rolling down after.
/// The ground's own sound rides on that, darker than before (nothing bright above ~5 kHz, nothing narrow enough to ring).
/// </summary>
public static class Steps
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A boot coming down at <paramref name="t"/>: the heel's weight (a low noise thud, no pitch), the sole's
	/// slap (a short mid band), and the toe rolled down after it, softer. <paramref name="soft"/> 0 hard ground .. 1 deep and
	/// giving (the slap muffled, the thud slower).</summary>
	static void Boot(double[] x, Rng r, int sr, double t, double weight, double soft)
	{
		// (2026-10-13, the "bouncy / circus" sweep) The boot was two low thuds, heel then toe, each mostly noise under
		// 160 Hz: 70-90 % of every step's energy sat under 200 Hz (tools/AudioAudit), and a low thump, a beat, a softer
		// low thump, its pitch nudged up or down each step, is a ball bouncing. Now one heel strike that's mostly the
		// ground's body (150 Hz-1.6 kHz), a little weight under it, the sole's contact on top, and the roll onto the toe a
		// soft scuff along the ground, never a second thud.
		// the weight: a little low body, short (the softer the ground, the slower and duller)
		Slap(x, r, sr, t, weight * 0.55, 50, 150, 0.014 + 0.016 * soft, 0.002 + 0.004 * soft);
		// the heel into the ground: the body of the step
		Slap(x, r, sr, t + 0.001, weight * 0.95, 160, 1700 - 700 * soft, 0.009 + 0.009 * soft, 0.0008 + 0.0025 * soft);
		// the sole's leather meeting it: a dry tick, very short
		Slap(x, r, sr, t + 0.002, weight * 0.4 * (1 - 0.65 * soft), 1200, 4800, 0.0035, 0.0003);
		// the roll onto the toe: a scuff of the sole along the ground (a swell of mid noise, no impact in it)
		double roll = r.R(0.045, 0.085);
		Slap(x, r, sr, t + roll * 0.5, weight * r.R(0.22, 0.32), 350, 2600 - 900 * soft, roll * 0.45, roll * 0.35);
	}

	/// <summary>A scatter of tiny dark grains (the ground breaking up under the sole): many soft clicks, band-limited and
	/// never resonant (wide bands only: a narrow one rings, and ringing is a squeak).</summary>
	static void Grit(double[] x, Rng r, int sr, double t0, double t1, double rate, double lo, double hi, double amp)
	{
		for (double t = t0; t < t1; t += r.R(0.3, 1.7) / rate)
		{
			double u = (t - t0) / (t1 - t0);
			Slap(x, r, sr, t, amp * r.R(0.25, 1.0) * Env(u, 0.12, 0.55), lo, hi, r.R(0.0015, 0.005), 0.0004);
		}
	}

	static double[] Done(double[] x, int sr) => FinishOneShot(Wet(x, sr, 1.0, 0.05, 0.22, 0.6, 0.3), sr, -3, 50);

	/// <summary>The packed earth of the trail: the boot, and a soft dry crunch of grit under it.</summary>
	public static double[] Dirt(Rng r, int sr)
	{
		var x = Buf(sr, 0.42);
		Boot(x, r, sr, 0.0, 1.0, 0.25);
		Grit(x, r, sr, 0.004, r.R(0.12, 0.18), 160, 600, 3800, 0.32);
		HighPass(x, sr, 40);
		return Done(x, sr);
	}

	/// <summary>Gravel: the boot, and the stones grinding round under it (a denser, coarser grind).</summary>
	public static double[] Gravel(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Boot(x, r, sr, 0.0, 0.95, 0.2);
		Grit(x, r, sr, 0.0, r.R(0.14, 0.2), 300, 700, 4500, 0.5);
		Grit(x, r, sr, 0.04, 0.24, 90, 400, 2200, 0.3);
		return Done(x, sr);
	}

	/// <summary>Wet mud: the boot sinking (soft, slow), a thick wet squelch as it takes the weight (filtered noise, a slow
	/// swell: the old one's falling tone was the cartoon), the sucking pull of the sole out of it.</summary>
	public static double[] Mud(Rng r, int sr)
	{
		var x = Buf(sr, 0.55);
		Boot(x, r, sr, 0.0, 0.95, 0.8);
		Slap(x, r, sr, r.R(0.03, 0.05), 0.45, 120, 650, 0.05, 0.02);
		Grit(x, r, sr, r.R(0.17, 0.22), r.R(0.3, 0.36), 110, 300, 1600, 0.22);
		return Done(x, sr);
	}

	/// <summary>Wet leaves: the boot, a damp, matted shuffle (low, close).</summary>
	public static double[] Leaves(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Boot(x, r, sr, 0.0, 0.95, 0.45);
		Grit(x, r, sr, 0.0, r.R(0.14, 0.2), 200, 500, 2600, 0.3);
		return Done(x, sr);
	}

	/// <summary>Dry leaves: the boot, the leaves' papery crackle round it (soft, short: the old one hissed).</summary>
	public static double[] DryLeaves(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Boot(x, r, sr, 0.0, 0.95, 0.35);
		Grit(x, r, sr, 0.0, r.R(0.16, 0.24), 260, 800, 4800, 0.34);
		Grit(x, r, sr, 0.05, r.R(0.24, 0.3), 90, 500, 2500, 0.16);
		return Done(x, sr);
	}

	/// <summary>A small dry stick snapped under the boot: a short dull crack (wideband, not a ringing knock).</summary>
	public static double[] Twig(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Boot(x, r, sr, 0.0, 0.95, 0.35);
		Grit(x, r, sr, 0.0, r.R(0.12, 0.18), 200, 700, 4000, 0.28);
		double t = r.R(0.025, 0.06);
		Slap(x, r, sr, t, 0.55, 500, 3500, 0.006, 0.0004);
		Slap(x, r, sr, t + r.R(0.008, 0.016), 0.3, 400, 2600, 0.008, 0.0004);
		return Done(x, sr);
	}

	/// <summary>A root under the boot: the step lands on wood (a hollow dull knock under the heel: wide, low).</summary>
	public static double[] Root(Rng r, int sr)
	{
		var x = Buf(sr, 0.4);
		Boot(x, r, sr, 0.0, 0.9, 0.1);
		Slap(x, r, sr, 0.0, 0.5, 150, 500, 0.03, 0.002);
		Grit(x, r, sr, 0.01, 0.1, 120, 500, 2400, 0.18);
		return Done(x, sr);
	}

	/// <summary>Fresh snow: the boot pressing in (soft, slow), the snow packing with a dull, low crunch (the old bright
	/// ringing band, the "squeak", gone).</summary>
	public static double[] Snow(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		Boot(x, r, sr, 0.0, 0.85, 0.85);
		Grit(x, r, sr, r.R(0.015, 0.03), r.R(0.13, 0.19), 240, 400, 2200, 0.3);
		return FinishOneShot(Wet(x, sr, 1.0, 0.08, 0.35, 0.7, 0.5), sr, -3, 60);
	}
}
