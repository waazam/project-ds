using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge's furnishings and lights (Act 23): the grand, a little strange (the owner: the high-society,
/// eccentric place in a game of ruins; The Shining in mind, made ours). Noon, and every lamp in the place lit
/// though nobody's here: warm light everywhere, never a glare.
/// </summary>
public partial class SkiLodge
{
	/// <summary>The things to search, by place (the story puts the finds in them).</summary>
	public readonly List<Searchable> Search202 = new(), SearchPantry = new(), Search204 = new();
	/// <summary>The rooms' light switches (by their doors) and their bathrooms' (Act 23: all off to begin with).</summary>
	public readonly Dictionary<int, LightSwitch> RoomSwitches = new(), BathSwitches = new();
	public Node3D BarCardSpot { get; private set; }
	public Node3D Mop { get; private set; }
	public Node3D SillSpot203 { get; private set; }
	public Vector3 BarCounterWorld => ToGlobal(new Vector3(BarCounterX, 1.1f, 4f));
	public const float BarCounterX = -19.2f;
	private readonly List<OmniLight3D> _dayLights = new();
	private OmniLight3D _fireLight;

	private void BuildFurnishings()
	{
		BuildLobbyFurniture();
		BuildChristmas();
		BuildBar();
		BuildDetailSounds();
		BuildServiceAndPantry();
		BuildRooms();
		BuildDiningFurniture();
	}

	private OmniLight3D Light(Vector3 at, Color c, float energy, float range, string name = "Light")
	{
		var l = new OmniLight3D { Name = name, Position = at, LightColor = c, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.2f, ShadowEnabled = false };
		AddChild(l);
		return l;
	}

	private static readonly Color Warm = new(1f, 0.78f, 0.5f), Day = new(0.72f, 0.8f, 0.95f);
	private static StandardMaterial3D Shade => LodgeTextures.Glow("lodge_shade", new Color(0.95f, 0.78f, 0.52f), 0.9f);
	private static StandardMaterial3D Bulb => LodgeTextures.Glow("lodge_bulb", new Color(1f, 0.86f, 0.62f), 2.2f);

	// ------------------------------------------------------------------ the lobby

