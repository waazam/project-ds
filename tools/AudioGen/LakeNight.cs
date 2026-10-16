namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Water;

/// <summary>
/// The lake from its shore in the small hours (2026-10-15): no hiss bed (the owner's rule for this lake), only
/// events. Little waves lapping on the stones of the beach (a soft wash up, a trickle back through the gravel), a
/// lap or two further along the shore, quieter and duller, and now and then something far out: a fish rising, a drop
/// off a branch. The reeds get their own one-shots, placed in the world by the game.
/// </summary>
public static class LakeNight
{
	/// <summary>The shoreline loop: lapping on stones near and along the beach, the odd plop far out. Circular.</summary>
	public static double[] Shore(Rng r, int sr, double sec)
	{
		var x = Buf(sr, sec);
		// near laps: a soft "shlop" up the stones, a short fizzing run back down through the gravel
		for (double t = r.R(0, 0.6); t < sec; t += r.R(1.4, 3.6))
		{
			double a = r.LogR(0.45, 1.0);
			Lake.Slap(x, r, sr, t, a * 0.6, 70, r.R(500, 800), r.R(0.04, 0.07), 0.02, true);
			Wash(x, r, sr, t, r.R(0.4, 0.7), 250, 1300, 1.0, a * 0.35, u => Env(u, 0.15, 0.6), 0.1, true);
			Grains(x, r, sr, t + r.R(0.2, 0.35), r.R(0.6, 1.0), 400, 1200, 6000, a * 0.18, u => Env(u, 0.1, 0.7), 0.001, 0.003, true);
			Cloud(x, r, sr, t + 0.05, 0.5, r.I(3, 8), 0.0015, 0.006, a * 0.16, u => 1 - u, 0.15, true);
		}
		// laps further along the beach: duller, quieter, in their own time
		for (double t = r.R(0, 2); t < sec; t += r.R(0.9, 2.8))
		{
			double a = r.LogR(0.12, 0.3);
			Lake.Slap(x, r, sr, t, a, 60, r.R(250, 450), r.R(0.05, 0.09), 0.03, true);
		}
		// far out: something rising, a drop
		for (double t = r.R(1, 5); t < sec; t += r.R(5, 11))
		{
			double a = r.LogR(0.06, 0.14);
			Lake.Slap(x, r, sr, t, a, 300, 2000, 0.012, 0.002, true);
			Minnaert(x, sr, t + 0.004, r.R(0.003, 0.008), a * 0.8, 0.2, true);
		}
		var h = new Hall(sr, 2.2, 0.55, 30, 1.2);
		var o = Circular(x, v => v * 0.8 + h.P(v) * 0.35);
		NormRms(o, -32);
		return o;
	}

	/// <summary>Something far out on the dark water: a fish rising, a plop, its ring of ripples; mostly the lake's reverb.</summary>
	public static double[] FarPlop(Rng r, int sr)
	{
		var x = Buf(sr, 3.2);
		double a = 1.0;
		int n = r.Chance(0.35) ? 2 : 1;
		double t = 0.02;
		for (int k = 0; k < n; k++)
		{
			Lake.Slap(x, r, sr, t, a, 200, r.R(1800, 3000), r.R(0.01, 0.02), 0.002);
			Minnaert(x, sr, t + 0.005, r.R(0.004, 0.012), a * r.R(0.6, 0.9), 0.15);
			Cloud(x, r, sr, t + 0.01, 0.15, r.I(2, 5), 0.0015, 0.005, a * 0.3, u => 1 - u);
			Grains(x, r, sr, t + 0.12, 0.3, 40, 1200, 5000, a * 0.2, u => 1 - u, 0.001, 0.003);
			t += r.R(0.3, 0.7); a *= 0.5;
		}
		LowPass(x, sr, 3500);
		return FinishOneShot(Room(x, sr, 0.5, 0.8, 2.4, 0.5, 1.3), sr, -3, 400);
	}

	/// <summary>Reeds in a breath of air: dry stalks rubbing and ticking, leaves brushing, a squeak of stem on stem.</summary>
	public static double[] Reeds(Rng r, int sr)
	{
		double dur = r.R(1.0, 2.0);
		var x = Buf(sr, dur + 0.5);
		double peak = r.R(0.3, 0.6);
		Func<double, double> gust = u => u < peak ? Math.Sin(0.5 * Math.PI * u / peak) : Math.Pow(Math.Cos(0.5 * Math.PI * (u - peak) / (1 - peak)), 1.5);
		Grains(x, r, sr, 0, dur, 900, 1800, 8000, 0.5, gust, 0.0008, 0.004);
		Wash(x, r, sr, 0, dur, 2500, 6000, 0.8, 0.35, gust, 0.15);
		int ticks = r.I(4, 10);
		for (int i = 0; i < ticks; i++)
			Modal(x, r, sr, r.R(0.05, dur * 0.9), r.R(0.15, 0.4), Plank(r, r.R(900, 1600), 0.008, 3), 0.3, 9000);
		if (r.Chance(0.6))
		{
			var modes = new[] { (r.R(1700, 2400), 14.0, 1.0), (r.R(3300, 4200), 18.0, 0.4) };
			StickSlip(x, r, sr, r.R(0.1, dur * 0.5), r.R(0.15, 0.35), r.R(60, 120), r.R(150, 260), modes, 0.25, u => Env(u, 0.3, 0.4));
		}
		HighPass(x, sr, 500);
		return FinishOneShot(Room(x, sr, 1.0, 0.15, 0.6, 0.5, 0.6), sr, -3, 150);
	}
}
