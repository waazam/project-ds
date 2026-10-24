using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The forest's trunks as they're drawn (2026-10-23, the owner: squirrels "floating on the trees"): each scattered tree's
/// centre and its bark's radius at a height, measured from its mesh (the Blender firs' bark stands proud of the old
/// collision cylinder; things placed on a tree by its collider sank into one and hung off the next). Built once from the
/// forest's MultiMeshes when first asked, per level.
/// </summary>
public static class TreeTrunks
{
	public readonly record struct Trunk(Vector2 Centre, float Scale, float[] Radii, float Band);

	private static readonly Dictionary<Mesh, float[]> _radii = new();
	private static List<Trunk> _trunks;
	private static ulong _builtFor;
	private const float Band = 0.5f;
	private const int Bands = 16;

	/// <summary>Every trunk in the scene (empty until the forest is scattered).</summary>
	public static List<Trunk> All(Node from)
	{
		var root = from.GetTree()?.CurrentScene;
		if (root == null) return new List<Trunk>();
		if (_trunks != null && _builtFor == root.GetInstanceId() && _trunks.Count > 0) return _trunks;
		_builtFor = root.GetInstanceId();
		_trunks = new List<Trunk>();
		foreach (var inst in root.FindChildren("Instances", "Node3D", true, false))
			foreach (var child in inst.GetChildren())
			{
				if (child is not MultiMeshInstance3D mmi || mmi.Multimesh is not { Mesh: { } mesh } mm) continue;
				string n = mmi.Name.ToString();
				if (!(n.StartsWith("fir_") || n.StartsWith("decid") || n.StartsWith("snag"))) continue;
				var radii = RadiiOf(mesh);
				if (radii == null) continue;
				for (int i = 0; i < mm.InstanceCount; i++)
				{
					var t = mmi.GlobalTransform * mm.GetInstanceTransform(i);
					_trunks.Add(new Trunk(new Vector2(t.Origin.X, t.Origin.Z), t.Basis.X.Length(), radii, Band));
				}
			}
		return _trunks;
	}

	/// <summary>The nearest trunk to <paramref name="p"/> within <paramref name="within"/> m of its bark, or null.</summary>
	public static Trunk? Nearest(Node from, Vector2 p, float within)
	{
		Trunk? best = null;
		float bestD = float.MaxValue;
		foreach (var t in All(from))
		{
			float d = t.Centre.DistanceTo(p) - RadiusAt(t, 1f);
			if (d < within && d < bestD) { bestD = d; best = t; }
		}
		return best;
	}

	/// <summary>The bark's radius <paramref name="height"/> m up the trunk (scaled).</summary>
	public static float RadiusAt(Trunk t, float height)
	{
		float h = Mathf.Max(0f, height / t.Scale) / t.Band;
		int i = Mathf.Clamp((int)h, 0, t.Radii.Length - 1), j = Mathf.Min(i + 1, t.Radii.Length - 1);
		return Mathf.Lerp(t.Radii[i], t.Radii[j], Mathf.Clamp(h - i, 0f, 1f)) * t.Scale;
	}

	/// <summary>The mesh's bark radius in half-metre bands up from its foot: the 85th percentile of its vertices' distance
	/// from the axis among those near it (the branches' far ends left out).</summary>
	private static float[] RadiiOf(Mesh mesh)
	{
		if (_radii.TryGetValue(mesh, out var r)) return r;
		var lists = new List<float>[Bands];
		for (int b = 0; b < Bands; b++) lists[b] = new List<float>();
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var arr = mesh.SurfaceGetArrays(s);
			if (arr[(int)Mesh.ArrayType.Vertex].VariantType == Variant.Type.Nil) continue;
			foreach (var v in arr[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				int b = (int)(v.Y / Band);
				if (b < 0 || b >= Bands) continue;
				float d = new Vector2(v.X, v.Z).Length();
				if (d < 1.4f) lists[b].Add(d);
			}
		}
		r = new float[Bands];
		float last = 0.3f;
		for (int b = 0; b < Bands; b++)
		{
			var l = lists[b];
			if (l.Count < 6) { r[b] = last; continue; }
			l.Sort();
			r[b] = last = l[(int)(l.Count * 0.85f)];
		}
		_radii[mesh] = r;
		return r;
	}
}
