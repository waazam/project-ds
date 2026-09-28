using Godot;

namespace ProjectDS.World;

/// <summary>
/// Places numbered trail marker posts along the trail. Distances and Labels
/// are parallel arrays so a designer can re-number, skip or reorder posts
/// (the out-of-sequence post is just a label here).
/// </summary>
[GlobalClass]
public partial class TrailMarkerSet : Node3D
{
	[Export] public float[] Distances = { 18f, 58f, 98f, 138f, 205f, 268f };
	[Export] public string[] Labels = { "1", "2", "3", "4", "5", "4" };
	/// <summary>Metres from the trail centre (+ = right-hand side walking in).</summary>
	[Export] public float SideOffset = 1.75f;

	public override void _Ready()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) return;
		for (int i = 0; i < Distances.Length; i++)
		{
			string label = i < Labels.Length ? Labels[i] : (i + 1).ToString();
			var prop = new ParkProp { Name = $"Marker_{i:00}_{label}", Kind = ParkProp.PropKind.TrailMarker, Label = label, ClearRadius = 1.2f };
			float side = (i % 4 == 3) ? -SideOffset : SideOffset; // most on the right, the odd one on the left
			side = ClearOffset(terrain, Distances[i], side);
			var anchor = new TrailAnchor { Distance = Distances[i], Offset = side, YawDegrees = side > 0 ? -15f : 15f, HeightOffset = -0.05f };
			prop.AddChild(anchor);
			AddChild(prop);
		}
	}

	/// <summary>Steps a marker out from the trail until nothing of it (a post and a board, ~0.45 m round) stands on the
	/// path: on a bend the fixed offset put two of them within 0.3 m of the tread (the sign audit).</summary>
	private static float ClearOffset(ForestTerrain terrain, float s, float side)
	{
		const float reach = 0.45f, wanted = 0.6f;
		Vector3 c = terrain.TrailPoint(s, out Vector3 t);
		Vector3 right = t.Cross(Vector3.Up).Normalized();
		for (float off = Mathf.Abs(side); off <= 4f; off += 0.25f)
		{
			Vector3 p = c + right * (off * Mathf.Sign(side));
			float worst = 999f;
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.Tau / 8f;
				float x = p.X + Mathf.Cos(a) * reach, z = p.Z + Mathf.Sin(a) * reach;
				float d = terrain.TrailDistance(x, z, out float along) - terrain.TrailHalfWidth(along);
				worst = Mathf.Min(worst, d);
			}
			if (worst >= wanted) return off * Mathf.Sign(side);
		}
		return 4f * Mathf.Sign(side);
	}
}
