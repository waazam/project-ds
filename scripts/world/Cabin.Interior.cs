using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The cabin's room (2026-10-14h, the Hollow's interiors pass, after the interiors kit's recipe,
/// docs/design/interior-kit.md). The survey found it one orange-brown value from floor to ridge, the logs flat planks,
/// the stove, the crate and the shelf boxes, the roof lit to the ridge. Now each thing has its own surface
/// (<see cref="HollowLook"/>) and value: dark boards underfoot, silver-brown round logs with pale chinking, poles
/// smoked black overhead; a cast-iron box stove with its cook lids, door, draft and cabriole legs; a camp cot of dark
/// hardwood on crossed legs under a grey wool blanket; a shelf on braced brackets; the modelled crate; a braided rag
/// rug; a bucket, a broom, a bundle of newspapers for the stove. The dark baked into the vertices by
/// <see cref="RoomShade"/>: the corners and the joints, the roof black to the ridge, soot over the stove and its pipe and
/// over the lamp, under the furniture, the floor worn from the door to his table. Same places as before for everything
/// the story or a test knows about (the stove, the cot, the shelf and the knockables, the crate, the woodpile).
/// </summary>
public partial class Cabin
{
	private void BuildInterior(MeshKit k, MeshKit lk, MeshKit small, List<(Vector3, Vector3, Basis)> cols)
	{
		float ix = Hw - LogT * 0.5f, iz = Hd - LogT * 0.5f;
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 31 + 3) };

		// ---- floor: planks run front to back (the lining: tessellated for the bake)
		lk.Mat(HollowLook.CabinFloor);
		lk.Color = new Color(0.95f, 0.9f, 0.85f);
		BuildKit.Box(lk, new Vector3(0, -0.03f, 0), new Vector3(iz * 2f + LogBulge * 2f, 0.06f, ix * 2f + LogBulge * 2f), 1f / 0.6f, BuildKit.Face.NY, new Basis(Vector3.Up, Mathf.Pi * 0.5f));
		lk.Color = Colors.White;

		// ---- the poles overhead: rafters down both slopes, the ridge pole, the purlin the lamp hangs from, two ties
		k.Mat(HollowLook.SmokedPole);
		float cos = 1f / Mathf.Sqrt(1f + Pitch * Pitch);
		for (float x = -ix + 0.3f; x <= ix - 0.2f; x += 0.75f)
			foreach (int s in new[] { -1, 1 })
			{
				float drop = 0.075f / cos;
				Vector3 a = new(x, RoofBottomAt(iz) - drop, s * (iz + 0.05f)), b = new(x, RidgeY - drop - 0.06f, 0);
				k.Color = new Color(0.9f, 0.88f, 0.86f) * rng.RandfRange(0.85f, 1.05f);
				k.Cylinder(a, b, 0.068f, 0.06f, 7, false, 0.35f, rng.Randf());
			}
		k.Color = Colors.White;
		k.Cylinder(new Vector3(-ix - 0.05f, RidgeY - 0.15f, 0), new Vector3(ix + 0.05f, RidgeY - 0.15f, 0), 0.1f, 0.095f, 9, false, 0.35f);
		float pz = -0.75f;
		float py = RoofBottomAt(pz) - 0.26f;
		k.Cylinder(new Vector3(-ix - 0.05f, py, pz), new Vector3(ix + 0.05f, py, pz), 0.085f, 0.08f, 9, false, 0.35f, 0.7f);
		foreach (float x in new[] { -1.2f, 1.2f })
			k.Cylinder(new Vector3(x, WallHeight - 0.08f, -iz - 0.05f), new Vector3(x, WallHeight - 0.08f, iz + 0.05f), 0.08f, 0.085f, 9, false, 0.35f, x);

		// ---- the stove (left wall, in front of the chimney) and its pipe into the wall
		float sx = -ix + 0.42f, sz = -1.0f;
		BuildStove(k, sx, sz, ix);
		cols.Add((new Vector3(sx, 0.4f, sz), new Vector3(0.56f, 0.8f, 0.68f), Basis.Identity));

		// woodpile beside the stove (the modelled one: split logs in an iron cradle; the owner's fidelity pass, 2026-09-30)
		var iron = BuildingTextures.IronMat;
		var roles = new Dictionary<string, Material>
		{
			["bark"] = BuildingTextures.BarkScanMat, ["wood"] = PropTextures.PostMat, ["iron"] = iron, ["metal"] = BuildingTextures.Plain("b_tin", new Color(0.4f, 0.37f, 0.33f), 0.5f),
			["glass"] = BuildingTextures.Plain("b_jar", new Color(0.32f, 0.36f, 0.3f), 0.3f), ["label"] = BuildingTextures.Plain("b_label", new Color(0.55f, 0.5f, 0.4f)),
			["leather"] = BuildingTextures.Plain("b_book", new Color(0.26f, 0.2f, 0.16f)), ["paper"] = BuildingTextures.Plain("b_label", new Color(0.55f, 0.5f, 0.4f)),
			["rubber"] = BuildingTextures.Plain("b_rubber", new Color(0.05f, 0.05f, 0.05f)), ["cloth"] = BuildingTextures.Plain("b_blanket", new Color(0.3f, 0.12f, 0.09f)),
			["ceramic"] = BuildingTextures.Plain("b_enamel", new Color(0.7f, 0.7f, 0.66f), 0.4f),
		};
		bool modelled = FurnitureKit.Add(k, "log_pile", new Vector3(sx + 0.05f, 0f, sz + 0.78f), 0f, roles);
		k.Mat(BuildingTextures.RoundLogMat);
		for (int i = 0; i < (modelled ? 0 : 6); i++)
		{
			int row = i < 3 ? 0 : (i < 5 ? 1 : 2);
			float lx = sx - 0.05f + (i % 3 - 1) * 0.14f + row * 0.07f;
			if (row == 1) lx = sx - 0.05f + (i - 3.5f) * 0.14f;
			if (row == 2) lx = sx - 0.05f;
			float ly = 0.06f + row * 0.11f;
			float lz = sz + 0.72f + rng.RandfRange(-0.04f, 0.04f);
			float sh = rng.RandfRange(0.8f, 1.05f);
			k.Color = new Color(sh, sh * 0.95f, sh * 0.9f);
			k.Cylinder(new Vector3(lx, ly, lz - 0.2f), new Vector3(lx, ly, lz + 0.2f), 0.06f, 0.06f, 5, true, 2.5f, rng.Randf());
		}
		k.Color = Colors.White;

		// ---- the cot along the right wall: dark hardwood rails on crossed legs, canvas, a grey wool blanket
		float cx = ix - 0.42f, cz0 = -iz + 0.1f, cz1 = cz0 + 1.9f, cw = 0.36f;
		BuildCot(k, cx, cz0, cz1, cw);
		cols.Add((new Vector3(cx, 0.28f, (cz0 + cz1) * 0.5f), new Vector3(cw * 2f + 0.06f, 0.56f, cz1 - cz0), Basis.Identity));

		// ---- the shelf on the right wall toward the front, on braced brackets, with jars, tins and books
		float shx = ix - 0.14f, shz0 = 0.35f, shz1 = 1.55f;
		k.Mat(HollowLook.ShelfWood);
		k.Color = new Color(0.9f, 0.88f, 0.86f);
		foreach (float y in new[] { 1.05f, 1.5f })
		{
			k.BevelBox(new Vector3(shx, y, (shz0 + shz1) * 0.5f), new Vector3(0.26f, 0.032f, shz1 - shz0), 0.008f, 1.4f);
			foreach (float z in new[] { shz0 + 0.12f, shz1 - 0.12f })
			{
				// a cleat on the log, a brace up under the board's front
				k.ChamferBox(new Vector3(ix - 0.005f, y - 0.13f, z), new Vector3(0.035f, 0.22f, 0.045f), 0.006f, 1.4f);
				k.Beam(new Vector3(ix - 0.02f, y - 0.22f, z), new Vector3(shx - 0.09f, y - 0.02f, z), 0.035f, 0.03f, 1.4f, Vector3.Forward);
			}
		}
		k.Color = Colors.White;
		var glass = BuildingTextures.Plain("b_jar", new Color(0.32f, 0.36f, 0.3f), 0.3f);
		var tin = BuildingTextures.Plain("b_tin", new Color(0.4f, 0.37f, 0.33f), 0.5f);
		var book = BuildingTextures.Plain("b_book", new Color(0.26f, 0.2f, 0.16f));
		if (modelled)
		{
			// the modelled jars and tins, a row of books, a mug on the stove's top, boots by the door, a coat on a peg
			// (2026-10-15, the scan: jar, tin, jar, tin, 14 cm apart in a line; now set down as they were last put back:
			// uneven gaps, a gap where one was taken, some pushed to the back, turned any way)
			var jr = new RandomNumberGenerator { Seed = 5063 };
			string[] kinds = { "jar", "jar", "tin", "jar", "tin" };
			float jz = shz0 + 0.1f;
			for (int i = 0; i < kinds.Length && jz < shz0 + 0.72f; i++)
			{
				if (i == 2) { jz += 0.13f; continue; }   // (the one taken)
				FurnitureKit.Add(small, kinds[(i + 1) % kinds.Length], new Vector3(shx - 0.02f + jr.RandfRange(-0.04f, 0.03f), 1.065f, jz), jr.RandfRange(0f, Mathf.Tau), roles);
				jz += jr.RandfRange(0.1f, 0.17f);
			}
			FurnitureKit.Add(small, "books_row_b", new Vector3(shx - 0.01f, 1.515f, shz0 + 0.85f), Mathf.Pi * 0.5f, roles);
			FurnitureKit.Add(small, "tin", new Vector3(shx - 0.02f, 1.515f, shz0 + 0.2f), 0.4f, roles);
			FurnitureKit.Add(small, "mug", new Vector3(sx + 0.12f, 0.765f, sz + 0.18f), 2.2f, roles);
			FurnitureKit.Add(k, "boots", new Vector3(ix - 0.45f, 0.0f, iz - 0.4f), 2.6f, roles);
			FurnitureKit.Add(k, "coat_on_hook", new Vector3(-ix + 0.02f, 1.8f, iz - 0.95f), -Mathf.Pi * 0.5f, roles);   // (on the side wall: the front one is all door and window)
			// (2026-10-10) loose on the floor, to be knocked over: a tin under the shelf, a bottle by the boots
			var r = roles;
			Callable.From(() =>
			{
				Knockable.Put(this, "tin", r, Knockable.Sound.Tin, new Vector3(shx - 0.3f, 0.03f, shz1 + 0.15f), 0.3f);
				Knockable.Put(this, "bottle_whiskey", r, Knockable.Sound.Glass, new Vector3(ix - 0.75f, 0.03f, iz - 0.3f), 1.1f);
			}).CallDeferred();
		}
		for (int i = 0; i < (modelled ? 0 : 4); i++)
		{
			float z = shz0 + 0.15f + i * 0.14f;
			small.Mat(i % 2 == 0 ? glass : tin);
			small.Color = Colors.White;
			small.Cylinder(new Vector3(shx - 0.02f, 1.065f, z), new Vector3(shx - 0.02f, 1.065f + (i % 2 == 0 ? 0.17f : 0.11f), z), 0.045f, 0.045f, 8, true);
		}
		small.Mat(book);
		for (int i = 0; i < (modelled ? 0 : 5); i++)
		{
			float sh = rng.RandfRange(0.7f, 1.2f);
			small.Color = new Color(sh, sh * rng.RandfRange(0.8f, 1f), sh * 0.8f);
			float h = rng.RandfRange(0.17f, 0.24f);
			BuildKit.Box(small, new Vector3(shx - 0.01f, 1.515f + h * 0.5f, shz0 + 0.55f + i * 0.055f), new Vector3(0.17f, h, 0.045f), 3f, BuildKit.Face.NY, new Basis(Vector3.Right, i == 4 ? 0.3f : 0f));
		}
		small.Mat(tin);
		small.Color = Colors.White;
		if (!modelled) small.Cylinder(new Vector3(shx - 0.02f, 1.515f, shz0 + 0.2f), new Vector3(shx - 0.02f, 1.63f, shz0 + 0.2f), 0.06f, 0.055f, 8, true);

		// ---- the crate in the front-left corner (the modelled one, pale scrubbed deal)
		var crateAt = new Vector3(-ix + 0.34f, 0f, iz - 0.45f);
		var crateRoles = new Dictionary<string, Material> { ["*"] = HollowLook.CrateWood, ["wood"] = HollowLook.CrateWood, ["metal"] = iron, ["iron"] = iron };
		bool crateModel = false;
		if (FurnitureKit.Bounds("crate") is { } cb && cb.Size.X > 0.01f)
		{
			var scale = new Vector3(0.55f / cb.Size.X, 0.5f / cb.Size.Y, 0.5f / cb.Size.Z);
			crateModel = FurnitureKit.Add(k, "crate", new Transform3D(new Basis(Vector3.Up, 0.12f) * Basis.FromScale(scale), crateAt - new Vector3(0, cb.Position.Y * scale.Y, 0)), crateRoles);
		}
		if (!crateModel)
		{
			k.Mat(HollowLook.CrateWood);
			k.Color = new Color(0.9f, 0.86f, 0.8f);
			k.BevelBox(crateAt + new Vector3(0, 0.25f, 0), new Vector3(0.55f, 0.5f, 0.5f), 0.015f, 1.8f, new Basis(Vector3.Up, 0.12f));
		}
		cols.Add((crateAt + new Vector3(0, 0.25f, 0), new Vector3(0.55f, 0.5f, 0.5f), new Basis(Vector3.Up, 0.12f)));

		// ---- the braided rag rug inside the door: rings of old cloth, an oval
		BraidedRug(small, new Vector3(0, 0.004f, iz - 0.78f), 0.68f, 0.44f, -0.05f);

		// ---- what's left about: a bucket in the back corner by the stove, a broom against the front wall, newspapers
		// tied up for the stove
		var card = BuildingTextures.Plain("h_clutter", new Color(0.5f, 0.46f, 0.38f));
		ClutterKit.Bucket(iron).Build(small, new Transform3D(Basis.Identity, new Vector3(-ix + 0.26f, 0f, -iz + 0.3f)));
		ClutterKit.Broom(PropTextures.PostMat, card).Build(small, new Transform3D(new Basis(Vector3.Right, -0.2f) * new Basis(Vector3.Up, 0.4f), new Vector3(0.88f, 0f, iz - 0.3f)));
		var papers = ClutterKit.Bundle(card, card, new Vector3(0.36f, 0.16f, 0.27f), new Color(0.82f, 0.78f, 0.66f));
		papers.Build(small, new Transform3D(new Basis(Vector3.Up, 0.3f), new Vector3(-ix + 0.3f, 0f, 0.42f)));
		papers.Build(small, new Transform3D(new Basis(Vector3.Up, -0.2f), new Vector3(-ix + 0.32f, 0.16f, 0.4f)));

		// ---- the bake
		var lamp = new Vector3(0.3f, 1.95f, -0.75f);
		var shade = new RoomShade(-ix, ix, -iz, iz, 0f, RidgeY) { Dim = 0.6f, Ceiling = 0.5f, Reach = 0.9f, Seam = 0.5f, Head = 0.15f };
		// the roof's two slopes are the room's ceiling planes
		float sn = Pitch / Mathf.Sqrt(1f + Pitch * Pitch);
		foreach (int s in new[] { -1, 1 })
			shade.Plane(new Vector3(0, RoofBottomAt(0f), 0), new Vector3(0, -cos, -s * sn));
		shade.Under(new Vector3(sx, 0, sz), new Vector3(0.6f, 0.8f, 0.72f), 0.5f, 0.5f)
			.Under(new Vector3(cx, 0, (cz0 + cz1) * 0.5f), new Vector3(cw * 2f + 0.1f, 0.5f, cz1 - cz0), 0.5f, 0.45f)
			.Under(new Vector3(0.3f, 0, -1.6f), new Vector3(1.1f, 0.75f, 0.7f), 0.4f, 0.4f)
			.Under(crateAt, new Vector3(0.6f, 0.5f, 0.55f), 0.45f, 0.4f)
			.Under(new Vector3(sx + 0.05f, 0, sz + 0.78f), new Vector3(0.5f, 0.4f, 0.4f), 0.4f, 0.35f)
			.Under(new Aabb(new Vector3(shx - 0.13f, 0.95f, shz0), new Vector3(0.26f, 0.7f, shz1 - shz0)), 0.35f, 0.3f)
			.Soot(new Vector3(sx, 0.76f, sz), 0.55f, 0.55f).Soot(new Vector3(-ix, 1.75f, sz), 0.45f, 0.6f).Soot(lamp + Vector3.Up * 0.15f, 0.4f, 0.45f)
			.Light(lamp, 3.4f).Light(new Vector3(0, 1.0f, iz), 1.6f, 0.5f)
			.Damp(new Vector3(-WinCx, _winBot, iz), 0.9f, 0.6f).Damp(new Vector3(WinCx, _winBot, iz), 0.9f, 0.5f)
			.Path(new Vector3(0, 0, iz), new Vector3(0.3f, 0, -1.0f), 0.8f, 0.7f)
			.Path(new Vector3(0, 0, iz - 0.5f), new Vector3(sx + 0.4f, 0, sz), 0.6f, 0.5f);
		shade.Apply(lk);
		shade.Apply(k);
		shade.Apply(small);

		// ---- a few stains by hand (the walls are logs: no plaster rules)
		if (!Engine.IsEditorHint())
			Callable.From(() =>
			{
				if (!IsInstanceValid(_gen)) return;
				var dress = new Node3D { Name = "Stains" };
				_gen.AddChild(dress);
				DecalDresser.Place(dress, "soot", new Vector3(-ix + 0.02f, 1.95f, sz), Vector3.Right, new Vector2(0.8f, 1.1f), 0.75f, 0f, 0.1f);
				DecalDresser.Place(dress, "floor", new Vector3(0.1f, 0f, iz - 1.6f), Vector3.Up, new Vector2(1.3f, 1.0f), 0.55f, 0.4f, 0.08f);
				DecalDresser.Place(dress, "floor", new Vector3(-0.9f, 0f, -0.6f), Vector3.Up, new Vector2(1.1f, 1.2f), 0.45f, 1.9f, 0.08f);
				DecalDresser.Place(dress, "scuff", new Vector3(sx + 0.45f, 0f, sz + 0.1f), Vector3.Up, new Vector2(0.7f, 0.6f), 0.5f, 0.7f, 0.08f);
			}).CallDeferred();
	}

	/// <summary>A cast-iron box stove, its front (the door) to +X: a body with eased edges on an ash lip, a top plate
	/// with two round cook lids, cast ribs down its sides, a door with a raised frame, hinge pins, a spring-coil handle,
	/// a round draft under it, four splayed cabriole legs; the pipe up and into the wall.</summary>
	private void BuildStove(MeshKit k, float sx, float sz, float ix)
	{
		var iron = HollowLook.StoveIron;
		k.Mat(iron);
		var dark = new Color(0.62f, 0.6f, 0.58f);
		k.Color = dark;
		var c = new Vector3(sx, 0, sz);
		k.BevelBox(c + new Vector3(0, 0.47f, 0), new Vector3(0.5f, 0.5f, 0.62f), 0.025f, 2f);              // the firebox
		k.BevelBox(c + new Vector3(0, 0.215f, 0), new Vector3(0.56f, 0.03f, 0.68f), 0.01f, 2f);            // the ash lip
		k.Color = new Color(0.72f, 0.66f, 0.6f);
		k.BevelBox(c + new Vector3(0, 0.735f, 0), new Vector3(0.58f, 0.03f, 0.7f), 0.008f, 2f);            // the top plate (rusted)
		foreach (float dz in new[] { -0.15f, 0.15f })
		{
			k.Cylinder(c + new Vector3(0.02f, 0.75f, dz), c + new Vector3(0.02f, 0.757f, dz), 0.095f, 0.09f, 12, true);
			k.Beam(c + new Vector3(0.02f, 0.759f, dz - 0.03f), c + new Vector3(0.02f, 0.759f, dz + 0.03f), 0.012f, 0.008f, 3f);   // the lifter's notch
		}
		k.Color = dark;
		// cast ribs down the sides
		foreach (int s in new[] { -1, 1 })
			for (int r = 0; r < 3; r++)
				k.BevelBox(c + new Vector3(0, 0.34f + r * 0.13f, s * 0.315f), new Vector3(0.44f, 0.022f, 0.016f), 0.006f, 2f);
		// the door (faces +X into the room): a raised frame round a panel
		float fx = sx + 0.252f;
		k.BevelBox(new Vector3(fx + 0.004f, 0.48f, sz), new Vector3(0.016f, 0.28f, 0.32f), 0.004f, 2f);
		k.Color = new Color(0.5f, 0.48f, 0.46f);
		foreach (float dz in new[] { -0.14f, 0.14f }) k.BevelBox(new Vector3(fx + 0.016f, 0.48f, sz + dz), new Vector3(0.012f, 0.26f, 0.024f), 0.003f, 2f);
		foreach (float dy in new[] { -0.12f, 0.12f }) k.BevelBox(new Vector3(fx + 0.016f, 0.48f + dy, sz), new Vector3(0.012f, 0.024f, 0.3f), 0.003f, 2f);
		// hinge pins on its near edge, a spring-coil handle on the far
		foreach (float dy in new[] { -0.09f, 0.09f })
			k.Cylinder(new Vector3(fx + 0.012f, 0.48f + dy - 0.03f, sz - 0.165f), new Vector3(fx + 0.012f, 0.48f + dy + 0.03f, sz - 0.165f), 0.012f, 0.012f, 6, true);
		k.Cylinder(new Vector3(fx + 0.02f, 0.5f, sz + 0.12f), new Vector3(fx + 0.06f, 0.5f, sz + 0.12f), 0.006f, 0.006f, 5, false);
		k.Color = new Color(0.8f, 0.78f, 0.76f);
		for (int i = 0; i < 5; i++)
			k.Cylinder(new Vector3(fx + 0.06f, 0.5f, sz + 0.09f + i * 0.012f), new Vector3(fx + 0.06f, 0.5f, sz + 0.1f + i * 0.012f), 0.013f, 0.013f, 7, false);
		k.Color = dark;
		// the round draft under the door
		k.Cylinder(new Vector3(fx, 0.29f, sz), new Vector3(fx + 0.018f, 0.29f, sz), 0.05f, 0.05f, 12, true);
		k.Beam(new Vector3(fx + 0.019f, 0.29f, sz - 0.035f), new Vector3(fx + 0.019f, 0.29f, sz + 0.035f), 0.006f, 0.01f, 3f);
		// splayed legs, thick at the knee, out to a pad
		foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
		{
			var knee = c + new Vector3(lx * 0.2f, 0.205f, lz * 0.255f);
			var ankle = c + new Vector3(lx * 0.235f, 0.06f, lz * 0.3f);
			var foot = c + new Vector3(lx * 0.25f, 0.012f, lz * 0.315f);
			k.Cylinder(knee, ankle, 0.034f, 0.022f, 7, false);
			k.Cylinder(ankle, foot, 0.022f, 0.03f, 7, false);
			k.Cylinder(foot + Vector3.Down * 0.012f, foot + Vector3.Up * 0.004f, 0.034f, 0.034f, 8, true);
		}
		// the pipe: collar on the stove, up, a joint, the damper's handle, the elbow, along into the wall's thimble
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.33f, 0.31f, 0.3f);
		k.Cylinder(new Vector3(sx - 0.05f, 0.76f, sz), new Vector3(sx - 0.05f, 1.75f, sz), 0.065f, 0.065f, 12, false);
		k.Cylinder(new Vector3(sx - 0.05f, 0.75f, sz), new Vector3(sx - 0.05f, 0.82f, sz), 0.085f, 0.072f, 12, false);
		k.Cylinder(new Vector3(sx - 0.05f, 1.2f, sz), new Vector3(sx - 0.05f, 1.24f, sz), 0.071f, 0.071f, 12, false);
		k.Beam(new Vector3(sx - 0.05f, 1.0f, sz - 0.075f), new Vector3(sx - 0.05f, 1.0f, sz - 0.14f), 0.012f, 0.012f, 2f);
		k.Cylinder(new Vector3(-ix + 0.02f, 1.75f, sz), new Vector3(-ix + 0.07f, 1.75f, sz), 0.11f, 0.11f, 12, true);
		k.Cylinder(new Vector3(sx - 0.05f, 1.75f, sz), new Vector3(-ix - 0.05f, 1.75f, sz), 0.065f, 0.065f, 12, false);
		k.Cylinder(new Vector3(sx - 0.05f, 1.74f, sz), new Vector3(sx - 0.05f, 1.815f, sz), 0.066f, 0.066f, 12, true);   // the elbow's closed top
		k.Color = Colors.White;
	}

	/// <summary>The cot: two rails and two stretchers of dark hardwood, a pair of crossed legs at each end, the canvas
	/// slung between the rails; the blanket (grey army wool, rumpled, its edges hanging, its turned-back fold rolled) and
	/// a soft dented pillow.</summary>
	private void BuildCot(MeshKit k, float cx, float cz0, float cz1, float cw)
	{
		k.Mat(HollowLook.CotWood);
		k.Color = new Color(0.9f, 0.88f, 0.86f);
		float zm = (cz0 + cz1) * 0.5f, len = cz1 - cz0;
		foreach (int s in new[] { -1, 1 })
			k.ChamferBox(new Vector3(cx + s * cw, 0.36f, zm), new Vector3(0.045f, 0.06f, len + 0.08f), 0.01f, 1.4f, null, 2);
		foreach (float z in new[] { cz0 + 0.04f, cz1 - 0.04f })
		{
			k.ChamferBox(new Vector3(cx, 0.31f, z), new Vector3(cw * 2f - 0.05f, 0.035f, 0.035f), 0.006f, 1.4f, null, 0);
			// the crossed legs, an X under each end
			foreach (int s in new[] { -1, 1 })
				k.Beam(new Vector3(cx - s * (cw - 0.03f), 0.01f, z), new Vector3(cx + s * (cw - 0.03f), 0.34f, z), 0.035f, 0.03f, 1.4f, Vector3.Back);
			k.Cylinder(new Vector3(cx, 0.175f, z - 0.03f), new Vector3(cx, 0.175f, z + 0.03f), 0.012f, 0.012f, 6, true);
		}
		k.Mat(BuildingTextures.CanvasMat);
		k.Color = new Color(0.62f, 0.6f, 0.52f);
		// the canvas sags between the rails
		const int nz = 6;
		for (int i = 0; i < nz; i++)
		{
			float za = Mathf.Lerp(cz0 + 0.02f, cz1 - 0.02f, i / (float)nz), zb = Mathf.Lerp(cz0 + 0.02f, cz1 - 0.02f, (i + 1) / (float)nz);
			Vector3 L(float z) => new(cx - cw, 0.385f, z);
			Vector3 M(float z) => new(cx, 0.35f, z);
			Vector3 R(float z) => new(cx + cw, 0.385f, z);
			k.Quad(L(za), M(za), M(zb), L(zb), new Vector3(0.2f, 1f, 0).Normalized());
			k.Quad(M(za), R(za), R(zb), M(zb), new Vector3(-0.2f, 1f, 0).Normalized());
		}
		// the mattress: a thin ticking pad
		k.Color = new Color(0.55f, 0.52f, 0.44f);
		k.BevelBox(new Vector3(cx, 0.42f, zm), new Vector3(cw * 2f - 0.02f, 0.07f, len - 0.06f), 0.03f, 2f);
		// the blanket, rumpled back
		k.Mat(HollowLook.Blanket);
		k.Color = Colors.White;
		{
			float bz0 = cz1 - 1.33f, bz1 = cz1 - 0.06f, bxw = cw + 0.04f, by = 0.46f;
			const int bn = 8;
			var brng = new RandomNumberGenerator { Seed = (ulong)(Seed * 19 + 5) };
			var lump = new float[bn + 1];
			for (int i = 0; i <= bn; i++) lump[i] = brng.RandfRange(0f, 0.02f);
			Vector3 P(float side, int i, bool hang) =>
				new(cx + side * (hang ? bxw + 0.012f : bxw * 0.7f), hang ? by - 0.15f - lump[i] * 2f : by + 0.025f + lump[i], Mathf.Lerp(bz0, bz1, i / (float)bn));
			Vector3 E(float side, int i) => new(cx + side * bxw, by + 0.005f, Mathf.Lerp(bz0, bz1, i / (float)bn));
			for (int i = 0; i < bn; i++)
			{
				// a darker stripe across near the foot, as army blankets have
				float zf = (i + 0.5f) / bn;
				k.Color = zf > 0.78f && zf < 0.9f ? new Color(0.55f, 0.5f, 0.45f) : Colors.White;
				k.Quad(P(-1, i, false), P(1, i, false), P(1, i + 1, false), P(-1, i + 1, false), Vector3.Up);
				foreach (float sd in new[] { -1f, 1f })
				{
					Vector3 nOut = new(sd, 0.4f, 0);
					k.Quad(P(sd, i, false), E(sd, i), E(sd, i + 1), P(sd, i + 1, false), nOut.Normalized());
					k.Quad(E(sd, i), P(sd, i, true), P(sd, i + 1, true), E(sd, i + 1), new Vector3(sd, 0, 0));
				}
			}
			k.Color = Colors.White;
			k.Quad(P(-1, bn, false), P(1, bn, false), E(1, bn) + new Vector3(0, -0.15f, 0.03f), E(-1, bn) + new Vector3(0, -0.15f, 0.03f), Vector3.Back);
			k.Color = new Color(0.86f, 0.84f, 0.82f);
			k.Cylinder(new Vector3(cx - bxw - 0.01f, by + 0.045f, bz0 - 0.03f), new Vector3(cx + bxw + 0.01f, by + 0.05f, bz0 - 0.02f), 0.045f, 0.05f, 10, true);
		}
		k.Mat(BuildingTextures.Plain("b_pillow", new Color(0.5f, 0.48f, 0.43f)));
		k.Color = Colors.White;
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(new Vector3(cx - 0.25f, 0.515f, cz0 + 0.22f), 0.04f, 0.1f),
			new ItemMeshes.Ring(new Vector3(cx - 0.2f, 0.52f, cz0 + 0.22f), 0.055f, 0.14f),
			new ItemMeshes.Ring(new Vector3(cx, 0.515f, cz0 + 0.22f), 0.048f, 0.15f),
			new ItemMeshes.Ring(new Vector3(cx + 0.2f, 0.52f, cz0 + 0.22f), 0.055f, 0.14f),
			new ItemMeshes.Ring(new Vector3(cx + 0.25f, 0.515f, cz0 + 0.22f), 0.04f, 0.1f),
		}, 12, true, true, Vector3.Up, 4f, null, null, 1f, 0.5f);
	}

	/// <summary>A braided rag rug: concentric oval rings of old cloth in faded colours, flat on the floor.</summary>
	private static void BraidedRug(MeshKit k, Vector3 c, float rx, float rz, float yaw)
	{
		k.Mat(BuildingTextures.Plain("b_rug", new Color(0.42f, 0.36f, 0.3f)));
		var cloth = new[] { new Color(0.62f, 0.32f, 0.26f), new Color(0.5f, 0.5f, 0.46f), new Color(0.36f, 0.3f, 0.24f), new Color(0.56f, 0.5f, 0.36f), new Color(0.3f, 0.34f, 0.32f) };
		var b = new Basis(Vector3.Up, yaw);
		const int rings = 9, seg = 28;
		for (int r = 0; r < rings; r++)
		{
			float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
			for (int i = 0; i < seg; i++)
			{
				float a0 = Mathf.Tau * i / seg, a1 = Mathf.Tau * (i + 1) / seg;
				// the braid: each ring's colour broken into short lengths
				var col = cloth[(r * 3 + (i / 3) % 2) % cloth.Length] * (((i + r) % 2 == 0) ? 1f : 0.85f);
				k.Color = col;
				Vector3 P(float t, float a) => c + b * new Vector3(Mathf.Cos(a) * rx * t, 0.003f * (1f - t), Mathf.Sin(a) * rz * t);
				k.Quad(P(t0, a0), P(t1, a0), P(t1, a1), P(t0, a1), Vector3.Up);
			}
		}
		k.Color = Colors.White;
	}
}
