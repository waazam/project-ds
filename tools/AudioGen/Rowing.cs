namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Water;

/// <summary>
/// The rowboat, rebuilt from how rowing really sounds (2026-10-15, the owner: "the rowing of the boat sounds
/// awful"). A stroke is heard as: the <b>catch</b> (the blade's face slapping in, the air pocket behind it
/// closing in a gulp, a fizz of small bubbles), the <b>pull</b> (a swirl of water shed off the blade's edges in
/// vortices, gurgling, fluttering at the shedding rate), the <b>release</b> (the blade unsticking with a little
/// suck, the sheet of water pouring off it, then a line of drips running out). The boat itself: the oar's loom
/// knocking and turning in its lock (wood in metal), the hull's creak and knock, water lapping along the
/// overlapping planks (a flam of small slaps, strake by strake, with a gulp in each lap), water in the bilge
/// sloshing fore and aft, and the chuckle of water along the hull while she makes way.
/// </summary>
public static class Rowing
{
	// ------------------------------------------------------------------ the stroke

	/// <summary>The blade going in.</summary>
	public static double[] Catch(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		double hard = r.R(0.6, 1.0);
		// the face slapping in: a dense burst of grains, broadband but not bright
		Grains(x, r, sr, 0.0, r.R(0.02, 0.04), 5000, 220, r.R(3200, 4800), 0.9 * hard, u => Math.Exp(-u * 3), 0.0008, 0.004);
		// the water's mass taking it: a short low "thuk"
		Lake.Slap(x, r, sr, 0.002, 0.75 * hard, 55, r.R(280, 420), r.R(0.03, 0.05), 0.004);
		// the air pocket behind the blade closing: one gulp, a bubble a centimetre or so across
		Minnaert(x, sr, r.R(0.035, 0.07), r.R(0.005, 0.011), r.R(0.22, 0.34), r.R(0.15, 0.3));
		// and the cloud it leaves: a fizz of smaller ones, mostly in the first tenth of a second
		Cloud(x, r, sr, 0.02, r.R(0.18, 0.28), r.I(18, 34), 0.0012, 0.006, 0.16, u => Math.Exp(-u * 4));
		// a little spray off the blade's edge falling back
		Grains(x, r, sr, r.R(0.12, 0.18), r.R(0.25, 0.4), 90, 1200, 6000, 0.12, u => 1 - u, 0.001, 0.003);
		Cloud(x, r, sr, 0.15, 0.4, r.I(3, 7), 0.0009, 0.0022, 0.07, u => 1 - u);
		return FinishOneShot(Room(x, sr, 1.0, 0.1, 0.5, 0.55, 0.5), sr, -3, 80);
	}

	/// <summary>The blade drawn through the water (about 0.6-0.8 s; the game pitches it to the stroke).</summary>
	public static double[] Pull(Rng r, int sr)
	{
		double dur = r.R(0.6, 0.8);
		var x = Buf(sr, dur + 0.45);
		Func<double, double> load = u => Math.Sin(Math.PI * Math.Pow(Math.Clamp(u, 0, 1), 0.7));
		// the swirl: water forced round the blade, its band wandering as the flow changes
		var swirl = Buf(sr, dur);
		Wash(swirl, r, sr, 0, dur, r.R(170, 230), r.R(700, 1000), 1.3, 1.0, load, 0.07);
		var body = Buf(sr, dur);
		Wash(body, r, sr, 0, dur, 70, 220, 0.8, 0.8, load, 0.15);
		// vortices shed off the blade's edges make it flutter (5-9 a second, the rate drifting)
		double fs = r.R(5, 9), ph = r.R(0, 6.28);
		var drift = new Smooth(r, dur + 1, 0.12);
		for (int i = 0; i < swirl.Length; i++)
		{
			double tt = (double)i / sr;
			ph += TwoPi * fs * (0.8 + 0.4 * drift.At(tt)) / sr;
			swirl[i] *= 0.7 + 0.3 * Math.Sin(ph);
		}
		Mix(x, swirl, 0, sr, 0.75);
		Mix(x, body, 0, sr, 0.55);
		// the turbulence's grain
		Grains(x, r, sr, 0.02, dur, 700, 350, 2600, 0.22, load, 0.0015, 0.006);
		// gurgles in the vortices: bubbles dragged round and settling, low and mid
		Cloud(x, r, sr, 0.04, dur * 0.95, r.I(22, 40), 0.002, 0.009, 0.2, load, 0.15);
		// the boat surging answers with a soft lap at the bow
		Lake.Slap(x, r, sr, dur * r.R(0.65, 0.8), 0.18, 70, 420, 0.08, 0.02);
		return FinishOneShot(Room(x, sr, 1.0, 0.09, 0.5, 0.55, 0.5), sr, -3, 120);
	}

