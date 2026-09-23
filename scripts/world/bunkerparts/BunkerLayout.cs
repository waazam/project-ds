using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Shared dimensions of the bunker interior. Everything is local to the
/// BunkerInterior node (which sits far from the outdoor terrain): the hallway
/// runs from Z 0 (entrance) to Z -90 (the vine door), the CRT room continues
/// to Z -122, and the maze lives off to the side at <see cref="MazeOffset"/>.
/// </summary>
public static class BunkerLayout
{
	// ---------------------------------------------------------------- hallway
	public const float HallHalfWidth = 2.2f;
	public const float HallKickHeight = 1.2f;      // vertical wall before the vault starts curving
	public const float HallHeight = HallKickHeight + HallHalfWidth;   // visual peak of the vault
	public const float HallCollisionRoof = 2.3f;   // flat collision ceiling, well under the visual peak
	public const float HallCollisionHalfWidth = 2.0f;   // inside the ribs and conduits
	public const int HallArchSegs = 12;
	public const float HallLength = 90f;
	public const float LightSpacing = 5f;          // fixtures at Z = -2.5, -7.5, ...
	public const float RibSpacing = 2.5f;          // ribs at Z = -1.25, -3.75, ... (fixtures sit mid-bay)
	public const float RedFlickerFrac = 0.4f;      // fraction of HallLength where the flicker warning starts
	public const float RedTriggerFrac = 0.75f;     // fraction where the lights commit to red for good
	public static readonly Vector3 HallSpawn = new(0, 0, -0.6f);

	// ---------------------------------------------------------------- the vine door (in the bulkhead at the hall's end)
	public const float DoorHalfWidth = 0.65f;
	public const float DoorHeight = 2.3f;
	public const float BulkheadThickness = 0.3f;   // Z -90 .. -90.3

	// ---------------------------------------------------------------- CRT room
	public const float CrtRoomFrontZ = -90f;
	public const float CrtRoomBackZ = -108f;   // 18 m deep (was 32: the walk from the door was too long, Dan 2026-09-22)
	public const float CrtRoomHalfWidth = 6f;
	public const float CrtRoomHeight = 4.0f;

	// ---------------------------------------------------------------- maze
	public static readonly Vector3 MazeOffset = new(300f, 0f, 0f);
	public const int MazeCols = 6, MazeRows = 10;
	public const float MazeCell = 4f;
	public const float MazeWallHeight = 2.7f;
	public const float MazeWallThickness = 0.2f;
	public const ulong MazeSeed = 4242;            // the story's fixed maze

	/// <summary>Point i (0..segs) of the vault's arch, left to right, in the XY plane.</summary>
	public static Vector2 ArchPoint(int i, int segs = HallArchSegs)
	{
		float a = Mathf.Pi * (1f - (float)i / segs);
		return new Vector2(Mathf.Cos(a) * HallHalfWidth, HallKickHeight + Mathf.Sin(a) * HallHalfWidth);
	}

	/// <summary>The hallway's cross-section as a closed outline: floor-left, up the kick wall, over the
	/// arch, down the right kick wall to floor-right (the floor edge closes it).</summary>
	public static Vector2[] HallProfile()
	{
		var p = new Vector2[HallArchSegs + 3];
		p[0] = new Vector2(-HallHalfWidth, 0f);
		for (int i = 0; i <= HallArchSegs; i++) p[i + 1] = ArchPoint(i);
		p[HallArchSegs + 2] = new Vector2(HallHalfWidth, 0f);
		return p;
	}

	/// <summary>Inward-facing normal of the hallway shell at profile point <paramref name="p"/>.</summary>
	public static Vector3 HallInward(Vector2 p)
	{
		if (p.Y <= HallKickHeight + 1e-3f) return new Vector3(-Mathf.Sign(p.X), 0, 0);
		var d = new Vector2(p.X, p.Y - HallKickHeight).Normalized();
		return new Vector3(-d.X, -d.Y, 0);
	}
}
