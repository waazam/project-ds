using Godot;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// (2026-10-14c, the graphics pass: "cabin wall and door shouldn't be the same texture") The plain steel service door the
/// underground uses: the janitor's closet at the end of Act 15's hall (Act 16) and the same door shut behind them at the
/// top of the sewer (Act 17). Before, each was one flat box in the stairwell's structural steel (a 32 px texture whose
/// rust chips, stretched over a whole door, read as big red polka dots), in a bare hole in a wall of the same stuff.
///
/// Now three materials that read apart: the leaf in an old green-grey paint over a scanned steel sheet (its rust coming
/// through), the hollow-metal frame round it in a darker, scuffed brown-grey, and dull steel hardware. The leaf has its
/// rolled edges, a kick plate, three hinges and a lever on a rose; the frame has its face trim on both sides of the wall,
/// its soffit through the wall and a stop the leaf closes against.
/// </summary>
public static class ServiceDoor
{
	public static StandardMaterial3D LeafMat => _leaf ??= Paint("svc_door_leaf", new Color(0.34f, 0.38f, 0.34f), 1f, 0.82f);
	public static StandardMaterial3D FrameMat => _frame ??= Paint("svc_door_frame", new Color(0.24f, 0.22f, 0.2f), 1.7f, 0.9f);
	public static StandardMaterial3D HardwareMat => _hw ??= MakeHardware();
	private static StandardMaterial3D _leaf, _frame, _hw;

	private static StandardMaterial3D Paint(string name, Color tint, float scale, float rough)
	{
		var m = new StandardMaterial3D
		{
			ResourceName = name, AlbedoColor = tint, Roughness = rough, MetallicSpecular = 0.35f, VertexColorUseAsAlbedo = true,
			Uv1Triplanar = true, Uv1WorldTriplanar = false, Uv1TriplanarSharpness = 8f, Uv1Scale = Vector3.One * scale,
		};
		// the painted sheet with its rust (Poly Haven's Rusty Metal 02); a plain dark paint if the scan is missing
		return SurfaceSets.Scan(m, "rusty_metal_02", 1f, 0.8f, tint);
	}

	private static StandardMaterial3D MakeHardware()
	{
		var m = new StandardMaterial3D
		{
			ResourceName = "svc_door_hw", AlbedoColor = new Color(0.62f, 0.6f, 0.56f), Metallic = 0.75f, Roughness = 0.55f,
			VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1Scale = Vector3.One * 3f,
		};
		return SurfaceSets.Scan(m, "metal_plate_02", 1f, 0.6f, new Color(0.95f, 0.92f, 0.88f));
	}

