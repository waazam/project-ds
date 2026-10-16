namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Water;

/// <summary>
/// Act 18's thing in the blood (2026-10-15): its own voice and its own wet noises, in place of the giant's bellow and
/// rattle, the walkie's squelch and the generic roars it had borrowed. Size is low end (a deep throat with a growl of
/// subharmonic, a low tone under it); wetness is bubbling layered through the voice, as if it bellows up through the
/// blood. Its limbs hit steel: the grating's long ring and rattle under a heavy wet smack and a spatter of blood. Its
/// eyes burst: a membrane popping, a spurt, the spatter. The drain is a slow vortex of thick liquid.
/// </summary>
public static class Octopus
{
	static readonly double Thick = 1.0;

	/// <summary>Its bellow from under the blood: a huge, rough, wet voice with bubbles boiling up through it.</summary>
	public static double[] Bellow(Rng r, int sr)
	{
		double dur = r.R(3.0, 4.2);
		var x = Buf(sr, dur + 2.2);
		double f0 = r.R(26, 40), bend = r.R(-0.3, 0.1);
		var v = Throat(r, sr, dur, u => f0 * (1 + 0.35 * Math.Sin(Math.PI * Math.Min(1, u * 1.6)) + bend * u), u => Env(u, 0.12, 0.45),
			new[] { r.R(160, 220), r.R(380, 470), r.R(800, 1000), r.R(1500, 1900) }, 0.8, 0.3, 0.6);
		var gm = new Smooth(r, dur + 1, 0.045);
		for (int i = 0; i < v.Length; i++) v[i] *= 0.5 + 0.5 * gm.At((double)i / sr);
		MixPk(x, v, 0.05, sr, 1.0);
		AddTone(x, sr, 0.05, dur, u => f0 * 0.5 * (1 + 0.3 * Math.Sin(Math.PI * Math.Min(1, u * 1.6))), u => 0.45 * Env(u, 0.15, 0.5), new[] { 1.0, 0.4, 0.15 });
		Cloud(x, r, sr, 0.15, dur, r.I(60, 110), 0.008, 0.05, 0.35, u => Env(u, 0.15, 0.4), 0.25);
		LowPass(x, sr, 2200);
		return FinishOneShot(Room(x, sr, 0.75, 0.6, 3.2, 0.55, 1.5), sr, -3, 600);
	}

	/// <summary>A cry of pain as a valve bites: higher and rawer, a gargle in the throat, cracking at its top.</summary>
	public static double[] Cry(Rng r, int sr)
	{
		double dur = r.R(1.8, 2.8);
		var x = Buf(sr, dur + 1.8);
		double f0 = r.R(55, 95), up = r.R(0.4, 0.9);
		var v = Throat(r, sr, dur, u => f0 * (1 + up * Math.Sin(Math.PI * Math.Pow(u, 0.6))), u => Env(u, 0.06, 0.5),
			new[] { r.R(300, 380), r.R(650, 850), r.R(1400, 1800), r.R(2300, 2800) }, 1.0, 0.35, 0.4);
		var gm = new Smooth(r, dur + 1, 0.035);
		for (int i = 0; i < v.Length; i++) v[i] *= 0.45 + 0.55 * gm.At((double)i / sr);
		MixPk(x, v, 0.02, sr, 1.0);
		Cloud(x, r, sr, 0.05, dur, r.I(80, 140), 0.003, 0.014, 0.3, u => Env(u, 0.1, 0.5), 0.3);
		Grains(x, r, sr, 0.0, dur * 0.8, 600, 400, 3000, 0.15, u => Env(u, 0.1, 0.6), 0.001, 0.005);
		LowPass(x, sr, 3600);
		return FinishOneShot(Room(x, sr, 0.75, 0.55, 2.8, 0.5, 1.4), sr, -3, 500);
	}

	/// <summary>Its dying throat: slow, wet rattling pulses through the blood, gurgling, sagging lower and stopping.</summary>
	public static double[] DeathRattle(Rng r, int sr)
	{
		double dur = r.R(4.0, 5.2);
		var x = Buf(sr, dur + 2);
		double f0 = r.R(16, 24);
		var v = Throat(r, sr, dur, u => f0 * (1 - 0.35 * u), u => Env(u, 0.1, 0.4) * (0.6 + 0.4 * Math.Sin(TwoPi * 1.3 * u * 4)),
			new[] { r.R(140, 190), r.R(330, 420), r.R(700, 900) }, 1.0, 0.35, 0.7);
		MixPk(x, v, 0.0, sr, 0.9);
		Cloud(x, r, sr, 0.0, dur, r.I(70, 120), 0.006, 0.04, 0.35, u => Env(u, 0.1, 0.4), 0.25);
		Wash(x, r, sr, 0.0, dur, 60, 300, 0.8, 0.35, u => Env(u, 0.2, 0.5), 0.2);
		LowPass(x, sr, 1600);
		return FinishOneShot(Room(x, sr, 0.75, 0.6, 3.2, 0.55, 1.5), sr, -3, 600);
	}

