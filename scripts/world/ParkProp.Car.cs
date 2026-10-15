using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The player's own car (2026-10-15, the owner: "make the car look less blocky and more real but keep it PS2 style"): a
/// plain late-90s estate in faded dark green, backed into a bay with its tailgate lifted. It was slices of extruded
/// boxes with a box roof, box bumpers and slab lamps. Now its body is one smooth-shaded skin: the flanks rolled in at the
/// shoulder and tucked under at the sill, the nose and tail rounded in plan, the bonnet sloping down to the nose, the
/// wheel arches cut round 65 cm tyres; a raked windscreen, a crowned roof with rails, a glasshouse leaning in over the
/// belt on its pillars; tinted glass with the cabin inside it (the seats, the dash, the steering wheel, the door cards,
/// the headliner); door and bonnet shut lines, handles, mirrors, wipers, wrap-round bumpers, recessed headlamps and tail
/// lamps with their lenses, plates; the paint faded on top and mud up the sills and behind the wheels.
/// About 5 k triangles. Local frame: the tailgate end is +Z (the prop's front), the nose -Z; origin on the ground at the
/// car's middle. 4.6 x 1.74 x 1.43 m. What the opening and the endings use is where it was: the load floor at 0.6 (the
/// camera lies on it), the driver's seat at (-0.38, 1.15, 0.25) facing -Z, the two collision boxes.
/// </summary>
public partial class ParkProp
{
	private void Car()
	{
		// the paint: the old dark green, sun-faded, in the material; the vertices darken the flanks against the faded top and
		// carry the mud (multipliers)
		var paint = CarMat("car_paint2", new Color(0.075f, 0.12f, 0.09f), 0.5f, 0.4f);
		var glass = (StandardMaterial3D)(_carMats.TryGetValue("car_glass2", out var gm) ? gm : _carMats["car_glass2"] = new StandardMaterial3D
		{
			ResourceName = "car_glass2", AlbedoColor = new Color(0.05f, 0.065f, 0.07f, 0.62f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.08f, MetallicSpecular = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		});
		var trim = CarMat("car_trim", new Color(0.07f, 0.07f, 0.075f), 0.8f, 0.25f, false);
		var tyre = CarMat("car_tyre", new Color(0.035f, 0.035f, 0.035f), 0.95f, 0.1f, false);
		var hub = CarMat("car_hub", new Color(0.45f, 0.46f, 0.47f), 0.45f, 0.6f, false);
		var inner = CarMat("car_inner", new Color(0.13f, 0.13f, 0.14f), 0.95f, 0.15f, false);
		var seat = CarMat("car_seat", new Color(0.17f, 0.16f, 0.15f), 0.95f, 0.1f, false);
		var lamp = CarMat("car_lamp", new Color(0.55f, 0.08f, 0.06f), 0.3f, 0.6f, false);
		var amber = CarMat("car_amber", new Color(0.55f, 0.3f, 0.06f), 0.3f, 0.6f, false);
		var reverse = CarMat("car_reverse", new Color(0.6f, 0.6f, 0.58f), 0.25f, 0.7f, false);
		var head = CarMat("car_head2", new Color(0.42f, 0.43f, 0.4f), 0.15f, 0.85f, false);
		var plate = Tint("car_plate", new Color(0.78f, 0.77f, 0.7f), 0.6f);
		// small parts as plain boxes, the big ones eased (the budget: a PS2 car, ~5 k triangles)
		void B(Vector3 c, Vector3 s, float bevel, float uv = 1f, Basis? rot = null)
		{
			if (Mathf.Max(s.X, Mathf.Max(s.Y, s.Z)) < 0.2f || Mathf.Min(s.X, Mathf.Min(s.Y, s.Z)) < 0.015f) _k.Box(c, s, uv, rot);
			else _k.BevelBox(c, s, bevel, uv, rot);
		}

		const float L = 4.6f, W = 1.74f, hw = W * 0.5f, zN = -L * 0.5f, zR = L * 0.5f;
		const float sill = 0.28f, belt = 0.88f, roof = 1.42f;
		const float zWs = -0.95f, zRoofF = -0.12f, zRoofR = zR - 0.12f;
		const float floorY = 0.6f, zSeat = zR - 1.15f;
		const float axleY = 0.32f, tyreR = 0.325f, archR = 0.4f;
		float[] wheelZ = { -1.42f, 1.38f };
		const float hw0 = hw - 0.05f, hw1 = hw - 0.15f;   // the glass at the belt, and at the roof

		// ---- the colours: the old dark green, faded on top, mud up the sills and behind the wheels
		var green = new Color(0.72f, 0.85f, 0.78f);
		var faded = new Color(1f, 1f, 1f);
		var mud = new Color(1f, 0.78f, 0.52f) * 0.9f;
		float Noise(float a, float b) => 0.5f + 0.25f * Mathf.Sin(a * 13.1f + b * 7.3f) + 0.25f * Mathf.Sin(a * 5.7f - b * 17.9f + 1.3f);
		Color PaintAt(Vector3 p, float upness)
		{
			var c = green.Lerp(faded, Mathf.Clamp(upness, 0f, 1f) * 0.8f);
			float m = Mathf.Clamp(1f - (p.Y - sill) / 0.32f, 0f, 1f);
			foreach (float wz in wheelZ)
			{
				float behind = p.Z - wz;   // thrown back off the tyre (the car drove nose first)
				if (behind > 0f && behind < 0.9f) m = Mathf.Max(m, Mathf.Clamp(1f - (p.Y - axleY) / 0.5f, 0f, 1f) * (1f - behind / 0.9f));
			}
			m *= 0.55f + 0.45f * Noise(p.Z, p.Y);
			return c.Lerp(mud, Mathf.Clamp(m, 0f, 0.9f));
		}

		// ---- the body's skin: columns along the car, rows from the sill (or the arch) up to the shoulder
		float Round(float z)
		{
			const float rn = 0.24f, rt = 0.08f;
			if (z < zN + rn) { float d = rn - (z - zN); return rn - Mathf.Sqrt(Mathf.Max(0f, rn * rn - d * d)); }
			if (z > zR - rt) { float d = rt - (zR - z); return rt - Mathf.Sqrt(Mathf.Max(0f, rt * rt - d * d)); }
			return 0f;
		}
		float TopAt(float z) => z < zWs ? Mathf.Lerp(0.72f, belt - 0.015f, Mathf.SmoothStep(0f, 1f, (z - zN) / (zWs - zN))) : belt;
		float BottomAt(float z)
		{
			foreach (float wz in wheelZ)
				if (Mathf.Abs(z - wz) < archR) return axleY + Mathf.Sqrt(archR * archR - (z - wz) * (z - wz));
			// the overhangs lift a little toward the bumpers
			float lift = z < wheelZ[0] ? (wheelZ[0] - archR - z) * 0.06f : z > wheelZ[1] ? (z - wheelZ[1] - archR) * 0.06f : 0f;
			return sill + Mathf.Max(0f, lift);
		}
		var zs = new List<float>();
		for (float z = zN; z < zR - 0.001f; z += 0.16f) zs.Add(z);
		foreach (float wz in wheelZ) for (int i = 0; i <= 10; i++) zs.Add(wz - archR + i * archR * 2f / 10f);
		foreach (float z in new[] { zN + 0.02f, zN + 0.06f, zN + 0.12f, zN + 0.18f, zR - 0.02f, zR - 0.05f, zWs, zSeat, zR }) zs.Add(z);
		zs.Sort();
		for (int i = zs.Count - 1; i > 0; i--) if (zs[i] - zs[i - 1] < 0.012f) zs.RemoveAt(i);
		// the section: (t up the flank, x in from the widest)
		var rows = new (float t, float dx)[] { (0f, 0.05f), (0.07f, 0.014f), (0.22f, 0.002f), (0.5f, 0f), (0.8f, 0.004f), (0.93f, 0.018f), (1f, 0.05f) };
		Vector3 Skin(float side, float z, int j)
		{
			float b = BottomAt(z), t = TopAt(z);
			var (rt, dx) = rows[j];
			float y = Mathf.Lerp(b, t, rt);
			// at the nose the face leans back a little toward its top
			float lean = z < zN + 0.001f ? 0.05f * rt * rt : 0f;
			return new Vector3(side * (hw - dx - Round(z)), y, z + lean);
		}
		_k.Mat(paint);
		foreach (float side in new[] { -1f, 1f })
		{
			var p = new Vector3[zs.Count, rows.Length];
			for (int i = 0; i < zs.Count; i++)
				for (int j = 0; j < rows.Length; j++) p[i, j] = Skin(side, zs[i], j);
			Grid(p, (q, n) => PaintAt(q, n.Y), q => new Vector3(side, 0f, q.Z * 0.12f));
		}
		// the bonnet: across from shoulder to shoulder, crowned, from the nose to the scuttle
		{
			var cols = new List<float>();
			foreach (float z in zs) if (z <= zWs + 0.001f) cols.Add(z);
			var p = new Vector3[cols.Count, 5];
			for (int i = 0; i < cols.Count; i++)
			{
				var l = Skin(-1f, cols[i], rows.Length - 1);
				for (int j = 0; j < 5; j++)
				{
					float u = j / 4f, x = Mathf.Lerp(l.X, -l.X, u);
					p[i, j] = new Vector3(x, l.Y + 0.03f * (1f - (2f * u - 1f) * (2f * u - 1f)), l.Z);
				}
			}
			Grid(p, (q, n) => PaintAt(q, n.Y), q => Vector3.Up);
		}
		// the nose: the end of the skin closed across
		{
			var p = new Vector3[2, rows.Length];
			for (int j = 0; j < rows.Length; j++) { p[0, j] = Skin(-1f, zs[0], j); p[1, j] = Skin(1f, zs[0], j); }
			Grid(p, (q, n) => PaintAt(q, 0f), q => Vector3.Forward);
			var top = new Vector3[2, 2] { { p[0, rows.Length - 1], p[0, rows.Length - 1] + new Vector3(0, 0.03f, 0.001f) }, { p[1, rows.Length - 1], p[1, rows.Length - 1] + new Vector3(0, 0.03f, 0.001f) } };
			Grid(top, (q, n) => PaintAt(q, 0f), q => Vector3.Forward);
		}
		// the tail: closed across below the load floor; above it only the lamp panels beside the tailgate's opening
		{
			int last = zs.Count - 1;
			float gap = hw0 - 0.14f;
			var p = new Vector3[2, 3];
			for (int s = 0; s < 2; s++)
			{
				float side = s == 0 ? -1f : 1f;
				var a = Skin(side, zs[last], 0);
				p[s, 0] = a;
				p[s, 1] = new Vector3(a.X, (a.Y + floorY) * 0.5f, zR);
				p[s, 2] = new Vector3(Skin(side, zs[last], 3).X, floorY, zR);
			}
			Grid(p, (q, n) => PaintAt(q, 0f), q => Vector3.Back);
			foreach (float side in new[] { -1f, 1f })
			{
				var lp = new Vector3[2, 2];
				var outer0 = Skin(side, zs[last], 3); var outer1 = Skin(side, zs[last], rows.Length - 1);
				lp[0, 0] = new Vector3(side * gap, floorY, zR); lp[0, 1] = new Vector3(side * gap, belt, zR);
				lp[1, 0] = new Vector3(outer0.X, floorY, zR); lp[1, 1] = new Vector3(outer1.X, belt, zR);
				Grid(lp, (q, n) => PaintAt(q, 0f), q => Vector3.Back);
			}
		}

		// ---- the glasshouse: tinted glass leaning in from the belt to the roof, on its pillars
		Vector3 G(float side, float y, float z, float outBy = 0f) => new(side * (Mathf.Lerp(hw0, hw1, (y - belt) / (roof - belt)) + outBy), y, z);
		float AFront(float y) => Mathf.Lerp(zWs, zRoofF, (y - belt) / (roof - belt));   // the windscreen's side edge
		const float pillarB0 = 0.5f, pillarB1 = 0.6f, pillarC0 = 1.42f, pillarC1 = 1.52f, pillarD0 = zRoofR - 0.1f;
		const float gTop = roof - 0.045f;
		_k.Mat(glass);
		_k.Color = Colors.White;
		foreach (float side in new[] { -1f, 1f })
		{
			var n = new Vector3(side, 0.18f, 0).Normalized();
			_k.Quad(G(side, belt, zWs + 0.08f), G(side, belt, pillarB0), G(side, gTop, pillarB0), G(side, gTop, AFront(gTop) + 0.07f), n);
			_k.Quad(G(side, belt, pillarB1), G(side, belt, pillarC0), G(side, gTop, pillarC0), G(side, gTop, pillarB1), n);
			_k.Quad(G(side, belt, pillarC1), G(side, belt, pillarD0), G(side, gTop, pillarD0), G(side, gTop, pillarC1), n);
		}
		// the windscreen, raked
		var wsN = new Vector3(0, zRoofF - zWs, -(roof - belt)).Normalized();
		_k.Quad(new Vector3(-hw0 + 0.06f, belt + 0.01f, zWs + 0.03f), new Vector3(hw0 - 0.06f, belt + 0.01f, zWs + 0.03f), new Vector3(hw1 - 0.05f, gTop, AFront(gTop)), new Vector3(-hw1 + 0.05f, gTop, AFront(gTop)), wsN);
		_k.Mat(paint);
		foreach (float side in new[] { -1f, 1f })
		{
			// the pillars, a hair outside the glass, and the frame over the windows
			void Pillar(float z0, float z1, float zt0, float zt1)
			{
				var c0 = PaintAt(new Vector3(0, roof, 0), 1f);
				_k.Color = c0;
				_k.Quad(G(side, belt, z0, 0.004f), G(side, belt, z1, 0.004f), G(side, gTop, zt1, 0.004f), G(side, gTop, zt0, 0.004f), new Vector3(side, 0.18f, 0).Normalized());
			}
			Pillar(pillarB0, pillarB1, pillarB0, pillarB1);
			Pillar(pillarC0, pillarC1, pillarC0, pillarC1);
			Pillar(pillarD0, zRoofR, pillarD0, zRoofR);
			Pillar(zWs, zWs + 0.08f, AFront(gTop), AFront(gTop) + 0.07f);
			// the cant rail over the windows, up to the roof's edge
			_k.Quad(G(side, gTop, AFront(gTop), 0.004f), G(side, gTop, zRoofR, 0.004f), G(side, roof, zRoofR, 0.004f), G(side, roof, zRoofF, 0.004f), new Vector3(side, 0.4f, 0).Normalized());
			// the A pillar, rolled over the windscreen's edge
			_k.Beam(new Vector3(side * (hw0 - 0.035f), belt + 0.005f, zWs + 0.02f), new Vector3(side * (hw1 - 0.02f), roof - 0.005f, zRoofF + 0.01f), 0.075f, 0.035f, 1f, wsN);
		}
		// the roof: crowned across, its edges turned down into the cant rails
		{
			const int nx = 7;
			var rz = new List<float>();
			for (float z = zRoofF; z < zRoofR - 0.01f; z += 0.4f) rz.Add(z);
			rz.Add(zRoofR);
			var p = new Vector3[rz.Count, nx];
			for (int i = 0; i < rz.Count; i++)
				for (int j = 0; j < nx; j++)
				{
					float u = j / (nx - 1f) * 2f - 1f;
					float edge = Mathf.Abs(u) > 0.99f ? 0.006f : 0f;
					p[i, j] = new Vector3(u * (hw1 + 0.004f - edge), roof + 0.035f * (1f - u * u) - edge, rz[i]);
				}
			Grid(p, (q, n) => PaintAt(q, 1f), q => Vector3.Up);
			// the headliner under it
			_k.Mat(inner);
			_k.Color = new Color(0.7f, 0.68f, 0.62f);
			var h = new Vector3[rz.Count, nx];
			for (int i = 0; i < rz.Count; i++)
				for (int j = 0; j < nx; j++) h[i, j] = p[i, j] + Vector3.Down * 0.035f;
			GridFlat(h, Vector3.Down);
			_k.Color = Colors.White;
			_k.Mat(paint);
		}
		// the front and back edges of the roof: the windscreen's header and the tailgate's hinge rail
		_k.Color = PaintAt(new Vector3(0, roof, 0), 1f);
		_k.Beam(new Vector3(-hw1, roof - 0.01f, zRoofF + 0.01f), new Vector3(hw1, roof - 0.01f, zRoofF + 0.01f), 0.06f, 0.05f, 1f);
		_k.Beam(new Vector3(-hw1, roof - 0.01f, zRoofR), new Vector3(hw1, roof - 0.01f, zRoofR), 0.06f, 0.05f, 1f);
		_k.Color = Colors.White;

		// ---- trim: roof rails, the scuttle and the wipers, mirrors, handles, rubbing strips, the shut lines
		_k.Mat(trim);
		foreach (float side in new[] { -1f, 1f })
		{
			float rx = side * (hw1 - 0.1f);
			B(new Vector3(rx, roof + 0.06f, (zRoofF + zRoofR) * 0.5f + 0.2f), new Vector3(0.035f, 0.025f, zRoofR - zRoofF - 0.5f), 0.008f, 1f);
			foreach (float fz in new[] { zRoofF + 0.35f, zRoofR - 0.15f })
				B(new Vector3(rx, roof + 0.035f, fz), new Vector3(0.04f, 0.05f, 0.08f), 0.01f, 1f);
			// the mirror: an arm off the door's corner, its housing, the glass facing back
			var m = new Vector3(side * (hw0 + 0.12f), belt + 0.1f, zWs + 0.16f);
			_k.Beam(new Vector3(side * (hw0 - 0.01f), belt + 0.04f, zWs + 0.14f), m, 0.035f, 0.03f, 1f);
			B(m + new Vector3(side * 0.04f, 0, 0), new Vector3(0.17f, 0.11f, 0.07f), 0.02f, 1f, new Basis(Vector3.Up, side * 0.12f));
			// the door handles, pulls set into the doors under the belt
			foreach (float hz in new[] { 0.32f, 1.28f })
			{
				var at = Skin(side, hz, rows.Length - 2);
				B(at + new Vector3(side * 0.006f, -0.07f, 0), new Vector3(0.025f, 0.03f, 0.13f), 0.008f, 1f);
			}
			// the rubbing strip between the arches
			float sz0 = wheelZ[0] + archR + 0.06f, sz1 = wheelZ[1] - archR - 0.06f;
			var rs = Skin(side, 0f, 2);
			B(new Vector3(rs.X + side * 0.008f, rs.Y + 0.03f, (sz0 + sz1) * 0.5f), new Vector3(0.022f, 0.05f, sz1 - sz0), 0.008f, 1f);
			// the shut lines: the front door's, between the doors, the rear door's around the arch
			foreach (float z in new[] { -0.92f, 0.53f, 1.5f })
				for (int j = 0; j + 2 < rows.Length; j++)
				{
					var a = Skin(side, z, j); var b = Skin(side, z, j + 1);
					if (a.Y < BottomAt(z) + 0.01f && j == 0 && BottomAt(z) > sill + 0.05f) continue;
					{ var o = new Vector3(side * 0.003f, 0, 0); var w = new Vector3(0, 0, 0.003f); _k.Quad(a + o - w, a + o + w, b + o + w, b + o - w, new Vector3(side, 0, 0)); }
				}
			var top0 = Skin(side, -0.92f, rows.Length - 2); var topB = Skin(side, 0.53f, rows.Length - 2); var topC = Skin(side, 1.5f, rows.Length - 2);
			_k.Beam(top0 + new Vector3(side * 0.003f, 0.025f, 0), topC + new Vector3(side * 0.003f, 0.025f, 0), 0.004f, 0.006f, 1f, new Vector3(side, 0, 0));
			_ = topB;
		}
		// the bonnet's shut line and its scuttle panel, the wipers parked on the glass
		_k.Beam(new Vector3(-hw + 0.1f, TopAt(zWs - 0.06f) + 0.032f, zWs - 0.06f), new Vector3(hw - 0.1f, TopAt(zWs - 0.06f) + 0.032f, zWs - 0.06f), 0.004f, 0.006f, 1f);
		B(new Vector3(0, belt + 0.01f, zWs - 0.02f), new Vector3(W - 0.2f, 0.025f, 0.07f), 0.008f, 1f);
		foreach (float wx in new[] { -0.62f, 0.02f })
		{
			_k.Beam(new Vector3(wx, belt + 0.03f, zWs + 0.02f), new Vector3(wx + 0.52f, belt + 0.045f, zWs + 0.05f), 0.012f, 0.012f, 1f, wsN);
			_k.Cylinder(new Vector3(wx + 0.02f, belt + 0.02f, zWs + 0.0f), new Vector3(wx + 0.02f, belt + 0.04f, zWs + 0.02f), 0.012f, 0.012f, 6, true, 1f);
		}
		// mirror glass
		_k.Mat(hub);
		foreach (float side in new[] { -1f, 1f })
		{
			var m = new Vector3(side * (hw0 + 0.16f), belt + 0.1f, zWs + 0.196f);
			_k.Quad(m + new Vector3(-0.07f, -0.04f, 0), m + new Vector3(0.07f, -0.04f, 0), m + new Vector3(0.07f, 0.04f, 0), m + new Vector3(-0.07f, 0.04f, 0), Vector3.Back);
		}

		// ---- the bumpers, wrapped round the corners; the grille and the lamps set into the nose and the tail
		_k.Mat(trim);
		B(new Vector3(0, 0.3f, zN - 0.03f), new Vector3(W - 0.08f, 0.17f, 0.17f), 0.06f, 1f);
		B(new Vector3(0, 0.3f, zR + 0.03f), new Vector3(W - 0.04f, 0.17f, 0.17f), 0.06f, 1f);
		foreach (float side in new[] { -1f, 1f })
		{
			B(new Vector3(side * (hw - 0.17f), 0.3f, zN + 0.12f), new Vector3(0.2f, 0.16f, 0.3f), 0.05f, 1f, new Basis(Vector3.Up, side * 0.5f));
			B(new Vector3(side * (hw - 0.06f), 0.3f, zR - 0.12f), new Vector3(0.12f, 0.16f, 0.26f), 0.05f, 1f);
		}
		// the grille: a dark recess with its slats, between the lamps
		float noseY(float t) => Mathf.Lerp(BottomAt(zN), TopAt(zN), t);
		float fz0 = zN + 0.004f;
		B(new Vector3(0, 0.6f, fz0 - 0.004f), new Vector3(0.6f, 0.14f, 0.02f), 0.006f, 1f);
		_k.Mat(CarMat("car_grillehole", new Color(0.015f, 0.015f, 0.015f), 0.9f, 0.1f, false));
		_k.Box(new Vector3(0, 0.6f, fz0 - 0.016f), new Vector3(0.56f, 0.11f, 0.004f));
		_k.Mat(trim);
		for (int i = 0; i < 4; i++) B(new Vector3(0, 0.56f + i * 0.027f, fz0 - 0.022f), new Vector3(0.56f, 0.008f, 0.012f), 0.003f, 1f);
		foreach (float side in new[] { -1f, 1f })
		{
			// a headlamp: its black bezel standing proud, the lens set back inside it; the indicator beside it
			var hc = new Vector3(side * 0.53f, 0.61f, fz0 - 0.004f);
			_k.Mat(trim);
			B(hc + new Vector3(0, 0.075f, -0.012f), new Vector3(0.36f, 0.02f, 0.03f), 0.006f, 1f);
			B(hc + new Vector3(0, -0.075f, -0.012f), new Vector3(0.36f, 0.02f, 0.03f), 0.006f, 1f);
			B(hc + new Vector3(-0.17f, 0, -0.012f), new Vector3(0.02f, 0.17f, 0.03f), 0.006f, 1f);
			B(hc + new Vector3(0.17f, 0, -0.012f), new Vector3(0.02f, 0.17f, 0.03f), 0.006f, 1f);
			_k.Mat(head);
			B(hc + new Vector3(0, 0, -0.004f), new Vector3(0.32f, 0.13f, 0.012f), 0.004f, 1f);
			_k.Mat(CarMat("car_reflector", new Color(0.35f, 0.35f, 0.34f), 0.2f, 0.9f, false));
			_k.Cylinder(hc + new Vector3(side * 0.06f, 0, 0.002f), hc + new Vector3(side * 0.06f, 0, -0.01f), 0.045f, 0.04f, 12, true, 1f);
			_k.Mat(amber);
			B(new Vector3(side * 0.6f, 0.4f, zN - 0.035f), new Vector3(0.16f, 0.05f, 0.02f), 0.006f, 1f);
			// the tail: the tall lamp in its panel beside the opening, red over amber over the reversing lens, in a black frame
			float lx = side * (hw0 - 0.07f);
			_k.Mat(trim);
			B(new Vector3(lx, 0.75f, zR + 0.006f), new Vector3(0.13f, 0.27f, 0.014f), 0.006f, 1f);
			_k.Mat(lamp);
			B(new Vector3(lx, 0.8f, zR + 0.012f), new Vector3(0.11f, 0.13f, 0.012f), 0.004f, 1f);
			_k.Mat(amber);
			B(new Vector3(lx, 0.705f, zR + 0.012f), new Vector3(0.11f, 0.05f, 0.012f), 0.004f, 1f);
			_k.Mat(reverse);
			B(new Vector3(lx, 0.655f, zR + 0.012f), new Vector3(0.11f, 0.04f, 0.012f), 0.004f, 1f);
		}
		_ = noseY(0f);
		// the plates
		_k.Mat(plate);
		B(new Vector3(0, 0.31f, zN - 0.125f), new Vector3(0.52f, 0.11f, 0.008f), 0.003f, 1f);
		B(new Vector3(0, 0.47f, zR + 0.004f), new Vector3(0.52f, 0.11f, 0.008f), 0.003f, 1f);

		// ---- the cabin, seen through the glass: the floor, the door cards, the dash and its binnacle, the wheel, the
		// seats; the load space behind the rear seat as it was
		_k.Mat(inner);
		_k.Color = Colors.White;
		const float cabFloor = 0.32f;
		_k.Quad(new Vector3(-hw0 + 0.03f, cabFloor, zWs), new Vector3(hw0 - 0.03f, cabFloor, zWs), new Vector3(hw0 - 0.03f, cabFloor, zSeat), new Vector3(-hw0 + 0.03f, cabFloor, zSeat), Vector3.Up);
		foreach (float side in new[] { -1f, 1f })
		{
			float x = side * (hw0 - 0.035f);
			_k.Quad(new Vector3(x, cabFloor, zWs), new Vector3(x, cabFloor, zSeat), new Vector3(x, belt - 0.01f, zSeat), new Vector3(x, belt - 0.01f, zWs), new Vector3(-side, 0, 0));
			_k.Quad(new Vector3(x, belt - 0.01f, zWs), new Vector3(x, belt - 0.01f, zSeat), new Vector3(side * (hw0 - 0.005f), belt + 0.002f, zSeat), new Vector3(side * (hw0 - 0.005f), belt + 0.002f, zWs), Vector3.Up);
			// an armrest along each door
			B(new Vector3(side * (hw0 - 0.07f), 0.66f, -0.2f), new Vector3(0.06f, 0.04f, 0.5f), 0.012f, 1f);
		}
		// the dash: a moulded top under the windscreen, the instrument binnacle, the centre stack, the firewall under it
		B(new Vector3(0, 0.84f, zWs + 0.2f), new Vector3(2f * hw0 - 0.08f, 0.16f, 0.4f), 0.04f, 1f);
		B(new Vector3(-0.38f, 0.93f, zWs + 0.36f), new Vector3(0.38f, 0.08f, 0.12f), 0.03f, 1f);
		B(new Vector3(0, 0.62f, zWs + 0.32f), new Vector3(0.24f, 0.3f, 0.22f), 0.02f, 1f);
		_k.Quad(new Vector3(-hw0 + 0.04f, cabFloor, zWs + 0.02f), new Vector3(hw0 - 0.04f, cabFloor, zWs + 0.02f), new Vector3(hw0 - 0.04f, 0.76f, zWs + 0.02f), new Vector3(-hw0 + 0.04f, 0.76f, zWs + 0.02f), Vector3.Back);
		// the steering wheel on its column, three spokes, toward the driver
		{
			var wheelC = new Vector3(-0.38f, 0.9f, -0.42f);
			var wAxis = new Vector3(0, 0.45f, 1f).Normalized();
			_k.Mat(CarMat("car_wheelrim", new Color(0.04f, 0.04f, 0.04f), 0.6f, 0.3f, false));
			_k.Cylinder(new Vector3(-0.38f, 0.8f, zWs + 0.36f), wheelC - wAxis * 0.03f, 0.03f, 0.025f, 8, false, 1f);
			ItemMeshes.Torus(_k, wheelC, wAxis, 0.18f, 0.016f, 12, 4);
			var right = Vector3.Right;
			var up = wAxis.Cross(right).Normalized();
			foreach (var dir in new[] { right, -right, -up })
				_k.Beam(wheelC, wheelC + dir * 0.17f, 0.04f, 0.012f, 1f, wAxis);
			_k.Cylinder(wheelC - wAxis * 0.01f, wheelC + wAxis * 0.03f, 0.05f, 0.045f, 10, true, 1f);
		}
		// the seats: two buckets with their headrests, the rear bench's cushion
		_k.Mat(seat);
		foreach (float side in new[] { -1f, 1f })
		{
			float sx = side * 0.38f;
			B(new Vector3(sx, 0.5f, 0.22f), new Vector3(0.48f, 0.13f, 0.5f), 0.04f, 1f);
			var back = new Basis(Vector3.Right, 0.22f);
			B(new Vector3(sx, 0.86f, 0.52f), new Vector3(0.48f, 0.6f, 0.12f), 0.04f, 1f, back);
			B(new Vector3(sx, 1.24f, 0.6f), new Vector3(0.26f, 0.17f, 0.09f), 0.035f, 1f, back);
			_k.Mat(trim);
			foreach (float hx in new[] { -0.07f, 0.07f })
				_k.Cylinder(new Vector3(sx + hx, 1.13f, 0.58f), new Vector3(sx + hx, 1.17f, 0.59f), 0.007f, 0.007f, 5, false, 1f);
			_k.Mat(seat);
		}
		B(new Vector3(0, 0.48f, zSeat - 0.33f), new Vector3(2f * hw0 - 0.2f, 0.14f, 0.46f), 0.04f, 1f);
		_k.Mat(inner);
		B(new Vector3(0, floorY + 0.3f, zSeat - 0.05f), new Vector3(hw0 * 2f - 0.12f, 0.6f, 0.1f), 0.03f, 2f);

		// the load space: needle-felt floor, trim inside the pillars and over it
		_k.Mat(CarMat("car_carpet", new Color(0.2f, 0.2f, 0.21f), 1f, 0.05f));
		_k.Color = Colors.White;
		_k.Quad(new Vector3(-hw0 + 0.06f, floorY, zSeat), new Vector3(hw0 - 0.06f, floorY, zSeat), new Vector3(hw0 - 0.06f, floorY, zR - 0.02f), new Vector3(-hw0 + 0.06f, floorY, zR - 0.02f), Vector3.Up,
			new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1.5f), new Vector2(0, 1.5f));
		_k.Mat(inner);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(new Vector3(side * (hw0 - 0.04f), (floorY + roof) * 0.5f, (zSeat + zR) * 0.5f), new Vector3(0.04f, roof - floorY, zR - zSeat), 1f);
		// what's in it: a duffel and a water bottle
		_k.Mat(Tint("car_duffel", new Color(0.2f, 0.24f, 0.32f), 0.9f));
		ItemMeshes.Loft(_k, new[]
		{
			new ItemMeshes.Ring(new Vector3(-0.58f, floorY + 0.13f, zSeat + 0.25f), 0.07f, 0.09f), new ItemMeshes.Ring(new Vector3(-0.54f, floorY + 0.15f, zSeat + 0.255f), 0.13f, 0.15f),
			new ItemMeshes.Ring(new Vector3(-0.42f, floorY + 0.15f, zSeat + 0.265f), 0.15f, 0.17f), new ItemMeshes.Ring(new Vector3(-0.1f, floorY + 0.14f, zSeat + 0.29f), 0.14f, 0.17f),
			new ItemMeshes.Ring(new Vector3(0.02f, floorY + 0.14f, zSeat + 0.3f), 0.12f, 0.15f), new ItemMeshes.Ring(new Vector3(0.07f, floorY + 0.13f, zSeat + 0.3f), 0.06f, 0.08f),
		}, 12, true, true, Vector3.Up, 2f, null, null, 1f, 0.5f);
		_k.Mat(trim);
		foreach (float hx in new[] { -0.36f, -0.16f })
			ItemMeshes.Torus(_k, new Vector3(hx, floorY + 0.29f, zSeat + 0.28f), Vector3.Back, 0.05f, 0.008f, 6, 3);
		var bottleXf = new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(0.55f, floorY + 0.037f, zSeat + 0.45f));
		_k.Mat(Tint("car_bottle", new Color(0.3f, 0.45f, 0.5f), 0.4f));
		HollowProps.In(_k, bottleXf, () => HollowProps.Lathe(_k, Vector3.Zero, new Vector2[]
		{
			new(0.03f, 0f), new(0.036f, 0.004f), new(0.037f, 0.02f), new(0.035f, 0.1f), new(0.037f, 0.16f), new(0.036f, 0.19f), new(0.026f, 0.215f),
			new(0.015f, 0.23f), new(0.015f, 0.24f),
		}, 10, true, true));
		_k.Mat(Tint("car_bottlecap", new Color(0.08f, 0.08f, 0.09f), 0.6f));
		HollowProps.In(_k, bottleXf, () => HollowProps.Lathe(_k, Vector3.Zero, new Vector2[] { new(0.017f, 0.238f), new(0.017f, 0.262f), new(0.014f, 0.266f) }, 8, false, true));

