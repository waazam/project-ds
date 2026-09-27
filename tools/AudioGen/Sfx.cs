namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>One-shots: birds, footsteps, cloth. Every result goes through Dsp.FinishOneShot (zero ends, -3 dBFS peak).</summary>
public static class Sfx
{
	// ---------------------------------------------------------------- birds

	static double[] Space(double[] dry, int sr, double mix, double size = 0.75, double damp = 0.45, double lpHz = 0)
	{
		if (lpHz > 0) LowPass(dry, sr, lpHz);
		var rv = new Reverb(sr, size, damp);
		var o = new double[dry.Length];
		for (int i = 0; i < dry.Length; i++) o[i] = dry[i] + rv.P(dry[i]) * mix;
		return o;
	}

	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	public static double[] Bird(int index, Rng r, int sr) => index switch
	{
		1 => ChirpSequence(r, sr),
		2 => TwoNoteWhistle(r, sr),
		3 => Warble(r, sr),
		4 => Woodpecker(r, sr),
		5 => Caw(r, sr),
		6 => Coo(r, sr),
		7 => DistantScreech(r, sr),
		_ => SongPhrase(r, sr),
	};

	/// <summary>Quick run of descending chirps.</summary>
	static double[] ChirpSequence(Rng r, int sr)
	{
		var x = Buf(sr, 1.8);
		int k = r.I(5, 8);
		double t = 0.02, top = r.R(4000, 4600);
		for (int i = 0; i < k; i++)
		{
			double d = r.R(0.045, 0.07), f0 = top * (1 - 0.03 * i) * r.R(0.97, 1.03), f1 = f0 * r.R(0.55, 0.68);
			double a = (i == 0 ? 0.6 : 1.0) * r.R(0.75, 1.0) * (1 - 0.05 * i);
			AddTone(x, sr, t, d, u => f0 * Math.Pow(f1 / f0, Math.Pow(u, 0.7)), u => a * Env(u, 0.12, 0.45), new[] { 1.0, 0.12, 0.03 });
			t += d + r.R(0.055, 0.085);
		}
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 60);
	}

	/// <summary>Clear two-note "fee-bee" whistle, slight vibrato.</summary>
	static double[] TwoNoteWhistle(Rng r, int sr)
	{
		var x = Buf(sr, 1.9);
		double f1 = r.R(3250, 3500), f2 = f1 * r.R(0.84, 0.88);
		double d1 = r.R(0.28, 0.34), d2 = r.R(0.36, 0.44), gap = r.R(0.05, 0.08);
		AddTone(x, sr, 0.02, d1, u => f1 * (1 - 0.015 * u) * (1 + 0.004 * Math.Sin(TwoPi * 7 * u * d1)), u => 0.9 * Env(u, 0.18, 0.3), new[] { 1.0, 0.04 });
		AddTone(x, sr, 0.02 + d1 + gap, d2, u => f2 * (1 - 0.02 * u) * (1 + 0.005 * Math.Sin(TwoPi * 6 * u * d2)), u => Env(u, 0.12, 0.4), new[] { 1.0, 0.04 });
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	/// <summary>Fast warbling trill with a rise-and-fall contour.</summary>
	static double[] Warble(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		double d = r.R(1.1, 1.4), c = r.R(3000, 3500), rate = r.R(18, 24), depth = r.R(500, 750);
		AddTone(x, sr, 0.02, d, u =>
		{
			double contour = c * (1 + 0.18 * Math.Sin(Math.PI * u) - 0.08 * u);
			return contour + depth * Math.Sin(TwoPi * rate * u * d);
		}, u =>
		{
			double s = Math.Cos(Math.PI * rate * u * d);
			return Env(u, 0.12, 0.3) * (0.35 + 0.65 * s * s);
		}, new[] { 1.0, 0.12 });
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	/// <summary>Woodpecker drum: a quick burst of hollow knocks on a trunk, fading.</summary>
	static double[] Woodpecker(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		int k = r.I(13, 19);
		double t = 0.01, per = 1 / r.R(16, 19);
		double m1 = r.R(850, 1050), m2 = r.R(420, 520), m3 = r.R(1600, 1900);
		var nb = Biquad.Bp(sr, 1400, 1.5);
		for (int i = 0; i < k; i++)
		{
			double a = (1 - 0.45 * i / k) * r.R(0.85, 1.0);
			int s0 = (int)(t * sr);
			for (int j = 0; j < (int)(0.06 * sr) && s0 + j < x.Length; j++)
			{
				double tt = (double)j / sr;
				double v = nb.P(r.W() * Perc(tt, 0.0003, 0.0025)) * 1.4
					+ 0.8 * Math.Sin(TwoPi * m1 * tt) * Perc(tt, 0.0004, 0.009)
					+ 0.6 * Math.Sin(TwoPi * m2 * tt) * Perc(tt, 0.0004, 0.014)
					+ 0.25 * Math.Sin(TwoPi * m3 * tt) * Perc(tt, 0.0003, 0.005);
				x[s0 + j] += v * a;
			}
			t += per * (1 - 0.012 * i) * r.R(0.97, 1.03);
		}
		return FinishOneShot(Space(x, sr, 0.5, 0.85, 0.5, 5000), sr, -3, 100);
	}

	/// <summary>Crow-like harsh caws: rough harmonic-rich source through beak/throat formants.</summary>
	static double[] Caw(Rng r, int sr)
	{
		var src = Buf(sr, 2.2);
		int k = r.I(2, 4);
		double t = 0.02;
		var jit = new Smooth(r, 3, 0.012);
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.28, 0.4), f0 = r.R(480, 560) * (1 - 0.04 * c), a = r.R(0.85, 1.0);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len && s0 + i < src.Length; i++)
			{
				double u = (double)i / len, tt = (double)(s0 + i) / sr;
				double f = f0 * (1 + 0.12 * Math.Sin(Math.PI * Math.Min(1, u * 1.6)) - 0.18 * u) * (1 + 0.05 * (jit.At(tt) - 0.5));
				ph += TwoPi * f / sr;
				double v = 0;
				for (int h = 1; h <= 14; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / Math.Pow(h, 0.8);
				double rough = 0.7 + 0.3 * Math.Sin(TwoPi * 73 * tt + 3 * jit.At(tt * 3));
				src[s0 + i] += (v * 0.35 + r.W() * 0.5) * rough * a * Env(u, 0.08, 0.45);
			}
			t += d + r.R(0.18, 0.28);
		}
		var f1 = Biquad.Bp(sr, 1150, 2.5); var f2 = Biquad.Bp(sr, 1900, 3.5); var f3 = Biquad.Bp(sr, 3000, 3);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = f1.P(src[i]) + 0.7 * f2.P(src[i]) + 0.25 * f3.P(src[i]);
		return FinishOneShot(Space(x, sr, 0.45, 0.85, 0.5, 4200), sr, -3, 100);
	}

	/// <summary>Dove-like soft low "coo-OO-oo-oo".</summary>
	static double[] Coo(Rng r, int sr)
	{
		var x = Buf(sr, 2.45);
		double b = r.R(470, 530);
		(double d, double f0, double f1, double a, double gap)[] notes =
		{
			(0.24, b * 1.02, b * 1.1, 0.55, 0.09),
			(0.55, b * 1.12, b * 1.02, 1.0, 0.28),
			(0.3, b * 0.98, b * 0.95, 0.7, 0.1),
			(0.32, b * 0.96, b * 0.93, 0.6, 0),
		};
		double t = 0.03;
		foreach (var nt in notes)
		{
			double d = nt.d * r.R(0.92, 1.08), f0 = nt.f0, f1 = nt.f1, a = nt.a;
			AddTone(x, sr, t, d, u => (f0 + (f1 - f0) * Math.Sin(0.5 * Math.PI * u)) * (1 + 0.006 * Math.Sin(TwoPi * 5 * u * d)),
				u => a * Env(u, 0.3, 0.45), new[] { 1.0, 0.22, 0.07, 0.02 });
			t += d + nt.gap;
		}
		// A touch of breathy noise following the voice.
		var n = Biquad.Bp(sr, 700, 1.2);
		var env = x.Select(Math.Abs).ToArray();
		LowPass(env, sr, 20);
		for (int i = 0; i < x.Length; i++) x[i] += n.P(r.W()) * env[i] * 0.35;
		LowPass(x, sr, 1800);
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 100);
	}

	/// <summary>Distant jay-like screech: harsh noisy tone, dulled by distance and washed in reverb.</summary>
	static double[] DistantScreech(Rng r, int sr)
	{
		var src = Buf(sr, 2.4);
		int k = r.I(1, 3);
		double t = 0.03;
		for (int c = 0; c < k; c++)
		{
			double d = r.R(0.3, 0.42), f0 = r.R(2000, 2300), a = r.R(0.8, 1);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			double ph = 0;
			for (int i = 0; i < len; i++)
			{
				double u = (double)i / len;
				double f = f0 * (1 + 0.2 * Math.Sin(Math.PI * Math.Min(1, u * 1.4)) - 0.12 * u) + 60 * r.W();
				ph += TwoPi * f / sr;
				double v = Math.Sin(ph) + 0.55 * Math.Sin(2 * ph) + 0.3 * Math.Sin(3 * ph);
				v = Math.Tanh(1.6 * v) + 0.45 * r.W();
				src[s0 + i] += v * a * Env(u, 0.1, 0.5);
			}
			t += d + r.R(0.15, 0.25);
		}
		var bp = Biquad.Bp(sr, 2300, 1.2);
		for (int i = 0; i < src.Length; i++) src[i] = bp.P(src[i]);
		LowPass(src, sr, 2800); LowPass(src, sr, 3200);
		// Distance: more reverb than dry.
		var rv = new Reverb(sr, 0.9, 0.55);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = src[i] * 0.55 + rv.P(src[i]) * 0.9;
		return FinishOneShot(x, sr, -3, 150);
	}

	/// <summary>Robin-like phrase of varied syllables (sweeps, hooks).</summary>
	static double[] SongPhrase(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		int k = r.I(6, 9);
		double t = 0.02;
		for (int i = 0; i < k; i++)
		{
			double d = r.R(0.07, 0.17), f0 = r.R(2200, 3600), a = r.R(0.6, 1.0);
			int shape = r.I(0, 4);
			double f1 = shape switch { 0 => f0 * r.R(1.2, 1.45), 1 => f0 * r.R(0.65, 0.8), _ => f0 * r.R(0.95, 1.05) };
			double hook = r.R(0.2, 0.35);
			Func<double, double> fr = shape switch
			{
				2 => u => f0 + (f1 - f0) * u + f0 * hook * Math.Sin(Math.PI * u), // arch
				3 => u => f0 - f0 * hook * 0.8 * Math.Sin(Math.PI * u), // dip
				_ => u => f0 * Math.Pow(f1 / f0, u),
			};
			AddTone(x, sr, t, d, fr, u => a * Env(u, 0.15, 0.4), new[] { 1.0, 0.1, 0.02 });
			t += d + r.R(0.035, 0.1);
			if (t > 1.9) break;
		}
		return FinishOneShot(Space(x, sr, 0.35), sr, -3, 80);
	}

	// ---------------------------------------------------------------- footsteps

	/// <summary>Dirt / leaf litter: low heel thump + crunchy grain cluster + leaf swish. Some steps roll heel-to-toe.</summary>
	public static double[] StepDirt(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		double thumpF = r.R(95, 130), heavy = r.R(0.7, 1.0);
		bool roll = r.Chance(0.5);
		double[] contacts = roll ? new[] { 0.0, r.R(0.035, 0.07) } : new[] { 0.0 };
		var lpn = Biquad.Lp(sr, 260);
		for (int c = 0; c < contacts.Length; c++)
		{
			int s0 = (int)(contacts[c] * sr);
			double a = c == 0 ? heavy : heavy * r.R(0.35, 0.55), ph = 0;
			for (int i = 0; i + s0 < x.Length && i < 0.15 * sr; i++)
			{
				double tt = (double)i / sr, f = thumpF * (0.55 + 0.45 * Math.Exp(-tt / 0.02));
				ph += TwoPi * f / sr;
				x[s0 + i] += a * (Math.Sin(ph) * Perc(tt, 0.002, 0.025) * 0.2 + lpn.P(r.W()) * Perc(tt, 0.001, 0.018) * 0.7);
			}
		}
		// Crunch: short bright grains, dense at first then thinning.
		var ghp = Biquad.Hp(sr, 1700); var glp = Biquad.Lp(sr, r.R(5500, 8000));
		var gmid = Biquad.Bp(sr, r.R(900, 1400), 1.2);
		double win = r.R(0.14, 0.24);
		int ng = r.I(14, 34);
		var grains = new List<(int s, double tau, double a, bool mid)>();
		for (int g = 0; g < ng; g++)
		{
			double gt = win * Math.Pow(r.U(), 1.8) + r.R(0.0, 0.01);
			grains.Add(((int)(gt * sr), r.LogR(0.0008, 0.006), r.R(0.2, 1.0) * (1 - 0.6 * gt / win), r.Chance(0.3)));
		}
		var hi = new double[x.Length]; var mid = new double[x.Length];
		for (int i = 0; i < x.Length; i++) { double w = r.W(); hi[i] = glp.P(ghp.P(w)); mid[i] = gmid.P(r.W()); }
		double crunch = r.R(0.5, 0.9);
		foreach (var g in grains)
			for (int i = 0; i < 0.04 * sr && g.s + i < x.Length; i++)
			{
				double tt = (double)i / sr, e = Math.Exp(-tt / g.tau) - Math.Exp(-tt / 0.0002);
				x[g.s + i] += e * g.a * crunch * (g.mid ? mid[g.s + i] * 1.5 : hi[g.s + i]) * 1.8;
			}
		// Leaf swish under it.
		var sw = Biquad.Bp(sr, r.R(2000, 3200), 0.8);
		double swDur = r.R(0.08, 0.16);
		for (int i = 0; i < swDur * sr; i++) x[i] += sw.P(r.W()) * 0.12 * Env((double)i / (swDur * sr), 0.2, 0.7);
		HighPass(x, sr, 110);   // a footstep, not a kick drum
		return FinishOneShot(x, sr, -3, 40, r.R(0.24, 0.3));
	}

	// A boot on planks: almost all damped noise, no ringing partials (sine
	// modes read as hand drums). Heel strike = dull weight + a low-Q board knock
	// + a dry click; the toe lands softer 60-110 ms later; a little grit scuffs
	// between them. Even-numbered variants add a faint board creak.
	/// <summary>
	/// A body landing hard on earth (Act 6, the fall off the last landing; Dan, 2026-09-22: a proper thump).
	/// A soft-fronted sub thud under 80 Hz, a dull 150-300 Hz body of the fall, a short crunch of ground
	/// under it (noise, 400-900 Hz, dead in 40 ms) and a little settle after; nothing above 2 kHz. About
	/// half a second. Played flat at the ear, hot, at the moment the screen is black.
	/// </summary>
	public static double[] BodyThump(Rng r, int sr)
	{
		var x = Buf(sr, 0.55);
		var sub1 = Biquad.Lp(sr, r.R(40, 80)); var sub2 = Biquad.Lp(sr, 120);
		var body = Biquad.Bp(sr, r.R(150, 300), 0.8);
		var crunch = Biquad.Bp(sr, r.R(400, 900), 1.2);
		var settle = Biquad.Lp(sr, r.R(90, 140));
		double settleAt = r.R(0.14, 0.2);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] = sub2.P(sub1.P(r.W())) * Perc(t, 0.012, 0.09) * 4.0
				+ body.P(r.W()) * Perc(t, 0.004, 0.035) * 2.0
				+ crunch.P(r.W()) * Perc(t, 0.002, 0.012) * 0.9
				+ settle.P(r.W()) * Perc(t - settleAt, 0.01, 0.05) * 1.2;
		}
		LowPass(x, sr, 2000);
		return FinishOneShot(x, sr, -3, 60);
	}

	/// <summary>
	/// A heavy pound on the outside of a log wall (Act 5, the cabin; Dan, 2026-09-22: dramatic and
	/// scary, not a knuckle): a fist or a heel on the logs. A sub thump under 90 Hz with a hard
	/// 250-450 Hz body, the whole wall answering with a short low shudder, nothing above 1.5 kHz,
	/// 0.25 s. Hot (peak -1 dB): the beat plays three of these in a row.
	/// </summary>
	public static double[] WallPound(Rng r, int sr)
	{
		var x = Buf(sr, 0.34);
		var sub1 = Biquad.Lp(sr, r.R(50, 90)); var sub2 = Biquad.Lp(sr, 140);
		var body = Biquad.Bp(sr, r.R(250, 450), 0.9);
		var shudder = Biquad.Lp(sr, r.R(160, 220));
		var knock = Biquad.Bp(sr, r.R(600, 900), 1.4);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			x[i] = sub2.P(sub1.P(r.W())) * Perc(t, 0.004, 0.06) * 4.2
				+ body.P(r.W()) * Perc(t, 0.001, 0.02) * 2.4
				+ shudder.P(r.W()) * Perc(t - 0.02, 0.01, 0.09) * 1.1
				+ knock.P(r.W()) * Perc(t, 0.0004, 0.005) * 0.5;
		}
		LowPass(x, sr, 1500);
		return FinishOneShot(x, sr, -3, 50);   // Verify holds every one-shot at -3 dB; the beat plays it hot
	}

	/// <summary>
	/// The cabin's plank door slammed shut by nothing (Act 5; Dan, 2026-09-22: louder and dramatic):
	/// <see cref="DoorSlam"/>'s plank slab and frame chatter with a far heavier slab under it, and the
	/// whole log wall rattling for a moment after (damped low knocks thinning out over 0.6 s), plus a
	/// short low rumble tail. About a second. Hot (peak -1 dB).
	/// </summary>
	public static double[] CabinSlam(Rng r, int sr)
	{
		var x = Buf(sr, 1.05);
		var slab1 = Biquad.Lp(sr, r.R(60, 90)); var slab2 = Biquad.Lp(sr, 150);
		var plank1 = Biquad.Lp(sr, r.R(100, 130)); var plank2 = Biquad.Lp(sr, 200);
		var body = Biquad.Bp(sr, r.R(260, 380), 0.8);
		var chatter = Biquad.Bp(sr, r.R(500, 760), 1.3);
		var wall = Biquad.Bp(sr, r.R(170, 260), 1.1);
		var latch = Biquad.Bp(sr, r.R(1400, 2000), 2.0);
		var rumble = Biquad.Lp(sr, 110);
		double[] chatterAt = { 0.035, 0.07, 0.115, 0.17, 0.24 };
		double[] chatterGain = { 0.7, 0.55, 0.4, 0.28, 0.16 };
		// The wall's rattle: eight damped knocks, thinning, a little irregular.
		int rattles = 8;
		var rattleAt = new double[rattles]; var rattleGain = new double[rattles];
		double ra = 0.05;
		for (int k = 0; k < rattles; k++) { rattleAt[k] = ra; rattleGain[k] = 0.9 * Math.Pow(0.72, k); ra += r.R(0.05, 0.09); }
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double v = slab2.P(slab1.P(r.W())) * Perc(t, 0.008, 0.16) * 4.5
				+ plank2.P(plank1.P(r.W())) * Perc(t, 0.005, 0.09) * 2.6
				+ body.P(r.W()) * Perc(t, 0.002, 0.03) * 1.8
				+ rumble.P(r.W()) * Perc(t - 0.05, 0.05, 0.35) * 1.2;
			double ch = chatter.P(r.W());
			for (int c = 0; c < chatterAt.Length; c++)
				if (t >= chatterAt[c]) v += ch * Perc(t - chatterAt[c], 0.0008, 0.012) * chatterGain[c];
			double w = wall.P(r.W());
			for (int k = 0; k < rattles; k++)
				if (t >= rattleAt[k]) v += w * Perc(t - rattleAt[k], 0.001, 0.02) * rattleGain[k];
			v += latch.P(r.W()) * Perc(t - 0.012, 0.0003, 0.003) * 0.3;
			x[i] = v;
		}
		LowPass(x, sr, 2500);
		return FinishOneShot(Space(x, sr, 0.3), sr, -3, 80);   // same: -3 in the file, +18 dB in the room
	}

	/// <summary>
	/// A heavy plank door slammed into its frame (the bunker's rooms, Act 10): a soft-fronted
	/// slab thump under 130 Hz, the plank body knocking around 300 Hz, the frame chattering
	/// for a quarter second after, and one small latch click. Nothing above 2.5 kHz and no
	/// sharp transient body in the 100 Hz-1 kHz band, so it never reads as a gunshot.
	/// </summary>
	public static double[] DoorSlam(Rng r, int sr)
	{
		var x = Buf(sr, 0.8);
		var slab1 = Biquad.Lp(sr, r.R(95, 130)); var slab2 = Biquad.Lp(sr, 180);
		var body = Biquad.Bp(sr, r.R(260, 360), 0.8);
		var chatter = Biquad.Bp(sr, r.R(520, 760), 1.3);
		var latch = Biquad.Bp(sr, r.R(1500, 2200), 2.0);
		double[] chatterAt = { 0.035, 0.07, 0.115, 0.17, 0.24 };
		double[] chatterGain = { 0.7, 0.55, 0.4, 0.28, 0.16 };
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double v = slab2.P(slab1.P(r.W())) * Perc(t, 0.006, 0.09) * 3.2
				+ body.P(r.W()) * Perc(t, 0.002, 0.03) * 1.5;
			double ch = chatter.P(r.W());
			for (int c = 0; c < chatterAt.Length; c++)
				if (t >= chatterAt[c]) v += ch * Perc(t - chatterAt[c], 0.0008, 0.012) * chatterGain[c];
			v += latch.P(r.W()) * Perc(t - 0.012, 0.0003, 0.003) * 0.35;
			x[i] = v;
		}
		LowPass(x, sr, 2500);
		return FinishOneShot(Space(x, sr, 0.25), sr, -3, 60);
	}

	/// <summary>
	/// A knuckle on the outside of a log wall, heard from inside (Act 5, the cabin): one dull, dead
	/// knock, no ring and no pitch (a ringing knock reads as a drum): the wood's thud under 200 Hz,
	/// a short 300-500 Hz body that dies in 15 ms, and the faint tick of the knuckle itself.
	/// Wall-muffled: nothing above 1.2 kHz. The beat plays these in a pattern, so each is one knock.
	/// </summary>
	public static double[] WallKnock(Rng r, int sr)
	{
		var x = Buf(sr, 0.3);
		var thud1 = Biquad.Lp(sr, r.R(140, 190)); var thud2 = Biquad.Lp(sr, 260);
		var body = Biquad.Bp(sr, r.R(320, 480), 0.9);
		var tick = Biquad.Bp(sr, r.R(800, 1100), 1.6);
		for (int i = 0; i < 0.16 * sr; i++)
		{
			double t = (double)i / sr;
			x[i] = thud2.P(thud1.P(r.W())) * Perc(t, 0.003, 0.03) * 3.0
				+ body.P(r.W()) * Perc(t, 0.0008, 0.012) * 1.6
				+ tick.P(r.W()) * Perc(t, 0.0003, 0.004) * 0.5;
		}
		LowPass(x, sr, 1200);
		return FinishOneShot(x, sr, -3, 40);
	}

	public static double[] StepWood(Rng r, int sr, bool creak)
	{
		var x = Buf(sr, creak ? 0.5 : 0.38);
		double knockF = r.R(330, 480), clickF = r.R(1400, 2200);
		double[] contacts = { 0.0, r.R(0.06, 0.11) };
		double[] gains = { 1.0, r.R(0.35, 0.5) };
		for (int c = 0; c < contacts.Length; c++)
		{
			var weight = Biquad.Lp(sr, 160);
			var knock = Biquad.Bp(sr, knockF * r.R(0.95, 1.05), 1.1);
			var click = Biquad.Bp(sr, clickF * r.R(0.9, 1.1), 1.4);
			int s0 = (int)(contacts[c] * sr);
			double a = gains[c];
			for (int i = 0; i + s0 < x.Length && i < 0.09 * sr; i++)
			{
				double tt = (double)i / sr;
				double v = weight.P(r.W()) * Perc(tt, 0.002, 0.018) * 2.2
					+ knock.P(r.W()) * Perc(tt, 0.0006, 0.014) * 2.4
					+ click.P(r.W()) * Perc(tt, 0.0002, 0.004) * 1.2;
				x[s0 + i] += a * v;
			}
		}
		// Grit under the sole.
		var grit = Biquad.Hp(sr, 2600);
		int g0 = (int)(r.R(0.01, 0.03) * sr), gLen = (int)(r.R(0.05, 0.09) * sr);
		for (int i = 0; i < gLen && g0 + i < x.Length; i++)
			x[g0 + i] += grit.P(r.W()) * 0.1 * Env((double)i / gLen, 0.3, 0.6) * (r.Chance(0.15) ? 2.5 : 1.0);
		if (creak)
		{
			var c1 = Biquad.Bp(sr, r.R(520, 700), 9); var c2 = Biquad.Bp(sr, r.R(1100, 1400), 7);
			double cs = r.R(0.08, 0.14), cd = r.R(0.12, 0.2), rate0 = r.R(55, 80), rate1 = rate0 * r.R(1.2, 1.5);
			double ph = 0;
			for (int i = 0; i < cd * sr; i++)
			{
				double u = i / (cd * sr);
				ph += (rate0 + (rate1 - rate0) * u) / sr;
				double imp = 0;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0); }
				double v = (c1.P(imp) + 0.5 * c2.P(imp)) * Env(u, 0.3, 0.5);
				int idx = (int)(cs * sr) + i;
				if (idx < x.Length) x[idx] += v * 0.5;
			}
		}
		return FinishOneShot(x, sr, -3, 30, 0.24);
	}

	/// <summary>
	/// Stone settling onto stone (Act 6: the newel post's cap seating back onto its broken neck): a
	/// short, low, dry grind of grains catching and slipping under the weight, a heavy dull seat, and
	/// one last small shift. Built from individual contacts, everything under ~1.4 kHz: no hiss, no
	/// bed, no ring, no reverb.
	/// </summary>
	public static double[] NewelSeat(Rng r, int sr)
	{
		var x = Buf(sr, 1.1);
		const double grindLen = 0.42, seat = 0.44;
		var catches = new Smooth(r, 1.2, 0.045);
		// the grind: stick-slip grain contacts, ~100 a second, swelling where it catches
		for (double t = 0.0; t < grindLen; t += r.R(0.004, 0.016))
		{
			double u = t / grindLen;
			double level = Env(u, 0.25, 0.3) * (0.35 + 0.65 * Math.Pow(catches.At(t), 1.5));
			var bp = Biquad.Bp(sr, r.R(180, 520), 1.1);
			var lp = Biquad.Lp(sr, 900);
			double tau = r.R(0.003, 0.009), g = level * r.R(0.4, 1.0);
			int s0 = (int)(t * sr);
			for (int i = 0; i < 0.04 * sr && s0 + i < x.Length; i++)
				x[s0 + i] += lp.P(bp.P(r.W())) * Perc((double)i / sr, 0.0008, tau) * g * 5.0;
		}
		// the weight under it: a very low rumble riding the grind
		var w1 = Biquad.Lp(sr, 90); var w2 = Biquad.Lp(sr, 110);
		int wn = (int)((grindLen + 0.05) * sr);
		for (int i = 0; i < wn; i++)
		{
			double tt = (double)i / sr;
			x[i] += w2.P(w1.P(r.W())) * Env((double)i / wn, 0.3, 0.4) * (0.5 + 0.5 * catches.At(tt)) * 4.0;
		}
		// the seat: a heavy, dull knock, noise only
		{
			var l1 = Biquad.Lp(sr, 140); var l2 = Biquad.Lp(sr, 170); var b = Biquad.Bp(sr, 380, 0.9);
			int s0 = (int)(seat * sr);
			for (int i = 0; i < 0.35 * sr && s0 + i < x.Length; i++)
			{
				double tt = (double)i / sr;
				x[s0 + i] += l2.P(l1.P(r.W())) * Perc(tt, 0.002, 0.05) * 16 + b.P(r.W()) * Perc(tt, 0.001, 0.014) * 2.5;
			}
		}
		// one last small shift as it settles
		{
			var bp = Biquad.Bp(sr, r.R(250, 400), 1.0);
			int s0 = (int)((seat + r.R(0.13, 0.19)) * sr);
			for (int i = 0; i < 0.05 * sr && s0 + i < x.Length; i++)
				x[s0 + i] += bp.P(r.W()) * Perc((double)i / sr, 0.001, 0.01) * 1.2;
		}
		LowPass(x, sr, 1400);
		HighPass(x, sr, 30);
		return FinishOneShot(x, sr, -3, 60);
	}

	// A boot on old concrete/stone steps: a hard, dry heel tap with almost no
	// body (stone doesn't resonate like planks), a softer toe tap, and sandy
	// grit ground under the sole. Occasionally a small crumb skitters off.
	public static double[] StepStone(Rng r, int sr)
	{
		var x = Buf(sr, 0.4);
		double tapF = r.R(700, 1150), weight0 = r.R(0.7, 1.0);
		double[] contacts = { 0.0, r.R(0.05, 0.09) };
		double[] gains = { 1.0, r.R(0.3, 0.45) };
		for (int c = 0; c < contacts.Length; c++)
		{
			var weight = Biquad.Lp(sr, 140);
			var tap = Biquad.Bp(sr, tapF * r.R(0.9, 1.1), 0.8);
			var tick = Biquad.Hp(sr, 3000);
			int s0 = (int)(contacts[c] * sr);
			for (int i = 0; i + s0 < x.Length && i < 0.06 * sr; i++)
			{
				double tt = (double)i / sr;
				double v = weight.P(r.W()) * Perc(tt, 0.001, 0.016) * 3.4 * weight0
					+ tap.P(r.W()) * Perc(tt, 0.0003, 0.007) * 2.2
					+ tick.P(r.W()) * Perc(tt, 0.0001, 0.0025) * 1.0;
				x[s0 + i] += gains[c] * v;
			}
		}
		// Grit: sand between sole and stone, a short scrape of bright grains.
		var grit = Biquad.Bp(sr, r.R(3000, 4500), 0.7);
		int g0 = (int)(r.R(0.005, 0.02) * sr), gLen = (int)(r.R(0.07, 0.13) * sr);
		for (int i = 0; i < gLen && g0 + i < x.Length; i++)
			x[g0 + i] += grit.P(r.W()) * 0.09 * Env((double)i / gLen, 0.2, 0.6) * (r.Chance(0.08) ? 3.0 : 1.0);
		if (r.Chance(0.35))
		{
			// A crumb kicked loose: two or three tiny ticks trailing off.
			double t0 = r.R(0.12, 0.2);
			for (int k = 0; k < r.I(2, 4); k++, t0 += r.R(0.03, 0.07))
			{
				var hp = Biquad.Hp(sr, 4000);
				int s0 = (int)(t0 * sr);
				for (int i = 0; i < 0.01 * sr && s0 + i < x.Length; i++)
					x[s0 + i] += hp.P(r.W()) * Perc((double)i / sr, 0.0001, 0.0015) * 0.35 / (k + 1);
			}
		}
		return FinishOneShot(x, sr, -3, 30, 0.24);
	}

	// ---------------------------------------------------------------- cloth

	/// <summary>Jacket rustle: soft band noise in 2-3 overlapping swells, with a little fabric friction grain.</summary>
	public static double[] Cloth(Rng r, int sr)
	{
		double dur = r.R(0.28, 0.5);
		var x = Buf(sr, dur + 0.05);
		var bp = Biquad.Bp(sr, r.R(1500, 2400), 0.6); var hp = Biquad.Hp(sr, 500); var lp = Biquad.Lp(sr, 6000);
		var low = Biquad.Bp(sr, r.R(400, 600), 0.8);
		var fr = Biquad.Hp(sr, 3000);
		int k = r.I(2, 4);
		var bumps = Enumerable.Range(0, k).Select(_ => (c: r.R(0.15, 0.7), w: r.R(0.18, 0.4), a: r.R(0.5, 1.0))).ToArray();
		var tex = new Smooth(r, dur + 0.1, 0.012);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / (dur * sr), e = 0;
			foreach (var b in bumps)
			{
				double d = (u - b.c) / b.w;
				if (Math.Abs(d) < 1) e += b.a * 0.5 * (1 + Math.Cos(Math.PI * d));
			}
			double w = r.W();
			double v = lp.P(hp.P(bp.P(w))) + 0.35 * low.P(w) + 0.25 * fr.P(r.W()) * Math.Pow(tex.At((double)i / sr), 3);
			x[i] = v * e * (0.7 + 0.3 * tex.At((double)i / sr)) * Env(u, 0.05, 0.1);
		}
		return FinishOneShot(x, sr, -3, 40, dur);
	}

	/// <summary>A knife dragged through duct tape: a fast, textured rip - brighter and more
	/// plasticky than cloth, with a stickier crinkle riding on top as the tape gives.</summary>
	public static double[] KnifeSlice(Rng r, int sr)
	{
		double dur = r.R(0.3, 0.44);
		var x = Buf(sr, dur + 0.05);
		var bp = Biquad.Bp(sr, r.R(2200, 3200), 1.1);
		var hp = Biquad.Hp(sr, 1100);
		var crinkle = Biquad.Bp(sr, r.R(4500, 6500), 2.2);
		var tex = new Smooth(r, dur + 0.1, 0.01);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / (dur * sr);
			double t = tex.At((double)i / sr);
			double slice = hp.P(bp.P(r.W())) * Env(u, 0.015, 0.3);
			double crink = crinkle.P(r.W()) * Math.Pow(Math.Max(0, t - 0.25), 1.4) * 0.55 * Env(u, 0.02, 0.32);
			x[i] = (slice + crink) * (0.75 + 0.25 * t);
		}
		return FinishOneShot(x, sr, -3, 35, dur);
	}

	/// <summary>A heavy iron valve wheel, straining round: a filtered-noise creak whose pitch rises
	/// and falls with the effort of the turn, ending in a hard clank as it reaches its stop.</summary>
	public static double[] WheelTurn(Rng r, int sr)
	{
		double dur = r.R(1.1, 1.5);
		var x = Buf(sr, dur + 0.2);
		var creak = new Biquad(sr);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / (dur * sr);
			if ((i & 15) == 0) creak.SetBp(300 + 260 * Math.Sin(Math.PI * Math.Min(1, u)), 3.2);
			x[i] = creak.P(r.W()) * Env(Math.Min(1, u), 0.05, 0.5) * 0.85;
		}
		var clank = Biquad.Bp(sr, r.R(900, 1400), 4);
		int endIdx = (int)(dur * 0.9 * sr);
		for (int i = 0; i < (int)(0.18 * sr) && endIdx + i < x.Length; i++)
		{
			double u = i / (0.18 * sr);
			x[endIdx + i] += clank.P(r.W()) * Math.Exp(-u * 8) * 1.1;
		}
		return FinishOneShot(x, sr, -3, 80, dur);
	}

	/// <summary>The grandfather clock's noon chime, drowned: real bell partials (inharmonic, decaying
	/// each at its own rate) pushed through a heavy underwater lowpass with a slow amplitude wobble,
	/// so it reads as the same chime but choked and warped by the flooded room around it.</summary>
	public static double[] ClockChimeDrowned(Rng r, int sr)
	{
		const double dur = 4.2;
		var x = Buf(sr, dur + 0.4);
		double f0 = r.R(170, 190);
		var partials = new[] { (1.0, 0.55), (2.0, 0.35), (2.4, 0.25), (3.01, 0.18), (4.12, 0.1) };
		for (int i = 0; i < x.Length; i++)
		{
			double t = i / (double)sr, v = 0;
			foreach (var (mul, amp) in partials) v += Math.Sin(TwoPi * f0 * mul * t) * amp * Math.Exp(-t * (0.5 + mul * 0.35));
			x[i] = v;
		}
		var lp1 = Biquad.Lp(sr, 850); var lp2 = Biquad.Lp(sr, 650);
		for (int i = 0; i < x.Length; i++) x[i] = lp2.P(lp1.P(x[i]));
		var wobble = new Smooth(r, dur + 0.4, 0.22);
		for (int i = 0; i < x.Length; i++) { double t = i / (double)sr; x[i] *= 0.65 + 0.35 * wobble.At(t); }
		return FinishOneShot(x, sr, -3, 250, dur);
	}

	/// <summary>The clock breaking apart right after its chime: a sharp wood crack, a scatter of
	/// glass, and a low body-thud as the case comes apart.</summary>
	public static double[] ClockBreak(Rng r, int sr)
	{
		const double dur = 1.1;
		var x = Buf(sr, dur + 0.3);
		var crackHp = Biquad.Hp(sr, 900);
		var thudLp = Biquad.Lp(sr, 150);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / (dur * sr);
			x[i] = crackHp.P(r.W()) * Env(Math.Min(1, u), 0.002, 0.12) * 1.2
				+ thudLp.P(r.W()) * Env(Math.Min(1, u), 0.01, 0.3) * 0.9;
		}
		int n = r.I(5, 9);
		for (int g = 0; g < n; g++)
		{
			double at = r.R(0.03, 0.55);
			var glass = Biquad.Bp(sr, r.R(2500, 5500), r.R(10, 20));
			int s0 = (int)(at * sr), len = (int)(0.12 * sr);
			for (int i = 0; i < len && s0 + i < x.Length; i++)
				x[s0 + i] += glass.P(i == 0 ? 1.0 : 0.0) * Math.Exp(-i / (double)sr * 22) * r.R(0.15, 0.3);
		}
		return FinishOneShot(x, sr, -3, 350, dur);
	}

	/// <summary>
	/// A hunting rifle, far off but unmistakable: a hard crack, a punchy boom
	/// with enough mid-range to carry on any speaker, then the report rolling
	/// back off the ridges for seconds. The mix is compressed so the whole event
	/// is loud, not just its first millisecond.
	/// </summary>
	public static double[] RifleDistant(Rng r, int sr)
	{
		var x = Buf(sr, 6.5);
		var shot = new double[(int)(0.6 * sr)];
		var crackLp = Biquad.Lp(sr, 4200); var crackHp = Biquad.Hp(sr, 500);
		var boomBp = Biquad.Bp(sr, 260, 0.7); var boomLp = Biquad.Lp(sr, 1100);
		var body = Biquad.Lp(sr, 160);
		for (int i = 0; i < shot.Length; i++)
		{
			double t = (double)i / sr;
			double crack = crackHp.P(crackLp.P(r.W())) * Perc(t, 0.0002, 0.005) * 3.0;
			double tb = t - 0.012;
			double boom = boomLp.P(boomBp.P(r.W())) * Perc(tb, 0.0015, 0.09) * 9.0
				+ body.P(r.W()) * Perc(tb, 0.002, 0.14) * 5.0;
			shot[i] = crack + boom;
		}
		void Stamp(double at, double gain, double cutoff)
		{
			var lp = Biquad.Lp(sr, cutoff); var lp2 = Biquad.Lp(sr, cutoff * 1.2);
			int s0 = (int)(at * sr);
			for (int i = 0; i < shot.Length && s0 + i < x.Length; i++) x[s0 + i] += lp2.P(lp.P(shot[i])) * gain;
		}
		Stamp(0.02, 1.0, 7000);
		// One shot only: a single soft, smeared reflection off the far ridge. Distinct
		// slap-backs sounded like more shots, so the rest of the echo is the diffuse roll.
		Stamp(0.55 + r.R(-0.05, 0.05), 0.22, 900);
		// The diffuse roll between them.
		var roll = Biquad.Lp(sr, 420); var roll2 = Biquad.Lp(sr, 500);
		double swellPh = r.R(0, TwoPi);
		for (int i = (int)(0.2 * sr); i < x.Length; i++)
		{
			double t = (double)i / sr;
			double env = (1 - Math.Exp(-(t - 0.2) / 0.12)) * Math.Exp(-(t - 0.2) / 1.3);
			double swell = 0.85 + 0.15 * Math.Sin(TwoPi * 0.4 * t + swellPh);   // gentle, so it never pulses like more shots
			x[i] += roll2.P(roll.P(r.W())) * env * swell * 2.4;
		}
		HighPass(x, sr, 45);
		// Compress: soft-clip so the body and echoes sit close to the peak.
		double pk = 0; foreach (var v in x) pk = Math.Max(pk, Math.Abs(v));
		double drive = 5.0 / Math.Max(pk, 1e-9);
		for (int i = 0; i < x.Length; i++) x[i] = Math.Tanh(x[i] * drive);
		return FinishOneShot(x, sr, -3, 400);
	}

	/// <summary>
	/// Rolling thunder and nothing else (Dan, 2026-09-22: the continuous rumble bed after the first boom
	/// read as a loud rushing wind; he likes the rolling peals). There is no noise floor any more: the
	/// take is a first boom (the biggest peal: a 0.5-0.9 s soft front, a brief crest, a 1.2-2 s fall)
	/// followed by two to four rolls, each a quieter, softer peal 1.8-3.4 s after the last with near
	/// silence between them, the only tails being the hall's. Peals are noise through a 60-110 Hz band
	/// with a 220 Hz low-pass on top, so nothing above 300 Hz is in there but the peals' own soft body.
	/// Variants: 1 = a couple of kilometres off, boom + 2-3 rolls; 2 = nearer and fuller, boom + 3-4
	/// rolls; 3 = very far, a slower boom + 2 rolls.
	/// </summary>
	public static double[] Thunder(Rng r, int sr, int variant)
	{
		(int rolls, double frontMin, double frontMax, double gain) = variant switch
		{
			1 => (r.I(2, 4), 0.5, 0.8, 1.0),
			2 => (r.I(3, 5), 0.5, 0.9, 1.15),
			_ => (2, 0.7, 1.0, 0.85),
		};
		var pl = new List<(double at, double rise, double hold, double fall, double amp)>();
		double t0 = r.R(0.15, 0.4), amp = 1.0;
		pl.Add((t0, r.R(frontMin, frontMax), r.R(0.1, 0.3), r.R(1.2, 2.0), amp));
		for (int k = 0; k < rolls; k++)
		{
			t0 += pl[^1].rise + pl[^1].hold + r.R(1.6, 2.9);
			amp *= r.R(0.45, 0.65);
			pl.Add((t0, r.R(0.6, 1.0), r.R(0.05, 0.2), r.R(0.9, 1.6), amp));
		}
		var last = pl[^1];
		double dur = Math.Min(16.5, last.at + last.rise + last.hold + last.fall * 2.8 + 1.2);
		var x = Buf(sr, dur);
		double Peal(double t)
		{
			double v = 0;
			foreach (var (at, rise, hold, fall, a) in pl)
			{
				double u = t - at;
				if (u < 0) continue;
				double e = u < rise ? Math.Sin(0.5 * Math.PI * u / rise) : u < rise + hold ? 1 : Math.Exp(-(u - rise - hold) / fall);
				v += a * e * e;
			}
			return v;
		}
		var bp = new Biquad(sr); var lpTop = Biquad.Lp(sr, 160); var hp = Biquad.Hp(sr, 30);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			if ((i & 63) == 0) bp.SetBp(55 + 35 * Math.Max(0, 1 - u * 1.5), 0.8);   // the later rolls sit lower
			x[i] = hp.P(lpTop.P(bp.P(r.W()))) * Peal(t) * 1.6 * gain;
		}
		var hallRv = new Hall(sr, 2.4, 0.5, 30, 1.2);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] + hallRv.P(x[i]) * 0.4;
		return FinishOneShot(o, sr, -3, 900);
	}

	/// <summary>
	/// Low, distant thunder (Act 3: "low thunder rumbles"). No crack and no overdrive: at a few
	/// kilometres the air has taken the top off, and what's left is a rolling rumble. Built the way
	/// thunder is heard: hundreds of low pressure pulses arriving from different parts of a long,
	/// crooked channel, grouped into 2-3 rolls that swell and die away unevenly, then a long soft
	/// tail, all under a gentle outdoor reverb.
	/// Variants: 1 = far, three rolls; 2 = nearer, with a dull opening clap; 3 = very far, a slow low grumble.
	/// Superseded by <see cref="Thunder"/> (2026-09-22): a train of discrete thuds reads as a drum roll.
	/// </summary>
	public static double[] ThunderPulses(Rng r, int sr, int variant)
	{
		(double cut, int rolls, double clap, double rate) = variant switch
		{
			1 => (190.0, 3, 0.0, 70.0),
			2 => (300.0, 3, 0.8, 90.0),
			_ => (125.0, 2, 0.0, 55.0),
		};
		// Roll envelopes: onset, rise, decay, level. Each roll is a little weaker than the last, but
		// not always by much: a later roll can nearly match the first.
		var rl = new List<(double at, double rise, double decay, double amp)>();
		double at = r.R(0.15, 0.5) + (variant == 3 ? 0.6 : 0), lvl = 1.0;
		for (int k = 0; k < rolls; k++)
		{
			rl.Add((at, r.R(0.25, 0.7) * (variant == 3 ? 1.8 : 1), r.R(0.8, 1.6) * (variant == 3 ? 1.4 : 1), lvl));
			at += r.R(1.1, 2.4);
			lvl *= r.R(0.5, 0.9);
		}
		double tailAt = rl[0].at, tailTau = 2.6;
		// Long enough for everything to die away on its own (the finish trims the silent end).
		double dur = rl[^1].at + rl[^1].rise + 5 * rl[^1].decay + 3;
		var x = Buf(sr, dur);
		double Density(double t)
		{
			double d = 0;
			foreach (var (a, rise, decay, amp) in rl)
			{
				double u = t - a;
				if (u > 0) d += amp * (1 - Math.Exp(-u / rise)) * Math.Exp(-u / decay);
			}
			if (t > tailAt) d += 0.12 * Math.Exp(-(t - tailAt) / tailTau);
			return d;
		}
		double dMax = 0;
		for (double t = 0; t < dur; t += 0.01) dMax = Math.Max(dMax, Density(t));

		// Arrivals: short low-passed pressure pulses, each from a different stretch of the channel.
		double tt0 = 0;
		while (true)
		{
			tt0 += -Math.Log(1 - r.U()) / rate;
			if (tt0 >= dur) break;
			double d = Density(tt0) / dMax;
			if (r.U() > d) continue;
			double a = r.LogR(0.2, 1.0), tau = r.LogR(0.03, 0.16), att = r.R(0.006, 0.03);
			var lp1 = Biquad.Lp(sr, cut * r.R(0.7, 1.5)); var lp2 = Biquad.Lp(sr, cut * r.R(0.8, 1.6));
			int s0 = (int)(tt0 * sr), len = (int)((att + tau * 6) * sr);
			for (int i = 0; i < len && s0 + i < x.Length; i++)
				x[s0 + i] += lp2.P(lp1.P(r.W())) * Perc((double)i / sr, att, tau) * a;
		}
		if (clap > 0)
		{
			// The first return, still soft-edged: a dull low-mid clap, nothing above ~700 Hz.
			var c1 = Biquad.Lp(sr, 650); var c2 = Biquad.Lp(sr, 750); var ch = Biquad.Hp(sr, 60);
			int s0 = (int)(rl[0].at * sr);
			for (int i = 0; i < 0.6 * sr && s0 + i < x.Length; i++)
				x[s0 + i] += ch.P(c2.P(c1.P(r.W()))) * Perc((double)i / sr, 0.008, 0.07) * clap * 1.6;
		}
		var g1 = Biquad.Lp(sr, cut * 1.6); var g2 = Biquad.Lp(sr, cut * 2.2);
		for (int i = 0; i < x.Length; i++) x[i] = g2.P(g1.P(x[i]));
		if (variant != 3)
		{
			// The leading edge: a short tearing crack on the first roll (the part that says "lightning",
			// not just "rumble"), rolled off above ~1 kHz so it still reads as a mile or two away.
			var k1 = Biquad.Lp(sr, 950); var k2 = Biquad.Lp(sr, 1150); var kh = Biquad.Hp(sr, 140);
			int s0 = (int)((rl[0].at + r.R(0.02, 0.08)) * sr);
			double ka = variant == 2 ? 1.4 : 0.75;
			for (int i = 0; i < 0.4 * sr && s0 + i < x.Length; i++)
				x[s0 + i] += kh.P(k2.P(k1.P(r.W()))) * Perc((double)i / sr, 0.004, 0.06) * ka;
		}
		HighPass(x, sr, 24);
		var hall = new Hall(sr, 2.2, 0.6, 25, 1.2);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] + hall.P(x[i]) * 0.5;
		return FinishOneShot(o, sr, -3, 900);
	}

	/// <summary>
	/// The giant's stride far off in the fog (Act 4: "the low thuds of the giant landing its stride").
	/// From a few hundred metres it is not a bang but a pressure: a slow-edged (60-120 ms) swell of
	/// sub (under ~80 Hz) as the mass lands, a long low rumble through the ground behind it, and the
	/// faintest dull settle of the forest floor a moment later. No sharp edge and no 100-250 Hz
	/// "body" (that pair is what read as a gunshot), no pitched thump, no ringing mode. All noise.
	/// </summary>
	public static double[] GiantStep(Rng r, int sr)
	{
		var x = Buf(sr, 3.6);
		double t0 = 0.02;
		var l1 = Biquad.Lp(sr, r.R(48, 62)); var l2 = Biquad.Lp(sr, r.R(60, 80)); var l3 = Biquad.Lp(sr, 95);
		var t1 = Biquad.Lp(sr, 40); var t2 = Biquad.Lp(sr, 55);
		double att = r.R(0.06, 0.12), tau = r.R(0.22, 0.34), tail = r.R(0.9, 1.4);
		for (int i = (int)(t0 * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - t0;
			double impact = l3.P(l2.P(l1.P(r.W()))) * Perc(t, att, tau) * 16;
			double rumble = t2.P(t1.P(r.W())) * Perc(t, 0.2, tail) * 10 * (1 - Math.Exp(-t / 0.08));
			x[i] += impact + rumble;
		}
		// The floor settling: very faint, dull and late, with a slow edge so it never ticks.
		var dl = Biquad.Lp(sr, 500); var dh = Biquad.Hp(sr, 120);
		double ds = t0 + r.R(0.12, 0.2), dd = r.R(0.5, 0.8);
		var tex = new Smooth(r, 4, 0.05);
		for (int i = (int)(ds * sr); i < (ds + dd) * sr && i < x.Length; i++)
		{
			double u = (i / (double)sr - ds) / dd;
			x[i] += dh.P(dl.P(r.W())) * Env(u, 0.3, 0.6) * Math.Pow(tex.At(i / (double)sr), 2) * 0.25;
		}
		HighPass(x, sr, 20);
		var hall = new Hall(sr, 2.8, 0.7, 40, 1.4);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] + hall.P(x[i]) * 0.4;
		return FinishOneShot(o, sr, -3, 600);
	}

	/// <summary>
	/// A handheld's squelch gate (the Act 10-11 walkie). Open: a small relay-like click, then the
	/// receiver's floor hiss coming up over ~40 ms (the static loop takes over from there). Close:
	/// the classic "pfft" squelch tail, 80-160 ms of the bare FM noise floor as the carrier drops,
	/// cut dead by the gate with a tiny click. Both live in a small speaker's 300-3000 Hz: a real
	/// radio's squelch is a tick and a breath, not a burst of static.
	/// </summary>
	public static double[] Squelch(Rng r, int sr, bool open)
	{
		// From the reference samples (Dan, 2026-09-22): the open is a relay click and a ~60 ms burst of
		// the same bright static; the close is the classic "kshht": a 170-230 ms burst of full-band
		// white noise through the speaker, cut dead by the gate with a tiny click.
		double noiseDur = open ? r.R(0.05, 0.07) : r.R(0.17, 0.23);
		var x = Buf(sr, noiseDur + 0.08);
		var hp = Biquad.Hp(sr, 280); var lp = Biquad.Lp(sr, 3600);
		var hump = Biquad.Bp(sr, 2100, 0.5);
		var click = Biquad.Bp(sr, r.R(1200, 1900), 2.5);
		double n0 = open ? 0.005 : 0.0, clickAt = open ? 0.003 : noiseDur;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, env;
			if (open)
			{
				double u = (t - n0) / noiseDur;
				env = u < 0 ? 0 : u < 1 ? Math.Pow(Math.Sin(0.5 * Math.PI * Math.Min(1, u * 3)), 2) * (1 - 0.35 * u) : Math.Max(0, 1 - (t - n0 - noiseDur) / 0.02);
			}
			else
			{
				double u = t / noiseDur;
				env = u < 1 ? (0.7 + 0.3 * Math.Sin(0.5 * Math.PI * Math.Min(1, u * 4))) * (1 - 0.15 * u) : 0.8 * Math.Max(0, 1 - (t - noiseDur) / 0.003);
			}
			double w = r.W();
			double hiss = (lp.P(hp.P(w)) + 0.7 * hump.P(w)) * env;
			double tc = t - clickAt;
			double k = tc >= 0 ? click.P(r.W()) * Perc(tc, 0.0004, 0.003) * 0.9 : 0;
			x[i] = hiss + k;
		}
		return FinishOneShot(x, sr, -3, 6);
	}

	/// <summary>
	/// One tiny crackle from the handheld while a line is up: the receiver catching the edge of
	/// something for 30-90 ms. Band-limited to the little speaker (300-3000 Hz), a burst of grainy
	/// ticks over a whisper of hiss, no click and no tone. Act 11 sprinkles two to four of these
	/// under each radio line, never two within a quarter second.
	/// </summary>
	public static double[] RadioTick(Rng r, int sr)
	{
		double dur = r.R(0.03, 0.09);
		var x = Buf(sr, dur + 0.02);
		var hp = Biquad.Hp(sr, 320); var lp = Biquad.Lp(sr, 3000);
		var grain = Biquad.Bp(sr, r.R(900, 2200), 2.0);
		var gate = new Smooth(r, 1, 0.004);   // the crackle's own grain: fast random gating
		double att = r.R(0.05, 0.2);
		for (int i = 0; i < dur * sr; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double env = Env(u, att, 0.35);
			double g = Math.Pow(gate.At(t), 3) * 1.6;
			x[i] = (grain.P(r.W()) * g + 0.15 * lp.P(hp.P(r.W()))) * env;
		}
		return FinishOneShot(x, sr, -3, 6);
	}

	// ---------------------------------------------------------------- the bunker's steel doors

	/// <summary>A dull, damped knock: noise through one bandpass, dead in <paramref name="tau"/>*3 or so. No ring.</summary>
	static void DullKnock(double[] x, Rng r, int sr, double at, double fc, double q, double tau, double amp)
	{
		var bp = Biquad.Bp(sr, fc, q); var lp = Biquad.Lp(sr, 3800);
		for (int i = (int)(at * sr); i < (at + tau * 5 + 0.01) * sr && i < x.Length; i++)
		{
			double t = (double)i / sr - at;
			x[i] += lp.P(bp.P(r.W())) * Perc(t, 0.003, tau) * amp;
		}
	}

	/// <summary>
	/// A riveted steel slab opening (the bunker rooms), heavy and dead: a dull latch clunk (a damped
	/// 300-500 Hz knock), a low groan of filtered noise sweeping 260 down to 120 Hz over ~0.5 s
	/// (no sine, no pitch that rings), and a soft stop thud. Everything low-passed near 4 kHz and
	/// nothing resonant longer than 100 ms: no pinball, no wood. ~0.9 s.
	/// </summary>
	public static double[] SteelDoorOpen(Rng r, int sr)
	{
		var x = Buf(sr, 1.3);
		double t0 = 0.02;
		// Latch: two dull knocks a hair apart (the bolt, then the handle falling).
		DullKnock(x, r, sr, t0, r.R(320, 480), 1.0, r.R(0.018, 0.026), 3.0);
		DullKnock(x, r, sr, t0 + r.R(0.03, 0.06), r.R(300, 420), 1.0, r.R(0.02, 0.03), 1.6);
		// Hinge: a groan of filtered noise, the band sliding down, the level wavering.
		double g0 = t0 + r.R(0.1, 0.16), gd = r.R(0.45, 0.6), fa = r.R(230, 260), fb = r.R(120, 150);
		var groan = new Biquad(sr); var wav = new Smooth(r, 2, 0.04); var rough = new Smooth(r, 2, 0.008);
		var glp = Biquad.Lp(sr, 900);
		for (int i = (int)(g0 * sr); i < (g0 + gd) * sr && i < x.Length; i++)
		{
			double t = (double)i / sr, u = (t - g0) / gd;
			if (i % 32 == 0) groan.SetBp(fa * Math.Pow(fb / fa, u), 6);
			double lvl = Env(u, 0.2, 0.3) * (0.5 + 0.5 * wav.At(t)) * (0.6 + 0.8 * Math.Pow(rough.At(t), 2));
			x[i] += glp.P(groan.P(r.W())) * lvl * 2.2;
		}
		// Stop thud: the slab meeting its stop, soft and low.
		double k0 = g0 + gd + r.R(0.01, 0.04);
		var kl = Biquad.Lp(sr, 260);
		for (int i = (int)(k0 * sr); i < (k0 + 0.3) * sr && i < x.Length; i++)
		{
			double t = (double)i / sr - k0;
			x[i] += kl.P(r.W()) * Perc(t, 0.006, 0.05) * 3.0;
		}
		DullKnock(x, r, sr, k0 + 0.004, r.R(300, 420), 1.0, 0.02, 1.2);
		HighPass(x, sr, 40);
		var lpAll = Biquad.Lp(sr, 4000);
		var room = new Hall(sr, 0.5, 0.7, 8, 0.5);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) { double v = lpAll.P(x[i]); o[i] = v + room.P(v) * 0.25; }
		return FinishOneShot(o, sr, -3, 60, 0.7, 1.05);
	}

	/// <summary>
	/// A steel slab slamming shut in a concrete room, heavy and dead: a soft-fronted sub boom
	/// (60-110 Hz, 0.08 s front, ~0.5 s decay), a dull damped clunk (noise through a 250-500 Hz
	/// bandpass, dead in 60-90 ms), a very short low-level broadband slap (2-6 kHz, dead in 15 ms),
	/// a few dull frame knocks (200-400 Hz, ~40 ms each) and a low-passed rumble tail. No pitched
	/// ring anywhere (a ringing partial reads as a pinball bumper), nothing resonant past 100 ms,
	/// everything low-passed near 4 kHz. ~1.2 s.
	/// </summary>
	public static double[] SteelDoorSlam(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		double t0 = 0.01;
		var b1 = Biquad.Lp(sr, r.R(60, 80)); var b2 = Biquad.Lp(sr, r.R(90, 110)); var bh = Biquad.Hp(sr, 45);
		var r1 = Biquad.Lp(sr, 140); var r2 = Biquad.Lp(sr, 200);
		double boomTau = r.R(0.16, 0.2), rumTau = r.R(0.3, 0.4);
		for (int i = (int)(t0 * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - t0;
			double boom = bh.P(b2.P(b1.P(r.W()))) * Perc(t, 0.08, boomTau) * 12;
			double rumble = r2.P(r1.P(r.W())) * Perc(t, 0.12, rumTau) * 3.5;
			x[i] += boom + rumble;
		}
		// The clunk: the slab's body meeting the frame, dull and dead.
		DullKnock(x, r, sr, t0 + 0.005, r.R(250, 500), 1.0, r.R(0.02, 0.03), 4.0);
		// The slap: the thinnest broadband edge, gone in 15 ms, low.
		var sb = Biquad.Bp(sr, r.R(2500, 4500), 0.8); var sl = Biquad.Lp(sr, 5500);
		for (int i = (int)(t0 * sr); i < (t0 + 0.05) * sr; i++)
		{
			double t = (double)i / sr - t0;
			x[i] += sl.P(sb.P(r.W())) * Perc(t, 0.001, 0.004) * 0.7;
		}
		// The frame rattling: dull knocks thinning out.
		int knocks = r.I(3, 5);
		double kt = t0 + r.R(0.07, 0.12);
		for (int k = 0; k < knocks; k++)
		{
			DullKnock(x, r, sr, kt, r.R(200, 400), 1.2, r.R(0.01, 0.014), r.R(0.9, 1.8) * (1 - 0.15 * k));
			kt += r.R(0.05, 0.13) * (1 + 0.3 * k);
		}
		HighPass(x, sr, 28);
		var lpAll = Biquad.Lp(sr, 4000);
		var room = new Hall(sr, 0.8, 0.7, 10, 0.6);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) { double v = lpAll.P(x[i]); o[i] = v + room.P(v) * 0.3; }
		return FinishOneShot(o, sr, -3, 80, 1.0, 1.3);
	}

	/// <summary>
	/// One soft breath, in or out, mostly through the nose: warm, pre-softened
	/// noise through one broad low resonance, with the top rolled off hard. No
	/// throat buzz, no hiss. Inhales are a touch brighter and shorter; exhales
	/// are longer and lower. <paramref name="effort"/> 0..1 = resting .. winded
	/// (shorter, fuller). PlayerBreathing strings these together at a rate set
	/// by exertion, so no two breaths line up.
	/// </summary>
	public static double[] BreathOne(Rng r, int sr, bool inhale, double effort)
	{
		double dur = (inhale ? r.R(0.6, 0.95) : r.R(0.8, 1.3)) * (1 - 0.3 * effort);
		var x = Buf(sr, dur + 0.05);
		var warm = Biquad.Lp(sr, 900);   // pink-ish source: no fizz to begin with
		var body = Biquad.Bp(sr, inhale ? r.R(650, 900) : r.R(380, 560), 0.7);
		var soft1 = Biquad.Lp(sr, inhale ? 1700 : 1150);
		var soft2 = Biquad.Lp(sr, inhale ? 1900 : 1300);
		var hp = Biquad.Hp(sr, inhale ? 220 : 120);
		for (int i = 0; i < dur * sr; i++)
		{
			double u = i / (dur * sr);
			double n = warm.P(r.W()) * 0.7 + r.W() * 0.3;
			double v = soft2.P(soft1.P(hp.P(body.P(n))));
			// Smooth, unhurried shapes: an inhale swells in, an exhale lets go.
			double shape = inhale ? Math.Pow(Env(u, 0.45, 0.35), 1.2) : Math.Pow(Env(u, 0.15, 0.7), 1.1);
			x[i] = v * shape * (0.8 + 0.2 * effort);
		}
		return FinishOneShot(x, sr, -3, 40);
	}

	/// <summary>A camera's mechanical shutter: a sharp mirror-slap click, then a softer closing click ~90 ms later.</summary>
	public static double[] CameraShutter(Rng r, int sr)
	{
		var x = Buf(sr, 0.22);
		void Click(double at, double amp, double toneHz, double q, double dur)
		{
			var bp = Biquad.Bp(sr, toneHz, q);
			var hp = Biquad.Hp(sr, 1800);
			int s0 = (int)(at * sr);
			for (int i = 0; i + s0 < x.Length && i < dur * sr; i++)
			{
				double tt = (double)i / sr;
				double v = bp.P(r.W()) * Perc(tt, 0.0003, dur * 0.35) * 2.4 + hp.P(r.W()) * Perc(tt, 0.0001, dur * 0.2) * 1.2;
				x[s0 + i] += v * amp;
			}
		}
		Click(0.0, 1.0, 2600, 3.0, 0.02);
		Click(0.09 + r.R(-0.005, 0.01), 0.55, 1900, 2.4, 0.018);
		return FinishOneShot(x, sr, -3, 40);
	}

	/// <summary>
	/// The fourth bird's answer: a harsh scream, far off and wrong, breaking upward then choking off.
	/// Heavy distortion and reverb keep it distant rather than a jump-scare stinger.
	/// </summary>
	public static double[] DistantScream(Rng r, int sr)
	{
		double dur = 1.7;
		var src = Buf(sr, dur);
		double f0 = r.R(340, 420);
		double ph = 0;
		for (int i = 0; i < src.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double rise = Math.Pow(Math.Min(1, u * 1.8), 0.6);
			double f = f0 * (1 + 1.3 * rise) * (1 - 0.25 * Math.Max(0, u - 0.75) / 0.25);
			ph += TwoPi * f / sr;
			double v = 0;
			for (int h = 1; h <= 10; h++) if (f * h < sr * 0.45) v += Math.Sin(ph * h) / Math.Pow(h, 0.65);
			double rough = 0.6 + 0.4 * Math.Sin(TwoPi * 37 * t);
			double env = Env(u, 0.1, 0.55) * (u > 0.82 ? Math.Max(0, 1 - (u - 0.82) / 0.18) : 1.0);
			src[i] = Math.Tanh(v * 1.6) * rough * env;
		}
		var bp1 = Biquad.Bp(sr, 900, 1.1); var bp2 = Biquad.Bp(sr, 2200, 1.3);
		var x = new double[src.Length];
		for (int i = 0; i < src.Length; i++) x[i] = bp1.P(src[i]) * 0.8 + bp2.P(src[i]) * 0.5;
		// Distance: mostly reverb, dry signal well underneath.
		var rv = new Reverb(sr, 0.95, 0.6);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.35 + rv.P(x[i]) * 1.1;
		LowPass(o, sr, 3400);
		return FinishOneShot(o, sr, -3, 300);
	}

	/// <summary>
	/// A murmured, breathy voice out in the trees — never intelligible words,
	/// just a handful of syllable-like pulses sweeping downward through heavy
	/// reverb, so it reads as "something spoke" from a dreamlike distance.
	/// </summary>
	public static double[] WhisperVoice(Rng r, int sr)
	{
		double dur = r.R(1.8, 2.6);
		var x = Buf(sr, dur);
		int syl = r.I(3, 5);
		double t = r.R(0.03, 0.08);
		for (int s = 0; s < syl; s++)
		{
			double d = r.R(0.14, 0.26);
			double f0 = r.R(220, 340) * (1 - 0.08 * s);
			int s0 = (int)(t * sr), len = (int)(d * sr);
			var bp1 = Biquad.Bp(sr, f0, 5.0);
			var bp2 = Biquad.Bp(sr, f0 * r.R(2.6, 3.2), 4.0);
			for (int i = 0; i < len && s0 + i < x.Length; i++)
			{
				double tt = (double)i / sr, u = tt / d;
				double n = r.W();
				double v = bp1.P(n) * 1.4 + bp2.P(n) * 0.6;
				x[s0 + i] += v * Env(u, 0.25, 0.4) * r.R(0.7, 1.0);
			}
			t += d + r.R(0.05, 0.13);
		}
		HighPass(x, sr, 150); LowPass(x, sr, 2600);
		var rv = new Reverb(sr, 0.92, 0.6);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.4 + rv.P(x[i]) * 1.0;
		return FinishOneShot(o, sr, -3, 200);
	}

	/// <summary>
	/// The sting when you look straight at it: not a jump scare, just a wrongness
	/// in the ears. A thin, detuned high cluster swells in, pressure drops away
	/// underneath, and it all drains out.
	/// </summary>
	public static double[] StalkerSeen(Rng r, int sr)
	{
		double dur = 2.2;
		var x = Buf(sr, dur);
		double[] hi = { 3150, 3171, 4420, 4447 };   // close pairs: slow, sick beating
		double[] ph = new double[hi.Length];
		var noise = Biquad.Bp(sr, 5200, 3);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, u = t / dur;
			double swell = Env(u, 0.18, 0.7);
			double v = 0;
			for (int k = 0; k < hi.Length; k++)
			{
				ph[k] += TwoPi * hi[k] * (1 - 0.03 * u) / sr;   // sags very slightly
				v += Math.Sin(ph[k]) * (k < 2 ? 0.5 : 0.3);
			}
			double sub = Math.Sin(TwoPi * (46 - 18 * u) * t) * Env(u, 0.05, 0.8) * 0.45;
			x[i] = (v * 0.9 + noise.P(r.W()) * 0.5) * swell + sub;
		}
		return FinishOneShot(x, sr, -3, 200);
	}
}
