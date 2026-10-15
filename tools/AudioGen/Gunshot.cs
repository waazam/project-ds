namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Act 1's far-off rifle shot. Second pass, 2026-10-15 (the owner, after playing the first: "the gunshot in act 1 could use
/// some more work"). The first pass took the highs off at 1.6-3.4 kHz twice over and smeared the front: its centroid sat at
/// 380-610 Hz and it read as a thud or far thunder, not a rifle. What survives of a rifle's report across a valley:
/// <list type="bullet">
/// <item>Air takes the highs gradually, not like a wall: at 20 C / 50 % RH roughly 5 dB/km at 1 kHz, 10 at 2 kHz, 30 at
/// 4 kHz (ISO 9613-1). At 400-900 m the 1-2.5 kHz band is still there, a few dB down, and 4 kHz is mostly gone. So the
/// far shot is still a sharp "POP" with a hard front (under 2 ms), just without the bright crack on top.</item>
/// <item>Under it the boom: the muzzle blast's low end (60-150 Hz) carries furthest, a short heavy thump.</item>
/// <item>Then the valley answers: one to three distinct but duller copies off the hillsides 0.3-1.5 s later (a clear
/// first slap-back is what says "valley"), each darker and softer, then the woods' rolling tail of 2-3.5 s whose highs die
/// first.</item>
/// </list>
/// Each take is its own distance (400-900 m), ridges and tail. Levelled loud (RMS about -21 dBFS) by a clean look-ahead
/// limiter on the report, not by overdrive.
/// </summary>
public static class Gunshot
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] RifleDistant(Rng r, int sr)
	{
		double dist = r.R(400, 900), km = dist / 1000.0;
		var x = Buf(sr, 5.5);
		double start = 0.02;

		// --- the report: a Friedlander pulse with its ground reflection, scattered a little by the trees -----------------
		double T = r.R(0.0009, 0.0016), b = r.R(1.3, 1.9);
		var shot = new double[(int)(0.25 * sr)];
		for (int i = 0; i < shot.Length; i++)
		{
			double t = (double)i / sr;
			if (t > 10 * T) break;
			shot[i] = (1 - t / T) * Math.Exp(-b * t / T);
		}
		int gd = (int)(r.R(0.0004, 0.0012) * sr);
		var report = (double[])shot.Clone();
		for (int i = gd; i < shot.Length; i++) report[i] += shot[i - gd] * r.R(0.5, 0.7);
		// the trees: a short smear (10-25 ms), the direct path still leading so the front stays hard
		int scatterLen = (int)(r.R(0.01, 0.025) * sr);
		var scatter = new double[scatterLen];
		for (int i = 0; i < scatterLen; i++) scatter[i] = r.W() * 0.35 * Math.Exp(-i / (scatterLen * 0.35));
		scatter[0] += 1.0;
		report = Convolve(report, scatter, shot.Length);
		// the air, frequency by frequency (ISO 9613-1, roughly): a gentle tilt above 1 kHz, steeper above 3
		Air(report, sr, km);
		Normalize(report);

		// the boom: the blast's low end, a short heavy thump (it carries furthest)
		var boom = new double[(int)(0.4 * sr)];
		double bf = r.R(62, 95);
		for (int i = 0; i < boom.Length; i++)
		{
			double t = (double)i / sr;
			boom[i] = Math.Sin(TwoPi * bf * t * (1 - 0.18 * Math.Min(1, t / 0.25))) * Perc(t, 0.003, 0.05);
		}
		// the body: a 300-900 Hz "pop" of noise under the front (what makes it a gunshot and not a click)
		var body = new double[(int)(0.15 * sr)];
		var bpB = Biquad.Bp(sr, r.R(650, 1100), 0.8);
		for (int i = 0; i < body.Length; i++) body[i] = bpB.P(r.W()) * Perc((double)i / sr, 0.0015, r.R(0.025, 0.045));
		Normalize(body);

		var hit = new double[(int)(0.45 * sr)];
		Add(hit, report, 0, 1.0);
		Add(hit, body, 0, r.R(0.7, 0.95));
		Add(hit, boom, 0, r.R(0.22, 0.34));
		Normalize(hit);
		Add(x, hit, (int)(start * sr), 1.0);

		// --- the valley answering: one to three copies of the same shot coming back, softer each time ---------------------
		// (2026-10-15, the owner: "the gunshot echo does not match the gunshot") the shot itself, off the hills: the same
		// report, body and boom, only a little smeared (a hillside, 15-35 ms) and dulled by the extra air of the trip there
		// and back, so it's plainly the same bang returning (it was low-passed to 0.7-2.4 kHz: a different, dull thud)
		int echoes = r.I(1, 4);
		double at = r.R(0.35, 0.7);
		for (int k = 0; k < echoes; k++)
		{
			int len = (int)(r.R(0.015, 0.035) * sr);
			var sm = new double[len];
			for (int i = 0; i < len; i++) sm[i] = r.W() * 0.5 * Env((double)i / len, 0.1, 0.9);
			sm[0] += 1.0;
			var e = Convolve(hit, sm, hit.Length + len);
			// the round trip's extra air (about 340 m a second there and back), and a hillside that keeps a little less top
			Air(e, sr, km + at * 0.34);
			LowPass(e, sr, 4200 / (1 + k * 0.3));
			Normalize(e);
			double gain = (k == 0 ? r.R(0.38, 0.5) : r.R(0.2, 0.3)) * Math.Pow(0.8, k);
			Add(x, e, (int)((start + at) * sr), gain);
			at += r.R(0.35, 0.8);
		}

		// --- the roll: the same shot ringing on through the woods (the shot and its echoes reverberated, not a separate noise),
		// its highs dying first ---------------------------------------------------------------------------------------------
		var dry = (double[])x.Clone();
		var rv = new Reverb(sr, r.R(0.8, 0.88), r.R(0.45, 0.6));
		var wet = new double[x.Length];
		for (int i = 0; i < x.Length; i++) wet[i] = rv.P(dry[i]);
		LowPass(wet, sr, 2600);
		HighPass(wet, sr, 160);   // (the woods ring in the mids; the boom stays the direct shot's own)
		double wetGain = r.R(0.5, 0.75) / Math.Max(Peak(wet), 1e-9) * Peak(dry);
		for (int i = 0; i < x.Length; i++) x[i] += wet[i] * wetGain * 0.22;

		HighPass(x, sr, 35);
		// loud: the report's first milliseconds stop setting the level of everything after (clean look-ahead limiter), then
		// levelled to an RMS that carries
		Limit(x, sr, ceilingDb: -14);
		NormRms(x, -21, -3);
		return FinishOneShot(x, sr, -3, 500);
	}

	/// <summary>Air absorption over <paramref name="km"/>, roughly ISO 9613-1 at 20 C / 50 % RH: a high shelf above 1 kHz
	/// (about 5 dB/km at 1 kHz, 10 at 2 kHz) and a low-pass whose corner falls with distance (about 30 dB/km at 4 kHz).</summary>
	static void Air(double[] x, int sr, double km)
	{
		// the shelf: split at 1.2 kHz, the top turned down by ~8 dB/km
		var lo = Biquad.Lp(sr, 1200);
		double topGain = FromDb(-8 * km);
		for (int i = 0; i < x.Length; i++)
		{
			double l = lo.P(x[i]);
			x[i] = l + (x[i] - l) * topGain;
		}
		// the corner: ~6 kHz at 300 m, ~3 kHz at 1 km
		double fc = Math.Max(1800, 6500 / (1 + 1.3 * km));
		LowPass(x, sr, fc);
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
}
