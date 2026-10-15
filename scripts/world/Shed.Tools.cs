using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the Hollow's interiors pass) The shed's tools, modelled: they had been flat cards on the pegboard (a
/// grey slab for a saw, two sticks for a wrench and pliers) and a beam and a box for the shovel. Small and cheap: a few
/// dozen triangles each, in the shed's small-things mesh (no shadows: the bake's dark under them does that), tool steel
/// and hand-dark wood (<see cref="HollowLook"/>). The hammer itself is the pickup's own model on the bench; nothing here
/// looks like it.
/// </summary>
public partial class Shed
{
	/// <summary>The tools hung on the pegboard, whose face is at z = <paramref name="face"/>.</summary>
	private static void Tools(MeshKit k, float face)
	{
		var steel = HollowLook.ToolSteel;
		var wood = HollowLook.Handle;
		float z = face + 0.012f;

		// pegs: a short steel hook out of a hole, its tip turned up
		void Peg(float x, float y)
		{
			k.Mat(steel);
			k.Color = new Color(0.8f, 0.78f, 0.76f);
			k.Cylinder(new Vector3(x, y, face - 0.002f), new Vector3(x, y, face + 0.045f), 0.003f, 0.003f, 5, false);
			k.Cylinder(new Vector3(x, y, face + 0.045f), new Vector3(x, y + 0.015f, face + 0.05f), 0.003f, 0.003f, 5, true);
		}

		// (2026-10-15) the Blender tools (fixtures.py), each in the frame its code below used; that code is their fallback
		if (FixtureModels.Have("fx_hand_saw", "fx_wrench", "fx_pliers", "fx_screwdriver", "fx_rope_coil", "fx_block_plane"))
		{
			var roles = ToolRoles;
			var sb = new Basis(Vector3.Back, 0.1f);
			Vector3 so = new(-0.42f, 1.36f, z);
			FurnitureKit.Add(k, "fx_hand_saw", new Transform3D(sb, so), roles);
			Peg(so.X - 0.21f, so.Y + 0.02f);
			Vector3 wo = new(0.03f, 1.33f, z);
			FurnitureKit.Add(k, "fx_wrench", new Transform3D(Basis.Identity, wo), roles);
			Peg(wo.X, wo.Y - 0.1f);
			Vector3 piv = new(0.17f, 1.44f, z);
			FurnitureKit.Add(k, "fx_pliers", new Transform3D(Basis.Identity, piv), roles);
			Peg(piv.X, piv.Y + 0.065f);
			Vector3 dro = new(0.3f, 1.4f, z + 0.004f);
			FurnitureKit.Add(k, "fx_screwdriver", new Transform3D(Basis.Identity, dro), roles);
			Peg(dro.X + 0.022f, dro.Y - 0.01f);
			FurnitureKit.Add(k, "fx_rope_coil", new Transform3D(Basis.Identity, new Vector3(0.5f, 1.34f, face)), roles);
			FurnitureKit.Add(k, "fx_block_plane", new Transform3D(new Basis(Vector3.Up, 0.35f), new Vector3(-0.8f, BenchTop, -1.05f + 0.22f)), roles);
			return;
		}

		// ---- the hand saw: a tapered blade, its toothed edge down, the closed wooden grip at the heel
		{
			var b = new Basis(Vector3.Back, 0.1f);
			Vector3 o = new(-0.42f, 1.36f, z);
			Vector3 P(float x, float y, float dz = 0f) => o + b * new Vector3(x, y, dz);
			k.Mat(steel);
			k.Color = new Color(0.85f, 0.83f, 0.8f);
			const float half = 0.25f, t = 0.0012f;
			// the blade: heel 0.115 tall at the grip, toe 0.07, the back edge straight, the teeth along the bottom
			float hHeel = 0.115f, hToe = 0.07f, top = 0.04f;
			Vector3 tl = P(-half, top), tr = P(half, top), br = P(half, top - hHeel), bl = P(-half, top - hToe);
			k.Card(bl, br, tr, tl, b * Vector3.Back, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
			// the teeth: a row of small triangles under the edge
			const int teeth = 22;
			for (int i = 0; i < teeth; i++)
			{
				float x0 = Mathf.Lerp(-half, half, i / (float)teeth), x1 = Mathf.Lerp(-half, half, (i + 1) / (float)teeth);
				float e0 = top - Mathf.Lerp(hToe, hHeel, (x0 + half) / (2f * half)), e1 = top - Mathf.Lerp(hToe, hHeel, (x1 + half) / (2f * half));
				Vector3 a = P(x0, e0), c = P(x1, e1), tip = P((x0 + x1) * 0.5f, (e0 + e1) * 0.5f - 0.007f);
				k.Tri(a, c, tip, b * Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.Down);
				k.Tri(a, c, tip, b * Vector3.Forward, Vector2.Zero, Vector2.Right, Vector2.Down);
			}
			// its hole at the toe for the peg
			Peg(o.X - 0.21f, o.Y + 0.02f);
			// the grip: a closed D of wood round the hand-hole, two brass screws through it
			k.Mat(wood);
			k.Color = new Color(0.75f, 0.6f, 0.45f);
			float gx = half - 0.02f;
			Vector3 g(float x, float y) => P(gx + x, top - 0.01f + y);
			k.Beam(g(-0.02f, 0.01f), g(0.1f, 0.01f), 0.024f, 0.028f, 4f, b * Vector3.Back);         // the top of the D
			k.Beam(g(-0.01f, -0.12f), g(0.09f, -0.12f), 0.024f, 0.028f, 4f, b * Vector3.Back);      // its foot
			k.Beam(g(0.1f, 0.01f), g(0.12f, -0.06f), 0.024f, 0.03f, 4f, b * Vector3.Back);          // the back, round
			k.Beam(g(0.12f, -0.06f), g(0.09f, -0.12f), 0.024f, 0.03f, 4f, b * Vector3.Back);
			k.Beam(g(-0.02f, 0.01f), g(-0.01f, -0.12f), 0.024f, 0.035f, 4f, b * Vector3.Back);       // the cheek over the blade
			k.Mat(BuildingTextures.Plain("h_brass", new Color(0.45f, 0.36f, 0.18f), 0.4f));
			k.Color = Colors.White;
			foreach (float sy in new[] { -0.03f, -0.08f })
				k.Cylinder(P(gx - 0.015f, top - 0.01f + sy, -0.014f), P(gx - 0.015f, top - 0.01f + sy, 0.014f), 0.006f, 0.006f, 6, true);
		}

		// ---- the adjustable wrench: hung by the hole in its handle, the head up
		{
			Vector3 o = new(0.03f, 1.33f, z);
			Peg(o.X, o.Y - 0.1f);
			k.Mat(steel);
			k.Color = new Color(0.78f, 0.76f, 0.74f);
			k.ChamferBox(o + new Vector3(0, -0.01f, 0), new Vector3(0.024f, 0.19f, 0.009f), 0.004f, 3f);
			k.BevelBox(o + new Vector3(0, 0.1f, 0), new Vector3(0.055f, 0.04f, 0.012f), 0.004f, 3f);           // the head
			k.BevelBox(o + new Vector3(-0.018f, 0.135f, 0), new Vector3(0.018f, 0.035f, 0.012f), 0.003f, 3f);  // the fixed jaw
			k.BevelBox(o + new Vector3(0.016f, 0.13f, 0), new Vector3(0.016f, 0.026f, 0.011f), 0.003f, 3f);    // the moving jaw
			k.Cylinder(o + new Vector3(-0.012f, 0.087f, 0.007f), o + new Vector3(0.012f, 0.087f, 0.007f), 0.006f, 0.006f, 8, true);   // the worm
		}

		// ---- the pliers: jaws up, handles spread, oxblood dipped grips
		{
			Vector3 piv = new(0.17f, 1.44f, z);
			Peg(piv.X, piv.Y + 0.065f);
			k.Mat(steel);
			k.Color = new Color(0.72f, 0.7f, 0.68f);
			foreach (int s in new[] { -1, 1 })
			{
				k.Beam(piv, piv + new Vector3(s * 0.006f, 0.055f, 0), 0.012f, 0.008f, 3f, Vector3.Back);
				k.Beam(piv, piv + new Vector3(s * 0.035f, -0.1f, 0), 0.011f, 0.008f, 3f, Vector3.Back);
			}
			k.Cylinder(piv + new Vector3(0, 0, -0.006f), piv + new Vector3(0, 0, 0.007f), 0.009f, 0.009f, 8, true);
			k.Mat(HollowLook.Grip);
			k.Color = Colors.White;
			foreach (int s in new[] { -1, 1 })
				k.Cylinder(piv + new Vector3(s * 0.035f, -0.1f, 0) * 0.42f, piv + new Vector3(s * 0.04f, -0.2f, 0), 0.008f, 0.009f, 6, true);
		}

		// ---- the screwdriver: fluted wooden handle up, the shank down through a hole of its own
		{
			Vector3 o = new(0.3f, 1.4f, z + 0.004f);
			k.Mat(wood);
			k.Color = new Color(0.8f, 0.62f, 0.42f);
			k.Cylinder(o, o + new Vector3(0, 0.11f, 0), 0.013f, 0.015f, 6, true);
			k.Cylinder(o + new Vector3(0, 0.11f, 0), o + new Vector3(0, 0.12f, 0), 0.015f, 0.011f, 6, true);
			k.Mat(steel);
			k.Color = new Color(0.8f, 0.78f, 0.76f);
			k.Cylinder(o + new Vector3(0, -0.005f, 0), o + new Vector3(0, 0.005f, 0), 0.011f, 0.011f, 8, true);   // the ferrule
			k.Cylinder(o + new Vector3(0, -0.15f, 0), o, 0.003f, 0.003f, 6, false);
			k.BevelBox(o + new Vector3(0, -0.155f, 0), new Vector3(0.007f, 0.012f, 0.0015f), 0.0005f, 3f);
			Peg(o.X + 0.022f, o.Y - 0.01f);
		}

		// ---- the rope, coiled and hung on its peg: three loops of a round strand
		k.Mat(BuildingTextures.Plain("s_rope", new Color(0.5f, 0.44f, 0.3f)));
		k.Color = Colors.White;
		for (int loop = 0; loop < 3; loop++)
		{
			float r = 0.12f - loop * 0.008f, lz = face + 0.015f + loop * 0.02f, sag = 0.01f * loop;
			Vector3 Rp(int i) { float a = i * Mathf.Tau / 14f; return new Vector3(0.5f + Mathf.Cos(a) * r, 1.34f - sag + Mathf.Sin(a) * r * 1.08f, lz); }
			for (int i = 0; i < 14; i++) k.Cylinder(Rp(i), Rp(i + 1), 0.016f, 0.016f, 6, false);
		}
		k.Mat(steel);
		k.Color = new Color(0.6f, 0.58f, 0.56f);
		k.Cylinder(new Vector3(0.5f, 1.46f, face - 0.002f), new Vector3(0.5f, 1.46f, face + 0.08f), 0.005f, 0.005f, 6, true);

		// ---- a block plane on the bench's left end, on its side as it should be left
		{
			Vector3 o = new(-0.8f, BenchTop, -1.05f + 0.22f);
			var b = new Basis(Vector3.Up, 0.35f);
			k.Mat(wood);
			k.Color = new Color(0.62f, 0.5f, 0.38f);
			k.BevelBox(o + b * new Vector3(0, 0.03f, 0), b * Vector3.Zero + new Vector3(0.05f, 0.05f, 0.16f), 0.006f, 3f, b);
			k.Mat(steel);
			k.Color = new Color(0.6f, 0.58f, 0.56f);
			k.BevelBox(o + b * new Vector3(0, 0.062f, 0.01f), new Vector3(0.04f, 0.012f, 0.07f), 0.003f, 3f, b * new Basis(Vector3.Right, -0.5f));
			k.Cylinder(o + b * new Vector3(0, 0.06f, -0.05f), o + b * new Vector3(0, 0.075f, -0.05f), 0.012f, 0.012f, 8, true);
		}
	}

	/// <summary>The shovel and the rake leaning in the corner at <paramref name="c0"/>: turned ash handles, a dished
	/// steel blade on its socket, a rake's head of tines.</summary>
	/// <summary>The roles of the Blender tools (fixtures.py): the materials the code below used.</summary>
	private static System.Collections.Generic.Dictionary<string, Material> ToolRoles => new()
	{
		["steel"] = FixtureModels.Tint(HollowLook.ToolSteel, new Color(0.8f, 0.78f, 0.76f)), ["wood"] = FixtureModels.Tint(HollowLook.Handle, new Color(0.8f, 0.64f, 0.46f)),
		["brass"] = BuildingTextures.Plain("h_brass", new Color(0.45f, 0.36f, 0.18f), 0.4f), ["grip"] = HollowLook.Grip,
		["rope"] = BuildingTextures.Plain("s_rope", new Color(0.5f, 0.44f, 0.3f)), ["paint"] = BuildingTextures.Plain("s_gascan", new Color(0.42f, 0.08f, 0.06f), 0.6f),
	};

	private static void LongTools(MeshKit k, Vector3 c0)
	{
		if (FurnitureKit.Add(k, "fx_long_tools", new Transform3D(Basis.Identity, c0), ToolRoles)) return;
		k.Mat(HollowLook.Handle);
		k.Color = new Color(0.85f, 0.8f, 0.75f);
		Vector3 shTop = c0 + new Vector3(-0.28f, 1.25f, -0.12f), shFoot = c0 + new Vector3(0.0f, 0.28f, 0.0f);
		k.Cylinder(shFoot, shTop, 0.018f, 0.016f, 7, true);
		k.Cylinder(shTop, shTop + (shTop - shFoot).Normalized() * 0.04f, 0.016f, 0.03f, 7, true);   // the knob
		Vector3 rkFoot = c0 + new Vector3(-0.1f, 0.02f, -0.18f), rkTop = c0 + new Vector3(-0.35f, 1.4f, -0.3f);
		k.Cylinder(rkFoot + Vector3.Up * 0.02f, rkTop, 0.016f, 0.015f, 7, true);
		k.Mat(HollowLook.ToolSteel);
		k.Color = new Color(0.7f, 0.66f, 0.62f);
		{
			// the blade: dished, square-mouthed, its socket up the handle
			var bb = Basis.FromEuler(new Vector3(0.1f, 0.4f, 0.2f));
			Vector3 bc = c0 + new Vector3(0.02f, 0.15f, 0.01f);
			Vector3 Bp(float x, float y, float z) => bc + bb * new Vector3(x, y, z);
			for (int i = 0; i < 4; i++)
			{
				float x0 = -0.11f + i * 0.055f, x1 = x0 + 0.055f;
				float d0 = 0.022f * (1f - (x0 / 0.11f) * (x0 / 0.11f)), d1 = 0.022f * (1f - (x1 / 0.11f) * (x1 / 0.11f));
				k.Card(Bp(x0, -0.14f, d0), Bp(x1, -0.14f, d1), Bp(x1 * 0.85f, 0.13f, d1), Bp(x0 * 0.85f, 0.13f, d0), bb * Vector3.Back,
					new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, 0.28f), new Vector2(x0, 0.28f));
			}
			k.Cylinder(Bp(0, 0.12f, 0.008f), shFoot, 0.022f, 0.019f, 7, false);
		}
		{
			// the rake stands tines up: its head across the top, the tines
			Vector3 dir = (rkTop - rkFoot).Normalized();
			Vector3 head = rkTop + dir * 0.015f;
			var hb = new Basis(Vector3.Up, 0.5f);
			k.Cylinder(head + hb * new Vector3(-0.18f, 0, 0), head + hb * new Vector3(0.18f, 0, 0), 0.012f, 0.012f, 6, true);
			for (int t = 0; t < 10; t++)
			{
				Vector3 tp = head + hb * new Vector3(-0.17f + t * 0.038f, 0, 0);
				k.Cylinder(tp, tp + hb * new Vector3(0, 0.07f, 0.03f), 0.004f, 0.003f, 4, false);
			}
		}
	}

	/// <summary>A red gas can (eased box, a handle across its top, the spout and its cap).</summary>
	private static void GasCan(MeshKit k, Vector3 at, float yaw)
	{
		var b = new Basis(Vector3.Up, yaw);
		if (FurnitureKit.Add(k, "fx_gas_can", new Transform3D(b, at), ToolRoles)) return;
		k.Mat(BuildingTextures.Plain("s_gascan", new Color(0.42f, 0.08f, 0.06f), 0.6f));
		k.Color = Colors.White;
		k.BevelBox(at + b * new Vector3(0, 0.15f, 0), new Vector3(0.2f, 0.3f, 0.3f), 0.025f, 2f, b);
		k.Beam(at + b * new Vector3(0, 0.335f, -0.09f), at + b * new Vector3(0, 0.335f, 0.05f), 0.03f, 0.02f, 2f);
		foreach (float hz in new[] { -0.09f, 0.05f })
			k.Beam(at + b * new Vector3(0, 0.29f, hz), at + b * new Vector3(0, 0.335f, hz), 0.025f, 0.02f, 2f);
		k.Mat(HollowLook.ToolSteel);
		k.Color = new Color(0.6f, 0.55f, 0.5f);
		k.Cylinder(at + b * new Vector3(0, 0.29f, 0.1f), at + b * new Vector3(0, 0.36f, 0.14f), 0.022f, 0.016f, 8, true);
	}
}
