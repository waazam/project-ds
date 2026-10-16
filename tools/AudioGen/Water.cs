namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Building blocks for water, wood, glass and flesh, modelled on how the real sounds are made
/// (2026-10-15 research pass, for the lake, the window and the pit):
/// <list type="bullet">
/// <item><b>Bubbles.</b> Almost everything a liquid "says" is the ringing of the air it traps. A bubble
/// of radius r rings at about 3.26/r Hz (Minnaert: 3 mm sings near 1.1 kHz, 2 cm near 160 Hz), is
/// damped by d = 0.13/r + 0.0072·r^-1.5 per second (small ones die in a few milliseconds, big ones
/// ring), and its pitch rises a little as it settles at the surface. A splash or a gurgle is a
/// cloud of them with a power-law spread of sizes: many small, few big.</item>
/// <item><b>Impacts and pours.</b> The broadband part (a blade slapping in, a sheet of water pouring off
/// something) is a dense swarm of tiny noise grains, not a steady noise: its grain gives it the
/// "wet" crackle a filtered hiss never has.</item>
/// <item><b>Wood, metal, glass.</b> A struck body rings in its own modes: a plank in the inharmonic
/// series of a free bar (1, 2.76, 5.40, 8.93), short-lived in wood, long in steel, high and glassy in
/// a pane; a knock is a short noise contact through a bank of those resonators.</item>
/// <item><b>Friction.</b> A creak or a squeak is stick-slip: little slips at a wandering rate, each
/// ringing the body's modes.</item>
/// </list>
/// </summary>
public static class Water
{
	internal static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	static void Put(double[] x, int j, double v, bool wrap)
	{
		if (wrap) x[((j % x.Length) + x.Length) % x.Length] += v;
		else if (j >= 0 && j < x.Length) x[j] += v;
	}

	/// <summary>One bubble of <paramref name="radius"/> metres, ringing at its Minnaert pitch with its
	/// physical damping, rising by <paramref name="rise"/> (a fraction of the damping rate) as it settles.</summary>
	public static void Minnaert(double[] x, int sr, double t, double radius, double amp, double rise = 0.1, bool wrap = false)
	{
		double f0 = 3.26 / radius, d = 0.13 / radius + 0.0072 * Math.Pow(radius, -1.5);
		if (f0 > sr * 0.44) return;
		int n = (int)(Math.Min(7.0 / d, 1.2) * sr) + 1, s0 = (int)(t * sr);
		double ph = 0;
		for (int i = 0; i < n; i++)
		{
			double tt = (double)i / sr, f = f0 * (1 + rise * d * tt);
			if (f > sr * 0.45) break;
			ph += TwoPi * f / sr;
			double env = (1 - Math.Exp(-tt / 0.0003)) * Math.Exp(-d * tt);
			Put(x, s0 + i, Math.Sin(ph) * env * amp, wrap);
		}
	}

	/// <summary>
	/// A bubble cloud: <paramref name="count"/> bubbles over [t0, t0+dur) placed by <paramref name="density"/>
	/// (0..1 over the span), radii spread as a power law between <paramref name="rMin"/> and
	/// <paramref name="rMax"/> (more small than big), bigger ones a little louder.
	/// </summary>
	public static void Cloud(double[] x, Rng r, int sr, double t0, double dur, int count, double rMin, double rMax, double amp,
		Func<double, double> density = null, double rise = 0.1, bool wrap = false)
	{
		density ??= u => 1 - u;
		for (int k = 0, tries = 0; k < count && tries < count * 40; tries++)
		{
			double u = r.U();
			if (r.U() > density(u)) continue;
			k++;
			// power law ~ r^-2 between the bounds (inverse-CDF sampling)
			double a = 1 / rMin, b = 1 / rMax, rad = 1 / (a + (b - a) * r.U());
			double g = amp * Math.Pow(rad / rMax, 0.35) * r.LogR(0.35, 1.0);
			Minnaert(x, sr, t0 + u * dur, rad, g, rise * r.R(0.6, 1.6), wrap);
		}
	}

	/// <summary>
	/// A swarm of tiny noise grains over [t0, t0+dur): the broadband body of moving water. <paramref name="rate"/>
	/// grains a second at the envelope's peak, each 1-6 ms of band-limited noise.
	/// </summary>
	public static void Grains(double[] x, Rng r, int sr, double t0, double dur, double rate, double lo, double hi, double amp,
		Func<double, double> env, double tauLo = 0.001, double tauHi = 0.006, bool wrap = false)
	{
		var hp = Biquad.Hp(sr, lo); var lp = Biquad.Lp(sr, hi); var lp2 = Biquad.Lp(sr, hi * 1.3);
		int maxLen = (int)(tauHi * 6 * sr) + 8;
		var ev = new double[maxLen];
		for (double t = 0; t < dur; t += -Math.Log(1 - r.U()) / Math.Max(rate, 1))
		{
			double u = t / dur, e = env(u);
			if (e <= 0.001 || r.U() > e) continue;
			double tau = r.LogR(tauLo, tauHi);
			int n = Math.Min(maxLen, (int)(tau * 6 * sr) + 4);
			double pk = 0;
			for (int i = 0; i < n; i++) { ev[i] = lp2.P(lp.P(hp.P(r.W()))) * Perc((double)i / sr, 0.0002, tau); pk = Math.Max(pk, Math.Abs(ev[i])); }
			double g = amp * r.LogR(0.2, 1.0) * Math.Sqrt(e) / Math.Max(pk, 1e-9);
			int s0 = (int)((t0 + t) * sr);
			for (int i = 0; i < n; i++) Put(x, s0 + i, ev[i] * g, wrap);
		}
	}

