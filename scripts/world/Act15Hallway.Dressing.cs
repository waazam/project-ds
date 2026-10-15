using System.Collections.Generic;
using Godot;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the old-and-abandoned pass; Dan: "old and abandoned ... not sci-fi") Act 15's hall dressed as an old
/// hospital corridor gone to ruin, decades shut: the walls' two-tone paint on block (<c>hospital_wall.gdshader</c>), a
/// bump rail down both sides on its brackets (a length missing here, one hanging off), a perished rubber base, ward
/// doors with wired-glass panels and their number plates, fire cabinets with their glass gone and their extinguishers
/// missing, enamel signs, paper trodden into the floor, vinyl tiles lifted, and stains, peel and scuffs by rule.
/// <para>None of it is in anyone's way (no collision: the hall is a body's width), and none of it touches the hall's own
/// dice: everything here is drawn from its own (red light, green light's timings come from the hall's; see Build).</para>
/// </summary>
public partial class Act15Hallway
{
	/// <summary>The ward doors: the side (-1 left, +1 right), where along the hall, the wall's half-width there.</summary>
	private readonly List<(float s, float z, float halfW)> _doors = new();

	private static Shader _wallShader;

	/// <summary>The hall's walls (old painted block, two tones at a dado line), for a floor at world y <paramref name="floorY"/>.</summary>
	private static ShaderMaterial HallWallMat(float floorY)
	{
		_wallShader ??= GD.Load<Shader>("res://assets/shaders/hospital_wall.gdshader");
		var m = new ShaderMaterial { Shader = _wallShader, ResourceName = "hw_hospital_wall" };
		m.SetShaderParameter("block_tex", SurfaceSets.Tex("ph_concrete_wall_004"));
		m.SetShaderParameter("block_nor", SurfaceSets.Tex("ph_concrete_wall_004", "normal"));
		m.SetShaderParameter("bare_tex", SurfaceSets.Tex("ph_concrete_wall_006"));
		m.SetShaderParameter("noise_tex", RoomSurface.Noise);
		m.SetShaderParameter("floor_y", floorY);
		m.SetShaderParameter("top_y", floorY + H2);
		m.SetShaderParameter("seed", 15f);
		return m;
	}

