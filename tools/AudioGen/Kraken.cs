namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Water;

/// <summary>
/// The thing in the lake, and its limb at the station's window (2026-10-15). A creature this size is heard
/// mostly through the water it moves: very low pressure pulses with harmonics so small speakers carry them,
/// sheets and streams of water pouring off a rising limb, the crater a slam leaves collapsing in a deep
/// "whoomp", big bubbles bursting up behind it. Up close it is wet flesh: suction pops as suckers let go,
/// lids peeling open on mucus (stick-slip runs of tiny pops), drips off it. The window: flesh on glass
/// (a dull pane thump and a rubbery squeak), the crack running, the burst, the wet drag back out.
/// </summary>
public static class Kraken
{
	// ------------------------------------------------------------------ under and through the water

	/// <summary>Something very big moving under the boat: a deep pressure thud (with harmonics), a second softer
	/// pulse, and a slow belch of big bubbles coming up after it.</summary>
	public static double[] DeepThoom(Rng r, int sr)
	{
		var x = Buf(sr, 3.6);
		double f0 = r.R(30, 42);
		AddTone(x, sr, 0.02, 1.7, u => f0 * (1 - 0.2 * u), u => Math.Exp(-u * 3.5) * Math.Min(1, u * 40), new[] { 1.0, 0.6, 0.32, 0.14, 0.06 });
		Lake.Slap(x, r, sr, 0.02, 0.5, 18, 150, 0.3, 0.03);
		if (r.Chance(0.7))
		{
			double t2 = r.R(0.5, 0.9), f2 = f0 * r.R(0.85, 1.1);
			AddTone(x, sr, t2, 1.4, u => f2 * (1 - 0.15 * u), u => 0.45 * Math.Exp(-u * 4) * Math.Min(1, u * 30), new[] { 1.0, 0.5, 0.25 });
		}
		// the belch: big bubbles (2-5 cm) let go below and rising, ringing low
		double tb = r.R(0.7, 1.4);
		Cloud(x, r, sr, tb, r.R(0.8, 1.4), r.I(8, 16), 0.012, 0.045, 0.3, u => Math.Sin(Math.PI * u), 0.25);
		LowPass(x, sr, 500);
		return FinishOneShot(Room(x, sr, 0.9, 0.4, 1.8, 0.6, 1.1), sr, -3, 400);
	}

	/// <summary>
	/// A limb rising out of the water: a heave as the surface bulges and breaks, then water sheeting off it
	/// (a dense pour thinning into streams, the streams into heavy drops), the water gulping closed round its root,
	/// and the flesh itself straining. <paramref name="thick"/> 0 is lake water; 1 is the pit's blood (slower,
	/// lower, gloopier).
	/// </summary>
	public static double[] LimbRise(Rng r, int sr, double thick)
	{
		double dur = r.R(2.2, 3.0);
		var x = Buf(sr, dur + 1.0);
		double lo = 1 - 0.45 * thick;
		// the bulge breaking
		Lake.Slap(x, r, sr, 0.0, 0.8, 30, 500 * lo, r.R(0.12, 0.2), 0.04);
		Wash(x, r, sr, 0.0, 0.9, 120 * lo, 900 * lo, 0.9, 0.6, u => Env(u, 0.25, 0.6), 0.1);
		// the pour: dense at first, thinning out
		double pour = dur * r.R(0.55, 0.75);
		Grains(x, r, sr, 0.08, pour, 4200 * (1 - 0.5 * thick), 280 * lo, 5200 * lo, 0.7, u => Math.Pow(1 - u, 1.4) * Math.Min(1, u * 12), 0.0012, 0.006 + 0.01 * thick);
		Cloud(x, r, sr, 0.1, pour, r.I(40, 70), 0.0015 / lo, 0.012 / lo, 0.3, u => Math.Pow(1 - u, 1.2), 0.15);
		// streams breaking into heavy drops, spread out over the rest
		for (double t = pour * 0.6; t < dur; t += r.R(0.03, 0.14) * (1 + (t - pour * 0.6) * 1.5))
		{
			Lake.Slap(x, r, sr, t, r.R(0.12, 0.3), 400 * lo, 4000 * lo, r.R(0.004, 0.01), 0.001);
			if (r.Chance(0.6)) Minnaert(x, sr, t + 0.003, r.R(0.002, 0.006) / lo, r.R(0.1, 0.25), 0.25);
		}
		// the water gulping closed round its root
		for (int i = 0; i < r.I(2, 4); i++) Minnaert(x, sr, r.R(0.2, 0.9), r.R(0.012, 0.03) / lo, r.R(0.25, 0.45), 0.2);
		// the flesh straining: a slow, low, rubbery stretch
		var modes = new[] { (r.R(140, 220) * lo, 4.0, 1.0), (r.R(330, 450) * lo, 5.0, 0.5) };
		StickSlip(x, r, sr, r.R(0.3, 0.6), r.R(0.8, 1.3), 18, 40, modes, 0.18, u => Env(u, 0.4, 0.4));
		double heave = r.R(36, 44) * lo;
		AddTone(x, sr, 0.0, 1.2, u => heave * (1 - 0.1 * u), u => 0.3 * Env(u, 0.1, 0.7), new[] { 1.0, 0.5, 0.2 });
		return FinishOneShot(Room(x, sr, 0.9, 0.35 + 0.15 * thick, 1.6 + thick, 0.5, 1.1), sr, -3, 400);
	}

