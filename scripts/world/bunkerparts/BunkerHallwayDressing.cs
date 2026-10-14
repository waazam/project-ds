using Godot;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The tunnel's dressing (the CRT room pass, 2026-10-03, the owner: "the tunnel and the crtv room need some work"). It
/// was ninety metres of the same bay; now the way down it has things to pass, so it reads as a place people worked in,
/// and left:
/// <list type="bullet">
/// <item>distance marks stencilled every ten metres, the bulkhead's distance at the entrance, RESTRICTED and hazard
/// stripes at the far end;</item>
/// <item>two sealed steel side doors on the right (one chained shut from this side), junction boxes with their
/// conduit up to the tray, a fire-hose cabinet, a wall telephone with its handset hanging off its cord;</item>
/// <item>the vault spalled in places, the rebar showing, the chunks that fell still on the floor; drains down the
/// middle; a crate or two, papers, and a pair of boots standing by the wall, nobody in them.</item>
/// </list>
/// Everything stands within the walls' last 0.2 m (outside the walking collision) or has its own.
/// </summary>
public partial class BunkerHallway
{
	private static System.Collections.Generic.Dictionary<string, Material> Roles => new()
	{
		["steel"] = BunkerTextures.PaintedMetalMat, ["plastic"] = BunkerTextures.PlasticMat, ["rubber"] = BunkerTextures.RubberMat,
		["paper"] = new StandardMaterial3D { ResourceName = "bk_paper", AlbedoColor = new Color(0.62f, 0.56f, 0.4f), Roughness = 0.95f },
		["brass"] = ProcTextures.MetalMat, ["metal"] = ProcTextures.MetalMat, ["iron"] = ProcTextures.MetalMat, ["wood"] = BuildingTextures.BoardsMat,
		["leather"] = new StandardMaterial3D { ResourceName = "bk_leather", AlbedoColor = new Color(0.16f, 0.11f, 0.08f), Roughness = 0.7f },
		["fabric"] = new StandardMaterial3D { ResourceName = "bk_fabric", AlbedoColor = new Color(0.2f, 0.19f, 0.17f), Roughness = 0.95f },
	};

	/// <summary>For the tests: the stencilled markings on its walls.</summary>
	public int Stencils { get; private set; }

