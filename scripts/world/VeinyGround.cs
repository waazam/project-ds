using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The Act 6 veiny-ground skin: a thin mesh that follows the terrain exactly
/// (built from <see cref="ForestTerrain.HeightAt"/> on the terrain's own 1 m grid
/// with the same triangle split, lifted a few centimetres in the shader), carrying
/// veiny_ground.gdshader. It never floats above or clips into slopes, and is lit
/// and fogged like the ground. Only cells inside the disc are emitted.
///
/// Usage (DeepZoneDressing): <c>AddChild(VeinyGround.Create(terrain, center, radius));</c>
/// The returned node is TopLevel and positioned in world space.
/// </summary>
public static class VeinyGround
{
	public static MeshInstance3D Create(ForestTerrain terrain, Vector3 center, float radius)
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/veiny_ground.gdshader") };
		mat.SetShaderParameter("center", new Vector2(center.X, center.Z));
		mat.SetShaderParameter("radius", radius);
		// Transparent veins must draw after the opaque ground they sit on.
		mat.RenderPriority = 1;

		float reach = radius * 1.18f;
		float cs = terrain?.CellSize ?? 1f;
		// align to the terrain's grid so our triangles coincide with its triangles
		Vector2 origin = terrain != null
			? new Vector2(terrain.GlobalPosition.X + terrain.MinXZ.X, terrain.GlobalPosition.Z + terrain.MinXZ.Y)
			: Vector2.Zero;
		int i0 = Mathf.FloorToInt((center.X - reach - origin.X) / cs), i1 = Mathf.CeilToInt((center.X + reach - origin.X) / cs);
		int j0 = Mathf.FloorToInt((center.Z - reach - origin.Y) / cs), j1 = Mathf.CeilToInt((center.Z + reach - origin.Y) / cs);
		int nx = i1 - i0 + 1;

		var verts = new List<Vector3>();
		var norms = new List<Vector3>();
		var idx = new List<int>();
		var map = new Dictionary<long, int>();
		Vector3 local0 = new(center.X, 0, center.Z);

		int V(int i, int j)
		{
			long key = (long)j * 100000 + i;
			if (map.TryGetValue(key, out int id)) return id;
			float x = origin.X + i * cs, z = origin.Y + j * cs;
			float y = terrain?.HeightAt(x, z) ?? center.Y;
			verts.Add(new Vector3(x, y, z) - local0);
			norms.Add(terrain?.NormalAt(x, z) ?? Vector3.Up);
			map[key] = verts.Count - 1;
			return verts.Count - 1;
		}

		for (int j = j0; j < j1; j++)
			for (int i = i0; i < i1; i++)
			{
				float cx = origin.X + (i + 0.5f) * cs - center.X, cz = origin.Y + (j + 0.5f) * cs - center.Z;
				if (cx * cx + cz * cz > reach * reach) continue;
				int a = V(i, j), b = V(i + 1, j), c = V(i, j + 1), d = V(i + 1, j + 1);
				// Same diagonal as ForestTerrain.HeightAt (fx >= fz: triangle 00-10-11; else 00-11-01),
				// wound clockwise seen from above (Godot's front face).
				idx.Add(a); idx.Add(b); idx.Add(d);
				idx.Add(a); idx.Add(d); idx.Add(c);
			}

		var arr = new Godot.Collections.Array();
		arr.Resize((int)Mesh.ArrayType.Max);
		arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arr[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
		var mesh = new ArrayMesh();
		if (idx.Count > 0)
		{
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			mesh.SurfaceSetMaterial(0, mat);
		}
		var mi = new MeshInstance3D
		{
			Name = "VeinyGround",
			Mesh = mesh,
			TopLevel = true,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		mi.Position = local0;
		return mi;
	}
}
