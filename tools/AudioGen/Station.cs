namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// Act 13, the forester station: its electrics dying, its drain, its clock, the flooding room and the
/// brass puzzle box, the lighter and the burning web, and the machine hum behind the iron door. Built
/// from the same events as the lake (bubbles, slaps, knocks, ticks) plus a few tonal pieces: mains
/// hum for the bulbs, a tuned brass click for the cryptex, a slow machine throb for the last room.
/// </summary>
public static class Station
{
	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>A caged bulb's mains buzz: 100 Hz and its odd harmonics, a faint filament sizzle, the
	/// level wandering a little. Whole cycles per loop so it repeats seamlessly.</summary>
	public static double[] BulbBuzz(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		int n = x.Length;
		double f0 = Math.Round(100 * sec) / sec;
		var wobble = new double[4]; var ph = new double[4];
		for (int i = 0; i < 4; i++) { wobble[i] = r.R(0.2, 1); ph[i] = r.R(0, TwoPi); }
		for (int i = 0; i < n; i++)
		{
			double t = (double)i / sr;
			double lvl = 0.8 + 0.2 * Math.Sin(TwoPi * 2 * t / sec + ph[0]) * Math.Sin(TwoPi * 3 * t / sec + ph[1]);
			double v = 0;
			foreach (var (h, a) in new[] { (1, 1.0), (3, 0.5), (5, 0.3), (7, 0.18), (9, 0.1) })
				v += a * Math.Sin(TwoPi * f0 * h * t + h);
			x[i] = Math.Tanh(v * 0.8) * lvl;
		}
		var sizzle = new double[n];
		for (double t = 0; t < sec; t += r.R(0.02, 0.12)) Slap(sizzle, r, sr, t, r.LogR(0.05, 0.25), 2500, 7000, 0.002, 0.0002, true);
		for (int i = 0; i < n; i++) x[i] += sizzle[i];
		NormRms(x, -30);
		return x;
	}

	/// <summary>The filament fighting: a crackling fizz, a dip of the hum, a tick of the relay.</summary>
	public static double[] BulbSputter(Rng r, int sr)
	{
		var x = Buf(sr, 0.9);
		for (double t = 0.01; t < 0.6; t += r.R(0.005, 0.04)) Slap(x, r, sr, t, r.LogR(0.1, 0.8) * Env(t / 0.6, 0.1, 0.5), 1500, 8000, r.R(0.001, 0.004), 0.0001);
		AddTone(x, sr, 0.0, 0.7, u => 100 * (1 - 0.1 * u), u => 0.4 * Env(u, 0.05, 0.6), new[] { 1.0, 0, 0.5, 0, 0.3 });
		Knock(x, r, sr, 0.05, 0.4, 2400, 8, 0.01);
		return FinishOneShot(x, sr, -3, 60);
	}

	/// <summary>The bulb gives out: a sharp pop, a tinkle of the filament.</summary>
	public static double[] BulbPop(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Slap(x, r, sr, 0.0, 1.0, 600, 9000, 0.008, 0.0001);
		Knock(x, r, sr, 0.001, 0.5, 3200, 12, 0.03);
		for (int i = 0; i < 6; i++) Knock(x, r, sr, 0.06 + i * r.R(0.03, 0.08), r.R(0.05, 0.15), r.LogR(4000, 7000), 30, 0.02);
		return FinishOneShot(Wet(x, sr, 1, 0.2, 0.6), sr, -3, 150);
	}

