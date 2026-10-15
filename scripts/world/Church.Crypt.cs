using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>
/// The crypt under the church (the owner's crypt references): a low hall of brick groin vaults on
/// squat stone columns with cushion capitals, over a floor of black and white marble squares; stone
/// tombs in the side bays; at the far end an old altar and a small pipe organ, and a hooded stone figure
/// standing in the corner. Twin stairs climb from its west end, through openings in the nave's floor. The
/// long stair's hatch is let into its floor.
/// </summary>
public partial class Church
{
	public const float CryptSpring = -3.2f, CryptRise = 2.4f, CryptBay = 4f;

	/// <summary>The crypt's vault at (x, z): a groin vault in each 4 m bay, the higher of its two barrels.</summary>
	public static float CryptVaultY(float x, float z)
	{
		float dx = Mathf.PosMod(x + CryptHalf, CryptBay) - CryptBay * 0.5f;
		float dz = Mathf.PosMod(z - CryptZ0, CryptBay) - CryptBay * 0.5f;
		float E(float d) => Mathf.Sqrt(Mathf.Max(0f, 1f - (d / (CryptBay * 0.5f)) * (d / (CryptBay * 0.5f))));
		return CryptSpring + CryptRise * Mathf.Max(E(dx), E(dz));
	}

	/// <summary>The crypt stairs, one under each opening in the nave floor: 30 steps down eastward.</summary>
	public const int CryptSteps = 30;