	/// <summary>A limb sliding back under: the water closing over it in deep gulps, a swirl, a last glug, drops.</summary>
	public static double[] LimbSink(Rng r, int sr)
	{
		double dur = r.R(1.4, 2.0);
		var x = Buf(sr, dur + 0.8);
		Wash(x, r, sr, 0, dur, 100, 600, 0.9, 0.6, u => Env(u, 0.3, 0.5), 0.12);
		Grains(x, r, sr, 0, dur * 0.8, 1200, 300, 3500, 0.35, u => Env(u, 0.2, 0.6), 0.0015, 0.006);
		Cloud(x, r, sr, 0.1, dur, r.I(10, 18), 0.008, 0.03, 0.4, u => 0.3 + 0.7 * u, 0.2);
		Minnaert(x, sr, dur * r.R(0.85, 0.95), r.R(0.025, 0.04), 0.55, 0.3);
		for (double t = 0.1; t < dur * 0.7; t += r.R(0.08, 0.25)) Rowing.Drop(x, r, sr, t, r.R(0.06, 0.15));
		return FinishOneShot(Room(x, sr, 0.9, 0.35, 1.5, 0.5, 1.0), sr, -3, 300);
	}

	/// <summary>
	/// A limb slammed flat on the water: the contact's dense crack of water, a thump through the lake, the crater it
	/// punches collapsing in a deep "whoomp" a moment later, bubbles boiling up, and the sheet it threw raining back.
	/// </summary>
	public static double[] SlamWater(Rng r, int sr)
	{
		var x = Buf(sr, 3.4);
		Grains(x, r, sr, 0.0, r.R(0.03, 0.05), 9000, 150, 6500, 1.0, u => Math.Exp(-u * 3), 0.0006, 0.004);
		Lake.Slap(x, r, sr, 0.0, 0.9, 22, 200, r.R(0.15, 0.25), 0.004);
		double f0 = r.R(42, 58);
		AddTone(x, sr, 0.0, 0.9, u => f0 * (1 - 0.3 * u), u => 0.6 * Math.Exp(-u * 5) * Math.Min(1, u * 80), new[] { 1.0, 0.45, 0.18 });
		// the crater collapsing: one big cavity ringing very low, and the boil after it
		double tc = r.R(0.22, 0.4);
		Minnaert(x, sr, tc, r.R(0.035, 0.06), 0.55, 0.15);
		Cloud(x, r, sr, tc, 0.8, r.I(40, 70), 0.003, 0.025, 0.3, u => Math.Exp(-u * 2.5), 0.2);
		// the sheet raining back: dense patter thinning, drops ringing
		Grains(x, r, sr, 0.3, r.R(1.6, 2.3), 1800, 700, 6000, 0.35, u => Env(u, 0.1, 0.8), 0.001, 0.004);
		for (double t = 0.4; t < 2.6; t += r.R(0.02, 0.08) * (1 + t)) Rowing.Drop(x, r, sr, t, r.R(0.08, 0.2));
		return FinishOneShot(Room(x, sr, 0.9, 0.45, 2.0, 0.45, 1.2), sr, -3, 400);
	}