		// ---- the tailgate: hinged at the roof's rear edge and lifted, its glass, its skin, its frame, the struts
		var hinge = new Vector3(0, roof, zR - 0.04f);
		var open = Basis.FromEuler(new Vector3(Mathf.DegToRad(-128f), 0, 0));   // hangs down (-Y) when shut; swung back and up
		Vector3 H(float x, float d, float t) => hinge + open * new Vector3(x, -d, t);
		float hh = roof - sill - 0.25f;   // its height when shut
		_k.Mat(glass);
		_k.Quad(H(-hw1, 0.07f, 0.03f), H(hw1, 0.07f, 0.03f), H(hw1, hh * 0.55f, 0.03f), H(-hw1, hh * 0.55f, 0.03f), open * Vector3.Back);
		_k.Mat(paint);
		_k.Color = PaintAt(new Vector3(0, 0.7f, zR), 0.3f);
		B(hinge + open * new Vector3(0, -hh * 0.78f, 0.0f), new Vector3(hw0 * 2f - 0.28f, hh * 0.44f, 0.06f), 0.02f, 1f, open);
		_k.Color = PaintAt(new Vector3(0, roof, 0), 1f);
		B(hinge + open * new Vector3(0, -0.035f, 0.0f), new Vector3(hw0 * 2f - 0.06f, 0.07f, 0.06f), 0.02f, 1f, open);
		foreach (float side in new[] { -1f, 1f })
			B(hinge + open * new Vector3(side * (hw1 + 0.02f), -hh * 0.3f, 0.0f), new Vector3(0.07f, hh * 0.6f, 0.06f), 0.02f, 1f, open);
		_k.Color = Colors.White;
		_k.Mat(inner);
		_k.Box(hinge + open * new Vector3(0, -hh * 0.78f, -0.035f), new Vector3(hw0 * 2f - 0.34f, hh * 0.4f, 0.01f), 1f, open);
		_k.Mat(trim);
		B(hinge + open * new Vector3(0, -hh * 0.6f, 0.04f), new Vector3(0.16f, 0.03f, 0.02f), 0.008f, 1f, open);   // its handle
		_k.Mat(hub);
		foreach (float side in new[] { -1f, 1f })
			_k.Cylinder(new Vector3(side * (hw0 - 0.1f), belt - 0.05f, zR - 0.1f), hinge + open * new Vector3(side * (hw0 - 0.1f), -hh * 0.45f, -0.05f), 0.012f, 0.012f, 6, false, 1f);

