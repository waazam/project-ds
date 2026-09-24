namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Act 12's lake: the rowboat, the water around it, and the thing that comes up out of it.
///
/// Water here is built from its real ingredient — the bubble. A drop or a splash is heard as the
/// ringing of the air pockets it traps: tiny sine chirps rising in pitch as each bubble settles
/// (Minnaert resonance), from a deep "gloop" at 200 Hz to a bright drip at 3 kHz. Those, plus short
/// band-limited slaps for impacts and scattered ticks for spray, make every sound in this file; no
/// sustained noise beds (the owner's standing rule), and the two loops are written circularly out of
/// events so they repeat seamlessly.
/// </summary>
public static class Lake
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>One bubble: a sine chirping up from <paramref name="f"/> by about half, decaying fast.</summary>
	static void Bubble(double[] x, int sr, double t, double f, double amp, double tau, bool wrap = false)
	{
		int n = (int)((tau * 6 + 0.004) * sr), s0 = (int)(t * sr);
		double ph = 0;
		for (int i = 0; i < n; i++)
		{
			double tt = (double)i / sr;
			double fi = f * (1 + 0.55 * (1 - Math.Exp(-tt / (tau * 0.8))));
			ph += TwoPi * fi / sr;
			double v = Math.Sin(ph) * Perc(tt, 0.0008, tau) * amp;
			int j = s0 + i;
			if (wrap) x[((j % x.Length) + x.Length) % x.Length] += v;
			else if (j < x.Length) x[j] += v;
		}
	}

	/// <summary>A band-limited noise impact (slap, knock, thump), peak-normalised to <paramref name="amp"/>.</summary>
	static void Slap(double[] x, Rng r, int sr, double t, double amp, double lo, double hi, double tau, double attack = 0.0006, bool wrap = false)
	{
		var hp = Biquad.Hp(sr, lo); var lp = Biquad.Lp(sr, hi); var lp2 = Biquad.Lp(sr, hi * 1.2);
		var ev = new double[(int)((tau * 7 + attack + 0.004) * sr)];
		for (int i = 0; i < ev.Length; i++) ev[i] = lp2.P(lp.P(hp.P(r.W()))) * Perc((double)i / sr, attack, tau);
		double g = amp / Math.Max(Peak(ev), 1e-12);
		int s0 = (int)(t * sr);
		for (int i = 0; i < ev.Length; i++)
		{
			int j = s0 + i;
			if (wrap) x[((j % x.Length) + x.Length) % x.Length] += ev[i] * g;
			else if (j < x.Length) x[j] += ev[i] * g;
		}
	}

	/// <summary>A hollow resonant knock (wood, a hull): a noise impulse through a tuned band-pass.</summary>
	static void Knock(double[] x, Rng r, int sr, double t, double amp, double f, double q, double tau, bool wrap = false)
	{
		var bp = Biquad.Bp(sr, f, q);
		var ev = new double[(int)((tau * 7 + 0.004) * sr)];
		for (int i = 0; i < ev.Length; i++) ev[i] = bp.P(r.W() * Perc((double)i / sr, 0.0004, 0.004)) ;
		for (int i = 0; i < ev.Length; i++) ev[i] *= Math.Exp(-(double)i / sr / tau);
		double g = amp / Math.Max(Peak(ev), 1e-12);
		int s0 = (int)(t * sr);
		for (int i = 0; i < ev.Length; i++)
		{
			int j = s0 + i;
			if (wrap) x[((j % x.Length) + x.Length) % x.Length] += ev[i] * g;
			else if (j < x.Length) x[j] += ev[i] * g;
		}
	}

	/// <summary>A cluster of bubbles over [t0, t1): a gulp, a gurgle, a splash's churn.</summary>
	static void Churn(double[] x, Rng r, int sr, double t0, double t1, double rate, double fLo, double fHi, double amp, bool wrap = false)
	{
		for (double t = t0; t < t1; t += r.R(0.3, 1.7) / rate)
		{
			double u = (t - t0) / Math.Max(t1 - t0, 1e-6);
			Bubble(x, sr, t, r.LogR(fLo, fHi), amp * r.LogR(0.3, 1) * (1 - 0.7 * u), r.R(0.006, 0.02), wrap);
		}
	}

	/// <summary>Spray and drops pattering back down over [t0, t1), thinning out.</summary>
	static void Patter(double[] x, Rng r, int sr, double t0, double t1, double rate, double amp, bool wrap = false)
	{
		for (double t = t0; t < t1; t += r.R(0.3, 1.7) / rate)
		{
			double u = (t - t0) / Math.Max(t1 - t0, 1e-6);
			if (r.Chance(u * 0.6)) continue;
			if (r.Chance(0.5)) Bubble(x, sr, t, r.LogR(1100, 3400), amp * r.LogR(0.2, 1), r.R(0.004, 0.012), wrap);
			else Slap(x, r, sr, t, amp * r.LogR(0.15, 0.6), 1500, 6000, r.R(0.002, 0.006), 0.0002, wrap);
		}
	}

	static double[] Wet(double[] x, int sr, double dry, double wet, double rt60, double damp = 0.45, double size = 1.0)
	{
		var h = new Hall(sr, rt60, damp, 25, size);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * dry + h.P(x[i]) * wet;
		return o;
	}

	// ------------------------------------------------------------------ the boat

	/// <summary>An oar biting the water and pulling through: the plunge (a slap and a few bubbles),
	/// a gurgle of churned water through the drive, and drips off the blade as it lifts.</summary>
	public static double[] OarStroke(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Slap(x, r, sr, 0.01, 0.55, 250, 2600, r.R(0.012, 0.02));
		for (int i = 0; i < 3; i++) Bubble(x, sr, 0.012 + r.R(0, 0.03), r.LogR(380, 850), r.R(0.5, 0.9), r.R(0.01, 0.025));
		Churn(x, r, sr, 0.05, 0.42, r.R(40, 70), 220, 700, 0.35);
		Patter(x, r, sr, 0.46, 0.8, r.R(10, 18), 0.22);
		return FinishOneShot(Wet(x, sr, 1.0, 0.18, 0.8, 0.5, 0.6), sr, -3, 80);
	}

	/// <summary>Wood rubbing in a bronze lock: a short stick-slip creak through woody and metal resonances.</summary>
	public static double[] OarlockCreak(Rng r, int sr) => Creak(r, sr, r.R(0.2, 0.32), r.R(55, 80), r.R(95, 140),
		new[] { (r.R(520, 700), 10.0, 1.0), (r.R(1250, 1500), 14.0, 0.6), (r.R(2200, 2600), 18.0, 0.3) });

	/// <summary>The hull taking a load: a slower, lower groan.</summary>
	public static double[] BoatCreak(Rng r, int sr) => Creak(r, sr, r.R(0.6, 1.0), r.R(16, 24), r.R(32, 48),
		new[] { (r.R(170, 230), 14.0, 1.0), (r.R(330, 420), 12.0, 0.7), (r.R(560, 680), 10.0, 0.35) });

	static double[] Creak(Rng r, int sr, double dur, double rLo, double rHi, (double f, double q, double a)[] modes)
	{
		var x = Buf(sr, dur + 0.35);
		var rate = new Smooth(r, dur + 1, r.R(0.08, 0.16));
		var res = modes.Select(m => Biquad.Bp(sr, m.f, m.q)).ToArray();
		var body = Biquad.Lp(sr, 3200);
		double ph = 0;
		int n = (int)(dur * sr);
		for (int i = 0; i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n, rt = rLo + (rHi - rLo) * rate.At((double)i / sr);
				ph += rt / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.2, 0.4); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].a * res[k].P(imp);
			x[i] = body.P(v);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.15, 0.5, 0.5, 0.5), sr, -3, 60);
	}

	/// <summary>Stepping down into the boat: a foot on the boards, the hull's hollow knock, water
	/// slopping against it as it dips, and a groan from the planks.</summary>
	public static double[] BoatBoard(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		Slap(x, r, sr, 0.0, 0.9, 40, 260, 0.07, 0.002);
		Knock(x, r, sr, 0.004, 0.6, r.R(150, 190), 5, 0.12);
		Knock(x, r, sr, 0.006, 0.3, r.R(380, 460), 6, 0.05);
		Churn(x, r, sr, 0.12, 0.7, 30, 180, 520, 0.35);
		Slap(x, r, sr, 0.35, 0.3, 120, 600, 0.06, 0.02);
		Slap(x, r, sr, 0.7, 0.18, 120, 600, 0.06, 0.02);
		var creak = BoatCreak(r.Fork(), sr);
		for (int i = 0; i < creak.Length && i + (int)(0.25 * sr) < x.Length; i++) x[i + (int)(0.25 * sr)] += creak[i] * 0.25;
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.6, 0.5, 0.6), sr, -3, 120);
	}

	/// <summary>The keel running up a gravel beach: a crunching rush of stones building and dying away,
	/// the hull knocking over them, and a groan as it stops.</summary>
	public static double[] BoatGround(Rng r, int sr)
	{
		double dur = 1.7;
		var x = Buf(sr, dur + 0.4);
		for (double t = 0.02; t < dur; t += r.R(0.3, 1.7) / (40 + 260 * Env(t / dur, 0.3, 0.55)))
			Slap(x, r, sr, t, r.LogR(0.08, 0.5) * Env(t / dur, 0.2, 0.5), r.R(900, 1600), r.R(3500, 6000), r.R(0.0015, 0.004), 0.0002);
		for (double t = 0.05; t < dur * 0.9; t += r.R(0.08, 0.2))
			Knock(x, r, sr, t, r.R(0.2, 0.45) * Env(t / dur, 0.2, 0.5), r.R(140, 200), 4, 0.06);
		Slap(x, r, sr, 0.0, 0.5, 30, 180, 0.4, 0.15);
		var creak = BoatCreak(r.Fork(), sr);
		int at = (int)((dur - 0.4) * sr);
		for (int i = 0; i < creak.Length && i + at < x.Length; i++) x[i + at] += creak[i] * 0.35;
		return FinishOneShot(Wet(x, sr, 1.0, 0.12, 0.7, 0.5, 0.6), sr, -3, 150);
	}

	/// <summary>A swell hitting the bow: a heavy hollow slap on the planks, spray over the top, drops.</summary>
	public static double[] WaveSlap(Rng r, int sr)
	{
		var x = Buf(sr, 1.1);
		Slap(x, r, sr, 0.0, 0.8, 60, 700, r.R(0.035, 0.06), 0.004);
		Knock(x, r, sr, 0.003, 0.55, r.R(160, 220), 4, 0.09);
		Slap(x, r, sr, 0.02, 0.35, 900, 5500, 0.05, 0.01);
		Churn(x, r, sr, 0.03, 0.4, 45, 260, 900, 0.3);
		Patter(x, r, sr, 0.15, 0.85, 30, 0.2);
		return FinishOneShot(Wet(x, sr, 1.0, 0.2, 0.9, 0.45, 0.8), sr, -3, 120);
	}

	// ------------------------------------------------------------------ the creature

	/// <summary>Something very big moving under the water: a deep pressure thud felt more than heard
	/// (with harmonics so small speakers still carry it), and a second, softer pulse after it.</summary>
	public static double[] UnderwaterThoom(Rng r, int sr)
	{
		var x = Buf(sr, 2.8);
		foreach (var (t0, a) in new[] { (0.02, 1.0), (r.R(0.55, 0.75), 0.45) })
		{
			double f0 = r.R(34, 42);
			AddTone(x, sr, t0, 1.6, u => f0 * (1 - 0.18 * u), u => a * Math.Exp(-u * 4) * Math.Min(1, u * 40), new[] { 1.0, 0.55, 0.3, 0.12 });
			Slap(x, r, sr, t0, a * 0.5, 20, 160, 0.25, 0.02);
		}
		Churn(x, r, sr, 0.3, 1.6, 8, 90, 220, 0.12);
		LowPass(x, sr, 420);
		return FinishOneShot(Wet(x, sr, 0.9, 0.4, 1.6, 0.6, 1.0), sr, -3, 400);
	}

	/// <summary>The breach: a deep boom as the water is thrown apart, a roaring rush of churned water
	/// as the limbs come up through it, and a long patter of it raining back down onto the lake.</summary>
	public static double[] BreachErupt(Rng r, int sr)
	{
		var x = Buf(sr, 4.6);
		AddTone(x, sr, 0.0, 1.8, u => 48 * (1 - 0.35 * u), u => Math.Exp(-u * 3.2) * Math.Min(1, u * 60), new[] { 1.0, 0.5, 0.25, 0.1 });
		Slap(x, r, sr, 0.0, 1.0, 25, 220, 0.4, 0.01);
		Slap(x, r, sr, 0.01, 0.8, 200, 3000, 0.25, 0.02);
		// the rush: hundreds of splashes and bubbles at once, thinning out
		for (double t = 0.02; t < 2.6; t += r.R(0.3, 1.7) / (320 * Math.Exp(-t * 1.1) + 20))
		{
			double u = t / 2.6;
			if (r.Chance(0.35)) Slap(x, r, sr, t, r.LogR(0.08, 0.5) * (1 - 0.6 * u), r.R(250, 700), r.R(1800, 4500), r.R(0.01, 0.04), 0.002);
			else Bubble(x, sr, t, r.LogR(160, 1200), r.LogR(0.1, 0.5) * (1 - 0.6 * u), r.R(0.008, 0.03));
		}
		Patter(x, r, sr, 0.8, 4.2, 90, 0.25);
		return FinishOneShot(Wet(x, sr, 0.9, 0.55, 2.6, 0.4, 1.3), sr, -3, 500);
	}

	/// <summary>A limb slamming flat onto the water: a huge wet slap, a thump through the lake, the
	/// spray going up and coming down.</summary>
	public static double[] TentacleSlam(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		Slap(x, r, sr, 0.0, 1.0, 120, 4200, 0.03, 0.0005);
		Slap(x, r, sr, 0.0, 0.8, 25, 180, 0.2, 0.003);
		AddTone(x, sr, 0.0, 0.8, u => 55 * (1 - 0.3 * u), u => 0.6 * Math.Exp(-u * 5) * Math.Min(1, u * 80), new[] { 1.0, 0.4, 0.15 });
		Churn(x, r, sr, 0.02, 0.9, 140, 200, 1400, 0.4);
		Patter(x, r, sr, 0.4, 2.2, 70, 0.28);
		return FinishOneShot(Wet(x, sr, 0.9, 0.45, 1.8, 0.4, 1.1), sr, -3, 300);
	}

	// ------------------------------------------------------------------ the lake at dawn

	/// <summary>
	/// A common loon, somewhere far out on the water: the long wail (variant 1), the quavering tremolo
	/// (2), or a three-note yodel (3). A pure, slightly hollow tone (a strong fundamental, a little
	/// second and third harmonic), a slow vibrato, and a lot of lake: the call rings off the far shore.
	/// </summary>
	public static double[] Loon(Rng r, int sr, int variant)
	{
		var x = Buf(sr, 6.0);
		double[] harm = { 1.0, 0.28, 0.1, 0.03 };
		double vib = r.R(4.5, 5.5), s = r.R(0.96, 1.04);
		Func<double, double, double> V = (f, t) => f * (1 + 0.006 * Math.Sin(TwoPi * vib * t));
		switch (variant)
		{
			case 1:
				AddTone(x, sr, 0.2, 1.5, u => V(s * (620 + 300 * Math.Min(1, u * 4) - 40 * u), u * 1.5), u => Env(u, 0.12, 0.25), harm);
				AddTone(x, sr, 1.8, 2.0, u => V(s * (880 + 280 * Math.Min(1, u * 3) - 170 * Math.Max(0, u - 0.6)), u * 2.0), u => Env(u, 0.1, 0.35), harm);
				break;
			case 2:
				AddTone(x, sr, 0.2, 2.4, u =>
				{
					double t = u * 2.4, q = 0.5 + 0.5 * Math.Sin(TwoPi * 8.5 * t);
					return s * (820 + 190 * q);
				}, u => Env(u, 0.08, 0.2) * (0.75 + 0.25 * Math.Sin(TwoPi * 8.5 * u * 2.4 + 1)), harm);
				break;
			default:
				AddTone(x, sr, 0.2, 1.1, u => V(s * (700 + 350 * Math.Min(1, u * 3)), u * 1.1), u => Env(u, 0.15, 0.2), harm);
				AddTone(x, sr, 1.35, 0.9, u => V(s * (1050 - 200 * u), u), u => Env(u, 0.1, 0.25), harm);
				AddTone(x, sr, 2.35, 1.5, u => V(s * (850 + 300 * Math.Min(1, u * 3) - 60 * u), u * 1.5), u => Env(u, 0.1, 0.35), harm);
				break;
		}
		LowPass(x, sr, 2600);
		return FinishOneShot(Wet(x, sr, 0.45, 0.9, 3.2, 0.35, 1.4), sr, -3, 600);
	}

	/// <summary>
	/// Water lapping at a wooden hull, heard from inside the boat: soft hollow "clocks" at an uneven
	/// pace (a little knot of them now and then as a ripple train goes by), each with a gulp of
	/// bubbles, and the odd tiny trickle. Event-built and circular, so it loops seamlessly.
	/// </summary>
	public static double[] HullLap(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double t = r.R(0, 0.5);
		while (t < sec)
		{
			int knot = r.Chance(0.25) ? r.I(2, 4) : 1;
			for (int k = 0; k < knot; k++)
			{
				double a = r.LogR(0.3, 1.0);
				Slap(x, r, sr, t, a * 0.6, 80, r.R(350, 600), r.R(0.03, 0.06), 0.006, true);
				Knock(x, r, sr, t + 0.004, a * 0.5, r.R(150, 210), 5, r.R(0.05, 0.09), true);
				for (int b = 0; b < r.I(1, 4); b++) Bubble(x, sr, t + r.R(0.01, 0.09), r.LogR(260, 720), a * r.R(0.15, 0.4), r.R(0.008, 0.02), true);
				t += r.R(0.18, 0.32);
			}
			t += r.R(0.5, 1.4);
		}
		for (double d = r.R(0, 1); d < sec; d += r.R(0.6, 2.5)) Bubble(x, sr, d, r.LogR(900, 2400), r.R(0.04, 0.1), r.R(0.004, 0.01), true);
		var h = new Hall(sr, 0.5, 0.5, 15, 0.4);
		var o = Circular(x, v => v + h.P(v) * 0.12);
		NormRms(o, -30);
		return o;
	}

	/// <summary>
	/// The lake after the breach, from a small boat in the middle of it: waves breaking into whitecaps
	/// all round at different distances (each a short washing rush of churned water), slaps on the hull,
	/// spray pattering down, and a slow heave of deep water underneath, rising and falling with the
	/// swell. Built from events on a circular buffer; the heave is a whole number of cycles per loop.
	/// </summary>
	public static double[] RoughWater(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		// whitecaps breaking nearby and further off
		for (double t = r.R(0, 1); t < sec; t += r.R(0.7, 2.0))
		{
			double near = r.U(), a = 0.25 + 0.6 * near;
			double len = r.R(0.5, 1.2);
			for (double e = 0; e < len; e += r.R(0.3, 1.7) / (90 * a))
			{
				double u = e / len, env = Env(u, 0.25, 0.6);
				if (r.Chance(0.45)) Slap(x, r, sr, t + e, a * env * r.LogR(0.15, 0.5), r.R(300, 600), 1200 + 3000 * near, r.R(0.01, 0.04), 0.004, true);
				else Bubble(x, sr, t + e, r.LogR(200, 900), a * env * r.LogR(0.08, 0.35), r.R(0.01, 0.03), true);
			}
		}
		// the hull taking a slap every so often
		for (double t = r.R(0, 2); t < sec; t += r.R(1.5, 3.5))
		{
			Slap(x, r, sr, t, 0.55, 60, 650, r.R(0.035, 0.06), 0.004, true);
			Knock(x, r, sr, t + 0.003, 0.35, r.R(160, 220), 4, 0.08, true);
		}
		Patter(x, r, sr, 0, sec, 25, 0.12, true);
		// the heave: low water swelling and falling, 3-4 s a cycle, whole cycles per loop
		int cycles = Math.Max(1, (int)Math.Round(sec / 3.4));
		var white = new double[x.Length];
		for (int i = 0; i < white.Length; i++) white[i] = r.W();
		var lp = Biquad.Lp(sr, 140); var lp2 = Biquad.Lp(sr, 160);
		var heave = Circular(white, v => lp2.P(lp.P(v)));   // filtered as if always looping: seamless
		for (int i = 0; i < heave.Length; i++)
		{
			double env = 0.5 + 0.5 * Math.Sin(TwoPi * cycles * i / heave.Length);
			heave[i] *= env * env;
		}
		var h = new Hall(sr, 1.4, 0.45, 20, 0.9);
		var o = Circular(x, v => v + h.P(v) * 0.3);
		double hg = Peak(o) * 0.35 / Math.Max(Peak(heave), 1e-12);
		for (int i = 0; i < o.Length; i++) o[i] += heave[i] * hg;
		NormRms(o, -24);
		return o;
	}
}
