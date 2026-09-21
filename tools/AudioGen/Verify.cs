namespace ProjectDS.Tools.AudioGen;

using static Dsp;

/// <summary>Reads the generated WAVs back and checks format, length, levels, loop seams, end clicks and spectrum.</summary>
public static class Verify
{
	static readonly double[] Edges = { 0, 100, 1000, 3000, 8000, double.MaxValue };

	public static bool Run(List<(string path, bool loop, int sr)> files, string root)
	{
		bool allOk = true;
		Console.WriteLine();
		Console.WriteLine($"{"file",-20} {"sr",6} {"dur s",7} {"peak",7} {"rms",7}  {"seam/ends",-22} {"<100",5} {"-1k",5} {"-3k",5} {"-8k",5} {">8k",5} {"centroid",8} {"mod dB",6}  result");
		foreach (var (path, loop, expSr) in files)
		{
			string name = Path.GetFileNameWithoutExtension(path);
			var problems = new List<string>();
			if (!File.Exists(path)) { Console.WriteLine($"{name,-20} MISSING"); allOk = false; continue; }
			var w = Wav.Read(path);
			var x = w.Samples;
			if (w.Channels != 1 || w.Bits != 16) problems.Add("format");
			if (w.SampleRate != expSr) problems.Add("rate");
			double dur = x.Length / (double)w.SampleRate;
			var (lo, hi) = ExpectedDuration(name);
			if (dur < lo || dur > hi) problems.Add($"duration {lo}-{hi}");

			double pk = Db(Peak(x)), rms = Db(Rms(x));
			if (pk > -1.0) problems.Add("peak>-1");
			if (!loop && (pk < -3.3 || pk > -2.7)) problems.Add("one-shot peak");

			string seam;
			if (loop)
			{
				// Wrap-around step and curvature vs. the file's own sample-to-sample distribution.
				int n = x.Length;
				var d1 = new double[n - 1]; var d2 = new double[n - 2];
				for (int i = 0; i < n - 1; i++) d1[i] = Math.Abs(x[i + 1] - x[i]);
				for (int i = 0; i < n - 2; i++) d2[i] = Math.Abs(x[i + 2] - 2 * x[i + 1] + x[i]);
				double p1 = Pct(d1, 0.999), p2 = Pct(d2, 0.999), lsb = 2.0 / 32767;
				double s1 = Math.Abs(x[0] - x[n - 1]), s2 = Math.Abs(x[0] - 2 * x[n - 1] + x[n - 2]);
				bool ok = s1 <= Math.Max(p1, lsb) && s2 <= Math.Max(p2, lsb);
				// Level continuity: 100 ms either side of the seam.
				int win = w.SampleRate / 10;
				double ra = Db(Rms(x[^win..])), rb = Db(Rms(x[..win]));
				string lvl = ra < -90 || rb < -90 ? "near-silence" : $"lvl{rb - ra:+0.0;-0.0}dB";
				seam = $"{(ok ? "OK" : "BAD")} d={s1 / Math.Max(p1, lsb):0.00} {lvl}";
				if (!ok) problems.Add("seam");
			}
			else
			{
				double a = Db(Math.Abs(x[0]) + 1e-9), b = Db(Math.Abs(x[^1]) + 1e-9);
				bool ok = a < -60 && b < -60;
				seam = ok ? "ends at zero" : $"ENDS {a:0}/{b:0} dB";
				if (!ok) problems.Add("end click");
			}

			var (bands, centroid) = Spectrum(x, w.SampleRate);
			problems.AddRange(ContentChecks(name, bands, centroid));
			string detail = null;
			if (Detailed(name))
			{
				var m = Measure(x, w.SampleRate);
				problems.AddRange(TasteChecks(name, m, bands, centroid));
				detail = $"    crest {m.Crest:0.0} dB | 10ms floor(p10) {m.Floor:+0.0;-0.0} dB re RMS | 10ms power CV {m.Cv:0.00} | 50ms windows at >=0.9 pk {m.NearPeak:0} "
					+ $"| hiss gate (>1k p50 re p99) {m.HissGate:+0.0;-0.0} dB | 1s gaps (<-12 dB re RMS) {m.Gaps * 100:0}% | top-bin {m.TopBin * 100:0.0}% | tail {m.Tail:+0.0;-0.0} dB";
			}

			bool fileOk = problems.Count == 0;
			allOk &= fileOk;
			Console.WriteLine($"{name,-20} {w.SampleRate,6} {dur,7:0.000} {pk,7:0.0} {rms,7:0.0}  {seam,-22} "
				+ string.Join(" ", bands.Select(v => $"{v * 100,5:0.0}")) + $" {centroid,8:0} {ModRange(x, w.SampleRate),6:0.0}  {(fileOk ? "ok" : "FAIL: " + string.Join(", ", problems))}");
			if (detail != null) Console.WriteLine(detail);
		}
		Console.WriteLine(allOk ? "\nAll checks passed." : "\nSome checks FAILED.");
		return allOk;
	}

