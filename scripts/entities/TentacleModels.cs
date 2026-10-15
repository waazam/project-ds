using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The tentacle creatures' Blender-built flesh (tools/Blender/tentacle.py → assets/models/tentacle/tentacle.glb; 2026-10-15,
/// the owner: "everything should be made in blender"): the limb (three builds: <c>Limb</c> for the lake and the window,
/// <c>LimbLong</c> for the Leviathan's thick limbs, <c>LimbWhip</c> for its skinny ones), the mouth (<c>Maw</c>), the eye's
/// parts and the Leviathan's hide. The limbs are made straight up +Y, <see cref="AuthoredLength"/> long and of unit
/// radius, their sucker cups shaped into the skin; <see cref="Limb"/> lays a limb's own length and radius along it over
/// them, so one model serves every limb, and the creatures go on driving the bones as before. Everything here is null
/// when the model is missing, and the callers keep their code-built flesh then.
/// </summary>
public static class TentacleModels
{
	public const string Path = "res://assets/models/tentacle/tentacle.glb";
	public const float AuthoredLength = 12f;
	/// <summary>`--no-tentacle-model` (dev): the code-built flesh, for a before/after.</summary>
	public static readonly bool Disabled = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-tentacle-model") >= 0;
	private static Dictionary<string, ArrayMesh> _meshes;

	/// <summary>A mesh of the model by its object's name, or null.</summary>
	public static ArrayMesh Get(string name)
	{
		if (_meshes == null && Disabled) _meshes = new Dictionary<string, ArrayMesh>();
		if (_meshes == null)
		{
			_meshes = new Dictionary<string, ArrayMesh>();
			if (ResourceLoader.Exists(Path) && GD.Load<PackedScene>(Path)?.Instantiate() is Node root)
			{
				var stack = new Stack<Node>();
				stack.Push(root);
				while (stack.Count > 0)
				{
					var n = stack.Pop();
					foreach (var c in n.GetChildren()) stack.Push(c);
					if (n is MeshInstance3D mi && mi.Mesh is ArrayMesh am) _meshes[mi.Name.ToString()] = am;
				}
				root.Free();
			}
		}
		return _meshes.TryGetValue(name, out var m) ? m : null;
	}

	public static bool Available => Get("Limb") != null;

	/// <summary>Which of a surface's materials (by the model's material name) to use.</summary>
	private static Material Pick(ArrayMesh src, int s, IReadOnlyDictionary<string, Material> mats)
	{
		string n = src.SurfaceGetMaterial(s)?.ResourceName ?? "";
		if (string.IsNullOrEmpty(n)) n = src.SurfaceGetName(s);
		foreach (var kv in mats) if (n.StartsWith(kv.Key)) return kv.Value;
		return src.SurfaceGetMaterial(s);
	}

