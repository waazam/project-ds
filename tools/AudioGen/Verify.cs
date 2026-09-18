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

			bool fileOk = problems.Count == 0;
			allOk &= fileOk;
			Console.WriteLine($"{name,-20} {w.SampleRate,6} {dur,7:0.000} {pk,7:0.0} {rms,7:0.0}  {seam,-22} "
				+ string.Join(" ", bands.Select(v => $"{v * 100,5:0.0}")) + $" {centroid,8:0} {ModRange(x, w.SampleRate),6:0.0}  {(fileOk ? "ok" : "FAIL: " + string.Join(", ", problems))}");
		}
		Console.WriteLine(allOk ? "\nAll checks passed." : "\nSome checks FAILED.");
		return allOk;
	}

	static (double, double) ExpectedDuration(string name) => name switch
	{
		_ when name.EndsWith("_loop") => (20, 40),
		_ when name.StartsWith("bird") => (0.3, 2.5),
		_ when name.StartsWith("step_dirt") => (0.2, 0.36),
		_ when name.StartsWith("step_wood") => (0.2, 0.6),
		_ when name.StartsWith("cloth") => (0.25, 0.55),
		_ => (0, 1e9),
	};

	/// <summary>Sanity rules on where the energy should be.</summary>
	static IEnumerable<string> ContentChecks(string name, double[] b, double centroid)
	{
		if (name == "insects_loop" && b[3] + b[4] < 0.85) yield return "insects not mostly >3k";
		if (name == "drone_loop" && b[0] < 0.85) yield return "drone not mostly <100";
		if (name == "ringing_loop" && b[3] + b[4] < 0.95) yield return "ringing not high";
		if (name == "distant_loop" && b[0] + b[1] < 0.9) yield return "distant not low";
		if (name == "wind_loop" && b[0] + b[1] < 0.8) yield return "wind not low/mid";
		if (name == "leaves_loop" && b[2] + b[3] + b[4] < 0.6) yield return "leaves not high-band";
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