	static (double, double) ExpectedDuration(string name) => name switch
	{
		"rain_loop" => (150, 240),
		"choir_chant_loop" => (50, 90),
		_ when name.EndsWith("_loop") => (20, 40),
		_ when name.StartsWith("thunder") => (6, 18),
		_ when name.StartsWith("giant_step") => (1.5, 4),
		_ when name.StartsWith("bird") => (0.3, 2.5),
		_ when name.StartsWith("step_dirt") => (0.2, 0.36),
		_ when name.StartsWith("step_wood") => (0.2, 0.6),
		_ when name.StartsWith("cloth") => (0.25, 0.55),
		_ when name.StartsWith("twig_snap") => (0.25, 0.6),
		_ when name.StartsWith("branch_drop") => (1.2, 2.2),
		_ when name.StartsWith("trunk_creak") => (1.0, 2.5),
		_ when name.StartsWith("cricket_chirp") => (0.3, 0.8),
		_ when name.StartsWith("raven") => (0.8, 1.8),
		_ when name.StartsWith("rustle") => (0.4, 1.2),
		_ => (0, 1e9),
	};

	/// <summary>Sanity rules on where the energy should be.</summary>
	static IEnumerable<string> ContentChecks(string name, double[] b, double centroid)
	{
		if (name == "insects_loop" && b[3] + b[4] < 0.85) yield return "insects not mostly >3k";
		if (name == "pressure_loop" && b[0] < 0.9) yield return "pressure not mostly <100";
		if (name == "ringing_loop" && b[3] + b[4] < 0.95) yield return "ringing not high";
		if (name == "distant_loop" && b[0] + b[1] < 0.9) yield return "distant not low";
		if (name == "wind_loop" && b[0] + b[1] < 0.8) yield return "wind not low/mid";
		if (name == "leaves_loop" && b[2] + b[3] + b[4] < 0.6) yield return "leaves not high-band";
		if (name == "heartbeat_loop" && b[0] < 0.85) yield return "heartbeat not mostly <100";
		if (name.StartsWith("cricket_chirp") && b[3] + b[4] < 0.85) yield return "cricket not mostly >3k";
		if (name.StartsWith("trunk_creak") && (b[1] < 0.6 || centroid > 800)) yield return "creak not low/woody";
		if (name.StartsWith("raven") && (centroid > 1500 || b[4] > 0.01)) yield return "raven not dull/distant";
		if (name.StartsWith("twig_snap") && b[2] + b[3] < 0.4) yield return "twig not broadband";
		if (name.StartsWith("rustle") && b[2] + b[3] + b[4] < 0.5) yield return "rustle not crunchy";
	}

	/// <summary>Files that get the extra taste measurements (the reworked sounds and the new ones).</summary>
	static bool Detailed(string name) => name is "rain_loop" or "fire_crackle_loop" or "choir_chant_loop" or "stairs_hum_loop"
		|| name.StartsWith("thunder") || name.StartsWith("giant_step");

	public record Metrics(double Crest, double Floor, double Cv, double NearPeak, double HissGate, double Gaps, double TopBin, double Tail, double MaxAbs);

