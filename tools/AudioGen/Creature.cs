namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Creature vocalisations for the stalker and the giant: growls, roars, screeches and snarls.
/// Candidates only for now (nothing in the game plays them yet); the ones that work get wired
/// into the stalker's close-behind moments and the giant's crossing.
///
/// Every voice is built the same way: a glottal pulse train (a decaying click per cycle, with
/// per-cycle jitter and period doubling for vocal fry) at a very low fundamental, pushed through
/// three drifting formant resonances (a throat and a mouth, not a synth), a little breath noise
/// at the mouth, then tanh saturation and a room. All of it stays deep: fundamentals 30-110 Hz,
/// screeches capped under 4 kHz and given a sub layer, so nothing reads as thin or high.
/// </summary>
public static class Creature
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>
	/// The larynx: one decaying pulse per period. <paramref name="jitter"/> = per-cycle pitch wobble
	/// (0.05 = ±5 %), <paramref name="fry"/> = how much every other pulse is weakened (period doubling:
	/// the rattle of a growl), <paramref name="width"/> = pulse decay as a fraction of the period.
	/// </summary>
	sealed class Larynx
	{
		readonly Rng _r; readonly int _sr;
		double _ph = 1, _age, _period = 1, _gain = 1; int _n;
		public double Jitter = 0.04, Fry = 0.0, Width = 0.22;
		public Larynx(Rng r, int sr) { _r = r; _sr = sr; }
		public double P(double f0)
		{
			_ph += f0 / _sr;
			if (_ph >= 1)
			{
				_ph -= 1; _age = 0; _n++;
				_period = 1.0 / Math.Max(f0, 1) * (1 + Jitter * _r.W());
				_gain = (1 + 0.15 * _r.W()) * ((_n & 1) == 1 ? 1 - Fry : 1);
			}
			_age += 1.0 / _sr;
			return _gain * Math.Exp(-_age / (_period * Width));
		}
	}

	/// <summary>Three retunable formant resonances (mouth shape), retuned every 32 samples.</summary>
	sealed class Mouth
	{
		readonly Biquad _f1, _f2, _f3; readonly double _sr; int _k;
		public double F1 = 350, F2 = 900, F3 = 2300, G1 = 1, G2 = 0.55, G3 = 0.25, Q = 7;
		public Mouth(double sr) { _sr = sr; _f1 = new Biquad(sr); _f2 = new Biquad(sr); _f3 = new Biquad(sr); Retune(); }
		void Retune() { _f1.SetBp(F1, Q); _f2.SetBp(F2, Q * 1.2); _f3.SetBp(F3, Q * 1.3); }
		public double P(double x)
		{
			if ((_k++ & 31) == 0) Retune();
			return _f1.P(x) * G1 + _f2.P(x) * G2 + _f3.P(x) * G3;
		}
	}

	static double[] Room(double[] dry, int sr, double mix, double size, double damp)
	{
		var rv = new Reverb(sr, size, damp);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] + rv.P(dry[i]) * mix;
		return o;
	}

	/// <summary>
	/// A low, chesty growl close by: 30-48 Hz with fry, the mouth nearly closed, a slow swell that
	/// holds and lets go, and a rattle as the pulses thin out at the end.
	/// </summary>
	public static double[] Growl(Rng r, int sr, int variant)
	{
		double dur = r.R(2.2, 3.4);
		var x = Buf(sr, dur);
		var lar = new Larynx(r, sr) { Jitter = r.R(0.05, 0.09), Fry = r.R(0.35, 0.6), Width = r.R(0.16, 0.24) };
		var mouth = new Mouth(sr) { F1 = r.R(280, 400), F2 = r.R(700, 1000), F3 = r.R(1800, 2400), G2 = 0.45, G3 = 0.18, Q = 6 };
		double f0 = r.R(30, 48);
		var drift = new Smooth(r, dur, 0.15);
		var open = new Smooth(r, dur, 0.2);
		var breathBp = Biquad.Bp(sr, mouth.F2, 2.5);
		double tremorHz = r.R(4, 7);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double env = Env(u, 0.22, 0.32);
			// The pulse rate sags and rattles as the breath runs out.
			double f = f0 * (1 + 0.12 * drift.At(t)) * (1 - 0.35 * Math.Max(0, u - 0.7) / 0.3);
			double o = 0.5 + 0.5 * open.At(t);
			mouth.F1 = 280 + 160 * o; mouth.F2 = 700 + 300 * o;
			double src = lar.P(f);
			double breath = breathBp.P(r.W()) * 0.12 * (0.6 + 0.4 * o);
			double tremor = 1 - 0.15 * (0.5 + 0.5 * Math.Sin(TwoPi * tremorHz * t));
			x[i] = Math.Tanh((mouth.P(src) * 3.2 + breath) * 1.6) * env * tremor + Math.Sin(TwoPi * f * t) * 0.18 * env;
		}
		LowPass(x, sr, variant % 2 == 0 ? 2600 : 3400);
		return FinishOneShot(Room(x, sr, 0.22, 0.5, 0.5), sr, -3, 120);
	}

	/// <summary>
	/// The lake creature's breach: a real foghorn's slow, mournful pitch (a low pipe resonance, built
	/// from a pulse train through a formant, not a clean synth tone) with something alive underneath
	/// it - a second throat a few percent flat for a dissonant beat, a slow creature-like pulse
	/// instead of a steady drone, and a very slow wobble so it never sits perfectly still. Long swell
	/// in, a held mournful sag, slow swell out, in a big hall (heard across open water).
	/// </summary>
	public static double[] Foghorn(Rng r, int sr)
	{
		double dur = r.R(4.5, 5.5);
		var x = Buf(sr, dur);
		double f0 = r.R(58, 72);
		var larA = new Larynx(r, sr) { Jitter = 0.02, Fry = r.R(0.1, 0.25), Width = 0.3 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.03, Fry = 0.15, Width = 0.28 };
		var pipe = new Mouth(sr) { F1 = f0 * 1.9, F2 = f0 * 3.1, F3 = f0 * 5.4, G1 = 1.0, G2 = 0.6, G3 = 0.25, Q = 9 };
		double wobbleHz = r.R(0.35, 0.55);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double env = Env(u, 0.16, 0.4);   // slow swell in, slow release
			double sag = 1.0 - 0.05 * Math.Min(1, u / 0.5);   // a mournful droop as it holds
			double wobble = 1 + 0.015 * Math.Sin(TwoPi * wobbleHz * t);
			double f = f0 * sag * wobble;
			double src = larA.P(f) + larB.P(f * 0.982) * 0.75;
			double horn = Math.Tanh(pipe.P(src) * 3.0);
			x[i] = horn * env;
		}
		LowPass(x, sr, 900);
		var hall = new Hall(sr, 3.8, 0.45, 60, 1.8);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.5 + hall.P(x[i]) * 1.3;
		return FinishOneShot(o, sr, -3, 700);
	}

	/// <summary>
	/// A full roar: the pitch climbs from ~55 Hz to ~110 Hz and breaks, the mouth opens wide, two
	/// detuned throats for size, a sub layer under it all. <paramref name="far"/> puts it a few hundred
	/// metres off through the trees (the giant): mostly hall, no top end.
	/// </summary>
	public static double[] Roar(Rng r, int sr, bool far)
	{
		double dur = r.R(2.8, 4.0);
		var x = Buf(sr, dur);
		var larA = new Larynx(r, sr) { Jitter = 0.035, Fry = r.R(0.05, 0.2), Width = 0.28 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.05, Fry = 0.1, Width = 0.26 };
		var mouth = new Mouth(sr) { G2 = 0.7, G3 = 0.3, Q = 5.5 };
		double lo = r.R(52, 62), hi = r.R(95, 115), peakAt = r.R(0.35, 0.5);
		var rasp = Biquad.Bp(sr, 1600, 1.5);
		double crack = r.R(0.62, 0.78);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double rise = u < peakAt ? Math.Pow(u / peakAt, 0.7) : 1 - 0.45 * Math.Pow((u - peakAt) / (1 - peakAt), 1.4);
			double f = lo + (hi - lo) * rise;
			if (u > crack) f *= 1 - 0.18 * Math.Min(1, (u - crack) / 0.08);   // the voice breaks
			double open = Math.Pow(Math.Min(1, rise * 1.2), 0.8);
			mouth.F1 = 320 + 560 * open; mouth.F2 = 850 + 650 * open; mouth.F3 = 2400 + 400 * open;
			double src = larA.P(f) + larB.P(f * 1.028) * 0.6;
			double env = Env(u, 0.14, 0.3);
			double harsh = Math.Tanh(mouth.P(src) * 4.5) + rasp.P(r.W()) * 0.18 * open;
			double sub = Math.Sin(TwoPi * f * 0.5 * t) * 0.5 * env * (0.4 + 0.6 * open);
			x[i] = harsh * env + sub;
		}
		if (far)
		{
			LowPass(x, sr, 1500);
			var hall = new Hall(sr, 2.6, 0.55, 40, 1.4);
			var o = new double[x.Length];
			for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.3 + hall.P(x[i]) * 1.1;
			return FinishOneShot(o, sr, -3, 700);
		}
		LowPass(x, sr, 4200);
		return FinishOneShot(Room(x, sr, 0.35, 0.7, 0.45), sr, -3, 250);
	}

	/// <summary>
	/// A screech: not a bird, not a woman. Two inharmonic throats a fifth-and-a-bit apart around
	/// 200-330 Hz with a fast quaver, the sound tearing upward and cracking, a thin knife of noise
	/// at the mouth, and a low pulse under it so it still has a chest. Capped under 4 kHz.
	/// </summary>
	public static double[] Screech(Rng r, int sr, int variant)
	{
		double dur = r.R(1.2, 2.0);
		var x = Buf(sr, dur);
		double f0 = r.R(160, 250), ratio = r.R(1.38, 1.46);
		double quaverHz = r.R(9, 14), crack = r.R(0.5, 0.68);
		var mouth = new Mouth(sr) { F1 = r.R(700, 900), F2 = r.R(1700, 2100), F3 = r.R(2800, 3400), G1 = 0.8, G2 = 0.8, G3 = 0.45, Q = 5 };
		var knife = Biquad.Bp(sr, r.R(2000, 2600), 3);
		var lar = new Larynx(r, sr) { Jitter = 0.08, Fry = 0.5, Width = 0.2 };
		double phA = 0, phB = 0;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double bend = 1 + 0.7 * Math.Pow(Math.Min(1, u / 0.45), 0.6);
			if (u > crack) bend *= 1 - 0.3 * Math.Min(1, (u - crack) / 0.05);
			double quaver = 1 + 0.06 * Math.Sin(TwoPi * quaverHz * t) + 0.02 * r.W();
			double fA = f0 * bend * quaver, fB = fA * ratio;
			phA += TwoPi * fA / sr; phB += TwoPi * fB / sr;
			double vA = 0, vB = 0;
			for (int h = 1; h <= 9; h++)
			{
				if (fA * h < sr * 0.45) vA += Math.Sin(phA * h) / Math.Pow(h, 0.55);
				if (fB * h < sr * 0.45 && h <= 6) vB += Math.Sin(phB * h) / Math.Pow(h, 0.7);
			}
			double env = Env(u, 0.08, 0.35) * (u > crack ? 0.75 + 0.25 * Math.Max(0, 1 - (u - crack) / 0.1) : 1);
			double voice = Math.Tanh((vA + vB * 0.7) * 1.8);
			double tear = knife.P(r.W()) * 0.35 * Math.Pow(Math.Min(1, u / 0.3), 2);
			double chest = lar.P(42 + 10 * bend) * 0.9;
			x[i] = (mouth.P(voice) * 1.4 + voice * 0.35 + tear) * env + Math.Tanh(chest * 2) * 0.4 * env;
		}
		LowPass(x, sr, variant % 2 == 0 ? 2600 : 3200);
		return FinishOneShot(Room(x, sr, 0.5, 0.75, 0.4), sr, -3, 260);
	}

	/// <summary>
	/// The jumpscare's voice: a roar that tears into a scream. Built the way film and game creature
	/// screams are layered (a low roar body, a mid tearing voice, air on top): it opens as a roar (a
	/// 70-120 Hz fry larynx, the mouth snapping open in a tenth of a second) and a quarter second in
	/// the scream comes up through it, a harmonic voice at 220-340 Hz with a fast quaver and an
	/// inharmonic partner a fifth-and-a-bit above, a knife of noise at the mouth, breaking downward
	/// near the end into fry. A sub under it and a 3.4 kHz cap keep it a creature, not a woman.
	/// </summary>
	public static double[] JumpScream(Rng r, int sr, int variant)
	{
		double dur = r.R(1.4, 1.9);
		var x = Buf(sr, dur);
		var lar = new Larynx(r, sr) { Jitter = 0.05, Fry = r.R(0.25, 0.45), Width = 0.26 };
		var roarMouth = new Mouth(sr) { G2 = 0.7, G3 = 0.3, Q = 5 };
		var screamMouth = new Mouth(sr) { F1 = r.R(650, 850), F2 = r.R(1500, 1900), F3 = r.R(2500, 3000), G1 = 0.9, G2 = 0.8, G3 = 0.4, Q = 5 };
		var knife = Biquad.Bp(sr, r.R(1800, 2400), 2.5);
		double f0 = r.R(220, 340), ratio = r.R(1.38, 1.46), quaverHz = r.R(6, 9), crack = r.R(0.62, 0.74);
		double phA = 0, phB = 0;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double roarF = 70 + 50 * Math.Min(1, t / 0.25);
			double roarOpen = Math.Min(1, t / 0.12);
			roarMouth.F1 = 300 + 500 * roarOpen; roarMouth.F2 = 800 + 600 * roarOpen; roarMouth.F3 = 2400;
			double roarEnv = Math.Min(1, t / 0.012) * (u < 0.55 ? 1 : Math.Max(0, 1 - (u - 0.55) / 0.3)) * (t < 0.3 ? 1 : 0.55);
			double roar = Math.Tanh(roarMouth.P(lar.P(roarF)) * 4.5) * roarEnv;
			double sIn = Math.Max(0, Math.Min(1, (t - 0.18) / 0.15));
			double bend = 1 + 0.5 * Math.Pow(Math.Min(1, Math.Max(0, u - 0.1) / 0.4), 0.7);
			if (u > crack) bend *= 1 - 0.28 * Math.Min(1, (u - crack) / 0.06);
			double quaver = 1 + 0.05 * Math.Sin(TwoPi * quaverHz * t) + 0.015 * r.W();
			double fA = f0 * bend * quaver, fB = fA * ratio;
			phA += TwoPi * fA / sr; phB += TwoPi * fB / sr;
			double vA = 0, vB = 0;
			for (int h = 1; h <= 10; h++)
			{
				if (fA * h < sr * 0.45) vA += Math.Sin(phA * h) / Math.Pow(h, 0.5);
				if (fB * h < sr * 0.45 && h <= 6) vB += Math.Sin(phB * h) / Math.Pow(h, 0.7);
			}
			double screamEnv = sIn * Env(u, 0.02, 0.3) * (u > crack ? 0.8 : 1);
			double voice = Math.Tanh((vA + vB * 0.6) * 2.0);
			double air = knife.P(r.W()) * 0.4 * sIn;
			double scream = (screamMouth.P(voice) * 1.3 + voice * 0.4 + air) * screamEnv;
			double sub = Math.Sin(TwoPi * 45 * t) * 0.35 * Env(u, 0.05, 0.4);
			x[i] = roar * 0.9 + scream + sub;
		}
		LowPass(x, sr, variant == 2 ? 2900 : 3400);
		return FinishOneShot(Room(x, sr, 0.45, 0.7, 0.4), sr, -3, 300);
	}

	/// <summary>
	/// The jumpscare, to Dan's brief (2026-09-22): a cinematic, extremely loud monster hit, something
	/// physically impossible right in front of the camera. On his timeline:
	///  * 0.00-0.10 s: near-silence with a reversed breath/whoosh swelling into it, then a violent impact:
	///    a massive sub hit, a crushing broadband slam, metal tearing (all of it noise: nothing pitched, nothing that boings);
	///  * 0.10-0.50 s: the roar/scream at full force: a deep distorted roar (60-90 Hz fry larynx through
	///    a wide-open mouth), a harsh animal scream with a human vowel on it (a 300-450 Hz voice through
	///    "aah" formants, quavering, with an inharmonic partner), a sharp shriek over it (2.5-3.5 kHz),
	///    heavy breath/growl textures under it, deep bass sustained;
	///  * 0.50-1.20 s: the vocalization sustained: growls, choking (a 12-18 Hz amplitude stutter),
	///    distorted breathing, organic tearing (wet, crackling noise bursts);
	///  * 1.20-1.80 s: it deteriorates: the scream collapses into distorted static, a low growl, and a
	///    final crushing impact;
	///  * 1.80-2.50 s: a short tail: deep rumble dying, two unsettling breaths.
	/// Soft-clipped for loudness, peak-normalised, no hard clipping; the game plays it +9 dB.
	/// </summary>
	public static double[] Jumpscare(Rng r, int sr, int variant)
	{
		double dur = 2.5;
		var x = Buf(sr, dur);
		int n = x.Length;
		double T(int i) => (double)i / sr;

		// --- the lead-in: a reversed breath (noise swelling up to the hit, cut dead by it)
		var leadBp = Biquad.Bp(sr, r.R(900, 1400), 1.2);
		for (int i = 0; i < 0.085 * sr; i++)
		{
			double u = T(i) / 0.085;
			x[i] += leadBp.P(r.W()) * Math.Pow(u, 2.5) * 0.35;
		}

		// --- the impact at 0.09 s: sub hit, crushing slam, metal tear. All noise: a swept sine reads as a
		// "boing" or a kick drum, never as a body hitting you.
		int hit = (int)(0.09 * sr);
		var subLp1 = Biquad.Lp(sr, 48); var subLp2 = Biquad.Lp(sr, 62);
		var slamBp = Biquad.Bp(sr, 170, 0.6); var slamLp = Biquad.Lp(sr, 1100);
		var tearBp1 = Biquad.Bp(sr, r.R(1800, 2400), 10); var tearBp2 = Biquad.Bp(sr, r.R(2900, 3500), 12); var tearBp3 = Biquad.Bp(sr, r.R(3900, 4700), 12);
		for (int i = 0; i + hit < n && i < 0.9 * sr; i++)
		{
			double t = T(i);
			double sub = subLp2.P(subLp1.P(r.W())) * Perc(t, 0.003, 0.28) * 22.0;
			double slam = slamLp.P(slamBp.P(r.W())) * Perc(t, 0.001, 0.055) * 5.0;
			double tearAm = 0.55 + 0.45 * Math.Sign(Math.Sin(TwoPi * r.R(38, 46) * t));   // the metal shrieking in ragged pulses
			double tear = (tearBp1.P(r.W()) * 1.0 + tearBp2.P(r.W()) * 0.8 + tearBp3.P(r.W()) * 0.5) * Perc(t, 0.001, 0.11) * 3.5 * tearAm;
			x[hit + i] += Math.Tanh(sub) * 1.3 + Math.Tanh(slam) + tear;
		}

		// --- the voice: roar + scream + shriek + breath, 0.10 .. 1.8 s, sustained then collapsing
		var lar = new Larynx(r, sr) { Jitter = 0.06, Fry = r.R(0.3, 0.5), Width = 0.24 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.05, Fry = 0.2, Width = 0.28 };
		var roarMouth = new Mouth(sr) { F1 = 700, F2 = 1300, F3 = 2600, G2 = 0.7, G3 = 0.3, Q = 5 };
		var vowel = new Mouth(sr) { F1 = r.R(650, 800), F2 = r.R(1050, 1250), F3 = r.R(2500, 2900), G1 = 1, G2 = 0.8, G3 = 0.45, Q = 6 };
		var shriekBp = new Biquad(sr);   // a screaming band of noise, retuned as it wobbles (never a sine: that whistles)
		var breathBp = Biquad.Bp(sr, r.R(500, 800), 1.4);
		var crackleBp = Biquad.Bp(sr, r.R(1100, 1600), 2.5);
		var staticHp = Biquad.Hp(sr, 700);
		double f0 = r.R(300, 450), ratio = r.R(1.33, 1.47), quaverHz = r.R(7, 11), chokeHz = r.R(12, 18);
		double roughHz = r.R(28, 44), shriekF = r.R(2600, 3400);
		var bassLp1 = Biquad.Lp(sr, 52); var bassLp2 = Biquad.Lp(sr, 70);
		double phA = 0, phB = 0;
		int v0 = (int)(0.10 * sr);
		for (int i = v0; i < 1.85 * sr && i < n; i++)
		{
			double t = T(i), tv = t - 0.10;
			// envelopes on Dan's timeline
			double burst = tv < 0.4 ? 1 : tv < 1.1 ? 0.85 : Math.Max(0, 1 - (tv - 1.1) / 0.6);      // roar/scream body
			double choke = tv > 0.4 && tv < 1.1 ? 0.55 + 0.45 * (Math.Sin(TwoPi * chokeHz * t) > 0.2 ? 1 : 0) : 1;
			double collapse = tv > 1.1 ? Math.Min(1, (tv - 1.1) / 0.5) : 0;                             // into static
			// roar: fry larynx, wide mouth
			double roarF = 60 + 30 * Math.Min(1, tv / 0.2) * (1 - 0.4 * collapse);
			double roar = Math.Tanh(roarMouth.P(lar.P(roarF) + larB.P(roarF * 1.03) * 0.6) * 5.0);
			// scream with a human vowel, quavering, a fifth-and-a-bit partner
			double bend = 1 + 0.35 * Math.Min(1, tv / 0.25) - 0.5 * collapse;
			double q = 1 + 0.06 * Math.Sin(TwoPi * quaverHz * t) + 0.02 * r.W();
			double fA = f0 * bend * q, fB = fA * ratio;
			phA += TwoPi * fA / sr; phB += TwoPi * fB / sr;
			double vA = 0, vB = 0;
			for (int h = 1; h <= 10; h++)
			{
				if (fA * h < sr * 0.45) vA += Math.Sin(phA * h) / Math.Pow(h, 0.5);
				if (fB * h < sr * 0.45 && h <= 6) vB += Math.Sin(phB * h) / Math.Pow(h, 0.7);
			}
			// roughness: the voice torn by a 30-45 Hz flutter (a growl's grit), plus noise driven through the same vowel
			double rough = 0.6 + 0.4 * Math.Sign(Math.Sin(TwoPi * roughHz * t + 0.5 * Math.Sin(TwoPi * 3.3 * t)));
			double screamSrc = Math.Tanh((vA + vB * 0.6) * 2.2) * rough + r.W() * 0.35;
			double scream = Math.Tanh(vowel.P(screamSrc) * 1.8);
			// the shriek over it: a narrow screaming band of noise that wobbles, not a tone
			if ((i & 31) == 0) shriekBp.SetBp(shriekF * (1 + 0.05 * Math.Sin(TwoPi * 9 * t)) * (1 - 0.3 * collapse), 9);
			double shriek = Math.Tanh(shriekBp.P(r.W()) * 4.0) * (tv < 0.5 ? 1 : 0.5) * (1 - collapse);
			// breath, growl grit and organic tearing under it
			double breath = breathBp.P(r.W()) * 0.5 * (0.5 + 0.5 * Math.Sin(TwoPi * 3.1 * t));
			double crackle = crackleBp.P(r.W()) * (r.U() < 0.08 ? 3.0 : 0.3) * (tv > 0.4 ? 1 : 0.3);
			// the collapse into distorted static: bitcrushed noise takes over
			double st = staticHp.P(r.W());
			st = Math.Round(st * 6) / 6;
			double stat = Math.Tanh(st * 4) * collapse * 0.9;
			double voice = (roar * 1.1 + scream * 1.0 + shriek * 0.55 + breath * 0.6 + crackle * 0.5) * burst * choke * (1 - 0.6 * collapse) + stat;
			// deep bass sustained under the first half second
			double bass = bassLp2.P(bassLp1.P(r.W())) * 9.0 * (tv < 0.5 ? 1 : Math.Exp(-(tv - 0.5) / 0.3));   // sub weight from noise, not a tone
			x[i] += Math.Tanh(voice * 1.4) + bass;
		}

		// --- the final impact at ~1.7 s, then the tail: rumble and two breaths
		int hit2 = (int)(1.7 * sr);
		var s2Lp = Biquad.Lp(sr, 500); var s2Sub1 = Biquad.Lp(sr, 45); var s2Sub2 = Biquad.Lp(sr, 60);
		for (int i = 0; i + hit2 < n && i < 0.6 * sr; i++)
		{
			double t = T(i);
			x[hit2 + i] += Math.Tanh(s2Sub2.P(s2Sub1.P(r.W())) * Perc(t, 0.003, 0.22) * 20.0) * 1.2 + s2Lp.P(r.W()) * Perc(t, 0.002, 0.06) * 2.2;
		}
		var rumLp1 = Biquad.Lp(sr, 70); var rumLp2 = Biquad.Lp(sr, 90);
		var brBp = Biquad.Bp(sr, r.R(450, 650), 1.1); var brLp = Biquad.Lp(sr, 1500);
		for (int i = (int)(1.8 * sr); i < n; i++)
		{
			double t = T(i), u = (t - 1.8) / 0.7;
			double rumble = rumLp2.P(rumLp1.P(r.W())) * Math.Exp(-u * 2.2) * 6.0;
			double b1 = Env(Math.Clamp((t - 1.95) / 0.25, 0, 1), 0.3, 0.5), b2 = Env(Math.Clamp((t - 2.25) / 0.22, 0, 1), 0.3, 0.5);
			double breath = brLp.P(brBp.P(r.W())) * (b1 + b2 * 0.8) * 0.5;
			x[i] += rumble + breath;
		}
		// nothing hard-clips: a soft ceiling, then the peak set high
		for (int i = 0; i < n; i++) x[i] = Math.Tanh(x[i] * 0.7) * 1.3;
		LowPass(x, sr, 5200);
		return FinishOneShot(Room(x, sr, 0.3, 0.65, 0.4), sr, -3, 200);   // the game plays it +9 dB: the loudness is in the mix, the file stays clean
	}

	/// <summary>
	/// The rattle: a dry, clicking croak from deep in the throat (a death rattle, not an insect),
	/// in bursts that speed up and trail off, with quiet between them. A seamless loop the stalker
	/// plays from its tree; the game brings it up and quickens it as it gets closer. Each click is
	/// a short low knock through a closed-throat resonance, so it sits at 300-900 Hz with a
	/// 40 Hz chest under it: nothing high, nothing tonal.
	///
	/// One voice, many beats (Dan, 2026-09-22: "no new sounds, just different beats or rhythms"):
	/// every take uses exactly this click, throat and chest; <paramref name="rhythm"/> 1-8 changes
	/// only the timing. 1 = the original accelerating bursts; 2 = slow and sparse; 3 = long runs
	/// that speed up then wind down; 4 = double-clicks; 5 = triplets; 6 = starting fast and
	/// slowing; 7 = stuttering short bursts with the odd long silence; 8 = a mix of 1, 3, 4 and 6.
	/// </summary>
	public static double[] RattleLoop(Rng r, int sr, int sec, int rhythm = 1)
	{
		int n = sec * sr;
		var x = new double[n];
		// The click itself: unchanged across every take.
		void Click(double at, double amp, double throat, double chest)
		{
			double tau = r.R(0.004, 0.009);
			var bp = Biquad.Bp(sr, throat * r.R(0.85, 1.15), 4);
			var lp = Biquad.Lp(sr, 1400);
			int s0 = (int)(at * sr), len = (int)(0.06 * sr);
			AddEvent(x, s0, len, i =>
			{
				double tt = (double)i / sr;
				double knock = lp.P(bp.P(r.W())) * Perc(tt, 0.0004, tau) * 5.5;
				double thump = Math.Sin(TwoPi * chest * tt) * Perc(tt, 0.002, 0.018) * 0.8;
				return (Math.Tanh(knock * 1.8) + thump) * amp;
			});
		}
		// A run of single clicks: the gap shrinking (accelerating) or growing, quiet lead-in, stragglers at the end.
		double Run(double t, int clicks, double gap, Func<int, double> gapMul, double throat, double chest, double burstGain)
		{
			for (int k = 0; k < clicks; k++)
			{
				double amp = burstGain * (0.7 + 0.3 * r.U()) * (k < 2 ? 0.6 : 1) * (k > clicks - 3 ? 0.55 : 1);
				Click(t, amp, throat, chest);
				t += gap;
				gap *= gapMul(k);
			}
			return t;
		}
		// Groups of two or three clicks a hair apart, the groups themselves spaced out and drawing together.
		double Grouped(double t, int groups, int size, double within, double between, double throat, double chest, double burstGain)
		{
			double[] inner = { 1.0, 0.75, 0.6 };
			for (int g = 0; g < groups; g++)
			{
				double amp = burstGain * (0.7 + 0.3 * r.U()) * (g == 0 ? 0.7 : 1) * (g == groups - 1 ? 0.6 : 1);
				for (int k = 0; k < size; k++) Click(t + k * within * r.R(0.9, 1.1), amp * inner[k], throat, chest);
				t += between;
				between *= r.R(0.9, 0.97);
			}
			return t;
		}

		double tt0 = r.R(0.2, 0.8);
		int burstNo = 0;
		while (tt0 < sec)
		{
			double throat = r.R(380, 620), chest = r.R(36, 48), burstGain = r.R(0.6, 1.0);
			int mode = rhythm == 8 ? new[] { 1, 3, 4, 6 }[r.I(0, 4)] : rhythm;
			double rest;
			switch (mode)
			{
				case 2:   // slow and sparse
					tt0 = Run(tt0, r.I(4, 10), r.R(0.18, 0.26), k => r.R(0.93, 0.98), throat, chest, burstGain);
					rest = r.R(1.8, 3.5);
					break;
				case 3:   // long runs: speeding up, then winding down
				{
					int clicks = r.I(18, 35);
					tt0 = Run(tt0, clicks, r.R(0.09, 0.13), k => k < clicks * 0.6 ? r.R(0.9, 0.96) : r.R(1.08, 1.2), throat, chest, burstGain);
					rest = r.R(1.0, 2.0);
					break;
				}
				case 4:   // double-clicks
					tt0 = Grouped(tt0, r.I(5, 13), 2, r.R(0.045, 0.07), r.R(0.2, 0.3), throat, chest, burstGain);
					rest = r.R(1.0, 2.5);
					break;
				case 5:   // triplets
					tt0 = Grouped(tt0, r.I(4, 10), 3, r.R(0.055, 0.08), r.R(0.3, 0.45), throat, chest, burstGain);
					rest = r.R(1.2, 2.8);
					break;
				case 6:   // starting fast and slowing down
					tt0 = Run(tt0, r.I(8, 17), r.R(0.07, 0.09), k => r.R(1.07, 1.12), throat, chest, burstGain);
					rest = r.R(0.9, 2.4);
					break;
				case 7:   // stutter: short bursts close together, then now and then a long silence
					tt0 = Run(tt0, r.I(3, 6), r.R(0.1, 0.14), k => r.R(0.95, 1.0), throat, chest, burstGain);
					rest = burstNo % r.I(3, 5) == 0 ? r.R(2.0, 3.5) : r.R(0.35, 0.7);
					break;
				default:   // 1: the original
				{
					int clicks = r.I(6, 23);
					tt0 = Run(tt0, clicks, r.R(0.11, 0.17), k => k < clicks * 0.6 ? r.R(0.86, 0.95) : r.R(1.15, 1.45), throat, chest, burstGain);
					rest = r.R(0.7, 2.2);
					break;
				}
			}
			tt0 += rest;
			burstNo++;
		}
		var hp = Biquad.Hp(sr, 30);
		var o = Circular(x, hp.P);
		var rv = new Reverb(sr, 0.45, 0.6);
		o = Circular(o, v => v + rv.P(v) * 0.18, 3);
		NormRms(o, -24, -3);
		return o;
	}

	/// <summary>
	/// The giant's rattle (Dan, 2026-09-22: "very distant rattling similar to the mini stalker but deep loud
	/// and distant sounding"): the stalker's own click and throat pitched down about two octaves (throat
	/// 90-160 Hz, chest 18-30 Hz), the clicks two to three times slower and heavier, through a huge hall
	/// (long pre-delay, a 4-6 s tail) and low-passed at 600 Hz, so it reads as a hillside away. Seamless.
	/// </summary>
	public static double[] GiantRattle(Rng r, int sr, int sec)
	{
		int n = sec * sr;
		var x = new double[n];
		void Click(double at, double amp, double throat, double chest)
		{
			double tau = r.R(0.012, 0.025);   // three times longer than the stalker's
			var bp = Biquad.Bp(sr, throat * r.R(0.85, 1.15), 4);
			var lp = Biquad.Lp(sr, 500);
			int s0 = (int)(at * sr), len = (int)(0.2 * sr);
			AddEvent(x, s0, len, i =>
			{
				double tt = (double)i / sr;
				double knock = lp.P(bp.P(r.W())) * Perc(tt, 0.0015, tau) * 5.5;
				double thump = Math.Sin(TwoPi * chest * tt) * Perc(tt, 0.006, 0.06) * 1.1;
				return (Math.Tanh(knock * 1.8) + thump) * amp;
			});
		}
		double t0 = r.R(0.5, 1.5);
		while (t0 < sec)
		{
			double throat = r.R(90, 160), chest = r.R(18, 30), burstGain = r.R(0.6, 1.0);
			int clicks = r.I(5, 14);
			double gap = r.R(0.28, 0.45);   // 2-3x slower than the stalker's 0.11-0.17
			for (int k = 0; k < clicks; k++)
			{
				double amp = burstGain * (0.7 + 0.3 * r.U()) * (k < 2 ? 0.6 : 1) * (k > clicks - 3 ? 0.55 : 1);
				Click(t0, amp, throat, chest);
				t0 += gap;
				gap *= k < clicks * 0.6 ? r.R(0.88, 0.96) : r.R(1.12, 1.4);
			}
			t0 += r.R(1.5, 4.0);
		}
		var hp = Biquad.Hp(sr, 16);
		var dry = Circular(x, hp.P);
		var hall = new Hall(sr, 5.0, 0.7, 120, 2.2);
		var o = Circular(dry, v => v * 0.35 + hall.P(v) * 1.0, 3);
		LowPass(o, sr, 600);
		NormRms(o, -24, -3);
		return o;
	}

	// ------------------------------------------------------------------ rattle candidates (2026-09-22)
	// Dan: the click-and-chest rattle "sounds like horse hooves". Six different characters, none with a
	// pitched knock that could read as a hoof. Nothing here is wired into the game until he picks one;
	// each takes a RattleVar (speed / density / breath / wobble knobs) so the pick can be spun into
	// rhythm and speed variations.

	/// <summary>Knobs shared by the candidates. Speed = how fast events or pulses run (0.5 slow, 1 base, 1.6 quick);
	/// Density = events per second / how full the clusters are; Breath = spacing of the rests (bigger = longer
	/// silences); Wobble = how much the speed wanders over the loop.</summary>
	public struct RattleVar
	{
		public double Speed, Density, Breath, Wobble;
		public static RattleVar Base => new() { Speed = 1, Density = 1, Breath = 1, Wobble = 1 };
	}

	static double[] LoopOf(Rng r, int sr, int sec, Action<double[], int, int> fill, double preSec = 0.4, double xfSec = 0.35)
	{
		int n = sec * sr, pre = (int)(preSec * sr), xf = (int)(xfSec * sr);
		var x = new double[pre + n + xf];
		fill(x, pre, n);
		return MakeLoop(x, pre, n, xf);
	}

	/// <summary>A: the Grudge-style throat croak. Vocal fry (a 30-60 Hz pulse train) through a 400-700 Hz throat,
	/// its speed wavering, continuous but for breaths; no discrete clicks at all.</summary>
	public static double[] RattleCroak(Rng r, int sr, int sec, RattleVar v)
	{
		double period = sec;
		var loop = LoopOf(r, sr, sec, (x, pre, n) =>
		{
			var lar = new Larynx(r, sr) { Jitter = 0.12, Fry = 0.55, Width = 0.12 };
			var throat = new Mouth(sr) { F1 = r.R(420, 520), F2 = r.R(600, 700), F3 = 1500, G1 = 1, G2 = 0.7, G3 = 0.08, Q = 5 };
			var chest = Biquad.Lp(sr, 120);
			var hp = Biquad.Hp(sr, 40);
			double ph1 = r.R(0, TwoPi), ph2 = r.R(0, TwoPi), ph3 = r.R(0, TwoPi);
			for (int i = 0; i < x.Length; i++)
			{
				double t = (double)(i - pre) / sr;
				// The speed wanders (loop-periodic), and every so often it slows into a breath and stops.
				double wander = 0.5 + 0.5 * Math.Sin(TwoPi * 3 / period * t + ph1) * Math.Sin(TwoPi * 7 / period * t + ph2);
				double f0 = (30 + 30 * wander * v.Wobble) * v.Speed;
				double breathCycle = 0.5 + 0.5 * Math.Sin(TwoPi * Math.Round(4 / v.Breath) / period * t + ph3);
				double gate = Math.Clamp((breathCycle - 0.18) / 0.25, 0, 1);   // ~70 % voiced, the rest a breath
				double pulse = lar.P(f0 * (0.6 + 0.4 * gate));
				double voice = throat.P(pulse) * gate * gate;
				double breath = chest.P(r.W()) * (1 - gate) * 0.25 + hp.P(r.W()) * (1 - gate) * 0.02;
				x[i] = voice * 1.6 + breath;
			}
		});
		var hpO = Biquad.Hp(sr, 45);
		loop = Circular(loop, hpO.P);
		var rv = new Reverb(sr, 0.45, 0.6);
		loop = Circular(loop, s => s + rv.P(s) * 0.15, 3);
		NormRms(loop, -24, -3);
		return loop;
	}

	/// <summary>B: dry insect-like ticking. Very fast irregular tiny ticks (800-2500 Hz, 15-40 a second in ragged
	/// clusters) over a faint hiss; no low thump anywhere.</summary>
	public static double[] RattleInsect(Rng r, int sr, int sec, RattleVar v)
	{
		var x = Buf(sr, sec);
		double t = r.R(0, 0.5);
		while (t < sec)
		{
			// A cluster: 0.4-1.6 s of ticks at 15-40/s, the rate drifting inside it; then a rest.
			double len = r.R(0.4, 1.6) / v.Breath, rate0 = r.R(15, 40) * v.Density, end = t + len;
			double tt = t;
			while (tt < end)
			{
				double u = (tt - t) / len;
				double rate = rate0 * (0.6 + 0.8 * Math.Sin(Math.PI * u)) * v.Speed;
				double amp = (0.5 + 0.5 * Math.Sin(Math.PI * u)) * r.R(0.4, 1.0);
				var bp = Biquad.Bp(sr, r.R(800, 2500), 3.5);
				double tau = r.R(0.0008, 0.002);
				int s0 = (int)(tt * sr), n = (int)(0.012 * sr);
				AddEvent(x, s0, n, i => { double q = (double)i / sr; return bp.P(r.W()) * Perc(q, 0.0002, tau) * 6 * amp; });
				tt += 1.0 / Math.Max(rate, 4) * r.R(0.55, 1.45);
			}
			t = end + r.R(0.5, 2.2) * v.Breath;
		}
		// The faint hiss under it (loop-periodic swell).
		var hiss = Biquad.Bp(sr, 3000, 0.8);
		double ph = r.R(0, TwoPi);
		for (int i = 0; i < x.Length; i++)
		{
			double q = (double)i / sr;
			x[i] += hiss.P(r.W()) * 0.012 * (0.7 + 0.3 * Math.Sin(TwoPi * 2 / sec * q + ph));
		}
		var hp = Biquad.Hp(sr, 500);
		var o = Circular(x, hp.P);
		var rv = new Reverb(sr, 0.3, 0.7);
		o = Circular(o, s => s + rv.P(s) * 0.1, 3);
		NormRms(o, -26, -3);
		return o;
	}

	/// <summary>C: wet mouth clicks. Tongue and palate clicks (short 1-3 kHz pops with a wet tail) at irregular
	/// 0.15-0.6 s spacing, breath between, no rhythm.</summary>
	public static double[] RattleWetClicks(Rng r, int sr, int sec, RattleVar v)
	{
		var x = Buf(sr, sec);
		double t = r.R(0, 0.4);
		while (t < sec)
		{
			int clicks = (int)Math.Round(r.I(2, 7) * v.Density);
			for (int k = 0; k < Math.Max(1, clicks); k++)
			{
				WetClick(x, r, sr, t, r.R(0.5, 1.0));
				t += r.R(0.15, 0.6) / v.Speed;
			}
			// A breath: a soft exhale through the mouth, then a rest.
			double bt = t + r.R(0.1, 0.3);
			var bb = Biquad.Bp(sr, r.R(900, 1400), 1.2);
			double bl = r.R(0.35, 0.7);
			int s0 = (int)(bt * sr), n = (int)(bl * sr);
			AddEvent(x, s0, n, i => { double u = (double)i / n; return bb.P(r.W()) * Env(u, 0.35, 0.5) * 0.06; });
			t = bt + bl + r.R(0.6, 2.0) * v.Breath;
		}
		var hp = Biquad.Hp(sr, 200);
		var o = Circular(x, hp.P);
		var rv = new Reverb(sr, 0.35, 0.6);
		o = Circular(o, s => s + rv.P(s) * 0.14, 3);
		NormRms(o, -25, -3);
		return o;
	}

	static void WetClick(double[] x, Rng r, int sr, double at, double amp)
	{
		var pop = Biquad.Bp(sr, r.R(1000, 3000), 5);
		var wet = Biquad.Bp(sr, r.R(1800, 2600), 2.5);
		double tau = r.R(0.0015, 0.003), wetTau = r.R(0.02, 0.045);
		int s0 = (int)(at * sr), n = (int)(0.08 * sr);
		AddEvent(x, s0, n, i =>
		{
			double q = (double)i / sr;
			double p = pop.P(r.W()) * Perc(q, 0.0002, tau) * 7;
			double w = wet.P(r.W()) * Perc(q - 0.003, 0.004, wetTau) * 0.9;
			return (Math.Tanh(p) + w) * amp;
		});
	}

	/// <summary>D: bone clacks. Hollow damped knocks (300-600 Hz) at Poisson spacing (mean 0.5 s), never a gallop,
	/// with a low creak underneath.</summary>
	public static double[] RattleBone(Rng r, int sr, int sec, RattleVar v)
	{
		var x = Buf(sr, sec);
		double t = r.R(0, 0.3), mean = 0.5 / (v.Speed * v.Density);
		while (t < sec)
		{
			double amp = r.R(0.35, 1.0);
			int knocks = r.Chance(0.2) ? 2 : 1;   // the odd double, never a triplet
			for (int k = 0; k < knocks; k++)
			{
				var body = Biquad.Bp(sr, r.R(300, 600), 9);
				var body2 = Biquad.Bp(sr, r.R(700, 1100), 6);
				double tau = r.R(0.018, 0.035);
				int s0 = (int)(t * sr), n = (int)(0.12 * sr);
				int kk2 = k;
				AddEvent(x, s0, n, i =>
				{
					double q = (double)i / sr;
					return (body.P(r.W()) * 4 + body2.P(r.W()) * 1.2) * Perc(q, 0.0006, tau) * amp * (kk2 == 1 ? 0.6 : 1);
				});
				t += r.R(0.06, 0.11);
			}
			// Exponential (Poisson) waiting time: no pattern, ever; the odd long silence.
			t += -Math.Log(1 - r.U()) * mean + (r.Chance(0.08) ? r.R(0.8, 1.8) * v.Breath : 0);
		}
		// The creak under it: a slow resonant groan in the chest, loop-periodic in level.
		var creak = new Biquad(sr); var creakLp = Biquad.Lp(sr, 400);
		double ph = r.R(0, TwoPi), ph2 = r.R(0, TwoPi);
		int kk = 0;
		for (int i = 0; i < x.Length; i++)
		{
			double q = (double)i / sr;
			if ((kk++ & 63) == 0) creak.SetBp(90 + 60 * (0.5 + 0.5 * Math.Sin(TwoPi * 5 / sec * q + ph2)), 12);
			double lvl = Math.Max(0, Math.Sin(TwoPi * 2 / sec * q + ph)) * 0.35;
			x[i] += creakLp.P(creak.P(r.W())) * lvl * 0.5;
		}
		var hp = Biquad.Hp(sr, 60);
		var o = Circular(x, hp.P);
		var rv = new Reverb(sr, 0.5, 0.55);
		o = Circular(o, s => s + rv.P(s) * 0.16, 3);
		NormRms(o, -24, -3);
		return o;
	}

	/// <summary>E: the raspy ratchet. A death-rattle in the chest: noise through a resonant 150-300 Hz filter,
	/// modulated at 8-14 Hz, speeding and slowing slowly, with breaths.</summary>
	public static double[] RattleRatchet(Rng r, int sr, int sec, RattleVar v)
	{
		double period = sec;
		var loop = LoopOf(r, sr, sec, (x, pre, n) =>
		{
			var res = new Biquad(sr); var lp = Biquad.Lp(sr, 900); var chest = Biquad.Lp(sr, 140);
			double ph1 = r.R(0, TwoPi), ph2 = r.R(0, TwoPi), ph3 = r.R(0, TwoPi), mph = 0;
			int kk = 0;
			for (int i = 0; i < x.Length; i++)
			{
				double t = (double)(i - pre) / sr;
				double slow = 0.5 + 0.5 * Math.Sin(TwoPi * 2 / period * t + ph1) * Math.Sin(TwoPi * 5 / period * t + ph2);
				double rate = (8 + 6 * slow * v.Wobble) * v.Speed;
				mph += rate / sr; if (mph >= 1) mph -= 1;
				double tooth = Math.Pow(Math.Max(0, Math.Sin(TwoPi * mph)), 3) * (0.6 + 0.4 * r.U());   // the ratchet's teeth
				double breathCycle = 0.5 + 0.5 * Math.Sin(TwoPi * Math.Round(3 / v.Breath) / period * t + ph3);
				double gate = Math.Clamp((breathCycle - 0.15) / 0.3, 0, 1);
				if ((kk++ & 63) == 0) res.SetBp(150 + 150 * slow, 9);
				double rasp = lp.P(res.P(r.W())) * tooth * gate * 2.2 * v.Density;
				double breath = chest.P(r.W()) * (1 - gate) * 0.3;
				x[i] = rasp + breath;
			}
		});
		var hpO = Biquad.Hp(sr, 50);
		loop = Circular(loop, hpO.P);
		var rv = new Reverb(sr, 0.45, 0.6);
		loop = Circular(loop, s => s + rv.P(s) * 0.15, 3);
		NormRms(loop, -24, -3);
		return loop;
	}

	/// <summary>F: the croak with occasional wet clicks laid over it.</summary>
	public static double[] RattleCroakClicks(Rng r, int sr, int sec, RattleVar v)
	{
		var croak = RattleCroak(r, sr, sec, v);
		var clicks = Buf(sr, sec);
		double t = r.R(0.5, 1.5);
		while (t < sec)
		{
			int cnt = r.I(1, 4);
			for (int k = 0; k < cnt; k++) { WetClick(clicks, r, sr, t, r.R(0.3, 0.7)); t += r.R(0.18, 0.5) / v.Speed; }
			t += r.R(1.5, 4.0) * v.Breath;
		}
		var hp = Biquad.Hp(sr, 200);
		var c = Circular(clicks, hp.P);
		NormRms(c, -31, -6);
		var o = new double[croak.Length];
		for (int i = 0; i < o.Length; i++) o[i] = croak[i] + c[i];
		NormRms(o, -24, -3);
		return o;
	}

	/// <summary>
	/// The giant's voice from far off: a long low moan at 24-34 Hz, the mouth barely shaping it,
	/// swelling and sagging over five or six seconds through a huge hall. Almost all of it is
	/// below 150 Hz; small speakers hear the 100-250 Hz throat, big ones feel the rest.
	/// </summary>
	public static double[] GiantMoan(Rng r, int sr)
	{
		double dur = r.R(5.0, 6.5);
		var x = Buf(sr, dur);
		var larA = new Larynx(r, sr) { Jitter = 0.025, Fry = 0.2, Width = 0.32 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.03, Fry = 0.25, Width = 0.3 };
		var mouth = new Mouth(sr) { F1 = 180, F2 = 420, F3 = 900, G1 = 1, G2 = 0.5, G3 = 0.15, Q = 4 };
		double f0 = r.R(24, 34);
		var drift = new Smooth(r, dur, 0.6);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double f = f0 * (1 + 0.18 * (drift.At(t) - 0.5)) * (1 - 0.15 * Math.Max(0, u - 0.65) / 0.35);
			double env = Env(u, 0.35, 0.4);
			double open = 0.4 + 0.6 * Math.Sin(Math.PI * Math.Min(1, u * 1.1));
			mouth.F1 = 150 + 120 * open; mouth.F2 = 380 + 160 * open;
			double src = larA.P(f) + larB.P(f * 1.012) * 0.7;
			x[i] = Math.Tanh(mouth.P(src) * 3.0) * env + Math.Sin(TwoPi * f * t) * 0.35 * env;
		}
		LowPass(x, sr, 900);
		var hall = new Hall(sr, 3.2, 0.6, 45, 1.5);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.4 + hall.P(x[i]) * 1.0;
		return FinishOneShot(o, sr, -3, 900);
	}

	/// <summary>
	/// The giant's bellow: a roar an octave under the stalker's (40 to 75 Hz), the size of a hillside,
	/// five seconds long with the break at the end, far off through the trees.
	/// </summary>
	public static double[] GiantBellow(Rng r, int sr)
	{
		double dur = r.R(4.4, 5.4);
		var x = Buf(sr, dur);
		var larA = new Larynx(r, sr) { Jitter = 0.03, Fry = 0.15, Width = 0.3 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.04, Fry = 0.1, Width = 0.28 };
		var larC = new Larynx(r.Fork(), sr) { Jitter = 0.05, Fry = 0.2, Width = 0.26 };
		var mouth = new Mouth(sr) { G2 = 0.7, G3 = 0.25, Q = 5 };
		double lo = r.R(38, 44), hi = r.R(68, 80), peakAt = r.R(0.4, 0.55), crack = r.R(0.7, 0.82);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double rise = u < peakAt ? Math.Pow(u / peakAt, 0.75) : 1 - 0.4 * Math.Pow((u - peakAt) / (1 - peakAt), 1.3);
			double f = lo + (hi - lo) * rise;
			if (u > crack) f *= 1 - 0.2 * Math.Min(1, (u - crack) / 0.1);
			double open = Math.Pow(Math.Min(1, rise * 1.15), 0.8);
			mouth.F1 = 220 + 420 * open; mouth.F2 = 600 + 500 * open; mouth.F3 = 1600 + 300 * open;
			double src = larA.P(f) + larB.P(f * 1.02) * 0.6 + larC.P(f * 0.5) * 0.5;
			double env = Env(u, 0.18, 0.28);
			x[i] = Math.Tanh(mouth.P(src) * 4.0) * env + Math.Sin(TwoPi * f * 0.5 * t) * 0.5 * env * open;
		}
		LowPass(x, sr, 1300);
		var hall = new Hall(sr, 3.0, 0.55, 40, 1.5);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.35 + hall.P(x[i]) * 1.1;
		return FinishOneShot(o, sr, -3, 800);
	}

	/// <summary>
	/// A short snarl or huff right behind the player: a hard breath with fry at the start, the mouth
	/// snapping open, over in under a second. For the stalker's close moments. Takes 1-5 are the
	/// original single huffs; 6 and 7 are the same voice as a double huff (huff-huff); 8 is the same
	/// voice opening slower and holding a touch longer. Nothing else changes (Dan, 2026-09-22:
	/// "no new sounds, just different beats or rhythms").
	/// </summary>
	public static double[] Snarl(Rng r, int sr, int take = 1)
	{
		bool twice = take is 6 or 7; bool slow = take == 8;
		double dur = twice ? r.R(0.75, 0.9) : slow ? r.R(0.7, 0.9) : r.R(0.5, 0.9);
		var x = Buf(sr, dur);
		var lar = new Larynx(r, sr) { Jitter = 0.1, Fry = r.R(0.4, 0.7), Width = 0.2 };
		var mouth = new Mouth(sr) { F1 = 520, F2 = 1250, F3 = 2600, G2 = 0.6, G3 = 0.3, Q = 5 };
		var breath1 = Biquad.Bp(sr, 1100, 1.2); var breath2 = Biquad.Lp(sr, 3000);
		double f0 = r.R(38, 60);
		double second = twice ? dur * r.R(0.42, 0.5) : double.PositiveInfinity;   // when the second huff lands
		double openTau = slow ? 0.13 : 0.06;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double tl = t >= second ? t - second : t;   // time since the current huff began
			double f = f0 * (1 + 0.4 * Math.Exp(-tl / 0.12)) * (1 - 0.3 * u);
			double open = 1 - Math.Exp(-tl / openTau);
			mouth.F1 = 380 + 300 * open; mouth.F2 = 900 + 500 * open;
			double env = twice
				? (Perc(t, 0.03, second * 0.5) * (t < second ? 1 : 0) + Perc(t - second, 0.03, (dur - second) * 0.4)) * Env(u, 0.02, 0.3)
				: Perc(t, slow ? 0.08 : 0.03, dur * (slow ? 0.45 : 0.35)) * Env(u, 0.02, 0.4);
			double breath = breath2.P(breath1.P(r.W())) * 0.5 * (Perc(t, 0.01, 0.09) + (twice ? Perc(t - second, 0.01, 0.09) : 0));
			x[i] = Math.Tanh(mouth.P(lar.P(f)) * 3.5 + breath * 1.5) * env;
		}
		LowPass(x, sr, 3600);
		return FinishOneShot(Room(x, sr, 0.15, 0.45, 0.55), sr, -3, 60);
	}
}
