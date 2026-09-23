namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Looping ambient beds. Two loop strategies:
///  * noise beds render linearly past the loop end and crossfade the tail into the head (Dsp.MakeLoop);
///  * tonal / event beds are built to be exactly periodic (frequencies are whole multiples of 1/length,
///    events are written into a circular buffer).
/// </summary>
public static class Ambient
{
	/// <summary>Soft forest wind: brown/pink noise under a gust-driven low-pass, plus a faint moving whistle band.</summary>
	public static double[] Wind(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		double T = (double)tot / sr;
		var gust = new Smooth(r, T, 2.6);
		var surge = new Smooth(r, T, 8.5);
		var flutter = new Smooth(r, T, 0.3);
		var br = new Brown(); var pk = new Pink();
		var lp1 = new Biquad(sr); var lp2 = new Biquad(sr); var wh = new Biquad(sr);
		var hp = Biquad.Hp(sr, 75);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double g = 0.55 * gust.At(t) + 0.45 * surge.At(t);
			g = g * g * (3 - 2 * g);
			double fl = flutter.At(t);
			if (i % 16 == 0)
			{
				double fc = 260 + 950 * g + 220 * fl * g;
				lp1.SetLp(fc, 0.6); lp2.SetLp(fc * 1.4, 0.6);
				wh.SetBp(260 + 300 * g + 70 * fl, 4.5);
			}
			double w = r.W();
			double body = lp2.P(lp1.P(br.P(w) * 0.25 + pk.P(w) * 0.9));
			double whistle = wh.P(r.W()) * 0.18 * g * g;
			x[i] = hp.P(body * (0.22 + 0.78 * g) + whistle);
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -21);
		return o;
	}

	/// <summary>
	/// Leaves moving in the wind: smooth, airy "shhh" swells. No crackle grains:
	/// thousands of tiny clicks a second read as a fire, not a forest.
	/// </summary>
	public static double[] Leaves(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var swell = new Smooth(r, T, 1.6);
		var slow = new Smooth(r, T, 5.0);
		var hp = Biquad.Hp(sr, 700); var lp1 = Biquad.Lp(sr, 3800); var lp2 = Biquad.Lp(sr, 4500);
		var air = Biquad.Bp(sr, 2400, 0.6);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double s = Math.Pow(0.55 * swell.At(t) + 0.45 * slow.At(t), 1.6);
			double n0 = r.W();
			// Mostly quiet, with occasional rustles: a steady hiss reads as a jet overhead.
			x[i] = (lp2.P(lp1.P(hp.P(n0))) * 0.8 + air.P(n0) * 0.3) * (0.015 + s * s * 1.6);
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -27);
		return o;
	}

	/// <summary>
	/// Cricket / katydid chorus: individual chirpers (3.5-6 kHz pulses) with their own rhythms and
	/// fading activity, plus a faint band-limited distant buzz.
	/// </summary>
	public static double[] Insects(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var chirps = new double[tot];
		int[] types = { 0, 0, 0, 1, 1, 2, 2, 0 }; // 0 cricket, 1 tree-cricket trill, 2 katydid rasp
		for (int c = 0; c < types.Length; c++)
		{
			var cr = r.Fork();
			var act = new Smooth(cr, T, cr.R(3.5, 8));
			double f = types[c] switch { 0 => cr.R(4000, 5200), 1 => cr.R(3500, 4200), _ => cr.R(5000, 6000) };
			double amp = cr.R(0.25, 1.0);
			double Act(double t) { double a = Math.Clamp((act.At(t) - 0.2) / 0.5, 0, 1); return a * a * (3 - 2 * a); }
			double t0 = cr.R(0, 1.5);
			if (types[c] == 0)
			{
				double period = 1 / cr.R(1.3, 2.8), pp = 1 / cr.R(24, 38);
				int pulses = cr.I(3, 6);
				for (double t = t0; t < T; t += period * cr.R(0.96, 1.04))
				{
					double a = amp * Act(t);
					if (a < 0.02) continue;
					for (int p = 0; p < pulses; p++)
					{
						double pa = a * (p == 0 ? 0.7 : 1.0) * cr.R(0.85, 1.0), ff = f * cr.R(0.995, 1.005);
						AddTone(chirps, sr, t + p * pp, pp * 0.62, u => ff * (1 - 0.025 * u), u => pa * Env(u, 0.25, 0.55), new[] { 1.0, 0.07 });
					}
				}
			}
			else if (types[c] == 1)
			{
				double pr = cr.R(42, 58), t = t0;
				while (t < T)
				{
					double phrase = cr.R(2.0, 6.0), end = Math.Min(T, t + phrase);
					for (double tp = t; tp < end; tp += 1 / pr)
					{
						double env = Env((tp - t) / phrase, 0.12, 0.2), pa = amp * 0.6 * Act(tp) * env;
						if (pa < 0.01) continue;
						AddTone(chirps, sr, tp, 0.55 / pr, u => f, u => pa * Env(u, 0.3, 0.5), new[] { 1.0, 0.05 });
					}
					t = end + cr.R(0.4, 2.0);
				}
			}
			else
			{
				double period = cr.R(0.8, 1.3), rasp = cr.R(160, 240);
				for (double t = t0; t < T; t += period * cr.R(0.95, 1.05))
				{
					double a = amp * 0.8 * Act(t);
					if (a < 0.02) continue;
					int syl = cr.I(2, 4);
					for (int s = 0; s < syl; s++)
					{
						double d = cr.R(0.05, 0.085), st = t + s * (d + 0.06), sa = a * cr.R(0.8, 1.0);
						AddTone(chirps, sr, st, d, u =>
						{
							double ph = Math.Cos(Math.PI * rasp * u * d);
							return f * (1 + 0.01 * ph);
						}, u =>
						{
							double ph = Math.Cos(Math.PI * rasp * u * d);
							return sa * Env(u, 0.12, 0.35) * (0.15 + 0.85 * ph * ph * ph * ph);
						}, new[] { 1.0, 0.1 });
					}
				}
			}
		}

		// Faint distant chorus: narrow noise bands, gently pulsed.
		var buzz = new double[tot];
		var b1 = Biquad.Bp(sr, 4600, 4); var b1b = Biquad.Bp(sr, 4600, 4);
		var b2 = Biquad.Bp(sr, 5500, 5); var b2b = Biquad.Bp(sr, 5500, 5);
		var bm = new Smooth(r, T, 3.0);
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double am1 = 0.65 + 0.35 * Math.Sin(TwoPi * 46 * t), am2 = 0.7 + 0.3 * Math.Sin(TwoPi * 31 * t + 1);
			buzz[i] = (b1b.P(b1.P(r.W())) * am1 + 0.7 * b2b.P(b2.P(r.W())) * am2) * (0.6 + 0.4 * bm.At(t));
		}
		Scale(buzz, 0.3 * Rms(chirps) / Rms(buzz));

		var x = new double[tot];
		for (int i = 0; i < tot; i++) x[i] = chirps[i] + buzz[i];
		HighPass(x, sr, 1500);
		LowPass(x, sr, 9500);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -29);
		return o;
	}

	/// <summary>Far-off bed: sub rumble + low air. (No bird texture: dulled synthetic chirps read as flutes.)</summary>
	public static double[] Distant(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 3 * sr, pre = sr, tot = pre + n + xf;
		double T = (double)tot / sr;
		var m1 = new Smooth(r, T, 7); var m2 = new Smooth(r, T, 12.5);
		var br = new Brown(); var pk = new Pink();
		var rl1 = Biquad.Lp(sr, 70); var rl2 = Biquad.Lp(sr, 110);
		var air = Biquad.Bp(sr, 280, 0.6);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double rum = rl2.P(rl1.P(br.P(r.W())));
			double a = air.P(pk.P(r.W()));
			x[i] = rum * (0.6 + 0.4 * m1.At(t)) + a * 0.35 * (0.4 + 0.6 * m2.At(t));
		}
		HighPass(x, sr, 16);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -30);
		return o;
	}

	/// <summary>Small creek: dense population of rising-pitch "bubble" resonances over band noise and gurgle.</summary>
	public static double[] Stream(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var turb = new Smooth(r, T, 0.07); var flow = new Smooth(r, T, 1.1);
		var nb = Biquad.Bp(sr, 900, 0.5); var gl = Biquad.Lp(sr, 500);
		var br = new Brown();
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr, tu = turb.At(t), fl = flow.At(t);
			x[i] = nb.P(r.W()) * 0.09 * (0.4 + tu) + gl.P(br.P(r.W())) * 0.12 * (0.4 + 0.6 * fl);
		}
		var bub = new double[tot];
		double tb = 0;
		while (tb < T)
		{
			double fl = flow.At(tb);
			tb += -Math.Log(1 - r.U()) / (70 + 150 * fl * fl);
			double f0 = 320 * Math.Pow(2600.0 / 320, Math.Pow(r.U(), 1.5));
			double d = r.R(0.012, 0.045) * Math.Pow(1000 / f0, 0.35);
			double rise = r.R(0.15, 0.9), a = Math.Pow(r.U(), 2) * r.R(0.3, 1) * Math.Pow(1000 / f0, 0.25);
			AddTone(bub, sr, tb, d, u => f0 * (1 + rise * u), u =>
			{
				double at = u < 0.05 ? Math.Pow(Math.Sin(0.5 * Math.PI * u / 0.05), 2) : 1;
				return a * at * Math.Exp(-4 * u) * (1 - u);
			});
		}
		for (int i = 0; i < tot; i++) x[i] += bub[i] * 0.4;
		HighPass(x, sr, 90);
		LowPass(x, sr, 7000);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -24);
		return o;
	}

	/// <summary>
	/// A large still lake from the shore: no current, so none of Stream's turbulence or bubbling -
	/// just a very soft, low hush of air over open water and an occasional round, low lap against
	/// the bank (never a bright tick; a lake's lap is a soft "vwomp", not a stream's "plink").
	/// Deliberately quiet and plain: the point is to sit under a scene unnoticed, not perform.
	/// </summary>
	public static double[] Lake(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = 2 * sr, pre = sr / 2, tot = pre + n + xf;
		double T = (double)tot / sr;
		var breathe = new Smooth(r, T, 14.0);
		var hp = Biquad.Hp(sr, 35); var lp1 = Biquad.Lp(sr, 320); var lp2 = Biquad.Lp(sr, 400);
		var br = new Brown();
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr;
			double b = 0.6 + 0.4 * breathe.At(t);
			x[i] = hp.P(lp2.P(lp1.P(br.P(r.W())))) * 0.05 * b;
		}
		var rl = r.Fork();
		foreach (double t in Poisson(rl, T, 0.4, _ => 1))
		{
			double a = 0.1 * rl.LogR(0.35, 1);
			Tick(x, rl, sr, t, a, rl.LogR(160, 340), rl.R(0.5, 0.9), rl.LogR(0.02, 0.045), 0.02);
			Body(x, rl, sr, t, a * 0.85, rl.R(85, 150), rl.R(0.05, 0.11));
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -34);
		return o;
	}

	/// <summary>
	/// A periodic random control signal in roughly 0..1: random-phase sines with whole cycles per loop,
	/// nothing faster than <paramref name="minPeriod"/> seconds. Loops exactly, unlike <see cref="Smooth"/>.
	/// </summary>
	static Func<double, double> PeriodicSmooth(Rng r, double sec, double minPeriod)
	{
		int k = Math.Max(1, (int)(sec / minPeriod));
		var amp = new double[k]; var ph = new double[k];
		for (int i = 0; i < k; i++) { amp[i] = Math.Pow(i + 1, -0.8) * r.R(0.5, 1.0); ph[i] = r.R(0, TwoPi); }
		double Raw(double t) { double v = 0; for (int i = 0; i < k; i++) v += amp[i] * Math.Sin(TwoPi * (i + 1) * t / sec + ph[i]); return v; }
		double lo = double.MaxValue, hi = double.MinValue;
		for (double t = 0; t < sec; t += minPeriod / 16) { double v = Raw(t); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
		return t => Math.Clamp((Raw(t) - lo) / Math.Max(hi - lo, 1e-9), 0, 1);
	}

	/// <summary>
	/// One band-limited noise impulse (a drop, a pop) added at <paramref name="t"/> seconds, wrapping.
	/// Each event is scaled so its own peak is exactly <paramref name="amp"/>: filtered noise has random
	/// peaks, and without this a few freak events would set the whole file's level.
	/// </summary>
	static void Tick(double[] buf, Rng r, int sr, double t, double amp, double fc, double q, double tau, double attack = 0.00006)
	{
		var bp = Biquad.Bp(sr, fc, q);
		var ev = new double[(int)((tau * 7 + attack + 0.002) * sr)];
		for (int i = 0; i < ev.Length; i++) ev[i] = bp.P(r.W()) * Perc((double)i / sr, attack, tau);
		AddWrapped(buf, ev, (int)(t * sr), amp / Math.Max(Peak(ev), 1e-12));
	}

	/// <summary>A soft low-passed noise "give" under a drop or pop (a leaf flexing, wood thickness). Noise, never a tuned mode. Peak = <paramref name="amp"/>.</summary>
	static void Body(double[] buf, Rng r, int sr, double t, double amp, double fc, double tau)
	{
		var lp1 = Biquad.Lp(sr, fc); var lp2 = Biquad.Lp(sr, fc * 1.3);
		var ev = new double[(int)((tau * 7 + 0.004) * sr)];
		for (int i = 0; i < ev.Length; i++) ev[i] = lp2.P(lp1.P(r.W())) * Perc((double)i / sr, 0.0008, tau);
		AddWrapped(buf, ev, (int)(t * sr), amp / Math.Max(Peak(ev), 1e-12));
	}

	/// <summary>Poisson event times over [0, sec) with rate <paramref name="rate"/>·density(t) (thinning).</summary>
	static IEnumerable<double> Poisson(Rng r, double sec, double rate, Func<double, double> density)
	{
		double t = 0;
		while (true)
		{
			t += -Math.Log(1 - r.U()) / rate;
			if (t >= sec) yield break;
			if (r.U() < density(t)) yield return t;
		}
	}

	/// <summary>
	/// The storm's rain, heard from under the canopy. There is deliberately no noise bed: a filtered-noise
	/// rain read as surf, static or a jet. It is built from individual drops instead:
	///  * near drops on leaves around you (about one a second): a sharp tick, sometimes a soft leaf "give"
	///    under it, sometimes a short run of drips as the water runs off the leaf;
	///  * mid-distance drops (~9 a second): smaller, a little duller;
	///  * far patter (~60 a second): tiny, dulled, mostly reverb, so the forest sounds wet all the way out;
	///  * a very low, very soft hush (under 300 Hz, about 26 dB under the drops) for weight.
	/// Density swells slowly (36-90 s), never fast enough to sound like waves. The loop is long (3 min)
	/// because the storm lasts 10-15 minutes, and every event is written circularly so it loops exactly.
	/// </summary>
	public static double[] RainSparse(Rng r, int sr, int sec)
	{
		int n = sec * sr;
		var dens = PeriodicSmooth(r.Fork(), sec, 36);
		double D(double t) => 0.45 + 0.55 * dens(t);
		var far = new double[n]; var mid = new double[n]; var near = new double[n];

		var rf = r.Fork();
		foreach (double t in Poisson(rf, sec, 60, D))
			Tick(far, rf, sr, t, 0.2 * rf.LogR(0.25, 1), rf.LogR(1200, 3400), 1.2, rf.LogR(0.0006, 0.0025));

		var rm = r.Fork();
		foreach (double t in Poisson(rm, sec, 7, D))
		{
			double a = 0.45 * rm.LogR(0.2, 1);
			Tick(mid, rm, sr, t, a, rm.LogR(1800, 5000), rm.R(1.4, 3.0), rm.LogR(0.0006, 0.0025));
			if (rm.Chance(0.2)) Body(mid, rm, sr, t, a * 0.5, rm.R(350, 650), rm.R(0.003, 0.006));
		}

		var rn = r.Fork();
		foreach (double t in Poisson(rn, sec, 0.8, D))
		{
			double a = 0.35 * rn.LogR(0.3, 1);
			Tick(near, rn, sr, t, a, rn.LogR(2500, 6000), rn.R(1.2, 2.4), rn.LogR(0.0006, 0.002));
			if (rn.Chance(0.45)) Body(near, rn, sr, t, a * 0.45, rn.R(400, 800), rn.R(0.003, 0.007));
			if (rn.Chance(0.3))
			{
				// Water running off the leaf: a few smaller drips, slowing down.
				double td = t, gap = rn.R(0.05, 0.12), ad = a;
				for (int k = rn.I(1, 4); k > 0; k--)
				{
					td += gap; gap *= rn.R(1.3, 1.9); ad *= rn.R(0.35, 0.6);
					Tick(near, rn, sr, td, ad, rn.LogR(2500, 6000), rn.R(1.2, 2.4), rn.LogR(0.0004, 0.0012));
				}
			}
		}

		// Distance: far drops lose their top and sit mostly in the reverb; near ones are almost dry.
		var fl1 = Biquad.Lp(sr, 2400); var fl2 = Biquad.Lp(sr, 2800);
		var farD = Circular(far, v => fl2.P(fl1.P(v)));
		var ml = Biquad.Lp(sr, 5200);
		var midD = Circular(mid, ml.P);
		var send = new double[n];
		for (int i = 0; i < n; i++) send[i] = farD[i] * 0.8 + midD[i] * 0.3 + near[i] * 0.08;
		var hall = new Hall(sr, 1.4, 0.55, 12, 0.8);
		var wet = Circular(send, hall.P);

		var drops = new double[n];
		for (int i = 0; i < n; i++) drops[i] = farD[i] * 0.55 + midD[i] + near[i] + wet[i] * 0.9;
		var dh = Biquad.Hp(sr, 150);
		drops = Circular(drops, dh.P);

		// The hush: very low, very soft, barely moving (a fast-swelling low noise reads as surf).
		var hush = new double[n];
		for (int i = 0; i < n; i++) hush[i] = r.W();
		var h1 = Biquad.Lp(sr, 220); var h2 = Biquad.Lp(sr, 280); var h3 = Biquad.Hp(sr, 50);
		hush = Circular(hush, v => h3.P(h2.P(h1.P(v))));
		Scale(hush, FromDb(-26) * Rms(drops) / Math.Max(Rms(hush), 1e-12));

		var o = new double[n];
		for (int i = 0; i < n; i++) o[i] = drops[i] + hush[i] * (0.8 + 0.2 * D((double)i / sr));
		NormRms(o, -32, -6);
		return o;
	}

	/// <summary>
	/// Steady rain under the trees (the Act 3-5 storm), replacing <see cref="RainSparse"/>, whose
	/// sixty drops a second read as a crackling fire. Real rain in a wood is thousands of tiny drops
	/// a second on the canopy, blended by distance into a soft, grainy patter; this is built the same
	/// way, from drops, so it never becomes a flat hiss:
	///  * canopy patter: ~1100 drops a second, each a 1-2 ms tick in the 1.5-4.5 kHz band, dulled and
	///    partly in the reverb, so the whole wood sounds wet;
	///  * nearer drops (~50 a second): a little bigger and lower, some with a soft leaf "give";
	///  * drops on the leaves right around the player (2-3 a second): a sharp tick, a body, sometimes
	///    a short run of drips as the water runs off.
	/// The density breathes by a quarter over 40-90 s, never fast enough to sound like waves, and
	/// there is no low hush (a low bed reads as surf or an engine). Three minutes long because the
	/// storm lasts a while; every event is written circularly so it loops exactly.
	/// </summary>
	public static double[] Rain(Rng r, int sr, int sec)
	{
		int n = sec * sr;
		var dens = PeriodicSmooth(r.Fork(), sec, 40);
		double D(double t) => 0.75 + 0.25 * dens(t);
		var far = new double[n]; var mid = new double[n]; var near = new double[n];

		// Duller and rounder than the canopy patter above: rain landing on soil has no crisp leaf
		// "tick", just a soft, low plip and a bit of wet body. Longer attacks and lower, wider
		// bands than a leaf-drop keep every layer out of the popcorn-kernel "pop" register.
		var rf = r.Fork();
		foreach (double t in Poisson(rf, sec, 935, D))
			Tick(far, rf, sr, t, 0.14 * rf.LogR(0.3, 1), rf.LogR(500, 1600), rf.R(0.8, 1.5), rf.LogR(0.0009, 0.003), 0.0003);

		var rm = r.Fork();
		foreach (double t in Poisson(rm, sec, 42, D))
		{
			double a = 0.32 * rm.LogR(0.25, 1);
			Tick(mid, rm, sr, t, a, rm.LogR(450, 1400), rm.R(0.8, 1.5), rm.LogR(0.0012, 0.0035), 0.0004);
			if (rm.Chance(0.7)) Body(mid, rm, sr, t, a * 0.7, rm.R(220, 450), rm.R(0.004, 0.008));
		}

		var rn = r.Fork();
		foreach (double t in Poisson(rn, sec, 3.4, D))
		{
			double a = 0.32 * rn.LogR(0.3, 1);
			Tick(near, rn, sr, t, a, rn.LogR(550, 1600), rn.R(0.7, 1.3), rn.LogR(0.0012, 0.0035), 0.0005);
			if (rn.Chance(0.85)) Body(near, rn, sr, t, a * 0.75, rn.R(200, 420), rn.R(0.005, 0.01));
			if (rn.Chance(0.25))
			{
				double td = t, gap = rn.R(0.05, 0.12), ad = a;
				for (int k = rn.I(1, 4); k > 0; k--)
				{
					td += gap; gap *= rn.R(1.3, 1.9); ad *= rn.R(0.35, 0.6);
					Tick(near, rn, sr, td, ad, rn.LogR(550, 1600), rn.R(0.7, 1.3), rn.LogR(0.0008, 0.002), 0.0004);
				}
			}
		}

		// Distance and the trees between: the patter loses its top and its bottom and sits partly in the reverb.
		var fl1 = Biquad.Lp(sr, 2200); var fl2 = Biquad.Lp(sr, 2800); var fh = Biquad.Hp(sr, 300);
		var farD = Circular(far, v => fh.P(fl2.P(fl1.P(v))));
		var ml = Biquad.Lp(sr, 3200);
		var midD = Circular(mid, ml.P);
		var send = new double[n];
		for (int i = 0; i < n; i++) send[i] = farD[i] * 0.6 + midD[i] * 0.3 + near[i] * 0.08;
		var hall = new Hall(sr, 1.2, 0.6, 12, 0.8);
		var wet = Circular(send, hall.P);
		var o = new double[n];
		for (int i = 0; i < n; i++) o[i] = farD[i] * 0.8 + midD[i] + near[i] + wet[i] * 0.7;
		var dh = Biquad.Hp(sr, 150);
		o = Circular(o, dh.P);
		NormRms(o, -30, -6);
		return o;
	}

	/// <summary>
	/// Act 7's burning cabin: irregular pops, snaps and a soft low roar, with no hiss anywhere
	/// (a noise hiss read as static). Pops come in clusters with quiet gaps between them and a
	/// power-law spread of sizes (lots of tiny ones, a few big); every few seconds a piece of
	/// wood splits with a snap and a flurry of embers. The roar is under 300 Hz and breathes slowly.
	/// </summary>
	public static double[] FireCrackle(Rng r, int sr, int sec)
	{
		int n = sec * sr;
		var crack = new double[n];
		double Size(Rng g) => Math.Min(0.4, 0.06 * Math.Pow(1 - g.U(), -1 / 2.5));
		void Pop(Rng g, double t, double a)
		{
			Tick(crack, g, sr, t, a, g.LogR(900, 4800), g.R(0.7, 1.8), g.LogR(0.0003, 0.0015), 0.00003);
			if (a > 0.3) Body(crack, g, sr, t, a * 0.35, g.R(500, 1100), g.R(0.002, 0.004));
			if (a > 0.2)
				for (int k = g.I(0, 3); k > 0; k--)
					Tick(crack, g, sr, t + g.R(0.001, 0.007), a * g.R(0.15, 0.4), g.LogR(1200, 5000), 1.2, g.LogR(0.00015, 0.0005), 0.00003);
		}

		// Crackle bursts: clusters of pops, most small.
		var rc = r.Fork();
		var heat = PeriodicSmooth(rc.Fork(), sec, 3);
		foreach (double t0 in Poisson(rc, sec, 1.3, t => 0.4 + 0.6 * heat(t)))
		{
			double t = t0, level = rc.R(0.4, 1.0);
			for (int k = rc.I(4, 28); k > 0; k--)
			{
				Pop(rc, t, Size(rc) * level);
				t += -Math.Log(1 - rc.U()) * rc.R(0.015, 0.05);
			}
		}
		// Isolated pops between the bursts.
		var ri = r.Fork();
		foreach (double t in Poisson(ri, sec, 2.5, t => 1)) Pop(ri, t, Size(ri));
		// Fine crackle: tiny, dull ticks while the heat is up. Sparse enough (well under 5% duty) to stay grainy, never a hiss.
		var rf = r.Fork();
		foreach (double t in Poisson(rf, sec, 22, t => 0.25 + 0.75 * heat(t)))
			Tick(crack, rf, sr, t, 0.02 * rf.LogR(0.3, 1), rf.LogR(1000, 3000), 1.0, rf.LogR(0.0003, 0.001), 0.00005);
		// Snaps: wood splitting, then a flurry of small embers.
		var rs = r.Fork();
		foreach (double t in Poisson(rs, sec, 1 / 5.5, t => 1))
		{
			var hp = Biquad.Hp(sr, rs.R(700, 1200)); var sp = Biquad.Bp(sr, rs.R(1800, 3600), 0.9);
			double tauN = rs.R(0.0008, 0.002);
			AddEvent(crack, (int)(t * sr), (int)(0.05 * sr), i =>
			{
				double tt = (double)i / sr;
				return hp.P(rs.W()) * Perc(tt, 0.00008, tauN) * 0.45 + sp.P(rs.W()) * Perc(tt, 0.0001, 0.005) * 0.25;
			});
			Body(crack, rs, sr, t, 0.2, rs.R(300, 600), rs.R(0.004, 0.008));
			double te = t + 0.01;
			for (int k = rs.I(5, 14); k > 0; k--) { te += rs.R(0.004, 0.03); Pop(rs, te, Size(rs) * 0.6); }
		}
		var cl = Biquad.Lp(sr, 7000); var ch = Biquad.Hp(sr, 250);
		crack = Circular(crack, v => ch.P(cl.P(v)));

		// Roar: low, soft, slowly breathing. Under 300 Hz only, so it's warmth, not hiss.
		var roar = new double[n];
		for (int i = 0; i < n; i++) roar[i] = r.W();
		var r1 = Biquad.Lp(sr, 170); var r2 = Biquad.Lp(sr, 230); var r3 = Biquad.Hp(sr, 35);
		roar = Circular(roar, v => r3.P(r2.P(r1.P(v))));
		var breath = PeriodicSmooth(r.Fork(), sec, 0.8);
		for (int i = 0; i < n; i++) { double b = breath((double)i / sr); roar[i] *= 0.35 + 0.65 * b * b; }
		Scale(roar, FromDb(-4) * Rms(crack) / Math.Max(Rms(roar), 1e-12));

		var mix = new double[n];
		for (int i = 0; i < n; i++) mix[i] = crack[i] + roar[i];
		var room = new Hall(sr, 0.6, 0.5, 8, 0.6);
		var wet = Circular(mix, room.P);
		for (int i = 0; i < n; i++) mix[i] += wet[i] * 0.2;
		NormRms(mix, -28, -3);
		return mix;
	}

	// ---------------------------------------------------------------- choir

	/// <summary>
	/// Plan for one chanted phrase: start time, unison note (female; the men sing an octave lower),
	/// level and tempo. <paramref name="Voices"/> caps how many singers take part (a phrase can be one
	/// lone voice); <paramref name="Lead"/> lets one singer come in alone ahead of the rest.
	/// </summary>
	public sealed record ChantPhrase(double At, int Midi, double Level, double Tempo, int Voices = 99, bool Lead = false);

	/// <summary>
	/// Renders the chant: every singer sings the planned phrases they take part in, with their own
	/// habit of coming in early or late, their own tuning, and wavering. Not every singer joins every
	/// phrase (<paramref name="join"/>), a phrase can be cast small, and on a <see cref="ChantPhrase.Lead"/>
	/// phrase one voice starts well ahead of the others, so the unison is never a block.
	/// </summary>
	public static double[] ChantDry(Rng r, int sr, int n, IList<ChantPhrase> plan, int men, int women, double join = 0.8, double wobble = 1.0)
	{
		var dry = new double[n];
		int total = men + women;
		static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);
		// Casting: who sings each phrase, and who (if anyone) leads it in.
		var rc = r.Fork();
		var cast = new bool[plan.Count, total]; var lead = new int[plan.Count];
		for (int p = 0; p < plan.Count; p++)
		{
			int want = Math.Min(total, plan[p].Voices);
			var order = Enumerable.Range(0, total).OrderBy(_ => rc.U()).ToArray();
			for (int k = 0; k < want; k++) cast[p, order[k]] = true;
			lead[p] = plan[p].Lead ? order[0] : -1;
		}
		for (int s = 0; s < total; s++)
		{
			var rs = r.Fork();
			bool female = s >= men;
			double detune = Math.Pow(2, rs.R(-28, 28) * wobble / 1200);   // everyone a little off (a couple of percent at full wobble)
			double habit = (rs.U() + rs.U() - 1) * 0.14 * wobble;   // this singer always comes in a touch early or late
			double eager = rs.R(0.5, 1.0);   // and always a bit under or over the others
			var takes = new List<Voice.Take>();
			for (int p = 0; p < plan.Count; p++)
			{
				var ph = plan[p];
				bool isLead = lead[p] == s;
				if (!cast[p, s] || (!isLead && !rs.Chance(join))) continue;
				double late = isLead ? -rs.R(0.4, 1.0) : habit + (rs.U() + rs.U() + rs.U() - 1.5) * 0.13 * wobble;
				double f0 = Hz(ph.Midi - (female ? 0 : 12)) * detune * Math.Pow(2, rs.R(-8, 8) * wobble / 1200);   // and lands a little differently each time
				takes.Add(new Voice.Take(ph.At + late, ph.Tempo * rs.R(0.94, 1.06), f0,
					ph.Level * eager * rs.R(0.7, 1.0) * (female ? 0.8 : 1.0) * (isLead ? 1.15 : 1.0), -rs.R(15, 110)));
			}
			Voice.Sing(dry, sr, rs, female, takes, wobble);
		}
		return dry;
	}

	/// <summary>
	/// Act 10's maze: "come and see" chanted, low and far off, by a choir of unsteady voices in the
	/// dark. Formant-synthesised singers (five basses, five altos an octave up, all pitched deep and
	/// played back deeper still), each with their own habit of coming in early or late, a couple of
	/// percent sharp or flat, their own irregular vibrato setting in at their own moment, their own
	/// throat and vowels; the long "see" sags flat by a different amount in every voice. Sparse: a
	/// phrase every 6-14 s with real silence between (no grid: the spacings are drawn at random and
	/// scaled to fit the loop), the phrases short, the level swelling and receding slowly over the
	/// loop, some phrases cast to a few voices or a single one, some led in by one voice before the
	/// rest follow. Mostly on G, twice a semitone off. 108 s, exactly periodic (phrases and reverb wrap).
	/// </summary>
	public static double[] ChoirChant(Rng r, int sr, int sec)
	{
		int n = sec * sr, phrases = 10;
		// Spacings 6-14 s, scaled so the loop closes; phrases short (tempo 0.8-0.95).
		var gap = new double[phrases]; double sum = 0;
		for (int p = 0; p < phrases; p++) { gap[p] = r.R(6, 14); sum += gap[p]; }
		double scale = sec / sum;
		int[] note = { 55, 55, 55, 56, 55, 55, 54, 55, 55, 55 };   // G3 for the altos, G2 for the basses (x0.66 in game)
		var plan = new List<ChantPhrase>();
		double at = r.R(0, 2), level = r.R(0.5, 0.8);
		for (int p = 0; p < phrases; p++)
		{
			level = 0.55 * level + 0.45 * r.R(0.35, 1.0);   // a slow swell, never a table
			int voices = r.U() < 0.2 ? r.I(1, 3) : r.U() < 0.3 ? r.I(4, 6) : 99;
			bool lead = voices > 3 && r.U() < 0.4;
			plan.Add(new ChantPhrase(at, note[p], level, r.R(0.8, 0.95), voices, lead));
			at += gap[p] * scale;
		}
		var dry = ChantDry(r, sr, n, plan, 5, 5, 0.7, 2.0);
		return ChoirSpace(dry, sr);
	}

	/// <summary>Far away: no low end, a dulled top, heard mostly through a big soft hall. Circular, so the loop stays exact.</summary>
	public static double[] ChoirSpace(double[] dry, int sr)
	{
		int n = dry.Length;
		var hp = Biquad.Hp(sr, 70); var lp = Biquad.Lp(sr, 4200);
		dry = Circular(dry, v => lp.P(hp.P(v)));
		var hall = new Hall(sr, 5.0, 0.6, 80, 2.0);
		var wet = Circular(dry, hall.P);
		var o = new double[n];
		for (int i = 0; i < n; i++) o[i] = dry[i] * 0.3 + wet[i] * 1.0;
		var h2 = Biquad.Hp(sr, 55);
		o = Circular(o, h2.P);
		NormRms(o, -25, -6);
		return o;
	}

	/// <summary>
	/// The stairs' hum (Act 2: barely audible, growing as you get close; Act 11: very loud on the
	/// climb). A deep, sustained tone on D1 (36.7 Hz) with a soft harmonic series up to ~370 Hz so
	/// it is still a hum, not just pressure, on small speakers. Pure partials, random phases and
	/// independent swells of 13-40 s, so it never pulses (no engine firing rhythm, no noise). Each
	/// partial has a slightly detuned twin 1-2 cycles per loop away: a 20-40 s slow turning, far
	/// slower than breathing. Every frequency is a whole number of cycles per loop, so it loops exactly
	/// and stays clean however far it is turned up.
	/// </summary>
	public static double[] StairsHum(Rng r, int sr, int sec)
	{
		int n = sec * sr;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;
		double f0 = F(36.7);
		double[] amp = { 1, 0.8, 0.6, 0.45, 0.32, 0.22, 0.15, 0.1, 0.07, 0.05 };
		int H = amp.Length;
		var twin = new double[H]; var ph = new double[H]; var ph2 = new double[H]; var sw = new double[H]; var cyc = new int[H];
		for (int h = 0; h < H; h++)
		{
			twin[h] = (h + 1) * f0 + r.I(1, 3) * q;
			ph[h] = r.R(0, TwoPi); ph2[h] = r.R(0, TwoPi); sw[h] = r.R(0, TwoPi); cyc[h] = r.I(1, 4);
		}
		var x = new double[n];
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr, v = 0;
			for (int h = 0; h < H; h++)
			{
				double swell = 0.65 + 0.35 * Math.Sin(TwoPi * cyc[h] * q * t + sw[h]);
				v += amp[h] * swell * (Math.Sin(TwoPi * (h + 1) * f0 * t + ph[h]) + 0.6 * Math.Sin(TwoPi * twin[h] * t + ph2[h]));
			}
			x[i] = v;
		}
		NormRms(x, -20, -3);
		return x;
	}

	/// <summary>
	/// Real FM inter-station static (Dan, 2026-09-22, from the reference samples): plain white noise
	/// rushing through the handheld's little speaker, 280 Hz to 3.6 kHz with a broad bright hump
	/// around 2 kHz, a slow swell, and sparse crackles. No dark "hiss": the old 1.4 kHz honk and the
	/// 2.6 kHz roll-off made it nasal. Played under every radio line (Act 11) and gated with it.
	/// </summary>
	public static double[] RadioStatic(Rng r, int sr, double sec)
	{
		int n = (int)(sec * sr), xf = (int)(0.3 * sr), pre = (int)(0.2 * sr), tot = pre + n + xf;
		double T = (double)tot / sr;
		var x = new double[tot];
		var hp = Biquad.Hp(sr, 280); var lp = Biquad.Lp(sr, 3600);
		var hump = Biquad.Bp(sr, 2100, 0.5);   // the bright presence of a small speaker on white noise
		var swell = new Smooth(r, T, 0.5);
		for (int i = 0; i < tot; i++)
		{
			double t = (double)i / sr, w = r.W();
			x[i] = (lp.P(hp.P(w)) + 0.7 * hump.P(w)) * (0.85 + 0.15 * swell.At(t));
		}
		// Sparse crackles riding the rush: the receiver catching the edge of something.
		int pops = (int)(sec * 1.5);
		for (int p = 0; p < pops; p++)
		{
			double at = r.R(0, sec);
			var pf = Biquad.Bp(sr, r.R(900, 2600), 2.0);
			double dur = r.R(0.015, 0.05);
			int len = (int)(dur * sr);
			for (int i = 0; i < len; i++)
			{
				double tt = (double)i / sr;
				int idx = pre + ((int)(at * sr) + i) % n;
				x[idx] += pf.P(r.W()) * Perc(tt, 0.001, dur * 0.4) * r.R(0.5, 1.0) * 1.2;
			}
		}
		HighPass(x, sr, 280);
		var loop = MakeLoop(x, pre, n, xf);
		NormRms(loop, -22);
		return loop;
	}

	/// <summary>
	/// The old walkie-talkie noise, kept as the stalker's jumpscare (Dan, 2026-09-22: the super
	/// loud static burst the radio used to make was the scariest sound in the build). The hiss
	/// and crackle of the old <see cref="RadioStatic"/>, denser, swelling hard, as a 2.6 s one-shot
	/// normalised hot; the game plays it on the Radio bus right at the ear.
	/// </summary>
	public static double[] RadioBurst(Rng r, int sr)
	{
		double sec = 2.6;
		int n = (int)(sec * sr);
		var x = new double[n];
		var hiss = Biquad.Bp(sr, 3200, 0.7);
		var swell = new Smooth(r, sec, 0.25);
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr;
			double env = Perc(t, 0.01, 1.4) * (0.55 + 0.45 * swell.At(t));
			x[i] = hiss.P(r.W()) * env;
		}
		int pops = (int)(sec * 9);
		for (int p = 0; p < pops; p++)
		{
			double at = r.R(0, sec - 0.05);
			var pf = Biquad.Bp(sr, r.R(800, 2200), 3.5);
			double dur = r.R(0.01, 0.03);
			int len = (int)(dur * sr);
			for (int i = 0; i < len; i++)
			{
				double tt = (double)i / sr;
				int idx = (int)(at * sr) + i;
				if (idx >= n) break;
				x[idx] += pf.P(r.W()) * Perc(tt, 0.0003, dur * 0.5) * r.R(0.5, 1.0) * 1.8 * Perc(at, 0.01, 1.6);
			}
		}
		HighPass(x, sr, 400);
		return FinishOneShot(x, sr, -3, 120);
	}

	/// <summary>"Silence ringing": thin ~8.2 kHz sine, a 0.25 Hz beating partner for the wobble, all periodic.</summary>
	public static double[] Ringing(int sr, int sec)
	{
		int n = sec * sr;
		double q = 1.0 / sec;
		var x = new double[n];
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr;
			double v = Math.Sin(TwoPi * 8200 * t + 0.9 * Math.Sin(TwoPi * 3 * q * t)) * (1 + 0.12 * Math.Sin(TwoPi * 2 * q * t))
				+ 0.18 * Math.Sin(TwoPi * (8200 + 5 * q) * t)
				+ 0.05 * (0.5 + 0.5 * Math.Sin(TwoPi * q * t)) * Math.Sin(TwoPi * 7340 * t);
			x[i] = v;
		}
		NormPeak(x, -26);
		return x;
	}

	static double[] Inhale(Rng r, int sr, double dur)
	{
		int len = (int)(dur * sr), warm = 2048;
		var hp = Biquad.Hp(sr, 450); var b1 = Biquad.Bp(sr, r.R(1900, 2400), 0.8); var b2 = Biquad.Bp(sr, r.R(1000, 1250), 1.6);
		var lp = Biquad.Lp(sr, 5000);
		var tex = new Smooth(r, dur + 1, 0.07);
		var o = new double[len];
		for (int i = -warm; i < len; i++)
		{
			double w = hp.P(r.W());
			double v = lp.P(b1.P(w) + 0.6 * b2.P(w));
			if (i < 0) continue;
			double u = (double)i / len;
			// Swells up, peaks late, then stops fairly quickly (the lungs fill).
			double env = Env(u, 0.7, 0.28) * (1 + 0.25 * u);
			o[i] = v * env * (0.82 + 0.18 * tex.At((double)i / sr));
		}
		return o;
	}

	static double[] Exhale(Rng r, int sr, double dur)
	{
		int len = (int)(dur * sr), warm = 2048;
		var lp1 = Biquad.Lp(sr, r.R(1300, 1700)); var b1 = Biquad.Bp(sr, r.R(550, 750), 0.7);
		var hp = Biquad.Hp(sr, 180);
		var tex = new Smooth(r, dur + 1, 0.09);
		var o = new double[len];
		for (int i = -warm; i < len; i++)
		{
			double w = r.W();
			double v = hp.P(lp1.P(w) * 0.8 + b1.P(w) * 0.9);
			if (i < 0) continue;
			double u = (double)i / len;
			// Quick-ish onset, long relaxed decay.
			double env = Env(u, 0.18, 0.8) * (1.1 - 0.4 * u);
			o[i] = v * env * (0.85 + 0.15 * tex.At((double)i / sr));
		}
		return o;
	}

	/// <summary>
	/// The air near a staircase: a shapeless low pressure, more felt than heard.
	/// Deliberately has no pulse. The old drone's 0.22 Hz beating read as
	/// breathing, so nothing here repeats faster than every 13 s, and the tones
	/// never sit close enough together to beat.
	/// </summary>
	public static double[] Pressure(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;   // whole cycles per loop
		double f1 = F(29.0), f2 = F(43.6), f3 = F(61.3);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi), a3 = r.R(0, TwoPi), a4 = r.R(0, TwoPi);
		var lp1 = Biquad.Lp(sr, 48); var lp2 = Biquad.Lp(sr, 55);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			// Slow, uneven swells built from 40 s and 13.3 s components only.
			double swellA = 0.55 + 0.28 * Math.Sin(TwoPi * q * t + a1) + 0.17 * Math.Sin(TwoPi * 3 * q * t + a2);
			double swellB = 0.5 + 0.3 * Math.Sin(TwoPi * 2 * q * t + a3) + 0.2 * Math.Sin(TwoPi * q * t + a4);
			double rumble = lp2.P(lp1.P(r.W())) * 6.0;
			double tones = 0.5 * Math.Sin(TwoPi * f1 * t)
				+ 0.28 * swellB * Math.Sin(TwoPi * f2 * t + 0.4 * Math.Sin(TwoPi * q * t))
				+ 0.08 * (1 - swellB) * Math.Sin(TwoPi * f3 * t);
			x[i] = rumble * swellA + tones * (0.35 + 0.65 * swellA * swellB);
		}
		HighPass(x, sr, 16);
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -24, -8);
		return o;
	}

	/// <summary>
	/// A low, constant wrongness under the whole forest: two tones a tritone apart
	/// (they never resolve and never beat into a pulse), faint octaves so small
	/// speakers still carry it, and a dark rumble. Swells are 16 s or slower.
	/// </summary>
	public static double[] Undertone(Rng r, int sr, int sec)
	{
		int n = sec * sr, pre = 2 * sr, xf = 3 * sr, tot = pre + n + xf;
		double q = 1.0 / sec;
		double F(double hz) => Math.Round(hz / q) * q;   // whole cycles per loop
		double f1 = F(55.0), f2 = F(77.8), f3 = F(110.0), f4 = F(155.6);
		double a1 = r.R(0, TwoPi), a2 = r.R(0, TwoPi), a3 = r.R(0, TwoPi);
		var lp1 = Biquad.Lp(sr, 90); var lp2 = Biquad.Lp(sr, 110); var hp = Biquad.Hp(sr, 30);
		var x = new double[tot];
		for (int i = 0; i < tot; i++)
		{
			double t = (double)(i - pre) / sr;
			double sA = 0.6 + 0.25 * Math.Sin(TwoPi * q * t + a1) + 0.15 * Math.Sin(TwoPi * 3 * q * t + a2);
			double sB = 0.55 + 0.3 * Math.Sin(TwoPi * 2 * q * t + a3) + 0.15 * Math.Sin(TwoPi * q * t + a2);
			double tones = 0.55 * sA * Math.Sin(TwoPi * f1 * t)
				+ 0.4 * sB * Math.Sin(TwoPi * f2 * t)
				+ 0.12 * sA * Math.Sin(TwoPi * f3 * t)
				+ 0.08 * sB * Math.Sin(TwoPi * f4 * t);
			double rumble = hp.P(lp2.P(lp1.P(r.W()))) * 5.0 * (0.5 + 0.5 * sB);
			x[i] = tones + rumble;
		}
		var o = MakeLoop(x, pre, n, xf);
		NormRms(o, -22, -6);
		return o;
	}
}
