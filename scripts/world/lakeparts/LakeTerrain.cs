using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// Builds the lake's ground (a heightfield bowl from <see cref="LakeShape.Ground"/>, with trimesh
/// collision where the player can actually stand) and its water surface (a fine grid carrying the
/// per-vertex wave fade and depth that lake_water.gdshader reads).
/// </summary>
public static class LakeTerrain
{
	public const float MinX = -175f, MaxX = 175f, MinZ = LakeShape.FarShoreZ - 125f, MaxZ = 105f;
	private const float Cell = 2f;

	public static MeshInstance3D BuildGround(Node3D parent)
	{
		int nx = Mathf.RoundToInt((MaxX - MinX) / Cell), nz = Mathf.RoundToInt((MaxZ - MinZ) / Cell);
		int w = nx + 1;
		var verts = new Vector3[w * (nz + 1)];
		var norms = new Vector3[verts.Length];
		var cols = new Color[verts.Length];
		for (int j = 0; j <= nz; j++)
			for (int i = 0; i <= nx; i++)
			{
				float x = MinX + i * Cell, z = MinZ + j * Cell;
				int v = j * w + i;
				verts[v] = new Vector3(x, LakeShape.Ground(x, z), z);
				norms[v] = LakeShape.GroundNormal(x, z, Cell * 0.5f);
				float d = LakeShape.ShoreDist(x, z);
				cols[v] = new Color(BeachGravel(x, z, d), CanopyShade(x, z, d), 0f);
			}
		var idx = new int[nx * nz * 6];
		int k = 0;
		for (int j = 0; j < nz; j++)
			for (int i = 0; i < nx; i++)
			{
				int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
				// Godot's front faces wind clockwise seen from the front (above)
				idx[k++] = a; idx[k++] = b; idx[k++] = c;
				idx[k++] = b; idx[k++] = d; idx[k++] = c;
			}
		var arr = new Godot.Collections.Array();
		arr.Resize((int)Mesh.ArrayType.Max);
		arr[(int)Mesh.ArrayType.Vertex] = verts;
		arr[(int)Mesh.ArrayType.Normal] = norms;
		arr[(int)Mesh.ArrayType.Color] = cols;
		arr[(int)Mesh.ArrayType.Index] = idx;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
		mesh.SurfaceSetMaterial(0, ShoreMaterial());
		var mi = new MeshInstance3D { Name = "Ground", Mesh = mesh };
		parent.AddChild(mi);

		// Collision only where anyone stands: the wake-up clearing and the station's rise (the rest is
		// fenced off and far too big to be worth a trimesh).
		var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "dirt");
		parent.AddChild(body);
		AddCollision(body, verts, w, nx, nz, new Rect2(-30f, -6f, 60f, 42f));
		AddCollision(body, verts, w, nx, nz, new Rect2(-34f, LakeShape.FarShoreZ - 45f, 68f, 50f));
		return mi;
	}

	private static void AddCollision(StaticBody3D body, Vector3[] verts, int w, int nx, int nz, Rect2 area)
	{
		var faces = new List<Vector3>();
		for (int j = 0; j < nz; j++)
			for (int i = 0; i < nx; i++)
			{
				int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
				Vector3 va = verts[a];
				if (va.X < area.Position.X || va.X > area.End.X || va.Z < area.Position.Y || va.Z > area.End.Y) continue;
				faces.Add(va); faces.Add(verts[b]); faces.Add(verts[c]);
				faces.Add(verts[b]); faces.Add(verts[d]); faces.Add(verts[c]);
			}
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces(faces.ToArray());
		body.AddChild(new CollisionShape3D { Shape = shape });
	}

	/// <summary>Gravel on the two landing beaches, fading out along the shore to either side.</summary>
	private static float BeachGravel(float x, float z, float d)
	{
		float near = 1f - Mathf.SmoothStep(6f, 16f, new Vector2(x, z).DistanceTo(new Vector2(0f, LakeShape.NearShoreZ + 2f)));
		float far = 1f - Mathf.SmoothStep(7f, 18f, new Vector2(x, z).DistanceTo(new Vector2(0f, LakeShape.FarShoreZ - 3f)));
		return Mathf.Max(near, far) * (1f - Mathf.SmoothStep(8f, 14f, d));
	}

	/// <summary>Baked shade under the treeline (1 = open sky).</summary>
	private static float CanopyShade(float x, float z, float d)
	{
		float clearing = Mathf.Max(1f - Mathf.SmoothStep(14f, 20f, new Vector2(x, z).DistanceTo(LakeShape.NearClearing)),
			1f - Mathf.SmoothStep(16f, 24f, new Vector2(x, z).DistanceTo(LakeShape.StationSite)));
		return 1f - 0.4f * Mathf.SmoothStep(10f, 20f, d) * (1f - clearing);
	}

	private static ShaderMaterial ShoreMaterial() => (ShaderMaterial)ProcTextures.Cached("lake_shore", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/lake_shore.gdshader") };
		m.SetShaderParameter("tex_grass", ProcTextures.GrassGround());
		m.SetShaderParameter("tex_floor", ProcTextures.ForestFloor());
		m.SetShaderParameter("tex_moss", ProcTextures.Moss());
		m.SetShaderParameter("tex_dirt", ProcTextures.Dirt());
		m.SetShaderParameter("tex_gravel", ProcTextures.Gravel());
		m.SetShaderParameter("tex_rock", ProcTextures.Rock());
		m.SetShaderParameter("tex_noise", ProcTextures.WaterNoise());
		return m;
	});

	// ------------------------------------------------------------------ water

	/// <summary>The water surface: a 1 m grid over the whole oval (the ground hides the overhang), each
	/// vertex carrying its wave fade (r) and depth (g).</summary>
	public static MeshInstance3D BuildWater(Node3D parent, ShaderMaterial material)
	{
		const float cell = 1f;
		float x0 = -LakeShape.SemiX - 6f, x1 = LakeShape.SemiX + 6f;
		float z0 = LakeShape.FarShoreZ - 6f, z1 = LakeShape.NearShoreZ + 6f;
		int nx = Mathf.CeilToInt((x1 - x0) / cell), nz = Mathf.CeilToInt((z1 - z0) / cell);
		int w = nx + 1;
		var verts = new List<Vector3>();
		var cols = new List<Color>();
		var remap = new int[w * (nz + 1)];
		for (int j = 0; j <= nz; j++)
			for (int i = 0; i <= nx; i++)
			{
				float x = x0 + i * cell, z = z0 + j * cell;
				float d = LakeShape.ShoreDist(x, z);
				if (d > 4f) { remap[j * w + i] = -1; continue; }
				remap[j * w + i] = verts.Count;
				verts.Add(new Vector3(x, 0f, z));
				float depth = Mathf.SmoothStep(0f, 14f, -d);
				cols.Add(new Color(LakeShape.WaveFade(x, z), depth, 0f));
			}
		var idx = new List<int>();
		for (int j = 0; j < nz; j++)
			for (int i = 0; i < nx; i++)
			{
				int a = remap[j * w + i], b = remap[j * w + i + 1], c = remap[(j + 1) * w + i], d = remap[(j + 1) * w + i + 1];
				if (a < 0 || b < 0 || c < 0 || d < 0) continue;
				idx.Add(a); idx.Add(b); idx.Add(c);
				idx.Add(b); idx.Add(d); idx.Add(c);
			}
		var norms = new Vector3[verts.Count];
		System.Array.Fill(norms, Vector3.Up);
		var arr = new Godot.Collections.Array();
		arr.Resize((int)Mesh.ArrayType.Max);
		arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arr[(int)Mesh.ArrayType.Normal] = norms;
		arr[(int)Mesh.ArrayType.Color] = cols.ToArray();
		arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
		mesh.SurfaceSetMaterial(0, material);
		// the waves lift vertices up to ~1.5 m: keep the culling box honest
		mesh.CustomAabb = new Aabb(new Vector3(x0, -2f, z0), new Vector3(x1 - x0, 4f, z1 - z0));
		var mi = new MeshInstance3D { Name = "Water", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild(mi);
		return mi;
	}
}
