namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The fall through the ground at the end of Act 6's stair loop (2026-10-15): the sound of falling through a lit void,
/// eight seconds of it, cut off by the blackout. A deep rush rising and quickening (noise through a band that climbs
/// from ~300 Hz to ~2.4 kHz, louder as it goes), a low hum stretched and sinking under it (the stairs' D drawn out and
/// sliding down), and streaks going by the ear now and then (short filtered bursts sweeping past, left and right
/// alternating in level). Three takes, each its own timing.
/// </summary>
public static class FallVoidSound
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>The free fall from high over the clearing (about 5 s): wind roaring past the ears, rising and buffeting
	/// (noise through a climbing low band, a flutter in it), a whistle edge coming in near the end.</summary>
	public static double[] Wind(Rng r, int sr)
	{
		double dur = 5.6;
		var x = Buf(sr, dur);
		var b1 = new Biquad(sr); var b2 = new Biquad(sr); var lo = Biquad.Lp(sr, 220);
		var pink = new Pink();
		var gust = new Smooth(r.Fork(), dur + 1, 0.18);
		double ph = r.R(0, TwoPi);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double fc = 260 * Math.Pow(4.5, u);
			if ((i & 31) == 0) { b1.SetBp(fc, 0.7); b2.SetBp(fc * 3.2, 2.5); }
			double w = pink.P(r.W());
			ph += TwoPi * (7 + 9 * u) / sr;   // the buffeting
			double flutter = 0.75 + 0.25 * Math.Sin(ph) * (0.6 + 0.4 * gust.At(t));
			double a = Math.Pow(u, 0.7) * 0.9 + 0.1;
			x[i] = (b1.P(w) * 1.2 + b2.P(w) * 0.35 * u * u + lo.P(r.W()) * 1.4) * a * flutter;
		}
		HighPass(x, sr, 30);
		NormRms(x, -15, -3);
		return FinishOneShot(x, sr, -3, 25, minSec: dur - 0.05);
	}

	public static double[] Rush(Rng r, int sr)
	{
		double dur = 14.0;   // (2026-10-15: the void is 10-13 s now, darker: a lower band, slower to rise)
		var x = Buf(sr, dur);
		var band = new Biquad(sr); var band2 = new Biquad(sr);
		var low = Biquad.Lp(sr, 140);
		var pink = new Pink();
		var wob = new Smooth(r.Fork(), dur + 1, 0.35);
		double ph = 0, ph2 = 0;
		double f0 = r.R(70, 76);   // the stairs' D, near enough
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double fc = 200 * Math.Pow(5.0, Math.Pow(u, 1.5)) * (0.9 + 0.2 * wob.At(t));
			if ((i & 31) == 0) { band.SetBp(fc, 0.9); band2.SetBp(fc * 1.6, 1.2); }
			double w = pink.P(r.W());
			double rush = band.P(w) * 0.8 + band2.P(w) * 0.4;
			double rumble = low.P(r.W()) * 2.2;
			// the hum, stretched: sliding down a fifth over the fall, a slow beat in it
			double f = f0 * Math.Pow(2, -0.6 * u);
			ph += TwoPi * f / sr; ph2 += TwoPi * f * 1.004 / sr;
			double hum = (Math.Sin(ph) + 0.5 * Math.Sin(2 * ph2) + 0.25 * Math.Sin(3 * ph)) * 0.18;
			double rise = 0.25 + 0.75 * Math.Pow(u, 0.8);
			x[i] = rush * rise * 1.6 + rumble * (0.4 + 0.6 * u) + hum * (0.6 + 0.4 * u);
		}
		// streaks going by
		double at = r.R(0.6, 1.2);
		while (at < dur - 0.4)
		{
			double len = r.R(0.18, 0.4), a = r.R(0.35, 0.8) * (0.5 + 0.5 * at / dur);
			var bp = new Biquad(sr);
			int s0 = (int)(at * sr), n = (int)(len * sr);
			for (int i = 0; i < n && s0 + i < x.Length; i++)
			{
				double u = (double)i / n;
				if ((i & 15) == 0) bp.SetBp(4200 * Math.Pow(0.18, u), 1.6);   // sweeping down as it passes
				x[s0 + i] += bp.P(r.W()) * Env(u, 0.35, 0.6) * a;
			}
			at += r.R(0.25, 0.9) * (1.2 - 0.6 * at / dur);
		}
		HighPass(x, sr, 28);
		NormRms(x, -16, -3);
		// no fade-out tail of its own: the blackout cuts it
		return FinishOneShot(x, sr, -3, 30, minSec: dur - 0.05);
	}
}