	/// <summary>Band-limited noise shaped by <paramref name="env"/> over [t0, t0+dur), its band centre wandering
	/// (a swirl, a rush). Peak-normalised to <paramref name="amp"/>.</summary>
	public static void Wash(double[] x, Rng r, int sr, double t0, double dur, double fLo, double fHi, double q, double amp,
		Func<double, double> env, double wander = 0.08, bool wrap = false)
	{
		var bp = new Biquad(sr); var bp2 = new Biquad(sr);
		var sm = new Smooth(r, dur + 1, wander);
		int n = (int)(dur * sr);
		var ev = new double[n];
		for (int i = 0; i < n; i++)
		{
			double tt = (double)i / sr, f = fLo * Math.Pow(fHi / fLo, sm.At(tt));
			if (i % 32 == 0) { bp.SetBp(f, q); bp2.SetBp(f * 1.07, q); }
			ev[i] = bp2.P(bp.P(r.W())) * env((double)i / n);
		}
		double g = amp / Math.Max(Peak(ev), 1e-12);
		int s0 = (int)(t0 * sr);
		for (int i = 0; i < n; i++) Put(x, s0 + i, ev[i] * g, wrap);
	}

	/// <summary>
	/// A struck body: a short noise contact (<paramref name="contactMs"/>) through resonators at
	/// <paramref name="modes"/> (Hz, decay seconds, gain). Peak-normalised to <paramref name="amp"/>.
	/// </summary>
	public static void Modal(double[] x, Rng r, int sr, double t, double amp, (double f, double tau, double g)[] modes,
		double contactMs = 1.5, double contactLp = 4000, bool wrap = false)
	{
		double longest = modes.Max(m => m.tau);
		int n = (int)((longest * 6.5 + contactMs / 1000 + 0.01) * sr);
		var ev = new double[n];
		var lp = Biquad.Lp(sr, contactLp);
		int cn = Math.Max(2, (int)(contactMs / 1000 * sr));
		var exc = new double[n];
		for (int i = 0; i < cn; i++) exc[i] = lp.P(r.W()) * Math.Sin(Math.PI * i / cn);
		foreach (var (f, tau, g) in modes)
		{
			if (f > sr * 0.45) continue;
			// a two-pole resonator with the mode's own decay
			double rr = Math.Exp(-1 / (tau * sr)), w = TwoPi * f / sr, a1 = 2 * rr * Math.Cos(w), a2 = -rr * rr;
			double y1 = 0, y2 = 0;
			for (int i = 0; i < n; i++)
			{
				double y = exc[i] + a1 * y1 + a2 * y2;
				y2 = y1; y1 = y;
				ev[i] += y * g * (1 - rr);
			}
		}
		double gg = amp / Math.Max(Peak(ev), 1e-12);
		int s0 = (int)(t * sr);
		for (int i = 0; i < n; i++) Put(x, s0 + i, ev[i] * gg, wrap);
	}

	/// <summary>The modes of a wooden plank or a bar (free-free bending series), with wood's quick decay.</summary>
	public static (double, double, double)[] Plank(Rng r, double f1, double tau1, int count = 4)
	{
		double[] ratio = { 1, 2.76, 5.40, 8.93, 13.34 };
		var m = new (double, double, double)[Math.Min(count, ratio.Length)];
		for (int k = 0; k < m.Length; k++)
			m[k] = (f1 * ratio[k] * r.R(0.97, 1.03), tau1 / (1 + k * 0.9) * r.R(0.8, 1.2), Math.Pow(0.6, k) * r.R(0.7, 1.0));
		return m;
	}

	/// <summary><paramref name="count"/> scattered inharmonic modes between two pitches (steel, glass).</summary>
	public static (double, double, double)[] Scatter(Rng r, int count, double fLo, double fHi, double tauLo, double tauHi)
	{
		var m = new (double, double, double)[count];
		for (int k = 0; k < count; k++)
		{
			double f = r.LogR(fLo, fHi), u = Math.Log(f / fLo) / Math.Log(fHi / fLo);
			m[k] = (f, (tauHi + (tauLo - tauHi) * u) * r.R(0.7, 1.3), r.LogR(0.3, 1.0));
		}
		return m;
	}

