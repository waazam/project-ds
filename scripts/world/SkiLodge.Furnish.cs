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
		BuildOverhaul();   // (2026-10-10b: the paintings, rugs, gear, books, clutter, the fire's embers, Christmas in the dining hall)
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
		// the mantel, a split log: its split face the shelf (at 2.07), the round of it under (2026-10-15: it was a box)
		LodgeProps.SplitLog(k, fc - fout * 0.12f + Vector3.Up * 2.07f, falong, -fout, 3.8f, 0.36f, 0.24f);
		// the fire irons on their stand, at the hearth's other end from the logs
		LodgeProps.FireTools(k, (fc - fout * 0.5f - falong * 1.45f) with { Y = 0.3f }, Mathf.Atan2(-fout.X, -fout.Z) + Mathf.Pi);
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
		LodgeKit.Armchair(k, (sit - falong * 2.1f + fout * 0.95f) with { Y = FloorY }, fyaw + Mathf.Pi - 0.9f, LodgeTextures.Upholstery("rust"));   // (2026-10-14h: rust corduroy, not violet velvet)
		LodgeKit.Armchair(k, (sit - falong * 2.45f - fout * 1.05f) with { Y = FloorY }, fyaw + Mathf.Pi - 1.4f, LodgeTextures.Upholstery("rust"));
		LodgeKit.Table(k, (sit + fout * 0.1f) with { Y = FloorY }, fyaw, new Vector2(1.4f, 0.8f), 0.42f, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat);
		foreach (var (p, sz) in new[] { (sit - fout * 1.4f, new Vector3(2.4f, 0.9f, 0.9f)), (sit + falong * 2.3f, new Vector3(0.9f, 0.9f, 2.1f)), (sit + fout * 0.1f, new Vector3(1.4f, 0.45f, 0.8f)) })
			LodgeKit.Solid(_inBody, p with { Y = FloorY + sz.Y * 0.5f }, sz, Mathf.Atan2(-falong.Z, falong.X));
		foreach (var fl0 in new[] { sit - falong * 3f - fout * 1.8f, sit + falong * 3.2f - fout * 1.6f })
		{
			// (clear of the stair's lower flight, which comes down by this side of the seating now)
			var fl = fl0;
			if (fl.X > StairXL - 0.5f && fl.X < StairXL + StairW + 0.5f && fl.Z < StairZBot + 0.5f) fl.X = StairXL + StairW + 0.55f;
			var bulb = LodgeKit.Lamp(k, fl with { Y = FloorY }, 1.6f, Shade, true);
			Light(bulb, Warm, 0.7f, 5f);
		}
		// ---- the front desk, by the front doors: a panelled counter, pigeonholes and the room keys' hooks behind
		Vector3 d0 = new(5.1f, FloorY, 7.3f);   // (east of the doors: on the west, the balcony's corner post came down through it)
		FrontDesk(k, d0);
		// (2026-10-15, the scan pass) the pigeonholes' case open-fronted: a back, a header and a plinth, sides, the shelves
		// and the uprights between the holes, a key or a slip left in some (they were 32 black plates standing proud of a
		// solid box). And a board on its cornice for the keys' hooks and the sign (they hung 24 cm out from the wall on
		// nothing)
		k.Mat(LodgeTextures.DoorWoodMat);
		const float phX0 = 2.95f, phPitch = 0.34f, phY0 = 1.12f, phRow = 0.36f, phD = 0.24f;
		float phZ = HexIn - phD * 0.5f;
		k.Color = new Color(0.45f, 0.42f, 0.4f);   // (the back, in the holes' shade)
		k.Box(new Vector3(4.3f, 1.9f, HexIn - 0.01f), new Vector3(2.8f, 1.6f, 0.02f), 1f);
		k.Color = Colors.White;
		k.BevelBox(new Vector3(4.3f, (1.1f + phY0) * 0.5f, phZ), new Vector3(2.8f, phY0 - 1.1f + 0.02f, phD), 0.006f);   // the plinth's top board
		k.BevelBox(new Vector3(4.3f, (phY0 + 4 * phRow + 2.7f) * 0.5f, phZ), new Vector3(2.8f, 2.7f - phY0 - 4 * phRow, phD), 0.012f);   // the header
		foreach (float sx in new[] { 2.9f + 0.03f, 5.7f - 0.03f })
			k.BevelBox(new Vector3(sx, 1.9f, phZ), new Vector3(0.06f, 1.6f, phD), 0.008f);   // the sides
		for (int r = 1; r < 4; r++)
			k.BevelBox(new Vector3(4.3f, phY0 + r * phRow, phZ - 0.005f), new Vector3(2.74f, 0.02f, phD - 0.02f), 0.004f);   // the shelves
		for (int c = 1; c < 8; c++)
			k.BevelBox(new Vector3(phX0 + c * phPitch, phY0 + 2f * phRow, phZ - 0.005f), new Vector3(0.02f, 4f * phRow, phD - 0.02f), 0.004f);   // the uprights
		k.BevelBox(new Vector3(4.3f, 2.72f, HexIn - 0.13f), new Vector3(2.88f, 0.05f, 0.27f), 0.012f);   // its cornice
		k.BevelBox(new Vector3(4.3f, 2.875f, HexIn - 0.235f), new Vector3(2.8f, 0.26f, 0.03f), 0.008f);   // the key board, on the cornice
		// in some of the holes: a room key on its tag, a folded slip, a letter (the rest empty)
		var phr = new RandomNumberGenerator { Seed = 1301 };
		for (int r = 0; r < 4; r++)
			for (int c = 0; c < 8; c++)
			{
				float roll = phr.Randf();
				var cell = new Vector3(phX0 + (c + 0.5f) * phPitch + phr.RandfRange(-0.05f, 0.05f), phY0 + r * phRow + 0.011f, HexIn - 0.12f);   // (on the shelf's top, or the plinth's)
				if (roll < 0.22f)
				{
					k.Mat(LodgeTextures.BrassMat);
					k.Box(cell + new Vector3(0, 0.004f, -0.05f), new Vector3(0.035f, 0.008f, 0.07f), 1f, new Basis(Vector3.Up, phr.RandfRange(-0.6f, 0.6f)));
					k.Cylinder(cell + new Vector3(0.01f, 0.008f, 0.0f), cell + new Vector3(0.01f, 0.008f, 0.06f), 0.004f, 0.004f, 4, true);
				}
				else if (roll < 0.45f)
				{
					k.Mat(Paper);
					k.Color = new Color(1.3f, 1.25f, 1.1f) * phr.RandfRange(0.8f, 1f);
					k.Box(cell + new Vector3(0, 0.003f, 0), new Vector3(phr.RandfRange(0.12f, 0.2f), 0.005f, phr.RandfRange(0.1f, 0.16f)), 1f, new Basis(Vector3.Up, phr.RandfRange(-0.3f, 0.3f)));
					k.Color = Colors.White;
				}
			}
		k.Mat(LodgeTextures.BrassMat);
		foreach (var (num, x) in new[] { ("201", 5.35f), ("202", 5.0f), ("203", 4.65f), ("204", 4.3f) })
			k.Cylinder(new Vector3(x, 2.85f, HexIn - 0.249f), new Vector3(x, 2.85f, HexIn - 0.3f), 0.01f, 0.01f, 5, true);
		// the bell, the ledger open, a green-shaded banker's lamp (2026-10-15: it was the bedside lamp in green, a pleated
		// shade on a stick; the owner: "the green lamp")
		k.Mat(LodgeTextures.BlackMat);
		LodgeProps.Lathe(k, d0 + new Vector3(0.9f, 1.13f, 0), new[] { new Vector2(0.045f, 0f), new Vector2(0.046f, 0.008f), new Vector2(0.04f, 0.012f), new Vector2(0.04f, 0.018f) }, 12, capBottom: true);
		k.Mat(LodgeTextures.BrassMat);
		LodgeProps.Lathe(k, d0 + new Vector3(0.9f, 1.148f, 0), new[] { new Vector2(0.042f, 0f), new Vector2(0.04f, 0.018f), new Vector2(0.03f, 0.04f), new Vector2(0.014f, 0.052f), new Vector2(0.006f, 0.056f), new Vector2(0.006f, 0.066f), new Vector2(0.009f, 0.068f), new Vector2(0f, 0.072f) }, 12);
		Ledger(k, d0 + new Vector3(-0.3f, 1.13f, 0.05f), 0.1f);
		var deskBulb = LodgeProps.BankersLamp(k, d0 + new Vector3(-1.2f, 1.13f, 0.12f), Mathf.Pi + 0.25f, LodgeTextures.Glow("lodge_greenshade", new Color(0.12f, 0.4f, 0.2f), 0.7f));
		Light(deskBulb, Warm, 0.5f, 3.5f);
		LodgeKit.Solid(_inBody, d0 + new Vector3(0, 0.55f, 0), new Vector3(3.2f, 1.1f, 0.6f));
		// (on the key board: the sign at its left, the numbers over the hooks)
		SignKit.Text(this, "RECEPTION", new Vector3(3.45f, 2.875f, HexIn - 0.252f), new Basis(Vector3.Up, Mathf.Pi), 0.11f, new Color(0.75f, 0.58f, 0.3f), shadow: false);
		foreach (var (num, x) in new[] { ("201", 5.35f), ("202", 5.0f), ("203", 4.65f), ("204", 4.3f) })
			SignKit.Text(this, num, new Vector3(x, 2.94f, HexIn - 0.252f), new Basis(Vector3.Up, Mathf.Pi), 0.04f, new Color(0.8f, 0.7f, 0.45f), shadow: false);
		// ---- the front doors, from inside (their own nodes: they're pushed, and then they're gone: SkiLodge.Finale.cs)
		BuildFrontDoorsInside();
		// ---- the chandelier: iron and antler, three rings of candle bulbs, hanging over the middle of it all
		var bulbs = LodgeKit.Chandelier(k, new Vector3(0, 18f, 0), 8.2f, 2.8f, 14, Bulb);
		Light(new Vector3(0, 8.8f, 0), Warm, 2.4f, 18f, "Chandelier");
		// ---- sconces along the walls: on each side the first stretch of plain wall from a few places along it (the owner,
		// 2026-10-10c: "there are lamps in weird place like on a window ... make sure lamps are in normal places")
		for (int i = 0; i < 6; i++)
		{
			Vector3 a = HexVert(i), b = HexVert(i + 1), mid = (a + b) * 0.5f, inw = -mid.Normalized(), along = (b - a).Normalized();
			if (i == 1) continue;   // the fireplace's wall
			float y = i >= 3 && i <= 4 ? 2.4f : i == 2 ? 5.6f : 2.2f;
			foreach (float off in new[] { 2.4f, -2.4f, 3.6f, -3.6f, 1.2f, -1.2f, 4.6f, -4.6f })
			{
				var at = mid + inw * 0.4f + Vector3.Up * y + along * off;
				if (!ClearOfHallOpenings(at - inw * 0.4f, 0.25f, 0.35f)) continue;
				var s1 = LodgeKit.Sconce(k, at, inw, Shade);
				Light(s1, Warm, 0.55f, 5f, "Sconce");
				break;
			}
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
		var lobby = k.CommitTo(this, "LobbyFurniture", true);
		CurioPieces(lobby);
	}

	private readonly List<(Curio c, string model, Vector3 at, float yaw)> _curioPieces = new();

	/// <summary>(2026-10-15) The curios' own meshes, each under the lobby's furniture (so the freeze glazes them with it),
	/// each its curio's highlight root: the white outline finds it (drawn into the room's one mesh, it found nothing of its
	/// own, and was off).</summary>
	private void CurioPieces(Node3D under)
	{
		foreach (var (c, model, at, yaw) in _curioPieces)
		{
			var mk = new MeshKit();
			if (!FurnitureKit.Add(mk, model, at, yaw, Woodwork)) continue;
			var holder = new Node3D { Name = c.Name + "_Piece" };
			under.AddChild(holder);
			mk.CommitTo(holder, "Mesh", false);
			c.Outline = true;
			c.HighlightRoot = c.GetPathTo(holder);
		}
		_curioPieces.Clear();
	}

	public PickupInteractable FrontInsideUse { get; private set; }
	/// <summary>The lobby's clutter that can be picked up and looked at (<see cref="Curio"/>).</summary>
	public System.Collections.Generic.List<Curio> Curios { get; } = new();

	/// <summary>The roles' materials for the modelled clutter (FurnitureKit): the lodge's own.</summary>
	private static System.Collections.Generic.Dictionary<string, Material> Woodwork => new()
	{
		// (2026-10-14d: furniture in a smooth varnished wood, not the wainscot's planks)
		["wood"] = LodgeTextures.DoorWoodMat, ["metal"] = LodgeTextures.BrassMat, ["face"] = LodgeTextures.PorcelainMat, ["black"] = LodgeTextures.BlackMat,
		["glass"] = BottleGlass, ["upholstery"] = LodgeTextures.LeatherMat, ["leather"] = LodgeTextures.LeatherMat, ["paper"] = Paper,
		["label"] = Paper, ["cork"] = LodgeTextures.DarkWoodMat, ["ceramic"] = LodgeTextures.PorcelainMat, ["cloth"] = Wool,
		["iron"] = LodgeTextures.IronMat, ["bark"] = BuildingTextures.LogMat, ["wax"] = Wax, ["rubber"] = LodgeTextures.BlackMat,
		["ski"] = LodgeTextures.DarkWoodMat,
	};
	private static StandardMaterial3D _bottleGlass, _paper, _wool, _wax;
	private static readonly Dictionary<string, StandardMaterial3D> _shelfGlass = new();
	/// <summary>(2026-10-15, the scan pass) A back-bar bottle's glass: unlit, dark and glossy, a little rim of light.</summary>
	private static StandardMaterial3D ShelfGlass(string name, Color c) => _shelfGlass.TryGetValue(name, out var m) ? m
		: _shelfGlass[name] = new StandardMaterial3D { ResourceName = name, AlbedoColor = c, Roughness = 0.1f, MetallicSpecular = 0.75f, RimEnabled = true, Rim = 0.2f };
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
		// the low table before the fire (its top at 0.42): a stack of books, magazines fanned, two glasses, a bottle
		var t = (sit + fout * 0.1f) with { Y = 0.42f };
		FurnitureKit.Add(k, "magazines", t + falong * 0.3f, fyaw + 0.4f, w);
		FurnitureKit.Add(k, "tumbler", t + falong * 0.05f - fout * 0.22f, 0f, w);
		FurnitureKit.Add(k, "tumbler", t - falong * 0.12f - fout * 0.18f, 0f, w);
		// by the wing chairs, on the floor: an ashtray and a glass left beside one, a mug by the other
		FurnitureKit.Add(k, "ashtray", (sit - falong * 2.75f + fout * 0.1f) with { Y = FloorY }, 0.3f, w);
		FurnitureKit.Add(k, "wine_glass", (sit - falong * 2.6f + fout * 0.25f) with { Y = FloorY }, 0f, w);
		// some of it can be picked up and looked at (the detail pass); (2026-10-15) each drawn as its own small mesh, not
		// into the room's, so its white outline finds it (CurioPieces, after the room's mesh is made)
		_curioPieces.Clear();
		void Piece(Curio c, string model, Vector3 at, float yaw) { Curios.Add(c); _curioPieces.Add((c, model, at, yaw)); }
		Piece(Curio.Add(this, t - falong * 0.02f - fout * 0.05f + Vector3.Up * 0.16f, "Pick up the bottle", Curio.Kind.Glass,
			"Whiskey. Two glasses poured beside it, and neither one touched.", "The glasses are still cold."), "bottle_whiskey", t - falong * 0.02f - fout * 0.05f, 0.5f);
		Piece(Curio.Add(this, t - falong * 0.4f + fout * 0.1f + Vector3.Up * 0.08f, "Look at the books", Curio.Kind.Book,
			"Paperbacks, their spines cracked. Each one is dog-eared at the same page.", "Page 202."), "books_stack", t - falong * 0.4f + fout * 0.1f, fyaw - 0.2f);
		Piece(Curio.Add(this, (sit - falong * 2.85f - fout * 1.35f) with { Y = FloorY + 0.06f }, "Pick up the mug", Curio.Kind.Ceramic,
			"Coffee, with a skin of ice across it.", "Whoever left it was sitting right here."), "mug", (sit - falong * 2.85f - fout * 1.35f) with { Y = FloorY }, 1f);
		Piece(Curio.Add(this, (fc - fout * 0.1f - falong * 1.6f) with { Y = my + 0.1f }, "Look at the books", Curio.Kind.Book,
			"Trail guides from the season the lodge opened. Every run is circled in pencil but one.", "That one goes up into the trees, behind the lodge."), "books_stack", (fc - fout * 0.1f - falong * 1.6f) with { Y = my }, fy + 0.3f);
		// the front desk (its top at 1.13): a few books at its end, an ashtray
		FurnitureKit.Add(k, "books_row_b", d0 + new Vector3(1.05f, 1.13f, 0.15f), Mathf.Pi, w);
		FurnitureKit.Add(k, "ashtray", d0 + new Vector3(0.45f, 1.13f, -0.1f), 0.2f, w);
	}

	/// <summary>(2026-10-15, the props pass) The front desk at <paramref name="d0"/> (its front toward the doors, -Z): a
	/// plinth, raised panels between pilasters, a brass rail, a thick top with its edge rounded (it was a box under a
	/// gilt slab). 3.2 m long, its top at 1.13.</summary>
	private static void FrontDesk(MeshKit k, Vector3 d0)
	{
		const float L = 3.2f, D = 0.6f, top = 1.13f;
		k.Color = Colors.White;
		// (2026-10-15, the Blender pass: tools/Blender/lodge_props.py; this code is its fallback)
		if (LodgeProps.Model(k, "lp_front_desk", new Transform3D(Basis.Identity, d0 with { Y = 0f }), ("wood", LodgeTextures.DoorWoodMat), ("brass", LodgeTextures.BrassMat))) return;
		k.Mat(LodgeTextures.DoorWoodMat);
		k.BevelBox(d0 + new Vector3(0, 0.06f, 0.015f), new Vector3(L - 0.04f, 0.12f, D - 0.03f), 0.01f);                  // the plinth
		k.Box(d0 + new Vector3(0, 0.6f, 0.01f), new Vector3(L - 0.06f, 0.96f, D - 0.04f), 1f);                             // the body
		int panels = 6;
		float pw = (L - 0.1f) / panels, zf = d0.Z - D * 0.5f + 0.02f;
		var faceB = new Basis(Vector3.Right, Vector3.Up, Vector3.Back);
		for (int i = 0; i < panels; i++)
		{
			float px = d0.X - (L - 0.1f) * 0.5f + pw * (i + 0.5f);
			k.BevelBox(new Vector3(px, 0.6f, zf + 0.004f), new Vector3(pw - 0.05f, 0.92f, 0.02f), 0.006f, 1f, faceB);   // the frame
			k.BevelBox(new Vector3(px, 0.6f, zf - 0.006f), new Vector3(pw - 0.18f, 0.66f, 0.012f), 0.006f, 1f, faceB);   // the panel
			k.BevelBox(new Vector3(px, 0.6f, zf - 0.012f), new Vector3(pw - 0.28f, 0.54f, 0.012f), 0.008f, 1f, faceB);   // its raised field
		}
		for (int i = 0; i <= panels; i++)
			k.ChamferBox(new Vector3(d0.X - (L - 0.1f) * 0.5f + pw * i, 0.6f, zf - 0.01f), new Vector3(0.07f, 0.96f, 0.05f), 0.012f);   // the pilasters
		foreach (float s in new[] { -1f, 1f })
			k.BevelBox(d0 + new Vector3(s * (L * 0.5f - 0.02f), 0.6f, 0.02f), new Vector3(0.04f, 0.96f, D - 0.04f), 0.008f);   // the ends
		// the top: thick, its edges rounded, a hand over the front
		LodgeProps.RoundSlab(k, d0 + new Vector3(0, top, -0.02f), L * 0.5f + 0.05f, D * 0.5f + 0.04f, 0.05f, 0.03f, 0.018f, 3);
		k.Mat(LodgeTextures.BrassMat);
		k.Cylinder(new Vector3(d0.X - L * 0.5f, 1.05f, zf - 0.05f), new Vector3(d0.X + L * 0.5f, 1.05f, zf - 0.05f), 0.012f, 0.012f, 8, true);   // the brass rail under it
	}

	/// <summary>The register lying open on the desk at <paramref name="at"/> (its top), turned by <paramref name="yaw"/>:
	/// leather boards, its pages fanning up from the spine, a ribbon.</summary>
	private static void Ledger(MeshKit k, Vector3 at, float yaw)
	{
		var b = new Basis(Vector3.Up, yaw);
		k.Mat(LodgeTextures.LeatherMat);
		k.Color = new Color(0.5f, 0.2f, 0.16f);
		foreach (float s in new[] { -1f, 1f })
			k.BevelBox(at + b * new Vector3(s * 0.13f, 0.004f, 0), new Vector3(0.26f, 0.008f, 0.37f), 0.003f, 1f, b * new Basis(Vector3.Back, -s * 0.03f));
		k.Color = Colors.White;
		k.Mat(Paper);
		foreach (float s in new[] { -1f, 1f })
		{
			// the block of pages, thick at the spine and curling down to the edge
			var pb = b * new Basis(Vector3.Back, -s * 0.07f);
			k.BevelBox(at + b * new Vector3(s * 0.12f, 0.018f, 0), new Vector3(0.235f, 0.016f, 0.35f), 0.006f, 1f, pb);
		}
		k.Mat(LodgeTextures.BlackMat);
		k.Box(at + b * new Vector3(0.02f, 0.03f, 0.05f), new Vector3(0.006f, 0.002f, 0.38f), 1f, b * new Basis(Vector3.Up, 0.1f));   // its ribbon
	}

	/// <summary>A low fire's flames: a few soft orange billboards rising slowly (no flicker: they drift).</summary>
	/// <summary>The fire left in the grate: low, a few tongues over the embers (the fire's 3D volumes, FireVfx; its light is
	/// the hearth's own, FireLight). 2026-10-10c: it was flat sprites.</summary>
	private void AddFlames(Vector3 at, Vector3 along)
	{
		var fx = new FireVfx
		{
			Name = "Flames", Extent = new Vector3(0.9f, 0.42f, 0.32f), FlameScale = 0.75f, Smoke = false, EmitLight = false, Haze = false, Seed = 901,
		};
		fx.Position = at + Vector3.Down * 0.08f;
		fx.Basis = new Basis(Vector3.Up, Mathf.Atan2(along.X, along.Z) - Mathf.Pi * 0.5f);
		AddChild(fx);
		fx.Intensity = 0.55f;
	}

	// ------------------------------------------------------------------ the bar

	/// <summary>The bar (ours, after the Gold Room): a long panelled counter, a brass foot rail; a mirrored back
	/// bar of bottles; stools; small round tables with little lamps; wagon wheels and enamel pendants (2026-10-14h).</summary>
	private void BuildBar()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		float cx = BarCounterX, z0 = 1.6f, z1 = 6.6f;
		// the counter: dark wood, its front in raised panels between pilasters (2026-10-14h, the kit pass: the front was a
		// panel lit from within, and with the strip lights over it the bar read as an office), a brass foot rail, a worn
		// bar top with a rounded nosing (2026-10-15: modelled in Blender, lp_bar_counter; this is its fallback)
		if (!LodgeProps.Model(k, "lp_bar_counter", new Transform3D(Basis.Identity, new Vector3(cx, 0f, (z0 + z1) * 0.5f)), ("darkwood", LodgeTextures.DarkWoodMat), ("wood", LodgeTextures.DoorWoodMat), ("brass", LodgeTextures.BrassMat)))
		{
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Box(new Vector3(cx - 0.35f, 0.55f, (z0 + z1) * 0.5f), new Vector3(0.7f, 1.1f, z1 - z0), 1f);
			k.Mat(LodgeTextures.DoorWoodMat);
			int panels = 5;
			float pw = (z1 - z0) / panels;
			var faceB = new Basis(Vector3.Forward, Vector3.Up, Vector3.Right);   // (a panel's face toward +X, the customers)
			for (int i = 0; i < panels; i++)
			{
				float pz = z0 + pw * (i + 0.5f);
				k.BevelBox(new Vector3(cx + 0.004f, 0.6f, pz), new Vector3(pw - 0.22f, 0.62f, 0.02f), 0.008f, 1f, faceB);       // the panel (its faces short of the blacklight ink on the front, 2.5 cm out)
				k.BevelBox(new Vector3(cx + 0.009f, 0.6f, pz), new Vector3(pw - 0.36f, 0.48f, 0.02f), 0.008f, 1f, faceB);   // its raised field
			}
			for (int i = 0; i <= panels; i++)
				k.ChamferBox(new Vector3(cx + 0.03f, 0.55f, z0 + pw * i), new Vector3(0.06f, 1.02f, 0.09f), 0.012f);   // the pilasters
			k.BevelBox(new Vector3(cx + 0.02f, 0.06f, (z0 + z1) * 0.5f), new Vector3(0.06f, 0.12f, z1 - z0), 0.01f);    // the kick
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(new Vector3(cx + 0.22f, 0.16f, z0 + 0.05f), new Vector3(cx + 0.22f, 0.16f, z1 - 0.05f), 0.025f, 0.025f, 8, true);   // the foot rail
			foreach (float z in new[] { z0 + 0.3f, (z0 + z1) * 0.5f, z1 - 0.3f })
				k.Cylinder(new Vector3(cx + 0.05f, 0.16f, z), new Vector3(cx + 0.22f, 0.16f, z), 0.012f, 0.012f, 5, false);
			k.Mat(LodgeTextures.DoorWoodMat);
			k.BevelBox(new Vector3(cx - 0.3f, 1.13f, (z0 + z1) * 0.5f), new Vector3(0.92f, 0.05f, z1 - z0 + 0.12f), 0.02f);
			k.Cylinder(new Vector3(cx + 0.16f, 1.12f, z0 - 0.06f), new Vector3(cx + 0.16f, 1.12f, z1 + 0.06f), 0.035f, 0.035f, 8, true);   // the nosing, rounded
		}
		LodgeKit.Solid(_inBody, new Vector3(cx - 0.35f, 0.58f, (z0 + z1) * 0.5f), new Vector3(0.75f, 1.16f, z1 - z0));
		// under the lip, on the customers' side, where a card could be taped: a dark recess
		BarCardSpot = new Node3D { Name = "BarCardSpot", Position = new Vector3(cx + 0.13f, 1.08f, 3.9f) };
		AddChild(BarCardSpot);
		// stools
		// (2026-10-15, the scan pass: they stood on a dead-straight line every 0.8 m. Now each pushed back its own way, and
		// one knocked over, lying on its side out from the counter)
		var srng = new RandomNumberGenerator { Seed = 3690 };
		int si = 0;
		for (float z0s = z0 + 0.4f; z0s < z1; z0s += 0.8f, si++)
		{
			float z = z0s + srng.RandfRange(-0.1f, 0.1f);
			if (si == 3 && FurnitureKit.Has("bar_stool"))
			{
				// over on its side (the seat's rim and the foot ring on the floor)
				var over = new Basis(Vector3.Up, 0.7f) * new Basis(Vector3.Back, Mathf.Pi * 0.5f - 0.04f);
				FurnitureKit.Add(k, "bar_stool", new Transform3D(over, new Vector3(cx + 1.25f, FloorY + 0.205f, z + 0.2f)), Woodwork);
				continue;
			}
			if (FurnitureKit.Add(k, "bar_stool", new Vector3(cx + 0.6f + srng.RandfRange(-0.04f, 0.16f), FloorY, z), (z * 1.7f) % 6.28f, Woodwork)) continue;
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(new Vector3(cx + 0.6f, FloorY, z), new Vector3(cx + 0.6f, 0.72f, z), 0.025f, 0.025f, 6, false);
			k.Cylinder(new Vector3(cx + 0.6f, FloorY, z), new Vector3(cx + 0.6f, 0.03f, z), 0.2f, 0.18f, 10, true);
			k.Mat(LodgeTextures.LeatherMat);
			k.Cylinder(new Vector3(cx + 0.6f, 0.72f, z), new Vector3(cx + 0.6f, 0.8f, z), 0.2f, 0.19f, 12, true);
		}
		// the back bar on the west wall: shelves of bottles in front of a mirror, a gilt frame
		// (2026-10-15, the props pass) the mirror in a gilt frame, a moulded cornice over it; the base a run of panelled
		// cupboards under a bar top; the shelves on brackets, each with a lip (they were a box and three planks)
		// (2026-10-15: modelled in Blender, lp_back_bar and lp_case_backbar; the code is their fallback)
		bool backModel = LodgeProps.Model(k, "lp_back_bar", new Transform3D(Basis.Identity, new Vector3(BarX0, 0f, (z0 + z1) * 0.5f)), ("mirror", LodgeTextures.MirrorMat), ("gold", LodgeTextures.GoldMat), ("wood", LodgeTextures.DoorWoodMat));
		if (!backModel)
		{
			k.Mat(LodgeTextures.MirrorMat);
			k.Box(new Vector3(BarX0 + 0.1f, 2.0f, (z0 + z1) * 0.5f), new Vector3(0.02f, 1.6f, z1 - z0), 1f);
			k.Mat(LodgeTextures.GoldMat);
			k.BevelBox(new Vector3(BarX0 + 0.12f, 2.82f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.06f, z1 - z0 + 0.16f), 0.012f);
			k.BevelBox(new Vector3(BarX0 + 0.135f, 2.87f, (z0 + z1) * 0.5f), new Vector3(0.08f, 0.05f, z1 - z0 + 0.24f), 0.015f);
			foreach (float fz in new[] { z0 - 0.04f, z1 + 0.04f })
				k.BevelBox(new Vector3(BarX0 + 0.12f, 2.0f, fz), new Vector3(0.05f, 1.66f, 0.08f), 0.012f);
		}
		var backBar = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(BarX0 + 0.35f, FloorY, (z0 + z1) * 0.5f));
		LodgeProps.Carcass(k, backBar, z1 - z0, 1.0f, 0.6f, LodgeTextures.DoorWoodMat, System.Array.Empty<(float, float)>(), legH: 0.1f, legs: false, topThick: 0.04f, bays: 8);
		k.Mat(LodgeTextures.DoorWoodMat);
		foreach (float y in backModel ? System.Array.Empty<float>() : new[] { 1.45f, 1.95f, 2.45f })
		{
			k.BevelBox(new Vector3(BarX0 + 0.25f, y, (z0 + z1) * 0.5f), new Vector3(0.3f, 0.035f, z1 - z0), 0.008f);
			k.BevelBox(new Vector3(BarX0 + 0.395f, y + 0.02f, (z0 + z1) * 0.5f), new Vector3(0.012f, 0.03f, z1 - z0), 0.004f);   // the lip
			for (float bz = z0 + 0.25f; bz < z1; bz += 1.5f)
			{
				k.BevelBox(new Vector3(BarX0 + 0.12f, y - 0.09f, bz), new Vector3(0.03f, 0.15f, 0.03f), 0.006f);
				k.Beam(new Vector3(BarX0 + 0.12f, y - 0.15f, bz), new Vector3(BarX0 + 0.33f, y - 0.02f, bz), 0.024f, 0.024f, 1f, Vector3.Forward);
			}
		}
		var brng = new RandomNumberGenerator { Seed = 4242 };
		// (2026-10-15, the scan pass: the glass was the lodge's glow, and the bottles read lit from inside. Plain glass now: dark,
		// coloured, glossy, the lamps in their shoulders)
		var bottleMats = new[] { ShelfGlass("lodge_bottle_amber", new Color(0.34f, 0.17f, 0.05f)), ShelfGlass("lodge_bottle_green", new Color(0.06f, 0.18f, 0.07f)), ShelfGlass("lodge_bottle_clear", new Color(0.36f, 0.36f, 0.33f)) };
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
			// (2026-10-15, the scan pass: both chairs square at exactly 0.62 m, every table. Now each pushed back from it by
			// its own amount, round it a little, and turned)
			foreach (float side in new[] { 1f, -1f })
			{
				float a = srng.RandfRange(-0.4f, 0.4f), r = srng.RandfRange(0.6f, 0.82f);
				var dir = new Vector3(side * Mathf.Cos(a), 0, Mathf.Sin(a));
				LodgeKit.Chair(k, c + dir * r, Mathf.Atan2(dir.X, dir.Z) + srng.RandfRange(-0.25f, 0.25f), LodgeTextures.LeatherMat);
			}
			// (2026-10-15) a net-glass candle globe, not a glowing tube
			LodgeProps.TableCandle(k, c + Vector3.Up * 0.742f, LodgeTextures.Glow("lodge_candleglass", new Color(1f, 0.6f, 0.3f), 1.2f));
			LodgeKit.Solid(_inBody, c + Vector3.Up * 0.4f, new Vector3(0.8f, 0.8f, 0.8f));
		}
		// a runner from the arch to the counter (a Navajo weave since 2026-10-14h)
		k.Mat(LodgeTextures.RugMat);
		k.Quad(new Vector3(-18.4f, FloorY + 0.005f, 5.2f), new Vector3(-10.6f, FloorY + 0.005f, 5.2f), new Vector3(-10.6f, FloorY + 0.005f, 3.6f), new Vector3(-18.4f, FloorY + 0.005f, 3.6f), Vector3.Up,
			new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 1), new Vector2(0, 1));
		// (2026-10-14h, the kit pass) Its lights: there was a row of glowing strips flat on the ceiling, and they read as an
		// office's fluorescent panels. Now two wagon wheels hung on chains over the room, their little amber shades dim, and
		// three green enamel pendants low over the counter: pools of light on the bar top and the tables, dark between
		var wheelGlow = LodgeTextures.Glow("lodge_wheel_shade", new Color(0.9f, 0.55f, 0.28f), 0.7f);
		foreach (var wc in new[] { new Vector3(-16.2f, 0f, 3.9f), new Vector3(-12.6f, 0f, 4.6f) })
		{
			LodgeKit.WagonWheel(k, wc with { Y = SlabY }, 1.25f, 0.62f, 8, wheelGlow);
			Light(wc with { Y = SlabY - 1.45f }, new Color(1f, 0.7f, 0.42f), 0.85f, 6.5f, "WheelLight");
		}
		var pendant = LodgeTextures.Glow("lodge_pendant", new Color(1f, 0.8f, 0.55f), 0.8f);
		foreach (float pz in new[] { 2.4f, 4.1f, 5.8f })
		{
			var bulbAt = LodgeKit.Pendant(k, new Vector3(cx - 0.3f, SlabY, pz), 1.75f, LodgeTextures.EnamelMat, pendant);
			Light(bulbAt + Vector3.Down * 0.1f, new Color(1f, 0.76f, 0.5f), 0.55f, 3f, "PendantLight");
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
		// (2026-10-13, the joints pass) the bare bulbs on a socket and a flex from the ceiling (3 m): they hung 10-15 cm
		// under it on nothing
		void Flex(Vector3 at)
		{
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(at + Vector3.Up * 0.035f, at + Vector3.Up * 0.09f, 0.022f, 0.018f, 8, true);
			k.Mat(LodgeTextures.BlackMat);
			k.Cylinder(at + Vector3.Up * 0.085f, new Vector3(at.X, 3.02f, at.Z), 0.005f, 0.005f, 5, false);
			k.Cylinder(new Vector3(at.X, 2.985f, at.Z), new Vector3(at.X, 3.02f, at.Z), 0.03f, 0.03f, 8, true);
		}
		Flex(new Vector3(-27.6f, 2.8f, -0.7f));
		k.Mat(Bulb);
		k.Blob(new Vector3(-27.6f, 2.8f, -0.7f), Vector3.One * 0.05f, 971, 0f, false, 1f);
		Light(new Vector3(-27.6f, 2.6f, -0.7f), Warm, 0.7f, 6f);
		k.Mat(Bulb);
		k.Blob(new Vector3(-30f, 2.8f, -4.8f), Vector3.One * 0.05f, 972, 0f, false, 1f);
		Light(new Vector3(-30f, 2.6f, -4.8f), Warm, 0.9f, 6f, "MudroomBulb");   // (a step up: the snowed-in moment read as black; 0.75 until the kit pass, 2026-10-14h, its bake a step darker)
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
		// (2026-10-15, the scan pass: that pass brought the cupboards' fronts out to the drawers, and left their backs 11 cm
		// off the wall, the worktop run back over the gap. Now cupboards and drawers stand back against the skirting: the
		// partition's face at PantryZ1 - 0.075, the skirting 1.6 cm proud of it)
		float cz = PantryZ1 - 0.3f + 0.114f;
		// (2026-10-15, the props pass) a run of base cupboards, a drawer over a panelled door in each, under one worktop run
		// back to the wall (they were boxes under a plaster slab, the drawers standing 25 cm out of them; the drawers
		// don't move: the cupboards' fronts come out to them)
		float cfront = cz - 0.28f - 0.24f + 0.011f, cback = PantryZ1 - 0.01f;
		for (int i = 0; i < 8; i++)
		{
			float x = px0 + 0.45f + i * 0.95f;
			LodgeProps.Carcass(k, new Transform3D(Basis.Identity, new Vector3(x, FloorY, cfront + 0.3f)), 0.93f, 0.94f, 0.6f, LodgeTextures.DoorWoodMat, new[] { (0.51f, 0.81f) }, openW: 0.8f, legH: 0.1f, legs: false, topThick: 0.04f, drawTop: false);
			var d = MakeDrawer($"PantryDrawer{i}", new Vector3(x, 0.66f, cz - 0.28f), new Basis(Vector3.Up, Mathf.Pi), new Vector3(0.8f, 0.3f, 0.5f), "drawer");
			d.Flag = $"lodge_pantry_drawer_{i}";
			SearchPantry.Add(d);
		}
		float wx0 = px0 + 0.45f - 0.47f, wx1 = px0 + 0.45f + 7f * 0.95f + 0.47f;
		k.Mat(LodgeTextures.DoorWoodMat);
		k.Color = new Color(0.85f, 0.8f, 0.74f);
		LodgeProps.RoundSlab(k, new Vector3((wx0 + wx1) * 0.5f, 0.94f, (cfront - 0.025f + cback) * 0.5f), (wx1 - wx0) * 0.5f + 0.01f, (cback - cfront + 0.025f) * 0.5f, 0.04f, 0.01f, 0.012f, 2);
		k.Color = Colors.White;
		LodgeKit.Solid(_inBody, new Vector3(px0 + 0.45f + 3.5f * 0.95f, 0.47f, (cfront + cback) * 0.5f), new Vector3(8f * 0.95f, 0.94f, cback - cfront));
		// one bare bulb, and the mop leaning by the door (it falls)
		Flex(new Vector3(-15f, 2.85f, -6.4f));
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

	/// <summary>(2026-10-15, the scan pass) Whether a piece <paramref name="half"/> wide either side of x, against a room's
	/// outer wall (its lining at <paramref name="zOut"/>, from x0 to x1), stands under one of that wall's windows: their
	/// sills come 11 cm into the room at 0.33 m (the windows as <see cref="Lining"/> cuts them).</summary>
	private static bool UnderSill(float x0, float x1, float zOut, float x, float half)
	{
		foreach (float wx in WingWindows(Mathf.Sign(zOut), true))
		{
			if (wx < Mathf.Min(x0, x1) + 0.6f || wx > Mathf.Max(x0, x1) - 0.6f) continue;
			if (Mathf.Abs(x - wx) < 1.1f * 0.5f + 0.12f + half) return true;
		}
		return false;
	}

	/// <summary>A drawer (a front with a brass pull, an open box behind it) as a Searchable; returns it.</summary>
	private Searchable MakeDrawer(string name, Vector3 at, Basis facing, Vector3 size, string what)
	{
		var s = new Searchable { Name = name, Position = at, Basis = facing, Type = Searchable.Kind.Drawer, Travel = size.Z * 0.75f, What = what, Grip = new Vector3(0, 0, size.Z * 0.5f + 0.03f) };
		AddChild(s);
		var k = new MeshKit();
		k.Color = Colors.White;
		// (2026-10-15, the props pass: the front eased, a raised field on a deep one, turned knobs; the furniture's smooth
		// varnish, not the wainscot's planks. The case round it stands flush with it now: LodgeProps.Carcass)
		LodgeProps.DrawerFront(k, new Vector3(0, 0, size.Z * 0.5f - 0.01f), size.X, size.Y, LodgeTextures.DoorWoodMat);
		k.Mat(LodgeTextures.DoorWoodMat);
		k.Color = new Color(0.7f, 0.66f, 0.62f);
		k.Box(new Vector3(0, -size.Y * 0.5f + 0.01f, 0), new Vector3(size.X - 0.06f, 0.015f, size.Z - 0.04f), 1f);   // the bottom
		k.Box(new Vector3(-size.X * 0.5f + 0.04f, -0.02f, 0), new Vector3(0.015f, size.Y - 0.06f, size.Z - 0.04f), 1f);
		k.Box(new Vector3(size.X * 0.5f - 0.04f, -0.02f, 0), new Vector3(0.015f, size.Y - 0.06f, size.Z - 0.04f), 1f);
		k.Box(new Vector3(0, -0.02f, -size.Z * 0.5f + 0.03f), new Vector3(size.X - 0.06f, size.Y - 0.06f, 0.015f), 1f);
		k.Color = Colors.White;
		k.CommitTo(s.Part, "Mesh", false);
		return s;
	}

	/// <summary>A tall cabinet (wardrobe or closet) with one door as a Searchable (the door swings on its left edge).</summary>
	private Searchable MakeCabinet(string name, Vector3 at, Basis facing, Vector3 size, string what)
	{
		// (2026-10-15, the props pass) a case of boards with a moulded cornice and a base rail, its sides panelled, a rail
		// inside; the door frame-and-panel (it was a box under a gilt slab, a plank for a door)
		var body = new MeshKit();
		body.Color = Colors.White;
		LodgeProps.CabinetCase(body, new Transform3D(facing, at), size, LodgeTextures.DoorWoodMat, true);
		body.CommitTo(this, name + "Body", true);
		var solid = LodgeKit.Solid(_inBody, at + facing * new Vector3(0, size.Y * 0.5f, 0), new Vector3(size.X, size.Y, size.Z), facing.GetEuler().Y);
		// the door, hinged on its left edge
		var s = new Searchable { Name = name, Position = at + facing * new Vector3(-size.X * 0.5f, 0, size.Z * 0.5f), Basis = facing, Type = Searchable.Kind.Door, Travel = -1.8f, What = what, Grip = new Vector3(size.X - 0.08f, 1.1f, 0.05f), PickRadius = 0.35f };
		AddChild(s);
		var d = new MeshKit();
		LodgeProps.CabinetDoor(d, size.X, 0.1f, size.Y - 0.008f, new Vector3(size.X - 0.08f, 1.1f, 0.025f), LodgeTextures.DoorWoodMat);
		d.CommitTo(s.Part, "Door", true);
		// the door's own collider, swinging with it (open, it stood out into the room and could be walked through)
		var db = new StaticBody3D { Name = "DoorBody", CollisionLayer = 1, CollisionMask = 0 };
		db.SetMeta("surface", "wood");
		db.AddChild(new CollisionShape3D { Position = new Vector3(size.X * 0.5f, size.Y * 0.5f, 0.015f), Shape = new BoxShape3D { Size = new Vector3(size.X - 0.02f, size.Y - 0.02f, 0.04f) } });
		s.Part.AddChild(db);
		// (2026-10-10b) a wardrobe big enough to hide in
		if (what == "wardrobe" && int.TryParse(name.Replace("Room", "").Replace("Wardrobe", ""), out int num)) BuildHides(s, solid, at, facing, size, num);
		return s;
	}

	// ------------------------------------------------------------------ the rooms

	private void BuildRooms()
	{
		foreach (var (num, x0, x1, front) in new[] { (202, RoomSplitX, CorrX1, true), (201, CorrX0, RoomSplitX, true), (203, RoomSplitX, CorrX1, false), (204, CorrX0, RoomSplitX, false) })
			FurnishRoom(num, x0, x1, front);
	}

	/// <summary>A room: the bed against the outer wall between the windows, nightstands with a drawer each, a
	/// wardrobe, a closet by the door, a corduroy sofa, a writing desk, lamps; the bathroom's arched tub alcove (green
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
		LodgeKit.Bed(k, bedAt, front ? 0f : Mathf.Pi, LodgeTextures.BedspreadMat, LodgeTextures.Upholstery("tobacco"), 1.7f);   // (2026-10-14h: a plaid spread, a tobacco headboard)
		LodgeKit.Solid(_inBody, bedAt + new Vector3(0, 0.3f, 0), new Vector3(1.8f, 0.6f, 2.2f));
		// nightstands either side, one drawer each, a lamp on each
		var room = num == 202 ? Search202 : num == 204 ? Search204 : null;
		foreach (float side in new[] { -1f, 1f })
		{
			// (2026-10-15, the props pass) a nightstand on turned legs, its drawer flush in its front and an open shelf under
			// it, 50 x 60 x 48 (it was a plank-textured box, the drawer standing 15 cm out of it).
			// (2026-10-15, the scan pass: that pass brought the case's front out to the drawer, and left its back 21 cm off the
			// wall. Now case, drawer, lamp and all stand back against the skirting (the lining's face 2 cm in from zOut, the
			// skirting 1.6 cm proud of it), or against the window's sill where one's over it: the sill comes 11 cm into the
			// room, under the nightstand's top)
			float back = UnderSill(x0, x1, zOut, cx + side * 1.6f, 0.27f) ? 0.125f : 0.04f;
			var nsC = new Vector3(cx + side * 1.6f, y, zOut - s * (back + 0.24f));
			var ns = nsC + new Vector3(0, 0, s * 0.14f);   // (where the drawer and the lamp were set from)
			LodgeProps.Carcass(k, new Transform3D(new Basis(Vector3.Up, front ? 0f : Mathf.Pi), nsC), 0.5f, 0.6f, 0.48f, LodgeTextures.DoorWoodMat, new[] { (0.36f, 0.52f) }, openW: 0.42f, legH: 0.12f, legs: true, cubby: true);
			LodgeKit.Solid(_inBody, nsC + new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.6f, 0.48f));
			var d = MakeDrawer($"Room{num}Night{(side < 0 ? "L" : "R")}", ns + new Vector3(0, 0.44f, -s * 0.2f), new Basis(Vector3.Up, front ? Mathf.Pi : 0f), new Vector3(0.42f, 0.16f, 0.38f), "nightstand's drawer");
			d.Flag = $"lodge_{num}_night_{side}";
			room?.Add(d);
			if (!wrecked)
			{
				var bulb = LodgeKit.Lamp(k, nsC + new Vector3(side * -0.04f, 0.6f, s * 0.03f), 0.6f, shade);   // (60 cm: was 50)
				roomLights.Add(Light(bulb, Warm, 0.35f, 3.5f));
				// on the nightstand beside the lamp: a mug on one, a glass on the other (somebody stayed here)
				if (num != 201) FurnitureKit.Add(k, side < 0 ? "mug" : "wine_glass", nsC + new Vector3(side * 0.15f, 0.6f, -s * 0.12f), side * 0.7f, Woodwork);
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
			LodgeKit.Sofa(k, sofaAt, Mathf.Pi * 0.5f, LodgeTextures.Upholstery(num == 202 ? "olive" : num == 201 ? "mustard" : "rust"), 1.8f);
			LodgeKit.Solid(_inBody, sofaAt + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 1.8f));
		}
		k.Mat(LodgeTextures.RugMat);
		k.Quad(new Vector3(cx - 1.6f, y + 0.005f, zOut - s * 2.4f), new Vector3(cx + 1.6f, y + 0.005f, zOut - s * 2.4f), new Vector3(cx + 1.6f, y + 0.005f, zOut - s * 4.6f), new Vector3(cx - 1.6f, y + 0.005f, zOut - s * 4.6f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		// the ceiling light
		// (2026-10-15) a frosted dome on a brass canopy, lit with the lamps on the switch (it was a gilt drum, never lit)
		LodgeProps.CeilingDome(k, new Vector3(cx, RoomTop - 0.01f, (zIn + zOut) * 0.5f), wrecked ? LodgeTextures.PorcelainMat : shade);
		if (!wrecked) roomLights.Add(Light(new Vector3(cx, RoomTop - 0.5f, (zIn + zOut) * 0.5f), num == 202 ? new Color(0.72f, 0.78f, 1f) : Warm, 0.8f, 7f, $"Room{num}Light"));
		// 202: the blue from the snow banked up the windows
		if (num == 202) Light(new Vector3(cx, y + 1.6f, zOut - s * 0.8f), new Color(0.48f, 0.58f, 0.82f), 0.45f, 5f, "SnowBlue");   // (2026-10-14h: was 1.1 of a saturated blue)
		// ---- the bathroom: green tile, the tub in an arched alcove with a mustard trim, the vanity (two drawers)
		float bx0 = x0 + 0.07f, bx1 = x0 + 2.7f, bz = zIn + s * 3.1f;
		Vector3 tubAt = new(bx0 + 1.15f, y, bz - s * 0.45f);   // along the bathroom's far wall, in an arched alcove
		// (2026-10-15, the props pass) a built-in tub: its apron rounded, a rolled rim, the basin sloping in, its taps
		// (it was a mint box with a black lid)
		LodgeProps.Tub(k, new Transform3D(Basis.Identity, tubAt), 0.9f, 0.4f, 0.56f, LodgeTextures.MintPorcelainMat);
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
		// (2026-10-15, the props pass) the vanity a case round its two drawers (flush now: they stood 18 cm out of a box),
		// on a plinth, its top run back to the wall with an oval basin let into it, its taps; the mirror framed
		// (2026-10-15, the scan pass: that pass brought the case out to the drawers and left its back 12 cm off the wall,
		// the top run back over the gap. Now the case, its drawers with it, stands against the skirting: the wall's face at
		// zIn + 0.07, the skirting 1.6 cm proud of it; the top from the wall to a hand over the front)
		const float vanShift = 0.12f;
		van -= new Vector3(0, 0, s * vanShift);
		var vanC = van + new Vector3(0, 0, s * 0.14f);
		var vanB = new Basis(Vector3.Up, front ? Mathf.Pi : 0f);
		LodgeProps.Carcass(k, new Transform3D(vanB, vanC), 0.9f, 0.9f, 0.56f, LodgeTextures.DoorWoodMat, new[] { (0.51f, 0.73f), (0.21f, 0.43f) }, openW: 0.8f, legH: 0.1f, legs: false, topThick: 0.035f, drawTop: false);
		LodgeProps.BasinTop(k, new Transform3D(vanB, vanC + new Vector3(0, 0, -s * 0.0f)), 0.94f, 0.6f, 0.9f, LodgeTextures.PorcelainMat, LodgeTextures.MintPorcelainMat, 0f, -0.04f);
		k.Mat(LodgeTextures.DoorWoodMat);
		k.BevelBox(new Vector3(van.X, y + 1.6f, zIn + s * 0.078f), new Vector3(0.7f, 0.8f, 0.016f), 0.006f);
		k.Mat(LodgeTextures.MirrorMat);
		k.Box(new Vector3(van.X, y + 1.6f, zIn + s * 0.089f), new Vector3(0.6f, 0.7f, 0.006f), 1f);   // (the joints pass: flat on the wall, it stood 1 cm off)
		LodgeKit.Solid(_inBody, vanC + new Vector3(0, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.58f));
		foreach (int di in new[] { 0, 1 })
		{
			var d = MakeDrawer($"Room{num}Vanity{di}", van + new Vector3(0, 0.62f - di * 0.3f, s * 0.24f), new Basis(Vector3.Up, front ? 0f : Mathf.Pi), new Vector3(0.8f, 0.22f, 0.4f), "vanity drawer");
			d.Flag = $"lodge_{num}_vanity_{di}";
			room?.Add(d);
		}
		// the lavatory, against the side wall (not in 203: that wall's broken through)
		if (!wrecked)
		{
			// (2026-10-15) a close-coupled pan: a pedestal flaring to its bowl, a wooden seat and lid, the cistern (two boxes)
			Vector3 wc = new(bx0 + 0.235f, y, zIn + s * 1.9f);
			LodgeProps.Lavatory(k, new Transform3D(new Basis(Vector3.Up, -Mathf.Pi * 0.5f), wc), LodgeTextures.MintPorcelainMat, LodgeTextures.DoorWoodMat);
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
			// (2026-10-15, the props pass) a writing desk on tapered legs, its drawer in the apron (it was a plank on two
			// slabs, the drawer hanging under it); its top 76.5 cm, a hand over the drawer's front
			LodgeProps.WritingDesk(k, new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), desk - new Vector3(0.015f, 0, 0)), 1.2f, 0.64f, 0.765f, 0.9f, 0.58f, 0.7f, LodgeTextures.DoorWoodMat, 0.031f);
			LodgeKit.Solid(_inBody, desk + new Vector3(-0.015f, 0.4f, 0), new Vector3(0.64f, 0.8f, 1.2f));
			var dd = MakeDrawer("Room204Desk", desk + new Vector3(-0.1f, 0.64f, 0), new Basis(Vector3.Up, -Mathf.Pi * 0.5f), new Vector3(0.9f, 0.12f, 0.45f), "desk drawer");
			dd.Flag = "lodge_204_desk";
			Search204.Add(dd);
			// (2026-10-15, the scan pass: 0.204 nearer the wall, case and drawers together: the case's back stood 20 cm off it)
			var dr = new Vector3(-23.9f, y, zIn - 0.33f + 0.204f);   // against the corridor wall (its face at zIn - 0.07, the skirting 1.6 cm proud), facing into the room
			// (2026-10-15) a chest on turned feet round its two drawers, flush (they stood 22 cm out of a box)
			var drC = dr - new Vector3(0, 0, 0.214f);
			LodgeProps.Carcass(k, new Transform3D(Basis.Identity, drC), 1.2f, 1.1f, 0.5f, LodgeTextures.DoorWoodMat, new[] { (0.66f, 0.98f), (0.26f, 0.58f) }, openW: 1.1f, legH: 0.1f, legs: true);
			LodgeKit.Solid(_inBody, drC + new Vector3(0, 0.55f, 0), new Vector3(1.2f, 1.1f, 0.5f));
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
		// snow on the sill (its top at 4.586), heaped toward the hinge side, the key's end left clear (2026-10-15: it was a
		// slab of snow floating 0.85 m over the sill)
		k.Blob(new Vector3(wx - 0.22f, 4.586f, zOut - s * 0.05f), new Vector3(0.42f, 0.075f, 0.1f), 9306, 0.25f, false, 1f);
		k.Blob(new Vector3(wx + 0.12f, 4.586f, zOut - s * 0.04f), new Vector3(0.14f, 0.03f, 0.08f), 9307, 0.25f, false, 1f);
		// the raised sash (the window's lower half open to the storm: a dark gap)
		k.Mat(LodgeTextures.BlackMat);
		k.Box(new Vector3(wx, 4.59f + 0.35f, zOut - s * 0.005f), new Vector3(1.0f, 0.6f, 0.01f), 1f);
		// ice: sheets on the floor near the window, icicles off the rail, frost on the bed's end
		k.Mat(WinterWoods.IceMat);
		k.Blob(new Vector3(wx - 0.6f, y + 0.02f, zOut - s * 1.6f), new Vector3(1.4f, 0.03f, 1.0f), 9304, 0.4f, true, 1f);
		// (2026-10-15, the scan pass: they hung every 0.13 m like a comb; now in clumps and gaps, each its own girth)
		var irng = new RandomNumberGenerator { Seed = 2031 };
		for (float x = wx - 1.02f; x < wx + 1.05f; x += irng.Randf() < 0.25f ? irng.RandfRange(0.18f, 0.32f) : irng.RandfRange(0.04f, 0.11f))
		{
			float r = irng.RandfRange(0.008f, 0.016f), len = irng.RandfRange(0.05f, 0.4f);
			float xz = zOut - s * (0.1f + irng.RandfRange(-0.008f, 0.008f));
			k.Cylinder(new Vector3(x, 6.45f, xz), new Vector3(x + irng.RandfRange(-0.01f, 0.01f), 6.45f - len, xz), r, 0.002f, 4, false);
		}
		// (2026-10-13, the joints pass) the rail they hang off, on its two brackets: there wasn't one, the icicles hung
		// 4-7 cm under nothing
		k.Mat(LodgeTextures.IronMat);
		k.Cylinder(new Vector3(wx - 1.12f, 6.46f, zOut - s * 0.1f), new Vector3(wx + 1.12f, 6.46f, zOut - s * 0.1f), 0.014f, 0.014f, 8, true);
		foreach (float bx in new[] { wx - 1.05f, wx + 1.05f })
			k.Beam(new Vector3(bx, 6.46f, zOut), new Vector3(bx, 6.46f, zOut - s * 0.1f), 0.02f, 0.03f);
		// the hole into 204: plaster rubble on the tile, lath splinters
		k.Mat(LodgeTextures.PlasterMat);
		var rng = new RandomNumberGenerator { Seed = 2030 };
		// (the joints pass: down on the tile, they lay 8 mm over it)
		for (int i = 0; i < 14; i++)
			k.Box(new Vector3(RoomSplitX + rng.RandfRange(0.1f, 0.9f), y + 0.02f, zIn + s * rng.RandfRange(0.6f, 2.3f)), new Vector3(rng.RandfRange(0.05f, 0.2f), 0.04f, rng.RandfRange(0.05f, 0.2f)), 1f, new Basis(Vector3.Up, rng.Randf() * 3f));
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
		var chairs = DiningChairs(out var sheeted);
		// the chairs (one model): one instanced set, drawn once for all of them, rather than 80-odd copies merged into the
		// room's mesh (the optimization pass, 2026-10-02)
		var chairMesh = FurnitureKit.Mesh("dining_chair", new() { ["wood"] = LodgeTextures.DoorWoodMat, ["upholstery"] = LodgeTextures.Upholstery("oxblood") });
		if (chairMesh == null) foreach (var xf in chairs) LodgeKit.Chair(k, xf.Origin, xf.Basis.GetEuler().Y, LodgeTextures.Upholstery("oxblood"));
		// (2026-10-15, the scan pass) the chairs at one end of a table or two under dust sheets of their own, as the tables
		// are: the house shut for the winter by someone who ran out of sheets
		for (int i = 0; i < sheeted.Count; i++) ChairDustSheet(k, sheeted[i], 4100 + i);
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

	/// <summary>(2026-10-15, the scan pass) The dining hall's chairs, as people left them: it was 84 of one chair in a
	/// perfect grid, 0.9 m apart, every one tucked square. Now each is pushed in or left out by its own amount and turned a
	/// little; about one in five pulled well out and swung; a few gone from their places (carried off to the
	/// end wall, where they stand in a huddle, one tipped over) and one knocked onto its back. The run at one end of a
	/// table or two is under dust sheets of its own (<paramref name="sheeted"/>: drawn by <see cref="ChairDustSheet"/>).
	/// Seeded: the same every game.</summary>
	private static List<Transform3D> DiningChairs(out List<Transform3D> sheeted)
	{
		var chairs = new List<Transform3D>();
		sheeted = new List<Transform3D>();
		var rng = new RandomNumberGenerator { Seed = 23084 };
		// which tables have a sheeted run, and at which end and side (a run of three from that end)
		var sheetRuns = new Dictionary<int, (float end, float side)> { [1] = (1f, 1f), [3] = (-1f, -1f), [5] = (1f, -1f) };
		for (int t = 0; t < 6; t++)
		{
			var c = TableCentre(t);
			// (the tables themselves are Act 23's, each its own node so one can fall apart: BuildTable)
			foreach (float sz in new[] { -1f, 1f })
			{
				int slot = 0;
				for (float x0 = -TableLen * 0.5f + 0.6f; x0 < TableLen * 0.5f - 0.3f; x0 += 0.9f, slot++)
				{
					float x = x0 + rng.RandfRange(-0.07f, 0.07f), yaw0 = sz > 0 ? 0f : Mathf.Pi;
					// under a dust sheet: square to the table, tucked (whoever sheeted them set them straight first)
					bool sheet = sheetRuns.TryGetValue(t, out var run) && run.side == sz && (run.end > 0 ? slot >= 4 : slot <= 2);
					if (sheet)
					{
						var xf = new Transform3D(new Basis(Vector3.Up, yaw0 + rng.RandfRange(-0.03f, 0.03f)), new Vector3(c.X + x0 + (x - x0) * 0.25f, FloorY, c.Z + sz * (TableW * 0.5f + 0.27f)));   // (their sheets near touching: barely shifted)
						chairs.Add(xf);
						sheeted.Add(xf);
						continue;
					}
					float roll = rng.Randf();
					if (roll < 0.06f) continue;   // gone from its place (to the huddle by the end wall)
					float push, turn;
					if (roll < 0.24f) { push = rng.RandfRange(0.32f, 0.6f); turn = rng.RandfRange(0.18f, 0.55f) * (rng.Randf() < 0.5f ? -1f : 1f); }   // pulled out, swung
					else if (roll < 0.52f) { push = rng.RandfRange(0.1f, 0.24f); turn = rng.RandfRange(-0.15f, 0.15f); }   // left out a little
					else { push = rng.RandfRange(0.0f, 0.06f); turn = rng.RandfRange(-0.05f, 0.05f); }   // tucked in, not quite square
					chairs.Add(new Transform3D(new Basis(Vector3.Up, yaw0 + turn), new Vector3(c.X + x, FloorY, c.Z + sz * (TableW * 0.5f + 0.25f + push))));
				}
			}
		}
		// one knocked over onto its back, behind its place at the second table's aisle side (its back and back legs on
		// the floor, the front legs up)
		{
			var c = TableCentre(1);
			var fallen = new Basis(Vector3.Up, Mathf.Pi + 0.35f) * new Basis(Vector3.Right, Mathf.Pi * 0.5f);
			chairs.Add(new Transform3D(fallen, new Vector3(c.X - 1.1f, FloorY + 0.31f, c.Z - (TableW * 0.5f + 0.95f))));
		}
		// the huddle by the end wall (near the door from the hall, clear of the open window): two against the wall, one
		// turned out from them, one tipped onto its side in front
		{
			float hx = 14.2f, hz = -InnerZ + 0.42f;
			chairs.Add(new Transform3D(new Basis(Vector3.Up, Mathf.Pi + 0.06f), new Vector3(hx, FloorY, hz)));
			chairs.Add(new Transform3D(new Basis(Vector3.Up, Mathf.Pi - 0.1f), new Vector3(hx + 0.58f, FloorY, hz + 0.02f)));
			chairs.Add(new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f + 0.4f), new Vector3(hx + 1.35f, FloorY, hz + 0.25f)));
			// on its side: its right side down (the seat's edge and two legs on the floor)
			var side = new Basis(Vector3.Up, 2.2f) * new Basis(Vector3.Back, Mathf.Pi * 0.5f);
			chairs.Add(new Transform3D(side, new Vector3(hx + 0.5f, FloorY + 0.23f, hz + 0.95f)));
		}
		return chairs;
	}

	/// <summary>(2026-10-15, the scan pass) A dust sheet thrown over a dining chair (standing at <paramref name="xf"/>):
	/// lying on the seat, over the top of the back and down behind it, falling at the sides and the front to a hem a hand or
	/// two above the floor, the legs showing under it. A ring of the chair's outline (its seat, its back), drawn out from the
	/// middle of the seat to the rim and on down to the hem, folded as it falls.</summary>
	private static void ChairDustSheet(MeshKit k, Transform3D xf, int seed)
	{
		// (2026-10-15, the Blender pass) three sheets settled over the chair's model by a cloth simulation; the code's sheet
		// below is the fallback
		if (LodgeProps.Model(k, "lp_dust_sheet_" + (char)('a' + seed % 3), xf, ("linen", LodgeTextures.ChairSheetMat))) return;
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		const int na = 40, nIn = 6, nOut = 7;
		// (the model: the seat's top 0.495 from the front to z 0.15, the back z 0.2 to 0.31 and 0.98 high, 0.46 wide)
		const float seatY = 0.515f, backY = 1.02f, backZ = 0.2f, cz = 0.03f, ax = 0.285f, az = 0.34f;   // (the rim a squircle round the frame: its corners clear the back's)
		float ph = rng.Randf() * Mathf.Tau, hem0 = rng.RandfRange(0.12f, 0.22f), slew = rng.RandfRange(-0.04f, 0.04f);
		// the chair's top under the cloth: the seat, the back rising behind it (the cloth drawn from its top to the seat)
		float Top(float x, float z) => Mathf.Max(seatY - x * x * 0.3f, backY - Mathf.Max(0f, backZ - z) * 3.2f - x * x * 0.7f - Mathf.Max(0f, z - 0.3f) * 1.5f);
		var pts = new Vector3[na, nIn + nOut + 1];
		for (int i = 0; i < na; i++)
		{
			float a = Mathf.Tau * i / na;
			float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
			// a rounded rectangle (a superellipse) a little outside the chair's frame
			var rim = new Vector2(ax * Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 0.5f), cz + az * Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 0.5f));
			float rimY = Top(rim.X, rim.Y);
			for (int j = 0; j <= nIn; j++)
			{
				float s = j / (float)nIn;
				var p = new Vector2(0, cz).Lerp(rim, s);
				pts[i, j] = new Vector3(p.X, Top(p.X, p.Y) + 0.004f * Mathf.Sin(a * 3f + ph) * s, p.Y);
			}
			// the fall: out a little (flaring), down to the hem, in folds that deepen toward the hem
			// (longer behind, where it was thrown over from the front; uneven all round)
			float hem = Mathf.Max(0.06f, hem0 + 0.08f * Mathf.Sin(a * 2f + ph) + 0.04f * Mathf.Sin(a * 5f + ph * 2f) - 0.08f * Mathf.Max(0f, sa));
			var outward = new Vector2(rim.X, rim.Y - cz).Normalized();
			for (int j = 1; j <= nOut; j++)
			{
				float r = j / (float)nOut;
				// the folds: a few big ones and smaller between, deepening toward the hem; the cloth flaring as it falls
				float fold = (0.028f * Mathf.Sin(a * 7f + ph + r * 1.3f) + 0.012f * Mathf.Sin(a * 13f + ph * 3f)) * r;
				var p = rim + outward * (0.08f * r * r + 0.015f * r + fold + 0.012f * Mathf.Sin(r * Mathf.Pi));
				float y = Mathf.Lerp(rimY, hem, r);
				pts[i, nIn + j] = new Vector3(p.X + slew * r, y, p.Y);
			}
		}
		// smooth normals from the grid's neighbours; uv in metres along the ring and down from the middle
		int nr = nIn + nOut + 1;
		var nrm = new Vector3[na, nr];
		var uv = new Vector2[na, nr];
		for (int i = 0; i < na; i++)
		{
			float d = 0f;
			for (int j = 0; j < nr; j++)
			{
				if (j > 0) d += pts[i, j].DistanceTo(pts[i, j - 1]);
				var du = pts[(i + 1) % na, j] - pts[(i + na - 1) % na, j];
				var dv = pts[i, Mathf.Min(j + 1, nr - 1)] - pts[i, Mathf.Max(j - 1, 0)];
				var n = du.Cross(dv);
				var outv = pts[i, j] - new Vector3(0, 0.25f, cz);
				if (n.LengthSquared() < 1e-10f) n = Vector3.Up;
				if (n.Dot(outv) < 0f) n = -n;
				nrm[i, j] = j == 0 ? Vector3.Up : n.Normalized();
				uv[i, j] = new Vector2(i / (float)na * 1.9f, d);   // (metres: the ring's about 1.9 round)
			}
		}
		var was = k.Xf;
		k.Xf = xf;
		k.Mat(LodgeTextures.ChairSheetMat);
		k.Color = Colors.White;
		for (int i = 0; i < na; i++)
		{
			int i1 = (i + 1) % na;
			for (int j = 0; j < nr - 1; j++)
			{
				var u0 = uv[i, j]; var u1 = uv[i1, j]; var u2 = uv[i1, j + 1]; var u3 = uv[i, j + 1];
				if (i1 == 0) { u1.X += 1.9f; u2.X += 1.9f; }
				k.Tri(pts[i, j], pts[i1, j], pts[i1, j + 1], nrm[i, j], nrm[i1, j], nrm[i1, j + 1], u0, u1, u2);
				k.Tri(pts[i, j], pts[i1, j + 1], pts[i, j + 1], nrm[i, j], nrm[i1, j + 1], nrm[i, j + 1], u0, u2, u3);
			}
		}
		k.Xf = was;
	}

	private GpuParticles3D _diningCold;
	public const float TableLen = 7.2f, TableW = 1.3f, TableH = 0.76f;
	/// <summary>The dining tables' centres (floor), in two rows of three.</summary>
	public static Vector3 TableCentre(int t) => new(17f + (t % 3) * 10f, 0, t < 3 ? 3.3f : -3.3f);
}
