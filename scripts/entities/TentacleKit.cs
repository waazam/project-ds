using Godot;
using ProjectDS.World;
using ProjectDS.World.StationParts;

namespace ProjectDS.Entities;

/// <summary>
/// The shared make-up of the thing in the lake (<see cref="LakeCreature"/>) and in the pit
/// (<see cref="Leviathan"/>), after the owner's octopus references: gooey, grimy flesh
/// (<c>octopus_flesh.gdshader</c>), raised ring suckers in two staggered rows down the underside
/// (a glossy pink-red rim round a dark wet hole), and at the end of each limb a mouth: three flared,
/// ribbed jaws round a dark throat, ringed with small hooked teeth.
///
/// Limb segments grow along +Y; the underside (the suckers) is +Z, the back -Z.
/// </summary>
public static class TentacleKit
{
	private static StandardMaterial3D _rim, _hole, _tooth, _throat;

	/// <summary>A new flesh material (each creature keeps its own, so its rot is its own).</summary>
	public static ShaderMaterial Flesh(Vector3? backDir = null, float scale = 0.9f, float backAmount = 0.7f)
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/octopus_flesh.gdshader") };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("scale", scale);
		m.SetShaderParameter("back_dir", backDir ?? new Vector3(0, 0, -1));
		m.SetShaderParameter("back_amount", backAmount);
		return m;
	}

	/// <summary>The fleshy lid round an eye socket (the eyes sit in the flesh, not on it).</summary>
	public static StandardMaterial3D Lid => _lid ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.3f, 0.06f, 0.08f), Roughness = 0.15f, MetallicSpecular = 0.7f,
		ClearcoatEnabled = true, Clearcoat = 0.8f, ClearcoatRoughness = 0.1f, RimEnabled = true, Rim = 0.4f, RimTint = 0.6f,
	};
	private static StandardMaterial3D _lid;

	public static StandardMaterial3D SuckerRim => _rim ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.62f, 0.16f, 0.2f), Roughness = 0.12f, MetallicSpecular = 0.8f,
		ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.05f,
		RimEnabled = true, Rim = 0.5f, RimTint = 0.6f,
	};

	public static StandardMaterial3D SuckerHole => _hole ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.14f, 0.01f, 0.03f), Roughness = 0.08f, MetallicSpecular = 0.9f,
	};

	public static StandardMaterial3D Tooth => _tooth ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.78f, 0.72f, 0.5f), Roughness = 0.3f, MetallicSpecular = 0.6f,
	};

	public static StandardMaterial3D Throat => _throat ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.22f, 0.02f, 0.04f), Roughness = 0.1f, MetallicSpecular = 0.8f,
		EmissionEnabled = true, Emission = new Color(0.12f, 0.0f, 0.01f), EmissionEnergyMultiplier = 0.6f,
	};

	/// <summary>Suckers down one segment's underside (+Z): two staggered rows, sized to the limb.</summary>
	public static void Suckers(MeshKit k, float segLen, float r0, float r1, int perRow, float spread = 0.42f)
	{
		for (int row = 0; row < 2; row++)
			for (int s = 0; s < perRow; s++)
			{
				float t = (s + 0.5f + row * 0.5f) / (perRow + 0.5f);
				float y = segLen * t;
				float r = Mathf.Lerp(r0, r1, t);
				float ang = (row == 0 ? -1f : 1f) * spread;
				Vector3 n = new(Mathf.Sin(ang), 0, Mathf.Cos(ang));
				float size = r * 0.34f;
				Vector3 at = new Vector3(0, y, 0) + n * (r * 0.9f);
				// the cup: a short raised ring
				k.Mat(SuckerRim);
				k.Color = Colors.White;
				k.Cylinder(at, at + n * size * 0.55f, size, size * 0.82f, 10, false);
				ItemMeshes.Torus(k, at + n * size * 0.55f, n, size * 0.7f, size * 0.16f, 12, 5);
				// the hole in the middle, wet and dark
				k.Mat(SuckerHole);
				k.Cylinder(at + n * size * 0.2f, at + n * size * 0.5f, size * 0.52f, size * 0.5f, 8, true);
			}
	}

	/// <summary>The mouth at a limb's tip, built on <paramref name="tip"/> (its +Y is along the limb):
	/// three jaws flaring open round a dark throat, teeth round their rims.</summary>
	public static Node3D Maw(Node3D tip, float neckR, float size, Material flesh, int seed, float open = 0.38f)
	{
		var maw = new Node3D { Name = "Maw" };
		tip.AddChild(maw);
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 977 + 13) };
		var k = new MeshKit();
		// a swollen head the jaws grow out of
		k.Mat(flesh);
		k.Color = Colors.White;
		float head = Mathf.Max(neckR * 1.3f, size * 0.38f);
		k.Blob(new Vector3(0, size * 0.28f, 0), new Vector3(head, size * 0.42f, head), seed, 0.08f, false);
		// the throat
		k.Mat(Throat);
		k.Cylinder(new Vector3(0, size * 0.3f, 0), new Vector3(0, size * 0.66f, 0), head * 0.2f, head * 0.8f, 10, true);
		for (int j = 0; j < 3; j++)
		{
			float a = j / 3f * Mathf.Tau + 0.4f;
			Vector3 radial = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new(-radial.Z, 0, radial.X);
			// a jaw: a ribbed, tapering lip, curving outward from the head
			Vector3 root = new Vector3(0, size * 0.58f, 0) + radial * head * 0.55f;
			Vector3 mid = root + (Vector3.Up * 0.8f + radial * open).Normalized() * size * 0.55f;
			Vector3 tipP = mid + (Vector3.Up * 0.5f + radial * (open + 0.5f)).Normalized() * size * 0.45f;
			k.Mat(flesh);
			k.Color = Colors.White;
			float w = head * 1.25f;
			JawPiece(k, root, mid, side, radial, w, w * 0.85f, size * 0.2f);
			JawPiece(k, mid, tipP, side, radial, w * 0.85f, w * 0.3f, size * 0.14f);
			// the inside of the jaw, dark and wet
			k.Mat(Throat);
			JawPiece(k, root - radial * size * 0.04f, mid - radial * size * 0.035f, side, radial, w * 0.8f, w * 0.65f, size * 0.03f);
			// teeth down both edges of the jaw's inner face, hooked inward
			k.Mat(Tooth);
			for (int t = 0; t < 7; t++)
			{
				float u = (t + 0.5f) / 7f;
				Vector3 p = u < 0.5f ? root.Lerp(mid, u * 2f) : mid.Lerp(tipP, (u - 0.5f) * 2f);
				float ww = Mathf.Lerp(w, w * 0.3f, u) * 0.9f;
				foreach (int s in new[] { -1, 1 })
				{
					Vector3 b = p + side * s * ww * 0.5f - radial * size * 0.02f;
					float len = size * rng.RandfRange(0.07f, 0.13f) * (1.2f - u * 0.5f);
					k.Cylinder(b, b - radial * len + Vector3.Up * len * 0.3f, len * 0.28f, 0.002f, 5, false);
				}
			}
		}
		k.CommitTo(maw, "Mouth", true);
		return maw;
	}

	/// <summary>One flattened, tapering piece of a jaw from a to b (a box that narrows).</summary>
	private static void JawPiece(MeshKit k, Vector3 a, Vector3 b, Vector3 side, Vector3 radial, float w0, float w1, float thick)
	{
		Vector3 d = b - a;
		Vector3 n = side.Cross(d).Normalized();
		if (n.Dot(radial) < 0) n = -n;
		Vector3 a0 = a - side * w0 * 0.5f, a1 = a + side * w0 * 0.5f, b0 = b - side * w1 * 0.5f, b1 = b + side * w1 * 0.5f;
		Vector3 t = n * thick * 0.5f;
		Quad(k, a0 + t, a1 + t, b1 + t, b0 + t, n);
		Quad(k, a1 - t, a0 - t, b0 - t, b1 - t, -n);
		Quad(k, a0 - t, a0 + t, b0 + t, b0 - t, -side);
		Quad(k, a1 + t, a1 - t, b1 - t, b1 + t, side);
		Quad(k, b0 + t, b1 + t, b1 - t, b0 - t, d.Normalized());
	}

	private static void Quad(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
	{
		if ((b - a).Cross(d - a).Dot(n) > 0) k.Quad(a, b, c, d, n);
		else k.Quad(b, a, d, c, n);
	}

	/// <summary>An eye's size a fraction <paramref name="f"/> of the way up a limb: big at the root where
	/// it meets the body, tapering down toward the tip (the owner's note).</summary>
	public static float EyeSize(float rootSize, float tipSize, float f) => Mathf.Lerp(rootSize, tipSize, Mathf.Pow(Mathf.Clamp(f, 0f, 1f), 0.8f));
}