	private void BuildLobbyFurniture()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		// ---- the fireplace: a river-stone chimney breast up the back-east wall to the roof, a deep hearth, a mantel
		Vector3 fa = HexVert(1), fb = HexVert(2), fmid = (fa + fb) * 0.5f, fout = fmid.Normalized(), falong = (fb - fa).Normalized();
		Vector3 fc = fmid - fout * (0.4f + 0.75f);   // the breast's face, 0.75 proud of the wall
		var fbasis = new Basis(falong, Vector3.Up, -fout);
		k.Mat(LodgeTextures.RiverStoneMat);
		// the breast, full width to 7.5 m; a stepped shoulder; then the stack, narrow enough to run up through the
		// roof inside the chimney outside (4.2 m wide all the way, it stood out through the roof either side of it)
		k.Box(fc + fout * 0.375f + Vector3.Up * 3.75f, new Vector3(4.2f, 7.5f, 0.75f), 0.6f, fbasis);
		k.Box(fc + fout * 0.375f + Vector3.Up * 7.8f, new Vector3(3.2f, 0.6f, 0.75f), 0.6f, fbasis);
		k.Box(fc + fout * 0.375f + Vector3.Up * 14.5f, new Vector3(2.2f, 12.8f, 0.75f), 0.6f, fbasis);
		// (its stones are the photo's now: the old dabs of stone over it read as brown spots)
		k.Color = Colors.White;
		k.Mat(LodgeTextures.BlackMat);
		k.Box(fc - fout * 0.01f + Vector3.Up * 0.95f, new Vector3(1.9f, 1.5f, 0.02f), 1f, fbasis);                 // the firebox's black mouth
		k.Mat(LodgeTextures.RiverStoneMat);
		k.Box(fc - fout * 0.4f + Vector3.Up * 0.15f, new Vector3(3.6f, 0.3f, 0.8f), 0.6f, fbasis);                 // the hearth
		k.Mat(BuildingTextures.LogMat);
		k.Box(fc - fout * 0.12f + Vector3.Up * 1.95f, new Vector3(3.8f, 0.24f, 0.36f), 0.5f, fbasis);              // the mantel, a split log
		LodgeKit.Solid(_inBody, fc + fout * 0.2f + Vector3.Up * 1.5f, new Vector3(4.2f, 3f, 1.4f), Mathf.Atan2(-falong.Z, falong.X));
		// the fire: a low one, burning in an empty lodge (logs, embers; its light moves slowly, never flickers hard)
		k.Mat(BuildingTextures.RoundLogMat);
		k.Cylinder(fc - fout * 0.3f + falong * -0.5f + Vector3.Up * 0.38f, fc - fout * 0.3f + falong * 0.5f + Vector3.Up * 0.4f, 0.09f, 0.09f, 7, true);
		k.Cylinder(fc - fout * 0.45f + falong * -0.4f + Vector3.Up * 0.36f, fc - fout * 0.2f + falong * 0.45f + Vector3.Up * 0.5f, 0.08f, 0.08f, 7, true);
		k.Mat(LodgeTextures.Glow("lodge_embers", new Color(1f, 0.42f, 0.12f), 1.6f));
		k.Blob(fc - fout * 0.35f + Vector3.Up * 0.32f, new Vector3(0.6f, 0.05f, 0.25f), 950, 0.4f, true, 1f);
		_fireLight = Light(fc - fout * 0.9f + Vector3.Up * 0.7f, new Color(1f, 0.55f, 0.25f), 1.6f, 7f, "FireLight");
		AddFlames(fc - fout * 0.35f + Vector3.Up * 0.45f, falong);
		// the elk's head over the mantel (antlers: the house knows what's outside)
		var rng = new RandomNumberGenerator { Seed = 777 };
		Vector3 head = fc - fout * 0.25f + Vector3.Up * 4.2f;
		k.Mat(PropTextures.FurMat);
		k.Color = new Color(0.3f, 0.22f, 0.16f);   // a dark elk (lighter, the head read as a pale lump)
		k.Blob(head + fout * 0.05f, new Vector3(0.42f, 0.5f, 0.36f), 960, 0.1f, false, 1f);
		k.Blob(head - fout * 0.5f + Vector3.Down * 0.18f, new Vector3(0.2f, 0.22f, 0.45f), 961, 0.1f, false, 1f);
		k.Mat(LodgeTextures.BlackMat);
		k.Blob(head - fout * 0.2f + falong * 0.14f + Vector3.Up * 0.05f, Vector3.One * 0.03f, 962, 0f, false, 1f);
		k.Blob(head - fout * 0.2f - falong * 0.14f + Vector3.Up * 0.05f, Vector3.One * 0.03f, 963, 0f, false, 1f);
		k.Color = Colors.White;
		k.Mat(WinterWoods.Bone);
		WinterWoods.Antler(k, head + falong * 0.2f + Vector3.Up * 0.4f, fbasis * new Basis(Vector3.Forward, -0.6f), 1.6f, rng, 0.06f);
		WinterWoods.Antler(k, head - falong * 0.2f + Vector3.Up * 0.4f, fbasis * new Basis(Vector3.Forward, 0.6f) * new Basis(Vector3.Up, Mathf.Pi), 1.6f, rng, 0.06f);
		// ---- seating before the fire: two chesterfields, two wing chairs, a low table, a big rug
		Vector3 sit = fc - fout * 4.2f;
		float fyaw = Mathf.Atan2(-fout.X, -fout.Z);   // facing the fire
		k.Mat(LodgeTextures.RugMat);
		k.Quad(sit + (falong * 3.2f + fout * 2.4f) with { Y = FloorY + 0.006f }, sit + (-falong * 3.2f + fout * 2.4f) with { Y = FloorY + 0.006f }, sit + (-falong * 3.2f - fout * 2.4f) with { Y = FloorY + 0.006f }, sit + (falong * 3.2f - fout * 2.4f) with { Y = FloorY + 0.006f }, Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		LodgeKit.Sofa(k, (sit - fout * 1.4f) with { Y = FloorY }, fyaw + Mathf.Pi, LodgeTextures.LeatherMat, 2.4f);
		LodgeKit.Sofa(k, (sit + falong * 2.3f) with { Y = FloorY }, fyaw + Mathf.Pi + Mathf.Pi * 0.5f, LodgeTextures.LeatherMat, 2.1f);
		// (spread further apart: the modelled wing chairs are broader than the old blocks, and the two overlapped)
		LodgeKit.Armchair(k, (sit - falong * 2.1f + fout * 0.95f) with { Y = FloorY }, fyaw + Mathf.Pi - 0.9f, LodgeTextures.VelvetVioletMat);
		LodgeKit.Armchair(k, (sit - falong * 2.45f - fout * 1.05f) with { Y = FloorY }, fyaw + Mathf.Pi - 1.4f, LodgeTextures.VelvetVioletMat);
		LodgeKit.Table(k, (sit + fout * 0.1f) with { Y = FloorY }, fyaw, new Vector2(1.4f, 0.8f), 0.42f, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat);
		foreach (var (p, sz) in new[] { (sit - fout * 1.4f, new Vector3(2.4f, 0.9f, 0.9f)), (sit + falong * 2.3f, new Vector3(0.9f, 0.9f, 2.1f)), (sit + fout * 0.1f, new Vector3(1.4f, 0.45f, 0.8f)) })
			LodgeKit.Solid(_inBody, p with { Y = FloorY + sz.Y * 0.5f }, sz, Mathf.Atan2(-falong.Z, falong.X));
		foreach (var fl in new[] { sit - falong * 3f - fout * 1.8f, sit + falong * 3.2f - fout * 1.6f })
		{
			var bulb = LodgeKit.Lamp(k, fl with { Y = FloorY }, 1.6f, Shade, true);
			Light(bulb, Warm, 0.7f, 5f);
		}
		// ---- the front desk, by the front doors: a panelled counter, pigeonholes and the room keys' hooks behind
		Vector3 d0 = new(5.1f, FloorY, 7.3f);   // (east of the doors: on the west, the balcony's corner post came down through it)
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(d0 + new Vector3(0, 0.55f, 0), new Vector3(3.2f, 1.1f, 0.6f), 1f);
		k.Mat(LodgeTextures.GoldMat);
		k.Box(d0 + new Vector3(0, 1.11f, 0), new Vector3(3.3f, 0.04f, 0.7f), 1f);
		k.Box(d0 + new Vector3(0, 0.55f, -0.31f), new Vector3(3.2f, 0.06f, 0.02f), 1f);
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(new Vector3(4.3f, 1.9f, HexIn - 0.12f), new Vector3(2.8f, 1.6f, 0.24f), 1f);   // the pigeonholes' case
		k.Mat(LodgeTextures.BlackMat);
		for (int r = 0; r < 4; r++) for (int c = 0; c < 8; c++)
			k.Box(new Vector3(3.12f + c * 0.34f, 1.3f + r * 0.36f, HexIn - 0.245f), new Vector3(0.28f, 0.28f, 0.01f), 1f);
		k.Mat(LodgeTextures.BrassMat);
		foreach (var (num, x) in new[] { ("201", 5.35f), ("202", 5.0f), ("203", 4.65f), ("204", 4.3f) })
			k.Cylinder(new Vector3(x, 2.85f, HexIn - 0.24f), new Vector3(x, 2.85f, HexIn - 0.3f), 0.01f, 0.01f, 5, true);
		// the bell, the ledger open, a green-shaded desk lamp
		k.Cylinder(d0 + new Vector3(0.9f, 1.13f, 0), d0 + new Vector3(0.9f, 1.2f, 0), 0.05f, 0.02f, 10, true);
		k.Mat(LodgeTextures.LinenMat);
		k.Box(d0 + new Vector3(-0.3f, 1.14f, 0.05f), new Vector3(0.5f, 0.02f, 0.36f), 1f, new Basis(Vector3.Up, 0.1f));
		var deskBulb = LodgeKit.Lamp(k, d0 + new Vector3(-1.2f, 1.13f, 0.1f), 0.45f, LodgeTextures.Glow("lodge_greenshade", new Color(0.2f, 0.5f, 0.3f), 0.8f));
		Light(deskBulb, Warm, 0.5f, 3.5f);
		LodgeKit.Solid(_inBody, d0 + new Vector3(0, 0.55f, 0), new Vector3(3.2f, 1.1f, 0.6f));
		SignKit.Text(this, "RECEPTION", new Vector3(4.3f, 2.95f, HexIn - 0.26f), new Basis(Vector3.Up, Mathf.Pi), 0.11f, new Color(0.75f, 0.58f, 0.3f), shadow: false);
		foreach (var (num, x) in new[] { ("201", 5.35f), ("202", 5.0f), ("203", 4.65f), ("204", 4.3f) })
			SignKit.Text(this, num, new Vector3(x, 2.72f, HexIn - 0.245f), new Basis(Vector3.Up, Mathf.Pi), 0.04f, new Color(0.8f, 0.7f, 0.45f), shadow: false);
		// ---- the front doors, from inside (their own nodes: they're pushed, and then they're gone: SkiLodge.Finale.cs)
		BuildFrontDoorsInside();
		// ---- the chandelier: iron and antler, three rings of candle bulbs, hanging over the middle of it all
		var bulbs = LodgeKit.Chandelier(k, new Vector3(0, 18f, 0), 8.2f, 2.8f, 14, Bulb);
		Light(new Vector3(0, 8.8f, 0), Warm, 2.4f, 18f, "Chandelier");
		// ---- sconces along the upper walls and under the balcony
		for (int i = 0; i < 6; i++)
		{
			Vector3 a = HexVert(i), b = HexVert(i + 1), mid = (a + b) * 0.5f, inw = -mid.Normalized();
			if (i == 1) continue;   // the fireplace's wall
			var s1 = LodgeKit.Sconce(k, mid + inw * 0.4f + Vector3.Up * (i >= 3 && i <= 4 ? 2.4f : i == 2 ? 5.6f : 3.0f) + (b - a).Normalized() * 2.4f, inw, Shade);
			Light(s1, Warm, 0.55f, 5f);
		}
		// ---- daylight: the high windows (noon behind the snow): soft cool fill
		foreach (var p in new[] { new Vector3(0, 7f, -7.5f), new Vector3(0, 7f, 7.5f) })
			_dayLights.Add(Light(p, Day, 0.9f, 14f, "Daylight"));
		// ---- odds and ends: a grandfather clock by the stairs
		if (!FurnitureKit.Add(k, "grandfather_clock", new Vector3(6.5f, FloorY, -6.6f), 0.52f + Mathf.Pi, Woodwork))
		{
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Box(new Vector3(6.5f, 1.05f, -6.6f), new Vector3(0.55f, 2.1f, 0.4f), 1f, new Basis(Vector3.Up, 0.52f));
			k.Mat(LodgeTextures.PorcelainMat);
			k.Cylinder(new Vector3(6.4f, 1.7f, -6.4f), new Vector3(6.35f, 1.7f, -6.3f), 0.17f, 0.17f, 14, true);
		}
		LobbyClutter(k, fc, fout, falong, sit, fyaw, d0);
		LodgeKit.Solid(_inBody, new Vector3(6.5f, 1.05f, -6.6f), new Vector3(0.6f, 2.1f, 0.45f), 0.52f);
		k.CommitTo(this, "LobbyFurniture", true);
	}

	public PickupInteractable FrontInsideUse { get; private set; }
	/// <summary>The lobby's clutter that can be picked up and looked at (<see cref="Curio"/>).</summary>
	public System.Collections.Generic.List<Curio> Curios { get; } = new();