	private void BuildDressing(StaticBody3D body)
	{
		var rng = new RandomNumberGenerator { Seed = 15141 };
		float z1 = Part1 + Taper;
		// part 2's doors: in the bays between its pilasters (every 8 m from 154), never by where he first stands, never by the end
		for (float z = z1 + 8f; z < End - 14f; z += 8f)
			if (Mathf.Abs(z - ShadowZ) > 7f && rng.Randf() < 0.24f)
				_doors.Add((rng.Randf() < 0.5f ? -1f : 1f, z, W2 * 0.5f));

		var leaf = new MeshKit(); leaf.Mat(HospitalTextures.DoorMat);
		var hw = new MeshKit(); hw.Mat(HospitalTextures.PlateMat);
		var frame = new MeshKit(); frame.Mat(ServiceDoor.FrameMat);
		var glass = new MeshKit(); glass.Mat(HospitalTextures.GlassMat);
		var signs = new MeshKit(); signs.Mat(HospitalTextures.EnamelMat);
		var backs = new MeshKit(); backs.Mat(HospitalTextures.SignBackMat);
		var red = new MeshKit(); red.Mat(HospitalTextures.RedMat);
		var rail = new MeshKit(); rail.Mat(HospitalTextures.RailMat);
		var cove = new MeshKit(); cove.Mat(HospitalTextures.CoveMat);
		var floorBits = new MeshKit(); floorBits.Mat(HospitalTextures.PaperMat);
		var tiles = new MeshKit(); tiles.Mat(HospitalTextures.CoveMat);
		foreach (var k in new[] { leaf, hw, frame, glass, signs, backs, red, rail, cove, floorBits, tiles }) k.Color = Colors.White;
		var labels = new Node3D { Name = "Signs" };
		AddChild(labels);
		var font = new SystemFont { FontNames = new[] { "Arial Narrow", "Arial", "Liberation Sans" }, FontWeight = 700 };
		void Text(string t, Vector3 at, Vector3 n, float px, Color c)
		{
			var l = new Label3D
			{
				Text = t, Font = font, FontSize = 48, PixelSize = px, Modulate = c, OutlineSize = 0, Shaded = true,
				Position = at, DoubleSided = false,
			};
			labels.AddChild(l);
			// (a Label3D faces +Z: turned to face out of its wall)
			l.Basis = Basis.LookingAt(-n, Vector3.Up);
		}

		// ---- the doors
		int room = 101;
		var cuts = new List<(float s, float a, float b)>();   // what the rail and the base break round
		foreach (var (s, zc, hwid) in _doors)
		{
			const float w = 1.0f, h = 2.1f, t = 0.04f;
			Vector3 n = new(-s, 0, 0);
			// the leaf, its face 3.5 cm off the wall's, its hinge at the near end
			leaf.Xf = hw.Xf = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(s * (hwid - 0.03f), 0.005f, zc - w * 0.5f));
			if (s > 0) leaf.Xf = hw.Xf = new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), new Vector3(s * (hwid - 0.03f), 0.005f, zc + w * 0.5f));
			ServiceDoor.Leaf(leaf, hw, w - 0.02f, h - 0.02f, t, 1.0f);
			leaf.Xf = hw.Xf = Transform3D.Identity;
			// the vision panel: wired glass in a steel bead, at eye height
			Vector3 face = new(s * (hwid - 0.03f - t * 0.5f - 0.002f), 0, zc);
			float gz0 = zc - 0.16f, gz1 = zc + 0.16f, gy0 = 1.22f, gy1 = 1.78f;
			glass.Quad(new Vector3(face.X, gy0, gz0), new Vector3(face.X, gy0, gz1), new Vector3(face.X, gy1, gz1), new Vector3(face.X, gy1, gz0), n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
			hw.Color = Colors.White * 0.7f;
			foreach (var (c, sz) in new[]
			{
				(new Vector3(face.X - s * 0.006f, gy0, zc), new Vector3(0.012f, 0.025f, 0.36f)), (new Vector3(face.X - s * 0.006f, gy1, zc), new Vector3(0.012f, 0.025f, 0.36f)),
				(new Vector3(face.X - s * 0.006f, (gy0 + gy1) * 0.5f, gz0), new Vector3(0.012f, 0.58f, 0.025f)), (new Vector3(face.X - s * 0.006f, (gy0 + gy1) * 0.5f, gz1), new Vector3(0.012f, 0.58f, 0.025f)),
			})
				BuildKit.Box(hw, c, sz);
			hw.Color = Colors.White;
			// the frame's face round it, proud of the wall
			frame.Color = Colors.White * rng.RandfRange(0.75f, 1f);
			float fx = s * (hwid - 0.025f);
			BuildKit.Box(frame, new Vector3(fx, h * 0.5f + 0.03f, zc - w * 0.5f - 0.03f), new Vector3(0.05f, h + 0.06f, 0.07f));
			BuildKit.Box(frame, new Vector3(fx, h * 0.5f + 0.03f, zc + w * 0.5f + 0.03f), new Vector3(0.05f, h + 0.06f, 0.07f));
			BuildKit.Box(frame, new Vector3(fx, h + 0.035f, zc), new Vector3(0.05f, 0.07f, w + 0.13f));
			// its number on a plate over it
			BuildKit.Box(hw, new Vector3(s * (hwid - 0.006f), h + 0.22f, zc), new Vector3(0.012f, 0.09f, 0.22f));
			Text($"C-{room}", new Vector3(s * (hwid - 0.0135f), h + 0.22f, zc), n, 0.0016f, new Color(0.1f, 0.09f, 0.07f));
			room += rng.RandiRange(1, 3);
			cuts.Add((s, zc - w * 0.5f - 0.1f, zc + w * 0.5f + 0.1f));
		}

		// ---- the fire cabinets: steel, red, the glass gone from most, the extinguisher gone from some
		var cabinets = new List<(float s, float z, float hw)>();
		for (float z = 38f; z < End - 20f; z += rng.RandfRange(34f, 52f))
		{
			bool p1 = z < Part1 - 2f;
			if (!p1 && z < z1 + 4f) continue;
			float zc = p1 ? Mathf.Round(z / 6f) * 6f + 1.6f : Mathf.Round((z - z1) / 8f) * 8f + z1 + 2.2f;
			float s = rng.Randf() < 0.5f ? -1f : 1f;
			bool clash = false;
			foreach (var d in _doors) if (d.s == s && Mathf.Abs(d.z - zc) < 1.4f) clash = true;
			if (clash) s = -s;
			cabinets.Add((s, zc, p1 ? W1 * 0.5f : W2 * 0.5f));
		}
		var cabGlass = new StandardMaterial3D
		{
			ResourceName = "hw_cabinet_glass", AlbedoColor = new Color(0.5f, 0.52f, 0.48f, 0.22f), Roughness = 0.08f, MetallicSpecular = 0.9f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		var cg = new MeshKit(); cg.Mat(cabGlass); cg.Color = Colors.White;
		var dark = new MeshKit(); dark.Mat(HospitalTextures.SignBackMat); dark.Color = Colors.White * 0.4f;
		foreach (var (s, zc, hwid) in cabinets)
		{
			const float cw = 0.34f, ch = 0.68f, cd = 0.13f, cy = 1.2f;
			float wx = s * hwid;
			red.Color = Colors.White * rng.RandfRange(0.7f, 1f);
			// the box's sides, top and bottom (the front open), and its back dark inside
			BuildKit.Box(red, new Vector3(wx - s * cd * 0.5f, cy, zc - cw * 0.5f + 0.01f), new Vector3(cd, ch, 0.02f));
			BuildKit.Box(red, new Vector3(wx - s * cd * 0.5f, cy, zc + cw * 0.5f - 0.01f), new Vector3(cd, ch, 0.02f));
			BuildKit.Box(red, new Vector3(wx - s * cd * 0.5f, cy + ch * 0.5f - 0.01f, zc), new Vector3(cd, 0.02f, cw));
			BuildKit.Box(red, new Vector3(wx - s * cd * 0.5f, cy - ch * 0.5f + 0.01f, zc), new Vector3(cd, 0.02f, cw));
			BuildKit.Box(dark, new Vector3(wx - s * 0.006f, cy, zc), new Vector3(0.01f, ch - 0.02f, cw - 0.02f));
			float r = rng.Randf();
			if (r < 0.55f)
			{
				// an extinguisher still in it, its hose hung round
				Vector3 foot = new(wx - s * 0.07f, cy - ch * 0.5f + 0.03f, zc);
				red.Color = Colors.White * rng.RandfRange(0.6f, 0.9f);
				red.Cylinder(foot, foot + Vector3.Up * 0.42f, 0.07f, 0.07f, 12, true);
				red.Cylinder(foot + Vector3.Up * 0.42f, foot + Vector3.Up * 0.48f, 0.07f, 0.03f, 10, true);
				dark.Cylinder(foot + Vector3.Up * 0.47f, foot + new Vector3(0, 0.3f, 0.09f), 0.012f, 0.012f, 5, false);
			}
			if (rng.Randf() < 0.35f)
			{
				// its glass still in (cracked, but in)
				float gx = wx - s * (cd - 0.005f);
				cg.Quad(new Vector3(gx, cy - ch * 0.5f + 0.02f, zc - cw * 0.5f + 0.02f), new Vector3(gx, cy - ch * 0.5f + 0.02f, zc + cw * 0.5f - 0.02f),
					new Vector3(gx, cy + ch * 0.5f - 0.02f, zc + cw * 0.5f - 0.02f), new Vector3(gx, cy + ch * 0.5f - 0.02f, zc - cw * 0.5f + 0.02f), new Vector3(-s, 0, 0));
			}
			else if (rng.Randf() < 0.5f)
				// its glass on the floor under it, in pieces
				for (int i = 0; i < 6; i++)
					BuildKit.Box(cg, new Vector3(wx - s * rng.RandfRange(0.05f, 0.45f), 0.003f, zc + rng.RandfRange(-0.3f, 0.3f)), new Vector3(rng.RandfRange(0.03f, 0.1f), 0.004f, rng.RandfRange(0.03f, 0.12f)), 1f, BuildKit.Face.NY, new Basis(Vector3.Up, rng.RandfRange(0f, 3f)));
			Text("FIRE", new Vector3(wx - s * 0.003f, cy + ch * 0.5f + 0.1f, zc), new Vector3(-s, 0, 0), 0.0028f, new Color(0.45f, 0.08f, 0.05f, 0.85f));
			cuts.Add((s, zc - cw * 0.5f - 0.05f, zc + cw * 0.5f + 0.05f));
		}

		// ---- the bump rail and the rubber base, bay by bay between the pilasters, broken round doors and cabinets
		var bays = new List<(float s, float a, float b, float hw, float stand)>();
		foreach (float s in new[] { -1f, 1f })
		{
			for (float z = 3f; z < Part1 - 3f; z += 6f) bays.Add((s, z + 0.25f, z + 6f - 0.25f, W1 * 0.5f - 0.0f, 0f));
			bays.Add((s, z1 + 0.1f, z1 + 4f - 0.38f, W2 * 0.5f, 0f));
			for (float z = z1 + 4f; z < End - 0.5f; z += 8f) bays.Add((s, z + 0.38f, Mathf.Min(z + 8f - 0.38f, End - 0.1f), W2 * 0.5f, 0f));
		}
		foreach (var (s, a0, b0, hwid, _) in bays)
		{
			// the bay cut round what's in it
			var runs = new List<(float a, float b)> { (a0, b0) };
			foreach (var c in cuts)
			{
				if (c.s != s) continue;
				var next = new List<(float a, float b)>();
				foreach (var (a, b) in runs)
				{
					if (c.b <= a || c.a >= b) { next.Add((a, b)); continue; }
					if (c.a - a > 0.15f) next.Add((a, c.a));
					if (b - c.b > 0.15f) next.Add((c.b, b));
				}
				runs = next;
			}
			foreach (var (a, b) in runs)
			{
				float len = b - a, wx = s * hwid;
				// the base: perished rubber, a piece of it peeled off now and then
				if (rng.Randf() > 0.08f)
				{
					cove.Color = Colors.White * rng.RandfRange(0.7f, 1.1f);
					BuildKit.Box(cove, new Vector3(wx - s * 0.006f, 0.05f, (a + b) * 0.5f), new Vector3(0.012f, 0.1f, len));
				}
				// the rail: on brackets every metre and a half; a length gone (its brackets left), or hanging off one
				float roll = rng.Randf();
				for (float u = a + 0.25f; u < b - 0.1f; u += 1.5f)
				{
					hw.Color = Colors.White * 0.55f;
					BuildKit.Box(hw, new Vector3(wx - s * 0.025f, 0.9f, u), new Vector3(0.05f, 0.03f, 0.03f));
				}
				hw.Color = Colors.White;
				if (roll < 0.07f || len < 0.4f) continue;
				rail.Color = Colors.White * rng.RandfRange(0.75f, 1.05f);
				var size = new Vector3(0.03f, 0.13f, len - 0.04f);
				if (roll < 0.13f && len > 1.6f)
				{
					// come away at one end and hanging from the other's bracket
					float ang = rng.RandfRange(0.25f, 0.6f) * (rng.Randf() < 0.5f ? -1f : 1f);
					float pivot = ang > 0 ? b - 0.3f : a + 0.3f;
					var rot = new Basis(Vector3.Right, ang);
					Vector3 piv = new(wx - s * 0.065f, 0.9f, pivot);
					Vector3 c = piv + rot * new Vector3(0, 0, (a + b) * 0.5f - pivot);
					BuildKit.Box(rail, c, size, 1f, BuildKit.Face.None, rot);
				}
				else BuildKit.Box(rail, new Vector3(wx - s * 0.065f, 0.9f, (a + b) * 0.5f), size);
			}
		}

		// ---- the signs: enamel, some out from the wall to be read down the hall
		(string text, float z)[] hung =
		{
			("WARD C", 30f), ("NO SMOKING", 62f), ("RADIOLOGY  >", 96f), ("QUIET PLEASE", 178f), ("< WARD D", 214f), ("ISOLATION", 262f),
			("STAFF ONLY", 305f), ("PATHOLOGY  >", 350f), ("NO ADMITTANCE", 402f), ("WARD E", 446f),
		};
		foreach (var (text, z0) in hung)
		{
			bool p1 = z0 < Part1;
			float hwid = p1 ? W1 * 0.5f : W2 * 0.5f;
			float zc = p1 ? Mathf.Round(z0 / 6f) * 6f + 0.6f : Mathf.Round((z0 - z1) / 8f) * 8f + z1 + 0.9f;
			float s = rng.Randf() < 0.5f ? -1f : 1f;
			bool flag = rng.Randf() < 0.55f;
			Color ink = new(0.12f, 0.11f, 0.09f);
			if (flag)
			{
				// a flag sign out from the wall on a bracket, both faces lettered; one or two hang crooked off a broken bracket
				float tilt = rng.Randf() < 0.25f ? rng.RandfRange(0.12f, 0.35f) : 0f;
				float y = 2.45f, wout = 0.62f;
				Vector3 c = new(s * (hwid - 0.05f - wout * 0.5f), y, zc);
				var rot = new Basis(Vector3.Forward, s * tilt);
				backs.Color = Colors.White * 0.7f;
				backs.Cylinder(new Vector3(s * hwid, y + 0.12f, zc), new Vector3(s * (hwid - wout - 0.06f), y + 0.12f, zc), 0.008f, 0.008f, 4, false);
				signs.Color = Colors.White * rng.RandfRange(0.65f, 0.95f);
				BuildKit.Box(signs, c, new Vector3(wout, 0.16f, 0.018f), 1f, BuildKit.Face.None, rot);
				foreach (float side in new[] { -1f, 1f })
				{
					var l = new Label3D
					{
						Text = text, Font = font, FontSize = 48, PixelSize = 0.0024f, Modulate = ink, OutlineSize = 0, Shaded = true,
					};
					labels.AddChild(l);
					l.Transform = new Transform3D(rot * new Basis(Vector3.Up, side > 0 ? 0f : Mathf.Pi), c + rot * new Vector3(0, 0, side * 0.0105f));
				}
			}
			else
			{
				float y = rng.RandfRange(1.75f, 2.2f);
				Vector3 at = new(s * (hwid - 0.006f), y, zc);
				signs.Color = Colors.White * rng.RandfRange(0.65f, 0.95f);
				BuildKit.Box(signs, at, new Vector3(0.012f, 0.15f, 0.56f));
				Text(text, at + new Vector3(-s * 0.0075f, 0, 0), new Vector3(-s, 0, 0), 0.0022f, ink);
			}
		}

		// ---- the floor: paper trodden into it along the walls, tiles lifted to the black adhesive
		Vector3 org = GlobalPosition;
		for (int i = 0; i < 150; i++)
		{
			float z = rng.RandfRange(2f, End - 2f);
			float hwid = z < Part1 ? W1 * 0.5f : z < z1 ? Mathf.Lerp(W1, W2, (z - Part1) / Taper) * 0.5f : W2 * 0.5f;
			float s = rng.Randf() < 0.5f ? -1f : 1f;
			// mostly against the walls (kicked there), a few out in the way
			float x = s * (hwid - (rng.Randf() < 0.8f ? rng.RandfRange(0.03f, 0.2f) : rng.RandfRange(0.25f, hwid)));
			int sheets = rng.Randf() < 0.3f ? rng.RandiRange(2, 4) : 1;
			for (int p = 0; p < sheets; p++)
			{
				var rot = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Right, rng.RandfRange(-0.06f, 0.06f));
				floorBits.Color = new Color(1f, 1f, 1f) * rng.RandfRange(0.5f, 1f);
				BuildKit.Box(floorBits, new Vector3(x + rng.RandfRange(-0.1f, 0.1f), 0.002f + p * 0.0015f, z + rng.RandfRange(-0.15f, 0.15f)), new Vector3(0.21f, 0.0012f, 0.297f), 1f, BuildKit.Face.NY, rot);
			}
		}
		for (int i = 0; i < 26; i++)
		{
			// a patch of tiles gone, on the floor's own 30 cm grid
			float z = rng.RandfRange(4f, End - 4f);
			float hwid = z < Part1 ? W1 * 0.5f : z < z1 ? Mathf.Lerp(W1, W2, (z - Part1) / Taper) * 0.5f : W2 * 0.5f;
			int n = rng.RandiRange(1, 4);
			float gx = rng.RandfRange(-hwid + 0.1f, hwid - 0.4f) + org.X, gz = z + org.Z;
			gx = Mathf.Floor(gx / 0.3f) * 0.3f; gz = Mathf.Floor(gz / 0.3f) * 0.3f;
			for (int t = 0; t < n; t++)
			{
				float tx = gx + (t % 2) * 0.3f, tz = gz + (t / 2) * 0.3f;
				Vector3 c = new(tx + 0.15f - org.X, 0.0012f, tz + 0.15f - org.Z);
				if (Mathf.Abs(c.X) > hwid - 0.16f) continue;
				tiles.Color = new Color(0.55f, 0.45f, 0.32f) * rng.RandfRange(0.7f, 1.2f);
				BuildKit.Box(tiles, c, new Vector3(0.296f, 0.0024f, 0.296f), 1f, BuildKit.Face.NY);
			}
		}

		leaf.CommitTo(this, "WardDoors", true);
		hw.CommitTo(this, "WardDoorHardware", false);
		frame.CommitTo(this, "WardDoorFrames", false);
		glass.CommitTo(this, "WardDoorGlass", false);
		signs.CommitTo(this, "SignPlates", false);
		backs.CommitTo(this, "SignBrackets", false);
		red.CommitTo(this, "FireCabinets", false);
		dark.CommitTo(this, "FireCabinetBacks", false);
		cg.CommitTo(this, "FireCabinetGlass", false);
		rail.CommitTo(this, "BumpRail", false);
		cove.CommitTo(this, "CoveBase", false);
		floorBits.CommitTo(this, "FloorPaper", false);
		tiles.CommitTo(this, "LiftedTiles", false);

		// ---- stains, peel, scuffs, mould by rule along both walls (a decal every few metres, faded out with distance)
		var decals = new Node3D { Name = "HallDecals" };
		AddChild(decals);
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-decals") >= 0) return;
		string[] low = { "tide", "scuff", "mould", "tide", "scuff" }, mid = { "peel", "peel", "crack", "streak", "seep" };
		for (float z = 4f; z < End - 3f; z += rng.RandfRange(3.5f, 7.5f))
		{
			if (z > Part1 - 1f && z < z1 + 1f) continue;   // (the taper's walls lean: nothing to lie flat on)
			float hwid = z < Part1 ? W1 * 0.5f : W2 * 0.5f;
			float s = rng.Randf() < 0.5f ? -1f : 1f;
			Vector3 n = new(-s, 0, 0);
			bool clear = true;
			foreach (var c in cuts) if (c.s == s && z > c.a - 0.4f && z < c.b + 0.4f) clear = false;
			if (!clear) continue;
			if (rng.Randf() < 0.6f)
			{
				string tex = low[rng.RandiRange(0, low.Length - 1)];
				float hgt = tex == "mould" ? 0.7f : 0.5f;
				DecalDresser.Place(decals, tex, new Vector3(s * hwid, hgt * 0.5f + 0.02f, z), n, new Vector2(rng.RandfRange(0.7f, 1.6f), hgt), rng.RandfRange(0.55f, 0.8f), 0f, 0.1f);
			}
			else
			{
				string tex = mid[rng.RandiRange(0, mid.Length - 1)];
				float hgt = tex == "streak" ? rng.RandfRange(1.5f, 2.6f) : rng.RandfRange(0.4f, 0.9f);
				float y = tex == "streak" ? 3.15f - hgt * 0.5f : rng.RandfRange(1.3f, 2.6f);
				DecalDresser.Place(decals, tex, new Vector3(s * hwid, y, z), n, new Vector2(rng.RandfRange(0.4f, 1.0f), hgt), rng.RandfRange(0.55f, 0.85f), 0f, 0.1f);
			}
			// now and then a stain on the floor too
			if (rng.Randf() < 0.18f)
				DecalDresser.Place(decals, rng.Randf() < 0.6f ? "floor" : "puddle", new Vector3(rng.RandfRange(-hwid + 0.4f, hwid - 0.4f), 0.01f, z + 1f), Vector3.Up,
					new Vector2(rng.RandfRange(0.6f, 1.3f), rng.RandfRange(0.5f, 1.1f)), rng.RandfRange(0.45f, 0.7f), rng.RandfRange(0f, Mathf.Tau), 0.06f);
		}
	}

	/// <summary>(2026-10-14h) The closet weathered by the kit: its corners and its foot darker, the light's pool round the
	/// bulb, the shelves' dark behind and under them, damp up from the floor, the stains and cracks by rule.</summary>
	private RoomShade ClosetShade(float z0, float z1, float h) =>
		new RoomShade(-ClosetHalf, ClosetHalf, z0, z1, 0f, h) { Dim = 0.78f }
			.Under(new Vector3(0, 0, z1 - 0.2f), new Vector3(ClosetHalf * 2f, 2.2f, 0.4f), 0.4f, 0.4f)
			.Under(new Vector3(-ClosetHalf + 0.18f, 0, (z0 + z1) * 0.5f + 0.2f), new Vector3(0.4f, 2.2f, ClosetDepth - 0.8f), 0.4f, 0.4f)
			.Light(new Vector3(0, 2.35f, (z0 + z1) * 0.5f), 2.6f)
			.Damp(new Vector3(ClosetHalf, 0.2f, z1 - 0.4f), 1.1f, 0.8f)
			.Path(new Vector3(0, 0, z0), new Vector3(0, 0, z1 - 0.6f), 0.7f, 0.6f);

	private void DecorateCloset(float z0, float z1, float h)
	{
		var spec = new DecalDresser.RoomSpec
		{
			Box = new Aabb(new Vector3(-ClosetHalf, 0f, z0), new Vector3(ClosetHalf * 2f, h, z1 - z0)), Seed = 16, Amount = 0.8f, Painted = true,
		};
		spec.Doors.Add((new Vector3(0, 0, z0), Vector3.Back, DoorWidth, 2.15f));
		// (the shelves' walls: nothing would show behind the stock)
		spec.SkipWalls.Add(Vector3.Forward);
		spec.SkipWalls.Add(Vector3.Right);
		spec.Keep.Add((new Vector3(DoorWidth * 0.5f + 0.25f, 1.25f, z0), 0.25f));   // the switch
		DecalDresser.Room(this, spec);
	}
}
