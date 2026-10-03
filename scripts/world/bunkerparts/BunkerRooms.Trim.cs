using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The room of doors, less of a box (the interiors pass, 2026-10-03, the owner: "make all the interiors feel more
/// natural and less boxy"): the walls painted to shoulder height in an institutional grey-green with a dark stripe at
/// its edge, a concrete plinth along their foot, pipes run along the top of the long walls on brackets, a vent
/// grille high on the far wall. All of it round the doors' frames, and the same every time, like the rest of it.
/// </summary>
public partial class BunkerRooms
{
	private void BuildRoomTrim()
	{
		float hw = RoomW * 0.5f;
		var k = new MeshKit();
		k.Mat(BunkerTextures.RoomWallMat);
		const float paintTop = 1.15f, frameHalf = 0.63f;
		// each wall's inner face: a start, its direction along, its inward normal, its length, and its door's centre along it
		var walls = new (Vector3 a, Vector3 along, Vector3 n, float len, float door)[]
		{
			(new Vector3(-hw, 0, 0), Vector3.Forward, Vector3.Right, RoomD, 3.5f),
			(new Vector3(hw, 0, -RoomD), Vector3.Back, Vector3.Left, RoomD, RoomD - 3.5f),
			(new Vector3(-hw, 0, -RoomD), Vector3.Right, Vector3.Back, RoomW, hw),
			(new Vector3(hw, 0, 0), Vector3.Left, Vector3.Forward, RoomW, hw),
		};
		foreach (var (a, along, n, len, door) in walls)
		{
			foreach (var (u0, u1) in new[] { (0f, door - frameHalf), (door + frameHalf, len) })
			{
				if (u1 - u0 < 0.05f) continue;
				Vector3 mid = a + along * ((u0 + u1) * 0.5f);
				var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized());
				float l = u1 - u0;
				// the paint (a skin a few millimetres proud), its stripe, and the plinth under it all
				k.Color = new Color(0.52f, 0.64f, 0.56f);
				k.Box(mid + n * 0.004f + Vector3.Up * (paintTop * 0.5f), new Vector3(l, paintTop, 0.006f), 1f, basis);
				k.Color = new Color(0.22f, 0.24f, 0.21f);
				k.Box(mid + n * 0.008f + Vector3.Up * (paintTop + 0.025f), new Vector3(l, 0.05f, 0.012f), 1f, basis);
				k.Color = new Color(0.42f, 0.42f, 0.4f);
				k.Box(mid + n * 0.02f + Vector3.Up * 0.082f, new Vector3(l, 0.16f, 0.04f), 1f, basis);
			}
		}
		k.CommitTo(this, "RoomTrim");

		// pipes along the top of the long walls (under the beams), on brackets, and a vent grille
		var m = new MeshKit();
		m.Mat(ProcTextures.MetalMat);
		foreach (float sx in new[] { -1f, 1f })
		{
			float x = sx * (hw - 0.14f), y = RoomH - 0.42f;
			m.Color = new Color(0.42f, 0.44f, 0.4f);
			m.Cylinder(new Vector3(x, y, -0.35f), new Vector3(x, y, -RoomD + 0.35f), 0.055f, 0.055f, 8, true);
			m.Cylinder(new Vector3(x + sx * 0.03f, y - 0.14f, -0.35f), new Vector3(x + sx * 0.03f, y - 0.14f, -RoomD + 0.35f), 0.03f, 0.03f, 6, true);
			m.Color = new Color(0.3f, 0.3f, 0.28f);
			for (float z = -0.8f; z > -RoomD + 0.5f; z -= 1.3f)
			{
				m.Box(new Vector3(sx * (hw - 0.06f), y - 0.06f, z), new Vector3(0.12f, 0.26f, 0.05f));
				m.Box(new Vector3(x, y, z), new Vector3(0.13f, 0.03f, 0.06f));
			}
		}
		// the grille: a frame and its slats, high on the far wall left of the door
		var g = new Vector3(-hw + 1.6f, RoomH - 0.55f, -RoomD + 0.03f);
		m.Color = new Color(0.36f, 0.37f, 0.34f);
		m.Box(g, new Vector3(0.62f, 0.38f, 0.04f));
		m.Color = new Color(0.08f, 0.08f, 0.08f);
		m.Box(g + Vector3.Back * 0.021f, new Vector3(0.54f, 0.3f, 0.002f));
		m.Color = new Color(0.32f, 0.33f, 0.3f);
		for (int i = 0; i < 6; i++)
			m.Box(g + new Vector3(0, -0.125f + i * 0.05f, 0.03f), new Vector3(0.54f, 0.012f, 0.03f), 1f, Basis.FromEuler(new Vector3(0.6f, 0, 0)));
		m.CommitTo(this, "RoomServices", false);
		WebKit.DressRoom(this, new RandomNumberGenerator { Seed = 7181 }, -hw, hw, -RoomD, 0f, 0f, RoomH, 0.8f, 0.7f);
		DebrisKit.Scatter(this, new RandomNumberGenerator { Seed = 7182 }, -hw + 0.05f, hw - 0.05f, -RoomD + 0.05f, -0.05f, 0f, new Color(0.5f, 0.5f, 0.47f), 60, 2,
			new[] { (Vector3.Zero, 1.1f), (new Vector3(-hw, 0, -3.5f), 1.1f), (new Vector3(hw, 0, -3.5f), 1.1f), (new Vector3(0, 0, -RoomD), 1.1f) });
	}
}