	/// <summary>The blade lifting out, and the drips running off it.</summary>
	public static double[] Release(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		// the face unsticking from the water: a small suck
		Pop(x, r, sr, 0.0, 0.32, r.R(260, 360), r.R(600, 900), r.R(0.008, 0.014), 0.25);
		// the sheet pouring off the blade, thinning
		double sheet = r.R(0.14, 0.26);
		Grains(x, r, sr, 0.005, sheet, 2600, 450, r.R(5000, 7000), 0.55, u => Math.Pow(1 - u, 1.3), 0.0008, 0.004);
		Cloud(x, r, sr, 0.01, sheet + 0.05, r.I(10, 18), 0.001, 0.0035, 0.18, u => 1 - u);
		// the drip line: a run as the stream off the tip breaks up, then single drops, slowing
		double t = sheet + r.R(0.0, 0.05), gap = r.R(0.03, 0.05), a = 0.3;
		int drops = r.I(9, 16);
		for (int i = 0; i < drops && t < 1.45; i++)
		{
			Drop(x, r, sr, t, a * r.R(0.6, 1.0));
			t += gap * r.R(0.7, 1.4);
			gap *= r.R(1.15, 1.45);
			a *= 0.9;
		}
		return FinishOneShot(Room(x, sr, 1.0, 0.1, 0.5, 0.5, 0.5), sr, -3, 120);
	}

	/// <summary>A drop landing on still water from a little height: the tick of the impact and (not always)
	/// the bright ring of the tiny bubble it traps.</summary>
	internal static void Drop(double[] x, Rng r, int sr, double t, double amp, bool wrap = false)
	{
		Grains(x, r, sr, t, 0.004, 1500, 900, 6000, amp * 0.45, _ => 1, 0.0006, 0.0015, wrap);
		if (r.Chance(0.65)) Minnaert(x, sr, t + r.R(0.001, 0.004), r.R(0.0011, 0.0028), amp, r.R(0.2, 0.5), wrap);
	}

	// ------------------------------------------------------------------ the oars in their locks

	/// <summary>The loom knocking in its lock as the oar loads: a dull wooden "tok" with a small metal ring
	/// in it, sometimes a rattle after.</summary>
	public static double[] LockKnock(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		var wood = Plank(r, r.R(240, 360), r.R(0.018, 0.03), 4);
		Modal(x, r, sr, 0.0, 0.9, wood, r.R(0.8, 1.6), 3500);
		Modal(x, r, sr, 0.0005, 0.25, Scatter(r, 6, 1700, 5200, 0.03, 0.09), 0.5, 7000);
		if (r.Chance(0.6))
		{
			double t2 = r.R(0.03, 0.07);
			Modal(x, r, sr, t2, r.R(0.3, 0.5), Plank(r, r.R(260, 380), 0.015, 3), 0.8, 3000);
			Modal(x, r, sr, t2, 0.1, Scatter(r, 4, 2000, 5000, 0.02, 0.06), 0.4, 7000);
		}
		LowPass(x, sr, 6000);
		return FinishOneShot(Room(x, sr, 1.0, 0.06, 0.4, 0.5, 0.4), sr, -3, 40);
	}

