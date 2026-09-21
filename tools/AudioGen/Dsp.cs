namespace ProjectDS.Tools.AudioGen;

/// <summary>Deterministic xorshift64* RNG (System.Random's seeded output is not a contract we want to rely on).</summary>
public sealed class Rng
{
	ulong _s;
	public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; if (_s == 0) _s = 1; }
	public ulong Next() { _s ^= _s >> 12; _s ^= _s << 25; _s ^= _s >> 27; return _s * 0x2545F4914F6CDD1DUL; }
	/// <summary>Uniform [0,1).</summary>
	public double U() => (Next() >> 11) * (1.0 / 9007199254740992.0);
	public double R(double a, double b) => a + (b - a) * U();
	/// <summary>Uniform [-1,1).</summary>
	public double W() => U() * 2.0 - 1.0;
	public int I(int a, int bExclusive) => a + (int)(U() * (bExclusive - a));
	public bool Chance(double p) => U() < p;
	/// <summary>Log-uniform between a and b.</summary>
	public double LogR(double a, double b) => a * Math.Pow(b / a, U());
	public Rng Fork() => new Rng(Next());
}

/// <summary>RBJ cookbook biquad, transposed direct form II. Coefficients can be retuned per sample.</summary>
public sealed class Biquad
{
	double _b0, _b1, _b2, _a1, _a2, _z1, _z2;
	readonly double _sr;
	public Biquad(double sr) { _sr = sr; }

	public static Biquad Lp(double sr, double f, double q = 0.7071) { var b = new Biquad(sr); b.SetLp(f, q); return b; }
	public static Biquad Hp(double sr, double f, double q = 0.7071) { var b = new Biquad(sr); b.SetHp(f, q); return b; }
	public static Biquad Bp(double sr, double f, double q) { var b = new Biquad(sr); b.SetBp(f, q); return b; }

	void Clamp(ref double f) { f = Math.Clamp(f, 5.0, _sr * 0.45); }

	public void SetLp(double f, double q = 0.7071)
	{
		Clamp(ref f);
		double w = 2 * Math.PI * f / _sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
		_b0 = (1 - c) / 2 / a0; _b1 = (1 - c) / a0; _b2 = _b0; _a1 = -2 * c / a0; _a2 = (1 - al) / a0;
	}

	public void SetHp(double f, double q = 0.7071)
	{
		Clamp(ref f);
		double w = 2 * Math.PI * f / _sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
		_b0 = (1 + c) / 2 / a0; _b1 = -(1 + c) / a0; _b2 = _b0; _a1 = -2 * c / a0; _a2 = (1 - al) / a0;
	}

	/// <summary>Band-pass with 0 dB peak gain.</summary>
	public void SetBp(double f, double q)
	{
		Clamp(ref f);
		double w = 2 * Math.PI * f / _sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
		_b0 = al / a0; _b1 = 0; _b2 = -al / a0; _a1 = -2 * c / a0; _a2 = (1 - al) / a0;
	}

	public double P(double x)
	{
		double y = _b0 * x + _z1;
		_z1 = _b1 * x - _a1 * y + _z2;
		_z2 = _b2 * x - _a2 * y;
		return y;
	}
}

/// <summary>Paul Kellet's refined pink noise filter.</summary>
public sealed class Pink
{
	double b0, b1, b2, b3, b4, b5, b6;
	public double P(double w)
	{
		b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759;
		b2 = 0.96900 * b2 + w * 0.1538520; b3 = 0.86650 * b3 + w * 0.3104856;
		b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
		double o = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362;
		b6 = w * 0.115926;
		return o * 0.11;
	}
}

/// <summary>Leaky-integrated white noise (brown / red noise).</summary>
public sealed class Brown
{
	double _y;
	public double P(double w) { _y = (_y + 0.02 * w) / 1.002; return _y * 3.5; }
}