	/// <summary>
	/// The breach: a boom as the lake is thrown apart, a roaring rush of water pouring off a mass coming up through
	/// it, big bubbles boiling, the low heave of displaced water, and a long patter of it raining back down.
	/// </summary>
	public static double[] BreachSurge(Rng r, int sr)
	{
		var x = Buf(sr, 5.0);
		double f0 = r.R(38, 52);
		AddTone(x, sr, 0.0, 2.0, u => f0 * (1 - 0.35 * u), u => Math.Exp(-u * 3) * Math.Min(1, u * 60), new[] { 1.0, 0.55, 0.28, 0.12 });
		Lake.Slap(x, r, sr, 0.0, 0.9, 20, 240, 0.45, 0.01);
		Grains(x, r, sr, 0.0, r.R(2.4, 3.0), 7000, 220, 5000, 0.9, u => Math.Exp(-u * 1.6) * Math.Min(1, u * 30), 0.001, 0.008);
		Cloud(x, r, sr, 0.05, 2.2, r.I(90, 140), 0.003, 0.035, 0.35, u => Math.Exp(-u * 1.5), 0.2);
		Wash(x, r, sr, 0.0, 3.0, 50, 260, 0.8, 0.5, u => Env(u, 0.1, 0.7), 0.2);
		Grains(x, r, sr, 0.8, 3.4, 1500, 700, 6000, 0.3, u => Env(u, 0.1, 0.8), 0.001, 0.004);
		for (double t = 1.0; t < 4.2; t += r.R(0.03, 0.12) * (1 + (t - 1) * 0.5)) Rowing.Drop(x, r, sr, t, r.R(0.06, 0.18));
		return FinishOneShot(Room(x, sr, 0.9, 0.55, 2.6, 0.4, 1.3), sr, -3, 500);
	}

	// ------------------------------------------------------------------ wet flesh, close

	/// <summary>An eye opening: the lid peeling back on mucus (a run of tiny sticky pops, speeding up),
	/// a low squelch under it, a last wet "plip" as it comes clear.</summary>
	public static double[] EyeOpen(Rng r, int sr)
	{
		double dur = r.R(0.35, 0.7);
		var x = Buf(sr, dur + 0.4);
		Wash(x, r, sr, 0, dur, 160, 700, 1.8, 0.5, u => Env(u, 0.2, 0.4), 0.06);
		int n = r.I(8, 20);
		double t = 0.02, gap = dur / n * r.R(1.4, 1.9);
		for (int i = 0; i < n && t < dur; i++)
		{
			Pop(x, r, sr, t, r.R(0.15, 0.4), r.R(700, 1800), r.R(400, 900), r.R(0.002, 0.005), 0.4);
			t += gap * r.R(0.5, 1.3);
			gap *= 0.9;
		}
		Pop(x, r, sr, dur, 0.55, r.R(500, 800), r.R(1000, 1500), r.R(0.006, 0.012), 0.3);
		return FinishOneShot(Room(x, sr, 1.0, 0.25, 1.0, 0.5, 0.8), sr, -3, 120);
	}

	/// <summary>An eye blinking shut: a quick wet "schlup", two or three pops, a soft low closing.</summary>
	public static double[] EyeBlink(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		Wash(x, r, sr, 0, r.R(0.12, 0.2), 200, 900, 1.6, 0.6, u => Env(u, 0.3, 0.5), 0.04);
		for (int i = 0; i < r.I(2, 4); i++) Pop(x, r, sr, r.R(0.02, 0.15), r.R(0.2, 0.45), r.R(600, 1400), r.R(300, 600), r.R(0.003, 0.007), 0.4);
		Lake.Slap(x, r, sr, r.R(0.12, 0.18), 0.4, 60, 400, 0.03, 0.006);
		return FinishOneShot(Room(x, sr, 1.0, 0.2, 0.8, 0.5, 0.7), sr, -3, 80);
	}

	/// <summary>A run of suckers letting go: deep wet "plok"s, each with its smack, under a slither of flesh.</summary>
	public static double[] SuckerPops(Rng r, int sr)
	{
		double dur = r.R(0.5, 1.1);
		var x = Buf(sr, dur + 0.4);
		Wash(x, r, sr, 0, dur, 250, 1400, 1.4, 0.35, u => Env(u, 0.2, 0.4), 0.08);
		int n = r.I(3, 9);
		for (int i = 0; i < n; i++)
		{
			double f = r.LogR(220, 700);
			Pop(x, r, sr, r.R(0.0, dur), r.R(0.45, 1.0), f, f * r.R(0.55, 0.8), r.R(0.008, 0.02), r.R(0.3, 0.8));
		}
		return FinishOneShot(Room(x, sr, 1.0, 0.2, 0.8, 0.5, 0.7), sr, -3, 100);
	}

	/// <summary>A drop off the limb overhead landing on the lake.</summary>
	public static double[] DripWater(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		Rowing.Drop(x, r, sr, 0.005, 1.0);
		if (r.Chance(0.4)) Rowing.Drop(x, r, sr, r.R(0.06, 0.2), r.R(0.3, 0.6));
		return FinishOneShot(Room(x, sr, 1.0, 0.15, 0.6, 0.5, 0.6), sr, -3, 60);
	}

