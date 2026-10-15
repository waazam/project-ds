namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Act 1's far-off rifle shot, rebuilt 2026-10-15 (the owner: "do some research on hunting game gunshot noises in the
/// distance and make ours better"). The old take was a 4 kHz crack and a white-noise boom driven into tanh: loud, but a
/// near shot made quiet, and the same one every time. What a hunting rifle sounds like from half a mile off in the woods:
/// <list type="bullet">
/// <item>No crack. The bullet's supersonic shock only reaches someone it passes near (downrange); off to the side you hear
/// the muzzle blast alone.</item>
/// <item>The blast is a pressure spike of a millisecond or two (a Friedlander pulse: a sharp rise, a longer suck back),
/// but after hundreds of metres of air and ground its highs are gone (air absorbs more the higher the frequency) and it
/// arrives smeared by the ground's reflection and the trees' scatter: a dull, rounded "pow" with a low thump in it.</item>
/// <item>Most of what you hear is the landscape answering: a few soft, smeared reflections off the ridges and tree lines
/// (never crisp slap-backs, which read as more shots), melting into a long rolling tail that darkens as it dies, two to
/// four seconds.</item>
/// </list>
/// Each take is its own distance (500-1200 m), its own pulse, ridges and tail, so no two are alike.
/// </summary>
public static class Gunshot
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] RifleDistant(Rng r, int sr)
	{
		double dist = r.R(500, 1200), far = (dist - 500) / 700;   // 0 nearer .. 1 farthest
		var x = Buf(sr, 6.5);

		// --- the blast as it arrives -------------------------------------------------------------------------------
		// a Friedlander pulse (positive phase T, then the negative suck), a couple of milliseconds
		double T = r.R(0.0012, 0.0022), b = r.R(1.2, 1.8);
		var shot = new double[(int)(0.35 * sr)];
		for (int i = 0; i < shot.Length; i++)
		{
			double t = (double)i / sr;
			if (t > 8 * T) break;
			shot[i] = (1 - t / T) * Math.Exp(-b * t / T);
		}
		// the ground's reflection: a little later, partly cancelling (a softer, fuller front)
		int gd = (int)(r.R(0.0006, 0.0018) * sr);
		var withGround = (double[])shot.Clone();
		for (int i = gd; i < shot.Length; i++) withGround[i] += shot[i - gd] * r.R(0.55, 0.7);
		// the trees' scatter: the front smeared over a few tens of milliseconds
		int scatterLen = (int)(r.R(0.03, 0.06) * sr);
		var scatter = new double[scatterLen];
		for (int i = 0; i < scatterLen; i++) scatter[i] = r.W() * Math.Exp(-i / (scatterLen * 0.3));
		scatter[0] += 1.2;   // (the direct path still leads)
		var front = Convolve(withGround, scatter, shot.Length);
		// the air: the highs taken, more the farther
		double air = 3400 - 1600 * far;
		LowPass(front, sr, air);
		LowPass(front, sr, air * 1.8);
		// the low thump: distance keeps the lows, so lean on them a little
		var thump = (double[])front.Clone();
		LowPass(thump, sr, 140);
		LowPass(thump, sr, 160);
		for (int i = 0; i < front.Length; i++) front[i] += thump[i] * r.R(0.5, 0.9);
		Normalize(front);
		double start = 0.03;
		Add(x, front, (int)(start * sr), 1.0);

		// --- the ridges answering: soft, smeared, darker each time ---------------------------------------------------
		// (never crisp: distinct slap-backs read as more shots, the old take found; two to four, smeared over a hillside)
		int echoes = r.I(2, 5);
		var times = Enumerable.Range(0, echoes).Select(_ => r.R(0.3, 2.6)).OrderBy(v => v).ToArray();
		for (int k = 0; k < echoes; k++)
		{
			double at = times[k];
			// smear: each reflection comes back off a whole hillside, not a wall
			int len = (int)(r.R(0.12, 0.26) * sr);
			var sm = new double[len];
			for (int i = 0; i < len; i++) sm[i] = r.W() * Env((double)i / len, 0.25, 0.75);
			var e = Convolve(front, sm, front.Length + len);
			double cut = Math.Max(380, air * r.R(0.35, 0.55) / (1 + at * 0.5));
			LowPass(e, sr, cut);
			LowPass(e, sr, cut * 1.2);
			Normalize(e);
			double gain = r.R(0.2, 0.34) * Math.Exp(-at / 2.4);
			Add(x, e, (int)((start + at) * sr), gain);
		}

		// --- the roll: the woods' long tail, darkening as it dies ----------------------------------------------------
		double t60 = r.R(2.2, 3.6);
		var lp = new Biquad(sr); var lp2 = new Biquad(sr);
		var roll = new Smooth(r.Fork(), 7, r.R(0.28, 0.45));
		double rise = r.R(0.04, 0.09), tailGain = r.R(0.9, 1.3);
		for (int i = (int)(start * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - start;
			double env = (1 - Math.Exp(-t / rise)) * Math.Exp(-6.9 * t / t60);
			// a little lift as each ridge answers (the roll travels along the valley)
			foreach (var at in times) env *= 1 + 0.35 * Math.Exp(-Math.Pow((t - at - 0.15) / 0.35, 2));
			double fc = 300 + (air * 0.6 - 300) * Math.Exp(-t / 0.9);
			if ((i & 15) == 0) { lp.SetLp(fc); lp2.SetLp(fc * 1.25); }
			double swell = 0.7 + 0.3 * roll.At(t);
			x[i] += lp2.P(lp.P(r.W())) * env * swell * tailGain;
		}

		HighPass(x, sr, 32);
		// gentle levelling (no overdrive): the tail lifted toward the report so the whole event carries
		Limit(x, sr, ceilingDb: -12);
		Level(x, sr, thresholdDb: -30, ratio: 3.0);
		return FinishOneShot(x, sr, -3, 600);
	}

	/// <summary>A clean look-ahead peak limiter (3 ms ahead, 120 ms release) at <paramref name="ceilingDb"/> below the peak:
	/// the blast's first millisecond stops setting the level of everything after it, without clipping it.</summary>
	static void Limit(double[] x, int sr, double ceilingDb)
	{
		double ceil = Peak(x) * FromDb(ceilingDb);
		if (ceil <= 0) return;
		int ahead = (int)(0.003 * sr);
		var need = new double[x.Length];
		for (int i = 0; i < x.Length; i++) need[i] = Math.Min(1, ceil / Math.Max(Math.Abs(x[i]), 1e-12));
		double rel = Math.Exp(-1.0 / (0.12 * sr)), g = 1;
		var outp = new double[x.Length];
		for (int i = 0; i < x.Length; i++)
		{
			double want = 1;
			for (int j = i; j < Math.Min(x.Length, i + ahead); j++) want = Math.Min(want, need[j]);
			g = want < g ? want : rel * g + (1 - rel) * want;
			outp[i] = x[i] * g;
		}
		Array.Copy(outp, x, x.Length);
	}

	static double[] Convolve(double[] a, double[] k, int outLen)
	{
		var o = new double[outLen];
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] == 0) continue;
			for (int j = 0; j < k.Length && i + j < outLen; j++) o[i + j] += a[i] * k[j];
		}
		return o;
	}

	static void Normalize(double[] x)
	{
		double p = Peak(x);
		if (p > 0) Scale(x, 1 / p);
	}

	static void Add(double[] into, double[] what, int at, double gain)
	{
		for (int i = 0; i < what.Length && at + i < into.Length; i++) into[at + i] += what[i] * gain;
	}

	/// <summary>A slow, clean compressor (RMS follower, 5 ms attack, 350 ms release) relative to the peak.</summary>
	static void Level(double[] x, int sr, double thresholdDb, double ratio)
	{
		double pk = Peak(x);
		if (pk <= 0) return;
		double thr = pk * FromDb(thresholdDb), env = 0;
		double att = Math.Exp(-1.0 / (0.005 * sr)), rel = Math.Exp(-1.0 / (0.35 * sr));
		for (int i = 0; i < x.Length; i++)
		{
			double a = Math.Abs(x[i]);
			env = a > env ? att * env + (1 - att) * a : rel * env + (1 - rel) * a;
			if (env > thr) x[i] *= Math.Pow(env / thr, 1 / ratio - 1);
		}
	}
}
