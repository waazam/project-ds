namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The wendigo's howl (2026-10-22, the owner: the old one "sounds like a sawblade revving up like some kind of weird hyper
/// fast rc car"; it should be animal, not machine). The old howl (tools/WinterAudio/make_crawl_audio.py) was a sawtooth,
/// thirteen stacked harmonics, swept smoothly up threefold with a fast, perfectly regular wobble: an engine's spectrum
/// and an engine's rev. This one is built as an animal's: a big throat's glottal pulses, every cycle a little different
/// (jitter), breaking into period doubling where it strains (the growl at its start and its ragged end), a second throat
/// a few percent off beating against it, a great deal of breath torn through the open mouth, and a jaw that opens and
/// closes as it goes (the formants moving). Its pitch rises only about half again, and drifts as a voice does, never a
/// clean sweep. The harsher ones begin with a ragged in-breath and end in a wet snort. A long mammal's vocal tract keeps
/// the formants low, so nothing in it whines.
/// </summary>
public static partial class Creature
{
	/// <param name="take">1..4, each worse (longer, rougher, more breath); the far ones are 1 at a medium roughness.</param>
	/// <param name="far">Through the walls and the trees: low-passed, mostly the hall's.</param>
	public static double[] WendigoHowl(Rng r, int sr, int take, bool far)
	{
		double harsh = far ? 0.35 : (take - 1) / 3.0;
		double gasp = far ? 0.0 : 0.28 + 0.2 * harsh;            // the in-breath before it (s)
		double voiceSec = (far ? r.R(2.4, 3.0) : 2.2 + 0.45 * (take - 1)) + r.R(0, 0.3);
		double dur = gasp + voiceSec + 0.25;
		var x = Buf(sr, dur);

		var larA = new Larynx(r, sr) { Jitter = 0.06 + 0.05 * harsh, Fry = 0.15, Width = 0.25 };
		var larB = new Larynx(r.Fork(), sr) { Jitter = 0.09 + 0.04 * harsh, Fry = 0.3, Width = 0.21 };
		var larSub = new Larynx(r.Fork(), sr) { Jitter = 0.08, Fry = 0.45, Width = 0.3 };   // the chest an octave under
		var mouth = new Mouth(sr) { G1 = 1.0, G2 = 0.6, G3 = 0.22, Q = 4.5 };
		var subLp = Biquad.Lp(sr, 260);
		var drift = new Smooth(r, dur, 0.13);     // the voice wandering
		var waver = new Smooth(r, dur, 0.05);     // a ragged, uneven waver (not a vibrato's clock)
		var jaw = new Smooth(r, dur, 0.22);
		var strain = new Smooth(r, dur, 0.18);    // where it breaks into a growl
		var shake = new Smooth(r, dur, 0.035);    // breath shaking the level
		var asp = Biquad.Bp(sr, 1150, 1.1);
		var asp2 = Biquad.Bp(sr, 2600, 1.6);
		var gaspBp = Biquad.Bp(sr, 900, 1.4);
		var snortBp = Biquad.Bp(sr, 520, 2.2);
		double lo = r.R(70, 86) * (1 - 0.12 * harsh), peak = lo * r.R(1.38, 1.58);
		double peakAt = r.R(0.3, 0.4);

		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr;
			double y = 0;
			// the in-breath: a rough, rising draw of air through a wet throat
			if (t < gasp)
			{
				double g = t / gasp;
				double e = Math.Sin(Math.PI * g) * (0.6 + 0.4 * shake.At(t));
				gaspBp.SetBp(600 + 700 * g, 1.4);
				y += gaspBp.P(r.W()) * e * (0.35 + 0.3 * harsh);
			}
			double tv = t - gasp, u = tv / voiceSec;
			if (u > 0 && u < 1)
			{
				// up out of a groan, held, dragged down and breaking
				double rise = u < peakAt ? Math.Pow(u / peakAt, 0.8) : u < 0.66 ? 1 - 0.08 * (u - peakAt) / (0.66 - peakAt) : 0.92 - 0.5 * Math.Pow((u - 0.66) / 0.34, 1.3);
				double f = lo + (peak - lo) * rise;
				f *= 1 + 0.07 * (drift.At(t) - 0.5) + 0.035 * (waver.At(t) - 0.5);
				// the growl: at its start, at its end and wherever it strains (the worse, the more)
				double edge = Math.Max(1 - u / 0.14, (u - 0.72) / 0.28);
				double fry = Math.Clamp(0.12 + 0.55 * Math.Max(0, edge) + 0.35 * harsh * strain.At(t), 0, 0.85);
				larA.Fry = fry; larB.Fry = Math.Min(0.9, fry + 0.15);
				// the jaw: open as it climbs, and working
				double open = Math.Clamp(0.15 + 0.75 * rise + 0.25 * (jaw.At(t) - 0.5), 0, 1);
				mouth.F1 = 290 + 430 * open; mouth.F2 = 720 + 520 * open; mouth.F3 = 2050 + 450 * open;
				double src = larA.P(f) + larB.P(f * (1.03 + 0.01 * harsh)) * 0.6;
				double voiced = Math.Tanh(mouth.P(src) * (2.6 + 2.6 * harsh));
				// breath torn through the open mouth: a lot of it, more the worse it is
				double breath = (asp.P(r.W()) * 0.9 + asp2.P(r.W()) * 0.35) * (0.22 + 0.3 * harsh) * (0.4 + 0.6 * open);
				double chest = subLp.P(larSub.P(f * 0.5)) * (0.45 + 0.25 * harsh);
				double env = Env(u, 0.1, 0.3) * (0.82 + 0.18 * shake.At(t));
				y += (voiced * 0.8 + breath + chest) * env;
			}
			// the end: a wet snort out through the nose (the worse ones)
			double ts = tv - voiceSec;
			if (!far && harsh > 0.3 && ts > -0.05 && ts < 0.2)
			{
				double e = Math.Exp(-Math.Max(0, ts) / 0.06) * Math.Min(1, (ts + 0.05) / 0.02);
				y += snortBp.P(r.W()) * e * 0.5 * harsh;
			}
			x[i] = y;
		}
		HighPass(x, sr, 45);
		if (far)
		{
			LowPass(x, sr, 1000);
			var hall = new Hall(sr, 2.4, 0.55, 50, 1.3);
			var o = new double[x.Length];
			for (int i = 0; i < x.Length; i++) o[i] = x[i] * 0.35 + hall.P(x[i]) * 1.1;
			return FinishOneShot(o, sr, -3, 500);
		}
		LowPass(x, sr, 4800);
		return FinishOneShot(Room(x, sr, 0.3, 0.6, 0.45), sr, -3, 220);
	}

	/// <summary>
	/// Its leap up into the trees (2026-10-22: the old one's bough creak was a sine warbled at a steady 3 Hz and chopped by a
	/// 31 Hz square: a motor's buzz). An animal's grunt as it goes, the air torn past, bark cracking under its hands, the
	/// bough taking its weight in a real wood's groan (irregular stick-slip clicks through the timber's ring, never a steady
	/// buzz), and the snow shaken off sifting down.
	/// </summary>
	public static double[] WendigoLeap(Rng r, int sr, int take)
	{
		double dur = 1.7;
		var x = Buf(sr, dur);
		// the grunt: a short, hard push from the chest
		var lar = new Larynx(r, sr) { Jitter = 0.1, Fry = 0.55, Width = 0.22 };
		var mouth = new Mouth(sr) { F1 = r.R(380, 480), F2 = r.R(820, 980), F3 = 2100, G2 = 0.5, G3 = 0.15, Q = 5 };
		var gruntBreath = Biquad.Bp(sr, 1000, 1.3);
		double g0 = r.R(85, 105), gLen = r.R(0.22, 0.3);
		// the rush of air, its band climbing
		var rush = new Biquad(sr);
		// the bough: stick-slip in the wood
		var wood = Biquad.Bp(sr, r.R(260, 340), 7);
		var wood2 = Biquad.Bp(sr, r.R(620, 760), 9);
		double nextSlip = 0.36, slip = 0;
		var sift = Biquad.Bp(sr, 5200, 0.8);
		for (int i = 0; i < x.Length; i++)
		{
			double t = (double)i / sr, y = 0;
			if (t < gLen)
			{
				double u = t / gLen, f = g0 * (1 + 0.25 * u) * (1 - 0.3 * u * u);
				double e = Env(u, 0.15, 0.55);
				y += (Math.Tanh(mouth.P(lar.P(f)) * 3.5) * 0.7 + gruntBreath.P(r.W()) * 0.35) * e;
			}
			double rw = Math.Exp(-Math.Pow((t - 0.16) / 0.07, 2));
			rush.SetBp(500 + 1500 * Math.Clamp((t - 0.06) / 0.2, 0, 1), 1.1);
			y += rush.P(r.W()) * rw * 0.9;
			// the groan: clicks at an uneven, slowing rate as the weight settles, each ringing the wood
			double groan = Math.Exp(-Math.Pow((t - 0.5) / 0.2, 2));
			if (t >= nextSlip && t < 0.95)
			{
				slip = r.R(0.5, 1.0);
				nextSlip = t + (1.0 / r.R(28, 55)) * (1 + 1.5 * Math.Max(0, t - 0.5));
			}
			double imp = slip; slip *= 0.6;
			y += (wood.P(imp) * 2.2 + wood2.P(imp) * 0.9) * groan;
			// snow sifting down after
			if (t > 0.25) y += sift.P(r.W()) * 0.18 * Math.Min(1, (t - 0.25) / 0.1) * Math.Exp(-(t - 0.25) / 0.5);
			x[i] = y;
		}
		// bark cracking under it
		for (int c = 0, n = r.I(5, 9); c < n; c++)
		{
			int at = (int)(sr * (0.17 + r.R(0, 0.14))), len = (int)(sr * 0.012);
			double gain = r.R(0.4, 1.0);
			for (int k = 0; k < len && at + k < x.Length; k++) x[at + k] += r.W() * Math.Exp(-k / (len / 4.0)) * gain;
		}
		HighPass(x, sr, 50);
		LowPass(x, sr, 6500);   // (bark and snow, not hiss)
		return FinishOneShot(Room(x, sr, 0.25, 0.5, 0.5), sr, -3, 200);
	}
}
