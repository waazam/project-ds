namespace ProjectDS.Systems;

/// <summary>Time spent drawing textures pixel by pixel in code (the load overhaul, 2026-10-09: is it the load?).</summary>
public static class TexGen
{
	public static double Ms;
	public static int Count;
	public static ulong Start() => Godot.Time.GetTicksUsec();
	public static void Stop(ulong t0) { Ms += (Godot.Time.GetTicksUsec() - t0) / 1000.0; Count++; }
}
