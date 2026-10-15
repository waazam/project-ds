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
		// (2026-10-13i, the owner: "too smooth") the creatures' skin detail: its creases, cracks, veins and pores
		CreatureDetail.Hook(m);
		m.SetShaderParameter("cd_scale", 1.4f);
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
		// (2026-10-15) nearer the flesh's own colour, a little paler (they were a bright pink, like rubber rings)
		AlbedoColor = new Color(0.5f, 0.17f, 0.19f), Roughness = 0.14f, MetallicSpecular = 0.75f,
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

	/// <summary>Suckers down one segment's underside (+Z): two loose, staggered rows, sized to the limb.
	/// (2026-10-15, the owner: "less robotic and blocky") each a low, soft cup of the flesh swelling out of the skin with a
	/// wet dark pit in its middle — not a raised o-ring — and no two alike: each its own size, a little off its row and its
	/// spacing, now and then one missing, smaller toward the tip. <paramref name="seed"/> keeps a limb's own pattern.</summary>
	public static void Suckers(MeshKit k, float segLen, float r0, float r1, int perRow, float spread = 0.42f, int seed = 0)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed != 0 ? seed : Mathf.Abs((int)(r0 * 1000f) * 31 + (int)(segLen * 977f))) + 7 };
		for (int row = 0; row < 2; row++)
			for (int s = 0; s < perRow; s++)
			{
				if (rng.Randf() < 0.08f) continue;   // a gap now and then
				float t = (s + 0.5f + row * 0.5f + rng.RandfRange(-0.18f, 0.18f)) / (perRow + 0.5f);
				t = Mathf.Clamp(t, 0.04f, 0.98f);
				float y = segLen * t;
				float r = Mathf.Lerp(r0, r1, t);
				float ang = (row == 0 ? -1f : 1f) * spread * rng.RandfRange(0.82f, 1.15f) + rng.RandfRange(-0.06f, 0.06f);
				Vector3 n = new(Mathf.Sin(ang), 0, Mathf.Cos(ang));
				float size = r * 0.3f * rng.RandfRange(0.72f, 1.18f);
				// sunk a little into the skin, so it swells out of it rather than sitting on it
				Vector3 at = new Vector3(0, y, 0) + n * (r * 0.93f);
				k.Mat(SuckerRim);
				k.Color = Colors.White * rng.RandfRange(0.85f, 1.08f);
				ItemMeshes.Loft(k, new[]
				{
					new ItemMeshes.Ring(at - n * size * 0.3f, size * 1.25f, size * 1.15f),
					new ItemMeshes.Ring(at + n * size * 0.02f, size * 1.12f, size * 1.04f),
					new ItemMeshes.Ring(at + n * size * 0.15f, size * 0.98f, size * 0.92f),
					new ItemMeshes.Ring(at + n * size * 0.22f, size * 0.8f, size * 0.76f),
				}, 12, false, true, Vector3.Up, 2f);
				// the pit in the middle, wet and dark, on the cup's flat crown
				k.Mat(SuckerHole);
				k.Color = Colors.White;
				ItemMeshes.Disc(k, at + n * size * 0.226f, n, size * 0.44f * rng.RandfRange(0.85f, 1.1f), 12);
			}
	}

	/// <summary>The mouth at a limb's tip, built on <paramref name="tip"/> (its +Y is along the limb):
	/// three jaws flaring open round a dark throat, teeth round their rims.
	/// (2026-10-15, the owner: "less robotic and blocky") the head is the limb itself swelling a little into the mouth (it
	/// was a ball on a stick), and each jaw is a curled, rounded lip of flesh, thick at its root and thinning to a soft edge
	/// (it was a pair of flat boxes): lofted along a bent spine, each its own length and flare.</summary>
	public static Node3D Maw(Node3D tip, float neckR, float size, Material flesh, int seed, float open = 0.38f)
	{
		var maw = new Node3D { Name = "Maw" };
		tip.AddChild(maw);
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 977 + 13) };
		var k = new MeshKit();
		k.Mat(flesh);
		k.Color = Colors.White;
		// the swelling: the limb's own end widening into the mouth, no seam, no ball
		float head = Mathf.Max(neckR * 1.12f, size * 0.3f);
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(new Vector3(0, -size * 0.12f, 0), neckR * 0.98f),
			new ItemMeshes.Ring(new Vector3(0, size * 0.12f, 0), Mathf.Lerp(neckR, head, 0.6f)),
			new ItemMeshes.Ring(new Vector3(0, size * 0.36f, 0), head),
			new ItemMeshes.Ring(new Vector3(0, size * 0.56f, 0), head * 0.9f),
		}, 18, false, false, Vector3.Right, 2f);
		// the throat
		k.Mat(Throat);
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(new Vector3(0, size * 0.3f, 0), head * 0.3f),
			new ItemMeshes.Ring(new Vector3(0, size * 0.5f, 0), head * 0.72f),
			new ItemMeshes.Ring(new Vector3(0, size * 0.58f, 0), head * 0.86f),
		}, 14, true, false, Vector3.Right, 2f);
		for (int j = 0; j < 3; j++)
		{
			float a = j / 3f * Mathf.Tau + 0.4f + rng.RandfRange(-0.15f, 0.15f);
			Vector3 radial = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new(-radial.Z, 0, radial.X);
			float len = rng.RandfRange(0.85f, 1.15f), flare = open * rng.RandfRange(0.8f, 1.25f);
			// the jaw's spine: up out of the rim, then curling outward and back
			Vector3 root = new Vector3(0, size * 0.54f, 0) + radial * head * 0.62f;
			Vector3 mid = root + (Vector3.Up * 0.8f + radial * flare).Normalized() * size * 0.5f * len;
			Vector3 tipP = mid + (Vector3.Up * 0.35f + radial * (flare + 0.65f)).Normalized() * size * 0.42f * len;
			Vector3 B(float u) => (1 - u) * (1 - u) * root + 2 * (1 - u) * u * mid + u * u * tipP;
			float w = head * 1.15f;
			var lip = new System.Collections.Generic.List<ItemMeshes.Ring>();
			var inner = new System.Collections.Generic.List<ItemMeshes.Ring>();
			for (int q = 0; q <= 6; q++)
			{
				float u = q / 6f;
				float width = w * Mathf.Lerp(1f, 0.18f, Mathf.Pow(u, 1.3f)) * 0.5f;
				float thick = size * Mathf.Lerp(0.11f, 0.025f, u);
				lip.Add(new ItemMeshes.Ring(B(u), width, thick));
				inner.Add(new ItemMeshes.Ring(B(u) - radial * thick * 0.7f, width * 0.86f, thick * 0.35f));
			}
			k.Mat(flesh);
			k.Color = Colors.White;
			ItemMeshes.Loft(k, lip, 12, false, true, side, 2f, dome: 0.6f);
			// the inside of the jaw, dark and wet
			k.Mat(Throat);
			ItemMeshes.Loft(k, inner, 10, false, true, side, 2f, dome: 0.5f);
			// teeth down both edges of the jaw's inner face, hooked inward, each its own length
			k.Mat(Tooth);
			for (int t = 0; t < 7; t++)
			{
				float u = (t + 0.5f) / 7f;
				Vector3 p = B(u * 0.92f);
				float ww = w * Mathf.Lerp(1f, 0.18f, Mathf.Pow(u, 1.3f)) * 0.42f;
				foreach (int s in new[] { -1, 1 })
				{
					Vector3 b = p + side * s * ww - radial * size * 0.03f;
					float tl = size * rng.RandfRange(0.06f, 0.13f) * (1.2f - u * 0.5f);
					k.Cylinder(b, b - radial * tl + Vector3.Up * tl * 0.3f, tl * 0.28f, 0.002f, 6, false);
				}
			}
		}
		k.CommitTo(maw, "Mouth", true);
		return maw;
	}

	/// <summary>An eye's size a fraction <paramref name="f"/> of the way up a limb: big at the root where
	/// it meets the body, tapering down toward the tip (the owner's note).</summary>
	public static float EyeSize(float rootSize, float tipSize, float f) => Mathf.Lerp(rootSize, tipSize, Mathf.Pow(Mathf.Clamp(f, 0f, 1f), 0.8f));
}
