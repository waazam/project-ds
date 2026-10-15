namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>
/// The player's breath (2026-10-15 rework, after the owner's "fix our breathe" and a look at how horror games do it:
/// Outlast's breath is the player's fear gauge, The Last of Us Part II drives its breathing sets from one heart-rate value
/// fed by sprinting, damage and nearby enemies, INSIDE locks the boy's breaths to his footsteps). One breath per file,
/// many takes per set, so <c>PlayerBreathing</c> never strings the same one twice.
///
/// Each breath is airflow through a throat: a flow curve (how hard the air moves, moment to moment) drives turbulence
/// noise whose brightness rises with the flow, through the tract's resonances:
/// <list type="bullet">
/// <item><b>nose</b>: calm and walking. A nasal murmur (~280 Hz and ~1 kHz, a dip near 700), soft and dark, a faint
/// whistle of hiss on top. Slow, even.</item>
/// <item><b>mouth</b>: jogging and running. An open "hah" (F1 ~650, F2 ~1200, F3 ~2500, wide), the inhale drawn past the
/// teeth (a frictive band at 3-5 kHz), shorter and harder with effort.</item>
/// <item><b>winded</b>: after a hard run. Heavy and wet, the exhale voiced a little (a breathy "hhah" at ~110 Hz), the
/// chest heard under it; a lip or saliva click now and then at the mouth's opening.</item>
/// <item><b>scared</b>: shallow and quick, the air shaking (an irregular 6-11 Hz tremor, the inhale caught in two or three
/// stutters), a thin voiced quaver in some exhales, a whimper's edge. No words.</item>
/// <item><b>gasp</b>: a fright's catch. A sharp drawn-in breath stopped dead, sometimes twice.</item>
/// </list>
/// </summary>
public static class Breath
{
	public enum Kind { Nose, Mouth, Winded, Scared, Gasp }

	static double[] Buf(int sr, double sec) => new double[(int)(sec * sr)];