	/// <summary>
	/// The leaf in <paramref name="leaf"/>'s and <paramref name="hw"/>'s space: x from 0 (the hinge side) to
	/// <paramref name="w"/>, y from 0 to <paramref name="h"/>, z centred on 0, <paramref name="t"/> thick. The lever is
	/// on both faces, <paramref name="handleX"/> from the hinge side.
	/// </summary>
	public static void Leaf(MeshKit leaf, MeshKit hw, float w, float h, float t, float handleY = 1.0f, int seed = 0)
	{
		// (2026-10-15) modelled in Blender (tools/Blender/kits.py: svc_leaf at 1.0 x 2.1 x 0.05 scaled to the leaf, svc_lever
		// mirrored for the far face, svc_kick, svc_hinge), in the kits' own materials; the boxes below are the fallback
		if (FurnitureKit.Has("svc_leaf") && FurnitureKit.Has("svc_lever") && FurnitureKit.Has("svc_kick") && FurnitureKit.Has("svc_hinge"))
		{
			Material lm = leaf.CurrentMaterial, hm = hw.CurrentMaterial;
			FurnitureKit.Add(leaf, "svc_leaf", new Transform3D(Basis.FromScale(new Vector3(w, h / 2.1f, t / 0.05f)), Vector3.Zero),
				FurnitureKit.Tinted(("leaf", lm, Colors.White), ("edge", lm, Colors.White * 0.86f)));
			leaf.Mat(lm);
			var roles = FurnitureKit.Tinted(("hw", hm, Colors.White), ("grip", hm, Colors.White * 0.25f));
			foreach (float s in new[] { -1f, 1f })
			{
				FurnitureKit.Add(hw, "svc_kick", new Transform3D(Basis.FromScale(new Vector3((w - 0.1f) / 0.9f, 1f, s)), new Vector3(w * 0.5f, 0.16f, s * (t * 0.5f + 0.003f))), roles);
				FurnitureKit.Add(hw, "svc_lever", new Transform3D(Basis.FromScale(new Vector3(1f, 1f, s)), new Vector3(w - 0.09f, handleY, s * t * 0.5f)), roles);
			}
			foreach (float y in new[] { 0.25f, h * 0.5f + 0.05f, h - 0.25f })
				FurnitureKit.Add(hw, "svc_hinge", new Transform3D(Basis.Identity, new Vector3(-0.004f, y, 0)), roles);
			hw.Mat(hm);
			hw.Color = Colors.White;
			return;
		}
		leaf.Color = Colors.White;
		// the slab, and its rolled edges a hair proud all round (so its outline catches the light, not a cut box)
		BuildKit.Box(leaf, new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w - 0.03f, h - 0.03f, t));
		leaf.Color = Colors.White * 0.86f;
		float e = 0.022f, et = t + 0.006f;
		BuildKit.Box(leaf, new Vector3(e * 0.5f, h * 0.5f, 0), new Vector3(e, h, et));
		BuildKit.Box(leaf, new Vector3(w - e * 0.5f, h * 0.5f, 0), new Vector3(e, h, et));
		BuildKit.Box(leaf, new Vector3(w * 0.5f, h - e * 0.5f, 0), new Vector3(w - 2f * e, e, et));
		BuildKit.Box(leaf, new Vector3(w * 0.5f, e * 0.5f, 0), new Vector3(w - 2f * e, e, et));
		hw.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
		{
			float fz = s * (t * 0.5f + 0.003f);
			// a kick plate, scuffed by boots
			BuildKit.Box(hw, new Vector3(w * 0.5f, 0.16f, fz), new Vector3(w - 0.1f, 0.25f, 0.003f));
			// the lever on its rose
			float hx = w - 0.09f;
			hw.Cylinder(new Vector3(hx, handleY, s * t * 0.5f), new Vector3(hx, handleY, s * (t * 0.5f + 0.012f)), 0.032f, 0.03f, 14, true);
			hw.Cylinder(new Vector3(hx, handleY, s * (t * 0.5f + 0.012f)), new Vector3(hx, handleY, s * (t * 0.5f + 0.05f)), 0.011f, 0.011f, 8, false);
			hw.Cylinder(new Vector3(hx + 0.006f, handleY, s * (t * 0.5f + 0.055f)), new Vector3(hx - 0.115f, handleY - 0.004f, s * (t * 0.5f + 0.06f)), 0.012f, 0.01f, 8, true);
			// the key's escutcheon under it
			hw.Cylinder(new Vector3(hx, handleY - 0.14f, s * t * 0.5f), new Vector3(hx, handleY - 0.14f, s * (t * 0.5f + 0.006f)), 0.02f, 0.019f, 10, true);
		}
		// three hinges' knuckles down the hinge edge
		foreach (float y in new[] { 0.25f, h * 0.5f + 0.05f, h - 0.25f })
			hw.Cylinder(new Vector3(-0.004f, y - 0.06f, 0), new Vector3(-0.004f, y + 0.06f, 0), 0.011f, 0.011f, 8, true);
	}

	/// <summary>
	/// The frame round an opening <paramref name="w"/> wide and <paramref name="h"/> high whose foot's middle is at
	/// <paramref name="foot"/>, through a wall from z = <paramref name="z0"/> to <paramref name="z1"/>, in <paramref name="k"/>'s
	/// space. Face trim stands <paramref name="proud"/> off both faces; the soffit lines the opening a few mm inside its cut
	/// edge (no faces in the wall's planes); a stop strip runs round at <paramref name="stopZ"/>.
	/// </summary>
	public static void Frame(MeshKit k, Vector3 foot, float w, float h, float z0, float z1, float proud = 0.014f, float face = 0.055f, float? stopZ = null)
	{
		k.Color = Colors.White;
		float d = z1 - z0 + 2f * proud, zc = (z0 + z1) * 0.5f, inset = 0.006f;
		float xl = foot.X - w * 0.5f, xr = foot.X + w * 0.5f, yt = foot.Y + h;
		// (2026-10-15) modelled in Blender (svc_casing: a moulded trim bar, svc_strip: the soffit and the stop, each 1 m up Y
		// and scaled to its box; across, a bar is turned on its side); the boxes below are the fallback
		if (FurnitureKit.Has("svc_casing") && FurnitureKit.Has("svc_strip"))
		{
			var fm = k.CurrentMaterial;
			void Bar(string model, Vector3 c, Vector3 size, bool across, float flipX, float flipZ, Color tint, Vector2 refXZ)
			{
				var b = across
					? new Basis(Vector3.Back, Mathf.Pi * 0.5f) * Basis.FromScale(new Vector3(flipX * size.Y / refXZ.X, size.X, flipZ * size.Z / refXZ.Y))
					: Basis.FromScale(new Vector3(flipX * size.X / refXZ.X, size.Y, flipZ * size.Z / refXZ.Y));
				FurnitureKit.Add(k, model, new Transform3D(b, c), FurnitureKit.Tinted(("frame", fm, tint)));
			}
			var strip = new Vector2(0.02f, 0.016f);
			var casing = new Vector2(0.055f, 0.014f);
			Bar("svc_strip", new Vector3(xl + inset * 0.5f + 0.004f, foot.Y + h * 0.5f, zc), new Vector3(inset + 0.008f, h, d), false, 1f, 1f, Colors.White, strip);
			Bar("svc_strip", new Vector3(xr - inset * 0.5f - 0.004f, foot.Y + h * 0.5f, zc), new Vector3(inset + 0.008f, h, d), false, 1f, 1f, Colors.White, strip);
			Bar("svc_strip", new Vector3(foot.X, yt - inset * 0.5f - 0.004f, zc), new Vector3(w, inset + 0.008f, d), true, 1f, 1f, Colors.White, strip);
			foreach (var (zf, out_) in new[] { (z0 - proud * 0.5f, -1f), (z1 + proud * 0.5f, 1f) })
			{
				var tc = Colors.White * 0.95f;
				// (the bead on the outer edge of each: the jambs' away from the opening, the head's along its top)
				Bar("svc_casing", new Vector3(xl - face * 0.5f + inset, foot.Y + (h + face) * 0.5f, zf), new Vector3(face, h + face, proud), false, 1f, out_, tc, casing);
				Bar("svc_casing", new Vector3(xr + face * 0.5f - inset, foot.Y + (h + face) * 0.5f, zf), new Vector3(face, h + face, proud), false, -1f, out_, tc, casing);
				Bar("svc_casing", new Vector3(foot.X, yt + face * 0.5f - inset, zf), new Vector3(w + 2f * face - 2f * inset, face, proud + 0.002f), true, -1f, out_, tc, casing);
			}
			if (stopZ is float sz0)
			{
				var tc = Colors.White * 0.8f;
				Bar("svc_strip", new Vector3(xl + 0.018f, foot.Y + h * 0.5f, sz0), new Vector3(0.02f, h, 0.016f), false, 1f, 1f, tc, strip);
				Bar("svc_strip", new Vector3(xr - 0.018f, foot.Y + h * 0.5f, sz0), new Vector3(0.02f, h, 0.016f), false, 1f, 1f, tc, strip);
				Bar("svc_strip", new Vector3(foot.X, yt - 0.018f, sz0), new Vector3(w, 0.02f, 0.016f), true, 1f, 1f, tc, strip);
			}
			k.Mat(fm);
			k.Color = Colors.White;
			return;
		}
		// the soffit: the frame's inside through the wall
		BuildKit.Box(k, new Vector3(xl + inset * 0.5f + 0.004f, foot.Y + h * 0.5f, zc), new Vector3(inset + 0.008f, h, d));
		BuildKit.Box(k, new Vector3(xr - inset * 0.5f - 0.004f, foot.Y + h * 0.5f, zc), new Vector3(inset + 0.008f, h, d));
		BuildKit.Box(k, new Vector3(foot.X, yt - inset * 0.5f - 0.004f, zc), new Vector3(w, inset + 0.008f, d));
		// the face trim on both sides of the wall, mitred at the head (a box each way, the head's overlapping the jambs')
		foreach (float zf in new[] { z0 - proud * 0.5f, z1 + proud * 0.5f })
		{
			k.Color = Colors.White * 0.95f;
			BuildKit.Box(k, new Vector3(xl - face * 0.5f + inset, foot.Y + (h + face) * 0.5f, zf), new Vector3(face, h + face, proud));
			BuildKit.Box(k, new Vector3(xr + face * 0.5f - inset, foot.Y + (h + face) * 0.5f, zf), new Vector3(face, h + face, proud));
			BuildKit.Box(k, new Vector3(foot.X, yt + face * 0.5f - inset, zf), new Vector3(w + 2f * face - 2f * inset, face, proud + 0.002f));
		}
		// the stop the leaf closes against
		if (stopZ is float sz)
		{
			k.Color = Colors.White * 0.8f;
			BuildKit.Box(k, new Vector3(xl + 0.018f, foot.Y + h * 0.5f, sz), new Vector3(0.02f, h, 0.016f));
			BuildKit.Box(k, new Vector3(xr - 0.018f, foot.Y + h * 0.5f, sz), new Vector3(0.02f, h, 0.016f));
			BuildKit.Box(k, new Vector3(foot.X, yt - 0.018f, sz), new Vector3(w, 0.02f, 0.016f));
		}
	}
}
