using System.Collections.Generic;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The creatures' surface detail (2026-10-13i, the owner: "the monster models look too smooth, add more detail"). Two
/// things every creature's hide shader reads (assets/shaders/creature_detail.gdshaderinc):
/// - the skin's detail maps (assets/textures/creatures/flesh_detail_*.png, tools/Textures/make_creature_detail.py):
///   folds, wrinkles, cracked dry patches, veins, scars, pores, projected three ways;
/// - the mesh again with its rest pose in its custom channels, so that detail rides the flesh as it moves instead of
///   swimming across it, and the mesh's own creases (its curvature, worked out once here) darken as an ambient
///   occlusion would: between the ribs, round the knuckles and the joints, in the folds of the neck.
///
/// The channels: CUSTOM0 = rest position (xyz) and curvature (w: + in a crease, - on a ridge, -1..1); CUSTOM1 = rest
/// normal; CUSTOM2 = rest tangent (xyz) and its sign (w). Done once per mesh (every instance shares it); the skin, the
/// blend shapes and the surfaces' materials are kept, the vertices themselves untouched.
/// </summary>
public static class CreatureDetail
{
	private static readonly Dictionary<Mesh, ArrayMesh> _done = new();
	private static readonly Dictionary<string, float[]> _curv = new();

	private static string TopologyKey(int verts, int[] idx)
	{
		ulong h = 1469598103934665603UL;
		foreach (int i in idx) h = (h ^ (uint)i) * 1099511628211UL;
		return $"{verts}:{idx.Length}:{h:x}";
	}
	private static Texture2D _normal, _mask;

	public static Texture2D NormalMap => _normal ??= GD.Load<Texture2D>("res://assets/textures/creatures/flesh_detail_normal.png");
	public static Texture2D MaskMap => _mask ??= GD.Load<Texture2D>("res://assets/textures/creatures/flesh_detail_mask.png");

	/// <summary>Hands the detail maps to a material whose shader includes creature_detail.gdshaderinc.</summary>
	public static T Hook<T>(T m) where T : ShaderMaterial
	{
		m?.SetShaderParameter("cd_normal", NormalMap);
		m?.SetShaderParameter("cd_mask", MaskMap);
		m?.SetShaderParameter("cd_rest", true);
		return m;
	}

	/// <summary>Gives <paramref name="mi"/>'s mesh its rest pose and curvature (see above). <paramref name="surfaces"/>:
	/// only meshes with a surface whose material's name starts with one of these (null: any).</summary>
	public static void Prepare(MeshInstance3D mi, params string[] surfaces)
	{
		if (mi?.Mesh is not ArrayMesh src) return;
		if (!_done.TryGetValue(src, out var rest))
		{
			rest = Build(src, surfaces);
			_done[src] = rest;
		}
		if (rest != null) mi.Mesh = rest;
	}