	/// <summary>
	/// Its death: a strained shriek rising out of it, collapsing into the deepest bellow it has, breaking up into a
	/// bubbling exhale as the air goes out of it through the blood, one last huge glug, and the blood settling.
	/// </summary>
	public static double[] Death(Rng r, int sr)
	{
		var x = Buf(sr, 11.0);
		double hi = r.R(120, 160);
		var shriek = Throat(r, sr, 2.2, u => hi * (1 + 0.25 * Math.Sin(Math.PI * Math.Min(1, u * 1.4))) * (1 - 0.35 * u), u => Env(u, 0.08, 0.35),
			new[] { r.R(500, 650), r.R(1200, 1500), r.R(2400, 2900), r.R(3500, 4200) }, 1.1, 0.4, 0.3);
		MixPk(x, shriek, 0.05, sr, 0.85);
		double lo = r.R(26, 34);
		var bellow = Throat(r, sr, 3.4, u => lo * (1 + 0.6 * (1 - u)) * (1 - 0.3 * u), u => Env(u, 0.15, 0.55),
			new[] { r.R(150, 200), r.R(360, 450), r.R(780, 950) }, 0.9, 0.3, 0.7);
		var gm = new Smooth(r, 5, 0.05);
		for (int i = 0; i < bellow.Length; i++) bellow[i] *= 0.5 + 0.5 * gm.At((double)i / sr);
		MixPk(x, bellow, 1.6, sr, 1.0);
		AddTone(x, sr, 1.6, 3.6, u => lo * 0.5 * (1 - 0.3 * u), u => 0.45 * Env(u, 0.15, 0.6), new[] { 1.0, 0.4 });
		// the exhale through the blood, then the last glug
		Cloud(x, r, sr, 3.6, 2.6, r.I(120, 180), 0.006, 0.05, 0.4, u => Math.Exp(-u * 1.6), 0.25);
		Minnaert(x, sr, r.R(6.0, 6.4), r.R(0.06, 0.08), 0.6, 0.2);
		Wash(x, r, sr, 5.0, 3.5, 50, 250, 0.8, 0.3, u => Env(u, 0.2, 0.7), 0.25);
		LowPass(x, sr, 4500);
		return FinishOneShot(Room(x, sr, 0.75, 0.6, 3.5, 0.55, 1.6), sr, -3, 800);
	}

	/// <summary>
	/// A limb slammed on the catwalk: a heavy wet smack, the steel grating ringing long and rattling in its frame, the
	/// bolts chattering, a ping, blood thrown off it spattering the grating.
	/// </summary>
	public static double[] Slam(Rng r, int sr)
	{
		var x = Buf(sr, 4.0);
		Lake.Slap(x, r, sr, 0.0, 1.0, 28, 800, r.R(0.06, 0.09), 0.003);
		Grains(x, r, sr, 0.0, 0.05, 8000, 200, 4000, 0.7, u => Math.Exp(-u * 3), 0.0008, 0.005);
		Modal(x, r, sr, 0.001, 0.8, Scatter(r, 16, 90, 2600, 0.25, 1.1), 2, 3000);
		for (double t = 0.02; t < 0.7; t += r.R(0.015, 0.05) * (1 + t * 2))
			Modal(x, r, sr, t, r.R(0.08, 0.3) * (1 - t / 0.75), Scatter(r, 3, 900, 4500, 0.01, 0.04), 0.3, 6000);
		if (r.Chance(0.6)) Modal(x, r, sr, r.R(0.05, 0.25), 0.2, Scatter(r, 3, 2500, 5000, 0.15, 0.4), 0.2, 8000);
		Grains(x, r, sr, 0.08, r.R(0.8, 1.2), 500, 300, 3500, 0.3, u => Math.Exp(-u * 2.5), 0.001, 0.006);
		Cloud(x, r, sr, 0.05, 0.6, r.I(10, 20), 0.003, 0.012, 0.18, u => 1 - u, 0.2);
		return FinishOneShot(Room(x, sr, 0.85, 0.5, 2.4, 0.55, 1.4), sr, -3, 500);
	}

