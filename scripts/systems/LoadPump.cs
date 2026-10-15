using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Keeps the window answering while a level builds (2026-10-15, the Act 1 to 2 load: the Hollow's build held the main
/// thread 7.5 s on a warm texture cache and 22.5 s on a cold one, so Windows greyed the window out as "Not Responding").
/// The heavy builders tick it (every texture drawn, every mesh committed); while a load is on (<see cref="Active"/>, set
/// by <see cref="UI.LoadingVeil"/>), at most every <see cref="EveryMs"/> it lets the window manager have its messages,
/// dropping any input. Nothing is drawn: the screen keeps the veil's last frame.
/// </summary>
public static class LoadPump
{
	public static bool Active;
	public const ulong EveryMs = 200;
	private static ulong _last;
	public static int Pumps { get; private set; }
	/// <summary>The longest the window went unanswered while a load was on (ms), for the log and the tests.</summary>
	public static ulong MaxGapMs { get; private set; }

	public static void Begin() { Active = true; _last = Time.GetTicksMsec(); MaxGapMs = 0; Pumps = 0; }

	public static void End() { if (Active) Gap(Time.GetTicksMsec()); Active = false; }

	/// <summary>A frame went round (the main loop answered the window itself).</summary>
	public static void Frame() { if (!Active) return; ulong now = Time.GetTicksMsec(); Gap(now); _last = now; }

	private static void Gap(ulong now) { if (now - _last > MaxGapMs) MaxGapMs = now - _last; }

	public static void Tick()
	{
		if (!Active) return;
		ulong now = Time.GetTicksMsec();
		if (now - _last < EveryMs) return;
		Gap(now);
		_last = now;
		DisplayServer.ForceProcessAndDropEvents();
		Pumps++;
	}
}
