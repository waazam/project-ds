using System.Globalization;
using System.Numerics;
using System.Text;

// Project DS audio audit (2026-10-13, the "bouncy / circus" sweep): scores every .wav/.ogg for how tonal it is and
// whether its pitch glides, so synthesized Foley that reads as a cartoon "boing" can be found and ranked.
//
//   dotnet run --project tools/AudioAudit -- [--dir assets/audio] [--only frag,frag] [--csv out.csv] [--top 60]
//                                            [--status gitdiff.txt]
//
// Per file (active frames only: within 35 dB of the loudest frame):
//   flat   spectral flatness 60 Hz..8 kHz, energy-weighted (1 = white noise, 0 = a pure tone)
//   prom   the strongest spectral peak's height over its own neighbourhood (+-1/3 octave), dB, energy-weighted
//   tonal  the share of the sound's energy in frames with a clear pitched peak (prom > 15 dB)
//   glide  the largest pitch movement of one continuous tracked peak, in semitones (a "boing" is a falling tone)
//   rate   that glide's speed, semitones per second
//   f0     the tracked peak's median frequency
//   cent   spectral centroid (Hz); low: the share of energy under 200 Hz (a boomy, "bouncing ball" thud is mostly low)
//   toy    tonal x (prom/30) x (1 + glide/3) x 100: the ranking (a pitched blip that slides scores highest)
// --status takes `git diff --name-status <old> HEAD -- assets/audio` output and tags rows A (new) / M (changed).

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string root = FindRoot();
string dir = Arg("--dir") ?? Path.Combine(root, "assets", "audio");
if (!Path.IsPathRooted(dir)) dir = Path.GetFullPath(Path.Combine(root, dir));
string[] only = Arg("--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
string csv = Arg("--csv");
int top = int.TryParse(Arg("--top"), out int tp) ? tp : 60;
var status = new Dictionary<string, string>();
if (Arg("--status") is { } sf)
	foreach (var line in File.ReadAllLines(sf))
	{
		var parts = line.Split('\t');
		if (parts.Length >= 2) status[Path.GetFileName(parts[^1])] = parts[0][..1];
	}

var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
	.Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
	.Where(f => only == null || only.Any(o => Path.GetFileName(f).Contains(o)))
	.OrderBy(f => f).ToList();

var rows = new List<Row>();
foreach (var f in files)
{
	try
	{
		var (x, sr) = Load(f);
		var r = Analyse(x, sr);
		r.Path = Path.GetRelativePath(dir, f).Replace('\\', '/');
		r.Status = status.TryGetValue(Path.GetFileName(f), out var s) ? s : "";
		rows.Add(r);
	}
	catch (Exception e) { Console.Error.WriteLine($"  skip {f}: {e.Message}"); }
}

if (csv != null)
{
	var sb = new StringBuilder("path,status,dur,peak_db,rms_db,flat,prom,tonal,glide,rate,f0,toy,cent,low,ring\n");
	foreach (var r in rows)
		sb.AppendLine($"{r.Path},{r.Status},{r.Dur:0.00},{r.PeakDb:0.0},{r.RmsDb:0.0},{r.Flat:0.000},{r.Prom:0.0},{r.Tonal:0.00},{r.Glide:0.0},{r.Rate:0.0},{r.F0:0},{r.Toy:0.0},{r.Cent:0},{r.Low:0.00},{r.Ring:0.0}");
	File.WriteAllText(csv, sb.ToString());
	Console.WriteLine($"wrote {csv} ({rows.Count} files)");
}

// --compare old.csv: the files whose measurements moved, old -> new (a before/after for a rebuild)
if (Arg("--compare") is { } cmpPath)
{
	var old = File.ReadAllLines(cmpPath).Skip(1).Select(l => l.Split(',')).Where(p => p.Length >= 14)
		.ToDictionary(p => p[0], p => (toy: double.Parse(p[11]), tonal: double.Parse(p[7]), glide: double.Parse(p[8]), cent: double.Parse(p[12]), low: double.Parse(p[13]), rms: double.Parse(p[4]), ring: p.Length > 14 ? double.Parse(p[14]) : 0));
	Console.WriteLine($"{"file",-40} {"toy",15} {"tonal",11} {"glide",11} {"cent",13} {"low",11} {"ring",13}");
	foreach (var r in rows.OrderBy(r => r.Path))
	{
		if (!old.TryGetValue(r.Path, out var o)) { Console.WriteLine($"{Trunc(r.Path, 40),-40} (new) toy {r.Toy:0.0} tonal {r.Tonal:0.00} glide {r.Glide:0.0} cent {r.Cent:0} low {r.Low:0.00} ring {r.Ring:0.0}"); continue; }
		if (Math.Abs(o.rms - r.RmsDb) < 0.05 && Math.Abs(o.cent - r.Cent) < 1) continue;
		Console.WriteLine($"{Trunc(r.Path, 40),-40} {o.toy,6:0.0} -> {r.Toy,5:0.0} {o.tonal,4:0.00}->{r.Tonal,4:0.00} {o.glide,4:0.0}->{r.Glide,4:0.0} {o.cent,5:0}->{r.Cent,5:0} {o.low,4:0.00}->{r.Low,4:0.00} {o.ring,5:0.0}->{r.Ring,5:0.0}");
	}
	return 0;
}

Console.WriteLine($"{"file",-58} {"st",2} {"dur",5} {"flat",5} {"prom",5} {"tonal",5} {"glide",5} {"rate",6} {"f0",6} {"toy",6} {"cent",6} {"low",5} {"ring",5}");
foreach (var r in rows.OrderByDescending(r => r.Toy).Take(top))
	Console.WriteLine($"{Trunc(r.Path, 58),-58} {r.Status,2} {r.Dur,5:0.0} {r.Flat,5:0.00} {r.Prom,5:0.0} {r.Tonal,5:0.00} {r.Glide,5:0.0} {r.Rate,6:0.0} {r.F0,6:0} {r.Toy,6:0.0} {r.Cent,6:0} {r.Low,5:0.00} {r.Ring,5:0.0}");
return 0;

string Arg(string name)
{
	int i = Array.IndexOf(args, name);
	return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Trunc(string s, int n) => s.Length <= n ? s : "..." + s[^(n - 3)..];

static Row Analyse(double[] x, int sr)
{
	var r = new Row { Dur = x.Length / (double)sr };
	double peak = x.Length == 0 ? 0 : x.Max(Math.Abs), ss = 0;
	foreach (var v in x) ss += v * v;
	r.PeakDb = 20 * Math.Log10(peak + 1e-12);
	r.RmsDb = 10 * Math.Log10(ss / Math.Max(1, x.Length) + 1e-24);
	r.Ring = Ring(x, sr);

	int n = sr >= 40000 ? 4096 : 2048, hop = n / 4;
	var win = new double[n];
	for (int i = 0; i < n; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
	double binHz = sr / (double)n;
	int lo = (int)Math.Ceiling(60 / binHz), hi = (int)Math.Min(n / 2 - 2, 8000 / binHz), pitchHi = (int)Math.Min(hi, 4000 / binHz);

	var frames = new List<(double e, double flat, double prom, double f)>();
	var buf = new Complex[n];
	var p = new double[n / 2];
	// pad short sounds so they get at least one frame
	int count = Math.Max(1, (x.Length - n) / hop + 1);
	double centSum = 0, lowSum = 0, allSum = 0;
	for (int fi = 0; fi < count; fi++)
	{
		int o = fi * hop;
		for (int i = 0; i < n; i++) buf[i] = new Complex(o + i < x.Length ? x[o + i] * win[i] : 0, 0);
		Fft(buf);
		double e = 0, logSum = 0;
		for (int k = 0; k < n / 2; k++) p[k] = buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary;
		for (int k = lo; k <= hi; k++) { e += p[k]; logSum += Math.Log(p[k] + 1e-20); }
		double all = 0, cw = 0, low = 0;
		for (int k = 1; k < n / 2; k++) { all += p[k]; cw += p[k] * k * binHz; if (k * binHz < 200) low += p[k]; }
		centSum += cw; lowSum += low; allSum += all;
		int nb = hi - lo + 1;
		double flat = Math.Exp(logSum / nb) / (e / nb + 1e-20);
		// the strongest local peaks in the pitch band, scored by their height over their neighbourhood
		double bestProm = 0, bestF = 0;
		var cands = new List<int>();
		for (int k = lo + 1; k < pitchHi; k++) if (p[k] > p[k - 1] && p[k] >= p[k + 1]) cands.Add(k);
		foreach (int k in cands.OrderByDescending(k => p[k]).Take(6))
		{
			int a = Math.Max(lo, (int)(k / 1.26)), b = Math.Min(hi, (int)(k * 1.26) + 1);
			var nbh = new List<double>();
			for (int j = a; j <= b; j++) if (Math.Abs(j - k) > 3) nbh.Add(p[j]);
			if (nbh.Count < 4) continue;
			nbh.Sort();
			double med = nbh[nbh.Count / 2];
			double prom = 10 * Math.Log10((p[k] + 1e-20) / (med + 1e-20));
			if (prom > bestProm)
			{
				bestProm = prom;
				// parabolic interpolation on the log spectrum
				double y0 = Math.Log(p[k - 1] + 1e-20), y1 = Math.Log(p[k] + 1e-20), y2 = Math.Log(p[k + 1] + 1e-20);
				double d = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2 + 1e-12);
				bestF = (k + Math.Clamp(d, -0.5, 0.5)) * binHz;
			}
		}
		frames.Add((e, flat, bestProm, bestF));
	}
	double maxE = frames.Max(f => f.e);
	if (maxE <= 0) return r;
	r.Cent = centSum / (allSum + 1e-20);
	r.Low = lowSum / (allSum + 1e-20);
	var act = frames.Select((f, i) => (f, i)).Where(t => t.f.e >= maxE * 3.16e-4).ToList();   // within 35 dB
	double eSum = act.Sum(t => t.f.e);
	r.Flat = act.Sum(t => t.f.e * t.f.flat) / eSum;
	r.Prom = act.Sum(t => t.f.e * Math.Min(t.f.prom, 50)) / eSum;
	r.Tonal = act.Where(t => t.f.prom > 15).Sum(t => t.f.e) / eSum;
	var tonalF = act.Where(t => t.f.prom > 15).Select(t => t.f.f).OrderBy(v => v).ToList();
	r.F0 = tonalF.Count > 0 ? tonalF[tonalF.Count / 2] : 0;

	// glides: runs of consecutive tonal frames whose peak moves less than 1.5 semitones a frame (one partial, tracked)
	double frameSec = hop / (double)sr;
	var activeIdx = new HashSet<int>(act.Select(t => t.i));
	int start = -1;
	var segF = new List<double>();
	for (int i = 0; i <= frames.Count; i++)
	{
		bool tonal = i < frames.Count && activeIdx.Contains(i) && frames[i].prom > 15 && frames[i].f > 0;
		bool cont = tonal && segF.Count > 0 && Math.Abs(12 * Math.Log2(frames[i].f / segF[^1])) < 1.5;
		if (tonal && (segF.Count == 0 || cont)) { if (segF.Count == 0) start = i; segF.Add(frames[i].f); continue; }
		if (segF.Count >= 3)
		{
			double st = 12 * Math.Log2(segF.Max() / segF.Min());
			// a glide must be a real slide, not vibrato: the net movement from start to end counts too
			double net = Math.Abs(12 * Math.Log2(segF[^1] / segF[0]));
			double g = Math.Min(st, net + 1);
			if (g > r.Glide) { r.Glide = g; r.Rate = g / ((segF.Count - 1) * frameSec); }
		}
		segF.Clear();
		if (tonal) { start = i; segF.Add(frames[i].f); }
	}
	r.Toy = r.Tonal * Math.Min(r.Prom, 40) / 30 * (1 + Math.Min(r.Glide, 12) / 3) * 100;
	return r;
}

/// <summary>A short-window look at the loudest moments (~23 ms frames): how far the strongest peak in 150 Hz-6 kHz stands
/// over its own +-1/2 octave, averaged over the 8 loudest frames. A broadband tap or scuff sits around 6-10 dB; a noise tap
/// through a narrow band-pass (a woodblock "tok", a glockenspiel "tink") stands 15-25 dB proud. The long frames above miss
/// these: the ring is over in 20-40 ms.</summary>
static double Ring(double[] x, int sr)
{
	int n = sr >= 40000 ? 1024 : 512, hop = n / 4;
	if (x.Length < n) x = x.Concat(new double[n - x.Length]).ToArray();
	double binHz = sr / (double)n;
	int lo = (int)Math.Ceiling(150 / binHz), hi = (int)Math.Min(n / 2 - 2, 6000 / binHz);
	var win = new double[n];
	for (int i = 0; i < n; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
	var buf = new Complex[n];
	var p = new double[n / 2];
	var res = new List<(double e, double prom)>();
	for (int o = 0; o + n <= x.Length; o += hop)
	{
		for (int i = 0; i < n; i++) buf[i] = new Complex(x[o + i] * win[i], 0);
		Fft(buf);
		double e = 0;
		for (int k = 0; k < n / 2; k++) { p[k] = buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary; if (k >= lo && k <= hi) e += p[k]; }
		double best = 0;
		for (int k = lo + 1; k < hi; k++)
		{
			if (!(p[k] > p[k - 1] && p[k] >= p[k + 1])) continue;
			int a = Math.Max(1, (int)(k / 1.414)), b = Math.Min(n / 2 - 1, (int)(k * 1.414) + 1);
			var nb = new List<double>();
			for (int j = a; j <= b; j++) if (Math.Abs(j - k) > 2) nb.Add(p[j]);
			if (nb.Count < 4) continue;
			nb.Sort();
			best = Math.Max(best, 10 * Math.Log10((p[k] + 1e-20) / (nb[nb.Count / 2] + 1e-20)));
		}
		res.Add((e, best));
	}
	var topF = res.OrderByDescending(t => t.e).Take(8).ToList();
	return topF.Count == 0 ? 0 : topF.Average(t => t.prom);
}

static void Fft(Complex[] a)
{
	int n = a.Length;
	for (int i = 1, j = 0; i < n; i++)
	{
		int bit = n >> 1;
		for (; (j & bit) != 0; bit >>= 1) j ^= bit;
		j ^= bit;
		if (i < j) (a[i], a[j]) = (a[j], a[i]);
	}
	for (int len = 2; len <= n; len <<= 1)
	{
		double ang = -2 * Math.PI / len;
		var wl = new Complex(Math.Cos(ang), Math.Sin(ang));
		for (int i = 0; i < n; i += len)
		{
			var w = Complex.One;
			for (int j = 0; j < len / 2; j++)
			{
				var u = a[i + j]; var v = a[i + j + len / 2] * w;
				a[i + j] = u + v; a[i + j + len / 2] = u - v;
				w *= wl;
			}
		}
	}
}

static (double[] x, int sr) Load(string path)
{
	if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
	{
		using var vr = new NVorbis.VorbisReader(path);
		int ch = vr.Channels;
		var all = new List<float>();
		var tmp = new float[4096 * ch];
		int got;
		while ((got = vr.ReadSamples(tmp, 0, tmp.Length)) > 0) all.AddRange(tmp.Take(got));
		var mono = new double[all.Count / ch];
		for (int i = 0; i < mono.Length; i++) { double s = 0; for (int c = 0; c < ch; c++) s += all[i * ch + c]; mono[i] = s / ch; }
		return (mono, vr.SampleRate);
	}
	using var br = new BinaryReader(File.OpenRead(path));
	if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") throw new InvalidDataException("not RIFF");
	br.ReadInt32();
	if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") throw new InvalidDataException("not WAVE");
	int sr = 0, chs = 1, bits = 16, fmt = 1;
	while (br.BaseStream.Position < br.BaseStream.Length - 8)
	{
		string id = Encoding.ASCII.GetString(br.ReadBytes(4));
		int len = br.ReadInt32();
		if (id == "fmt ")
		{
			fmt = br.ReadInt16(); chs = br.ReadInt16(); sr = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
			br.ReadBytes(len - 16);
			if (fmt == unchecked((short)0xFFFE)) fmt = bits == 32 ? 3 : 1;   // extensible: assume PCM / float by width
		}
		else if (id == "data")
		{
			var raw = br.ReadBytes(Math.Min(len, (int)(br.BaseStream.Length - br.BaseStream.Position)));
			int bps = bits / 8, frames = raw.Length / (bps * chs);
			var mono = new double[frames];
			for (int i = 0; i < frames; i++)
			{
				double s = 0;
				for (int c = 0; c < chs; c++)
				{
					int o = (i * chs + c) * bps;
					s += bits switch
					{
						8 => (raw[o] - 128) / 128.0,
						16 => BitConverter.ToInt16(raw, o) / 32768.0,
						24 => ((raw[o] | (raw[o + 1] << 8) | (raw[o + 2] << 16)) << 8 >> 8) / 8388608.0,
						32 when fmt == 3 => BitConverter.ToSingle(raw, o),
						32 => BitConverter.ToInt32(raw, o) / 2147483648.0,
						_ => 0,
					};
				}
				mono[i] = s / chs;
			}
			return (mono, sr);
		}
		else br.ReadBytes(len + (len & 1));
	}
	throw new InvalidDataException("no data chunk");
}

static string FindRoot()
{
	foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
	{
		var d = new DirectoryInfo(start);
		while (d != null)
		{
			if (File.Exists(Path.Combine(d.FullName, "project.godot"))) return d.FullName;
			d = d.Parent;
		}
	}
	return Directory.GetCurrentDirectory();
}

class Row
{
	public string Path, Status;
	public double Dur, PeakDb, RmsDb, Flat, Prom, Tonal, Glide, Rate, F0, Toy, Cent, Low, Ring;
}