	/// <summary>The oar turning in its lock: wood and a leather collar on bronze, a short stick-slip creak.</summary>
	public static double[] LockCreak(Rng r, int sr)
	{
		double dur = r.R(0.18, 0.42);
		var x = Buf(sr, dur + 0.3);
		var modes = new[] { (r.R(480, 680), 9.0, 1.0), (r.R(1150, 1500), 12.0, 0.55), (r.R(2200, 2900), 22.0, 0.25) };
		StickSlip(x, r, sr, 0.0, dur, r.R(40, 70), r.R(90, 140), modes, 0.9, u => Env(u, 0.25, 0.35), 0.06);
		return FinishOneShot(Room(x, sr, 1.0, 0.08, 0.4, 0.5, 0.4), sr, -3, 60);
	}

	// ------------------------------------------------------------------ the hull

	/// <summary>The hull taking a load: a slow stick-slip groan in the planks, and a tick or two as a strake settles.</summary>
	public static double[] HullCreak(Rng r, int sr)
	{
		double dur = r.R(0.5, 1.1);
		var x = Buf(sr, dur + 0.5);
		var modes = new[] { (r.R(150, 240), 12.0, 1.0), (r.R(320, 430), 11.0, 0.65), (r.R(560, 720), 9.0, 0.3) };
		double rise = r.R(0.15, 0.4);
		StickSlip(x, r, sr, 0.0, dur, r.R(12, 22), r.R(26, 44), modes, 0.9, u => Env(u, rise, 0.35), r.R(0.08, 0.2));
		int ticks = r.I(0, 3);
		for (int i = 0; i < ticks; i++)
			Modal(x, r, sr, r.R(0.1, dur), r.R(0.15, 0.35), Plank(r, r.R(200, 330), 0.03, 3), 1.2, 2500);
		LowPass(x, sr, 2600);
		return FinishOneShot(Room(x, sr, 1.0, 0.12, 0.5, 0.5, 0.5), sr, -3, 80);
	}

	/// <summary>
	/// A knock on the hull from under the water (the thing below): a heavy, soft-fleshed blow on the bottom
	/// boards, heard from inside, so low, boomy and muffled; the bilge water jumping; a few bubbles let go under the keel.
	/// </summary>
	public static double[] KnockBelow(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		var boards = Plank(r, r.R(85, 135), r.R(0.07, 0.12), 4);
		Modal(x, r, sr, 0.0, 1.0, boards, r.R(5, 9), 900);
		Lake.Slap(x, r, sr, 0.0, 0.7, 28, 170, r.R(0.05, 0.08), 0.006);
		// a second, smaller resonance: the frame next to it
		Modal(x, r, sr, 0.003, 0.35, Plank(r, r.R(170, 240), 0.04, 3), 4, 1400);
		// the water in the bottom of the boat jumping
		Grains(x, r, sr, r.R(0.02, 0.05), r.R(0.12, 0.2), 500, 500, 3500, 0.12, u => 1 - u, 0.001, 0.004);
		// bubbles knocked loose under the keel
		Cloud(x, r, sr, r.R(0.05, 0.12), 0.35, r.I(4, 9), 0.003, 0.012, 0.14, u => 1 - u);
		LowPass(x, sr, 2200);
		return FinishOneShot(Room(x, sr, 1.0, 0.16, 0.6, 0.55, 0.5), sr, -3, 120);
	}

	/// <summary>Something big lifting the boat from under: a deep thud through the bottom, the whole hull
	/// groaning as it rides up, the bilge water rushing aft.</summary>
	public static double[] HullBump(Rng r, int sr)
	{
		var x = Buf(sr, 2.0);
		Modal(x, r, sr, 0.0, 1.0, Plank(r, r.R(70, 105), r.R(0.1, 0.16), 4), r.R(8, 14), 700);
		Lake.Slap(x, r, sr, 0.0, 0.9, 25, 150, r.R(0.09, 0.13), 0.01);
		var creak = HullCreak(r.Fork(), sr);
		MixPk(x, creak, r.R(0.08, 0.2), sr, 0.45);
		var slosh = BilgeSlosh(r.Fork(), sr);
		MixPk(x, slosh, r.R(0.2, 0.35), sr, 0.4);
		LowPass(x, sr, 3000);
		return FinishOneShot(Room(x, sr, 1.0, 0.15, 0.6, 0.55, 0.5), sr, -3, 200);
	}