/// <summary>Slow random control signal: random points every <c>step</c> seconds, cosine-interpolated. Output 0..1.</summary>
public sealed class Smooth
{
	readonly double[] _pts;
	readonly double _step;
	public Smooth(Rng rng, double seconds, double step)
	{
		_step = step;
		_pts = new double[(int)(seconds / step) + 3];
		for (int i = 0; i < _pts.Length; i++) _pts[i] = rng.U();
	}
	public double At(double t)
	{
		double p = Math.Max(0, t / _step);
		int i = Math.Min((int)p, _pts.Length - 2);
		double f = p - i, w = (1 - Math.Cos(Math.PI * f)) * 0.5;
		return _pts[i] * (1 - w) + _pts[i + 1] * w;
	}
}

/// <summary>Freeverb-style mono reverb (8 damped combs + 4 allpasses), used for a little outdoor air.</summary>
public sealed class Reverb
{
	readonly double[][] _comb, _ap;
	readonly int[] _ci, _ai;
	readonly double[] _cf;
	readonly double _fb, _damp;
	public Reverb(double sr, double size = 0.8, double damp = 0.4)
	{
		int[] cl = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
		int[] al = { 556, 441, 341, 225 };
		double s = sr / 44100.0;
		_comb = cl.Select(l => new double[Math.Max(1, (int)(l * s))]).ToArray();
		_ap = al.Select(l => new double[Math.Max(1, (int)(l * s))]).ToArray();
		_ci = new int[cl.Length]; _ai = new int[al.Length]; _cf = new double[cl.Length];
		_fb = 0.7 + 0.28 * size; _damp = damp;
	}
	public double P(double x)
	{
		double o = 0, inp = x * 0.015;
		for (int k = 0; k < _comb.Length; k++)
		{
			var b = _comb[k]; int i = _ci[k];
			double y = b[i];
			_cf[k] = y * (1 - _damp) + _cf[k] * _damp;
			b[i] = inp + _cf[k] * _fb;
			_ci[k] = (i + 1) % b.Length;
			o += y;
		}
		for (int k = 0; k < _ap.Length; k++)
		{
			var b = _ap[k]; int i = _ai[k];
			double bo = b[i];
			b[i] = o + bo * 0.5;
			o = bo - o;
			_ai[k] = (i + 1) % b.Length;
		}
		return o;
	}
}

/// <summary>
/// Soft hall: a short pre-delay, three diffusing allpasses, then an 8-line feedback delay network
/// (Householder mixing, one-pole damping per line) with its decay set as an RT60 in seconds.
/// Much larger and smoother than <see cref="Reverb"/>; used for the choir and the weather.
/// </summary>
public sealed class Hall
{
	readonly double[][] _d; readonly int[] _i; readonly double[] _g, _lp;
	readonly double[][] _ap; readonly int[] _ai;
	readonly double[] _pre; int _pi;
	readonly double _damp;
	public Hall(int sr, double rt60, double damp = 0.4, double preDelayMs = 30, double size = 1.0)
	{
		double[] ms = { 43.1, 51.7, 61.3, 68.9, 77.5, 86.3, 97.1, 107.9 };
		_d = ms.Select(m => new double[Math.Max(1, (int)(m * size * sr / 1000))]).ToArray();
		_i = new int[ms.Length]; _lp = new double[ms.Length];
		_g = _d.Select(b => Math.Pow(10, -3.0 * b.Length / sr / rt60)).ToArray();
		double[] apMs = { 4.7, 7.3, 11.3 };
		_ap = apMs.Select(m => new double[Math.Max(1, (int)(m * sr / 1000))]).ToArray();
		_ai = new int[apMs.Length];
		_pre = new double[Math.Max(1, (int)(preDelayMs * sr / 1000))];
		_damp = damp;
	}
	public double P(double x)
	{
		double v = _pre[_pi]; _pre[_pi] = x; _pi = (_pi + 1) % _pre.Length;
		for (int k = 0; k < _ap.Length; k++)
		{
			var b = _ap[k]; int i = _ai[k];
			double bo = b[i], w = v + bo * 0.6;
			b[i] = w; v = bo - 0.6 * w;
			_ai[k] = (i + 1) % b.Length;
		}
		int n = _d.Length;
		double sum = 0, o = 0;
		for (int k = 0; k < n; k++)
		{
			double y = _d[k][_i[k]];
			_lp[k] = y * (1 - _damp) + _lp[k] * _damp;
			sum += _lp[k];
			o += (k % 2 == 0 ? 1 : -1) * _lp[k];
		}
		double h = 2.0 / n * sum;
		for (int k = 0; k < n; k++)
		{
			_d[k][_i[k]] = v * 0.35 + _g[k] * (_lp[k] - h);
			_i[k] = (_i[k] + 1) % _d[k].Length;
		}
		return o * 0.5;
	}
}