		// ---- the wheels: 65 cm tyres with their sidewalls rolled, steel wheels under plain hubcaps, the arch liners
		foreach (float side in new[] { -1f, 1f })
			foreach (float z in wheelZ)
			{
				Vector3 c = new(side * (hw - 0.12f), axleY, z);
				Vector3 ax = new(side, 0, 0);
				_k.Mat(tyre);
				_k.Cylinder(c - ax * 0.08f, c + ax * 0.08f, tyreR, tyreR, 12, false, 1f);
				_k.Cylinder(c + ax * 0.08f, c + ax * 0.1f, tyreR, tyreR - 0.025f, 12, false, 1f);
				_k.Cylinder(c - ax * 0.08f, c - ax * 0.1f, tyreR, tyreR - 0.025f, 12, false, 1f);
				_k.Cylinder(c + ax * 0.1f, c + ax * 0.095f, tyreR - 0.025f, 0.2f, 12, false, 1f);
				_k.Cylinder(c - ax * 0.1f, c - ax * 0.095f, tyreR - 0.025f, 0.2f, 12, false, 1f);
				// the hubcap: a shallow dish with its vents, the centre boss
				_k.Mat(CarMat("car_hubcap", new Color(0.38f, 0.39f, 0.4f), 0.4f, 0.6f, false));
				HollowProps.In(_k, new Transform3D(new Basis(Vector3.Forward, -side * Mathf.Pi * 0.5f), c + ax * 0.085f), () =>
					HollowProps.Lathe(_k, Vector3.Zero, new Vector2[] { new(0.2f, 0f), new(0.19f, 0.012f), new(0.15f, 0.02f), new(0.12f, 0.028f), new(0.07f, 0.03f), new(0.05f, 0.04f), new(0f, 0.042f) }, 10, false, false));
				_k.Mat(CarMat("car_wheel", new Color(0.13f, 0.13f, 0.13f), 0.6f, 0.4f, false));
				for (int v = 0; v < 6; v++)
				{
					float a = v * Mathf.Tau / 6f;
					Vector3 vp = c + ax * 0.112f + new Vector3(0, Mathf.Sin(a) * 0.135f, Mathf.Cos(a) * 0.135f);
					_k.Quad(vp + new Vector3(0, -0.02f, -0.02f), vp + new Vector3(0, -0.02f, 0.02f), vp + new Vector3(0, 0.02f, 0.02f), vp + new Vector3(0, 0.02f, -0.02f), ax);   // a vent, dark
				}
				_k.Mat(trim);
				for (int s = 0; s < 8; s++)
				{
					float a0 = Mathf.Pi * s / 8f, a1 = Mathf.Pi * (s + 1) / 8f;
					Vector3 p0 = new(c.X - side * 0.02f, axleY + Mathf.Sin(a0) * (archR - 0.012f), z + Mathf.Cos(a0) * (archR - 0.012f));
					Vector3 p1 = new(c.X - side * 0.02f, axleY + Mathf.Sin(a1) * (archR - 0.012f), z + Mathf.Cos(a1) * (archR - 0.012f));
					_k.Beam(p0, p1, 0.26f, 0.02f, 1f, (p0 + p1) * 0.5f - new Vector3((p0.X + p1.X) * 0.5f, axleY, z));
				}
			}
		// the underside, dark, between the sills
		_k.Mat(trim);
		_k.Quad(new Vector3(-hw + 0.06f, sill + 0.01f, zN + 0.3f), new Vector3(hw - 0.06f, sill + 0.01f, zN + 0.3f), new Vector3(hw - 0.06f, sill + 0.01f, zR - 0.2f), new Vector3(-hw + 0.06f, sill + 0.01f, zR - 0.2f), Vector3.Down);
		_k.Color = Colors.White;