	/// <summary>A limb coming down on the boat: planks cracking and splintering, the hull booming, a wall of water over the side.</summary>
	public static double[] HullHit(Rng r, int sr)
	{
		var x = Buf(sr, 2.6);
		Lake.Slap(x, r, sr, 0.0, 1.0, 25, 900, 0.06, 0.002);
		Modal(x, r, sr, 0.0, 0.9, Plank(r, r.R(80, 120), 0.14, 4), 6, 1500);
		Modal(x, r, sr, 0.004, 0.6, Plank(r, r.R(190, 280), 0.06, 4), 2, 4000);
		// splintering: a crackle of small sharp breaks, fast then thinning
		for (double t = 0.005; t < 0.35; t += r.R(0.004, 0.03) * (1 + t * 8))
			Modal(x, r, sr, t, r.R(0.12, 0.4) * (1 - t / 0.4), Plank(r, r.R(500, 1400), 0.008, 3), 0.4, 8000);
		// the water coming over the side
		Grains(x, r, sr, 0.02, 1.4, 3500, 200, 4500, 0.55, u => Math.Exp(-u * 2.5), 0.001, 0.006);
		Cloud(x, r, sr, 0.03, 1.2, r.I(40, 70), 0.0015, 0.012, 0.25, u => Math.Exp(-u * 2));
		var slosh = BilgeSlosh(r.Fork(), sr);
		MixPk(x, slosh, r.R(0.4, 0.6), sr, 0.45);
		return FinishOneShot(Room(x, sr, 1.0, 0.25, 1.0, 0.45, 0.8), sr, -3, 300);
	}

