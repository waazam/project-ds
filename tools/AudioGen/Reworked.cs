namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// Dedicated sounds for things that had been borrowing someone else's sample (the owner, 2026-10-15: "stop reusing
/// old sounds for stuff and do your research"). Before this, a forest trunk's creak was the bunker's steel door, the
/// vine door, the cabin door's hinge, the burning cabin's beams and the giant's joints; a radio squelch was the blood
/// on the station walls and the eye in the drain; a steel door slam sat under a wooden door and a log wall; and the
/// stalker shuffled about in the player's boots.
///
/// Each is modelled on what the real thing does, not on a bed of noise:
/// <list type="bullet">
/// <item><b>Friction</b> (hinges, rusted doors, beams, sinew): stick-slip. The surfaces lock, strain builds, they
/// let go in a jolt; each jolt is an impulse, and the rate of the jolts is the pitch you hear (a fast hinge stops
/// squeaking because the slip never re-sticks). The impulses ring whatever is attached: a steel leaf's inharmonic
/// plate modes, a door panel's low wooden ones, a log's dull few.</item>
/// <item><b>Fibre</b> (vines, charring timber): many tiny fibres parting one at a time, a crackle that thickens
/// toward the break, then a snap: broadband, short, with a brief ring.</item>
/// <item><b>Wet</b> (flesh, blood): cavities of air collapsing (each a short rising "plip"), the tacky peel of a
/// surface coming away (a dense run of tiny clicks), and the dull slap of a heavy wet mass.</item>
/// <item><b>Feet</b> (the stalker): a padded, heel-less foot: a soft low press, a little skin, and long nails
/// ticking down a moment after, toe by toe.</item>
/// </list>
/// </summary>
public static class Reworked
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A bank of resonators: rings each impulse at the given modes.</summary>
	sealed class Modes
	{
		readonly Biquad[] _b; readonly double[] _a;
		public Modes(int sr, double[] f, double[] q, double[] a)
		{
			_b = new Biquad[f.Length];
			for (int i = 0; i < f.Length; i++) _b[i] = Biquad.Bp(sr, Math.Min(f[i], sr * 0.45), q[i]);
			_a = a;
		}
		public double P(double x) { double v = 0; for (int i = 0; i < _b.Length; i++) v += _a[i] * _b[i].P(x); return v; }
	}

	/// <summary>A room's tail mixed in (wet 0..1).</summary>
	static double[] Room(double[] x, int sr, double size, double damp, double wet)
	{
		var rv = new Reverb(sr, size, damp);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = x[i] + wet * rv.P(x[i]);
		return o;
	}

	/// <summary>
	/// Stick-slip over [t0, t0+dur): returns the impulse train (one array, the jolts). <paramref name="rate"/> maps
	/// u (0..1) to the slip rate in Hz; <paramref name="stall"/> is how often it seizes for a moment.
	/// </summary>
	static double[] StickSlip(Rng r, int sr, int len, double t0, double dur, Func<double, double> rate, Func<double, double> amp, double jitter, double stall)
	{
		var imp = new double[len];
		var wob = new Smooth(r, dur + 1, r.R(0.08, 0.2));
		int s0 = (int)(t0 * sr), n = (int)(dur * sr);
		double ph = 0, stallLeft = 0;
		for (int i = 0; i < n && s0 + i < len; i++)
		{
			double u = (double)i / n;
			if (stallLeft > 0) { stallLeft -= 1.0 / sr; continue; }
			if (r.U() < stall / sr) { stallLeft = r.R(0.03, 0.14); continue; }
			double f = rate(u) * (0.75 + 0.5 * wob.At((double)i / sr));
			ph += f / sr * (1 + jitter * r.W());
			if (ph >= 1) { ph -= 1; imp[s0 + i] += amp(u) * r.R(0.6, 1.0) * (r.Chance(0.5) ? 1 : -1); }
		}
		return imp;
	}

	/// <summary>A short broadband crack with a brief ring: a fibre bundle or a charred member giving way.</summary>
	static void Snap(double[] x, Rng r, int sr, double t, double amp, double ringF, double ringTau)
	{
		int s0 = (int)(t * sr), n = (int)(0.25 * sr);
		var hp = Biquad.Hp(sr, r.R(600, 1400)); var ring = Biquad.Bp(sr, ringF, r.R(6, 14)); var body = Biquad.Bp(sr, ringF * r.R(0.35, 0.5), 3);
		for (int i = 0; i < n && s0 + i < x.Length; i++)
		{
			double tt = (double)i / sr, w = r.W();
			double crack = hp.P(w) * Perc(tt, 0.0003, r.R(0.004, 0.009));
			double rg = ring.P(w * Perc(tt, 0.0005, 0.006)) * 6 * Math.Exp(-tt / ringTau);
			double bd = body.P(w) * Perc(tt, 0.001, 0.02) * 0.8;
			x[s0 + i] += amp * (crack + rg + bd);
		}
	}

	/// <summary>Little ticks (fibres parting, grit, nails): very short high noise grains.</summary>
	static void Tick(double[] x, Rng r, int sr, double t, double amp, double lo, double hi, double tau)
	{
		int s0 = (int)(t * sr), n = (int)(tau * 8 * sr) + 4;
		var hp = Biquad.Hp(sr, lo); var lp = Biquad.Lp(sr, hi);
		for (int i = 0; i < n && s0 + i < x.Length; i++)
		{
			if (s0 + i < 0) continue;
			x[s0 + i] += amp * lp.P(hp.P(r.W())) * Perc((double)i / sr, 0.0002, tau);
		}
	}

	/// <summary>Band-limited noise with an envelope over [t0, t0+dur).</summary>
	static void Noise(double[] x, Rng r, int sr, double t0, double dur, double lo, double hi, double amp, Func<double, double> env)
	{
		var hp = Biquad.Hp(sr, lo); var lp = Biquad.Lp(sr, hi); var lp2 = Biquad.Lp(sr, hi);
		int s0 = (int)(t0 * sr), n = (int)(dur * sr);
		for (int i = 0; i < n && s0 + i < x.Length; i++)
			if (s0 + i >= 0) x[s0 + i] += amp * lp2.P(lp.P(hp.P(r.W()))) * env((double)i / n);
	}

	/// <summary>A collapsing air cavity in something wet: a short rising sine "plip".</summary>
	static void Plip(double[] x, Rng r, int sr, double t, double amp, double f0)
	{
		double dur = r.R(0.012, 0.035), rise = r.R(1.4, 2.4);
		AddTone(x, sr, t, dur, u => f0 * (1 + (rise - 1) * u * u), u => amp * Math.Sin(Math.PI * Math.Min(1, u * 4) * 0.5) * (1 - u) * (1 - u));
	}

	// ---------------------------------------------------------------------------------------------- steel

	/// <summary>
	/// The bunker's steel door ground open on hinges rusted since it was hung: slow stick-slip of rust on steel,
	/// each jolt ringing the leaf's inharmonic plate modes (the near-musical "grrraaank" the real ones make), grit
	/// scraping off with it, the weight of the leaf in a low scrape underneath, and a heavy clunk where it stops.
	/// Recorded references: a WWII bunker door near Halifax "moving it yields near-musical tones with rich harmonics".
	/// </summary>
	public static double[] BunkerDoorGrind(Rng r, int sr)
	{
		double dur = r.R(2.2, 3.2);
		var x = Buf(sr, dur + 2.2);
		double f0 = r.R(140, 210);
		double[] ratio = { 1, 2.32, 3.86, 5.71, 8.1, 10.9 };
		var plate = new Modes(sr, ratio.Select(k => f0 * k * r.R(0.97, 1.03)).ToArray(),
			new[] { 40.0, 55, 60, 70, 70, 60 }, new[] { 1.0, 0.8, 0.55, 0.4, 0.25, 0.15 });
		// the groan: the slip rate glides up as it gets going and sags as it slows; a pause in the middle where it binds
		double bind = r.R(0.35, 0.6);
		double rA = r.R(28, 45), rB = r.R(60, 110);
		var imp = StickSlip(r, sr, x.Length, 0.05, dur, u => rA + (rB - rA) * Math.Sin(Math.PI * Math.Min(1, u * 1.1)), u => Env(u, 0.12, 0.2) * (Math.Abs(u - bind) < 0.05 ? 0.15 : 1), 0.25, 2.5);
		var grit = Biquad.Bp(sr, r.R(1800, 2800), 1.2);
		var heavy = Biquad.Lp(sr, 160);
		var gate = Biquad.Lp(sr, 60);
		for (int i = 0; i < x.Length; i++)
		{
			double e = Math.Abs(gate.P(Math.Abs(imp[i]) * 40));
			x[i] = 0.6 * plate.P(imp[i]) + 0.5 * grit.P(r.W()) * e + 1.2 * heavy.P(r.W()) * e;
		}
		// a jolt where it breaks free, and the clunk at the stop
		Snap(x, r, sr, 0.06, 0.5, f0 * 2.3, 0.08);
		double stop = 0.05 + dur;
		Noise(x, r, sr, stop, 0.4, 30, 220, 1.0, u => Perc(u * 0.4, 0.002, 0.07));
		Snap(x, r, sr, stop + 0.005, 0.25, f0 * 3.9, 0.25);
		x = Room(x, sr, 0.85, 0.3, 0.55);   // a concrete bunker
		return FinishOneShot(x, sr, -3, 250);
	}

	// ---------------------------------------------------------------------------------------------- vines

	/// <summary>
	/// A door bound in vines swung: the stems stretched over the hinge squeak and creak (green wood on itself, slow
	/// stick-slip through soft, low modes), fibres popping one by one, and the leaves thrashing.
	/// </summary>
	public static double[] VineStrain(Rng r, int sr)
	{
		double dur = r.R(0.9, 1.5);
		var x = Buf(sr, dur + 0.6);
		var stem = new Modes(sr, new[] { r.R(320, 420), r.R(620, 780), r.R(1050, 1300) }, new[] { 9.0, 10, 8 }, new[] { 1.0, 0.6, 0.3 });
		double rA = r.R(45, 80), rB = r.R(110, 200);
		var imp = StickSlip(r, sr, x.Length, 0.02, dur * 0.85, u => rA + (rB - rA) * u, u => Env(u, 0.2, 0.3), 0.4, 4);
		for (int i = 0; i < x.Length; i++) x[i] = 2.0 * stem.P(imp[i]);
		// fibres parting, thicker toward the middle of the swing
		int pops = r.I(14, 30);
		for (int k = 0; k < pops; k++)
		{
			double t = 0.05 + dur * Math.Pow(r.U(), 0.8) * 0.9;
			Tick(x, r, sr, t, r.R(0.06, 0.2), 1800, 7000, r.R(0.0006, 0.0018));
		}
		if (r.Chance(0.6)) Snap(x, r, sr, r.R(0.3, 0.8) * dur, r.R(0.2, 0.4), r.R(1200, 2000), 0.02);
		// leaves thrashing with the swing
		Noise(x, r, sr, 0.0, dur + 0.3, 1500, 7000, 0.05, u => Env(u, 0.25, 0.5) * (0.6 + 0.4 * Math.Sin(u * r.R(20, 35))));
		x = Room(x, sr, 0.5, 0.5, 0.25);
		return FinishOneShot(x, sr, -3, 120);
	}

	/// <summary>The vine door shaken against its bindings: a short jerk, the vines snapping taut, leaves.</summary>
	public static double[] VineRattle(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		int jerks = r.I(2, 4);
		double t = 0.01;
		for (int j = 0; j < jerks; j++)
		{
			var stem = new Modes(sr, new[] { r.R(280, 380), r.R(560, 700) }, new[] { 7.0, 8 }, new[] { 1.0, 0.5 });
			var imp = StickSlip(r, sr, x.Length, t, r.R(0.06, 0.12), u => r.R(90, 160), u => Env(u, 0.1, 0.5), 0.5, 0);
			for (int i = 0; i < x.Length; i++) x[i] += 0.8 * stem.P(imp[i]);
			Noise(x, r, sr, t, 0.2, 60, 400, 0.9, u => Perc(u * 0.2, 0.003, 0.03));   // the leaf hitting its stop
			Noise(x, r, sr, t, r.R(0.2, 0.35), 1500, 7000, 0.2, u => Env(u, 0.05, 0.8));
			for (int k = 0; k < r.I(3, 8); k++) Tick(x, r, sr, t + r.R(0, 0.12), r.R(0.15, 0.4), 1800, 7000, 0.001);
			t += r.R(0.12, 0.22);
		}
		x = Room(x, sr, 0.5, 0.5, 0.2);
		return FinishOneShot(x, sr, -3, 80);
	}

	// ---------------------------------------------------------------------------------------------- the cabin door

	/// <summary>
	/// An old wooden door on iron hinges: a hinge pin squeaking (stick-slip fast enough to be a pitch, 300-900 Hz,
	/// gliding and breaking up as the speed changes, with a metallic edge), driven into the wooden panel's low modes.
	/// </summary>
	public static double[] HingeCreak(Rng r, int sr)
	{
		double dur = r.R(0.6, 1.2);
		var x = Buf(sr, dur + 0.5);
		double fA = r.R(280, 480), fB = fA * r.R(1.3, 2.1), fC = fA * r.R(0.8, 1.2);
		var pin = new Modes(sr, new[] { r.R(2400, 3200), r.R(4100, 5200) }, new[] { 25.0, 30 }, new[] { 0.5, 0.3 });
		var panel = new Modes(sr, new[] { r.R(140, 190), r.R(250, 320), r.R(420, 520), r.R(800, 1000) }, new[] { 6.0, 7, 8, 6 }, new[] { 0.8, 1.0, 0.6, 0.35 });
		var imp = StickSlip(r, sr, x.Length, 0.02, dur, u => u < 0.5 ? fA + (fB - fA) * u * 2 : fB + (fC - fB) * (u - 0.5) * 2, u => Env(u, 0.08, 0.25), 0.06, 3);
		for (int i = 0; i < x.Length; i++) x[i] = panel.P(imp[i]) + pin.P(imp[i]) + 0.35 * imp[i];
		LowPass(x, sr, 6000);
		x = Room(x, sr, 0.55, 0.45, 0.3);   // a log cabin
		return FinishOneShot(x, sr, -3, 120);
	}

	/// <summary>
	/// The body of a wooden door hitting its frame (the weight under the cabin's slam): a low thud of the slab, the
	/// panel's ring, the frame's jamb shuddering, and the iron latch rattling home.
	/// </summary>
	public static double[] DoorFrameThud(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		Noise(x, r, sr, 0, 0.5, 25, 140, 4.0, u => Perc(u * 0.5, 0.002, 0.08));
		var panel = new Modes(sr, new[] { r.R(85, 110), r.R(170, 220), r.R(290, 360), r.R(520, 640) }, new[] { 10.0, 12, 12, 9 }, new[] { 1.0, 0.8, 0.5, 0.25 });
		var imp = new double[x.Length]; imp[(int)(0.001 * sr)] = 1; imp[(int)(0.006 * sr)] = -0.6;
		for (int i = 0; i < x.Length; i++) x[i] += 6 * panel.P(imp[i]);
		// the latch: a few small iron knocks settling
		double t = r.R(0.01, 0.03);
		for (int k = 0; k < r.I(3, 6); k++) { Tick(x, r, sr, t, r.R(0.4, 0.8) / (k + 1), 1500, 5000, r.R(0.002, 0.005)); t += r.R(0.025, 0.07); }
		x = Room(x, sr, 0.6, 0.45, 0.35);
		return FinishOneShot(x, sr, -3, 200);
	}

	/// <summary>
	/// A log wall struck from outside, heard inside: the logs boom low and short (big, heavily damped), the
	/// chinking between them trickles, and whatever stands against the wall jumps and rattles.
	/// </summary>
	public static double[] LogWallShudder(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		Noise(x, r, sr, 0, 0.6, 20, 110, 5.0, u => Perc(u * 0.6, 0.004, 0.11));
		var logs = new Modes(sr, new[] { r.R(55, 75), r.R(120, 150), r.R(210, 260) }, new[] { 6.0, 7, 6 }, new[] { 1.0, 0.6, 0.3 });
		var imp = new double[x.Length]; imp[(int)(0.002 * sr)] = 1;
		for (int i = 0; i < x.Length; i++) x[i] += 10 * logs.P(imp[i]);
		// dust and chinking crumbs; a cup or tin on a shelf jumping
		for (int k = 0; k < r.I(10, 22); k++) Tick(x, r, sr, r.R(0.02, 0.5), r.R(0.05, 0.15), 2500, 8000, 0.0006);
		double t = r.R(0.02, 0.05);
		for (int k = 0; k < r.I(2, 5); k++) { Tick(x, r, sr, t, r.R(0.25, 0.5) / (k + 1), 900, 3500, r.R(0.004, 0.01)); t += r.R(0.04, 0.09); }
		x = Room(x, sr, 0.55, 0.5, 0.3);
		return FinishOneShot(x, sr, -3, 200);
	}

	// ---------------------------------------------------------------------------------------------- fire

	/// <summary>
	/// A burning cabin's timber under load: a slow, low stick-slip groan of a beam sagging as it chars, sap and
	/// moisture boiling out (a thin hiss, now and then a whistle), fibres crackling, and often a crack where
	/// something gives, with embers sprinkling down after.
	/// </summary>
	public static double[] FireTimberGroan(Rng r, int sr)
	{
		double dur = r.R(1.2, 2.4);
		var x = Buf(sr, dur + 1.4);
		var beam = new Modes(sr, new[] { r.R(90, 130), r.R(190, 250), r.R(330, 420) }, new[] { 8.0, 9, 8 }, new[] { 1.0, 0.7, 0.35 });
		double rA = r.R(8, 14), rB = r.R(18, 32);
		var imp = StickSlip(r, sr, x.Length, 0.05, dur, u => rA + (rB - rA) * u, u => Env(u, 0.3, 0.25), 0.3, 1.5);
		for (int i = 0; i < x.Length; i++) x[i] = 7.0 * beam.P(imp[i]);
		Noise(x, r, sr, 0.0, dur + 0.5, 2500, 9000, 0.012, u => Env(u, 0.3, 0.4));   // steam hiss off it
		if (r.Chance(0.4))
		{
			double f = r.R(1700, 3200), wt = r.R(0.2, 0.6) * dur;
			AddTone(x, sr, wt, r.R(0.25, 0.6), u => f * (1 + 0.08 * Math.Sin(u * 18)), u => 0.05 * Env(u, 0.3, 0.5));
		}
		int crackles = r.I(6, 16);
		for (int k = 0; k < crackles; k++) Tick(x, r, sr, r.R(0.05, dur), r.R(0.04, 0.12), 1200, 6000, r.R(0.0008, 0.002));
		if (r.Chance(0.65))
		{
			double ct = 0.05 + dur * r.R(0.8, 1.0);
			Snap(x, r, sr, ct, r.R(0.3, 0.45), r.R(700, 1200), 0.04);
			for (int k = 0; k < r.I(8, 20); k++) Tick(x, r, sr, ct + 0.05 + r.R(0, 0.9), r.R(0.05, 0.2), 2000, 7000, 0.0008);
		}
		x = Room(x, sr, 0.6, 0.5, 0.3);
		return FinishOneShot(x, sr, -3, 200);
	}

	// ---------------------------------------------------------------------------------------------- the giant

	/// <summary>
	/// Something that size bending: deep stick-slip of sinew over bone (a few jolts a second through cavernous body
	/// modes), the wet thock of a joint slipping its socket, and cartilage grinding in a dense low crackle.
	/// </summary>
	public static double[] GiantSinewCreak(Rng r, int sr)
	{
		double dur = r.R(1.0, 2.0);
		var x = Buf(sr, dur + 1.2);
		var body = new Modes(sr, new[] { r.R(38, 55), r.R(75, 100), r.R(130, 170), r.R(240, 300) }, new[] { 5.0, 6, 7, 6 }, new[] { 1.0, 0.8, 0.5, 0.25 });
		double rA = r.R(5, 9), rB = r.R(12, 22);
		var imp = StickSlip(r, sr, x.Length, 0.03, dur, u => rA + (rB - rA) * Math.Sin(Math.PI * u), u => Env(u, 0.2, 0.3), 0.35, 1);
		for (int i = 0; i < x.Length; i++) x[i] = 2.5 * body.P(imp[i]);
		// gristle: dense, low crackle, swelling with the bend
		var lp = Biquad.Lp(sr, 900); var hp = Biquad.Hp(sr, 150);
		double gr = r.R(0.15, 0.3);
		for (int i = 0; i < (int)(dur * sr); i++)
		{
			double u = (double)i / (dur * sr);
			double c = r.U() < 300.0 / sr ? r.W() : 0;
			x[i] += gr * 6 * lp.P(hp.P(c)) * Env(u, 0.3, 0.3);
		}
		// a joint slipping: a low wet thock with its pitch falling
		int thocks = r.I(1, 3);
		for (int k = 0; k < thocks; k++)
		{
			double t = r.R(0.1, 0.9) * dur, f = r.R(70, 120);
			AddTone(x, sr, t, 0.18, u => f * (1 - 0.45 * u), u => 0.8 * Math.Exp(-u * 5) * Math.Min(1, u * 40), new[] { 1.0, 0.3 });
			Noise(x, r, sr, t, 0.06, 200, 1500, 0.3, u => Perc(u * 0.06, 0.001, 0.012));
		}
		x = Room(x, sr, 0.7, 0.6, 0.35);   // out in the woods, at a distance
		LowPass(x, sr, 3000);
		return FinishOneShot(x, sr, -3, 250);
	}

	// ---------------------------------------------------------------------------------------------- flesh and blood

	/// <summary>
	/// A wet slap or squish of flesh: the dull slap of a heavy wet mass, cavities of trapped air collapsing, and the
	/// tacky peel as it comes away (a run of tiny clicks thinning out) -- what foley does with meat and wet fruit.
	/// </summary>
	public static double[] FleshSquelch(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		double t0 = 0.005;
		Noise(x, r, sr, t0, 0.2, 80, r.R(500, 900), r.R(1.2, 2.0), u => Perc(u * 0.2, 0.002, r.R(0.02, 0.04)));   // the slap
		int plips = r.I(4, 10);
		for (int k = 0; k < plips; k++) Plip(x, r, sr, t0 + r.R(0.005, 0.18), r.R(0.15, 0.4), r.R(250, 900));
		// the tacky peel: a run of tiny clicks, dense then thinning
		double t = t0 + r.R(0.04, 0.1), gap = r.R(0.002, 0.004);
		int clicks = r.I(15, 35);
		for (int k = 0; k < clicks && t < 0.55; k++) { Tick(x, r, sr, t, r.R(0.1, 0.25) * (1 - (double)k / clicks), 900, 4000, 0.0005); t += gap * r.R(0.5, 1.5); gap *= r.R(1.04, 1.12); }
		x = Room(x, sr, 0.3, 0.6, 0.12);
		return FinishOneShot(x, sr, -3, 40);
	}

	/// <summary>A hand pushed into a pool of thick blood: the plunge (a dull gulp and a sucking hollow), the fingers
	/// stirring, a slow viscous suck as it closes behind.</summary>
	public static double[] BloodPlunge(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		Noise(x, r, sr, 0.01, 0.3, 60, 600, 1.2, u => Perc(u * 0.3, 0.008, 0.06));
		double f = r.R(110, 170);
		AddTone(x, sr, 0.02, 0.2, u => f * (1 + 0.8 * u), u => 0.6 * Math.Sin(Math.PI * u) * (1 - u));   // the gulp of air dragged under
		for (int k = 0; k < r.I(8, 16); k++) Plip(x, r, sr, r.R(0.05, 0.8), r.R(0.08, 0.3), r.R(180, 700));
		// the suck closing behind: a slow, low, narrowing hollow
		var bp = Biquad.Bp(sr, 300, 3);
		double s0 = r.R(0.35, 0.55), sd = r.R(0.25, 0.45);
		for (int i = (int)(s0 * sr); i < (int)((s0 + sd) * sr) && i < x.Length; i++)
		{
			double u = (i - s0 * sr) / (sd * sr);
			bp.SetBp(250 + 500 * u, 4);
			x[i] += 0.5 * bp.P(r.W()) * Env(u, 0.4, 0.3);
		}
		x = Room(x, sr, 0.4, 0.6, 0.15);
		return FinishOneShot(x, sr, -3, 80);
	}

	/// <summary>Blood letting go of a wall and sheeting down: a broad wet pour that rushes and thins, with gobbets
	/// slapping the floor at its foot.</summary>
	public static double[] BloodSlide(Rng r, int sr)
	{
		double dur = r.R(1.4, 2.2);
		var x = Buf(sr, dur + 0.8);
		var wob = new Smooth(r, dur + 1, 0.06);
		var hp = Biquad.Hp(sr, 200); var lp = Biquad.Lp(sr, 1800);
		for (int i = 0; i < (int)(dur * sr); i++)
		{
			double u = i / (dur * sr);
			x[i] += 0.5 * lp.P(hp.P(r.W())) * Env(u, 0.08, 0.6) * (0.5 + 0.5 * wob.At((double)i / sr));
		}
		for (int k = 0; k < r.I(18, 34); k++) Plip(x, r, sr, r.R(0.02, dur), r.R(0.1, 0.35), r.R(220, 800));
		for (int k = 0; k < r.I(4, 8); k++) Noise(x, r, sr, r.R(0.2, dur), 0.12, 70, 500, r.R(0.4, 0.8), u => Perc(u * 0.12, 0.002, 0.025));
		x = Room(x, sr, 0.5, 0.55, 0.25);
		return FinishOneShot(x, sr, -3, 150);
	}

	// ---------------------------------------------------------------------------------------------- the stalker's feet

	/// <summary>
	/// The stalker's bare foot: no heel strike, a long padded foot pressing down (a soft low thud and a little skin),
	/// and its long nails ticking down a moment after, toe by toe. On stone the nails click; on dirt they scuff grit.
	/// (Creature foley is done with leather gloves and bone claws, timed to the foot; this is that, built.)
	/// </summary>
	public static double[] StalkerStep(Rng r, int sr, bool stone)
	{
		var x = Buf(sr, 0.45);
		Noise(x, r, sr, 0.004, 0.2, 40, stone ? 260 : 200, r.R(2.5, 3.5), u => Perc(u * 0.2, 0.006, 0.03));   // the pad
		Noise(x, r, sr, 0.006, 0.08, 300, 1600, r.R(0.25, 0.45), u => Perc(u * 0.08, 0.004, 0.012));          // skin
		int toes = r.I(2, 5);
		double t = r.R(0.015, 0.04);
		for (int k = 0; k < toes; k++)
		{
			if (stone) Tick(x, r, sr, t, r.R(0.35, 0.8), 2200, 7000, r.R(0.0006, 0.0014));
			else { Tick(x, r, sr, t, r.R(0.25, 0.5), 900, 4500, r.R(0.002, 0.004)); Noise(x, r, sr, t, 0.04, 1200, 5000, 0.08, u => Env(u, 0.1, 0.8)); }
			t += r.R(0.008, 0.035);
		}
		if (r.Chance(0.35)) Noise(x, r, sr, t + 0.02, r.R(0.06, 0.12), stone ? 2000 : 900, 6000, 0.08, u => Env(u, 0.2, 0.7));   // a drag of a nail
		x = Room(x, sr, stone ? 0.6 : 0.4, 0.45, stone ? 0.3 : 0.12);
		return FinishOneShot(x, sr, -3, 60);
	}

	// ---------------------------------------------------------------------------------------------- furniture

	/// <summary>A wooden drawer pulled on dry runners: wood rubbing on wood in a juddering, noisy slide, the contents
	/// shifting, and a bump at the stop.</summary>
	public static double[] DrawerSlide(Rng r, int sr)
	{
		double dur = r.R(0.25, 0.4);
		var x = Buf(sr, dur + 0.5);
		var runner = new Modes(sr, new[] { r.R(350, 500), r.R(800, 1100), r.R(1600, 2100) }, new[] { 3.0, 4, 4 }, new[] { 1.0, 0.6, 0.3 });
		var imp = StickSlip(r, sr, x.Length, 0.01, dur, u => r.R(60, 140), u => Env(u, 0.15, 0.2), 0.8, 2);
		for (int i = 0; i < x.Length; i++) x[i] = runner.P(imp[i]);
		Noise(x, r, sr, 0.01, dur, 400, 3000, 0.12, u => Env(u, 0.2, 0.3));
		for (int k = 0; k < r.I(1, 4); k++) Tick(x, r, sr, r.R(0.05, dur), r.R(0.2, 0.5), 800, 4000, r.R(0.002, 0.006));   // things inside sliding
		Noise(x, r, sr, 0.01 + dur, 0.2, 60, 900, 1.0, u => Perc(u * 0.2, 0.002, 0.02));   // the stop
		x = Room(x, sr, 0.45, 0.5, 0.2);
		return FinishOneShot(x, sr, -3, 60);
	}
}
