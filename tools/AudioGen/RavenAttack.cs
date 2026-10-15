namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The black omen bird coming at you (2026-10-15, the owner: "the blackbird should make more of a scary loud raven noise
/// when it flies at you"; the old far-off scream is kept in assets/audio/storage for Alex).
///
/// (2026-10-15b, the owner: "not loud, scary or sudden enough, should trigger as soon as the picture snaps") It fires on
/// the shutter now, so the hit is the very first thing, no wingbeats before it: within ~10 ms
/// <list type="bullet">
/// <item>a sub thump under it (the jolt) and the beak's clack;</item>
/// <item>the croak: a raven's harsh, guttural "KRRAAH", deeper and rougher than a crow's, screamed open-throated (a
/// rising-then-falling glottal source full of harmonics, a quickening knocking rattle through it, driven into rasp, a
/// sub-octave growl under it);</item>
/// <item>a high, thin, breaking screech over it, and torn air (a rasp) the whole length of the cry;</item>
/// </list>
/// then the rush of it tearing past and its wings going away under and after, and a shorter second cry. Only a little
/// room (it's right there), and squashed hard (driven, soft-clipped) so it's dense and loud the whole way through.
/// </summary>
public static class RavenAttack
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Make(Rng r, int sr)
	{
		double total = r.R(1.9, 2.4);
		var x = Buf(sr, total);
		int n = x.Length;
		const double hit = 0.004;

		// --- the hit -------------------------------------------------------------------------------------------------------
		// a sub thump under it (the jolt)
		{
			double f0 = r.R(48, 62), ph = 0;
			int s0 = (int)(hit * sr);
			for (int i = 0; i < (int)(0.32 * sr) && s0 + i < n; i++)
			{
				double t = (double)i / sr;
				double f = f0 * (1 + 0.8 * Math.Exp(-t / 0.03));
				ph += 2 * Math.PI * f / sr;
				x[s0 + i] += Math.Sin(ph) * Perc(t, 0.002, 0.09) * 1.1;
			}
		}
		// the beak's clack, right on it
		{
			var cb = Biquad.Bp(sr, r.R(2400, 3600), 4);
			for (int i = 0; i < sr / 40 && i < n; i++) x[i] += cb.P(r.W()) * Perc((double)i / sr, 0.0002, 0.005) * 2.2;
		}
		double cry = r.R(0.6, 0.85);
		Croak(x, r, sr, hit, cry, r.R(200, 260), 1.0, 0.012);
		// the screech over it: higher, thinner, shrill, breaking
		Screech(x, r, sr, hit + r.R(0.0, 0.02), r.R(0.38, 0.55), r.R(620, 900), 0.75);
		// the rasp: torn air through the throat, the whole length of the cry
		{
			var bp = Biquad.Bp(sr, r.R(1400, 2200), 0.8);
			int len = (int)(cry * sr);
			for (int i = 0; i < len && i < n; i++)
			{
				double u = (double)i / len;
				x[i] += Math.Tanh(bp.P(r.W()) * 6) * 0.22 * Env(u, 0.01, 0.4);
			}
		}

		// --- the rush of it past and its wings going away, under it and after -----------------------------------------------
		double passAt = r.R(0.12, 0.2);
		{
			var sweep = new Biquad(sr);
			for (int i = 0; i < (int)((passAt + 0.4) * sr) && i < n; i++)
			{
				double t = (double)i / sr;
				double env = t < passAt ? Math.Pow(t / passAt, 0.7) : Math.Exp(-(t - passAt) / 0.08);
				double f = t < passAt ? 1600 + 900 * t / passAt : 2500 - 1700 * Math.Min(1, (t - passAt) / 0.25);
				sweep.SetBp(f, 1.1);
				x[i] += sweep.P(r.W()) * env * 0.7;
			}
		}
		double bt = passAt + 0.05;
		int beats = r.I(2, 4);
		for (int b = 0; b < beats; b++)
		{
			var bp = Biquad.Bp(sr, r.R(380, 650), 0.9);
			var lp = Biquad.Lp(sr, r.R(1800, 3000));
			int s0 = (int)(bt * sr), len = (int)(r.R(0.07, 0.1) * sr);
			double a = 0.9 - 0.2 * b;   // going away
			for (int i = 0; i < len && s0 + i < n; i++) x[s0 + i] += lp.P(bp.P(r.W())) * Perc((double)i / sr, 0.005, 0.03) * a * 1.6;
			bt += r.R(0.1, 0.14) * (1 + 0.15 * b);
		}

		// --- a second, shorter cry ------------------------------------------------------------------------------------------
		if (r.Chance(0.7)) Croak(x, r, sr, cry + r.R(0.2, 0.35), r.R(0.3, 0.45), r.R(170, 220), r.R(0.55, 0.75), 0.04);

		var o = Space(x, sr, 0.16);
		// squashed: driven, then a soft clip, so it's loud the whole way through
		double pk = Peak(o);
		for (int i = 0; i < o.Length; i++) o[i] = Math.Tanh(o[i] / pk * 5.5) / Math.Tanh(5.5);
		return FinishOneShot(o, sr, -3, 160, minSec: 1.6);
	}

	/// <summary>One harsh raven cry: a glottal source rising then sliding down, a knocking rattle through it, rasp, a growl
	/// under it, through an open throat. <paramref name="attack"/> is the fraction of it taken to come in.</summary>
	static void Croak(double[] x, Rng r, int sr, double at, double dur, double f0, double gain, double attack)
	{
		int s0 = (int)(at * sr), len = (int)(dur * sr);
		var jit = new Smooth(r.Fork(), dur + 1, 0.012);
		double rattle0 = r.R(14, 22), rattle1 = r.R(28, 42);
		double ph = 0, sub = 0, rph = 0;
		// an open "aah" throat, pitched for a big bird (and a nasal edge)
		var f1 = Biquad.Bp(sr, r.R(850, 1050), 3.0);
		var f2 = Biquad.Bp(sr, r.R(1500, 1800), 4.0);
		var f3 = Biquad.Bp(sr, r.R(2500, 2900), 5.0);
		var body = Biquad.Lp(sr, 600);
		var hiss = Biquad.Bp(sr, r.R(2000, 3000), 1.0);
		for (int i = 0; i < len && s0 + i < x.Length; i++)
		{
			double u = (double)i / len, t = (double)i / sr;
			// up into the scream, then a long fall as it breaks
			double contour = u < 0.25 ? 1 + 0.18 * Math.Sin(Math.PI * 0.5 * u / 0.25) : 1.18 - 0.42 * Math.Pow((u - 0.25) / 0.75, 1.3);
			double f = f0 * contour * (1 + 0.05 * (jit.At(t) - 0.5));
			ph += 2 * Math.PI * f / sr;
			sub += 2 * Math.PI * f * 0.5 / sr;
			double v = 0;
			for (int h = 1; h <= 24; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h + h * 0.3) / Math.Pow(h, 0.8);
			// the knocking rattle: slow at the start, quickening; deep gaps in the voice
			rph += 2 * Math.PI * (rattle0 + (rattle1 - rattle0) * u) / sr;
			double rat = 0.35 + 0.65 * Math.Pow(0.5 + 0.5 * Math.Sin(rph), 0.6);
			double src = v * rat + r.W() * 0.35 * rat;
			double throat = f1.P(src) * 1.0 + f2.P(src) * 0.7 + f3.P(src) * 0.35 + hiss.P(r.W()) * 0.12;
			double growl = body.P(Math.Sin(sub) + Math.Sin(sub * 2) * 0.5) * 0.55 * rat;
			double env = Env(u, attack, 0.35);
			// driven into rasp, not into fuzz
			x[s0 + i] += Math.Tanh((throat * 2.4 + growl) * 1.8) * env * gain;
		}
	}

	/// <summary>The shrill top of the cry: a high, thin, breaking screech with a fast flutter.</summary>
	static void Screech(double[] x, Rng r, int sr, double at, double dur, double f0, double gain)
	{
		int s0 = (int)(at * sr), len = (int)(dur * sr);
		var jit = new Smooth(r.Fork(), dur + 1, 0.008);
		var f1 = Biquad.Bp(sr, r.R(1700, 2200), 3.0);
		var f2 = Biquad.Bp(sr, r.R(2900, 3500), 4.0);
		double ph = 0, fl = 0, flRate = r.R(28, 40);
		for (int i = 0; i < len && s0 + i < x.Length; i++)
		{
			double u = (double)i / len, t = (double)i / sr;
			double f = f0 * (1 + 0.25 * Math.Sin(Math.PI * Math.Min(1, u * 1.6))) * (1 - 0.3 * u) * (1 + 0.08 * (jit.At(t) - 0.5));
			ph += 2 * Math.PI * f / sr;
			fl += 2 * Math.PI * flRate / sr;
			double v = 0;
			for (int h = 1; h <= 12; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / Math.Pow(h, 0.7);
			v *= 0.6 + 0.4 * Math.Sin(fl);
			double s = f1.P(v) + f2.P(v) * 0.7 + r.W() * 0.08;
			x[s0 + i] += Math.Tanh(s * 3.0) * Env(u, 0.02, 0.5) * gain;
		}
	}

	static double[] Space(double[] dry, int sr, double mix)
	{
		var rv = new Reverb(sr, 0.45, 0.6);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] + rv.P(dry[i]) * mix;
		return o;
	}
}
