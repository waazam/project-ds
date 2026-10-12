namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The rowboat's oars (the owner's note: they should sound like someone paddling through water). A
/// real stroke is three sounds, and the game plays each one at its moment, synced to the blade:
/// <list type="bullet">
/// <item><b>catch</b>: the blade going in, edge first. A soft plunge: a deep "bloop" from the air
/// dragged down with it, a small tearing splash, a couple of low gulps.</item>
/// <item><b>pull</b>: the blade drawn through the water. A heavy swirl of water pushing past it: a
/// low, rushing "shhwoorl" swelling and fading, full of gurgling vortices shed off the blade's edges.
/// It is band-limited turbulence shaped by the stroke's envelope (a one-shot, never a bed).</item>
/// <item><b>release</b>: the blade lifting out. A sheet of water pouring off it, a light slap as it
/// breaks the surface, then drips pattering back onto the lake.</item>
/// </list>
/// </summary>
public static class Paddle
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>Filtered, enveloped turbulence over [t0, t0+dur): the rush of water past something.</summary>
	static void Rush(double[] x, Rng r, int sr, double t0, double dur, double lo, double hi, double amp, Func<double, double> env)
	{
		var hp = Biquad.Hp(sr, lo); var lp = Biquad.Lp(sr, hi); var lp2 = Biquad.Lp(sr, hi * 0.7);
		// a slow wobble of the tone, as the flow round the blade changes
		var wob = new Smooth(r, dur + 1, 0.08);
		int s0 = (int)(t0 * sr), n = (int)(dur * sr);
		for (int i = 0; i < n && s0 + i < x.Length; i++)
		{
			double u = (double)i / n;
			double w = r.W();
			double v = lp.P(hp.P(w));
			double dark = lp2.P(v);
			double mix = 0.35 + 0.5 * wob.At((double)i / sr);
			x[s0 + i] += (v * (1 - mix) + dark * mix) * amp * env(u);
		}
	}

	// (2026-10-12, the owner: "the Rowboat in the lake section sounds horrible while paddling and sounds nothing like paddles
	// running through water") The old strokes were mostly pitched bubbles (sine "bloops" and "plinks"), which is a cartoon of
	// water. Water moved by a blade is noise: a dull sploosh going in, a broad swirling wash while it's drawn through, a
	// spill and a few soft drips coming out. Now that's all it is; a single low gulp is the only pitched thing left, and quiet.

	/// <summary>Little noise drops (no pitch): water falling back off the blade onto the lake.</summary>
	static void Drips(double[] x, Rng r, int sr, double t0, double t1, int n, double amp)
	{
		double t = t0;
		for (int i = 0; i < n && t < t1; i++)
		{
			Slap(x, r, sr, t, amp * r.R(0.4, 1.0) * (1.2 - (t - t0) / (t1 - t0)), r.R(700, 1200), r.R(2600, 4200), r.R(0.004, 0.009), 0.0008);
			t += r.R(0.04, 0.14) * (1 + (t - t0) * 2);
		}
	}

	/// <summary>The blade going in.</summary>
	public static double[] Catch(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		// the sploosh: the blade's face slapping into the water, a body of low-mid noise
		Slap(x, r, sr, 0.0, 0.9, 160, 2200, r.R(0.035, 0.05), 0.004);
		// the thump under it (the water's mass taking the blade)
		Slap(x, r, sr, 0.004, 0.6, 50, 260, r.R(0.05, 0.07), 0.006);
		// a little spray off its edge
		Slap(x, r, sr, 0.002, 0.18, 1800, 6000, 0.02, 0.003);
		// one soft gulp of air dragged under, barely there
		Bubble(x, sr, 0.02, r.R(150, 210), 0.12, r.R(0.04, 0.06));
		return FinishOneShot(Wet(x, sr, 1.0, 0.07, 0.35, 0.6, 0.4), sr, -3, 80);
	}

	/// <summary>The blade drawn through the water (about 0.6 s at an easy pace; the game pitches it to the stroke).</summary>
	public static double[] Pull(Rng r, int sr)
	{
		double dur = r.R(0.62, 0.75);
		var x = Buf(sr, dur + 0.4);
		// the wash: a broad, heavy swirl swelling as the blade loads and easing as it comes through, its tone churning
		Func<double, double> env = u => Math.Sin(Math.PI * Math.Pow(u, 0.75));
		Rush(x, r, sr, 0.0, dur, 110, 1300, 1.0, env);
		Rush(x, r, sr, 0.04, dur * 0.85, 500, 2800, 0.32, u => Math.Sin(Math.PI * u) * (0.6 + 0.4 * Math.Sin(u * 13.0)));
		// the churn off the blade's edges: short soft bursts of noise (eddies), not bubbles
		int n = r.I(8, 13);
		for (int i = 0; i < n; i++)
			Slap(x, r, sr, r.R(0.06, dur * 0.9), r.R(0.12, 0.28), r.R(200, 400), r.R(900, 1800), r.R(0.02, 0.045), 0.006);
		// the hull answering as the boat surges: a soft low lap
		Slap(x, r, sr, dur * 0.7, 0.2, 60, 380, 0.12, 0.03);
		return FinishOneShot(Wet(x, sr, 1.0, 0.07, 0.35, 0.6, 0.4), sr, -3, 120);
	}

	/// <summary>The blade lifting out.</summary>
	public static double[] Release(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		// the blade breaking the surface, the sheet of water spilling off it
		Slap(x, r, sr, 0.0, 0.35, 500, 3800, r.R(0.012, 0.02), 0.003);
		Rush(x, r, sr, 0.005, r.R(0.2, 0.3), 700, 4200, 0.45, u => Math.Pow(1 - u, 1.4));
		// the drips back onto the lake, slowing
		Drips(x, r, sr, 0.12, 1.0, r.I(6, 10), 0.18);
		return FinishOneShot(Wet(x, sr, 1.0, 0.07, 0.35, 0.6, 0.4), sr, -3, 120);
	}
}
