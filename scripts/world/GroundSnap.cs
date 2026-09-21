using Godot;

namespace ProjectDS.World;

/// <summary>
/// Drop under any Node3D to sit it on the terrain (group "terrain", HeightAt)
/// when it enters play. Only Y changes unless AlignToSlope is set.
///
/// With a footprint, <see cref="Mode"/> picks which ground height under it wins:
/// Lowest (nothing floats: small props that can sink a little), Average (a
/// building on a slope that has its own foundation down to the low side),
/// Highest (nothing is buried) or Centre. Buildings (Cabin, Shed) ground
/// themselves with <see cref="SampleRect"/> instead and don't need this node.
/// </summary>
[GlobalClass]
public partial class GroundSnap : Node
{
	public enum SnapMode { Lowest, Average, Highest, Centre }

	/// <summary>Added to the ground height (negative sinks the object a little).</summary>
	[Export] public float Offset = 0f;
	/// <summary>Sample several points under the footprint (radius, metres) and combine them per <see cref="Mode"/>.</summary>
	[Export] public float FootprintRadius = 0f;
	[Export] public SnapMode Mode = SnapMode.Lowest;
	[Export] public bool AlignToSlope = false;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (GetParent() is Node3D target) Snap(target, Offset, FootprintRadius, AlignToSlope, Mode);
	}

	public static ForestTerrain FindTerrain(Node from)
		=> from.GetTree()?.GetFirstNodeInGroup("terrain") as ForestTerrain;

	public static void Snap(Node3D target, float offset = 0f, float footprint = 0f, bool align = false, SnapMode mode = SnapMode.Lowest)
	{
		var terrain = FindTerrain(target);
		if (terrain == null) return;
		Vector3 p = target.GlobalPosition;
		float h = terrain.HeightAt(p.X, p.Z);
		if (footprint > 0f && mode != SnapMode.Centre)
		{
			float lo = h, hi = h, sum = h;
			for (int i = 0; i < 8; i++)
			{
				float a = Mathf.Tau * i / 8f;
				float s = terrain.HeightAt(p.X + Mathf.Cos(a) * footprint, p.Z + Mathf.Sin(a) * footprint);
				lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); sum += s;
			}
			h = mode switch { SnapMode.Average => sum / 9f, SnapMode.Highest => hi, _ => lo };
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

	/// <summary>Ground statistics over a rectangle in a node's local XZ frame.</summary>
	public struct GroundStats
	{
		public float Min, Max, Avg;
	}

	/// <summary>
	/// Samples the terrain on an n x n grid over the local rectangle [min, max] (x, z) of
	/// <paramref name="xf"/> and returns heights in world Y. False if there is no terrain.
	/// </summary>
	public static bool SampleRect(ForestTerrain terrain, Transform3D xf, Vector2 min, Vector2 max, int n, out GroundStats stats)
	{
		stats = default;
		if (terrain == null) return false;
		float lo = float.MaxValue, hi = float.MinValue, sum = 0; int count = 0;
		for (int j = 0; j < n; j++)
			for (int i = 0; i < n; i++)
			{
				float u = n == 1 ? 0.5f : i / (float)(n - 1), v = n == 1 ? 0.5f : j / (float)(n - 1);
				Vector3 w = xf * new Vector3(Mathf.Lerp(min.X, max.X, u), 0, Mathf.Lerp(min.Y, max.Y, v));
				float h = terrain.HeightAt(w.X, w.Z);
				lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); sum += h; count++;
			}
		stats = new GroundStats { Min = lo, Max = hi, Avg = sum / count };
		return true;
	}
}