	private void BuildCrypt()
	{
		var k = new MeshKit();
		float y0 = CryptFloor;
		// ---- the floor, round the hatch
		k.Mat(ChurchTextures.ChequerMat);
		k.Color = Colors.White;
		var h = HatchLocal;
		void F(float x0, float x1, float z0, float z1)
		{
			if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) return;
			k.Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.Up,
				new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
			Collide(_marbleBody, new Vector3((x0 + x1) * 0.5f, y0 - 0.15f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.3f, z1 - z0));
		}
		F(-CryptHalf, h.X - 0.5f, CryptZ0, CryptZ1);
		F(h.X + 0.5f, CryptHalf, CryptZ0, CryptZ1);
		F(h.X - 0.5f, h.X + 0.5f, CryptZ0, h.Z - 0.5f);
		F(h.X - 0.5f, h.X + 0.5f, h.Z + 0.5f, CryptZ1);
		// ---- the walls (brick, like the vaults), the west one with the stairs' two doorways
		var brick = ChurchTextures.CryptBrickMat;
		var west = new List<Hole>();
		foreach (var (x0, x1, _, _) in StairOpenings) west.Add(new Hole { U0 = x0, U1 = x1, Bottom = y0, Top = _ => -3.25f });
		Wall(k, brick, new Vector3(0, 0, CryptZ0), Vector3.Right, Vector3.Back, 0.8f, -CryptHalf, CryptHalf, y0, _ => CryptTop, west);
		Wall(k, brick, new Vector3(0, 0, CryptZ1), Vector3.Right, Vector3.Forward, 0.8f, -CryptHalf, CryptHalf, y0, _ => CryptTop, null);
		foreach (float s in new[] { -1f, 1f })
		{
			Wall(k, brick, new Vector3(s * CryptHalf, 0, 0), Vector3.Back, new Vector3(-s, 0, 0), 0.8f, CryptZ0 - 0.8f, CryptZ1 + 0.8f, y0, _ => CryptTop, null);
			Collide(_stone, new Vector3(s * (CryptHalf + 0.4f), (y0 + CryptTop) * 0.5f, (CryptZ0 + CryptZ1) * 0.5f), new Vector3(0.8f, CryptTop - y0, CryptZ1 - CryptZ0 + 1.6f));
		}
		Collide(_stone, new Vector3(0, (y0 + CryptTop) * 0.5f, CryptZ1 + 0.4f), new Vector3(CryptHalf * 2f, CryptTop - y0, 0.8f));
		foreach (var (xa, xb) in new[] { (-CryptHalf, StairOpenings[0].x0), (StairOpenings[0].x1, StairOpenings[1].x0), (StairOpenings[1].x1, CryptHalf) })
			Collide(_stone, new Vector3((xa + xb) * 0.5f, (y0 + CryptTop) * 0.5f, CryptZ0 - 0.4f), new Vector3(xb - xa, CryptTop - y0, 0.8f));
		// ---- the vaults: a grid over each bay, following the groin surface
		k.Mat(brick);
		k.Color = new Color(0.9f, 0.86f, 0.82f);
		const int res = 10;
		for (float bx = -CryptHalf; bx < CryptHalf - 0.01f; bx += CryptBay)
			for (float bz = CryptZ0; bz < CryptZ1 - 0.01f; bz += CryptBay)
				for (int i = 0; i < res; i++)
					for (int j = 0; j < res; j++)
					{
						float xa = bx + CryptBay * i / res, xb = bx + CryptBay * (i + 1) / res;
						float za = bz + CryptBay * j / res, zb = bz + CryptBay * (j + 1) / res;
						float xm = (xa + xb) * 0.5f, zm = (za + zb) * 0.5f;
						Vector3 n = new Vector3((CryptVaultY(xm + 0.01f, zm) - CryptVaultY(xm - 0.01f, zm)) / 0.02f, -1f, (CryptVaultY(xm, zm + 0.01f) - CryptVaultY(xm, zm - 0.01f)) / 0.02f).Normalized();
						k.Quad(new Vector3(xa, CryptVaultY(xa + 1e-4f, za + 1e-4f), za), new Vector3(xb, CryptVaultY(xb - 1e-4f, za + 1e-4f), za),
							new Vector3(xb, CryptVaultY(xb - 1e-4f, zb - 1e-4f), zb), new Vector3(xa, CryptVaultY(xa + 1e-4f, zb - 1e-4f), zb), n,
							new Vector2(xa, za), new Vector2(xb, za), new Vector2(xb, zb), new Vector2(xa, zb));
					}
		// ---- the columns: a square base, a round shaft, a cushion capital
		var stone = ChurchTextures.AshlarDampMat;
		k.Mat(stone);
		k.Color = Colors.White;
		for (float x = -CryptHalf + CryptBay; x < CryptHalf - 0.01f; x += CryptBay)
			for (float z = CryptZ0 + CryptBay; z < CryptZ1 - 0.01f; z += CryptBay)
			{
				// (2026-10-11, the owner: the square base and capital read as an "I", the vault's groins coming down onto it
				// as spikes; "turn them into cylinders like the pillars so they look more structural") round now from foot to
				// vault: a moulded round base, the shaft, a capital flaring out round, and above it a round impost widening
				// up into the vault, so the groins land on stone rather than meeting at a point
				Vector3 b = new(x, y0, z);
				k.Cylinder(b, b + Vector3.Up * 0.16f, 0.4f, 0.4f, 18, true);
				k.Cylinder(b + Vector3.Up * 0.16f, b + Vector3.Up * 0.3f, 0.36f, 0.3f, 18, true);
				k.Cylinder(b + Vector3.Up * 0.3f, new Vector3(x, CryptSpring - 0.5f, z), 0.28f, 0.26f, 18, false);
				k.Cylinder(new Vector3(x, CryptSpring - 0.56f, z), new Vector3(x, CryptSpring - 0.5f, z), 0.3f, 0.3f, 18, false);   // (the astragal)
				k.Cylinder(new Vector3(x, CryptSpring - 0.5f, z), new Vector3(x, CryptSpring - 0.1f, z), 0.27f, 0.5f, 18, false);   // (the capital's flare)
				k.Cylinder(new Vector3(x, CryptSpring - 0.1f, z), new Vector3(x, CryptSpring + 0.02f, z), 0.56f, 0.56f, 18, true);  // (the abacus)
				k.Cylinder(new Vector3(x, CryptSpring + 0.02f, z), new Vector3(x, CryptSpring + 0.45f, z), 0.5f, 0.66f, 18, false); // (the impost into the vault)
				_stone.AddChild(new CollisionShape3D { Position = new Vector3(x, (y0 + CryptSpring) * 0.5f, z), Shape = new CylinderShape3D { Radius = 0.36f, Height = CryptSpring - y0 } });
			}
		// ---- webs: in the crypt's corners, and slung from the column tops up into the vaults
		var wr = new RandomNumberGenerator { Seed = 2121 };
		WebKit.DressRoom(this, wr, -CryptHalf, CryptHalf, CryptZ0, CryptZ1, y0, CryptSpring + 0.5f, 1.5f, 0.9f);
		for (float x = -CryptHalf + CryptBay; x < CryptHalf - 0.01f; x += CryptBay)
			for (float z = CryptZ0 + CryptBay; z < CryptZ1 - 0.01f; z += CryptBay)
			{
				if (wr.Randf() > 0.45f) continue;
				float ang = wr.RandiRange(0, 3) * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f;
				Vector3 dir = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
				Vector3 at = new Vector3(x, CryptSpring + 0.35f, z) + dir * 0.55f;
				WebKit.Card(this, wr.Randf() < 0.5f ? WebKit.Kind.Sheet : WebKit.Kind.Tangle, wr, at, new Vector3(-dir.Z, 0, dir.X), new Vector2(1.3f, 1.0f), wr.RandfRange(-0.3f, 0.3f), "VaultWeb");
			}
		// ---- the stairs up: 30 steps each, and their walls, under the nave's floor
		foreach (var (x0, x1, z0, z1) in StairOpenings)
		{
			float run = (z1 - z0) / CryptSteps, rise = -y0 / CryptSteps;
			k.Mat(stone);
			for (int i = 0; i < CryptSteps; i++)
			{
				float top = -(i + 1) * rise, za = z0 + i * run;
				BuildKit.Box(k, new Vector3((x0 + x1) * 0.5f, top - 0.1f, za + run * 0.5f), new Vector3(x1 - x0, 0.2f, run), 1f, BuildKit.Face.NY);
				float zr = i == 0 ? za + 0.004f : za;   // (the top riser a hair in: level with the nave floor's cut edge, the two fought)
				k.Quad(new Vector3(x0, top, zr), new Vector3(x1, top, zr), new Vector3(x1, top + rise, zr), new Vector3(x0, top + rise, zr), Vector3.Back);
			}
			float pitch = Mathf.Atan2(-y0, z1 - z0), len = Mathf.Sqrt((z1 - z0) * (z1 - z0) + y0 * y0);
			var rb = new Basis(Vector3.Right, pitch);
			// a ramp under the treads, its top on the line of the nosings, meeting the nave's floor at the opening's edge (2026-10-11,
			// playtest: "i cant walk up either staircase in the church": half a riser lower, it ended in a lip under the floor's
			// edge the body couldn't step over)
			Collide(_stone, new Vector3((x0 + x1) * 0.5f, y0 * 0.5f, (z0 + z1) * 0.5f) - rb.Y * 0.2f, new Vector3(x1 - x0, 0.4f, len + 0.2f), rb);

			// the stairwell's side walls, from under the steps up to the nave floor's underside
			foreach (var (x, n) in new[] { (x0, Vector3.Right), (x1, Vector3.Left) })
			{
				k.Mat(stone);
				for (float z = z0; z < z1 - 0.001f; z += 0.5f)
				{
					float zb = Mathf.Min(z + 0.5f, z1);
					float ba = -(z - z0) / run * rise - 0.5f, bb = -(zb - z0) / run * rise - 0.5f;
					k.Quad(new Vector3(x, ba, z), new Vector3(x, bb, zb), new Vector3(x, -0.6f, zb), new Vector3(x, -0.6f, z), n, new Vector2(z, -ba), new Vector2(zb, -bb), new Vector2(zb, 0.6f), new Vector2(z, 0.6f));
				}
				Collide(_stone, new Vector3(x + (n.X > 0 ? -0.2f : 0.2f), (y0 - 0.6f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.4f, -y0, z1 - z0));
			}
		}
		// ---- the tombs: stone chests with a cross cut in the lid, in the side bays
		foreach (var (x, z) in new[] { (-6f, 54f), (-6f, 62f), (6f, 54f), (6f, 62f) })
		{
			k.Mat(stone);
			// (2026-10-15, the props pass: a plinth under the chest, its edges worn round, the lid's edge chamfered and a
			// raised slab on it for the cross; they were two sharp boxes)
			k.BevelBox(new Vector3(x, y0 + 0.07f, z), new Vector3(1.22f, 0.14f, 2.42f), 0.025f);
			k.BevelBox(new Vector3(x, y0 + 0.52f, z), new Vector3(1.1f, 0.78f, 2.3f), 0.03f);
			k.BevelBox(new Vector3(x, y0 + 0.97f, z), new Vector3(1.25f, 0.14f, 2.45f), 0.045f);
			k.BevelBox(new Vector3(x, y0 + 1.04f + 0.0f, z), new Vector3(0.95f, 0.03f, 2.1f), 0.012f);
			k.Mat(ChurchTextures.IronMat);
			BuildKit.Box(k, new Vector3(x, y0 + 1.06f, z), new Vector3(0.1f, 0.01f, 1.4f), 1f);
			BuildKit.Box(k, new Vector3(x, y0 + 1.06f, z - 0.25f), new Vector3(0.7f, 0.01f, 0.1f), 1f);
			Collide(_stone, new Vector3(x, y0 + 0.52f, z), new Vector3(1.25f, 1.04f, 2.45f));
		}
		// ---- the far end: an altar draped in red, a small organ, a hooded figure
		k.Mat(stone);
		BuildKit.Box(k, new Vector3(0, y0 + 0.5f, CryptZ1 - 1.6f), new Vector3(2.2f, 1f, 0.9f), 1f, BuildKit.Face.NY);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.05f, 0.05f), Roughness = 0.9f });
		BuildKit.Box(k, new Vector3(0, y0 + 1.01f, CryptZ1 - 1.6f), new Vector3(2.3f, 0.02f, 1.0f), 1f);
		BuildKit.Box(k, new Vector3(0, y0 + 0.75f, CryptZ1 - 2.11f), new Vector3(2.3f, 0.5f, 0.02f), 1f);
		Collide(_stone, new Vector3(0, y0 + 0.5f, CryptZ1 - 1.6f), new Vector3(2.3f, 1f, 1f));
		// the organ: an oak case, a keyboard, a rank of pipes in gold
		float ox = -5.2f, oz = CryptZ1 - 0.9f;
		k.Mat(ChurchTextures.OakMat);
		BuildKit.Box(k, new Vector3(ox, y0 + 0.75f, oz), new Vector3(2.4f, 1.5f, 1f), 1f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(ox, y0 + 0.95f, oz - 0.62f), new Vector3(2f, 0.08f, 0.35f), 1f);
		BuildKit.Box(k, new Vector3(ox, y0 + 1.9f, oz + 0.1f), new Vector3(2.4f, 0.8f, 0.6f), 1f);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.88f, 0.82f), Roughness = 0.4f });
		BuildKit.Box(k, new Vector3(ox, y0 + 1.0f, oz - 0.7f), new Vector3(1.8f, 0.02f, 0.18f), 1f);
		k.Mat(ChurchTextures.GoldMat);
		for (int i = 0; i < 11; i++)
		{
			float x = ox - 1.05f + i * 0.21f, ht = 0.9f + 0.9f * (1f - Mathf.Abs(i - 5) / 5f);
			// (2026-10-15: each pipe on its conical foot, its mouth a step in under the body; they were plain tubes)
			TurnedKit.Lathe(k, new Vector3(x, y0 + 2.3f, oz + 0.1f), TurnedKit.P(0f, 0f, 0.018f, 0f, 0.062f, 0.2f, 0.07f, 0.21f, 0.07f, ht, 0.062f, ht + 0.004f, 0.056f, ht - 0.03f, 0f, ht - 0.03f), 10, 40f);
			k.Color = Colors.White * 0.35f;
			k.Box(new Vector3(x, y0 + 2.3f + 0.27f, oz + 0.1f - 0.066f), new Vector3(0.05f, 0.05f, 0.012f));
			k.Color = Colors.White;
		}
		Collide(_wood, new Vector3(ox, y0 + 1.2f, oz), new Vector3(2.4f, 2.4f, 1f));
		// the hooded figure, stone, head bowed, looking at its hands
		var fig = new Vector3(5.6f, y0, CryptZ1 - 1.2f);
		k.Mat(stone);
		k.Color = Colors.White * 0.8f;
		k.Cylinder(fig, fig + Vector3.Up * 0.3f, 0.42f, 0.42f, 8, true);
		k.Cylinder(fig + Vector3.Up * 0.3f, fig + Vector3.Up * 1.5f, 0.36f, 0.24f, 10, false);
		k.Blob(fig + Vector3.Up * 1.7f + new Vector3(0, 0, -0.06f), new Vector3(0.2f, 0.25f, 0.22f), 7, 0.1f);
		k.Blob(fig + Vector3.Up * 1.2f + new Vector3(0, 0, -0.3f), new Vector3(0.14f, 0.08f, 0.1f), 8, 0.1f);
		Collide(_stone, fig + Vector3.Up * 0.9f, new Vector3(0.84f, 1.8f, 0.84f));
		k.CommitTo(this, "Crypt", true);
		// its light: two red glass votives burning by the altar, the cold spill down each stair
		// (2026-10-14h, the interiors pass: they were red emissive tips under a red light. Now ordinary candles burning low in
		// old ruby votive glasses, the glass shaded like glass (a little of the flame's warmth through it), the light the
		// flames' own warm one; the red is the glass's and the altar cloth's.)
		var votive = new MeshKit();
		votive.Mat(new StandardMaterial3D { ResourceName = "ch_votive_glass", AlbedoColor = new Color(0.45f, 0.06f, 0.05f, 0.72f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f, MetallicSpecular = 0.7f, EmissionEnabled = true, Emission = new Color(0.5f, 0.1f, 0.04f), EmissionEnergyMultiplier = 0.35f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		var vwax = new MeshKit();
		vwax.Mat(WaxMaterial.Make(new Color(0.75f, 0.7f, 0.6f), 0.6f, "ch_votive_wax"));
		var vflame = new MeshKit();
		vflame.Mat(new StandardMaterial3D { ResourceName = "ch_votive_flame", AlbedoColor = new Color(1f, 0.8f, 0.42f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.26f), EmissionEnergyMultiplier = 3.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
		foreach (float x in new[] { -0.8f, -0.62f, 0.7f, 0.86f })
		{
			var v = new Vector3(x, y0 + 1.02f, CryptZ1 - 1.4f + (x < 0 ? 0.05f : -0.04f));
			float vh = Mathf.Abs(x) > 0.75f ? 0.11f : 0.08f;
			votive.Cylinder(v, v + Vector3.Up * vh, 0.042f, 0.05f, 10, false);
			votive.Cylinder(v, v + Vector3.Up * 0.01f, 0.042f, 0.042f, 10, true);
			vwax.Cylinder(v + Vector3.Up * 0.01f, v + Vector3.Up * (vh * 0.45f), 0.036f, 0.036f, 8, true);
			vflame.Blob(v + Vector3.Up * (vh * 0.45f + 0.03f), new Vector3(0.01f, 0.024f, 0.01f), 4, 0f, false);
		}
		votive.CommitTo(this, "Votives", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		vwax.CommitTo(this, "VotiveWax", false);
		vflame.CommitTo(this, "VotiveFlames", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		AddChild(new OmniLight3D { Position = new Vector3(0, y0 + 1.35f, CryptZ1 - 1.75f), LightColor = new Color(1f, 0.62f, 0.38f), LightEnergy = 1.1f, OmniRange = 9f, ShadowEnabled = true });
		foreach (var (x0, x1, z0, z1) in StairOpenings)
			AddChild(new OmniLight3D { Position = new Vector3((x0 + x1) * 0.5f, -1.2f, z1 - 1f), LightColor = new Color(0.7f, 0.78f, 0.92f), LightEnergy = 0.9f, OmniRange = 9f, ShadowEnabled = false });
		AddChild(new OmniLight3D { Position = HatchLocal + new Vector3(0, 1.8f, 0), LightColor = new Color(0.8f, 0.8f, 0.85f), LightEnergy = 0.35f, OmniRange = 6f, ShadowEnabled = false });
		Audio.AudioDirector.Zone(this, new Vector3(0, (y0 + CryptTop) * 0.5f, (CryptZ0 + CryptZ1) * 0.5f), new Vector3(CryptHalf * 2f, CryptTop - y0, CryptZ1 - CryptZ0), Audio.AudioDirector.Space.Hall, "CryptVerb");
	}
}
