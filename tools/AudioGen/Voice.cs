namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// A small formant voice for Act 10's choir, which chants the words "come and see" in unison.
/// Each singer is additive: a glottal-like harmonic series (a slight roll-off only), each
/// harmonic weighted by the current formant envelope (five parallel resonances). Consonants:
/// /k/ is a burst plus a breathy release through the vowel's formants, /m/ and /n/ are
/// low-formant nasal murmurs, /s/ is band-limited frication. Formants glide between phones
/// (one-pole smoothing, so neighbouring sounds blend the way speech does).
/// </summary>
public static class Voice
{
	/// <summary>One phone: formant centres (Hz), levels (dB) and bandwidths (Hz).</summary>
	public sealed record Phone(double[] F, double[] Db, double[] Bw);

	static readonly double[] VowelBw = { 70, 90, 130, 160, 200 }, NasalBw = { 90, 150, 200, 250, 250 };
	static readonly double[] UhDb = { 0, -5, -15, -22, -30 }, SchwaDb = { 0, -8, -16, -22, -30 }, EeDb = { 0, -12, -10, -18, -28 };
	static readonly double[] MDb = { 0, -22, -26, -30, -36 }, NDb = { 0, -20, -24, -30, -36 };

	// Male (tenor/baritone) and female (alto) values; order: uh (come), schwa (and), m, n, ee (see).
	static readonly Phone[] Male =
	{
		new(new[] { 640.0, 1190, 2390, 3300, 3750 }, UhDb, VowelBw),
		new(new[] { 500.0, 1400, 2450, 3300, 3750 }, SchwaDb, VowelBw),
		new(new[] { 250.0, 1100, 2200, 3300, 3750 }, MDb, NasalBw),
		new(new[] { 250.0, 1500, 2500, 3300, 3750 }, NDb, NasalBw),
		new(new[] { 270.0, 2290, 3010, 3500, 4000 }, EeDb, VowelBw),
	};
	static readonly Phone[] Female =
	{
		new(new[] { 760.0, 1400, 2780, 3900, 4600 }, UhDb, VowelBw),
		new(new[] { 580.0, 1650, 2800, 3900, 4600 }, SchwaDb, VowelBw),
		new(new[] { 280.0, 1200, 2500, 3900, 4600 }, MDb, NasalBw),
		new(new[] { 280.0, 1700, 2700, 3900, 4600 }, NDb, NasalBw),
		new(new[] { 310.0, 2790, 3310, 4000, 4600 }, EeDb, VowelBw),
	};
	const int Uh = 0, Schwa = 1, M = 2, N = 3, Ee = 4;

	/// <summary>
	/// Phrase timing in seconds at tempo 1: (end time, phone for the formants, voicing, aspiration, frication).
	/// "come" /k ʌ m/, "and" /ə n/, "see" /s iː/. The /d/ is elided, as it is in a sung "come an' see".
	/// </summary>
	static readonly (double end, int phone, double voice, double asp, double fric)[] Script =
	{
		(0.012, Uh, 0, 0, 0),      // /k/ burst (added separately)
		(0.055, Uh, 0, 0.55, 0),   // breathy release into the vowel
		(0.44, Uh, 1.0, 0.04, 0),  // come
		(0.64, M, 0.42, 0, 0),
		(0.83, Schwa, 0.85, 0.03, 0), // and
		(0.97, N, 0.42, 0, 0),
		(1.14, Ee, 0, 0, 1.0),     // /s/: formants already moving to the "ee"
		(2.55, Ee, 1.0, 0.04, 0),  // seeee
	};
	public const double PhraseSeconds = 2.55;

	/// <summary>How one singer performs one phrase.</summary>
	public sealed record Take(double Start, double Tempo, double F0, double Amp, double SeeSagCents);

	/// <summary>
	/// Renders one singer's takes into <paramref name="buf"/> (circularly). The singer keeps one
	/// voice across takes: their own vibrato rate and depth, pitch wander, breathiness and timbre.
	/// </summary>
	public static void Sing(double[] buf, int sr, Rng r, bool female, IEnumerable<Take> takes, double wobble = 1.0)
	{
		var set = female ? Female : Male;
		double vibRate = r.R(4.4, 5.8), vibDepth = r.R(0.008, 0.015) * wobble, breathy = r.R(0.8, 1.3);
		double fShift = r.R(0.96, 1.05);   // vocal tract size
		double tilt = r.R(0.1, 0.3);   // the table levels are already output levels: only a slight extra roll-off
		foreach (var take in takes) Phrase(buf, sr, r.Fork(), set, take, vibRate * r.R(0.95, 1.05), vibDepth, breathy, fShift, tilt, wobble);
	}

