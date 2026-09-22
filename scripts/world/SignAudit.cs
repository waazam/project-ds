using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness (scenes/levels/sign_audit.tscn): loads the trailhead world and reports every sign,
/// post and board whose footprint reaches into the trail, a branch path or the access road (and the
/// parking pad's driving lane). Footprints are the meshes' world AABBs, sampled on a grid. Writes
/// test-output/signs/report.txt and quits (1 if anything blocks).
/// </summary>
public partial class SignAudit : Node3D
{
	[Export] public string WorldScene = "res://scenes/levels/forest_world.tscn";
	/// <summary>Extra clearance wanted beyond the path's edge.</summary>
	[Export] public float Margin = 0.35f;

	public override void _Ready() => _ = Run();

	private async Task Run()
	{
		var world = GD.Load<PackedScene>(WorldScene).Instantiate<Node3D>();
		AddChild(world);
		for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var sb = new StringBuilder();
		int blocking = 0;
		var items = new List<Node3D>();
		void Walk(Node n)
		{
			bool sign = n is SignPost || n is TrailMarkerSet
				|| (n is ParkProp pp && pp.Kind is ParkProp.PropKind.TrailheadSign or ParkProp.PropKind.InfoBoard or ParkProp.PropKind.TrailRegister or ParkProp.PropKind.TrailMarker or ParkProp.PropKind.TrashCan or ParkProp.PropKind.VaultToilet or ParkProp.PropKind.ParkingBumper or ParkProp.PropKind.Car)
				|| n.Name.ToString().Contains("Sign") || n.Name.ToString().Contains("Post") || n.Name.ToString().Contains("Register") || n.Name.ToString().Contains("Board");
			if (sign && n is Node3D n3) items.Add(n3);
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(world);
		foreach (var it in items)
		{
			// Each mesh (or multimesh instance) separately: a sign is a few posts and boards.
			var meshes = new List<(string, Aabb)>();
			foreach (var m in it.FindChildren("*", "MeshInstance3D", true, false))
				if (m is MeshInstance3D mi && mi.Mesh != null && mi.IsVisibleInTree()) meshes.Add((mi.Name, mi.GlobalTransform * mi.GetAabb()));
			if (it is MeshInstance3D self && self.Mesh != null) meshes.Add((self.Name, self.GlobalTransform * self.GetAabb()));
			float worstTrail = 999f, worstRoad = 999f, worstBranch = 999f; string worstMesh = "";
			Vector3 worstAt = default;
			foreach (var (name, box) in meshes)
			{
				if (box.Size.Y > 12f || box.Size.X > 12f || box.Size.Z > 12f) continue;   // not a sign (e.g. a whole building)
				// Only what stands at body height matters (0.1 .. 2.0 m above the ground under it).
				for (float fx = 0; fx <= 1.001f; fx += 0.25f)
					for (float fz = 0; fz <= 1.001f; fz += 0.25f)
					{
						float x = box.Position.X + box.Size.X * fx, z = box.Position.Z + box.Size.Z * fz;
						float g = terrain.HeightAt(x, z);
						if (box.End.Y < g + 0.1f || box.Position.Y > g + 2.0f) continue;
						terrain.SampleFields(x, z, out float dT, out float sT, out _, out float dR);
						float trailEdge = dT - terrain.TrailHalfWidth(sT);
						float roadEdge = dR - terrain.RoadWidth * 0.5f;
						float br = terrain.SampleBranch(x, z, out float bHalf);
						float branchEdge = bHalf > 0.1f ? br - bHalf : 999f;
						if (trailEdge < worstTrail) { worstTrail = trailEdge; worstMesh = name; worstAt = new Vector3(x, g, z); }
						worstRoad = Mathf.Min(worstRoad, roadEdge);
						worstBranch = Mathf.Min(worstBranch, branchEdge);
					}
			}
			bool blocks = worstTrail < Margin || worstRoad < Margin || worstBranch < Margin;
			if (blocks) blocking++;
			sb.AppendLine($"{(blocks ? "BLOCKS" : "ok    ")}  {it.GetPath()}  trail edge {worstTrail:0.00} m  road edge {worstRoad:0.00} m  branch edge {worstBranch:0.00} m  (worst mesh '{worstMesh}' at {worstAt})");
		}
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-output/signs"));
		using (var f = FileAccess.Open("res://test-output/signs/report.txt", FileAccess.ModeFlags.Write)) f.StoreString(sb.ToString());
		GD.Print(sb.ToString());
		GD.Print($"[signs] {blocking} blocking");
		GetTree().Quit(blocking > 0 ? 1 : 0);
	}
}
