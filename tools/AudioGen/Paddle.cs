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

	/// <summary>The blade going in.</summary>
	public static double[] Catch(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		// the entry: a short tearing splash, soft-edged (the blade goes in edge first)
		Slap(x, r, sr, 0.0, 0.35, 700, 5200, r.R(0.018, 0.03), 0.006);
		// the plunge: the air dragged down with the blade, a deep bloop and a couple of gulps
		Bubble(x, sr, 0.012, r.R(170, 240), 0.9, r.R(0.05, 0.08));
		Bubble(x, sr, 0.03 + r.R(0, 0.02), r.R(300, 420), 0.5, r.R(0.03, 0.05));
		Bubble(x, sr, 0.06 + r.R(0, 0.03), r.R(250, 330), 0.35, r.R(0.03, 0.05));
		for (int i = 0; i < 6; i++) Bubble(x, sr, 0.02 + r.R(0, 0.12), r.LogR(700, 1800), r.R(0.08, 0.2), r.R(0.006, 0.014));
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.5, 0.55, 0.5), sr, -3, 80);
	}

	/// <summary>The blade drawn through the water (about 0.6 s at an easy pace; the game pitches it to the stroke).</summary>
	public static double[] Pull(Rng r, int sr)
	{
		double dur = r.R(0.62, 0.75);
		var x = Buf(sr, dur + 0.6);
		// the rush: low and heavy, swelling as the blade loads, easing as it comes through
		Func<double, double> env = u => Math.Sin(Math.PI * Math.Pow(u, 0.7)) * (0.7 + 0.3 * Math.Sin(u * 9.0));
		Rush(x, r, sr, 0.0, dur, 90, 900, 0.9, env);
		Rush(x, r, sr, 0.05, dur * 0.8, 600, 2600, 0.22, u => Math.Sin(Math.PI * u));
		// vortices shed off the blade's edges: gurgles, lower as it digs in
		int n = r.I(14, 22);
		for (int i = 0; i < n; i++)
		{
			double t = r.R(0.05, dur * 0.95);
			double f = r.LogR(160, 700) * (1 - 0.25 * (t / dur));
			Bubble(x, sr, t, f, r.R(0.12, 0.35), r.R(0.02, 0.05));
		}
		// the hull answering as the boat surges: a soft low lap
		Slap(x, r, sr, dur * 0.7, 0.18, 60, 380, 0.12, 0.03);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.5, 0.55, 0.5), sr, -3, 150);
	}

	/// <summary>The blade lifting out.</summary>
	public static double[] Release(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		// the blade breaking the surface: a light slap and a sheet of water pouring off it
		Slap(x, r, sr, 0.0, 0.45, 900, 6500, r.R(0.01, 0.018), 0.004);
		Rush(x, r, sr, 0.005, r.R(0.18, 0.26), 1200, 6000, 0.35, u => Math.Pow(1 - u, 1.5));
		// the water off it hitting the lake: bright little plops
		for (int i = 0; i < 9; i++) Bubble(x, sr, 0.05 + r.R(0, 0.2), r.LogR(900, 2600), r.R(0.1, 0.3), r.R(0.005, 0.012));
		// drips pattering back onto the lake, slowing
		double t = 0.25;
		for (int i = 0; i < 16 && t < 1.35; i++)
		{
			Bubble(x, sr, t, r.LogR(1500, 3600), r.R(0.05, 0.16) * (1.3 - t * 0.6), r.R(0.004, 0.009));
			t += r.R(0.03, 0.12) * (1 + t);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.5, 0.55, 0.5), sr, -3, 150);
	}
}