	static void Phrase(double[] buf, int sr, Rng r, Phone[] set, Take tk, double vibRate, double vibDepth, double breathy, double fShift, double tilt, double wobble)
	{
		double dur = PhraseSeconds * tk.Tempo;
		int len = (int)((dur + 0.05) * sr), s0 = (int)(tk.Start * sr), nb = buf.Length;
		var jit = new Smooth(r, dur + 1, 0.05); var drift = new Smooth(r, dur + 1, r.R(0.6, 1.2)); var rateWob = new Smooth(r, dur + 1, 0.7);
		const int H = 48, Block = 32;
		var cur = new double[H + 1]; var tgt = new double[H + 1];
		var F = new double[5]; var G = new double[5]; var B = new double[5];
		double voice = 0, asp = 0, fric = 0;
		{
			var p0 = set[Script[0].phone];
			for (int k = 0; k < 5; k++) { F[k] = p0.F[k] * fShift; G[k] = FromDb(p0.Db[k]); B[k] = p0.Bw[k]; }
		}
		var aspF = new Biquad[3]; for (int k = 0; k < 3; k++) aspF[k] = new Biquad(sr);
		var fr1 = Biquad.Bp(sr, r.R(4300, 5000), 1.1); var fr2 = Biquad.Bp(sr, r.R(6000, 7200), 1.6);
		var burst = Biquad.Bp(sr, r.R(1500, 1900), 1.2);
		double aForm = 1 - Math.Exp(-Block / (sr * 0.028)), aAmp = 1 - Math.Exp(-1.0 / (sr * 0.012));
		double ph = r.U(), vph = r.R(0, TwoPi), lastVoicedOnset = -1;
		int seg = 0;
		double fadeTail = 0.45 * tk.Tempo;
		for (int i = 0; i < len; i++)
		{
			double t = (double)i / sr, st = t / tk.Tempo;   // st = script time
			while (seg < Script.Length - 1 && st >= Script[seg].end) seg++;
			var sc = Script[seg];
			bool past = st >= PhraseSeconds;
			double tv = past ? 0 : sc.voice, ta = past ? 0 : sc.asp, tf = past ? 0 : sc.fric;
			if (!past && seg == Script.Length - 1)
			{
				double left = dur - t;
				if (left < fadeTail) { double fs = Math.Sin(0.5 * Math.PI * left / fadeTail); tv *= fs * fs; }
			}
			if (tv > 0.5 && lastVoicedOnset < 0 && voice < 0.1) lastVoicedOnset = t;
			if (sc.voice == 0) lastVoicedOnset = -1;
			voice += (tv - voice) * aAmp; asp += (ta - asp) * aAmp; fric += (tf - fric) * aAmp;

			// Pitch: a scoop up into each vowel after a consonant, an unsteady vibrato that grows in,
			// slow wander, fine jitter, and the long "see" sagging flat as the breath runs out.
			double since = lastVoicedOnset >= 0 ? t - lastVoicedOnset : 0;
			double scoop = -0.045 * Math.Exp(-since / 0.07);
			vph += TwoPi * vibRate * (1 + 0.15 * (rateWob.At(t) - 0.5)) / sr;
			double vibOn = Math.Clamp((t - 0.25) / 0.6, 0, 1);
			double sag = 0;
			if (seg == Script.Length - 1) { double u = Math.Clamp((st - 1.6) / (PhraseSeconds - 1.6), 0, 1); sag = tk.SeeSagCents * u * u; }
			double cents = sag + 25 * wobble * (drift.At(t) - 0.5);
			double f = tk.F0 * Math.Pow(2, cents / 1200) * (1 + scoop) * (1 + vibDepth * vibOn * Math.Sin(vph) + 0.006 * (jit.At(t) - 0.5));
			ph += f / sr; if (ph > 1) ph -= 1;

			if (i % Block == 0)
			{
				var p = set[sc.phone];
				for (int k = 0; k < 5; k++)
				{
					F[k] += (p.F[k] * fShift - F[k]) * aForm;
					G[k] += (FromDb(p.Db[k]) - G[k]) * aForm;
					B[k] += (p.Bw[k] * 1.3 - B[k]) * aForm;
				}
				for (int h = 1; h <= H; h++)
				{
					double fh = f * h;
					if (fh > 5200) { tgt[h] = 0; continue; }
					double e = 0;
					for (int k = 0; k < 5; k++) { double d = (fh - F[k]) / (B[k] * 0.5); e += G[k] / Math.Sqrt(1 + d * d); }
					tgt[h] = e * Math.Pow(h, -tilt);
				}
				for (int k = 0; k < 3; k++) aspF[k].SetBp(F[k], Math.Max(1, F[k] / B[k]));
			}
			double v = 0;
			for (int h = 1; h <= H; h++)
			{
				cur[h] += (tgt[h] - cur[h]) * 0.03;
				if (cur[h] > 1e-5) v += cur[h] * Math.Sin(TwoPi * h * ph);
			}
			double pulse = 0.5 + 0.5 * Math.Cos(TwoPi * ph);
			double wn = r.W();
			// Breath: turbulence at the glottis (pulse-synchronous while voicing) through the vocal tract.
			double src = wn * (asp + 0.05 * breathy * voice * pulse * pulse * 4);
			double breath = aspF[0].P(src) + 0.8 * aspF[1].P(src) + 0.5 * aspF[2].P(src);
			double s = (fr1.P(wn) + 0.6 * fr2.P(wn)) * fric * 0.2;
			double k0 = st < Script[0].end ? burst.P(wn) * Perc(t, 0.0003, 0.004) * 1.4 : 0;
			buf[((s0 + i) % nb + nb) % nb] += (v * voice * 0.55 + breath * 1.1 + s + k0) * tk.Amp;
		}
	}
}
