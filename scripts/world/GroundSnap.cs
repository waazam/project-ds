using Godot;

namespace ProjectDS.World;

/// <summary>
/// Drop under any Node3D to sit it on the terrain (group "terrain", HeightAt)
/// when it enters play. Only Y changes unless AlignToSlope is set.
/// </summary>
[GlobalClass]
public partial class GroundSnap : Node
{
	/// <summary>Added to the ground height (negative sinks the object a little).</summary>
	[Export] public float Offset = 0f;
	/// <summary>Sample several points under the footprint and use the lowest, so nothing floats.</summary>
	[Export] public float FootprintRadius = 0f;
	[Export] public bool AlignToSlope = false;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (GetParent() is Node3D target) Snap(target, Offset, FootprintRadius, AlignToSlope);
	}

	public static ForestTerrain FindTerrain(Node from)
		=> from.GetTree()?.GetFirstNodeInGroup("terrain") as ForestTerrain;

	public static void Snap(Node3D target, float offset = 0f, float footprint = 0f, bool align = false)
	{
		var terrain = FindTerrain(target);
		if (terrain == null) return;
		Vector3 p = target.GlobalPosition;
		float h = terrain.HeightAt(p.X, p.Z);
		if (footprint > 0f)
		{
			for (int i = 0; i < 8; i++)
			{
				float a = Mathf.Tau * i / 8f;
				h = Mathf.Min(h, terrain.HeightAt(p.X + Mathf.Cos(a) * footprint, p.Z + Mathf.Sin(a) * footprint));
			}
		}
		p.Y = h + offset;
		target.GlobalPosition = p;
		if (align)
		{
			Vector3 n = terrain.NormalAt(p.X, p.Z);
			Basis b = target.GlobalBasis.Orthonormalized();
			Vector3 fwd = -b.Z;
			Vector3 right = fwd.Cross(n).Normalized();
			fwd = n.Cross(right).Normalized();
			target.GlobalBasis = new Basis(right, n, -fwd).Scaled(target.Scale);
		}
	}
}
