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
			var anchor = new TrailAnchor { Distance = Distances[i], Offset = side, YawDegrees = side > 0 ? -15f : 15f, HeightOffset = -0.05f };
			prop.AddChild(anchor);
			AddChild(prop);
		}
	}
}
