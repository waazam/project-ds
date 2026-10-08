using Godot;

namespace ProjectDS.Systems;

/// <summary>How long a piece of the level took to build (logged when it's long: the load from Act 1 to the Hollow was
/// slow, 2026-10-07, and this says where the time goes). <c>using var _ = BuildTimer.Time("what");</c></summary>
public readonly struct BuildTimer : System.IDisposable
{
	private readonly string _what;
	private readonly ulong _t0;
	private BuildTimer(string what) { _what = what; _t0 = Godot.Time.GetTicksUsec(); }
	public static BuildTimer Time(string what) => new(what);
	public void Dispose()
	{
		double ms = (Godot.Time.GetTicksUsec() - _t0) / 1000.0;
		if (ms >= 80.0) GD.Print($"[build] {_what}: {ms:0} ms");
	}
}