	/// <summary>A heavy, slimy drop off the limb landing on the boat's boards: a splat, the plank's tick, a fleck of spatter.</summary>
	public static double[] DripWood(Rng r, int sr)
	{
		var x = Buf(sr, 0.4);
		Grains(x, r, sr, 0.003, 0.008, 3000, 400, 6000, 1.0, _ => 1, 0.0008, 0.003);
		Modal(x, r, sr, 0.003, 0.45, Plank(r, r.R(550, 900), 0.012, 3), 0.6, 5000);
		Grains(x, r, sr, 0.015, r.R(0.04, 0.09), 400, 1500, 7000, 0.2, u => 1 - u, 0.0006, 0.002);
		return FinishOneShot(Room(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 50);
	}

	/// <summary>
	/// Its voice close by, wet: a deep, rough groan from somewhere inside it, coming out through water and slime, so it
	/// bubbles and gargles as it goes (the low end lifted for size, a gurgle layered through it).
	/// </summary>
	public static double[] WetMoan(Rng r, int sr)
	{
		double dur = r.R(2.2, 3.4);
		var x = Buf(sr, dur + 1.6);
		double f0 = r.R(34, 52), bend = r.R(-0.25, 0.1);
		var v = Throat(r, sr, dur, u => f0 * (1 + bend * u + 0.12 * Math.Sin(Math.PI * u)), u => Env(u, 0.2, 0.45),
			new[] { r.R(170, 230), r.R(380, 480), r.R(750, 950) }, 0.7, 0.25, 0.5);
		// the gargle: the voice comes out through liquid, so it trembles at the bubbling rate
		var gm = new Smooth(r, dur + 1, 0.05);
		for (int i = 0; i < v.Length; i++) v[i] *= 0.55 + 0.45 * gm.At((double)i / sr);
		MixPk(x, v, 0.05, sr, 0.9);
		Cloud(x, r, sr, 0.1, dur, r.I(40, 80), 0.004, 0.02, 0.3, u => Env(u, 0.2, 0.4), 0.2);
		AddTone(x, sr, 0.05, dur, u => f0 * 0.5 * (1 + bend * u), u => 0.35 * Env(u, 0.3, 0.5), new[] { 1.0, 0.3 });
		LowPass(x, sr, 1800);
		return FinishOneShot(Room(x, sr, 0.85, 0.45, 2.0, 0.55, 1.2), sr, -3, 400);
	}

	// ------------------------------------------------------------------ the window (Act 13)

	/// <summary>Flesh striking the glass from outside: a dull pane thump, the glass's own ring, a rubbery squeak
	/// as it drags on it, a sucker or two letting go.</summary>
	public static double[] WindowPress(Rng r, int sr)
	{
		var x = Buf(sr, 1.4);
		Lake.Slap(x, r, sr, 0.0, 0.8, 40, 500, r.R(0.04, 0.07), 0.006);
		Modal(x, r, sr, 0.001, 0.6, Plank(r, r.R(110, 170), r.R(0.06, 0.1), 4), 6, 1200);
		Modal(x, r, sr, 0.001, 0.22, Scatter(r, 10, 1500, 7000, 0.04, 0.2), 1, 8000);
		var modes = new[] { (r.R(900, 1300), 14.0, 1.0), (r.R(1900, 2600), 18.0, 0.6), (r.R(3400, 4300), 20.0, 0.25) };
		StickSlip(x, r, sr, r.R(0.06, 0.12), r.R(0.2, 0.45), r.R(160, 260), r.R(380, 620), modes, 0.4, u => Env(u, 0.2, 0.4));
		for (int i = 0; i < r.I(1, 4); i++) Pop(x, r, sr, r.R(0.25, 0.7), r.R(0.2, 0.4), r.R(300, 600), r.R(200, 400), r.R(0.008, 0.015), 0.4);
		return FinishOneShot(Room(x, sr, 1.0, 0.18, 0.7, 0.5, 0.6), sr, -3, 120);
	}

	/// <summary>The pane cracking: a sharp report, the glass ringing high, the crack running in a burst of small
	/// ticks that slows and stops.</summary>
	public static double[] WindowCrack(Rng r, int sr)
	{
		var x = Buf(sr, 2.2);
		Grains(x, r, sr, 0.0, 0.003, 9000, 1500, 14000, 1.0, _ => 1, 0.0003, 0.001);
		Modal(x, r, sr, 0.0, 0.7, Scatter(r, 14, 1800, 9500, 0.06, 0.35), 0.3, 12000);
		Modal(x, r, sr, 0.0, 0.35, Plank(r, r.R(130, 190), 0.07, 3), 2, 2000);
		double t = r.R(0.01, 0.03), gap = r.R(0.004, 0.01);
		int n = r.I(14, 32);
		for (int i = 0; i < n && t < 0.9; i++)
		{
			Modal(x, r, sr, t, r.R(0.12, 0.4) * (1 - t), Scatter(r, 3, 2500, 11000, 0.01, 0.05), 0.15, 14000);
			t += gap * r.R(0.5, 1.8);
			gap *= r.R(1.08, 1.25);
		}
		return FinishOneShot(Room(x, sr, 1.0, 0.18, 0.7, 0.4, 0.6), sr, -3, 150);
	}

	/// <summary>
	/// The window bursting in: the limb's wet weight through it, the pane exploding into shards ringing as they fly,
	/// the shards raining on the floor, and the lake starting to pour in behind.
	/// </summary>
	public static double[] WindowBurst(Rng r, int sr)
	{
		var x = Buf(sr, 3.4);
		Lake.Slap(x, r, sr, 0.0, 0.9, 30, 700, 0.06, 0.003);
		Grains(x, r, sr, 0.0, 0.05, 9000, 800, 14000, 0.9, u => Math.Exp(-u * 3), 0.0004, 0.002);
		// the shards in flight: many small glassy rings
		for (int i = 0; i < r.I(60, 110); i++)
		{
			double t = r.R(0, 0.25) * r.R(0.2, 1);
			Modal(x, r, sr, t, r.LogR(0.05, 0.35), Scatter(r, 2, 2500, 11000, 0.02, 0.12), 0.1, 14000);
		}
		// landing: a tinkling patter on the floor, thinning
		for (double t = 0.25; t < 1.8; t += r.R(0.008, 0.05) * (1 + (t - 0.25) * 2))
			Modal(x, r, sr, t, r.LogR(0.04, 0.22), Scatter(r, 2, 3000, 11000, 0.015, 0.07), 0.1, 14000);
		// the water coming in behind
		Grains(x, r, sr, 0.05, 2.3, 3000, 150, 4000, 0.45, u => Math.Min(1, u * 6) * (1 - 0.4 * u), 0.0015, 0.008);
		Cloud(x, r, sr, 0.1, 2.2, r.I(50, 90), 0.002, 0.015, 0.2, u => Math.Min(1, u * 4) * (1 - 0.5 * u), 0.2);
		Pop(x, r, sr, r.R(0.05, 0.2), 0.3, r.R(250, 400), r.R(150, 250), 0.015, 0.6);
		return FinishOneShot(Room(x, sr, 1.0, 0.25, 0.9, 0.4, 0.7), sr, -3, 250);
	}

	/// <summary>The limb dragging itself back out over the sill: wet flesh slithering in pulses, suckers letting go,
	/// a rubbery squeal on the frame, broken glass gritting under it.</summary>
	public static double[] TentacleDrag(Rng r, int sr)
	{
		double dur = r.R(1.1, 1.8);
		var x = Buf(sr, dur + 0.6);
		var slither = Buf(sr, dur);
		Wash(slither, r, sr, 0, dur, 220, 1500, 1.4, 1.0, u => Env(u, 0.15, 0.3), 0.07);
		double pf = r.R(3, 6), ph = r.R(0, TwoPi);
		for (int i = 0; i < slither.Length; i++) { ph += TwoPi * pf / sr; slither[i] *= 0.45 + 0.55 * Math.Pow(0.5 + 0.5 * Math.Sin(ph), 1.5); }
		Mix(x, slither, 0, sr, 0.6);
		for (int i = 0; i < r.I(8, 18); i++)
		{
			double f = r.LogR(240, 750);
			Pop(x, r, sr, r.R(0, dur), r.R(0.25, 0.7), f, f * r.R(0.55, 0.8), r.R(0.007, 0.018), 0.5);
		}
		var modes = new[] { (r.R(300, 450), 6.0, 1.0), (r.R(700, 950), 8.0, 0.5) };
		StickSlip(x, r, sr, r.R(0.1, 0.4), r.R(0.3, 0.6), 30, 70, modes, 0.3, u => Env(u, 0.3, 0.4));
		Grains(x, r, sr, 0, dur, 120, 2500, 10000, 0.15, u => Env(u, 0.2, 0.4), 0.0004, 0.0015);
		return FinishOneShot(Room(x, sr, 1.0, 0.2, 0.8, 0.5, 0.6), sr, -3, 150);
	}
}