	/// <summary>
	/// Crest factor; the 10 ms level floor (p10 of 10 ms RMS windows re the overall RMS: a noise bed
	/// sits near 0 dB, sparse drops far below); the coefficient of variation of 10 ms power; the share
	/// of samples within 1 dB of the peak (overdrive and clipping pile samples up there); the "hiss
	/// gate" (median vs p99 of the >1 kHz band in 10 ms windows: a continuous hiss keeps the median
	/// close); the share of 1 s windows 12 dB under the RMS (silence between phrases); the largest
	/// single FFT bin's share of all energy (a steady drone concentrates there); and the level of the
	/// last quarter against the loudest second (natural decay).
	/// </summary>
	public static Metrics Measure(double[] x, int sr)
	{
		double rms = Db(Rms(x)), pk = Peak(x);
		int w10 = sr / 100;
		var lv = new List<double>(); var pw = new List<double>();
		for (int s = 0; s + w10 <= x.Length; s += w10) { double p = 0; for (int i = s; i < s + w10; i++) p += x[i] * x[i]; p /= w10; pw.Add(p); lv.Add(Db(Math.Sqrt(p))); }
		double floor = Pct(lv.ToArray(), 0.1) - rms;
		double mean = pw.Average(), sd = Math.Sqrt(pw.Sum(p => (p - mean) * (p - mean)) / pw.Count);
		int w50 = sr / 20; double near = 0;
		for (int s = 0; s + w50 <= x.Length; s += w50) { double lp = 0; for (int i = s; i < s + w50; i++) lp = Math.Max(lp, Math.Abs(x[i])); if (lp >= 0.9 * pk) near++; }
		var h1 = Biquad.Hp(sr, 1000); var h2 = Biquad.Hp(sr, 1000);
		var hx = x.Select(v => h2.P(h1.P(v))).ToArray();
		var hl = new List<double>();
		for (int s = 0; s + w10 <= hx.Length; s += w10) hl.Add(Db(Rms(hx[s..(s + w10)])));
		var ha = hl.ToArray();
		double gate = Pct(ha, 0.5) - Pct(ha, 0.99);
		int w1 = sr, gaps = 0, wins = 0; double loud = -200;
		for (int s = 0; s + w1 <= x.Length; s += w1 / 2) { double l = Db(Rms(x[s..(s + w1)])); wins++; if (l < rms - 12) gaps++; loud = Math.Max(loud, l); }
		double tail = Db(Rms(x[(x.Length * 3 / 4)..])) - loud;
		return new Metrics(Db(pk) - rms, floor, sd / Math.Max(mean, 1e-30), near, gate, wins > 0 ? (double)gaps / wins : 0, TopBin(x), tail, pk);
	}

	static double TopBin(double[] x)
	{
		int N = 4096;
		var pow = new double[N / 2]; var re = new double[N]; var im = new double[N];
		for (int s = 0; s + N <= x.Length; s += N / 2)
		{
			for (int i = 0; i < N; i++) { re[i] = x[s + i] * (0.5 - 0.5 * Math.Cos(TwoPi * i / (N - 1))); im[i] = 0; }
			Fft(re, im);
			for (int k = 1; k < N / 2; k++) pow[k] += re[k] * re[k] + im[k] * im[k];
		}
		double tot = pow.Sum();
		return tot > 0 ? pow.Max() / tot : 0;
	}

	/// <summary>The owner's taste, as numbers: no noise beds, no overdrive, no hiss, no drones.</summary>
	static IEnumerable<string> TasteChecks(string name, Metrics m, double[] b, double centroid)
	{
		if (name == "rain_loop")
		{
			if (m.Floor > -10) yield return "rain has a sustained floor";
			if (m.Cv < 2) yield return "rain level too steady";
			if (b[0] > 0.05 || b[4] > 0.1) yield return "rain rumble/fizz";
		}
		if (name.StartsWith("thunder"))
		{
			if (m.NearPeak > 4 || m.Crest < 12 || m.MaxAbs >= 0.999) yield return "thunder clipped/overdriven";
			if (b[0] + b[1] < 0.97 || centroid > 250) yield return "thunder not low";
			if (m.Tail > -15) yield return "thunder doesn't decay";
		}
		if (name == "fire_crackle_loop")
		{
			if (m.HissGate > -25) yield return "fire has a continuous hiss";
			if (m.Crest < 18) yield return "fire not poppy";
		}
		if (name == "choir_chant_loop")
		{
			if (b[2] < 0.03) yield return "choir has no vocal formants 1-3k";
			if (m.Gaps < 0.2) yield return "choir never stops (a drone)";
		}
		if (name == "stairs_hum_loop")
		{
			if (b[0] + b[1] < 0.99) yield return "hum not low";
			if (b[1] < 0.2) yield return "hum inaudible on small speakers";
			if (m.Crest > 12) yield return "hum not steady";
		}
		if (name.StartsWith("giant_step") && (b[0] < 0.6 || centroid > 150 || b[1] < 0.1 || b[2] + b[3] + b[4] > 0.02)) yield return "giant step not a low (but audible) thud";
	}

	/// <summary>Spread (p90 - p10, dB) of 250 ms RMS windows: how much the level moves (gusts, swells).</summary>
	static double ModRange(double[] x, int sr)
	{
		int win = sr / 4;
		var l = new List<double>();
		for (int s = 0; s + win <= x.Length; s += win) l.Add(Db(Rms(x[s..(s + win)])));
		if (l.Count < 4) return 0;
		var a = l.ToArray();
		return Pct(a, 0.9) - Pct(a, 0.1);
	}

