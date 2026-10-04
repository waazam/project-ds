using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Each place's own colour (2026-10-04, the owner: "bake in some other shaders to make everything stand out more"): the
/// finish's split-tone and saturation (ps2_post.gdshader) set by where the story is, eased from one to the next over a
/// few seconds, never a cut. Within the game's own grade (murky shadows, dirty warm highlights), each place leans its
/// own way:
/// <list type="bullet">
/// <item>the woods: as designed;</item>
/// <item>the bunker: sickly green;</item>
/// <item>the lake: a cold cyan dawn;</item>
/// <item>the station: dusty green-grey; the stairwell: cold grey-blue;</item>
/// <item>the sewer: sodium yellow-green; the pit: rust and blood;</item>
/// <item>the library and the round room: candle-warm;</item>
/// <item>the church: deep gold over cold stone; the winter woods: blue; the lodge: amber lamplight against blue snow light.</item>
/// </list>
/// (Act 15's hall has its own red grade, and keeps the woods' here.)
/// </summary>
public static class AreaGrade
{
	public readonly record struct Grade(Vector3 Shadow, Vector3 High, float Saturation);

	public static readonly Grade Woods = new(new(0.92f, 1.0f, 0.97f), new(1.04f, 1.0f, 0.92f), 0.95f);
	public static readonly Grade Bunker = new(new(0.88f, 1.0f, 0.9f), new(1.02f, 1.03f, 0.86f), 0.82f);
	public static readonly Grade Lake = new(new(0.86f, 0.98f, 1.04f), new(1.0f, 1.02f, 1.0f), 0.9f);
	public static readonly Grade Station = new(new(0.9f, 0.98f, 0.95f), new(1.03f, 1.0f, 0.9f), 0.85f);
	public static readonly Grade Stairwell = new(new(0.88f, 0.95f, 1.02f), new(1.0f, 1.0f, 0.98f), 0.8f);
	public static readonly Grade Sewer = new(new(0.9f, 0.98f, 0.88f), new(1.06f, 1.0f, 0.82f), 0.9f);
	public static readonly Grade Pit = new(new(0.98f, 0.92f, 0.92f), new(1.06f, 0.98f, 0.9f), 0.92f);
	public static readonly Grade Candle = new(new(0.96f, 0.95f, 0.9f), new(1.08f, 1.0f, 0.86f), 0.98f);
	public static readonly Grade Church = new(new(0.9f, 0.93f, 1.02f), new(1.08f, 0.99f, 0.86f), 0.95f);
	public static readonly Grade Winter = new(new(0.88f, 0.94f, 1.06f), new(0.99f, 1.0f, 1.02f), 0.85f);
	public static readonly Grade Lodge = new(new(0.88f, 0.94f, 1.06f), new(1.08f, 1.0f, 0.86f), 0.95f);

	/// <summary>The grade for where the story is (by its last save: each pocket of the world has its own).</summary>
	public static Grade For(Checkpoint cp) => cp switch
	{
		Checkpoint.Act8BunkerEntered => Bunker,
		Checkpoint.Act11GiantEncounter => Lake,
		>= Checkpoint.Act12LakeCrossed and < Checkpoint.Act13Finished => Station,
		Checkpoint.Act13Finished => Stairwell,
		Checkpoint.Act16Finished => Sewer,
		Checkpoint.Act17Finished => Pit,
		Checkpoint.Act18Finished or Checkpoint.Act19Finished or Checkpoint.Act20Finished => Candle,
		Checkpoint.Act21ChurchReached => Church,
		Checkpoint.Act21Finished => Winter,
		>= Checkpoint.Act22Finished => Lodge,
		_ => Woods,
	};

	private static Grade _now = Woods;
	private static bool _set;

	/// <summary>Eases the finish toward the grade for <paramref name="cp"/> (call every frame).</summary>
	public static void Apply(ShaderMaterial post, Checkpoint cp, float dt)
	{
		var want = For(cp);
		if (!_set) { _now = want; _set = true; }
		float k = 1f - Mathf.Exp(-dt / 2.5f);
		_now = new Grade(_now.Shadow.Lerp(want.Shadow, k), _now.High.Lerp(want.High, k), Mathf.Lerp(_now.Saturation, want.Saturation, k));
		post.SetShaderParameter("shadow_tone", _now.Shadow);
		post.SetShaderParameter("highlight_tone", _now.High);
		post.SetShaderParameter("saturation", _now.Saturation);
	}

	/// <summary>Straight to the grade (a load: no easing in from wherever the last level left it).</summary>
	public static void Snap() => _set = false;
}