	/// <summary>A big slow gurgle from the blood: large bubbles of gas coming up through thick liquid and bursting.</summary>
	public static double[] Gurgle(Rng r, int sr)
	{
		double dur = r.R(1.0, 2.2);
		var x = Buf(sr, dur + 1.2);
		Cloud(x, r, sr, 0.0, dur, r.I(10, 22), 0.012, 0.06, 0.6, u => Env(u, 0.15, 0.4), 0.3);
		for (int i = 0; i < r.I(1, 4); i++)
		{
			double f = r.R(80, 160);
			Pop(x, r, sr, r.R(0.1, dur), r.R(0.4, 0.8), f, f * r.R(1.3, 1.8), r.R(0.03, 0.06), 0.35);
		}
		Wash(x, r, sr, 0, dur, 70, 400, 0.9, 0.3, u => Env(u, 0.3, 0.5), 0.15);
		LowPass(x, sr, 1400);
		return FinishOneShot(Room(x, sr, 0.8, 0.5, 2.6, 0.55, 1.3), sr, -3, 400);
	}

	/// <summary>
	/// The blood going down a valve: a low vortex roaring and wandering, a slurping run of big glugs speeding up as
	/// the level drops, and a last sucking sputter as air gets into it.
	/// </summary>
	public static double[] Drain(Rng r, int sr)
	{
		double dur = r.R(4.6, 5.6);
		var x = Buf(sr, dur + 1.8);
		Wash(x, r, sr, 0, dur, 45, 280, 0.9, 0.7, u => Env(u, 0.15, 0.25), 0.25);
		Grains(x, r, sr, 0, dur, 700, 200, 1800, 0.25, u => Env(u, 0.2, 0.3), 0.003, 0.012);
		double t = 0.2, gap = r.R(0.35, 0.5);
		while (t < dur - 0.4)
		{
			double u = t / dur;
			Minnaert(x, sr, t, r.R(0.015, 0.045), r.R(0.35, 0.6) * Env(u, 0.1, 0.25), 0.3);
			if (r.Chance(0.5)) Pop(x, r, sr, t + r.R(0, 0.05), r.R(0.2, 0.4), r.R(90, 160), r.R(60, 100), r.R(0.03, 0.05), 0.3);
			t += gap * r.R(0.6, 1.3);
			gap = Math.Max(0.09, gap * 0.95);
		}
		// the sputter: air sucked into the outlet
		for (double s = dur - 0.5; s < dur + 0.3; s += r.R(0.03, 0.09))
			Pop(x, r, sr, s, r.R(0.25, 0.5), r.R(150, 300), r.R(80, 150), r.R(0.01, 0.025), 0.6);
		LowPass(x, sr, 1500);
		return FinishOneShot(Room(x, sr, 0.8, 0.5, 2.6, 0.6, 1.5), sr, -3, 500);
	}

	/// <summary>An eye bursting: the membrane popping, a spurt of fluid, a juicy squelch, spatter landing round it.</summary>
	public static double[] EyeBurst(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		Pop(x, r, sr, 0.0, 1.0, r.R(700, 1200), r.R(200, 350), r.R(0.006, 0.012), r.R(0.6, 1.0));
		Grains(x, r, sr, 0.003, r.R(0.04, 0.1), 3500, 400, 4500, 0.6, u => Math.Exp(-u * 2), 0.0008, 0.004);
		Wash(x, r, sr, 0.01, r.R(0.12, 0.22), 120, 600, 1.8, 0.45, u => Env(u, 0.1, 0.7), 0.04);
		Lake.Slap(x, r, sr, 0.0, 0.4, 50, 350, 0.025, 0.002);
		for (double t = r.R(0.08, 0.15); t < 0.6; t += r.R(0.02, 0.08) * (1 + t * 3))
			Grains(x, r, sr, t, 0.006, 2000, 500, 5000, r.R(0.08, 0.25), _ => 1, 0.0008, 0.003);
		return FinishOneShot(Room(x, sr, 0.9, 0.35, 1.6, 0.5, 1.0), sr, -3, 120);
	}

	/// <summary>A limb hauling up out of the blood before it strikes (thicker, lower, gloopier than the lake's).</summary>
	public static double[] LimbRise(Rng r, int sr) => Kraken.LimbRise(r, sr, Thick);
}