public static class Dsp
{
	public const double TwoPi = Math.PI * 2;

	/// <summary>
	/// Runs a periodic signal through a stateful chain (filters, reverb) as if it had always been
	/// looping: <paramref name="laps"/>-1 warm-up laps, then the output of the last lap. The result
	/// loops seamlessly, reverb tails from the end included, as long as the tail is shorter than a lap.
	/// </summary>
	public static double[] Circular(double[] x, Func<double, double> chain, int laps = 2)
	{
		for (int l = 0; l < laps - 1; l++) foreach (var v in x) chain(v);
		var o = new double[x.Length];
		for (int i = 0; i < x.Length; i++) o[i] = chain(x[i]);
		return o;
	}

	/// <summary>Adds <paramref name="len"/> samples of <paramref name="gen"/>(i) at <paramref name="start"/>, wrapping around the buffer.</summary>
	public static void AddEvent(double[] buf, int start, int len, Func<int, double> gen)
	{
		int n = buf.Length;
		for (int i = 0; i < len; i++) buf[(((start + i) % n) + n) % n] += gen(i);
	}

	public static double Db(double lin) => 20 * Math.Log10(Math.Max(lin, 1e-12));
	public static double FromDb(double db) => Math.Pow(10, db / 20);

	/// <summary>Smooth 0..1..0 envelope over u in [0,1]: sin² attack of fraction a, sin² release of fraction r.</summary>
	public static double Env(double u, double a, double r)
	{
		if (u <= 0 || u >= 1) return 0;
		double g = 1;
		if (u < a) { double s = Math.Sin(0.5 * Math.PI * u / a); g *= s * s; }
		if (u > 1 - r) { double s = Math.Sin(0.5 * Math.PI * (1 - u) / r); g *= s * s; }
		return g;
	}

	/// <summary>Fast-attack exponential-decay envelope, t in seconds.</summary>
	public static double Perc(double t, double attack, double tau)
	{
		if (t < 0) return 0;
		if (t < attack) { double s = Math.Sin(0.5 * Math.PI * t / attack); return s * s; }
		return Math.Exp(-(t - attack) / tau);
	}

	/// <summary>
	/// Adds a tonal event. <paramref name="freq"/> and <paramref name="amp"/> take normalised time u in [0,1].
	/// Harmonics above 0.45·sr are dropped so nothing aliases.
	/// </summary>
	public static void AddTone(double[] buf, int sr, double start, double dur, Func<double, double> freq, Func<double, double> amp, double[] harm = null, bool wrap = false)
	{
		harm ??= new[] { 1.0 };
		int s0 = (int)Math.Round(start * sr), n = (int)(dur * sr);
		double ph = 0;
		for (int i = 0; i < n; i++)
		{
			double u = (double)i / n, f = freq(u), a = amp(u);
			ph += TwoPi * f / sr;
			if (a == 0) continue;
			double v = 0;
			for (int h = 0; h < harm.Length; h++)
			{
				if (harm[h] == 0 || f * (h + 1) > sr * 0.45) continue;
				v += harm[h] * Math.Sin(ph * (h + 1));
			}
			int idx = s0 + i;
			if (wrap) idx = ((idx % buf.Length) + buf.Length) % buf.Length;
			else if (idx < 0 || idx >= buf.Length) continue;
			buf[idx] += v * a;
		}
	}