	private void BuildDressing()
	{
		var rng = new RandomNumberGenerator { Seed = 8104 };
		var steel = new MeshKit();
		var body = GetNodeOrNull<StaticBody3D>("HallwayBody");
		float wx = HallHalfWidth;          // the kick walls' faces
		var paint = new Color(0.82f, 0.82f, 0.78f, 0.75f);

		// ---- markings
		// every ten metres on the left wall, above the conduits: how far in
		for (int m = 10; m <= 80; m += 10)
		{
			Stencils++;
			BunkerKit.AddDecal(this, BunkerTextures.Stencil($"{m}M"), new Vector3(-wx, 1.62f, -m), Vector3.Right, Vector3.Down,
				new Vector2(0.62f, 0.34f), 0.3f, paint);
		}
		// the entrance: the way to the bulkhead
		Stencils++;
		BunkerKit.AddDecal(this, BunkerTextures.Stencil("BULKHEAD 90M <"), new Vector3(wx, 1.55f, -4.2f), Vector3.Left, Vector3.Down,
			new Vector2(1.9f, 0.3f), 0.3f, paint);
		// the far end: restricted, both walls, and the stripes low down
		Stencils++;
		BunkerKit.AddDecal(this, BunkerTextures.Stencil("RESTRICTED"), new Vector3(wx, 1.6f, -84.5f), Vector3.Left, Vector3.Down,
			new Vector2(1.5f, 0.3f), 0.3f, new Color(0.75f, 0.2f, 0.15f, 0.7f));
		Stencils++;
		BunkerKit.AddDecal(this, BunkerTextures.Stencil("RESTRICTED"), new Vector3(-wx, 1.6f, -84.5f), Vector3.Right, Vector3.Down,
			new Vector2(1.5f, 0.3f), 0.3f, new Color(0.75f, 0.2f, 0.15f, 0.7f));
		for (float z = -82.5f; z > -HallLength + 1f; z -= RibSpacing)
			foreach (float side in new[] { -1f, 1f })
				BunkerKit.AddDecal(this, BunkerTextures.Hazard(), new Vector3(side * wx, 0.3f, z), new Vector3(-side, 0, 0), Vector3.Down,
					new Vector2(2.2f, 0.4f), 0.3f, new Color(0.8f, 0.8f, 0.8f, 0.6f));
		// the drains down the middle
		for (float z = -6.25f; z > -HallLength + 2f; z -= 10f)
			BunkerKit.AddDecal(this, BunkerTextures.Grate(), new Vector3(0, 0f, z), Vector3.Up, Vector3.Forward, new Vector2(0.42f, 0.42f), 0.2f, Colors.White);

		// ---- the side doors, on the right (the left wall carries the conduits)
		// (the kick wall is only 1.2 m high: above it the vault curves in, so what stands taller than that stands
		// out from the curve, in a surround of its own)
		float WallX(float y) => y <= HallKickHeight ? wx : Mathf.Sqrt(Mathf.Max(0f, wx * wx - (y - HallKickHeight) * (y - HallKickHeight)));
		void Door(float z, bool chained, string sign)
		{
			const float dw = 1.0f, dh = 2.0f, face = 1.9f;
			// the concrete surround: two piers and a lintel standing out of the vault, the door set in them
			steel.Mat(BunkerTextures.HallWallMat);
			steel.Color = new Color(0.9f, 0.9f, 0.88f);
			float depth = wx + 0.02f - face, cx = face + depth * 0.5f;
			foreach (float s in new[] { -1f, 1f })
				steel.Box(new Vector3(cx, 1.225f, z + s * (dw * 0.5f + 0.27f)), new Vector3(depth, 2.45f, 0.3f));
			steel.Box(new Vector3(cx, dh + 0.12f + 0.165f, z), new Vector3(depth, 0.33f, dw + 0.24f));
			body?.AddChild(new CollisionShape3D { Position = new Vector3(face + 0.05f, 1.225f, z), Shape = new BoxShape3D { Size = new Vector3(0.1f, 2.45f, dw + 0.84f) } });
			// its steel frame, and the leaf set back in it
			// (2026-10-14b, the graphics pass: the frame and the leaf were one painted steel two shades apart, plain slabs)
			// the frame bare, dark, oiled steel with eased edges; the leaf painted, a pressed panel in it and a scuffed kick
			// plate at its foot
			steel.Mat(ProcTextures.MetalMat);
			steel.Color = new Color(0.2f, 0.2f, 0.19f);
			float fx = face + 0.02f;
			steel.BevelBox(new Vector3(fx, dh + 0.06f, z), new Vector3(0.1f, 0.12f, dw + 0.24f), 0.01f);          // the frame's head
			steel.BevelBox(new Vector3(fx, dh * 0.5f, z - dw * 0.5f - 0.06f), new Vector3(0.1f, dh, 0.12f), 0.01f); // its jambs
			steel.BevelBox(new Vector3(fx, dh * 0.5f, z + dw * 0.5f + 0.06f), new Vector3(0.1f, dh, 0.12f), 0.01f);
			steel.Mat(BunkerTextures.PaintedMetalMat);
			steel.Color = new Color(0.5f, 0.52f, 0.46f);
			steel.BevelBox(new Vector3(face + 0.05f, dh * 0.5f, z), new Vector3(0.05f, dh - 0.02f, dw - 0.02f), 0.006f);   // the leaf, paler
			steel.Color = new Color(0.44f, 0.46f, 0.41f);
			steel.BevelBox(new Vector3(face + 0.022f, dh * 0.56f, z), new Vector3(0.012f, dh * 0.62f, dw - 0.26f), 0.008f); // its pressed panel
			steel.Mat(ProcTextures.MetalMat);
			steel.Color = new Color(0.36f, 0.33f, 0.28f);
			steel.BevelBox(new Vector3(face + 0.022f, 0.14f, z), new Vector3(0.008f, 0.22f, dw - 0.08f), 0.003f);           // the kick plate
			steel.Color = new Color(0.5f, 0.48f, 0.42f);
			steel.BevelBox(new Vector3(face + 0.005f, 1.05f, z + dw * 0.32f), new Vector3(0.05f, 0.04f, 0.2f), 0.012f);    // its lever
			steel.Cylinder(new Vector3(face + 0.025f, 1.05f, z + dw * 0.4f), new Vector3(face - 0.005f, 1.05f, z + dw * 0.4f), 0.035f, 0.03f, 12);   // the lever's rose
			for (int h = 0; h < 2; h++)
				steel.Cylinder(new Vector3(face + 0.015f, 0.31f + h * 1.2f, z - dw * 0.5f + 0.02f), new Vector3(face + 0.015f, 0.49f + h * 1.2f, z - dw * 0.5f + 0.02f), 0.018f, 0.018f, 8);   // hinge barrels
			// the rust weeps from under the stencil, down the leaf (it began on the lintel's concrete: an orange bar over the door)
			BunkerKit.AddDecal(this, BunkerTextures.RustRun(), new Vector3(face + 0.03f, 1.0f, z + 0.2f), Vector3.Left, Vector3.Down,
				new Vector2(0.32f, 1.3f), 0.1f, new Color(1, 1, 1, 0.65f));
			Stencils++;
			BunkerKit.AddDecal(this, BunkerTextures.Stencil(sign), new Vector3(face + 0.03f, 1.72f, z), Vector3.Left, Vector3.Down,
				new Vector2(0.55f, 0.24f), 0.15f, paint);
			if (!chained) return;
			// a chain across it, from a staple in each jamb, and a padlock hanging off it
			Vector3 a = new(face - 0.04f, 1.12f, z - dw * 0.5f - 0.06f), b = new(face - 0.04f, 1.08f, z + dw * 0.5f + 0.06f);
			// (2026-10-14b: the links were flat bars in a row, each one apart from the next) oval links of round bar, each
			// turned a quarter to the last and threaded through it, along the sag; the padlock on the lowest
			var links = 22;
			steel.Color = new Color(0.38f, 0.33f, 0.28f);
			Vector3 Sag(float t) => a.Lerp(b, t) + Vector3.Down * (Mathf.Sin(t * Mathf.Pi) * 0.14f);
			for (int i = 0; i < links; i++)
			{
				float t0 = i / (float)links, t1 = (i + 1) / (float)links;
				Vector3 p0 = Sag(t0), p1 = Sag(t1), mid = (p0 + p1) * 0.5f, along = (p1 - p0).Normalized();
				// the link's plane: alternately the wall's and square to it
				Vector3 side = i % 2 == 0 ? Vector3.Right : along.Cross(Vector3.Right).Normalized();
				float half = (p1 - p0).Length() * 0.5f + 0.012f, wid = 0.016f;
				Vector3 prev = Vector3.Zero;
				for (int s = 0; s <= 8; s++)
				{
					float ang = s / 8f * Mathf.Tau;
					var q = mid + along * (Mathf.Cos(ang) * half) + side * (Mathf.Sin(ang) * wid);
					if (s > 0) steel.Cylinder(prev, q, 0.0045f, 0.0045f, 5, false);
					prev = q;
				}
			}
			var lockAt = Sag(0.5f) + Vector3.Down * 0.07f;
			steel.Color = new Color(0.45f, 0.4f, 0.3f);
			steel.BevelBox(lockAt + Vector3.Down * 0.035f, new Vector3(0.026f, 0.055f, 0.05f), 0.008f);
			steel.Color = new Color(0.5f, 0.5f, 0.48f);
			for (int s = 0; s < 6; s++)
			{
				float a0 = s / 6f * Mathf.Pi, a1 = (s + 1) / 6f * Mathf.Pi;
				steel.Cylinder(lockAt + new Vector3(0, Mathf.Sin(a0) * 0.03f, Mathf.Cos(a0) * 0.016f), lockAt + new Vector3(0, Mathf.Sin(a1) * 0.03f, Mathf.Cos(a1) * 0.016f), 0.004f, 0.004f, 5, false);
			}
		}
		Door(-32.5f, false, "AUX 2");
		Door(-62.5f, true, "AUX 3");

		// ---- on the walls
		void JunctionBox(float z)
		{
			float bx = WallX(1.65f);   // the box's back against the vault at its top
			steel.Mat(BunkerTextures.PaintedMetalMat);
			steel.Color = new Color(0.45f, 0.47f, 0.43f);
			steel.BevelBox(new Vector3(bx - 0.08f, 1.4f, z), new Vector3(0.16f, 0.5f, 0.4f), 0.012f);   // (2026-10-14b: its edges eased)
			steel.Color = new Color(0.38f, 0.4f, 0.36f);
			steel.BevelBox(new Vector3(bx - 0.165f, 1.4f, z), new Vector3(0.012f, 0.44f, 0.34f), 0.004f);                 // its lid
			steel.Mat(ProcTextures.MetalMat);
			steel.Color = new Color(0.55f, 0.56f, 0.52f);
			steel.Cylinder(new Vector3(bx - 0.08f, 1.65f, z), new Vector3(bx - 0.08f, 1.88f, z), 0.025f, 0.025f, 6, false);   // up to the tray, under the vault
			steel.Cylinder(new Vector3(bx - 0.08f, 1.88f, z), new Vector3(1.8f, 2.19f, z), 0.025f, 0.025f, 6, false);
			Stencils++;
			BunkerKit.AddDecal(this, BunkerTextures.Stencil("440V"), new Vector3(bx - 0.17f, 1.52f, z), Vector3.Left, Vector3.Down,
				new Vector2(0.2f, 0.1f), 0.05f, new Color(0.8f, 0.7f, 0.2f, 0.75f));
		}
		JunctionBox(-17.5f);
		JunctionBox(-47.5f);
		JunctionBox(-77.5f);

		// the fire-hose cabinet, left, above the conduits (its door glass long gone)
		{
			float z = -27.5f, bx = WallX(2.0f);
			steel.Mat(BunkerTextures.PaintedMetalMat);
			steel.Color = new Color(0.36f, 0.08f, 0.06f);
			steel.BevelBox(new Vector3(-bx + 0.08f, 1.65f, z), new Vector3(0.16f, 0.7f, 0.7f), 0.014f);   // (2026-10-14b: its edges eased)
			steel.Color = new Color(0.05f, 0.04f, 0.04f);
			steel.Box(new Vector3(-bx + 0.161f, 1.65f, z), new Vector3(0.004f, 0.56f, 0.56f));                  // the dark inside
			steel.Mat(BunkerTextures.RubberMat);
			steel.Color = new Color(0.5f, 0.3f, 0.15f);
			steel.Cylinder(new Vector3(-bx + 0.04f, 1.65f, z), new Vector3(-bx + 0.15f, 1.65f, z), 0.22f, 0.22f, 12);   // the hose, coiled
			steel.Color = new Color(0.1f, 0.07f, 0.05f);
			steel.Cylinder(new Vector3(-bx + 0.149f, 1.65f, z), new Vector3(-bx + 0.152f, 1.65f, z), 0.08f, 0.08f, 10);
			body?.AddChild(new CollisionShape3D { Position = new Vector3(-bx + 0.08f, 1.65f, z), Shape = new BoxShape3D { Size = new Vector3(0.16f, 0.7f, 0.7f) } });
			Stencils++;
			BunkerKit.AddDecal(this, BunkerTextures.Stencil("FIRE"), new Vector3(-WallX(2.12f) + 0.01f, 2.12f, z), Vector3.Right, Vector3.Down,
				new Vector2(0.4f, 0.14f), 0.1f, new Color(0.75f, 0.2f, 0.15f, 0.75f));
		}

		// the wall telephone, left: its handset off the hook, hanging by its cord to the floor
		{
			float z = -52.5f, bx = WallX(1.72f);
			// (2026-10-14b, the graphics pass: it was a black box with a smaller box on it and three boxes for a handset) a
			// moulded bakelite case with eased edges on a steel backplate, a rotary dial with its finger holes, the empty
			// cradle's two prongs, and the handset's grip with its ear and mouth cups
			var bakelite = new Color(0.1f, 0.095f, 0.085f);
			steel.Mat(BunkerTextures.PaintedMetalMat);
			steel.Color = new Color(0.4f, 0.42f, 0.38f);
			steel.BevelBox(new Vector3(-bx + 0.008f, 1.55f, z), new Vector3(0.016f, 0.44f, 0.3f), 0.004f);   // the backplate
			steel.Mat(BunkerTextures.PlasticMat);
			steel.Color = bakelite;
			steel.BevelBox(new Vector3(-bx + 0.075f, 1.55f, z), new Vector3(0.12f, 0.32f, 0.22f), 0.025f);
			steel.BevelBox(new Vector3(-bx + 0.09f, 1.68f, z), new Vector3(0.1f, 0.06f, 0.24f), 0.018f);       // the cradle's shoulder
			// the dial: a cream face, ten holes round it, a hub, the finger stop
			float df = -bx + 0.136f;
			steel.Color = new Color(0.5f, 0.46f, 0.37f);
			steel.Cylinder(new Vector3(df, 1.56f, z), new Vector3(df + 0.008f, 1.56f, z), 0.066f, 0.064f, 24);
			steel.Color = new Color(0.03f, 0.03f, 0.028f);
			for (int i = 0; i < 10; i++)
			{
				float a = -0.5f + i * 0.5f;
				var h = new Vector3(df + 0.0085f, 1.56f + Mathf.Sin(a) * 0.046f, z + Mathf.Cos(a) * 0.046f);
				steel.Cylinder(h, h + Vector3.Right * 0.001f, 0.0095f, 0.0095f, 10);
			}
			steel.Color = bakelite;
			steel.Cylinder(new Vector3(df + 0.008f, 1.56f, z), new Vector3(df + 0.014f, 1.56f, z), 0.022f, 0.02f, 14);
			steel.Mat(ProcTextures.MetalMat);
			steel.Color = new Color(0.6f, 0.58f, 0.52f);
			steel.Cylinder(new Vector3(df + 0.008f, 1.51f, z + 0.055f), new Vector3(df + 0.02f, 1.505f, z + 0.06f), 0.004f, 0.004f, 5);   // the finger stop
			// the cradle's prongs, empty
			foreach (float s in new[] { -1f, 1f })
				steel.Cylinder(new Vector3(-bx + 0.1f, 1.71f, z + s * 0.085f), new Vector3(-bx + 0.15f, 1.74f, z + s * 0.085f), 0.008f, 0.007f, 6);
			steel.Mat(BunkerTextures.RubberMat);
			steel.Color = Colors.White;
			var hook = new Vector3(-bx + 0.1f, 1.4f, z + 0.06f);
			var handset = new Vector3(-bx + 0.2f, 0.62f, z + 0.12f);
			BunkerKit.Cable(steel, hook, handset, 0.06f, 0.008f, 10);
			steel.Mat(BunkerTextures.PlasticMat);
			steel.Color = bakelite;
			var hb = Basis.FromEuler(new Vector3(0.15f, 0, 0.1f));
			Vector3 Hs(float along, float outward) => handset + hb * new Vector3(outward, -along, 0);
			steel.BevelBox(Hs(0.11f, 0f), new Vector3(0.04f, 0.2f, 0.045f), 0.014f, 1f, hb);
			foreach (float along in new[] { 0.0f, 0.22f })
			{
				steel.BevelBox(Hs(along, 0.01f), new Vector3(0.05f, 0.05f, 0.06f), 0.016f, 1f, hb);
				steel.Cylinder(Hs(along, 0.035f), Hs(along, 0.055f), 0.032f, 0.036f, 14);
			}
		}

		// ---- the vault spalling: a scar, the rebar showing, what fell
		var scar = BunkerTextures.Blotch();
		foreach (var (z, sideOf) in new[] { (-22.5f, -1f), (-57.5f, 1f), (-71.25f, -1f) })
		{
			float ang = rng.RandfRange(0.45f, 0.9f) * sideOf;   // over from the vault's crown
			var on = new Vector3(Mathf.Sin(ang) * wx, HallKickHeight + Mathf.Cos(ang) * wx, z);
			var n = new Vector3(-Mathf.Sin(ang), -Mathf.Cos(ang), 0f);
			// the scar where the cover fell away, darker in the middle, cracks running off it
			BunkerKit.AddDecal(this, scar, on, n, Vector3.Forward, new Vector2(1.5f, 1.1f), 0.4f, new Color(0.3f, 0.28f, 0.25f, 0.9f));
			BunkerKit.AddDecal(this, scar, on, n, Vector3.Forward, new Vector2(0.8f, 0.6f), 0.4f, new Color(0.12f, 0.11f, 0.1f, 0.95f));
			BunkerKit.AddDecal(this, BunkerTextures.Cracks(), on, n, Vector3.Forward, new Vector2(2.4f, 1.8f), 0.4f, new Color(1, 1, 1, 0.9f));
			// the rebar showing: a grid of rusted rods, along the tunnel and round the vault, standing out of the scar
			steel.Mat(ProcTextures.MetalMat);
			steel.Color = new Color(0.33f, 0.2f, 0.11f);
			var round = new Vector3(Mathf.Cos(ang), -Mathf.Sin(ang), 0);
			for (int r = 0; r < 3; r++)
			{
				var p = on + round * (-0.18f + r * 0.18f) + n * 0.02f;
				steel.Cylinder(p + Vector3.Forward * 0.42f, p - Vector3.Forward * 0.42f, 0.011f, 0.011f, 5);
			}
			for (int r = 0; r < 4; r++)
			{
				var p = on + new Vector3(0, 0, -0.3f + r * 0.2f) + n * 0.035f;
				steel.Cylinder(p - round * 0.3f, p + round * 0.3f + n * rng.RandfRange(0f, 0.05f), 0.01f, 0.01f, 5);
			}
			// the chunks on the floor under it, against the wall's foot
			steel.Mat(BunkerTextures.HallWallMat);
			for (int c = 0; c < 6; c++)
			{
				float sz = rng.RandfRange(0.04f, 0.11f);
				float x = Mathf.Sign(on.X) * rng.RandfRange(1.75f, 2.12f);
				steel.Color = new Color(0.85f, 0.85f, 0.83f);
				steel.Box(new Vector3(x, sz * 0.4f, z + rng.RandfRange(-0.5f, 0.5f)), new Vector3(sz, sz * 0.8f, sz * 1.3f), 1f,
					Basis.FromEuler(new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0f, 3f), rng.RandfRange(-0.4f, 0.4f))));
			}
		}

		// ---- on the floor
		// crates by the left wall (clear of the conduits), with their own collision
		foreach (var (z, stack) in new[] { (-38.6f, true), (-66.4f, false) })
		{
			var c = new Vector3(-1.62f, 0f, z);
			if (FurnitureKit.Add(steel, "crate", c, rng.RandfRange(-0.2f, 0.2f), Roles))
			{
				if (stack) FurnitureKit.Add(steel, "crate", c + new Vector3(0.02f, 0.5f, 0.03f), rng.RandfRange(-0.3f, 0.3f), Roles, new Vector3(0.85f, 0.85f, 0.85f));
				body?.AddChild(new CollisionShape3D { Position = c + new Vector3(0, stack ? 0.45f : 0.25f, 0), Shape = new BoxShape3D { Size = new Vector3(0.6f, stack ? 0.9f : 0.5f, 0.6f) } });
			}
		}
		// papers blown along it, and the boots
		foreach (var (x, z, yaw) in new[] { (0.6f, -14f, 0.4f), (-0.9f, -43.5f, 2.1f), (1.3f, -74f, -0.8f) })
			FurnitureKit.Add(steel, "papers", new Vector3(x, 0.002f, z), yaw, Roles);
		FurnitureKit.Add(steel, "boots", new Vector3(1.78f, 0f, -56.2f), -1.75f, Roles);

		steel.CommitTo(this, "Dressing");
	}
}