	/// <summary>Seven seconds of a basement draining: a throat of water gulping down a pipe, the swirl
	/// speeding up, a last long sucking slurp and the pipe knocking as it empties.</summary>
	public static double[] DrainGurgle(Rng r, int sr)
	{
		double dur = 7.5;
		var x = Buf(sr, dur + 1);
		for (double t = 0.05; t < dur; t += r.R(0.3, 1.7) / (15 + 60 * Env(t / dur, 0.4, 0.2)))
		{
			double u = t / dur;
			Bubble(x, sr, t, r.LogR(90 + 100 * u, 400 + 200 * u), r.LogR(0.2, 0.9) * Env(u, 0.3, 0.15), r.R(0.015, 0.05));
		}
		var lp = Biquad.Lp(sr, 300); var lp2 = Biquad.Lp(sr, 380);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / sr / dur;
			x[i] += lp2.P(lp.P(r.W())) * 0.6 * Env(Math.Min(u, 1), 0.5, 0.1) * (0.6 + 0.4 * Math.Sin(TwoPi * (1.5 + 4 * u) * u * dur));
		}
		AddTone(x, sr, dur - 1.4, 1.2, u => 180 - 120 * u, u => 0.5 * Env(u, 0.1, 0.6), new[] { 1.0, 0.4, 0.2 });   // the last slurp
		for (int i = 0; i < 4; i++) Knock(x, r, sr, dur - 0.6 + i * 0.18, 0.4, r.R(120, 180), 6, 0.08);
		return FinishOneShot(Wet(x, sr, 1, 0.35, 1.2, 0.5, 0.7), sr, -3, 300);
	}

	/// <summary>A wall clock ticking in a quiet room: tick... tock, one a second. Two seconds exactly per
	/// cycle, twenty cycles.</summary>
	public static double[] ClockTick(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		for (int s = 0; s < (int)sec; s++)
		{
			bool tock = s % 2 == 1;
			Knock(x, r, sr, s + 0.001, tock ? 0.8 : 1.0, tock ? r.R(1900, 2100) : r.R(2500, 2700), 14, 0.012, true);
			Slap(x, r, sr, s + 0.001, 0.3, 3000, 8000, 0.002, 0.0001, true);
		}
		var o = Circular(x, v => v);
		NormRms(o, -34);
		return o;
	}

	/// <summary>Old window glass under strain: a single dry crack and a creak through the frame.</summary>
	public static double[] GlassCrack(Rng r, int sr)
	{
		var x = Buf(sr, 1.0);
		Slap(x, r, sr, 0.0, 1.0, 1500, 9000, 0.004, 0.0001);
		for (int i = 0; i < 8; i++) Knock(x, r, sr, 0.003 + i * 0.012, r.R(0.2, 0.6), r.LogR(2500, 6500), 20, 0.02);
		for (double t = 0.1; t < 0.6; t += r.R(0.02, 0.06)) Slap(x, r, sr, t, r.LogR(0.05, 0.2), 2000, 7000, 0.002, 0.0001);
		return FinishOneShot(Wet(x, sr, 1, 0.25, 0.8), sr, -3, 150);
	}

	/// <summary>The window going: a bang, glass bursting and raining down, and the lake coming in behind it.</summary>
	public static double[] GlassShatter(Rng r, int sr)
	{
		var x = Buf(sr, 3.0);
		Slap(x, r, sr, 0.0, 1.0, 40, 3000, 0.08, 0.001);
		for (double t = 0.005; t < 1.6; t += r.R(0.3, 1.7) / (400 * Math.Exp(-t * 2.5) + 10))
			Knock(x, r, sr, t, r.LogR(0.05, 0.6) * Math.Exp(-t), r.LogR(2200, 8000), r.R(15, 40), r.R(0.01, 0.04));
		for (double t = 0.1; t < 2.6; t += r.R(0.3, 1.7) / 120)
		{
			double u = t / 2.6;
			if (r.Chance(0.4)) Slap(x, r, sr, t, r.LogR(0.08, 0.45) * Env(u, 0.1, 0.6), 250, 3000, r.R(0.01, 0.04), 0.002);
			else Bubble(x, sr, t, r.LogR(150, 900), r.LogR(0.08, 0.4) * Env(u, 0.1, 0.6), r.R(0.01, 0.03));
		}
		return FinishOneShot(Wet(x, sr, 1, 0.35, 1.2, 0.4, 0.8), sr, -3, 300);
	}

	/// <summary>Water pouring in through a window and crashing onto a floor: a dense, continuous tumble
	/// of splashes and churn, circular so it loops.</summary>
	public static double[] Torrent(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		for (double t = 0; t < sec; t += r.R(0.3, 1.7) / 260)
		{
			if (r.Chance(0.45)) Slap(x, r, sr, t, r.LogR(0.1, 0.6), r.R(200, 500), r.R(1500, 4500), r.R(0.01, 0.05), 0.003, true);
			else Bubble(x, sr, t, r.LogR(120, 1100), r.LogR(0.08, 0.45), r.R(0.01, 0.035), true);
		}
		var h = new Hall(sr, 0.9, 0.45, 15, 0.6);
		var o = Circular(x, v => v + h.P(v) * 0.35);
		NormRms(o, -21);
		return o;
	}

	/// <summary>The blood drawn back out of the window: a torrent played backward, swelling to a long
	/// indrawn roar that stops dead, with a low moan under it.</summary>
	public static double[] BloodSuck(Rng r, int sr)
	{
		var fwd = Torrent(r.Fork(), sr, 4.0);
		var x = Buf(sr, 5.0);
		for (int i = 0; i < fwd.Length; i++)
		{
			double u = (double)i / fwd.Length;
			x[i] = fwd[fwd.Length - 1 - i] * (0.2 + 0.8 * u * u);
		}
		AddTone(x, sr, 0.3, 3.6, u => 55 + 25 * u, u => 0.35 * Env(u, 0.4, 0.05), new[] { 1.0, 0.6, 0.3, 0.1 });
		for (int i = (int)(4.0 * sr); i < x.Length; i++) x[i] *= Math.Exp(-(i - 4.0 * sr) / (0.02 * sr));
		return FinishOneShot(Wet(x, sr, 1, 0.3, 1.4, 0.45, 0.9), sr, -3, 200);
	}

	/// <summary>One cryptex ring clacking round a notch: a bright brass tick with a detent spring behind it.</summary>
	public static double[] CryptexClick(Rng r, int sr)
	{
		var x = Buf(sr, 0.35);
		Knock(x, r, sr, 0.0, 1.0, r.R(2600, 3200), 18, 0.018);
		Knock(x, r, sr, 0.002, 0.5, r.R(1100, 1400), 10, 0.025);
		Knock(x, r, sr, r.R(0.035, 0.05), 0.35, r.R(3800, 4400), 20, 0.01);   // the detent
		return FinishOneShot(x, sr, -3, 40);
	}

	/// <summary>A ring that won't go: brass grinding on grit, a strain, a dull clunk back.</summary>
	public static double[] CryptexStick(Rng r, int sr)
	{
		var x = Buf(sr, 0.6);
		double ph = 0;
		var bp = Biquad.Bp(sr, r.R(900, 1300), 3);
		for (int i = 0; i < (int)(0.32 * sr); i++)
		{
			double t = (double)i / sr;
			ph += r.R(60, 140) / sr;
			double imp = 0;
			if (ph >= 1) { ph -= 1; imp = r.R(0.4, 1); }
			x[i] = bp.P(imp + r.W() * 0.08) * Env(t / 0.32, 0.2, 0.3);
		}
		Knock(x, r, sr, 0.33, 0.6, r.R(500, 700), 5, 0.05);
		return FinishOneShot(x, sr, -3, 60);
	}

	/// <summary>The cryptex giving: a mechanism unlatching, the cap drawn out along its tube, a soft thunk.</summary>
	public static double[] CryptexOpen(Rng r, int sr)
	{
		var x = Buf(sr, 1.6);
		for (int i = 0; i < 6; i++) Knock(x, r, sr, 0.02 + i * 0.045, r.R(0.4, 0.8), r.R(2400, 3600), 16, 0.015);
		var bp = Biquad.Bp(sr, 1800, 2);
		for (int i = (int)(0.35 * sr); i < (int)(1.3 * sr); i++)
		{
			double u = ((double)i / sr - 0.35) / 0.95;
			x[i] += bp.P(r.W()) * 0.25 * Env(u, 0.2, 0.3);
		}
		Knock(x, r, sr, 1.3, 0.7, 380, 4, 0.06);
		return FinishOneShot(Wet(x, sr, 1, 0.15, 0.5), sr, -3, 100);
	}

	/// <summary>A flip-top lighter: the lid's clink, the wheel's rasp on the flint, the wick taking.</summary>
	public static double[] LighterFlick(Rng r, int sr)
	{
		var x = Buf(sr, 1.2);
		Knock(x, r, sr, 0.0, 0.8, 3400, 14, 0.03);
		Knock(x, r, sr, 0.003, 0.4, 5200, 20, 0.02);
		for (double t = 0.25; t < 0.36; t += 0.004) Slap(x, r, sr, t, r.R(0.3, 0.8), 2000, 8000, 0.002, 0.0001);
		var lp = Biquad.Lp(sr, 900);
		for (int i = (int)(0.36 * sr); i < x.Length; i++)
		{
			double u = ((double)i / sr - 0.36) / 0.8;
			x[i] += lp.P(r.W()) * 0.35 * Env(Math.Min(u, 1), 0.15, 0.6);
		}
		return FinishOneShot(x, sr, -3, 120);
	}

	/// <summary>Old web catching: a soft whoomp, then a spitting, crackling rush that races across it and dies.</summary>
	public static double[] WebBurn(Rng r, int sr)
	{
		double dur = 3.4;
		var x = Buf(sr, dur + 0.5);
		var lp = Biquad.Lp(sr, 500);
		for (int i = 0; i < x.Length; i++)
		{
			double u = (double)i / sr / dur;
			x[i] = lp.P(r.W()) * 0.5 * Env(Math.Min(u, 1), 0.05, 0.6);
		}
		for (double t = 0.05; t < dur; t += r.R(0.3, 1.7) / (40 + 200 * Env(t / dur, 0.15, 0.5)))
			Slap(x, r, sr, t, r.LogR(0.1, 0.9) * Env(t / dur, 0.1, 0.5), r.R(800, 2000), r.R(4000, 8000), r.R(0.001, 0.005), 0.0001);
		return FinishOneShot(Wet(x, sr, 1, 0.25, 0.9), sr, -3, 200);
	}

	/// <summary>A foot going down into shin-deep water and coming up again: a slosh and a drip.</summary>
	public static double[] Wade(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		Slap(x, r, sr, 0.0, 0.8, 90, 1400, r.R(0.05, 0.09), 0.01);
		Churn(x, r, sr, 0.02, 0.35, 60, 180, 800, 0.5);
		Patter(x, r, sr, 0.3, 0.6, 14, 0.2);
		return FinishOneShot(x, sr, -3, 60);
	}

	/// <summary>A hinge that hasn't moved in years (the cigar box's lid): a short dry creak.</summary>
	public static double[] SmallCreak(Rng r, int sr)
	{
		var x = Buf(sr, 0.7);
		double ph = 0;
		var bp1 = Biquad.Bp(sr, r.R(700, 900), 12); var bp2 = Biquad.Bp(sr, r.R(1500, 1900), 14);
		for (int i = 0; i < (int)(0.45 * sr); i++)
		{
			double u = (double)i / (0.45 * sr);
			ph += (40 + 50 * u) / sr;
			double imp = 0;
			if (ph >= 1) { ph -= 1; imp = r.R(0.5, 1) * Env(u, 0.2, 0.3); }
			x[i] = bp1.P(imp) + 0.6 * bp2.P(imp);
		}
		return FinishOneShot(x, sr, -3, 60);
	}

	/// <summary>The machine behind the iron door: a slow heavy throb (whole cycles per loop), a low hum
	/// with a sour second note, and iron knocking somewhere in it now and then.</summary>
	public static double[] IndustrialDrone(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		double f0 = Math.Round(41 * sec) / sec, beat = Math.Round(0.55 * sec) / sec;
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double throb = 0.55 + 0.45 * Math.Pow(0.5 + 0.5 * Math.Sin(TwoPi * beat * t), 3);
			double v = Math.Sin(TwoPi * f0 * t) + 0.5 * Math.Sin(TwoPi * f0 * 2 * t + 1) + 0.25 * Math.Sin(TwoPi * f0 * 3 * t + 2)
				+ 0.3 * Math.Sin(TwoPi * Math.Round(f0 * 1.06 * sec) / sec * t);   // the sour one
			x[i] = Math.Tanh(v * 0.7) * throb;
		}
		for (double t = r.R(0, 2); t < sec; t += r.R(2.5, 6))
		{
			Knock(x, r, sr, t, r.R(0.3, 0.8), r.R(140, 260), 5, r.R(0.15, 0.3), true);
			Slap(x, r, sr, t, 0.3, 400, 2500, 0.02, 0.001, true);
		}
		var h = new Hall(sr, 2.2, 0.5, 30, 1.2);
		var o = Circular(x, v => v * 0.8 + h.P(v) * 0.5);
		NormRms(o, -22);
		return o;
	}
}
