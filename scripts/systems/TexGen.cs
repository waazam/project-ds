namespace ProjectDS.Systems;

/// <summary>Time spent drawing textures pixel by pixel in code (the load overhaul, 2026-10-09: is it the load?).</summary>
public static class TexGen
{
	public static double Ms;
	public static int Count;
	public static ulong Start() => Godot.Time.GetTicksUsec();
	/// <summary>(2026-10-19) A draw over 40 ms names itself (its file and method), so the slow ones can be found and kept.</summary>
	public static void Stop(ulong t0, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerMemberName] string member = "")
	{
		double ms = (Godot.Time.GetTicksUsec() - t0) / 1000.0;
		Ms += ms; Count++;
		if (ms >= 40.0) Godot.GD.Print($"[texgen] {System.IO.Path.GetFileNameWithoutExtension(file)}.{member}: {ms:0} ms");
		LoadPump.Tick();
	}
}