	/// <summary>Adds an event buffer into a circular buffer (for perfectly periodic loops).</summary>
	public static void AddWrapped(double[] buf, double[] ev, int start, double gain = 1)
	{
		int n = buf.Length;
		for (int i = 0; i < ev.Length; i++) buf[(((start + i) % n) + n) % n] += ev[i] * gain;
	}

	/// <summary>
	/// Turns a linear render of length ≥ n+xf into a seamless loop of length n by equal-power
	/// crossfading the tail (x[n..n+xf)) into the head. out[0]=x[n] continues x[n-1] exactly.
	/// </summary>
	public static double[] MakeLoop(double[] x, int offset, int n, int xf)
	{
		var o = new double[n];
		for (int i = 0; i < n; i++)
		{
			double v = x[offset + i];
			if (i < xf)
			{
				double w = (i + 0.5) / xf;
				v = v * Math.Sin(0.5 * Math.PI * w) + x[offset + n + i] * Math.Cos(0.5 * Math.PI * w);
			}
			o[i] = v;
		}
		return o;
	}

	public static double Peak(double[] x) { double p = 0; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return p; }
	public static double Rms(double[] x) { double s = 0; foreach (var v in x) s += v * v; return Math.Sqrt(s / x.Length); }

	public static void Scale(double[] x, double g) { for (int i = 0; i < x.Length; i++) x[i] *= g; }

	public static void NormPeak(double[] x, double db) => Scale(x, FromDb(db) / Math.Max(Peak(x), 1e-12));

	/// <summary>Sets RMS to <paramref name="rmsDb"/>, then pulls it down if the peak would pass <paramref name="ceilDb"/>.</summary>
	public static void NormRms(double[] x, double rmsDb, double ceilDb = -1.5)
	{
		Scale(x, FromDb(rmsDb) / Math.Max(Rms(x), 1e-12));
		double pk = Peak(x);
		if (pk > FromDb(ceilDb)) Scale(x, FromDb(ceilDb) / pk);
	}

	public static void HighPass(double[] x, int sr, double f)
	{
		var h = Biquad.Hp(sr, f);
		for (int i = 0; i < x.Length; i++) x[i] = h.P(x[i]);
	}

	public static void LowPass(double[] x, int sr, double f, double q = 0.7071)
	{
		var h = Biquad.Lp(sr, f, q);
		for (int i = 0; i < x.Length; i++) x[i] = h.P(x[i]);
	}

	/// <summary>
	/// One-shot finishing: DC removal, trim trailing near-silence, short fade in/out so both ends sit at zero,
	/// and peak-normalise.
	/// </summary>
	public static double[] FinishOneShot(double[] x, int sr, double peakDb = -3, double fadeOutMs = 25, double minSec = 0, double maxSec = 0)
	{
		HighPass(x, sr, 25);
		double pk = Peak(x), thr = pk * FromDb(-62);
		int end = x.Length;
		int minEnd = Math.Max(sr / 20, Math.Min(x.Length, (int)(minSec * sr)));
		while (end > minEnd && Math.Abs(x[end - 1]) < thr) end--;
		end = Math.Min(x.Length, end + sr / 100);
		if (maxSec > 0) end = Math.Min(end, (int)(maxSec * sr));
		var o = x.Take(end).ToArray();
		int fi = Math.Max(1, sr / 2000), fo = Math.Min(o.Length / 3, (int)(fadeOutMs * sr / 1000));
		for (int i = 0; i < fi; i++) o[i] *= (double)i / fi;
		for (int i = 0; i < fo; i++) { double w = (double)i / fo; o[o.Length - 1 - i] *= w * w; }
		NormPeak(o, peakDb);
		return o;
	}
}