	/// <summary>The roles' materials for the modelled clutter (FurnitureKit): the lodge's own.</summary>
	private static System.Collections.Generic.Dictionary<string, Material> Woodwork => new()
	{
		["wood"] = LodgeTextures.DarkWoodMat, ["metal"] = LodgeTextures.BrassMat, ["face"] = LodgeTextures.PorcelainMat, ["black"] = LodgeTextures.BlackMat,
		["glass"] = BottleGlass, ["upholstery"] = LodgeTextures.LeatherMat, ["leather"] = LodgeTextures.LeatherMat, ["paper"] = Paper,
		["label"] = Paper, ["cork"] = LodgeTextures.DarkWoodMat, ["ceramic"] = LodgeTextures.PorcelainMat, ["cloth"] = Wool,
		["iron"] = LodgeTextures.IronMat, ["bark"] = BuildingTextures.LogMat, ["wax"] = Wax, ["rubber"] = LodgeTextures.BlackMat,
		["ski"] = LodgeTextures.DarkWoodMat,
	};
	private static StandardMaterial3D _bottleGlass, _paper, _wool, _wax;
	private static StandardMaterial3D BottleGlass => _bottleGlass ??= new StandardMaterial3D { ResourceName = "lodge_bottle_glass", AlbedoColor = new Color(0.16f, 0.2f, 0.12f), Roughness = 0.12f, MetallicSpecular = 0.7f, RimEnabled = true, Rim = 0.25f };
	private static StandardMaterial3D Paper => _paper ??= new StandardMaterial3D { ResourceName = "lodge_paper", AlbedoColor = new Color(0.62f, 0.58f, 0.48f), Roughness = 0.95f };
	private static StandardMaterial3D Wool => _wool ??= new StandardMaterial3D { ResourceName = "lodge_wool", AlbedoColor = new Color(0.2f, 0.16f, 0.13f), Roughness = 1f, AlbedoTexture = LodgeTextures.LinenMat.AlbedoTexture, Uv1Scale = Vector3.One * 2f };
	private static StandardMaterial3D Wax => _wax ??= WaxMaterial.Make(new Color(0.8f, 0.76f, 0.64f), 0.5f, "lodge_wax");

	/// <summary>The lobby's clutter (the owner, 2026-09-30: more clutter, more environmental detail): logs by the hearth, a
	/// clock and candlesticks on the mantel, books and magazines and glasses on the low table, a forgotten tumbler and an
	/// ashtray by a wing chair, the desk's books.</summary>
	private void LobbyClutter(MeshKit k, Vector3 fc, Vector3 fout, Vector3 falong, Vector3 sit, float fyaw, Vector3 d0)
	{
		var w = Woodwork;
		float fy = Mathf.Atan2(-fout.X, -fout.Z) + Mathf.Pi;   // (facing out from the fire)
		// logs on the hearth to the side of the firebox, in their cradle
		FurnitureKit.Add(k, "log_pile", (fc - fout * 0.45f + falong * 1.45f) with { Y = 0.3f }, fy, w);
		// the mantel (its top at 2.07): the clock in the middle, a candlestick each side, books at one end
		float my = 2.07f;
		FurnitureKit.Add(k, "mantel_clock", (fc - fout * 0.12f) with { Y = my }, fy, w);
		foreach (float a in new[] { -1.1f, 1.1f }) FurnitureKit.Add(k, "candlestick", (fc - fout * 0.12f + falong * a) with { Y = my }, fy, w);
		FurnitureKit.Add(k, "books_stack", (fc - fout * 0.1f - falong * 1.6f) with { Y = my }, fy + 0.3f, w);
		// the low table before the fire (its top at 0.42): a stack of books, magazines fanned, two glasses, a bottle
		var t = (sit + fout * 0.1f) with { Y = 0.42f };
		FurnitureKit.Add(k, "magazines", t + falong * 0.3f, fyaw + 0.4f, w);
		FurnitureKit.Add(k, "books_stack", t - falong * 0.4f + fout * 0.1f, fyaw - 0.2f, w);
		FurnitureKit.Add(k, "tumbler", t + falong * 0.05f - fout * 0.22f, 0f, w);
		FurnitureKit.Add(k, "tumbler", t - falong * 0.12f - fout * 0.18f, 0f, w);
		FurnitureKit.Add(k, "bottle_whiskey", t - falong * 0.02f - fout * 0.05f, 0.5f, w);
		// by the wing chairs, on the floor: an ashtray and a glass left beside one, a mug by the other
		FurnitureKit.Add(k, "ashtray", (sit - falong * 2.75f + fout * 0.1f) with { Y = FloorY }, 0.3f, w);
		FurnitureKit.Add(k, "wine_glass", (sit - falong * 2.6f + fout * 0.25f) with { Y = FloorY }, 0f, w);
		FurnitureKit.Add(k, "mug", (sit - falong * 2.85f - fout * 1.35f) with { Y = FloorY }, 1f, w);
		// some of it can be picked up and looked at (the detail pass)
		Curios.Add(Curio.Add(this, t - falong * 0.02f - fout * 0.05f + Vector3.Up * 0.16f, "Pick up the bottle", Curio.Kind.Glass,
			"Whiskey. Two glasses poured beside it, and neither one touched.", "The glasses are still cold."));
		Curios.Add(Curio.Add(this, t - falong * 0.4f + fout * 0.1f + Vector3.Up * 0.08f, "Look at the books", Curio.Kind.Book,
			"Paperbacks, their spines cracked. Each one is dog-eared at the same page.", "Page 202."));
		Curios.Add(Curio.Add(this, (sit - falong * 2.85f - fout * 1.35f) with { Y = FloorY + 0.06f }, "Pick up the mug", Curio.Kind.Ceramic,
			"Coffee, with a skin of ice across it.", "Whoever left it was sitting right here."));
		Curios.Add(Curio.Add(this, (fc - fout * 0.1f - falong * 1.6f) with { Y = my + 0.1f }, "Look at the books", Curio.Kind.Book,
			"Trail guides from the season the lodge opened. Every run is circled in pencil but one.", "That one goes up into the trees, behind the lodge."));
		// the front desk (its top at 1.13): a few books at its end, an ashtray
		FurnitureKit.Add(k, "books_row_b", d0 + new Vector3(1.05f, 1.13f, 0.15f), Mathf.Pi, w);
		FurnitureKit.Add(k, "ashtray", d0 + new Vector3(0.45f, 1.13f, -0.1f), 0.2f, w);
	}

