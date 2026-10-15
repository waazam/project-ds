namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// The audio sweep's Foley (the things the player does that were silent: notes, pickups, the lantern,
/// locked doors, gravel underfoot) and its haunting (sounds for the interiors' dark corners: floorboards
/// taking a slow weight somewhere overhead, a moan of air through brick, water dripping in a long space, a
/// chain shifting far off). All events; no sustained noise beds (the owner's rule).
/// </summary>
public static class Foley
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>Stick-slip creak through resonant modes (wood, rope, a hinge).</summary>
	static void Creak(double[] x, Rng r, int sr, double t0, double dur, double rateLo, double rateHi, (double f, double q, double a)[] modes, double amp)
	{
		var res = modes.Select(m => Biquad.Bp(sr, m.f, m.q)).ToArray();
		var rate = new Smooth(r, dur + 1, r.R(0.06, 0.14));
		double ph = 0;
		int s0 = (int)(t0 * sr), n = (int)(dur * sr);
		for (int i = 0; i < n + sr / 2 && s0 + i < x.Length; i++)
		{
			double imp = 0;
			if (i < n)
			{
				double u = (double)i / n;
				ph += (rateLo + (rateHi - rateLo) * rate.At((double)i / sr)) / sr;
				if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1.0) * Env(u, 0.2, 0.35); }
			}
			double v = 0;
			for (int k = 0; k < res.Length; k++) v += modes[k].a * res[k].P(imp);
			x[s0 + i] += v * amp;
		}
	}

	/// <summary>Band-limited crinkle: a burst of tiny paper/leaf clicks.</summary>
	static void Crinkle(double[] x, Rng r, int sr, double t0, double t1, double rate, double lo, double hi, double amp)
	{
		for (double t = t0; t < t1; t += r.R(0.2, 1.8) / rate)
			Slap(x, r, sr, t, amp * r.R(0.3, 1.0) * Env((t - t0) / (t1 - t0), 0.2, 0.4), lo, hi, r.R(0.002, 0.006), 0.0003);
	}

	/// <summary>An old note unfolded and lifted: dry paper crinkling, a soft flap.</summary>
	public static double[] PaperRustle(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Crinkle(x, r, sr, 0.0, r.R(0.35, 0.55), 70, 1800, 9000, 0.6);
		Slap(x, r, sr, r.R(0.3, 0.5), 0.35, 300, 2500, 0.04, 0.01);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	// (2026-10-13, the "bouncy / circus" sweep) The everyday Foley below was built on Knock: a noise tap through a narrow
	// band-pass (Q 10-22), which rings at one pitch: a woodblock, a temple block, a glockenspiel "tink". Heard on every
	// pickup, every lantern toggle and every locked door, that's a percussion section. Now: broadband taps and clicks
	// (Slap, wide bands only), with at most a low-Q body under them (Q <= 3: the material's colour, never a note).

	/// <summary>Something taken up in the hand: a scuff off the surface, a cloth rustle into a pocket.</summary>
	public static double[] ItemTake(Rng r, int sr)
	{
		var x = Buf(sr, 0.8);
		// fingers closing on it and lifting it off whatever it lay on: a soft dull tap and a short scuff
		Slap(x, r, sr, 0.0, 0.45, 250, 2200, 0.012, 0.002);
		Slap(x, r, sr, 0.008, 0.35, 600, 4500, 0.03, 0.008);
		// into the coat: cloth sliding over cloth
		Slap(x, r, sr, r.R(0.12, 0.17), 0.3, 400, 3500, 0.07, 0.04);
		Crinkle(x, r, sr, 0.14, 0.45, 45, 700, 3800, 0.22);
		return FinishOneShot(Wet(x, sr, 1.0, 0.08, 0.35, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>The lantern on: the latch clicks, the shutter scrapes up, the flame catches with a soft fwump.</summary>
	public static double[] LanternOn(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		// the latch: two small metal clicks close together (sheet tin, dull: wide band, no ring)
		Slap(x, r, sr, 0.0, 0.7, 1800, 7500, 0.003, 0.0002);
		Slap(x, r, sr, 0.0, 0.3, 500, 2500, 0.006, 0.0004);
		Slap(x, r, sr, r.R(0.018, 0.03), 0.45, 1500, 6500, 0.0025, 0.0002);
		// the shutter sliding up its guides
		Slap(x, r, sr, 0.06, 0.25, 900, 5000, 0.05, 0.01);
		// the flame taking: a low breath of air
		Slap(x, r, sr, 0.14, 0.45, 60, 500, 0.12, 0.04);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>The lantern off: the shutter drops with a clack, the flame gutters out.</summary>
	public static double[] LanternOff(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		// the shutter dropping onto its stop: a flat tin clack
		Slap(x, r, sr, 0.0, 0.75, 1200, 6500, 0.005, 0.0002);
		Slap(x, r, sr, 0.0, 0.4, 300, 1800, 0.012, 0.0005);
		Slap(x, r, sr, r.R(0.014, 0.024), 0.25, 1500, 6000, 0.003, 0.0002);
		Slap(x, r, sr, 0.05, 0.25, 80, 700, 0.07, 0.02);
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.4, 0.5, 0.4), sr, -3, 80);
	}

	/// <summary>A locked door tried: the handle turned hard against the latch, the leaf rattling in its frame.</summary>
	public static double[] DoorLocked(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		double t = 0;
		int n = r.I(3, 4);
		for (int i = 0; i < n; i++)
		{
			double a = i == 0 ? 1.0 : r.R(0.6, 0.9);
			// the bolt knocking in the keeper: a hard short metal tap (wide, no ring)
			Slap(x, r, sr, t, 0.75 * a, 900, 6000, 0.006, 0.0003);
			// the leaf shifting in its frame: a dull wooden bump, low Q at most
			Slap(x, r, sr, t + 0.008, 0.6 * a, 90, 900, 0.035, 0.002);
			Knock(x, r, sr, t + 0.008, 0.18 * a, r.R(160, 260), 1.6, 0.03);
			// the handle's own rattle on its spindle
			Slap(x, r, sr, t + r.R(0.02, 0.04), 0.2 * a, 1500, 5000, 0.003, 0.0002);
			t += r.R(0.11, 0.19);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.18, 0.6, 0.5, 0.6), sr, -3, 100);
	}

	/// <summary>A step on gravel: a crunch of many small stones grinding under the weight.</summary>
	public static double[] StepGravel(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Slap(x, r, sr, 0.0, 0.6, 70, 400, 0.05, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.12, 0.2), 260, 1200, 7000, 0.7);
		Crinkle(x, r, sr, 0.03, 0.22, 90, 600, 3000, 0.4);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step in wet mud (the storm walk): the heel's soft slap, a suck as it sinks, and the wet pull out of it.</summary>
	public static double[] StepMud(Rng r, int sr)
	{
		var x = Buf(sr, 0.55);
		Slap(x, r, sr, 0.0, 0.75, 60, 380, 0.05, 0.006);
		// the suck: a short, falling, wet tone burst
		double f0 = r.R(170, 240);
		AddTone(x, sr, r.R(0.05, 0.08), r.R(0.08, 0.12), u => f0 * (1.0 - 0.45 * u), u => 0.28 * Env(u, 0.15, 0.6), new[] { 1.0, 0.35, 0.12 });
		// the pull out: wet crackle
		Crinkle(x, r, sr, r.R(0.16, 0.22), r.R(0.3, 0.38), 140, 500, 2600, 0.35);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step on wet fallen leaves: a soft pat and a damp, matted rustle (not the dry crackle of autumn).</summary>
	public static double[] StepLeaves(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		Slap(x, r, sr, 0.0, 0.5, 80, 500, 0.04, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.16, 0.24), 320, 900, 4200, 0.55);
		Crinkle(x, r, sr, 0.05, r.R(0.25, 0.32), 120, 400, 1800, 0.3);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>A step in dry fallen leaves: a crisp, papery crackle that runs on a moment after the foot lands.</summary>
	public static double[] StepDryLeaves(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		Slap(x, r, sr, 0.0, 0.35, 90, 600, 0.03, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.2, 0.3), 520, 1500, 7500, 0.7);
		Crinkle(x, r, sr, 0.06, r.R(0.28, 0.38), 220, 1000, 5000, 0.35);
		return FinishOneShot(Wet(x, sr, 1.0, 0.05, 0.25, 0.5, 0.25), sr, -3, 60);
	}

	/// <summary>A step that breaks a small dry twig underfoot among the leaves: a sharp crack in it, not loud.</summary>
	public static double[] StepTwig(Rng r, int sr)
	{
		var x = Buf(sr, 0.5);
		Slap(x, r, sr, 0.0, 0.35, 90, 600, 0.03, 0.004);
		Crinkle(x, r, sr, 0.0, r.R(0.14, 0.2), 380, 1400, 7000, 0.45);
		double t = r.R(0.02, 0.06);
		Knock(x, r, sr, t, 0.8, r.R(1800, 2600), 4, 0.008);
		Slap(x, r, sr, t, 0.7, 1500, 9000, 0.006, 0.0003);
		return FinishOneShot(Wet(x, sr, 1.0, 0.05, 0.25, 0.5, 0.25), sr, -3, 60);
	}

	/// <summary>A step onto a root: a hollow wooden knock under the sole, a scuff of bark.</summary>
	public static double[] StepRoot(Rng r, int sr)
	{
		var x = Buf(sr, 0.45);
		Knock(x, r, sr, 0.0, 0.7, r.R(160, 230), 6, 0.05);
		Knock(x, r, sr, 0.004, 0.35, r.R(480, 640), 9, 0.025);
		Crinkle(x, r, sr, 0.01, r.R(0.08, 0.13), 200, 700, 3000, 0.3);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 60);
	}

	/// <summary>Rain gathering on the boughs and falling off in fat drops onto the leaves below: two or three heavy taps,
	/// spaced, with their small splash.</summary>
	public static double[] CanopyDrip(Rng r, int sr)
	{
		// (2026-10-15, the owner: "still hearing those extra random footsteps in the rain that aren't the stalker's": these
		// were 2-4 soft 300-1600 Hz pats 0.18-0.45 s apart, a step's thud at a step's pace beside you) one light drop onto a
		// leaf, now and then a second right after it (a drop splitting, 40-110 ms): high and small, a plip, never a pat-pat
		var x = Buf(sr, 0.8);
		int n = r.Chance(0.35) ? 2 : 1;
		double t = 0.0;
		for (int k = 0; k < n; k++)
		{
			double a = r.R(0.6, 1.0) * (k == 0 ? 1.0 : 0.55);
			Slap(x, r, sr, t, a * 0.45, 900, 3600, r.R(0.004, 0.008), 0.0005);
			Slap(x, r, sr, t, a * 0.35, 2200, 7000, 0.002, 0.0002);
			Crinkle(x, r, sr, t + 0.003, t + 0.03, 800, 3000, 7000, a * 0.12);
			t += r.R(0.04, 0.11);
		}
		return FinishOneShot(Wet(x, sr, 1.0, 0.1, 0.45, 0.5, 0.4), sr, -3, 60);
	}

	/// <summary>The lantern's blacklight coming on: a switch click and the tube's electric buzz swelling in (tonal).</summary>
	public static double[] UvHum(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Knock(x, r, sr, 0.0, 0.8, r.R(2400, 2900), 20, 0.01);
		AddTone(x, sr, 0.03, 0.8, u => 120 * (1 + 0.002 * Math.Sin(u * 90)), u => 0.4 * Env(u, 0.3, 0.5), new[] { 0.5, 1.0, 0.7, 0.5, 0.35, 0.25, 0.15 });
		return FinishOneShot(Wet(x, sr, 1.0, 0.08, 0.3, 0.5, 0.3), sr, -3, 150);
	}

	/// <summary>(2026-10-13) The lantern's flame guttering out (it used the station's bulb_sputter, an electric buzz falling
	/// in pitch): the flame fluttering in a draught, faster and weaker, the wick sizzling, a last soft puff. Air and fire,
	/// no tone.</summary>
	public static double[] FlameGutter(Rng r, int sr)
	{
		double dur = r.R(1.1, 1.5);
		var x = Buf(sr, dur + 0.5);
		var lp = Biquad.Lp(sr, 520); var hp = Biquad.Hp(sr, 70);
		double ph = 0;
		for (int i = 0; i < (int)(dur * sr); i++)
		{
			double t = (double)i / sr, u = t / dur;
			// the flutter speeds up as it fails (6 -> 14 a second), and the flame's breath thins out
			ph += (6 + 8 * u + 2 * r.W()) / sr;
			double flutter = 0.55 + 0.45 * Math.Sin(TwoPi * ph);
			x[i] += hp.P(lp.P(r.W())) * flutter * flutter * Env(u, 0.05, 0.35) * (1 - 0.6 * u) * 1.4;
		}
		// the wick: a few tiny sizzles and spits
		for (double t = r.R(0.05, 0.15); t < dur; t += r.R(0.06, 0.25))
			Slap(x, r, sr, t, r.R(0.05, 0.18), 2000, 7000, r.R(0.002, 0.006), 0.0003);
		// out: a last soft puff of air
		Slap(x, r, sr, dur - 0.05, 0.35, 80, 600, 0.09, 0.03);
		return FinishOneShot(Wet(x, sr, 1.0, 0.06, 0.3, 0.5, 0.3), sr, -3, 120);
	}

	// ------------------------------------------------------------------ the haunting

	/// <summary>Somewhere overhead, or in the next room: old boards taking a slow weight, one step,
	/// a pause, another. Heard through a ceiling: low, dull, no top end.</summary>
	public static double[] BoardsOverhead(Rng r, int sr)
	{
		var x = Buf(sr, 4.5);
		int steps = r.I(2, 4);
		double t = 0.1;
		for (int i = 0; i < steps; i++)
		{
			Knock(x, r, sr, t, 0.8, r.R(70, 110), 5, 0.12);
			Creak(x, r, sr, t + 0.05, r.R(0.35, 0.8), 14, 30, new[] { (r.R(160, 230), 12.0, 1.0), (r.R(310, 420), 12.0, 0.6) }, 1.0);
			t += r.R(0.9, 1.5);
		}
		LowPass(x, sr, 900);
		return FinishOneShot(Wet(x, sr, 1.0, 0.35, 1.4, 0.6, 1.0), sr, -3, 300);
	}

	/// <summary>Air moaning through brickwork high up: a hollow, wavering tone rising and falling (tonal, never hiss).</summary>
	public static double[] WindMoan(Rng r, int sr)
	{
		double dur = r.R(5, 7);
		var x = Buf(sr, dur + 2);
		double f0 = r.R(150, 220);
		var drift = new Smooth(r, dur + 1, 0.7);
		AddTone(x, sr, 0, dur, u => f0 * (0.9 + 0.2 * drift.At(u * dur)) * (1 + 0.012 * Math.Sin(u * dur * 5.3)), u => Env(u, 0.35, 0.45), new[] { 1.0, 0.3, 0.12, 0.05 });
		AddTone(x, sr, 0.4, dur - 0.4, u => f0 * 1.498 * (0.92 + 0.16 * drift.At(u * dur + 3)), u => 0.35 * Env(u, 0.4, 0.4), new[] { 1.0, 0.2 });
		LowPass(x, sr, 1400);
		return FinishOneShot(Wet(x, sr, 0.5, 0.9, 3.5, 0.5, 1.6), sr, -3, 600);
	}

	/// <summary>Water dripping in a long, hard space: one drop, a pause, another, ringing off the walls.</summary>
	public static double[] DripEcho(Rng r, int sr)
	{
		var x = Buf(sr, 3.2);
		double t = 0.05;
		int n = r.I(2, 4);
		for (int i = 0; i < n; i++)
		{
			// the drop's hit on the water first, its little ring under it (a drip in a cave does ring, but briefly and
			// barely gliding: the clean sine rising half an octave was a "bloop")
			Slap(x, r, sr, t, 0.55, 1200, 7000, 0.003, 0.0002);
			Slap(x, r, sr, t, 0.25, 300, 1500, 0.008, 0.0005);
			Bubble(x, sr, t + 0.001, r.LogR(1100, 2200), 0.32, r.R(0.004, 0.008));
			t += r.R(0.5, 1.1);
		}
		return FinishOneShot(Wet(x, sr, 0.6, 0.9, 2.6, 0.4, 1.4), sr, -3, 400);
	}

	/// <summary>A heavy chain shifting somewhere far off in the dark: links grinding and clinking, settling.</summary>
	public static double[] ChainShift(Rng r, int sr)
	{
		var x = Buf(sr, 3.0);
		double t = 0;
		int n = r.I(9, 15);
		for (int i = 0; i < n; i++)
		{
			double a = Env((double)i / n, 0.2, 0.5);
			// iron links knocking (a dull clank: the Q-30 rings were wind chimes)
			Slap(x, r, sr, t, 0.6 * a, 800, 5000, 0.006, 0.0003);
			Knock(x, r, sr, t, 0.35 * a, r.R(1800, 3200), 6, 0.025);
			Knock(x, r, sr, t + 0.004, 0.3 * a, r.R(600, 900), 4, 0.03);
			t += r.R(0.05, 0.16);
		}
		LowPass(x, sr, 3500);
		return FinishOneShot(Wet(x, sr, 0.5, 0.9, 2.8, 0.5, 1.4), sr, -3, 400);
	}
}