	private static bool Wanted(ArrayMesh src, string[] surfaces)
	{
		if (surfaces == null || surfaces.Length == 0) return true;
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			string n = src.SurfaceGetMaterial(s)?.ResourceName ?? "";
			foreach (var w in surfaces) if (n.StartsWith(w)) return true;
		}
		return false;
	}

	private static ArrayMesh Build(ArrayMesh src, string[] surfaces)
	{
		if (!Wanted(src, surfaces)) return null;
		ulong t0 = Time.GetTicksUsec();
		var rest = new ArrayMesh { BlendShapeMode = src.BlendShapeMode, ResourceName = src.ResourceName + "_detail" };
		for (int b = 0; b < src.GetBlendShapeCount(); b++) rest.AddBlendShape(src.GetBlendShapeName(b));
		int verts = 0;
		ulong curvUs = 0;
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			var arr = src.SurfaceGetArrays(s);
			var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var n = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
			var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
			float[] tan = arr[(int)Mesh.ArrayType.Tangent].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
			bool madeTangents = false;
			if (tan == null || tan.Length < v.Length * 4)
			{
				// (none: one made from the normal, and handed to the mesh too, so the posed frame matches the rest one)
				tan = new float[v.Length * 4];
				for (int i = 0; i < v.Length; i++)
				{
					Vector3 nn = i < n.Length ? n[i] : Vector3.Up;
					Vector3 t = nn.Cross(Mathf.Abs(nn.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
					tan[i * 4] = t.X; tan[i * 4 + 1] = t.Y; tan[i * 4 + 2] = t.Z; tan[i * 4 + 3] = 1f;
				}
				madeTangents = true;
			}
			ulong tc = Time.GetTicksUsec();
			// (2026-10-23, the lag meter: the lake's limbs, each laid along its own length from the one Blender limb, worked
			// their creases out afresh every breach, 25-40 ms mid-crossing) the creases kept by the mesh's topology: stretched to
			// another length, a limb's ribs and knuckles are the same vertices
			string ck = idx == null ? null : TopologyKey(v.Length, idx);
			if (ck == null || !_curv.TryGetValue(ck, out var curv)) { curv = Curvature(v, n, idx); if (ck != null) _curv[ck] = curv; }
			curvUs += Time.GetTicksUsec() - tc;
			var c0 = new float[v.Length * 4];
			var c1 = new float[v.Length * 4];
			var c2 = new float[v.Length * 4];
			for (int i = 0; i < v.Length; i++)
			{
				Vector3 nn = i < n.Length ? n[i] : Vector3.Up;
				c0[i * 4] = v[i].X; c0[i * 4 + 1] = v[i].Y; c0[i * 4 + 2] = v[i].Z; c0[i * 4 + 3] = curv[i];
				c1[i * 4] = nn.X; c1[i * 4 + 1] = nn.Y; c1[i * 4 + 2] = nn.Z; c1[i * 4 + 3] = 0f;
				c2[i * 4] = tan[i * 4]; c2[i * 4 + 1] = tan[i * 4 + 1]; c2[i * 4 + 2] = tan[i * 4 + 2]; c2[i * 4 + 3] = tan[i * 4 + 3];
			}
			if (madeTangents) arr[(int)Mesh.ArrayType.Tangent] = tan;
			arr[(int)Mesh.ArrayType.Custom0] = c0;
			arr[(int)Mesh.ArrayType.Custom1] = c1;
			arr[(int)Mesh.ArrayType.Custom2] = c2;
			var flags = (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
				| ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
				| ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift));
			var blends = src.SurfaceGetBlendShapeArrays(s);
			rest.AddSurfaceFromArrays(src.SurfaceGetPrimitiveType(s), arr, blends, null, flags);
			rest.SurfaceSetMaterial(s, src.SurfaceGetMaterial(s));
			if (src.SurfaceGetName(s) is { Length: > 0 } sn) rest.SurfaceSetName(s, sn);
			verts += v.Length;
		}
		GD.Print($"[creature-detail] {src.ResourceName}: {verts} vertices in {(Time.GetTicksUsec() - t0) / 1000.0:0} ms (its creases {curvUs / 1000.0:0} ms)");
		return rest;
	}

	/// <summary>Each vertex's curvature at the scale of a rib or a knuckle: how far its neighbours rise along its normal
	/// (+: it lies in a crease, -: on a ridge), averaged out over a few rings, then scaled to about -1..1. Split
	/// vertices (UV seams) are welded first, so a seam doesn't read as an edge. Flat arrays throughout (it runs as each
	/// creature first loads).</summary>
	private static float[] Curvature(Vector3[] v, Vector3[] n, int[] idx)
	{
		int count = v.Length;
		var result = new float[count];
		if (idx == null || idx.Length < 3 || count == 0) return result;
		// weld by position (a 0.05 mm grid)
		var weld = new int[count];
		var map = new Dictionary<long, int>(count);
		var wpos = new Vector3[count];
		var wnrm = new Vector3[count];
		int wc = 0;
		for (int i = 0; i < count; i++)
		{
			long key = (((long)Mathf.RoundToInt(v[i].X * 20000f) & 0x1FFFFF) << 42) | (((long)Mathf.RoundToInt(v[i].Y * 20000f) & 0x1FFFFF) << 21)
				| ((long)Mathf.RoundToInt(v[i].Z * 20000f) & 0x1FFFFF);
			if (!map.TryGetValue(key, out int w)) { w = wc++; map[key] = w; wpos[w] = v[i]; }
			weld[i] = w;
			wnrm[w] += i < n.Length ? n[i] : Vector3.Up;
		}
		// the neighbours, packed: each triangle gives each corner its other two (an edge comes twice, once from each of
		// its triangles: every neighbour weighed alike)
		var deg = new int[wc + 1];
		int tris = idx.Length / 3;
		for (int t = 0; t < tris; t++)
			for (int c = 0; c < 3; c++) deg[weld[idx[t * 3 + c]]] += 2;
		var start = new int[wc + 1];
		for (int i = 0; i < wc; i++) start[i + 1] = start[i] + deg[i];
		var fill = new int[wc];
		var nb = new int[start[wc]];
		for (int t = 0; t < tris; t++)
		{
			int a = weld[idx[t * 3]], b = weld[idx[t * 3 + 1]], c = weld[idx[t * 3 + 2]];
			nb[start[a] + fill[a]++] = b; nb[start[a] + fill[a]++] = c;
			nb[start[b] + fill[b]++] = c; nb[start[b] + fill[b]++] = a;
			nb[start[c] + fill[c]++] = a; nb[start[c] + fill[c]++] = b;
		}
		var k = new float[wc];
		for (int i = 0; i < wc; i++)
		{
			Vector3 nn = wnrm[i].Normalized();
			float s = 0f;
			int cnt = 0;
			for (int e = start[i]; e < start[i + 1]; e++)
			{
				int j = nb[e];
				if (j == i) continue;
				Vector3 d = wpos[j] - wpos[i];
				float l = d.Length();
				if (l > 1e-7f) { s += nn.Dot(d) / l; cnt++; }
			}
			k[i] = cnt > 0 ? s / cnt : 0f;
		}
		// spread over a few rings (a crease, not a single edge)
		var tmp = new float[wc];
		for (int pass = 0; pass < 4; pass++)
		{
			for (int i = 0; i < wc; i++)
			{
				float s = k[i] * 4f;
				for (int e = start[i]; e < start[i + 1]; e++) s += k[nb[e]];
				tmp[i] = s / (4f + start[i + 1] - start[i]);
			}
			(k, tmp) = (tmp, k);
		}
		// scaled by its spread (the 90th centile of its size), so every mesh lands on about -1..1
		var abs = new float[wc];
		for (int i = 0; i < wc; i++) abs[i] = Mathf.Abs(k[i]);
		System.Array.Sort(abs);
		float scale = Mathf.Max(abs[(int)(wc * 0.9f)], 1e-5f);
		for (int i = 0; i < count; i++) result[i] = Mathf.Clamp(k[weld[i]] / scale, -1f, 1f);
		return result;
	}
}
