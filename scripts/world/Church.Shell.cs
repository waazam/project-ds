using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>The church's fabric: floors, piers and arcades, the walls with every window and door cut
/// through them, the vaults and their ribs.</summary>
public partial class Church
{
	/// <summary>The crypt stairs' openings in the nave floor (x from, x to, z from, z to), one either side of the aisle.</summary>
	public static readonly (float x0, float x1, float z0, float z1)[] StairOpenings = { (-5.4f, -3f, 31f, 40f), (3f, 5.4f, 31f, 40f) };
	public const float TransWestT = 0.62f;
	/// <summary>The right aisle's bay whose window is broken (snow drifted in under it).</summary>
	public const int BrokenBay = 3;

	public static float TransVaultY(float z) => Spring + Pointed(z - (NaveEnd + CrossEnd) * 0.5f, 8f, NaveR);

	// ------------------------------------------------------------------ floors

	private void BuildFloors()
	{
		var k = new MeshKit();
		var marble = ChurchTextures.CobbleMat;
		var red = ChurchTextures.RedMarbleMat;
		void Top(Material m, float x0, float x1, float z0, float z1, float y = 0f, StaticBody3D body = null)
		{
			if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) return;
			k.Mat(m);
			k.Color = Colors.White;
			k.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.Up,
				new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
			Collide(body ?? _marbleBody, new Vector3((x0 + x1) * 0.5f, y - 0.3f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.6f, z1 - z0));
		}
		// the nave and aisles (the aisles close at the transept's west wall), round the crypt stairs' openings
		const float medZ0 = 45.3f, medZ1 = 47.7f;
		foreach (float s in new[] { -1f, 1f })
		{
			float xa = s < 0 ? -AisleOuter : 1.2f, xb = s < 0 ? -1.2f : AisleOuter, zEnd = NaveEnd - TransWestT;
			var (ox0, ox1, oz0, oz1) = StairOpenings[s < 0 ? 0 : 1];
			// strips across x, split round the opening
			Top(marble, xa, ox0, 0f, zEnd);
			Top(marble, ox0, ox1, 0f, oz0);
			Top(marble, ox0, ox1, oz1, zEnd);
			Top(marble, ox1, xb, 0f, zEnd);
		}
		// the red aisle down the middle, and the medallion let into it
		Top(red, -1.2f, 1.2f, 0f, medZ0);
		Top(red, -1.2f, 1.2f, medZ1, NaveEnd);
		k.Mat(ChurchTextures.MedallionMat);
		k.Quad(new Vector3(-1.2f, 0, medZ0), new Vector3(1.2f, 0, medZ0), new Vector3(1.2f, 0, medZ1), new Vector3(-1.2f, 0, medZ1), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		Collide(_marbleBody, new Vector3(0, -0.3f, (medZ0 + medZ1) * 0.5f), new Vector3(2.4f, 0.6f, medZ1 - medZ0));
		// the last strip of nave under the crossing arch, and the crossing and transepts
		Top(marble, -NaveHalf, NaveHalf, NaveEnd - TransWestT, NaveEnd);
		Top(marble, -TransHalf, TransHalf, NaveEnd, CrossEnd);
		// thresholds through the thick walls: the great door, the tower's door, the vestry's and the chapel's
		float mid = (NaveEnd + CrossEnd) * 0.5f;
		Top(red, -2.6f, 2.6f, -1.5f, 0f);
		Top(red, 10.9f, 12.1f, -1.5f, 0f);
		Top(red, -TransHalf - WallT, -TransHalf, mid - 1.2f, mid + 1.2f);
		Top(red, TransHalf, TransHalf + WallT, mid - 1.2f, mid + 1.2f);
		// a red cross laid in the crossing's floor
		k.Mat(red);
		foreach (var (x0, x1, z0, z1) in new[] { (-6f, 6f, 59.9f, 61.1f), (-0.6f, 0.6f, 54.5f, 59.9f), (-0.6f, 0.6f, 61.1f, 66.5f) })
			k.Quad(new Vector3(x0, 0.004f, z0), new Vector3(x1, 0.004f, z0), new Vector3(x1, 0.004f, z1), new Vector3(x0, 0.004f, z1), Vector3.Up,
				new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
		// the rims of the stairs' openings, and a stone balustrade round them
		k.Mat(ChurchTextures.AshlarMat);
		foreach (var (x0, x1, z0, z1) in StairOpenings)
		{
			k.Color = Colors.White * 0.9f;
			k.Quad(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, -0.6f, z0), new Vector3(x0, -0.6f, z0), Vector3.Back);
			k.Quad(new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), new Vector3(x0, -0.6f, z1), new Vector3(x1, -0.6f, z1), Vector3.Forward);
			k.Quad(new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), new Vector3(x0, -0.6f, z0), new Vector3(x0, -0.6f, z1), Vector3.Right);
			k.Quad(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x1, -0.6f, z1), new Vector3(x1, -0.6f, z0), Vector3.Left);
			// a balustrade round three sides; the west end stays open, the way down
			Balustrade(k, new Vector3(x0 - 0.15f, 0, z1 + 0.15f), new Vector3(x1 + 0.15f, 0, z1 + 0.15f));
			Balustrade(k, new Vector3(x0 - 0.15f, 0, z0 + 0.4f), new Vector3(x0 - 0.15f, 0, z1 + 0.15f));
			Balustrade(k, new Vector3(x1 + 0.15f, 0, z0 + 0.4f), new Vector3(x1 + 0.15f, 0, z1 + 0.15f));
		}
		k.CommitTo(this, "Floors", false);
	}

	/// <summary>A low stone balustrade from a to b: a plinth, turned balusters, a moulded rail. Solid to walk into.</summary>
	private void Balustrade(MeshKit k, Vector3 a, Vector3 b)
	{
		Vector3 d = b - a;
		float len = d.Length();
		Vector3 dir = d / len;
		k.Mat(ChurchTextures.AshlarMat);
		k.Color = Colors.White * 0.95f;
		var basis = Basis.LookingAt(dir, Vector3.Up);
		Vector3 mid = (a + b) * 0.5f;
		BuildKit.Box(k, mid + Vector3.Up * 0.08f, new Vector3(0.26f, 0.16f, len + 0.26f), 1f, BuildKit.Face.NY, basis);
		BuildKit.Box(k, mid + Vector3.Up * 0.94f, new Vector3(0.3f, 0.12f, len + 0.3f), 1f, BuildKit.Face.None, basis);
		for (float t = 0.2f; t < len - 0.1f; t += 0.34f)
		{
			Vector3 p = a + dir * t;
			k.Cylinder(p + Vector3.Up * 0.16f, p + Vector3.Up * 0.42f, 0.05f, 0.085f, 8, false);
			k.Cylinder(p + Vector3.Up * 0.42f, p + Vector3.Up * 0.88f, 0.085f, 0.045f, 8, false);
		}
		Collide(_stone, mid + Vector3.Up * 0.5f, new Vector3(0.3f, 1.0f, len + 0.3f), basis);
	}

	// ------------------------------------------------------------------ piers and arcades

	private void BuildPiersAndArcades()
	{
		var k = new MeshKit();
		var ash = ChurchTextures.AshlarMat;
		k.Mat(ash);
		// the free-standing piers of the nave arcade, and the four big crossing piers
		foreach (float s in new[] { -1f, 1f })
		{
			for (int b = 1; b < NaveBays; b++) Pier(k, new Vector3(s * NaveHalf, 0, b * BayLen), 0.62f, PierTop);
			Pier(k, new Vector3(s * NaveHalf, 0, NaveEnd), 0.95f, PierTop + 1.5f);
			Pier(k, new Vector3(s * NaveHalf, 0, CrossEnd), 0.95f, PierTop + 1.5f);
			// half-piers against the west wall
			Pier(k, new Vector3(s * NaveHalf, 0, 0.35f), 0.5f, PierTop, half: true);
		}
		// the arcade wall over the piers, each side: pointed arches from pier to pier, the triforium band,
		// the clerestory's paired lancets, up to where the vault springs
		var glass = new[] { ChurchTextures.GlassMat(ChurchTextures.Lancet(1), 1.25f), ChurchTextures.GlassMat(ChurchTextures.Lancet(2), 1.25f), ChurchTextures.GlassMat(ChurchTextures.Lancet(3), 1.25f) };
		foreach (float s in new[] { -1f, 1f })
		{
			var holes = new List<Hole>();
			for (int b = 0; b < NaveBays; b++)
			{
				float z0 = b * BayLen + (b == 0 ? 0.85f : 0.62f), z1 = (b + 1) * BayLen - (b == NaveBays - 1 ? 0.95f : 0.62f);
				holes.Add(Hole.Lancet(z0, z1, PierTop, PierTop, null, 0.72f));
				float c = (b + 0.5f) * BayLen;
				holes.Add(Hole.Lancet(c - 1.85f, c - 0.35f, 18f, 22f, glass[b % 3], 1f));
				holes.Add(Hole.Lancet(c + 0.35f, c + 1.85f, 18f, 22f, glass[(b + 1) % 3], 1f));
			}
			// the chancel's side walls continue the line east of the crossing (their windows lower and taller)
			Vector3 origin = new(s * (NaveHalf - 0.45f), 0, 0);
			Wall(k, ash, origin, Vector3.Back, new Vector3(-s, 0, 0), 0.9f, 0f, NaveEnd - 0.95f, PierTop, _ => 28f, holes);
			// over the crossing's arches (north and south of the crossing): a wall from the arch up, open below
			// the crossing's arch into each transept arm springs where the vault does, and the wall over it
			// climbs to meet the transept's vault
			var cross = new List<Hole> { Hole.Lancet(NaveEnd + 0.95f, CrossEnd - 0.95f, PierTop + 1.5f, Spring, null, 0.62f) };
			Wall(k, ash, origin, Vector3.Back, new Vector3(-s, 0, 0), 0.9f, NaveEnd - 0.95f, CrossEnd + 0.95f, PierTop + 1.5f, u => Mathf.Max(28f, TransVaultY(u) + 0.4f), cross);
			// the triforium: two moulded courses and a small arcade of colonnettes between them
			float x = s * (NaveHalf - 0.45f - 0.08f);
			foreach (float y in new[] { ArcadeTop, TriforiumTop })
				BuildKit.Box(k, new Vector3(x, y, NaveEnd * 0.5f), new Vector3(0.16f, 0.26f, NaveEnd - 1.9f), 1f, BuildKit.Face.None);
			for (int b = 0; b < NaveBays; b++)
				for (int i = 0; i <= 6; i++)
				{
					float z = b * BayLen + 0.9f + i * (BayLen - 1.8f) / 6f;
					k.Cylinder(new Vector3(x, ArcadeTop + 0.13f, z), new Vector3(x, TriforiumTop - 0.55f, z), 0.07f, 0.07f, 8, false);
					if (i < 6)
					{
						float zn = b * BayLen + 0.9f + (i + 1) * (BayLen - 1.8f) / 6f, zm = (z + zn) * 0.5f;
						k.Beam(new Vector3(x, TriforiumTop - 0.55f, z), new Vector3(x, TriforiumTop - 0.22f, zm), 0.1f, 0.1f);
						k.Beam(new Vector3(x, TriforiumTop - 0.22f, zm), new Vector3(x, TriforiumTop - 0.55f, zn), 0.1f, 0.1f);
					}
				}
			// the vaulting shafts: three slender shafts up the wall from each pier to the springing
			for (int b = 0; b <= NaveBays; b++)
			{
				float z = b * BayLen;
				float y0 = b == NaveBays ? PierTop + 1.5f : PierTop;
				foreach (float dz in new[] { -0.22f, 0f, 0.22f })
				{
					if (b == 0 && dz < 0) continue;
					float zz = Mathf.Clamp(z + dz, 0.15f, CrossEnd);
					k.Cylinder(new Vector3(s * (NaveHalf - 0.62f), y0, zz), new Vector3(s * (NaveHalf - 0.62f), Spring, zz), 0.1f, 0.1f, 8, false);
				}
				BuildKit.Box(k, new Vector3(s * (NaveHalf - 0.62f), Spring, Mathf.Max(z, 0.3f)), new Vector3(0.36f, 0.22f, 0.7f), 1f);
			}
		}
		k.CommitTo(this, "Arcades", true).CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
	}

	/// <summary>A clustered pier: an octagonal plinth, a round core ringed with eight shafts, a moulded capital.</summary>
	private void Pier(MeshKit k, Vector3 at, float r, float h, bool half = false)
	{
		k.Mat(ChurchTextures.AshlarMat);
		k.Color = Colors.White * 0.96f;
		k.Cylinder(at, at + Vector3.Up * 0.55f, r * 1.55f, r * 1.45f, 8, true);
		k.Cylinder(at + Vector3.Up * 0.55f, at + Vector3.Up * (h - 0.45f), r, r, 12, false);
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f;
			Vector3 o = new(Mathf.Cos(a) * r * 1.08f, 0, Mathf.Sin(a) * r * 1.08f);
			if (half && o.Z < -0.01f) continue;
			float sr = i % 2 == 0 ? r * 0.27f : r * 0.2f;
			k.Cylinder(at + o + Vector3.Up * 0.55f, at + o + Vector3.Up * (h - 0.45f), sr, sr, 8, false);
		}
		k.Color = Colors.White * 0.9f;
		k.Cylinder(at + Vector3.Up * (h - 0.45f), at + Vector3.Up * (h - 0.1f), r * 1.2f, r * 1.55f, 12, false);
		k.Cylinder(at + Vector3.Up * (h - 0.1f), at + Vector3.Up * h, r * 1.6f, r * 1.6f, 8, true);
		_stone.AddChild(new CollisionShape3D { Position = at + Vector3.Up * (h * 0.5f), Shape = new CylinderShape3D { Radius = r * 1.35f, Height = h } });
	}

	// ------------------------------------------------------------------ the outer walls

	private void BuildWalls()
	{
		var k = new MeshKit();
		var ash = ChurchTextures.AshlarMat;
		var quarry = ChurchTextures.QuarryMat;
		var lancet = new[] { ChurchTextures.GlassMat(ChurchTextures.Lancet(4), 1.3f), ChurchTextures.GlassMat(ChurchTextures.Lancet(5), 1.3f), ChurchTextures.GlassMat(ChurchTextures.Lancet(6), 1.3f) };
		// ---- the aisles' outer walls: a wide window of clear leaded glass in each bay
		foreach (float s in new[] { -1f, 1f })
		{
			var holes = new List<Hole>();
			for (int b = 0; b < NaveBays; b++)
			{
				float c = (b + 0.5f) * BayLen;
				// one pane long broken: the snow blows in through it (a drift on the floor under it)
				holes.Add(Hole.Lancet(c - 1.4f, c + 1.4f, 2.6f, 7.2f, s > 0 && b == BrokenBay ? null : quarry, 0.8f));
			}
			Wall(k, ash, new Vector3(s * AisleOuter, 0, 0), Vector3.Back, new Vector3(-s, 0, 0), WallT, 0f, NaveEnd - TransWestT, 0f, _ => 12f, holes);
			Collide(_stone, new Vector3(s * (AisleOuter + WallT * 0.5f), 6f, (NaveEnd - TransWestT) * 0.5f), new Vector3(WallT, 12f, NaveEnd - TransWestT));
			// wall shafts opposite the piers, carrying the aisle vault
			k.Mat(ash);
			for (int b = 1; b < NaveBays; b++)
				k.Cylinder(new Vector3(s * (AisleOuter - 0.05f), 0, b * BayLen), new Vector3(s * (AisleOuter - 0.05f), AisleSpring, b * BayLen), 0.22f, 0.22f, 8, false);
		}
		// ---- the west front: the great door, the rose over it, a window in the left aisle, the tower's
		// door in the right aisle with a small window over it
		{
			var holes = new List<Hole>
			{
				Hole.Lancet(-2.6f, 2.6f, 0f, 5f, null, 0.6f),
				Hole.Round(0f, 21.5f, 4.4f, ChurchTextures.GlassMat(ChurchTextures.Rose, 1.4f)),
				Hole.Lancet(-12.9f, -10.1f, 3f, 7.6f, quarry, 0.8f),
				Hole.Lancet(10.9f, 12.1f, 0f, 2.4f, null, 0.7f),
				Hole.Lancet(10.9f, 12.1f, 5.2f, 8.6f, lancet[0], 1f),
			};
			holes.Sort((a, b) => a.U0.CompareTo(b.U0));
			Wall(k, ash, new Vector3(0, 0, 0), Vector3.Right, Vector3.Back, 1.5f, -AisleOuter, AisleOuter, 0f,
				u => Mathf.Abs(u) <= NaveHalf ? NaveVaultY(u) + 0.4f : AisleVaultY(u) + 0.4f, holes);
			// collision either side of the great door and the tower door
			Collide(_stone, new Vector3((-AisleOuter - 2.6f) * 0.5f, 5f, -0.75f), new Vector3(AisleOuter - 2.6f, 10f, 1.5f));
			Collide(_stone, new Vector3((2.6f + 10.9f) * 0.5f, 5f, -0.75f), new Vector3(10.9f - 2.6f, 10f, 1.5f));
			Collide(_stone, new Vector3((12.1f + AisleOuter) * 0.5f, 5f, -0.75f), new Vector3(AisleOuter - 12.1f, 10f, 1.5f));
		}
		// ---- the transepts: their end walls (a door, three tall lancets high over it), their west walls
		// (closing the aisles), their east walls
		foreach (float s in new[] { -1f, 1f })
		{
			float mid = (NaveEnd + CrossEnd) * 0.5f;
			var end = new List<Hole>
			{
				Hole.Lancet(mid - 1.2f, mid + 1.2f, 0f, 2.7f, null, 0.7f),
				Hole.Lancet(mid - 3.6f, mid - 1.6f, 9.5f, 19f, lancet[1], 1f),
				Hole.Lancet(mid - 1f, mid + 1f, 9.5f, 20.5f, lancet[0], 1f),
				Hole.Lancet(mid + 1.6f, mid + 3.6f, 9.5f, 19f, lancet[2], 1f),
			};
			end.Sort((a, b) => a.U0.CompareTo(b.U0));
			Wall(k, ash, new Vector3(s * TransHalf, 0, 0), Vector3.Back, new Vector3(-s, 0, 0), WallT, NaveEnd, CrossEnd, 0f, u => TransVaultY(u) + 0.4f, end);
			Collide(_stone, new Vector3(s * (TransHalf + WallT * 0.5f), 6f, (NaveEnd + mid - 1.2f) * 0.5f), new Vector3(WallT, 12f, mid - 1.2f - NaveEnd));
			Collide(_stone, new Vector3(s * (TransHalf + WallT * 0.5f), 6f, (mid + 1.2f + CrossEnd) * 0.5f), new Vector3(WallT, 12f, CrossEnd - mid - 1.2f));
			// west wall of the arm: from the nave's arcade wall out to the end wall
			float xa = NaveHalf + 0.45f, xb = TransHalf;
			var west = new List<Hole> { Hole.Lancet(18f, 21f, 10f, 17f, lancet[2], 1f) };
			Wall(k, ash, new Vector3(0, 0, NaveEnd), s > 0 ? Vector3.Right : Vector3.Left, Vector3.Back, TransWestT, xa, xb, 0f, _ => Spring + 0.6f, west);
			Collide(_stone, new Vector3(s * (xa + xb) * 0.5f, 6f, NaveEnd - TransWestT * 0.5f), new Vector3(xb - xa, 12f, TransWestT));
			// east wall of the arm: from the chancel's wall out to the end wall
			var east = new List<Hole> { Hole.Lancet(12f, 14.2f, 9f, 18.5f, lancet[1], 1f), Hole.Lancet(18f, 20.2f, 9f, 18.5f, lancet[0], 1f) };
			Wall(k, ash, new Vector3(0, 0, CrossEnd), s > 0 ? Vector3.Right : Vector3.Left, Vector3.Forward, WallT, xa, xb, 0f, _ => Spring + 0.6f, east);
			Collide(_stone, new Vector3(s * (xa + xb) * 0.5f, 6f, CrossEnd + WallT * 0.5f), new Vector3(xb - xa, 12f, WallT));
		}
		// ---- the chancel: tall windows either side, then the apse's seven facets, each with a tall lancet
		foreach (float s in new[] { -1f, 1f })
		{
			var holes = new List<Hole> { Hole.Lancet(CrossEnd + 2.2f, CrossEnd + 4.8f, 6.5f, 19f, lancet[0], 1f), Hole.Lancet(CrossEnd + 7.2f, CrossEnd + 9.8f, 6.5f, 19f, lancet[2], 1f) };
			Wall(k, ash, new Vector3(s * (NaveHalf - 0.45f), 0, 0), Vector3.Back, new Vector3(-s, 0, 0), 0.9f, CrossEnd + 0.95f, ChancelEnd, 0f, _ => 28f, holes);
			Collide(_stone, new Vector3(s * NaveHalf, 6f, (CrossEnd + ChancelEnd) * 0.5f), new Vector3(0.9f, 12f, ChancelEnd - CrossEnd));
		}
		const int facets = 7;
		for (int i = 0; i < facets; i++)
		{
			float a0 = -Mathf.Pi * 0.5f + Mathf.Pi * i / facets, a1 = -Mathf.Pi * 0.5f + Mathf.Pi * (i + 1) / facets;
			const float ri = ApseR - 0.45f;   // the facets' inner faces, in line with the chancel's walls
			Vector3 p0 = new(Mathf.Sin(a0) * ri, 0, ChancelEnd + Mathf.Cos(a0) * ri), p1 = new(Mathf.Sin(a1) * ri, 0, ChancelEnd + Mathf.Cos(a1) * ri);
			Vector3 along = (p1 - p0).Normalized();
			float len = (p1 - p0).Length();
			Vector3 inward = -new Vector3(Mathf.Sin((a0 + a1) * 0.5f), 0, Mathf.Cos((a0 + a1) * 0.5f));
			// the middle five open into small chapels (the owner's plan); the end two keep tall windows
			bool chapel = FacetHasChapel(i);
			var holes = chapel ? new List<Hole> { Hole.Lancet(len * 0.5f - 1.2f, len * 0.5f + 1.2f, 0f, 3.8f, null, 0.8f), Hole.Lancet(len * 0.5f - 0.6f, len * 0.5f + 0.6f, 11f, 20f, lancet[i % 3], 1f) }
				: new List<Hole> { Hole.Lancet(len * 0.5f - 0.85f, len * 0.5f + 0.85f, 5.5f, 20f, lancet[i % 3], 1f) };
			Wall(k, ash, p0, along, inward, 0.9f, 0f, len, 0f, _ => 28f, holes);
			var fb = Basis.LookingAt(inward, Vector3.Up);
			Vector3 At(float u) => p0 + along * u - inward * 0.45f;
			if (!chapel) Collide(_stone, At(len * 0.5f) + Vector3.Up * 6f, new Vector3(len, 12f, 0.9f), fb);
			else
			{
				float side = len * 0.5f - 1.2f;
				Collide(_stone, At(side * 0.5f) + Vector3.Up * 6f, new Vector3(side, 12f, 0.9f), fb);
				Collide(_stone, At(len - side * 0.5f) + Vector3.Up * 6f, new Vector3(side, 12f, 0.9f), fb);
				Collide(_stone, At(len * 0.5f) + Vector3.Up * 9f, new Vector3(2.4f, 6f, 0.9f), fb);
			}
		}
		k.CommitTo(this, "Walls", true).CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
		BuildBaseCourses();
		BuildDrift();
	}

	/// <summary>
	/// A moulded base course along the foot of a wall (the owner: the walls went straight into the floor;
	/// their castle-arch reference): a plain plinth standing out from the wall, a sloped weathering on
	/// top of it, and a round roll moulding under the slope. From <paramref name="a"/> to <paramref name="b"/>
	/// along the wall's face, standing out toward <paramref name="inward"/>, stopping at each gap (doorways).
	/// </summary>
	public static void BaseCourse(MeshKit k, Vector3 a, Vector3 b, Vector3 inward, float y0, IList<(float u0, float u1)> gaps = null, float h = 0.55f, float d = 0.16f)
	{
		Vector3 along = (b - a).Normalized();
		float len = (b - a).Length();
		inward = inward.Normalized();
		var spans = new List<(float, float)> { (0f, len) };
		if (gaps != null)
			foreach (var (g0, g1) in gaps)
			{
				var next = new List<(float, float)>();
				foreach (var (s0, s1) in spans)
				{
					if (g1 <= s0 || g0 >= s1) { next.Add((s0, s1)); continue; }
					if (g0 > s0) next.Add((s0, g0));
					if (g1 < s1) next.Add((g1, s1));
				}
				spans = next;
			}
		var basis = new Basis(along, Vector3.Up, inward);
		k.Mat(ChurchTextures.AshlarDampMat);
		k.Color = Colors.White * 0.95f;
		foreach (var (s0, s1) in spans)
		{
			if (s1 - s0 < 0.05f) continue;
			float l = s1 - s0;
			Vector3 m = a + along * ((s0 + s1) * 0.5f);
			m.Y = y0;
			// the plinth
			BuildKit.Box(k, m + Vector3.Up * (h * 0.5f) + inward * (d * 0.5f), new Vector3(l, h, d), 1f, BuildKit.Face.NY, basis);
			// the weathering: sloping back from the plinth's edge to the wall
			Vector3 e0 = m - along * (l * 0.5f), e1 = m + along * (l * 0.5f);
			Vector3 lo = Vector3.Up * h + inward * d, hi = Vector3.Up * (h + 0.14f);
			Vector3 n = (inward * 0.14f + Vector3.Up * d).Normalized();
			k.Quad(e0 + lo, e1 + lo, e1 + hi, e0 + hi, n);
			// the ends of the slope, where it stops at a door
			k.Tri(e0 + lo, e0 + hi, e0 + Vector3.Up * h, -along, Vector2.Zero, Vector2.Zero, Vector2.Zero);
			k.Tri(e1 + lo, e1 + Vector3.Up * h, e1 + hi, along, Vector2.Zero, Vector2.Zero, Vector2.Zero);
			// the roll under the slope, and a thin fillet at the plinth's foot
			k.Cylinder(e0 + Vector3.Up * (h - 0.03f) + inward * (d + 0.01f), e1 + Vector3.Up * (h - 0.03f) + inward * (d + 0.01f), 0.045f, 0.045f, 8, true);
			BuildKit.Box(k, m + Vector3.Up * 0.05f + inward * (d + 0.025f), new Vector3(l, 0.1f, 0.05f), 1f, BuildKit.Face.NY, basis);
		}
	}

	private void BuildBaseCourses()
	{
		var k = new MeshKit();
		float mid = (NaveEnd + CrossEnd) * 0.5f;
		foreach (float s in new[] { -1f, 1f })
		{
			// the aisles' outer walls
			BaseCourse(k, new Vector3(s * AisleOuter, 0, 0), new Vector3(s * AisleOuter, 0, NaveEnd - TransWestT), new Vector3(-s, 0, 0), 0f);
			// the transepts: the end walls (round the doors), the west and east walls
			BaseCourse(k, new Vector3(s * TransHalf, 0, NaveEnd), new Vector3(s * TransHalf, 0, CrossEnd), new Vector3(-s, 0, 0), 0f, new[] { (mid - 1.2f - NaveEnd, mid + 1.2f - NaveEnd) });
			BaseCourse(k, new Vector3(s * (NaveHalf + 0.45f), 0, NaveEnd), new Vector3(s * TransHalf, 0, NaveEnd), Vector3.Back, 0f);
			BaseCourse(k, new Vector3(s * (NaveHalf + 0.45f), 0, CrossEnd), new Vector3(s * TransHalf, 0, CrossEnd), Vector3.Forward, 0f);
			// the chancel's walls, on its raised floor
			BaseCourse(k, new Vector3(s * (NaveHalf - 0.45f), 0, CrossEnd + 2f), new Vector3(s * (NaveHalf - 0.45f), 0, ChancelEnd), new Vector3(-s, 0, 0), ChancelY);
		}
		// the west wall, round the great door and the tower's door
		BaseCourse(k, new Vector3(-AisleOuter, 0, 0), new Vector3(AisleOuter, 0, 0), Vector3.Back, 0f, new[] { (AisleOuter - 2.6f, AisleOuter + 2.6f), (AisleOuter + 10.9f, AisleOuter + 12.1f) });
		// the apse's facets (not across the chapels' openings)
		for (int i = 0; i < 7; i++)
		{
			ApseFacet(i, out var p0, out var along, out var inward, out float len);
			var gaps = FacetHasChapel(i) ? new[] { (len * 0.5f - 1.2f, len * 0.5f + 1.2f) } : null;
			BaseCourse(k, p0, p0 + along * len, inward, ChancelY, gaps);
		}
		// the vestry
		BaseCourse(k, new Vector3(VestryX1, 0, VestryZ0), new Vector3(VestryX1, 0, VestryZ1), Vector3.Right, 0f, null, 0.4f, 0.12f);
		BaseCourse(k, new Vector3(VestryX1, 0, VestryZ0), new Vector3(VestryX0, 0, VestryZ0), Vector3.Back, 0f, null, 0.4f, 0.12f);
		BaseCourse(k, new Vector3(VestryX1, 0, VestryZ1), new Vector3(VestryX0, 0, VestryZ1), Vector3.Forward, 0f, null, 0.4f, 0.12f);
		// the crypt, round the stairs' doorways
		var cg = new List<(float, float)>();
		foreach (var (x0, x1, _, _) in StairOpenings) cg.Add((x0 + CryptHalf, x1 + CryptHalf));
		BaseCourse(k, new Vector3(-CryptHalf, 0, CryptZ0), new Vector3(CryptHalf, 0, CryptZ0), Vector3.Back, CryptFloor, cg, 0.45f, 0.14f);
		BaseCourse(k, new Vector3(-CryptHalf, 0, CryptZ1), new Vector3(CryptHalf, 0, CryptZ1), Vector3.Forward, CryptFloor, null, 0.45f, 0.14f);
		foreach (float s in new[] { -1f, 1f })
			BaseCourse(k, new Vector3(s * CryptHalf, 0, CryptZ0), new Vector3(s * CryptHalf, 0, CryptZ1), new Vector3(-s, 0, 0), CryptFloor, null, 0.45f, 0.14f);
		k.CommitTo(this, "BaseCourses", true);
	}

	/// <summary>The snow blown in through the broken window: a drift against the wall, soft underfoot, and a
	/// thin spill of flakes still coming in.</summary>
	private void BuildDrift()
	{
		float z = (BrokenBay + 0.5f) * BayLen;
		var k = new MeshKit();
		k.Mat(ChurchTextures.SnowMat);
		k.Color = Colors.White;
		k.Blob(new Vector3(AisleOuter - 0.9f, 0f, z), new Vector3(1.4f, 0.55f, 2.2f), 41, 0.2f, true, 1f);
		k.Blob(new Vector3(AisleOuter - 2.4f, 0f, z + 0.6f), new Vector3(1.4f, 0.22f, 1.6f), 42, 0.25f, true, 1f);
		k.Blob(new Vector3(AisleOuter - 0.3f, 2.55f, z), new Vector3(0.3f, 0.08f, 1.3f), 43, 0.2f, true, 1f);   // on the sill
		k.CommitTo(this, "Drift", false);
		var body = new StaticBody3D { Name = "DriftBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		body.AddChild(new CollisionShape3D { Position = new Vector3(AisleOuter - 1.6f, 0.08f, z), Shape = new BoxShape3D { Size = new Vector3(2.8f, 0.16f, 3.6f) } });
		AddChild(body);
		var flake = new QuadMesh { Size = new Vector2(0.04f, 0.04f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.97f, 1f, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } };
		AddChild(new GpuParticles3D
		{
			Name = "BlownSnow", Amount = 120, Lifetime = 5f, Preprocess = 5f, DrawPass1 = flake, Position = new Vector3(AisleOuter - 0.2f, 5.5f, z),
			ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.2f, 2f, 1.2f), Direction = new Vector3(-1f, -0.4f, 0), Spread = 20f, InitialVelocityMin = 0.5f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -0.6f, 0) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	// ------------------------------------------------------------------ the vaults

	private void BuildVaults()
	{
		var web = new MeshKit();
		web.Mat(ChurchTextures.PlasterMat);
		web.Color = new Color(0.94f, 0.92f, 0.88f);
		// the nave's pointed barrel, crossing and chancel included (the transept's cuts through it: the lower
		// of the two is what shows from below, so where they cross they make a groin)
		Barrel(web, -NaveHalf - 0.45f, NaveHalf + 0.45f, 0f, ChancelEnd, x => NaveVaultY(x), alongZ: true);
		Barrel(web, NaveEnd, CrossEnd, -TransHalf - 0.6f, TransHalf + 0.6f, z => TransVaultY(z), alongZ: false);
		foreach (float s in new[] { -1f, 1f })
		{
			float x0 = s < 0 ? -AisleOuter - 0.1f : NaveHalf + 0.45f, x1 = s < 0 ? -NaveHalf - 0.45f : AisleOuter + 0.1f;
			Barrel(web, x0, x1, 0f, NaveEnd - TransWestT, AisleVaultY, alongZ: true);
		}
		// the apse's half-dome: the nave's profile swung round the apse's centre
		const int rs = 16, ts = 14;
		for (int i = 0; i < ts; i++)
			for (int j = 0; j < rs; j++)
			{
				float t0 = -Mathf.Pi * 0.5f + Mathf.Pi * i / ts, t1 = -Mathf.Pi * 0.5f + Mathf.Pi * (i + 1) / ts;
				float r0 = (ApseR + 0.5f) * j / rs, r1 = (ApseR + 0.5f) * (j + 1) / rs;
				Vector3 P(float r, float t) => new(Mathf.Sin(t) * r, NaveVaultY(Mathf.Min(r, NaveHalf)), ChancelEnd + Mathf.Cos(t) * r);
				web.Quad(P(r0, t0), P(r1, t0), P(r1, t1), P(r0, t1), Vector3.Down);
			}
		// (the sun must not come through them: they're one-sided, so they cast their shadow both ways)
		web.CommitTo(this, "Vaults", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
		// the ribs: transverse arches, diagonals, tiercerons and the ridge, gilded bosses where they meet
		var rib = new MeshKit();
		rib.Mat(ChurchTextures.AshlarMat);
		rib.Color = new Color(0.72f, 0.68f, 0.62f);
		var boss = new MeshKit();
		boss.Mat(ChurchTextures.GoldMat);
		boss.Color = Colors.White;
		Func<float, float, float> nave = (x, z) => NaveVaultY(x);
		for (int b = 0; b <= NaveBays; b++) Rib(rib, nave, new Vector2(-NaveHalf, b * BayLen), new Vector2(NaveHalf, b * BayLen));
		var bays = new List<(float, float)>();
		for (int b = 0; b < NaveBays; b++) bays.Add((b * BayLen, (b + 1) * BayLen));
		bays.Add((CrossEnd, CrossEnd + 6f));
		bays.Add((CrossEnd + 6f, ChancelEnd));
		foreach (var (z0, z1) in bays)
		{
			Rib(rib, nave, new Vector2(-NaveHalf, z0), new Vector2(NaveHalf, z1));
			Rib(rib, nave, new Vector2(NaveHalf, z0), new Vector2(-NaveHalf, z1));
			float q = (z1 - z0) * 0.25f;
			foreach (float s in new[] { -1f, 1f })
			{
				Rib(rib, nave, new Vector2(s * NaveHalf, z0), new Vector2(0, z0 + q));
				Rib(rib, nave, new Vector2(s * NaveHalf, z1), new Vector2(0, z1 - q));
			}
			Boss(boss, new Vector3(0, NaveVaultY(0) - 0.3f, (z0 + z1) * 0.5f), 0.45f);
			Boss(boss, new Vector3(0, NaveVaultY(0) - 0.25f, z0 + q), 0.28f);
			Boss(boss, new Vector3(0, NaveVaultY(0) - 0.25f, z1 - q), 0.28f);
		}
		foreach (float z in new[] { CrossEnd, CrossEnd + 6f, ChancelEnd }) Rib(rib, nave, new Vector2(-NaveHalf, z), new Vector2(NaveHalf, z));
		Rib(rib, nave, new Vector2(0, 0), new Vector2(0, ChancelEnd));   // the ridge
		// the crossing: the groin's diagonals, and a great boss where they meet
		Func<float, float, float> low = (x, z) => Mathf.Min(NaveVaultY(x), TransVaultY(z));
		Rib(rib, low, new Vector2(-NaveHalf, NaveEnd), new Vector2(NaveHalf, CrossEnd));
		Rib(rib, low, new Vector2(NaveHalf, NaveEnd), new Vector2(-NaveHalf, CrossEnd));
		Boss(boss, new Vector3(0, NaveVaultY(0) - 0.4f, (NaveEnd + CrossEnd) * 0.5f), 0.8f);
		// the transepts' arms: transverse ribs and diagonals in each of their two bays, and their ridge
		Func<float, float, float> trans = (x, z) => TransVaultY(z);
		foreach (float s in new[] { -1f, 1f })
		{
			Rib(rib, trans, new Vector2(s * TransHalf, (NaveEnd + CrossEnd) * 0.5f), new Vector2(s * NaveHalf, (NaveEnd + CrossEnd) * 0.5f));
			foreach (var (xa, xb) in new[] { (NaveHalf, 16f), (16f, TransHalf) })
			{
				Rib(rib, trans, new Vector2(s * xb, NaveEnd), new Vector2(s * xb, CrossEnd));
				Rib(rib, trans, new Vector2(s * xa, NaveEnd), new Vector2(s * xb, CrossEnd));
				Rib(rib, trans, new Vector2(s * xa, CrossEnd), new Vector2(s * xb, NaveEnd));
				Boss(boss, new Vector3(s * (xa + xb) * 0.5f, TransVaultY((NaveEnd + CrossEnd) * 0.5f) - 0.3f, (NaveEnd + CrossEnd) * 0.5f), 0.4f);
			}
		}
		// the aisles: transverse and diagonal ribs
		foreach (float s in new[] { -1f, 1f })
		{
			Func<float, float, float> aisle = (x, z) => AisleVaultY(x);
			float xa = s * NaveHalf, xb = s * AisleOuter;
			for (int b = 0; b < NaveBays; b++)
			{
				float z0 = b * BayLen, z1 = (b + 1) * BayLen;
				Rib(rib, aisle, new Vector2(xa, z0), new Vector2(xb, z0), 0.2f);
				Rib(rib, aisle, new Vector2(xa, z0), new Vector2(xb, z1), 0.2f);
				Rib(rib, aisle, new Vector2(xb, z0), new Vector2(xa, z1), 0.2f);
				Boss(boss, new Vector3((xa + xb) * 0.5f, AisleVaultY((xa + xb) * 0.5f) - 0.2f, (z0 + z1) * 0.5f), 0.22f);
			}
		}
		// the apse: ribs from each facet's corner up to the keystone
		for (int i = 0; i <= 7; i++)
		{
			float a = -Mathf.Pi * 0.5f + Mathf.Pi * i / 7f;
			var pts = new List<Vector3>();
			for (int j = 0; j <= 12; j++)
			{
				float r = ApseR * (1f - j / 12f);
				pts.Add(new Vector3(Mathf.Sin(a) * r, NaveVaultY(r) - 0.12f, ChancelEnd + Mathf.Cos(a) * r));
			}
			for (int j = 0; j < pts.Count - 1; j++) rib.Beam(pts[j], pts[j + 1], 0.26f, 0.3f);
		}
		Boss(boss, new Vector3(0, NaveVaultY(0) - 0.3f, ChancelEnd), 0.5f);
		rib.CommitTo(this, "Ribs", false);
		boss.CommitTo(this, "Bosses", false);
	}

	/// <summary>A pointed barrel's underside: along Z (profile across x from a0 to a1, z from b0 to b1) or
	/// along X (profile across z from a0 to a1, x from b0 to b1).</summary>
	private static void Barrel(MeshKit k, float a0, float a1, float b0, float b1, Func<float, float> profile, bool alongZ)
	{
		const float da = 0.4f, db = 2.5f;
		for (float a = a0; a < a1 - 1e-4f; a += da)
		{
			float an = Mathf.Min(a + da, a1);
			float ya = profile(a), yb = profile(an);
			Vector3 n = new Vector3((yb - ya) / (an - a), -1f, 0).Normalized();   // down, and in towards the middle
			for (float b = b0; b < b1 - 1e-4f; b += db)
			{
				float bn = Mathf.Min(b + db, b1);
				if (alongZ)
					k.Quad(new Vector3(a, ya, b), new Vector3(an, yb, b), new Vector3(an, yb, bn), new Vector3(a, ya, bn), n,
						new Vector2(a, b), new Vector2(an, b), new Vector2(an, bn), new Vector2(a, bn));
				else
					k.Quad(new Vector3(b, ya, a), new Vector3(b, yb, an), new Vector3(bn, yb, an), new Vector3(bn, ya, a), new Vector3(0, n.Y, n.X),
						new Vector2(b, a), new Vector2(b, an), new Vector2(bn, an), new Vector2(bn, a));
			}
		}
	}

	/// <summary>A rib along a vault's surface from plan point a to b (x,z), just under the surface.</summary>
	private static void Rib(MeshKit k, Func<float, float, float> surface, Vector2 a, Vector2 b, float scale = 0.28f)
	{
		float len = a.DistanceTo(b);
		int n = Mathf.Max(4, Mathf.CeilToInt(len / 0.7f));
		Vector3 prev = default;
		for (int i = 0; i <= n; i++)
		{
			var p = a.Lerp(b, i / (float)n);
			Vector3 q = new(p.X, surface(p.X, p.Y) - 0.13f, p.Y);
			if (i > 0) k.Beam(prev, q, scale, scale * 1.15f);
			prev = q;
		}
	}

	private static void Boss(MeshKit k, Vector3 at, float r)
	{
		k.Blob(at, new Vector3(r, r * 0.6f, r), Mathf.RoundToInt(at.Z * 10f), 0.15f, false, 1f);
	}
}
