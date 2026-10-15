namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The black omen bird coming at you (2026-10-15, the owner: "the blackbird should make more of a scary loud raven noise
/// when it flies at you"; the old far-off scream is kept in assets/audio/storage for Alex). Close and loud, a jump moment:
/// <list type="bullet">
/// <item>its wings rushing in: a few heavy, dry wingbeats coming faster, and the air of it passing (a whoosh that swells
/// and tears off);</item>
/// <item>a beak clack as it opens;</item>
/// <item>the cry: a raven's harsh, guttural "KRRAAH", deeper and rougher than a crow's (the raven's menace is its low,
/// rasping croak), screamed open-throated: a rising-then-falling glottal source full of harmonics, a slow-to-fast
/// knocking rattle through it (the raven's knock and rattle calls), driven a little into rasp, a sub-octave growl under
/// it for weight, through an open "aah" throat; sometimes a shorter second croak after it;</item>
/// <item>only a little room: it's right there.</item>
/// </list>
/// </summary>
public static class RavenAttack
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Make(Rng r, int sr)
	{
		double total = r.R(1.9, 2.5);
		var x = Buf(sr, total);
		int n = x.Length;

		// --- the wings rushing in ---------------------------------------------------------------------------------------
		int beats = r.I(3, 6);
		double bt = 0, gap = r.R(0.11, 0.15);
		for (int b = 0; b < beats; b++)
		{
			double a = 0.35 + 0.65 * (b + 1.0) / beats;   // nearer each beat
			var bp = Biquad.Bp(sr, r.R(380, 700), 0.9);
			var lp = Biquad.Lp(sr, r.R(1800, 3200));
			int s0 = (int)(bt * sr), len = (int)(r.R(0.07, 0.11) * sr);
			for (int i = 0; i < len && s0 + i < n; i++)
			{
				double t = (double)i / sr;
				x[s0 + i] += lp.P(bp.P(r.W())) * Perc(t, 0.006, 0.03) * a * 2.2;
			}
			bt += gap * r.R(0.85, 1.1);
			gap *= 0.85;   // coming faster
		}
		double passAt = bt + r.R(0.0, 0.05);
		// the air of it passing: a band of noise that swells up to the pass and tears off, its pitch dropping (a doppler)
		{
			var sweep = new Biquad(sr);
			double w0 = Math.Max(0, passAt - 0.45), w1 = passAt + 0.35;
			for (int i = (int)(w0 * sr); i < (int)(w1 * sr) && i < n; i++)
			{
				double t = (double)i / sr;
				double u = (t - w0) / (passAt - w0);
				double env = t < passAt ? Math.Pow(Math.Max(0, u), 2.2) : Math.Exp(-(t - passAt) / 0.07);
				double f = t < passAt ? 900 + 1400 * u : 2300 - 1500 * Math.Min(1, (t - passAt) / 0.2);
				sweep.SetBp(f, 1.2);
				x[i] += sweep.P(r.W()) * env * 0.9;
			}
		}

		// --- the cry -------------------------------------------------------------------------------------------------------
		double cryAt = Math.Max(0.12, passAt - r.R(0.05, 0.15));
		// a beak clack (or two) as it opens
		int clacks = r.I(1, 3);
		for (int c = 0; c < clacks; c++)
		{
			var cb = Biquad.Bp(sr, r.R(2200, 3600), 5);
			int c0 = (int)((cryAt - 0.06 + c * r.R(0.03, 0.05)) * sr);
			for (int i = 0; i < sr / 40 && c0 + i < n; i++) if (c0 + i >= 0) x[c0 + i] += cb.P(r.W()) * Perc((double)i / sr, 0.0003, 0.004) * r.R(1.2, 2.0);
		}
		Croak(x, r, sr, cryAt, r.R(0.55, 0.9), r.R(210, 290), 1.0);
		if (r.Chance(0.55)) Croak(x, r, sr, cryAt + r.R(0.85, 1.05), r.R(0.25, 0.4), r.R(170, 230), r.R(0.45, 0.65));

		// only a little room: it's right there
		var o = Space(x, sr, 0.22);
		return FinishOneShot(o, sr, -3, 160);
	}

	/// <summary>One harsh raven cry: a glottal source rising then sliding down, a knocking rattle through it, rasp, a growl
	/// under it, through an open throat.</summary>
	static void Croak(double[] x, Rng r, int sr, double at, double dur, double f0, double gain)
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
			double env = Env(u, 0.04, 0.35);
			// driven into rasp, not into fuzz
			x[s0 + i] += Math.Tanh((throat * 2.4 + growl) * 1.8) * env * gain;
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