	/// <summary>
	/// The limb <paramref name="model"/>, <paramref name="length"/> long, its radius <paramref name="radius"/>(f) a fraction
	/// f of the way up: each point of the model taken at its own f and put out at that radius (the cups, the lumps and the
	/// creases scaling with it), its normal carried through the same stretch. The model's rounded end follows on past the
	/// last ring. <paramref name="rest"/> also writes CUSTOM0/1 (the rest position and normal octopus_flesh's
	/// rest_space reads). Materials by the model's names: TentFlesh, SuckerRim, SuckerHole.
	/// </summary>
	public static ArrayMesh Limb(string model, float length, Func<float, float> radius, Material flesh, Material rim, Material hole,
		bool rest = false, Func<Vector3, (int[] bones, float[] weights)> skin = null)
	{
		var src = Get(model);
		if (src == null) return null;
		var mats = new Dictionary<string, Material> { ["TentFlesh"] = flesh, ["SuckerRim"] = rim, ["SuckerHole"] = hole };
		float sy = length / AuthoredLength;
		var dst = new ArrayMesh();
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			var arr = src.SurfaceGetArrays(s);
			var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
			var nr = (Vector3[])arr[(int)Mesh.ArrayType.Normal];
			var ov = new Vector3[v.Length];
			var on = new Vector3[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				float y = v[i].Y;
				float f = Mathf.Clamp(y / AuthoredLength, 0f, 1f);
				float past = Mathf.Max(0f, y - AuthoredLength);
				float r = radius(f);
				const float h = 0.004f;
				float dr = (radius(Mathf.Min(1f, f + h)) - radius(Mathf.Max(0f, f - h))) / (2f * h) / AuthoredLength;   // d radius / d (model y)
				if (past > 0f) dr = 0f;
				ov[i] = new Vector3(v[i].X * r, f * length + past * r, v[i].Z * r);
				// the normal through the stretch (the inverse transpose of its Jacobian)
				float a = v[i].X * dr, c = v[i].Z * dr, syy = past > 0f ? r : sy;
				var n = nr[i];
				on[i] = new Vector3(n.X / r, (-a * n.X - c * n.Z) / (r * syy) + n.Y / syy, n.Z / r).Normalized();
			}
			arr[(int)Mesh.ArrayType.Vertex] = ov;
			arr[(int)Mesh.ArrayType.Normal] = on;
			arr[(int)Mesh.ArrayType.Tangent] = default;
			var flags = (Mesh.ArrayFormat)0;
			if (rest)
			{
				var c0 = new float[ov.Length * 4];
				var c1 = new float[ov.Length * 4];
				for (int i = 0; i < ov.Length; i++)
				{
					c0[i * 4] = ov[i].X; c0[i * 4 + 1] = ov[i].Y; c0[i * 4 + 2] = ov[i].Z; c0[i * 4 + 3] = 1f;
					c1[i * 4] = on[i].X; c1[i * 4 + 1] = on[i].Y; c1[i * 4 + 2] = on[i].Z;
				}
				arr[(int)Mesh.ArrayType.Custom0] = c0;
				arr[(int)Mesh.ArrayType.Custom1] = c1;
				flags = (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
					| ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift));
			}
			if (skin != null)
			{
				var bi = new int[ov.Length * 4];
				var bw = new float[ov.Length * 4];
				for (int i = 0; i < ov.Length; i++)
				{
					var (b, w) = skin(ov[i]);
					for (int k = 0; k < 4; k++) { bi[i * 4 + k] = k < b.Length ? b[k] : 0; bw[i * 4 + k] = k < w.Length ? w[k] : 0f; }
				}
				arr[(int)Mesh.ArrayType.Bones] = bi;
				arr[(int)Mesh.ArrayType.Weights] = bw;
			}
			dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, null, null, flags);
			dst.SurfaceSetMaterial(s, Pick(src, s, mats));
		}
		return dst;
	}

	/// <summary>The mouth on <paramref name="tip"/> (its +Y along the limb), <paramref name="size"/> across, or null:
	/// the limb's end swelling into three curled lips round the throat, teeth down their edges.</summary>
	public static Node3D Maw(Node3D tip, float size, Material flesh)
	{
		var mesh = Get("Maw");
		if (mesh == null) return null;
		var maw = new Node3D { Name = "Maw" };
		tip.AddChild(maw);
		var mi = new MeshInstance3D { Name = "Mouth", Mesh = mesh, Scale = Vector3.One * size };
		var mats = new Dictionary<string, Material> { ["MawFlesh"] = flesh, ["Throat"] = TentacleKit.Throat, ["Tooth"] = TentacleKit.Tooth };
		for (int s = 0; s < mesh.GetSurfaceCount(); s++) mi.SetSurfaceOverrideMaterial(s, Pick(mesh, s, mats));
		maw.AddChild(mi);
		return maw;
	}

	/// <summary>One part of the eye (Eye_Ball, Eye_Iris, Eye_Pupil_Round/_Slit/_Bar, Eye_Lid) as a MeshInstance3D under
	/// <paramref name="socket"/>, or null.</summary>
	public static MeshInstance3D EyePart(Node3D socket, string part, Material m, Vector3? scale = null)
	{
		var mesh = Get("Eye_" + part);
		if (mesh == null) return null;
		var mi = new MeshInstance3D
		{
			Name = part, Mesh = mesh, MaterialOverride = m, Scale = scale ?? Vector3.One,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		socket.AddChild(mi);
		return mi;
	}
}