		Col(new Vector3(0, (0.15f + belt) * 0.5f, 0), new Vector3(W, belt - 0.15f, L));
		Col(new Vector3(0, (belt + roof) * 0.5f, (zWs + zSeat) * 0.5f), new Vector3(W - 0.1f, roof - belt, zSeat - zWs));
	}

	/// <summary>A smooth grid (the car's skin): normals from the grid, turned away from inside by <paramref name="outward"/>;
	/// each vertex's colour from its place and its normal.</summary>
	private void Grid(Vector3[,] p, System.Func<Vector3, Vector3, Color> colour, System.Func<Vector3, Vector3> outward)
	{
		int nu = p.GetLength(0), nv = p.GetLength(1);
		var n = new Vector3[nu, nv];
		var c = new Color[nu, nv];
		for (int i = 0; i < nu; i++)
			for (int j = 0; j < nv; j++)
			{
				Vector3 du = p[Mathf.Min(i + 1, nu - 1), j] - p[Mathf.Max(i - 1, 0), j];
				Vector3 dv = p[i, Mathf.Min(j + 1, nv - 1)] - p[i, Mathf.Max(j - 1, 0)];
				Vector3 cr = du.Cross(dv);
				if (cr.LengthSquared() < 1e-12f) cr = outward(p[i, j]);
				cr = cr.Normalized();
				if (cr.Dot(outward(p[i, j])) < 0f) cr = -cr;
				n[i, j] = cr;
				c[i, j] = colour(p[i, j], cr);
			}
		for (int i = 0; i + 1 < nu; i++)
			for (int j = 0; j + 1 < nv; j++)
			{
				var u0 = new Vector2(p[i, j].Z, p[i, j].Y); var u1 = new Vector2(p[i + 1, j].Z, p[i + 1, j].Y);
				var u2 = new Vector2(p[i + 1, j + 1].Z, p[i + 1, j + 1].Y); var u3 = new Vector2(p[i, j + 1].Z, p[i, j + 1].Y);
				_k.TriC(p[i, j], p[i + 1, j], p[i + 1, j + 1], n[i, j], n[i + 1, j], n[i + 1, j + 1], u0, u1, u2, c[i, j], c[i + 1, j], c[i + 1, j + 1]);
				_k.TriC(p[i, j], p[i + 1, j + 1], p[i, j + 1], n[i, j], n[i + 1, j + 1], n[i, j + 1], u0, u2, u3, c[i, j], c[i + 1, j + 1], c[i, j + 1]);
			}
	}

	/// <summary>A flat-faced grid facing <paramref name="normal"/> in the kit's colour (the headliner).</summary>
	private void GridFlat(Vector3[,] p, Vector3 normal)
	{
		for (int i = 0; i + 1 < p.GetLength(0); i++)
			for (int j = 0; j + 1 < p.GetLength(1); j++)
				_k.Quad(p[i, j], p[i + 1, j], p[i + 1, j + 1], p[i, j + 1], normal);
	}
}
