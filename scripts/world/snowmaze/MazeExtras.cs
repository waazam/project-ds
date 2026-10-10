using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// The maze's aids (2026-10-10):
/// <list type="bullet">
/// <item><b>The flares</b> (the owner's): green road flares stuck in the snow along the one right way through, far apart,
/// burning. Each one taken burns for four-fifths of the walk to the next (at a walking pace), so a steady walk gets there
/// just after it gutters out: the last stretch always in the dark.</item>
/// <item><b>The hollows</b>: at the maze's dead ends the snow has slumped from the roof into a heavy drift hanging low across
/// the tunnel, and behind it a hollow. Crouched, you get under it; in there it can't see you. It may come sniffing past
/// the mouth: hold your breath (V / up on the pad) and it moves on; breathe, and it hears you.</item>
/// </list>
/// </summary>
public static class MazeExtras
{
	public const float FlareSpacing = 27f;
	/// <summary>The hollows, in the cave's space: the inside (from the drift back to the tunnel's end), the mouth just
	/// outside the drift, and the way in (toward the end).</summary>
	public static readonly List<(Vector3 lintel, Vector3 mouth, Vector3 dir)> Hollows = new();
	public static readonly List<Flare> Flares = new();

	/// <summary>Whether a point (cave space) is inside one of the hollows.</summary>
	public static bool InHollow(Vector3 p, out Vector3 mouth)
	{
		foreach (var (lintel, m, dir) in Hollows)
		{
			var d = (p - lintel) with { Y = 0 };
			float along = d.Dot(dir), side = Mathf.Abs(d.Dot(new Vector3(-dir.Z, 0, dir.X)));
			if (along > 0.2f && along < 3.2f && side < 2.3f) { mouth = m; return true; }
		}
		mouth = default;
		return false;
	}

	/// <summary>A point the hunter would go to, kept out of the hollows (it stops at the mouth).</summary>
	public static Vector3 KeepOut(Vector3 p) => InHollow(p, out var m) ? m : p;

	// ------------------------------------------------------------------ the flares

	public static void PlaceFlares(SnowMazeCave cave)
	{
		Flares.Clear();
		var L = cave.Layout;
		var pts = new List<Vector3> { cave.ArriveAt with { Y = 0 } };
		for (int i = 0; i < L.Solution.Count - 1; i++) pts.Add(L.Nodes[L.Solution[i]] with { Y = 0 });
		pts.Add(L.EndAt(L.Solution[^2], L.Cavern) with { Y = 0 });
		var cum = new List<float> { 0f };
		for (int i = 1; i < pts.Count; i++) cum.Add(cum[^1] + pts[i].DistanceTo(pts[i - 1]));
		float total = cum[^1];
		var at = new List<(Vector3 p, Vector3 dir, float s)>();
		for (float s = 7f; s < total - 6f; s += FlareSpacing)
		{
			int seg = 1;
			while (seg < cum.Count - 1 && cum[seg] < s) seg++;
			float u = (s - cum[seg - 1]) / Mathf.Max(cum[seg] - cum[seg - 1], 0.01f);
			var p = pts[seg - 1].Lerp(pts[seg], u);
			var dir = (pts[seg] - pts[seg - 1]).Normalized();
			at.Add((p, dir, s));
		}
		var space = cave.GetWorld3D().DirectSpaceState;
		for (int i = 0; i < at.Count; i++)
		{
			var (p, dir, s) = at[i];
			float next = i + 1 < at.Count ? at[i + 1].s : total;
			// over to the right-hand side, out of the way, on the floor
			var side = new Vector3(-dir.Z, 0, dir.X) * 1.15f;
			var g = cave.ToGlobal(p + side + Vector3.Up * 1.5f);
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(g, g + Vector3.Down * 4f, 1));
			var f = new Flare { Name = $"Flare{i}", BurnWhenTaken = Mathf.Max(6f, 0.8f * (next - s) / 2.7f) };
			cave.AddChild(f);
			f.GlobalPosition = hit.Count > 0 ? (Vector3)hit["position"] : cave.ToGlobal(p + side);
			Flares.Add(f);
		}
		GD.Print($"[story] Act 24: {Flares.Count} flares along the way ({total:0} m), {FlareSpacing:0} m apart");
	}

	// ------------------------------------------------------------------ the hollows

	public static void BuildHollows(SnowMazeCave cave)
	{
		Hollows.Clear();
		var L = cave.Layout;
		var k = new MeshKit();
		k.Mat(cave.IceMaterial);
		k.Color = Colors.White;
		var body = new StaticBody3D { Name = "Hollows", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		cave.AddChild(body);
		int n = 0;
		foreach (int v in L.DeadEnds)
		{
			if (L.Adj[v].Count != 1 || n >= 7) continue;
			int u = L.Adj[v][0];
			var dir = (L.Nodes[v] - L.Nodes[u]) with { Y = 0 };
			if (dir.LengthSquared() < 1f) continue;
			dir = dir.Normalized();
			var lintel = (L.Nodes[v] - dir * 0.6f) with { Y = 0 };
			var side = new Vector3(dir.Z, 0, -dir.X);   // (side, up, dir: a right-handed frame)
			// the drift: slumped from the roof, its underside a crouch high (1.25 m), lumpy and heavy
			k.Xf = new Transform3D(new Basis(side, Vector3.Up, dir), lintel);
			k.Blob(new Vector3(0, 2.35f, 0), new Vector3(2.5f, 1.15f, 0.75f), 2450 + v, 0.12f, false, 1f, 0f, 2);
			k.Blob(new Vector3(-1.2f, 1.9f, 0.15f), new Vector3(1.1f, 0.8f, 0.7f), 2470 + v, 0.15f, false, 1f, 0f, 2);
			k.Blob(new Vector3(1.3f, 2.0f, -0.1f), new Vector3(1.0f, 0.75f, 0.65f), 2490 + v, 0.15f, false, 1f, 0f, 2);
			// heaped snow either side of the way in, leaving the middle open
			k.Blob(new Vector3(-1.55f, 0.35f, -0.2f), new Vector3(0.7f, 0.6f, 0.8f), 2510 + v, 0.2f, false, 1f, 0f, 1);
			k.Blob(new Vector3(1.6f, 0.3f, -0.1f), new Vector3(0.65f, 0.55f, 0.75f), 2530 + v, 0.2f, false, 1f, 0f, 1);
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(4.6f, 2.2f, 0.9f) },
				Transform = new Transform3D(new Basis(side, Vector3.Up, dir), lintel + Vector3.Up * (1.27f + 1.1f)),
			});
			Hollows.Add((lintel, lintel - dir * 1.9f, dir));
			n++;
		}
		k.Xf = Transform3D.Identity;
		if (n > 0) k.CommitTo(cave, "HollowDrifts", true);
		GD.Print($"[story] Act 24: {n} hollows in the dead ends");
	}

	/// <summary>The player is in a hollow, crouched, and showing no flare: hidden from it.</summary>
	public static bool PlayerHidden(SnowMazeCave cave, PlayerController player)
	{
		if (player == null || cave == null || !player.Crouching || HeldFlare.AnyLit) return false;
		return InHollow(cave.ToLocal(player.GlobalPosition), out _);
	}
}