	/// <summary>One breath of <paramref name="kind"/>; <paramref name="effort"/> 0..1 picks where in the set it sits.</summary>
	public static double[] One(Rng r, int sr, Kind kind, bool inhale, double effort)
	{
		// --- duration and flow shape ---------------------------------------------------------------------------------
		double dur = kind switch
		{
			Kind.Nose => inhale ? r.R(1.0, 1.5) : r.R(1.2, 1.9),
			Kind.Mouth => (inhale ? r.R(0.42, 0.7) : r.R(0.5, 0.85)) * (1 - 0.3 * effort),
			Kind.Winded => inhale ? r.R(0.45, 0.75) : r.R(0.6, 1.05),
			Kind.Scared => inhale ? r.R(0.38, 0.7) : r.R(0.5, 1.0),
			_ => r.R(0.22, 0.38),
		};
		bool doubleGasp = kind == Kind.Gasp && r.Chance(0.35);
		double total = dur + (doubleGasp ? r.R(0.12, 0.2) + dur * 0.7 : 0) + 0.3;
		var x = Buf(sr, total);
		int n = x.Length;

		// where the breath's air peaks (0..1 of its length), and how it leans: inhales swell and stop, exhales let go
		double peakAt = kind switch
		{
			Kind.Gasp => r.R(0.55, 0.75),
			_ when inhale => r.R(0.45, 0.7),
			Kind.Nose => r.R(0.18, 0.3),
			_ => r.R(0.08, 0.18),
		};
		double Flow(double u)
		{
			if (u <= 0 || u >= 1) return 0;
			double a = u < peakAt ? Math.Sin(0.5 * Math.PI * u / peakAt) : 1;
			double tail = kind == Kind.Gasp ? 0.08 : inhale ? 0.22 : 0.85;   // a gasp stops dead
			double b = u > peakAt ? Math.Pow(Math.Max(0, 1 - (u - peakAt) / (1 - peakAt)), inhale ? 0.6 : 1.6) : 1;
			if (u > 1 - tail * (1 - peakAt) && kind == Kind.Gasp) b *= Math.Max(0, (1 - u) / (tail * (1 - peakAt)));
			return a * a * b;
		}

		// --- the tract: formants (centre, bandwidth, level) -------------------------------------------------------------
		(double f, double bw, double g)[] tract = kind switch
		{
			Kind.Nose => new[] { (r.R(210, 255), 110.0, 1.0), (r.R(790, 950), 180.0, 0.7), (r.R(1850, 2150), 280.0, 0.42) },
			Kind.Scared => new[] { (r.R(430, 530), 150.0, 1.0), (r.R(1160, 1490), 210.0, 0.6), (r.R(2160, 2490), 300.0, 0.28) },
			_ => new[] { (r.R(500, 630), 170.0, 1.0), (r.R(910, 1120), 215.0, 0.72), (r.R(2000, 2240), 300.0, 0.36) },
		};
		var form = tract.Select(t => new Biquad(sr)).ToArray();
		var drift = tract.Select(_ => new Smooth(r.Fork(), total, 0.4)).ToArray();
		// the frictive edge: the inhale past the teeth (mouth), the nose's faint whistle
		var fric = Biquad.Bp(sr, kind == Kind.Nose ? r.R(2500, 3200) : r.R(3000, 4300), 1.1);
		var fricLp = Biquad.Lp(sr, kind == Kind.Nose ? 5000 : 7500);
		double fricAmt = kind switch
		{
			Kind.Nose => 0.1,
			Kind.Gasp => 0.16,
			_ when inhale => 0.12 + 0.06 * effort,
			_ => 0.03 + 0.03 * effort,
		};
		// the chest under heavy breaths
		var chest = Biquad.Lp(sr, 150);   // (2026-10-15b: deeper, the chest more)
		double chestAmt = kind == Kind.Winded ? 0.5 : kind == Kind.Mouth ? 0.08 + 0.16 * effort : kind == Kind.Nose ? 0.06 : 0.1;
		// the nose's dip near 700 Hz (an anti-resonance, roughly: subtract a band)
		var notch = Biquad.Bp(sr, r.R(540, 650), 2.0);
		var pink = new Pink();
		var bright = Biquad.Lp(sr, 1000);

		// voicing: winded exhales and scared ones carry a breathy voice under the air
		bool voiced = !inhale && (kind == Kind.Winded && r.Chance(0.5 + 0.4 * effort) || kind == Kind.Scared && r.Chance(0.55));
		double f0 = kind == Kind.Scared ? r.R(110, 140) : r.R(85, 105);
		double voiceAmt = voiced ? (kind == Kind.Scared ? r.R(0.02, 0.04) : r.R(0.025, 0.05)) : 0;
		var vib = new Smooth(r.Fork(), total, kind == Kind.Scared ? 0.045 : 0.15);
		var vForm = new[] { Biquad.Bp(sr, tract[0].f, 2.5), Biquad.Bp(sr, tract[1].f, 3.5) };
		double ph = 0;

		// shaking (scared): an irregular tremor on the air, and the inhale caught in stutters
		var tremorRate = new Smooth(r.Fork(), total, 0.25);
		double trPh = r.R(0, 2 * Math.PI);
		double tremorDepth = kind == Kind.Scared ? r.R(0.35, 0.65) : kind == Kind.Winded ? 0.08 : 0;
		int stutters = kind == Kind.Scared && inhale ? r.I(1, 4) : 0;
		var stutterAt = Enumerable.Range(0, stutters).Select(_ => r.R(0.2, 0.75)).OrderBy(v => v).ToArray();

		void Render(int start, double len, double gain)
		{
			int m = (int)(len * sr);
			for (int i = 0; i < m && start + i < n; i++)
			{
				double t = (double)(start + i) / sr, u = (double)i / m;
				double flow = Flow(u);
				if (flow <= 0) continue;
				foreach (var s in stutterAt) flow *= 1 - 0.85 * Math.Exp(-Math.Pow((u - s) / 0.035, 2));
				if (tremorDepth > 0)
				{
					trPh += 2 * Math.PI * (6 + 5 * tremorRate.At(t)) / sr;
					flow *= 1 - tremorDepth * (0.5 + 0.5 * Math.Sin(trPh));
				}
				// turbulence: louder and brighter as the air moves harder
				double w = r.W(), p = pink.P(w);
				bright.SetLp(700 + 3200 * flow);
				double src = bright.P(p) * 0.65 + p * 0.35;
				double v = 0;
				for (int k = 0; k < form.Length; k++)
				{
					var (f, bw, g) = tract[k];
					double fk = f * (1 + 0.06 * (drift[k].At(t) - 0.5)) * (inhale ? 1.04 : 1.0);
					form[k].SetBp(fk, fk / bw);
					v += form[k].P(src) * g;
				}
				if (kind == Kind.Nose) v -= notch.P(v) * 0.6;
				v += fricLp.P(fric.P(w)) * fricAmt * flow;
				v += chest.P(w) * chestAmt;
				v *= Math.Pow(flow, 1.3);
				if (voiceAmt > 0)
				{
					double vf = f0 * (1 + (kind == Kind.Scared ? 0.05 : 0.015) * (vib.At(t) - 0.5) * 2) * (1 - 0.08 * u);
					ph += 2 * Math.PI * vf / sr;
					double glot = 0;
					for (int h = 1; h <= 14; h++) if (vf * h < sr * 0.45) glot += Math.Sin(ph * h) / Math.Pow(h, 1.4);
					double vo = vForm[0].P(glot) + vForm[1].P(glot) * 0.6;
					double on = Math.Sin(Math.PI * Math.Min(1, Math.Max(0, (u - 0.04) / 0.7)));
					v += (vo * 0.7 + glot * 0.3) * voiceAmt * on * Math.Sqrt(flow);
				}
				x[start + i] += v * gain;
			}
		}

		Render(0, dur, 1);
		if (doubleGasp) Render((int)((dur + r.R(0.12, 0.2)) * sr), dur * 0.7, r.R(0.55, 0.8));

		// a lip or saliva click as the mouth opens (heavy and frightened breaths, now and then)
		if ((kind is Kind.Winded or Kind.Scared or Kind.Mouth) && r.Chance(kind == Kind.Mouth ? 0.15 : 0.35))
		{
			var cb = Biquad.Bp(sr, r.R(2200, 3800), 4);
			int c0 = (int)(r.R(0.0, 0.04) * sr);
			for (int i = 0; i < sr / 50 && c0 + i < n; i++) x[c0 + i] += cb.P(r.W()) * Perc((double)i / sr, 0.0004, 0.003) * r.R(0.25, 0.5);
		}

		return FinishOneShot(x, sr, -3, 60);
	}
}
