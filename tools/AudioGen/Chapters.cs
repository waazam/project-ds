namespace ProjectDS.Tools.AudioGen;

using static Dsp;
using static Lake;

/// <summary>
/// 2026-10-10c: the chapters' title drops (the owner: "add chapter title cards to the game to make it feel like a book").
/// <list type="bullet">
/// <item>chapter_hit: a breath of air drawn in (a reversed swell of low noise), then one deep blow under the floor
/// (a sub that drops in pitch) and a low, dark chord left ringing in a big room. Heavy, never sharp;</item>
/// <item>title_drop: the game's name, at the stairs, as the forest goes dead silent: no blow, only a very low bell-like
/// tone, two notes not quite agreeing, dying away a long while.</item>
/// </list>
/// </summary>
public static class Chapters
{
	public static double[] ChapterHit(Rng r, int sr)
	{
		var x = new double[(int)(9.0 * sr)];
		double hit = 1.25;
		// the swell: low noise, filtered, rising to the blow (and cut dead at it)
		var lp = Biquad.Lp(sr, 380); var bp = Biquad.Bp(sr, 160, 0.8);
		for (int i = 0; i < (int)(hit * sr); i++)
		{
			double u = (double)i / (hit * sr);
			x[i] += (lp.P(r.W()) * 1.2 + bp.P(r.W()) * 0.8) * u * u * u * 0.9;
		}
		// the blow: a sub falling from 62 to 34 Hz, its knock on the front
		double p = 0;
		for (int i = (int)(hit * sr); i < x.Length; i++)
		{
			double t = (double)i / sr - hit;
			double f = 34 + 28 * System.Math.Exp(-t * 3.0);
			p += 2 * System.Math.PI * f / sr;
			x[i] += System.Math.Sin(p) * System.Math.Exp(-t * 0.9) * 1.1 * System.Math.Min(1, t * 200);
		}
		Knock(x, r, sr, hit, 0.6, 90, 3, 0.08);
		// the chord: a low minor second and a fifth under it, piano-ish, ringing
		foreach (var (f, a) in new[] { (55.0, 0.35), (58.27, 0.22), (82.4, 0.25), (110.0, 0.12), (116.5, 0.08) })
			AddTone(x, sr, hit + 0.02, 7.5, _ => f, t => a * System.Math.Exp(-t * 0.55) * System.Math.Min(1, t * 60), new[] { 1.0, 0.35, 0.12, 0.05 });
		return FinishOneShot(Wet(x, sr, 0.9, 0.55, 4.5, 0.6, 1.6), sr, -3, 1500);
	}

	public static double[] TitleDrop(Rng r, int sr)
	{
		var x = new double[(int)(10.0 * sr)];
		// a bell far down: 49 and 49.6 Hz beating slowly, an inharmonic overtone or two, a long, long decay
		foreach (var (f, a) in new[] { (49.0, 0.5), (49.6, 0.4), (122.5, 0.16), (171.0, 0.08), (277.0, 0.03) })
			AddTone(x, sr, 0.3, 9.5, _ => f, t => a * System.Math.Exp(-t * (0.32 + f / 1200.0)) * System.Math.Min(1, t * 8));
		// air under it
		var lp = Biquad.Lp(sr, 260);
		for (int i = 0; i < x.Length; i++) { double t = (double)i / sr; x[i] += lp.P(r.W()) * 0.25 * Env(t / 10.0, 0.2, 0.6); }
		return FinishOneShot(Wet(x, sr, 0.8, 0.6, 5.0, 0.55, 1.7), sr, -3, 2000);
	}
}