	/// <summary>
	/// Stick-slip friction over [t0, t0+dur): slips at a rate wandering between <paramref name="rLo"/> and
	/// <paramref name="rHi"/> a second, each ringing <paramref name="modes"/> (a creak, a squeak, a groan).
	/// </summary>
	public static void StickSlip(double[] x, Rng r, int sr, double t0, double dur, double rLo, double rHi, (double f, double q, double g)[] modes,
		double amp, Func<double, double> env, double wander = 0.12)
	{
		var res = modes.Select(m => Biquad.Bp(sr, m.f, m.q)).ToArray();
		var rate = new Smooth(r, dur + 1, wander);
		int n = (int)((dur + 0.25) * sr), nd = (int)(dur * sr);
		var ev = new double[n];
		double ph = 0;
		for (int i = 0; i < n; i++)
		{
			double imp = 0;
			if (i < nd)
			{
				double u = (double)i / nd;
				ph += (rLo + (rHi - rLo) * rate.At((double)i / sr)) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1.0) * env(u); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].g * res[k].P(imp);
			ev[i] = v;
		}
		double g = amp / Math.Max(Peak(ev), 1e-12);
		int s0 = (int)(t0 * sr);
		for (int i = 0; i < n; i++) Put(x, s0 + i, ev[i] * g, false);
	}

	/// <summary>A wet pop (a sucker letting go, a bubble of slime bursting, an eye): a quick pitched "plok"
	/// sweeping from <paramref name="f0"/> to <paramref name="f1"/>, with a smack of noise at its start.</summary>
	public static void Pop(double[] x, Rng r, int sr, double t, double amp, double f0, double f1, double tau, double smack = 0.5, bool wrap = false)
	{
		int n = (int)((tau * 7 + 0.01) * sr), s0 = (int)(t * sr);
		var ev = new double[n];
		double ph = 0;
		for (int i = 0; i < n; i++)
		{
			double tt = (double)i / sr, u = Math.Min(1, tt / (tau * 2.5));
			double f = f0 * Math.Pow(f1 / f0, u);
			ph += TwoPi * f / sr;
			ev[i] = Math.Sin(ph) * Perc(tt, 0.0004, tau);
		}
		var hp = Biquad.Hp(sr, 700); var lp = Biquad.Lp(sr, 5000);
		int sn = (int)(0.006 * sr);
		for (int i = 0; i < sn && i < n; i++) ev[i] += lp.P(hp.P(r.W())) * smack * Perc((double)i / sr, 0.0002, 0.0012);
		double g = amp / Math.Max(Peak(ev), 1e-12);
		for (int i = 0; i < n; i++) Put(x, s0 + i, ev[i] * g, wrap);
	}

	/// <summary>
	/// A throat: a pulse train at a wandering pitch (with jitter and a growl of subharmonic), plus breath,
	/// through broad formants. Returns the dry voice, unscaled.
	/// </summary>
	public static double[] Throat(Rng r, int sr, double dur, Func<double, double> pitch, Func<double, double> env,
		double[] formants, double rough = 0.3, double breath = 0.2, double sub = 0.3)
	{
		int n = (int)(dur * sr);
		var o = new double[n];
		var jit = new Smooth(r, dur + 1, 0.03);
		var fb = formants.Select(f => Biquad.Bp(sr, f, 2.2)).ToArray();
		var lp = Biquad.Lp(sr, 2400);
		var bn = Biquad.Bp(sr, 600, 0.7);
		double ph = 0, ph2 = 0;
		for (int i = 0; i < n; i++)
		{
			double u = (double)i / n, f = pitch(u) * (1 + rough * 0.12 * (jit.At((double)i / sr) - 0.5));
			ph += f / sr; ph2 += f * 0.5 / sr;
			if (ph >= 1) ph -= 1;
			if (ph2 >= 1) ph2 -= 1;
			// a skewed glottal pulse: quick open, slower close; the subharmonic every other cycle
			double p = ph < 0.35 ? Math.Sin(Math.PI * ph / 0.35) : 0;
			double s = p * p * (1 + sub * (ph2 < 0.5 ? 1 : -0.6)) + rough * 0.25 * r.W() * p + breath * bn.P(r.W());
			double v = 0;
			for (int k = 0; k < fb.Length; k++) v += fb[k].P(s) / (1 + k * 0.6);
			o[i] = lp.P(v) * env(u);
		}
		return o;
	}

	internal static void Mix(double[] x, double[] ev, double t, int sr, double gain)
	{
		int s0 = (int)(t * sr);
		for (int i = 0; i < ev.Length && s0 + i < x.Length; i++) if (s0 + i >= 0) x[s0 + i] += ev[i] * gain;
	}

	/// <summary>Peak-normalise an event to <paramref name="amp"/> and mix it in.</summary>
	internal static void MixPk(double[] x, double[] ev, double t, int sr, double amp) => Mix(x, ev, t, sr, amp / Math.Max(Peak(ev), 1e-12));

	internal static double[] Room(double[] x, int sr, double dry, double wet, double rt60, double damp = 0.45, double size = 1.0)
	{
		var h = new Hall(sr, rt60, damp, 20, size);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] * dry + h.P(x[i]) * wet;
		return o;
	}
}