	/// <summary>Water in the bottom of the boat running fore and aft as she rocks: a sloshing wash, a gurgle under the
	/// thwarts, a little slap where it piles against a frame.</summary>
	public static double[] BilgeSlosh(Rng r, int sr)
	{
		double dur = r.R(0.55, 0.95);
		var x = Buf(sr, dur + 0.4);
		double peak = r.R(0.3, 0.6);
		Func<double, double> env = u => u < peak ? Math.Pow(Math.Sin(0.5 * Math.PI * u / peak), 1.5) : Math.Pow(Math.Cos(0.5 * Math.PI * (u - peak) / (1 - peak)), 2);
		Wash(x, r, sr, 0, dur, r.R(220, 320), r.R(900, 1500), 0.9, 0.7, env, 0.1);
		Grains(x, r, sr, 0, dur, 900, 400, 3200, 0.35, env, 0.001, 0.005);
		Cloud(x, r, sr, dur * 0.2, dur * 0.7, r.I(8, 16), 0.002, 0.007, 0.18, u => Math.Sin(Math.PI * u));
		Lake.Slap(x, r, sr, dur * peak, 0.3, 150, 1300, r.R(0.015, 0.03), 0.004);
		return FinishOneShot(Room(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 120);
	}

	/// <summary>A swell hitting the bow: a heavy slap on the planks strake by strake, spray over, drops, the bilge slopping.</summary>
	public static double[] BowSlap(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		double t = 0;
		int strakes = r.I(3, 5);
		for (int k = 0; k < strakes; k++)
		{
			double a = Math.Pow(0.72, k);
			Lake.Slap(x, r, sr, t, 0.9 * a, 60, r.R(600, 900), r.R(0.03, 0.05), 0.003);
			Modal(x, r, sr, t + 0.002, 0.5 * a, Plank(r, r.R(140, 220), 0.06, 3), 3, 1800);
			t += r.R(0.012, 0.035);
		}
		Grains(x, r, sr, 0.03, r.R(0.9, 1.3), 1600, 500, 6000, 0.35, u => Math.Exp(-u * 2.2), 0.001, 0.004);
		Cloud(x, r, sr, 0.03, 0.6, r.I(25, 45), 0.0015, 0.01, 0.22, u => Math.Exp(-u * 3));
		var slosh = BilgeSlosh(r.Fork(), sr);
		MixPk(x, slosh, r.R(0.25, 0.4), sr, 0.3);
		return FinishOneShot(Room(x, sr, 1.0, 0.18, 0.8, 0.45, 0.8), sr, -3, 150);
	}

	// ------------------------------------------------------------------ loops

	/// <summary>
	/// Water lapping on the overlapping planks, heard from inside: each ripple strikes two to four strakes in quick
	/// succession (a soft flam of slaps, each a little smaller, each with the plank's dull ring), with a gulp of air
	/// in the lap; ripple trains bunch them now and then; the odd drip and trickle. Event-built and circular.
	/// </summary>
	public static double[] HullLap(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double t = r.R(0, 0.4);
		while (t < sec)
		{
			int train = r.Chance(0.3) ? r.I(2, 5) : 1;
			for (int k = 0; k < train; k++)
			{
				double a = r.LogR(0.3, 1.0), tt = t;
				int strakes = r.I(2, 5);
				for (int s = 0; s < strakes; s++)
				{
					double sa = a * Math.Pow(0.65, s);
					Lake.Slap(x, r, sr, tt, sa * 0.55, 90, r.R(380, 700), r.R(0.018, 0.035), 0.004, true);
					Modal(x, r, sr, tt + 0.002, sa * 0.35, Plank(r, r.R(160, 260), 0.04, 3), 3, 1500, true);
					tt += r.R(0.012, 0.04);
				}
				Cloud(x, r, sr, t + 0.01, 0.12, r.I(1, 5), 0.003, 0.009, a * 0.22, u => 1 - u, 0.15, true);
				t += r.R(0.2, 0.36);
			}
			t += r.R(0.5, 1.6);
		}
		// the small stuff in between: little ripples touching the planks further along, never quite silent
		for (double d = r.R(0, 0.3); d < sec; d += r.R(0.15, 0.5))
			Lake.Slap(x, r, sr, d, r.LogR(0.04, 0.1), 110, r.R(400, 900), r.R(0.012, 0.025), 0.004, true);
		for (double d = r.R(0, 1); d < sec; d += r.R(0.8, 3.0)) Rowing.Drop(x, r, sr, d, r.R(0.05, 0.12), true);
		var h = new Hall(sr, 0.5, 0.5, 15, 0.4);
		var o = Circular(x, v => v + h.P(v) * 0.12);
		NormRms(o, -30);
		return o;
	}

	/// <summary>
	/// The hull making way: water chuckling along the planks at the bow and the waterline, a fine fizz in the wake,
	/// the bow's little lifting slaps. Event-built and circular (no noise bed); the game raises it with the boat's speed.
	/// </summary>
	public static double[] Glide(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		// the chuckle rises and falls a little with the boat's pitching: whole cycles per loop, so the seam is clean
		int k = Math.Max(1, (int)Math.Round(sec / 2.7));
		double p0 = r.R(0, TwoPi);
		Grains(x, r, sr, 0, sec, 260, 600, 4500, 0.25, u => 0.6 + 0.4 * Math.Sin(TwoPi * k * u + p0), 0.0015, 0.007, true);
		for (double t = r.R(0, 0.3); t < sec; t += r.R(0.03, 0.12))
			Minnaert(x, sr, t, r.LogR(0.0012, 0.005), r.LogR(0.03, 0.12), r.R(0.1, 0.4), true);
		for (double t = r.R(0, 0.5); t < sec; t += r.R(0.25, 0.7))
			Lake.Slap(x, r, sr, t, r.R(0.15, 0.4), 120, r.R(900, 1600), r.R(0.012, 0.03), 0.004, true);
		var h = new Hall(sr, 0.4, 0.5, 10, 0.4);
		var o = Circular(x, v => v + h.P(v) * 0.1);
		NormRms(o, -30);
		return o;
	}
}