	static double Pct(double[] v, double p)
	{
		var c = (double[])v.Clone();
		Array.Sort(c);
		return c[Math.Min(c.Length - 1, (int)(p * c.Length))];
	}

	/// <summary>Averaged Hann-windowed power spectrum -> energy fraction per band and spectral centroid.</summary>
	static (double[] bands, double centroid) Spectrum(double[] x, int sr)
	{
		int N = 4096;
		var pow = new double[N / 2];
		var re = new double[N]; var im = new double[N];
		int frames = 0;
		for (int s = 0; s + N <= Math.Max(x.Length, N); s += N / 2)
		{
			for (int i = 0; i < N; i++)
			{
				double v = s + i < x.Length ? x[s + i] : 0;
				re[i] = v * (0.5 - 0.5 * Math.Cos(TwoPi * i / (N - 1)));
				im[i] = 0;
			}
			Fft(re, im);
			for (int k = 0; k < N / 2; k++) pow[k] += re[k] * re[k] + im[k] * im[k];
			frames++;
		}
		var bands = new double[Edges.Length - 1];
		double tot = 0, cen = 0;
		for (int k = 1; k < N / 2; k++)
		{
			double f = (double)k * sr / N;
			tot += pow[k]; cen += pow[k] * f;
			for (int b = 0; b < bands.Length; b++) if (f >= Edges[b] && f < Edges[b + 1]) bands[b] += pow[k];
		}
		for (int b = 0; b < bands.Length; b++) bands[b] /= Math.Max(tot, 1e-30);
		return (bands, cen / Math.Max(tot, 1e-30));
	}

	static void Fft(double[] re, double[] im)
	{
		int n = re.Length;
		for (int i = 1, j = 0; i < n; i++)
		{
			int bit = n >> 1;
			for (; (j & bit) != 0; bit >>= 1) j ^= bit;
			j ^= bit;
			if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
		}
		for (int len = 2; len <= n; len <<= 1)
		{
			double ang = -TwoPi / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
			for (int i = 0; i < n; i += len)
			{
				double cr = 1, ci = 0;
				for (int k = 0; k < len / 2; k++)
				{
					int a = i + k, b = a + len / 2;
					double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
					re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
					double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
				}
			}
		}
	}
}

/// <summary>Per-phone spectral check of a dry chant take (used by --voice-test).</summary>
public static class PhoneCheck
{
	static readonly double[] Edges = { 0, 400, 1300, 2000, 3500, 1e9 };

	/// <summary>Energy share per band (&lt;400 / 400-1.3k / 1.3-2k / 2-3.5k / &gt;3.5k) in a 60 ms window centred at <paramref name="t"/>.</summary>
	public static double[] Bands(double[] x, int sr, double t)
	{
		int N = 2048, w = (int)(0.06 * sr), c = (int)(t * sr);
		var re = new double[N]; var im = new double[N];
		for (int i = 0; i < w; i++)
		{
			int k = c - w / 2 + i;
			re[i] = (k >= 0 && k < x.Length ? x[k] : 0) * (0.5 - 0.5 * Math.Cos(Dsp.TwoPi * i / (w - 1)));
		}
		Fft(re, im);
		var b = new double[Edges.Length - 1]; double tot = 0;
		for (int k = 1; k < N / 2; k++)
		{
			double f = (double)k * sr / N, p = re[k] * re[k] + im[k] * im[k];
			tot += p;
			for (int j = 0; j < b.Length; j++) if (f >= Edges[j] && f < Edges[j + 1]) b[j] += p;
		}
		for (int j = 0; j < b.Length; j++) b[j] /= Math.Max(tot, 1e-30);
		return b;
	}

	static void Fft(double[] re, double[] im)
	{
		int n = re.Length;
		for (int i = 1, j = 0; i < n; i++)
		{
			int bit = n >> 1;
			for (; (j & bit) != 0; bit >>= 1) j ^= bit;
			j ^= bit;
			if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
		}
		for (int len = 2; len <= n; len <<= 1)
		{
			double ang = -Dsp.TwoPi / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
			for (int i = 0; i < n; i += len)
			{
				double cr = 1, ci = 0;
				for (int k = 0; k < len / 2; k++)
				{
					int a = i + k, b = a + len / 2;
					double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
					re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
					double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
				}
			}
		}
	}
}
