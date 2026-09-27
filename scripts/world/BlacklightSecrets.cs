using System.Threading.Tasks;
using Godot;
using ProjectDS.World.BunkerParts;
using ProjectDS.World.LakeParts;

namespace ProjectDS.World;

/// <summary>
/// The blacklight's secrets across the game (the owner: easter eggs and clues everywhere, so the
/// player sees interesting things with the blacklight). Written in the ink only the lantern's second
/// light shows (<see cref="UvInk"/>), each placed relative to its own place once that place is built:
/// <list type="bullet">
/// <item>the camp: "HE COUNTS THE STEPS" by the fire, a hand on the stump;</item>
/// <item>the cabin: "IT KNOCKS THREE TIMES" on the back wall (it does, later), days tallied on the
/// side wall, small hands low on the inside of the door;</item>
/// <item>the bunker: "IT'S ON THE CEILING" in the hall, a hand dragged along the wall, "THE STAIRS
/// WANT YOU BACK" in the CRT room, "YOU'VE BEEN HERE BEFORE" in the rooms that repeat;</item>
/// <item>the lake: "ROW. DON'T STOP." on the end of the dock;</item>
/// <item>the station: "IT WAS ALWAYS STAIRS" behind the desk; <b>the cryptex's word, S T A I R S,
/// faint on Room 2's ceiling</b> (a clue); hands all over Room 1's ceiling; "IT MOVES WHEN YOU LOOK
/// AWAY" by the basement's drain;</item>
/// <item>the long hallway: "DON'T MOVE IN THE RED" at its start, and near the end <b>"THE SWITCH IS BY
/// THE DOOR"</b> (a clue for the dark closet), with a hand on the wall where it is;</item>
/// <item>the sewer: "DOWN" by the hole; the pit: "TURN THE LIT ONES" on the catwalk; the library: a
/// "HERE" by the book that sticks out; the round room: "UP. ALWAYS UP." on the dais.</item>
/// </list>
/// (The stairwell writes its own, turn by turn: <c>Stairwell.Events.cs</c>.)
/// </summary>
public partial class BlacklightSecrets : Node
{
	public int Written { get; private set; }

	public override void _Ready() => _ = Place();