	/// <summary>A low fire's flames: a few soft orange billboards rising slowly (no flicker: they drift).</summary>
	private void AddFlames(Vector3 at, Vector3 along)
	{
		var p = new GpuParticles3D
		{
			Name = "Flames", Amount = 40, Lifetime = 0.9f, Position = at,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.45f, 0.05f, 0.15f),
				Direction = Vector3.Up, Spread = 8f, InitialVelocityMin = 0.25f, InitialVelocityMax = 0.5f, Gravity = Vector3.Zero,
				ScaleMin = 0.6f, ScaleMax = 1.3f, Color = new Color(1f, 0.55f, 0.2f, 0.7f),
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.18f, 0.26f),
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(p);
	}

	// ------------------------------------------------------------------ the bar

	/// <summary>The bar (ours, after the Gold Room): a long counter lit from within, gold on its lip; a mirrored back
	/// bar of bottles; stools; small round tables with little lamps; a row of amber lights down the ceiling.</summary>
	private void BuildBar()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		float cx = BarCounterX, z0 = 1.6f, z1 = 6.6f;
		// the counter: dark wood sides, a front that glows amber through frosted panels, a gold lip, a black top
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(new Vector3(cx - 0.35f, 0.55f, (z0 + z1) * 0.5f), new Vector3(0.7f, 1.1f, z1 - z0), 1f);
		k.Mat(LodgeTextures.Glow("lodge_barfront", new Color(0.8f, 0.55f, 0.28f), 0.42f));   // (a warm amber, not a lightbox: up close it glared)
		k.Box(new Vector3(cx + 0.005f, 0.6f, (z0 + z1) * 0.5f), new Vector3(0.02f, 0.8f, z1 - z0 - 0.2f), 1f);
		k.Mat(LodgeTextures.GoldMat);
		k.Box(new Vector3(cx - 0.3f, 1.12f, (z0 + z1) * 0.5f), new Vector3(0.9f, 0.04f, z1 - z0 + 0.1f), 1f);
		k.Box(new Vector3(cx + 0.02f, 0.14f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.05f, z1 - z0), 1f);   // the foot rail
		k.Mat(LodgeTextures.BlackMat);
		k.Box(new Vector3(cx - 0.3f, 1.15f, (z0 + z1) * 0.5f), new Vector3(0.86f, 0.02f, z1 - z0 + 0.06f), 1f);
		LodgeKit.Solid(_inBody, new Vector3(cx - 0.35f, 0.58f, (z0 + z1) * 0.5f), new Vector3(0.75f, 1.16f, z1 - z0));
		Light(new Vector3(cx + 0.4f, 0.5f, (z0 + z1) * 0.5f), new Color(1f, 0.7f, 0.35f), 0.45f, 3f, "CounterGlow");
		// under the lip, on the customers' side, where a card could be taped: a dark recess
		BarCardSpot = new Node3D { Name = "BarCardSpot", Position = new Vector3(cx + 0.13f, 1.08f, 3.9f) };
		AddChild(BarCardSpot);
		// stools
		for (float z = z0 + 0.4f; z < z1; z += 0.8f)
		{
			if (FurnitureKit.Add(k, "bar_stool", new Vector3(cx + 0.6f, FloorY, z), (z * 1.7f) % 6.28f, Woodwork)) continue;
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(new Vector3(cx + 0.6f, FloorY, z), new Vector3(cx + 0.6f, 0.72f, z), 0.025f, 0.025f, 6, false);
			k.Cylinder(new Vector3(cx + 0.6f, FloorY, z), new Vector3(cx + 0.6f, 0.03f, z), 0.2f, 0.18f, 10, true);
			k.Mat(LodgeTextures.LeatherMat);
			k.Cylinder(new Vector3(cx + 0.6f, 0.72f, z), new Vector3(cx + 0.6f, 0.8f, z), 0.2f, 0.19f, 12, true);
		}
		// the back bar on the west wall: shelves of bottles in front of a mirror, a gilt frame
		k.Mat(LodgeTextures.MirrorMat);
		k.Box(new Vector3(BarX0 + 0.1f, 2.0f, (z0 + z1) * 0.5f), new Vector3(0.02f, 1.6f, z1 - z0), 1f);
		k.Mat(LodgeTextures.GoldMat);
		k.Box(new Vector3(BarX0 + 0.12f, 2.84f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.08f, z1 - z0 + 0.2f), 1f);
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(new Vector3(BarX0 + 0.35f, 0.5f, (z0 + z1) * 0.5f), new Vector3(0.6f, 1.0f, z1 - z0), 1f);
		foreach (float y in new[] { 1.45f, 1.95f, 2.45f }) k.Box(new Vector3(BarX0 + 0.25f, y, (z0 + z1) * 0.5f), new Vector3(0.3f, 0.04f, z1 - z0), 1f);
		var brng = new RandomNumberGenerator { Seed = 4242 };
		var bottleMats = new[] { LodgeTextures.Glow("lodge_bottle_amber", new Color(0.6f, 0.32f, 0.08f), 0.15f), LodgeTextures.Glow("lodge_bottle_green", new Color(0.1f, 0.32f, 0.12f), 0.12f), LodgeTextures.Glow("lodge_bottle_clear", new Color(0.55f, 0.55f, 0.5f), 0.1f) };
		var kinds = new[] { "bottle_wine", "bottle_whiskey", "bottle_liqueur" };
		var bw = Woodwork;
		foreach (float y in new[] { 1.47f, 1.97f, 2.47f })
			for (float z = z0 + 0.15f; z < z1 - 0.1f; z += brng.RandfRange(0.1f, 0.2f))
			{
				// the modelled bottles, their glass the shelf's coloured glow (amber, green, clear)
				var bg = bottleMats[brng.RandiRange(0, 2)];
				bw["glass"] = bg;
				var at = new Vector3(BarX0 + 0.25f + brng.RandfRange(-0.05f, 0.05f), y, z);
				if (FurnitureKit.Add(k, kinds[brng.RandiRange(0, 2)], at, brng.RandfRange(0f, 6.28f), bw)) continue;
				k.Mat(bottleMats[brng.RandiRange(0, 2)]);
				float h = brng.RandfRange(0.22f, 0.34f);
				var b = new Vector3(BarX0 + 0.25f + brng.RandfRange(-0.05f, 0.05f), y, z);
				k.Cylinder(b, b + Vector3.Up * h * 0.7f, 0.035f, 0.035f, 7, true);
				k.Cylinder(b + Vector3.Up * h * 0.7f, b + Vector3.Up * h, 0.03f, 0.012f, 6, true);
			}
		LodgeKit.Solid(_inBody, new Vector3(BarX0 + 0.35f, 1.4f, (z0 + z1) * 0.5f), new Vector3(0.7f, 2.8f, z1 - z0));
		// the room: small round tables with little lamps and two chairs each
		foreach (var p in new[] { new Vector3(-16f, 0, 2f), new Vector3(-16f, 0, 4.4f), new Vector3(-16f, 0, 6.6f), new Vector3(-13.2f, 0, 1.6f), new Vector3(-13f, 0, 6.4f) })
		{
			var c = p with { Y = FloorY };
			LodgeKit.Table(k, c, 0f, new Vector2(0.8f, 0.8f), 0.74f, LodgeTextures.BlackMat, LodgeTextures.BrassMat, round: true);
			LodgeKit.Chair(k, c + new Vector3(0.62f, 0, 0), -Mathf.Pi * 0.5f, LodgeTextures.LeatherMat);
			LodgeKit.Chair(k, c + new Vector3(-0.62f, 0, 0), Mathf.Pi * 0.5f, LodgeTextures.LeatherMat);
			k.Mat(LodgeTextures.Glow("lodge_candleglass", new Color(1f, 0.6f, 0.3f), 1.2f));
			k.Cylinder(c + Vector3.Up * 0.74f, c + Vector3.Up * 0.84f, 0.035f, 0.03f, 8, true);
			LodgeKit.Solid(_inBody, c + Vector3.Up * 0.4f, new Vector3(0.8f, 0.8f, 0.8f));
		}
		// a runner of red carpet from the arch to the counter
		k.Mat(LodgeTextures.RugMat);
		k.Quad(new Vector3(-18.4f, FloorY + 0.005f, 5.2f), new Vector3(-10.6f, FloorY + 0.005f, 5.2f), new Vector3(-10.6f, FloorY + 0.005f, 3.6f), new Vector3(-18.4f, FloorY + 0.005f, 3.6f), Vector3.Up,
			new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 1), new Vector2(0, 1));
		// the ceiling's row of amber lights, and its name in gold over the arch
		for (float x = -22f; x < -9f; x += 3.2f)
		{
			k.Mat(LodgeTextures.Glow("lodge_barceil", new Color(1f, 0.72f, 0.4f), 1.4f));
			k.Box(new Vector3(x, SlabY - 0.03f, 4f), new Vector3(1.2f, 0.04f, 0.3f), 1f);
			Light(new Vector3(x, SlabY - 0.4f, 4f), new Color(1f, 0.74f, 0.45f), 0.75f, 6.5f);
		}
		k.CommitTo(this, "Bar", true);
		Vector3 aa = HexVert(4), ab = HexVert(5), aalong = (ab - aa).Normalized(), aout = ((aa + ab) * 0.5f).Normalized();
		SignKit.Text(this, "THE GILDED ANTLER", aa + aalong * 4.5f - aout * 0.42f + Vector3.Up * 3.35f, new Basis(aalong, Vector3.Up, aout) * new Basis(Vector3.Up, Mathf.Pi), 0.13f, new Color(0.8f, 0.6f, 0.26f), shadow: false);
	}

	// ------------------------------------------------------------------ the service corridor, the pantry

	private void BuildServiceAndPantry()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		// a bulb in the service corridor, coats on hooks
		k.Mat(Bulb);
		k.Blob(new Vector3(-27.6f, 2.8f, -0.7f), Vector3.One * 0.05f, 971, 0f, false, 1f);
		Light(new Vector3(-27.6f, 2.6f, -0.7f), Warm, 0.7f, 6f);
		k.Mat(Bulb);
		k.Blob(new Vector3(-30f, 2.8f, -4.8f), Vector3.One * 0.05f, 972, 0f, false, 1f);
		Light(new Vector3(-30f, 2.6f, -4.8f), Warm, 0.75f, 5.5f, "MudroomBulb");   // (a step up: the snowed-in moment read as black)
		// ---- the pantry: shelves of tins and jars on the back wall, a long run of drawer cabinets along the other
		float px0 = BarX0 + 0.3f, px1 = -9.6f;
		k.Mat(LodgeTextures.DarkWoodMat);
		foreach (float y in new[] { 0.9f, 1.45f, 2.0f, 2.55f })
			k.Box(new Vector3((px0 + px1) * 0.5f - 0.6f, y, -InnerZ + 0.2f), new Vector3(px1 - px0 - 1.2f, 0.03f, 0.35f), 1f);
		// iron brackets under them
		k.Mat(LodgeTextures.IronMat);
		for (float bx = px0 + 0.1f; bx < px1 - 1.2f; bx += 1.1f)
			foreach (float y in new[] { 0.9f, 1.45f, 2.0f, 2.55f })
			{
				k.Box(new Vector3(bx, y - 0.1f, -InnerZ + 0.06f), new Vector3(0.025f, 0.18f, 0.02f), 1f);
				k.Box(new Vector3(bx, y - 0.025f, -InnerZ + 0.2f), new Vector3(0.025f, 0.02f, 0.3f), 1f);
				k.Box(new Vector3(bx, y - 0.09f, -InnerZ + 0.14f), new Vector3(0.02f, 0.02f, 0.2f), 1f, new Basis(Vector3.Right, -0.8f));
			}
		var rng = new RandomNumberGenerator { Seed = 3131 };
		if (!StockPantry(k, px0 + 0.15f, px1 - 1.3f, -InnerZ + 0.2f, new[] { 0.915f, 1.465f, 2.015f, 2.565f }))
		for (float x = px0 + 0.2f; x < px1 - 1.4f; x += rng.RandfRange(0.15f, 0.26f))
			foreach (float y in new[] { 0.92f, 1.47f, 2.02f, 2.57f })
			{
				if (rng.Randf() < 0.3f) continue;
				k.Mat(rng.Randf() < 0.5f ? LodgeTextures.BrassMat : LodgeTextures.Glow("lodge_jar", new Color(0.5f, 0.4f, 0.22f), 0.06f));
				float h = rng.RandfRange(0.1f, 0.22f);
				k.Cylinder(new Vector3(x, y, -InnerZ + 0.2f + rng.RandfRange(-0.08f, 0.08f)), new Vector3(x, y + h, -InnerZ + 0.2f), 0.045f, 0.045f, 7, true);
			}
		LodgeKit.Solid(_inBody, new Vector3((px0 + px1) * 0.5f - 0.6f, 1.4f, -InnerZ + 0.2f), new Vector3(px1 - px0 - 1.2f, 2.8f, 0.36f));
		// the drawers: eight cabinets along the north wall, each a stack of two drawers (the second of each searchable)
		float cz = PantryZ1 - 0.3f;
		for (int i = 0; i < 8; i++)
		{
			float x = px0 + 0.45f + i * 0.95f;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Box(new Vector3(x, 0.45f, cz), new Vector3(0.9f, 0.9f, 0.55f), 1f);
			k.Mat(LodgeTextures.PlasterMat);
			k.Box(new Vector3(x, 0.92f, cz), new Vector3(0.92f, 0.04f, 0.58f), 1f);
			var d = MakeDrawer($"PantryDrawer{i}", new Vector3(x, 0.66f, cz - 0.28f), new Basis(Vector3.Up, Mathf.Pi), new Vector3(0.8f, 0.3f, 0.5f), "drawer");
			d.Flag = $"lodge_pantry_drawer_{i}";
			SearchPantry.Add(d);
		}
		LodgeKit.Solid(_inBody, new Vector3(px0 + 0.45f + 3.5f * 0.95f, 0.47f, cz), new Vector3(8f * 0.95f, 0.94f, 0.55f));
		// one bare bulb, and the mop leaning by the door (it falls)
		k.Mat(Bulb);
		k.Blob(new Vector3(-15f, 2.85f, -6.4f), Vector3.One * 0.05f, 973, 0f, false, 1f);
		Light(new Vector3(-15f, 2.6f, -6.4f), Warm, 0.7f, 7f, "PantryBulb");
		k.CommitTo(this, "Pantry", true);
		Mop = new Node3D { Name = "Mop", Position = new Vector3(-9.8f, FloorY, -6.1f) };
		AddChild(Mop);
		var m = new MeshKit();
		m.Mat(LodgeTextures.DarkWoodMat);
		m.Color = Colors.White;
		m.Cylinder(Vector3.Zero, new Vector3(0, 1.5f, 0), 0.02f, 0.02f, 6, true);
		m.Mat(LodgeTextures.LinenMat);
		for (int i = 0; i < 14; i++)
		{
			float a = Mathf.Tau * i / 14f;
			m.Cylinder(new Vector3(0, 0.12f, 0), new Vector3(Mathf.Cos(a) * 0.16f, 0.01f, Mathf.Sin(a) * 0.16f), 0.02f, 0.015f, 4, false);
		}
		m.CommitTo(Mop, "Mesh", false);
		Mop.Rotation = new Vector3(0.22f, 0, -0.15f);   // leaning on the wall
	}

	/// <summary>A drawer (a front with a brass pull, an open box behind it) as a Searchable; returns it.</summary>
	private Searchable MakeDrawer(string name, Vector3 at, Basis facing, Vector3 size, string what)
	{
		var s = new Searchable { Name = name, Position = at, Basis = facing, Type = Searchable.Kind.Drawer, Travel = size.Z * 0.75f, What = what, Grip = new Vector3(0, 0, size.Z * 0.5f + 0.03f) };
		AddChild(s);
		var k = new MeshKit();
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0, size.Z * 0.5f - 0.01f), new Vector3(size.X, size.Y, 0.025f), 1f);            // the front
		k.Box(new Vector3(0, -size.Y * 0.5f + 0.01f, 0), new Vector3(size.X - 0.06f, 0.015f, size.Z - 0.04f), 1f);   // the bottom
		k.Box(new Vector3(-size.X * 0.5f + 0.04f, -0.02f, 0), new Vector3(0.015f, size.Y - 0.06f, size.Z - 0.04f), 1f);
		k.Box(new Vector3(size.X * 0.5f - 0.04f, -0.02f, 0), new Vector3(0.015f, size.Y - 0.06f, size.Z - 0.04f), 1f);
		k.Box(new Vector3(0, -0.02f, -size.Z * 0.5f + 0.03f), new Vector3(size.X - 0.06f, size.Y - 0.06f, 0.015f), 1f);
		k.Mat(LodgeTextures.BrassMat);
		k.Box(new Vector3(0, 0, size.Z * 0.5f + 0.015f), new Vector3(0.14f, 0.025f, 0.02f), 1f);
		k.CommitTo(s.Part, "Mesh", false);
		return s;
	}

	/// <summary>A tall cabinet (wardrobe or closet) with one door as a Searchable (the door swings on its left edge).</summary>
	private Searchable MakeCabinet(string name, Vector3 at, Basis facing, Vector3 size, string what)
	{
		var body = new MeshKit();
		body.Mat(LodgeTextures.DarkWoodMat);
		body.Color = Colors.White;
		var xf = new Transform3D(facing, at);
		body.Xf = xf;
		body.Box(new Vector3(0, size.Y * 0.5f, -0.02f), new Vector3(size.X, size.Y, size.Z - 0.04f), 1f, null);
		body.Mat(LodgeTextures.BlackMat);
		body.Box(new Vector3(0, size.Y * 0.5f, size.Z * 0.5f - 0.05f), new Vector3(size.X - 0.08f, size.Y - 0.08f, 0.01f), 1f);   // the dark inside, seen when open
		body.Mat(LodgeTextures.GoldMat);
		body.Box(new Vector3(0, size.Y + 0.02f, 0), new Vector3(size.X + 0.06f, 0.05f, size.Z + 0.04f), 1f);
		body.Xf = Transform3D.Identity;
		body.CommitTo(this, name + "Body", true);
		LodgeKit.Solid(_inBody, at + facing * new Vector3(0, size.Y * 0.5f, 0), new Vector3(size.X, size.Y, size.Z), facing.GetEuler().Y);
		// the door, hinged on its left edge
		var s = new Searchable { Name = name, Position = at + facing * new Vector3(-size.X * 0.5f, 0, size.Z * 0.5f), Basis = facing, Type = Searchable.Kind.Door, Travel = -1.8f, What = what, Grip = new Vector3(size.X - 0.08f, 1.1f, 0.05f), PickRadius = 0.35f };
		AddChild(s);
		var d = new MeshKit();
		d.Mat(LodgeTextures.DarkWoodMat);
		d.Color = Colors.White;
		d.Box(new Vector3(size.X * 0.5f, size.Y * 0.5f, 0.012f), new Vector3(size.X - 0.02f, size.Y - 0.02f, 0.025f), 1f);
		d.Box(new Vector3(size.X * 0.5f, size.Y * 0.7f, 0.03f), new Vector3(size.X - 0.2f, size.Y * 0.36f, 0.012f), 1f);
		d.Box(new Vector3(size.X * 0.5f, size.Y * 0.26f, 0.03f), new Vector3(size.X - 0.2f, size.Y * 0.3f, 0.012f), 1f);
		d.Mat(LodgeTextures.BrassMat);
		d.Cylinder(new Vector3(size.X - 0.08f, 1.1f, 0.03f), new Vector3(size.X - 0.08f, 1.1f, 0.06f), 0.015f, 0.015f, 6, true);
		d.CommitTo(s.Part, "Door", true);
		// the door's own collider, swinging with it (open, it stood out into the room and could be walked through)
		var db = new StaticBody3D { Name = "DoorBody", CollisionLayer = 1, CollisionMask = 0 };
		db.SetMeta("surface", "wood");
		db.AddChild(new CollisionShape3D { Position = new Vector3(size.X * 0.5f, size.Y * 0.5f, 0.015f), Shape = new BoxShape3D { Size = new Vector3(size.X - 0.02f, size.Y - 0.02f, 0.04f) } });
		s.Part.AddChild(db);
		return s;
	}

	// ------------------------------------------------------------------ the rooms

	private void BuildRooms()
	{
		foreach (var (num, x0, x1, front) in new[] { (202, RoomSplitX, CorrX1, true), (201, CorrX0, RoomSplitX, true), (203, RoomSplitX, CorrX1, false), (204, CorrX0, RoomSplitX, false) })
			FurnishRoom(num, x0, x1, front);
	}

	/// <summary>A room: the bed against the outer wall between the windows, nightstands with a drawer each, a
	/// wardrobe, a closet by the door, a violet sofa, a writing desk, lamps; the bathroom's arched tub alcove (green
	/// with a mustard trim), a basin on a vanity with two drawers, the lavatory. 203 is wrecked with ice.</summary>
	private void FurnishRoom(int num, float x0, float x1, bool front)
	{
		float s = front ? 1f : -1f, zIn = front ? CorrHalf : -CorrHalf, zOut = front ? InnerZ : -InnerZ;
		float y = UpperY;
		var k = new MeshKit();
		k.Color = Colors.White;
		var face = new Basis(Vector3.Up, front ? Mathf.Pi : 0f);   // furniture 'forward' (-Z) toward the corridor side... turned to face into the room
		// (1.2 left only a body's width between the bed and the sofa: a squeeze to the window; 204 has no sofa, but its
		// closet, whose door swung open into a pocket between the bed and the window a body could be shut in)
		float cx = (x0 + x1) * 0.5f + (num == 204 ? -0.2f : 1.0f);
		bool wrecked = num == 203;
		// the room's lights, on its switch (off: the lodge was shut down for the holidays; 201 excepted, its own)
		var roomLights = new List<Light3D>();
		var shade = LodgeTextures.Glow($"lodge_shade_{num}", new Color(0.95f, 0.78f, 0.52f), 0.9f);
		// the bed, its head against the outer wall
		var bedAt = new Vector3(cx, y, zOut - s * 1.15f);
		LodgeKit.Bed(k, bedAt, front ? 0f : Mathf.Pi, LodgeTextures.VelvetVioletMat, LodgeTextures.VelvetVioletMat, 1.7f);
		LodgeKit.Solid(_inBody, bedAt + new Vector3(0, 0.3f, 0), new Vector3(1.8f, 0.6f, 2.2f));
		// nightstands either side, one drawer each, a lamp on each
		var room = num == 202 ? Search202 : num == 204 ? Search204 : null;
		foreach (float side in new[] { -1f, 1f })
		{
			var ns = new Vector3(cx + side * 1.6f, y, zOut - s * 0.35f);   // a clear half-metre from the bed, room to stand at it
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Box(ns + new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.6f, 0.45f), 1f);
			LodgeKit.Solid(_inBody, ns + new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.6f, 0.45f));
			var d = MakeDrawer($"Room{num}Night{(side < 0 ? "L" : "R")}", ns + new Vector3(0, 0.44f, -s * 0.2f), new Basis(Vector3.Up, front ? Mathf.Pi : 0f), new Vector3(0.42f, 0.16f, 0.38f), "nightstand's drawer");
			d.Flag = $"lodge_{num}_night_{side}";
			room?.Add(d);
			if (!wrecked)
			{
				var bulb = LodgeKit.Lamp(k, ns + new Vector3(0, 0.6f, 0), 0.5f, shade);
				roomLights.Add(Light(bulb, Warm, 0.35f, 3.5f));
				// on the nightstand beside the lamp: a mug on one, a glass on the other (somebody stayed here)
				if (num != 201) FurnitureKit.Add(k, side < 0 ? "mug" : "wine_glass", ns + new Vector3(side * 0.16f, 0.6f, s * 0.06f), side * 0.7f, Woodwork);
			}
		}
		// books stacked on the floor by the bed's foot (not in 201: it's perfect)
		if (num == 202 || num == 204) FurnitureKit.Add(k, "books_stack", bedAt + new Vector3(-1.25f, 0, -s * 0.6f), 0.4f, Woodwork);
		// the wardrobe on the far side wall, the closet by the door (both searchable)
		float farX = x0 + 0.35f, doorSideX = x1 - 0.35f;
		if (num == 202 || num == 204)
		{
			var w = MakeCabinet($"Room{num}Wardrobe", new Vector3(x0 + 0.37f, y, zOut - s * 1.4f), new Basis(Vector3.Up, Mathf.Pi * 0.5f), new Vector3(1.1f, 2.1f, 0.6f), "wardrobe");
			w.Flag = $"lodge_{num}_wardrobe";
			room?.Add(w);
			// (204's clear of the hole from 203's bathroom, further along the wall)
			var c = MakeCabinet($"Room{num}Closet", new Vector3(doorSideX, y, zIn + s * (num == 204 ? 5.0f : 1.9f)), new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(1.2f, 2.3f, 0.6f), "closet");
			c.Flag = $"lodge_{num}_closet";
			room?.Add(c);
		}
		// a violet sofa under the second window, a writing desk and chair, a rug
		if (num != 204)
		{
			var sofaAt = new Vector3(x1 - 0.48f, y, zOut - s * 2.8f);   // against the side wall (its back to it), facing into the room
			LodgeKit.Sofa(k, sofaAt, Mathf.Pi * 0.5f, LodgeTextures.VelvetVioletMat, 1.8f);
			LodgeKit.Solid(_inBody, sofaAt + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 1.8f));
		}
		k.Mat(LodgeTextures.RugMat);
		k.Quad(new Vector3(cx - 1.6f, y + 0.005f, zOut - s * 2.4f), new Vector3(cx + 1.6f, y + 0.005f, zOut - s * 2.4f), new Vector3(cx + 1.6f, y + 0.005f, zOut - s * 4.6f), new Vector3(cx - 1.6f, y + 0.005f, zOut - s * 4.6f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		// the ceiling light
		k.Mat(LodgeTextures.GoldMat);
		k.Cylinder(new Vector3(cx, RoomTop - 0.02f, (zIn + zOut) * 0.5f), new Vector3(cx, RoomTop - 0.12f, (zIn + zOut) * 0.5f), 0.22f, 0.26f, 12, true);
		if (!wrecked) roomLights.Add(Light(new Vector3(cx, RoomTop - 0.5f, (zIn + zOut) * 0.5f), num == 202 ? new Color(0.72f, 0.78f, 1f) : Warm, 0.8f, 7f, $"Room{num}Light"));
		// 202: the blue from the snow banked up the windows
		if (num == 202) Light(new Vector3(cx, y + 1.6f, zOut - s * 0.8f), new Color(0.4f, 0.55f, 1f), 1.1f, 6f, "SnowBlue");
		// ---- the bathroom: green tile, the tub in an arched alcove with a mustard trim, the vanity (two drawers)
		float bx0 = x0 + 0.07f, bx1 = x0 + 2.7f, bz = zIn + s * 3.1f;
		Vector3 tubAt = new(bx0 + 1.15f, y, bz - s * 0.45f);   // along the bathroom's far wall, in an arched alcove
		k.Mat(LodgeTextures.MintPorcelainMat);
		k.Box(tubAt + new Vector3(0, 0.28f, 0), new Vector3(1.8f, 0.56f, 0.8f), 1f);
		k.Mat(LodgeTextures.BlackMat);
		k.Box(tubAt + new Vector3(0, 0.53f, 0), new Vector3(1.6f, 0.02f, 0.62f), 1f);
		// the alcove's arch (a mustard band over the tub)
		k.Mat(LodgeTextures.MustardMat);
		float az = tubAt.Z - s * 0.48f;
		for (int i = 0; i < 12; i++)
		{
			float a0 = Mathf.Pi * i / 12f, a1 = Mathf.Pi * (i + 1) / 12f;
			Vector3 c0 = new(tubAt.X - Mathf.Cos(a0) * 0.95f, y + 1.9f + Mathf.Sin(a0) * 0.6f, az), c1 = new(tubAt.X - Mathf.Cos(a1) * 0.95f, y + 1.9f + Mathf.Sin(a1) * 0.6f, az);
			k.Beam(c0, c1, 0.06f, 0.1f);
		}
		k.Box(new Vector3(tubAt.X - 0.95f, y + 0.95f, az), new Vector3(0.1f, 1.9f, 0.06f), 1f);
		k.Box(new Vector3(tubAt.X + 0.95f, y + 0.95f, az), new Vector3(0.1f, 1.9f, 0.06f), 1f);
		LodgeKit.Solid(_inBody, tubAt + new Vector3(0, 0.3f, 0), new Vector3(1.8f, 0.6f, 0.8f));
		// lace curtains at the alcove, pale
		k.Mat(LodgeTextures.LinenMat);
		k.Box(new Vector3(tubAt.X - 0.75f, y + 1.5f, az + s * 0.04f), new Vector3(0.3f, 1.2f, 0.02f), 1f);
		k.Box(new Vector3(tubAt.X + 0.75f, y + 1.5f, az + s * 0.04f), new Vector3(0.3f, 1.2f, 0.02f), 1f);
		// the vanity against the corridor wall, a basin on it, a mirror
		Vector3 van = new(bx1 - 0.8f, y, zIn + s * 0.35f);
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(van + new Vector3(0, 0.42f, 0), new Vector3(0.9f, 0.84f, 0.5f), 1f);
		k.Mat(LodgeTextures.MintPorcelainMat);
		k.Box(van + new Vector3(0, 0.88f, 0), new Vector3(0.7f, 0.08f, 0.45f), 1f);
		k.Mat(LodgeTextures.MirrorMat);
		k.Box(new Vector3(van.X, y + 1.6f, zIn + s * 0.09f), new Vector3(0.6f, 0.7f, 0.02f), 1f);
		LodgeKit.Solid(_inBody, van + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.5f));
		foreach (int di in new[] { 0, 1 })
		{
			var d = MakeDrawer($"Room{num}Vanity{di}", van + new Vector3(0, 0.62f - di * 0.3f, s * 0.24f), new Basis(Vector3.Up, front ? 0f : Mathf.Pi), new Vector3(0.8f, 0.22f, 0.4f), "vanity drawer");
			d.Flag = $"lodge_{num}_vanity_{di}";
			room?.Add(d);
		}
		// the lavatory, against the side wall (not in 203: that wall's broken through)
		if (!wrecked)
		{
			k.Mat(LodgeTextures.MintPorcelainMat);
			Vector3 wc = new(bx0 + 0.35f, y, zIn + s * 1.9f);
			k.Box(wc + new Vector3(0, 0.22f, 0), new Vector3(0.5f, 0.44f, 0.38f), 1f);
			k.Box(wc + new Vector3(-0.2f, 0.62f, 0), new Vector3(0.14f, 0.36f, 0.42f), 1f);
		}
		// (201's bathroom: its bulb throws a deep red, and only just reaches the hacked-through wall)
		var bathLight = num == 201
			? Light(new Vector3((bx0 + bx1) * 0.5f, RoomTop - 0.6f, (zIn + bz) * 0.5f), new Color(0.85f, 0.05f, 0.04f), 1.5f, 3.3f, "Bath201Red")
			: Light(new Vector3((bx0 + bx1) * 0.5f, RoomTop - 0.5f, (zIn + bz) * 0.5f), new Color(0.85f, 1f, 0.9f), wrecked ? 0.15f : 0.5f, 4f, $"Bath{num}");
		// the switches: the room's by its door (on the latch side, at a hand's height), the bathroom's beside its door,
		// on the room's side (so it can be put on before stepping in)
		var door = RoomDoors.GetValueOrDefault(num);
		if (door != null)
		{
			float latch = front ? door.Position.X + door.Width : door.Position.X - door.Width;
			var rs = new LightSwitch { Name = $"Switch{num}", Room = $"room {num}", Position = new Vector3(latch + (front ? 0.3f : -0.3f), y + 1.25f, zIn + s * 0.072f), Rotation = new Vector3(0, front ? 0f : Mathf.Pi, 0) };
			AddChild(rs);
			foreach (var l in roomLights) rs.Wire(l);
			rs.WireGlow(shade);
			RoomSwitches[num] = rs;
		}
		var bsw = new LightSwitch { Name = $"BathSwitch{num}", Room = $"{num}'s bathroom", Position = new Vector3(bx1 + 0.075f, y + 1.25f, zIn + s * 2.35f), Rotation = new Vector3(0, Mathf.Pi * 0.5f, 0) };
		AddChild(bsw);
		bsw.Wire(bathLight);
		BathSwitches[num] = bsw;
		// 204: a desk with a drawer and a dresser with two (where the note is)
		if (num == 204)
		{
			var desk = new Vector3(x1 - 0.5f, y, zIn - 3.4f);
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Box(desk + new Vector3(0, 0.74f, 0), new Vector3(0.6f, 0.05f, 1.2f), 1f);
			foreach (float dz in new[] { -0.55f, 0.55f }) k.Box(desk + new Vector3(0, 0.37f, dz), new Vector3(0.55f, 0.74f, 0.05f), 1f);
			LodgeKit.Solid(_inBody, desk + new Vector3(0, 0.4f, 0), new Vector3(0.6f, 0.8f, 1.2f));
			var dd = MakeDrawer("Room204Desk", desk + new Vector3(-0.1f, 0.64f, 0), new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(0.9f, 0.12f, 0.45f), "desk drawer");
			dd.Flag = "lodge_204_desk";
			Search204.Add(dd);
			var dr = new Vector3(-23.9f, y, zIn - 0.33f);   // against the corridor wall, facing into the room
			k.Box(dr + new Vector3(0, 0.55f, 0), new Vector3(1.2f, 1.1f, 0.5f), 1f);
			LodgeKit.Solid(_inBody, dr + new Vector3(0, 0.55f, 0), new Vector3(1.2f, 1.1f, 0.5f));
			foreach (int di in new[] { 0, 1 })
			{
				var d = MakeDrawer($"Room204Dresser{di}", dr + new Vector3(0, 0.82f - di * 0.4f, -0.26f), new Basis(Vector3.Up, Mathf.Pi), new Vector3(1.1f, 0.32f, 0.45f), "dresser drawer");
				d.Flag = $"lodge_204_dresser_{di}";
				Search204.Add(d);
			}
		}
		// 203: the window left open, snow and ice through it, the room iced over; the bathroom's broken wall
		if (wrecked) Wreck203(k, x0, x1, s, zIn, zOut);
		// 204: the snow come in through the hole from 203's bathroom
		if (num == 204)
		{
			k.Mat(WinterWoods.SoftSnow);
			k.Blob(new Vector3(RoomSplitX - 0.9f, y, -2.5f), new Vector3(1.0f, 0.12f, 0.9f), 9204, 0.3f, true, 1f);
			k.Blob(new Vector3(RoomSplitX - 1.9f, y, -2.3f), new Vector3(0.5f, 0.05f, 0.4f), 9205, 0.3f, true, 1f);
		}
		k.CommitTo(this, $"Room{num}", true);
	}

	/// <summary>Room 203: the far window's sash left up, the sill heaped with snow and snow blown across the carpet
	/// in drifts, ice glazing the furniture, icicles off the curtain rail, the lamps dead; frost's grey light. The
	/// dining room's key lies on the sill. In the bathroom, the wall to 204 broken through: a ragged hole, plaster
	/// and lath on the floor.</summary>
	private void Wreck203(MeshKit k, float x0, float x1, float s, float zIn, float zOut)
	{
		float y = UpperY;
		float wx = -14.31f;   // the open window (the back wall's)
		k.Mat(WinterWoods.SoftSnow);
		k.Color = Colors.White;
		k.Blob(new Vector3(wx, y, zOut - s * 0.6f), new Vector3(1.2f, 0.5f, 0.8f), 9301, 0.3f, true, 1f);
		k.Blob(new Vector3(wx - 1.5f, y, zOut - s * 2.2f), new Vector3(1.6f, 0.2f, 1.4f), 9302, 0.3f, true, 1f);
		k.Blob(new Vector3(wx + 0.5f, y, zOut - s * 3.8f), new Vector3(1.3f, 0.1f, 1.2f), 9303, 0.3f, true, 1f);
		k.Box(new Vector3(wx, y + 0.95f + 4.59f - UpperY - 0.1f, zOut - s * 0.12f), new Vector3(1.2f, 0.12f, 0.28f), 1f);   // snow on the sill
		// the raised sash (the window's lower half open to the storm: a dark gap)
		k.Mat(LodgeTextures.BlackMat);
		k.Box(new Vector3(wx, 4.59f + 0.35f, zOut - s * 0.005f), new Vector3(1.0f, 0.6f, 0.01f), 1f);
		// ice: sheets on the floor near the window, icicles off the rail, frost on the bed's end
		k.Mat(WinterWoods.IceMat);
		k.Blob(new Vector3(wx - 0.6f, y + 0.02f, zOut - s * 1.6f), new Vector3(1.4f, 0.03f, 1.0f), 9304, 0.4f, true, 1f);
		for (int i = 0; i < 16; i++)
		{
			float x = wx - 1f + i * 0.13f;
			k.Cylinder(new Vector3(x, 6.45f, zOut - s * 0.1f), new Vector3(x, 6.45f - 0.08f - (i * 37 % 9) * 0.04f, zOut - s * 0.1f), 0.012f, 0.002f, 4, false);
		}
		// the hole into 204: plaster rubble on the tile, lath splinters
		k.Mat(LodgeTextures.PlasterMat);
		var rng = new RandomNumberGenerator { Seed = 2030 };
		for (int i = 0; i < 14; i++)
			k.Box(new Vector3(RoomSplitX + rng.RandfRange(0.1f, 0.9f), y + 0.03f, zIn + s * rng.RandfRange(0.6f, 2.3f)), new Vector3(rng.RandfRange(0.05f, 0.2f), 0.04f, rng.RandfRange(0.05f, 0.2f)), 1f, new Basis(Vector3.Up, rng.Randf() * 3f));
		k.Mat(LodgeTextures.DarkWoodMat);
		for (int i = 0; i < 8; i++)
		{
			float yy = y + 0.2f + i * 0.25f;
			k.Beam(new Vector3(RoomSplitX, yy, zIn + s * 0.55f), new Vector3(RoomSplitX + 0.02f, yy + 0.05f, zIn + s * (0.55f + rng.RandfRange(0.1f, 0.4f))), 0.03f, 0.01f);
			k.Beam(new Vector3(RoomSplitX, yy, zIn + s * 2.35f), new Vector3(RoomSplitX + 0.02f, yy - 0.04f, zIn + s * (2.35f - rng.RandfRange(0.1f, 0.4f))), 0.03f, 0.01f);
		}
		// grey frost-light from the open window, and snow blowing in through it
		Light(new Vector3(wx, y + 1.5f, zOut - s * 1.2f), new Color(0.65f, 0.72f, 0.85f), 0.9f, 7f, "Room203Frost");
		SillSpot203 = new Node3D { Name = "SillSpot203", Position = new Vector3(wx + 0.3f, 4.59f - 0.03f + 0.02f, zOut - s * 0.15f) };
		AddChild(SillSpot203);
		var blow = new GpuParticles3D
		{
			Name = "Blowing203", Amount = 120, Lifetime = 3f, Position = new Vector3(wx, 4.95f, zOut - s * 0.1f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				// (the owner's snowflakes: a frame of the atlas each, turned at random)
				AnimOffsetMin = 0f, AnimOffsetMax = 0.99f, AngleMin = -180f, AngleMax = 180f,
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.5f, 0.25f, 0.05f),
				Direction = new Vector3(0, -0.3f, -s), Spread = 30f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -0.4f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.6f, ScaleMin = 0.6f, ScaleMax = 1.3f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = Weather.FlakeMaterial(0.8f) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(blow);
	}

	// ------------------------------------------------------------------ the dining hall

	/// <summary>The six long tables (in two rows of three), their sheets and chairs; three chandeliers; the window
	/// at the far end left open, snow on the sill and the floor under it, the curtains stiff with frost.</summary>
	private void BuildDiningFurniture()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		var chairs = new System.Collections.Generic.List<Transform3D>();
		for (int t = 0; t < 6; t++)
		{
			var c = TableCentre(t);
			// (the tables themselves are Act 23's, each its own node so one can fall apart: BuildTable)
			for (float x = -TableLen * 0.5f + 0.6f; x < TableLen * 0.5f - 0.3f; x += 0.9f)
				foreach (float sz in new[] { -1f, 1f })
					chairs.Add(new Transform3D(new Basis(Vector3.Up, sz > 0 ? 0f : Mathf.Pi), new Vector3(c.X + x, FloorY, c.Z + sz * (TableW * 0.5f + 0.25f))));
		}
		// the chairs (all 72 the same model): one instanced set, drawn once for all of them, rather than 72 copies
		// merged into the room's mesh (the optimization pass, 2026-10-02)
		var chairMesh = FurnitureKit.Mesh("dining_chair", new() { ["wood"] = LodgeTextures.DarkWoodMat, ["upholstery"] = LodgeTextures.VelvetVioletMat });
		if (chairMesh == null) foreach (var xf in chairs) LodgeKit.Chair(k, xf.Origin, xf.Basis.GetEuler().Y, LodgeTextures.VelvetVioletMat);
		foreach (float x in new[] { 17f, 27f, 37f })
		{
			var bulbs = LodgeKit.Chandelier(k, new Vector3(x, RoomTop - 0.3f, 0), 1.6f, 1.2f, 8, Bulb);
			Light(new Vector3(x, RoomTop - 2.3f, 0), Warm, 1.4f, 12f, "DiningChandelier");
		}
		// the far end's open window: snow on the sill, a drift below, frost-stiff curtains
		k.Mat(WinterWoods.SoftSnow);
		k.Blob(new Vector3(DiningX1 - 0.7f, FloorY, 2f), new Vector3(0.9f, 0.35f, 1.1f), 9401, 0.3f, true, 1f);
		k.Blob(new Vector3(DiningX1 - 1.8f, FloorY, 2.3f), new Vector3(1.2f, 0.08f, 1.2f), 9402, 0.3f, true, 1f);
		k.Mat(LodgeTextures.BlackMat);
		k.Box(new Vector3(DiningX1 - 0.01f, 1.9f, 2f), new Vector3(0.01f, 0.6f, 0.8f), 1f);
		k.Mat(LodgeTextures.LinenMat);
		foreach (float wz in new[] { -6f, -2f, 2f, 6f })
		{
			k.Box(new Vector3(DiningX1 - 0.1f, 4.2f, wz - 0.75f), new Vector3(0.04f, 5.4f, 0.4f), 1f);
			k.Box(new Vector3(DiningX1 - 0.1f, 4.2f, wz + 0.75f), new Vector3(0.04f, 5.4f, 0.4f), 1f);
		}
		var dining = k.CommitTo(this, "DiningFurniture", true);
		if (chairMesh != null)
		{
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = chairMesh, InstanceCount = chairs.Count };
			for (int i = 0; i < chairs.Count; i++) mm.SetInstanceTransform(i, chairs[i]);
			dining.AddChild(new MultiMeshInstance3D { Name = "Chairs", Multimesh = mm });   // (under the room's mesh: the frost glazes it too)
		}
		Light(new Vector3(DiningX1 - 1.2f, 2f, 2f), new Color(0.6f, 0.7f, 0.9f), 1f, 8f, "DiningFrost");
		_diningCold = new GpuParticles3D
		{
			Name = "DiningDraught", Amount = 60, Lifetime = 4f, Position = new Vector3(DiningX1 - 0.1f, 2f, 2f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				// (the owner's snowflakes: a frame of the atlas each, turned at random)
				AnimOffsetMin = 0f, AnimOffsetMax = 0.99f, AngleMin = -180f, AngleMax = 180f,
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.05f, 0.3f, 0.4f),
				Direction = new Vector3(-1, -0.2f, 0), Spread = 25f, InitialVelocityMin = 0.3f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0, -0.2f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.5f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = Weather.FlakeMaterial(0.8f) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_diningCold);
	}

	private GpuParticles3D _diningCold;
	public const float TableLen = 7.2f, TableW = 1.3f, TableH = 0.76f;
	/// <summary>The dining tables' centres (floor), in two rows of three.</summary>
	public static Vector3 TableCentre(int t) => new(17f + (t % 3) * 10f, 0, t < 3 ? 3.3f : -3.3f);
}