	private async Task Place()
	{
		// everything in the Hollow is built over the first frames (some of it deferred twice): wait for it
		for (int i = 0; i < 90 && !IsInsideTree(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		for (int i = 0; i < 240; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (StationInterior.Instance?.Boss?.Library?.Round?.Dais != null) break;
		}
		var scene = GetTree().CurrentScene;
		var terrain = GroundSnap.FindTerrain(this);
		Color C = UvInk.Cyan, G = UvInk.Green, P = UvInk.Pale;

		// the camp
		if (scene?.FindChild("Camp", true, false) is Camp camp)
		{
			Vector3 at = camp.StumpLocal + new Vector3(-0.2f, 0, 1.1f);
			if (terrain != null) at.Y = terrain.HeightAt(camp.ToGlobal(at).X, camp.ToGlobal(at).Z) - camp.GlobalPosition.Y + 0.02f;
			W(camp, at, Vector3.Up, "HE COUNTS THE STEPS", 1.6f, G, 0.4f);
			Mark(camp, camp.StumpLocal + new Vector3(0, 0.3f, camp.StumpRadius + 0.01f), Vector3.Back, UvInk.Hand(), 0.2f, P);
		}
		// the cabin
		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin)
		{
			float hd = cabin.Depth * 0.5f, hw = cabin.Width * 0.5f;
			W(cabin, new Vector3(0, 1.55f, -hd + 0.2f), Vector3.Back, "IT KNOCKS THREE TIMES", 1.9f, C);
			for (int i = 0; i < 4; i++) Mark(cabin, new Vector3(-hw + 0.2f, 1.1f + (i % 2) * 0.28f, -1.2f + i * 0.35f), Vector3.Right, UvInk.Tally(), 0.3f, P, 0.05f * i);
			Mark(cabin, new Vector3(-0.25f, 0.75f, hd - 0.12f), Vector3.Forward, UvInk.Hand(), 0.13f, G);
			Mark(cabin, new Vector3(0.2f, 0.9f, hd - 0.12f), Vector3.Forward, UvInk.Hand(), 0.12f, G, 0.4f);
		}
		// the bunker
		if (BunkerInterior.Instance is Node3D bi)
		{
			float x = BunkerLayout.HallHalfWidth - 0.2f;
			W(bi, new Vector3(-x, 1.6f, -30f), Vector3.Right, "IT'S ON THE CEILING", 1.6f, C);
			for (float z = -40f; z > -56f; z -= 1.4f) Mark(bi, new Vector3(x, 1.05f + 0.05f * Mathf.Sin(z), z), Vector3.Left, UvInk.Hand(), 0.2f, P, 0.3f);
			W(bi, new Vector3(0, 2.4f, BunkerLayout.CrtRoomBackZ + 0.08f), Vector3.Back, "THE STAIRS WANT YOU BACK", 3f, G);
			W(bi, BunkerLayout.MazeOffset + new Vector3(2.5f, 1.7f, -BunkerRooms.RoomD + 0.1f), Vector3.Back, "YOU'VE BEEN HERE BEFORE", 1.8f, C);
		}
		// the lake
		if (GetTree().GetFirstNodeInGroup("lake_marker") is Lake lake)
			W(lake, new Vector3(0, LakeShape.DockDeck + 0.02f, LakeShape.DockEndZ + 1.3f), Vector3.Up, "ROW. DON'T STOP.", 1.5f, C, Mathf.Pi);
		// the station
		if (StationInterior.Instance is { } st)
		{
			W(st, new Vector3(-3f, 2.1f, StationInterior.HalfDepth - 0.1f), Vector3.Forward, "IT WAS ALWAYS STAIRS", 2f, C);
			if (st.Room2 is { } r2) W(r2, new Vector3(0, StationRoom2.Height - 0.03f, 0.2f), Vector3.Down, "S  T  A  I  R  S", 1.6f, G, Mathf.Pi * 0.5f);
			if (st.Room1 is { } r1)
			{
				var rng = new RandomNumberGenerator { Seed = 1301 };
				for (int i = 0; i < 9; i++)
					Mark(r1, new Vector3(rng.RandfRange(-2.2f, 2.2f), StationRoom1.Height - 0.03f, rng.RandfRange(-2.2f, 2.2f)), Vector3.Down, UvInk.Hand(), 0.2f, P, rng.RandfRange(0f, Mathf.Tau));
			}
			if (st.Basement is { } b)
				W(b, new Vector3(StationBasement.Drain.X, StationBasement.Floor + 1.6f, StationBasement.MinZ + 0.18f), Vector3.Back, "IT MOVES WHEN YOU LOOK AWAY", 2.4f, C);
			// the long hallway and its closet
			if (st.Room3?.Stairs?.Hallway is { } hw)
			{
				W(hw, new Vector3(-Act15Hallway.W1 * 0.5f + 0.03f, 1.5f, 8f), Vector3.Right, "DON'T MOVE IN THE RED", 1.3f, G);
				W(hw, new Vector3(-Act15Hallway.W2 * 0.5f + 0.03f, 1.5f, Act15Hallway.End - 6f), Vector3.Right, "THE SWITCH IS BY THE DOOR", 1.5f, C);
				Mark(hw, new Vector3(Act15Hallway.DoorWidth * 0.5f + 0.25f, 1.55f, Act15Hallway.End + 0.2f + 0.04f), Vector3.Back, UvInk.Hand(), 0.2f, P);
			}
			if (st.Sewer is { } sw)
				W(sw, new Vector3(0, Sewer.PlatTop + 0.012f, Sewer.HoleLocal.Z - Sewer.HoleHalf - 0.8f), Vector3.Up, "DOWN", 1.3f, C, Mathf.Pi);
			if (st.Boss is { } boss)
			{
				W(boss, BossRoom.LandingLocal + new Vector3(0, 0.012f, 1.8f), Vector3.Up, "TURN THE LIT ONES", 1.8f, G, Mathf.Pi);
				if (boss.Library is { } lib)
				{
					W(lib, new Vector3(0.62f - 0.55f, 1.4f + 0.62f, Library.Depth - 0.4f), Vector3.Forward, "HERE", 0.5f, C);
					if (lib.Round?.Dais is { } dais) W(dais, new Vector3(0, RoundRoom.DaisTop + 0.012f, 0.9f), Vector3.Up, "UP. ALWAYS UP.", 1.5f, C, Mathf.Pi);
				}
			}
		}
		GD.Print($"[story] the blacklight's secrets: {Written} written round the Hollow");
	}

	private void W(Node3D parent, Vector3 local, Vector3 normal, string text, float width, Color ink, float spin = 0f)
	{
		UvInk.Write(parent, local, normal, text, width, ink, spin);
		Written++;
	}

	private void Mark(Node3D parent, Vector3 local, Vector3 normal, Texture2D mask, float size, Color ink, float spin = 0f)
	{
		UvInk.OnSurface(parent, local, normal, new Vector2(size, size), mask, ink, 1.4f, spin);
		Written++;
	}
}
