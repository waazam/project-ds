using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// The station lobby's shell and everything in it, in five states of decay. It opens kept and homely
/// (papered walls over a pine wainscot, a rug, the rangers' photos, a notice board, the district map, the radio desk,
/// filing cabinets, a cold stove, a ticking clock, a plant, the fire-danger sign at LOW, warm light) and rots a stage
/// every time the player goes off and comes back: damp and a stopped clock (after the basement), peeling paper, rifled
/// files and bloody handprints (after Room 1), rust plate, pipes and chains under a browned-out light (after Room 2),
/// and finally the rot: the ceiling down in places, mould over everything, and flesh grown through the walls (once the
/// iron door is open).
///
/// Every prop carries the stages it belongs to; <see cref="Apply"/> just shows and hides. The stage
/// follows the story's flags (<see cref="StageFor"/>), so Continue restores it; while playing it only
/// ever changes when the player isn't in the lobby to see it happen.
///
/// (2026-10-14h, the station pass: built with the interiors kit. The room's dark, its dirt and its light pool are baked
/// into the vertices (<see cref="RoomShade"/>), its paper, boards and plaster weathered (<see cref="RoomSurface"/>), a
/// beadboard wainscot and skirting swept round it (<see cref="TrimKit"/>); and it is a forester's station now, not a
/// parlour: the radio desk, the map, the files, the stove.)
/// </summary>
public partial class LobbyDecor : Node3D
{
	public int Stage { get; private set; } = -1;
	/// <summary>The desk's lamp (StationInterior builds the desk and hands it over): on until the rot sets in.</summary>
	public OmniLight3D DeskLamp;

	private readonly List<(Node3D node, int from, int to)> _staged = new();
	private OmniLight3D _ceiling;
	private MeshInstance3D _globe;
	private AudioStreamPlayer3D _tick, _drone;
	private Label3D _fireDial, _poster;
	private RoomShade _shade;

	private const float W = StationInterior.HalfWidth, D = StationInterior.HalfDepth, H = StationInterior.Height;
	/// <summary>The walls' inner faces (the slabs are 14 cm thick on the lines ±W, ±D).</summary>
	private const float IX = W - 0.07f, IZ = D - 0.07f;
	/// <summary>Where the dressing stands (the ranger station's furniture).</summary>
	private static readonly Vector3 RadioAt = new(-0.15f, 0f, -IZ + 0.33f), StoveAt = new(IX - 0.45f, 0f, 2.55f), MapAt = new(-IX, 1.75f, 3.15f);
	private static readonly Vector3 LampAt = new(0f, H - 0.85f, 0f);

	public static int StageFor(StoryManager s)
	{
		if (s == null) return 0;
		if (s.HasFlag(StoryManager.Flag.StationDoor3Open)) return 4;
		if (s.HasFlag(StoryManager.Flag.StationRoom2Solved)) return 3;
		if (s.HasFlag(StoryManager.Flag.StationRoom1Solved)) return 2;
		if (s.HasFlag(StoryManager.Flag.StationClockBroken)) return 1;
		return 0;
	}

	/// <summary>Moves to the story's stage, if the player isn't watching (or at load).</summary>
	public void Advance(StoryManager s, bool unseen)
	{
		int want = StageFor(s);
		if (want != Stage && (unseen || Stage < 0)) Apply(want);
	}

	private Node3D Group(string name, int from, int to)
	{
		var n = new Node3D { Name = name };
		AddChild(n);
		_staged.Add((n, from, to));
		return n;
	}

	public void Apply(int stage)
	{
		Stage = stage;
		foreach (var (node, from, to) in _staged) node.Visible = stage >= from && stage <= to;
		// (2026-10-14h: the light browns out as the place goes, not to a red club light: red was the loudest thing in the
		// late lobby, and the rot reads better in a failing, dirty amber)
		(Color c, float e)[] light =
		{
			(new Color(1f, 0.8f, 0.6f), 2.6f),
			(new Color(1f, 0.74f, 0.54f), 2.0f),
			(new Color(1f, 0.64f, 0.46f), 1.6f),
			(new Color(1f, 0.5f, 0.34f), 1.3f),
			(new Color(0.92f, 0.44f, 0.3f), 1.15f),
		};
		var (col, energy) = light[Mathf.Clamp(stage, 0, 4)];
		if (_ceiling != null) { _ceiling.LightColor = col; _ceiling.LightEnergy = energy; }
		if (_globe?.MaterialOverride is StandardMaterial3D gm) { gm.Emission = col; gm.EmissionEnergyMultiplier = 0.45f + 0.15f * energy; }
		if (DeskLamp != null) DeskLamp.Visible = stage <= 2;
		if (_tick != null) { if (stage == 0) { if (!_tick.Playing) _tick.Play(); } else _tick.Stop(); }
		if (_drone != null) { if (stage >= 3) { if (!_drone.Playing) _drone.Play(); } else _drone.Stop(); }
		if (_fireDial != null) _fireDial.Text = stage >= 3 ? "EXTREME" : stage >= 2 ? "HIGH" : "LOW";
		if (_poster != null) _poster.Text = stage >= 2 ? "DO NOT\nLOOK AT\nTHEM" : "MISSING\n\nHAVE YOU\nSEEN\nTHIS MAN?";
		GD.Print($"[story] Act 13: the lobby is at decay stage {stage}");
	}

	// ------------------------------------------------------------------ building

	public void Build(StationInterior st)
	{
		var rng = new RandomNumberGenerator { Seed = 1313 };
		BuildShade();
		BuildShell();
		BuildKept(rng);
		BuildRanger(rng);
		BuildDamp(rng);
		BuildBlood(rng);
		BuildIndustry(rng);
		BuildRot(rng);
		BuildLight();
		Apply(StageFor(StoryManager.Instance));
	}

	/// <summary>The room's bake: its seams and corners dark, the light falling off from the lamp's pool, dark under the
	/// furniture, soot over the stove, damp by the basement door and in the back corner, the boards worn from the door to
	/// the desk and on to each room.</summary>
	private void BuildShade()
	{
		var desk = StationInterior.DeskAt;
		_shade = new RoomShade(-IX, IX, -IZ, IZ, 0f, H) { Dim = 0.74f, Reach = 1.4f };
		_shade.Light(LampAt + Vector3.Down * 0.3f, 5.4f).Light(desk + new Vector3(0.55f, 1f, 0.1f), 2.2f, 0.7f)
			.Under(desk, new Vector3(1.9f, 0.82f, 0.8f), 0.5f, 0.5f)
			.Under(new Vector3(-3.4f, 0, D - 0.4f), new Vector3(1.6f, 0.46f, 0.42f), 0.4f, 0.4f)
			.Under(RadioAt, new Vector3(1.2f, 0.78f, 0.6f), 0.45f, 0.45f)
			.Under(new Vector3(-4.33f, 0, -IZ + 0.37f), new Vector3(1.05f, 1.32f, 0.6f), 0.5f, 0.45f)
			.Under(StoveAt, new Vector3(0.6f, 0.95f, 0.6f), 0.45f, 0.45f)
			.Under(new Vector3(-IX, 1.15f, -2.6f), new Vector3(0.1f, 0.95f, 1.36f), 0.3f, 0.3f)
			.Under(MapAt + Vector3.Down * 0.42f, new Vector3(0.1f, 0.84f, 1.14f), 0.3f, 0.3f)
			.Under(new Vector3(-IX + 0.35f, 0, IZ - 0.24f), new Vector3(0.7f, 0.6f, 0.36f), 0.4f, 0.35f)
			.Soot(StoveAt + Vector3.Up * 0.95f, 0.6f, 0.45f).Soot(new Vector3(IX - 0.05f, 2.35f, StoveAt.Z), 0.5f, 0.55f)
			.Soot(LampAt + Vector3.Up * 0.3f, 0.7f, 0.3f)
			.Damp(new Vector3(StationInterior.BasementGapX + 1.4f, 0.6f, -IZ), 1.4f, 0.8f).Damp(new Vector3(-IX, 2.9f, IZ), 1.6f, 0.6f)
			.Damp(new Vector3(IX, 0.5f, -IZ), 1.2f, 0.7f)
			.Path(new Vector3(StationInterior.EntryGapX, 0, -IZ), desk + new Vector3(0, 0, -0.9f), 1.0f, 0.75f)
			.Path(new Vector3(StationInterior.EntryGapX, 0, -IZ + 0.6f), new Vector3(StationInterior.BasementGapX, 0, -IZ), 0.8f, 0.5f)
			.Path(desk + new Vector3(0, 0, -0.9f), new Vector3(IX, 0, 0), 0.8f, 0.55f)
			.Path(desk + new Vector3(0, 0, -0.9f), new Vector3(-IX, 0, 0), 0.8f, 0.55f)
			.Path(desk + new Vector3(1.2f, 0, 0.4f), new Vector3(0, 0, IZ), 0.7f, 0.5f);
	}

	/// <summary>Bakes the room into a kit and commits it.</summary>
	private MeshInstance3D Commit(MeshKit k, Node parent, string name, bool shadows = true)
	{
		_shade.Apply(k);
		return k.CommitTo(parent, name, shadows);
	}

	/// <summary>The walls, floor and ceiling. The walls come in two skins (kept paper over wainscot;
	/// stained paper) that swap by stage; collision is built once.</summary>
	private void BuildShell()
	{
		var body = new StaticBody3D { Name = "LobbyShell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		void Walls(MeshKit k, StaticBody3D b)
		{
			// (the lobby has its own wainscot: the crown only)
			StationKit.WallAlongX(k, b, -D, -W, 0.2f, H, 0, (StationInterior.EntryGapX, 2.2f), trimSide: 1, panels: false);
			StationKit.WallAlongX(k, b, -D, 0.2f, W, H, 0, (StationInterior.BasementGapX, 2.2f), trimSide: 1, panels: false);
			StationKit.WallAlongX(k, b, D, -W, W, H, 0, (0f, 2.4f), 2.5f, trimSide: -1, panels: false);
			StationKit.WallAlongZ(k, b, W, -D, D, H, 0, (0f, 2.2f), trimSide: -1, panels: false);
			StationKit.WallAlongZ(k, b, -W, -D, D, H, 0, (0f, 2.2f), trimSide: 1, panels: false);
		}
		// (2026-10-14h) drawn finely enough for the bake to sit on, the paper weathered: streaked from the cornice, a tide
		// line at the foot; the stained skin more so
		var clean = new MeshKit { Tess = 0.6f };
		clean.Mat(RoomSurface.Of(StationTextures.WallpaperMat, RoomSurface.Paper with { Streaks = 0.45f, Tide = 0.4f, Mottle = 0.3f }, 131));
		clean.Color = Colors.White;
		Walls(clean, body);
		Commit(clean, Group("WallsKept", 0, 1), "Walls");
		var stained = new MeshKit { Tess = 0.6f };
		stained.Mat(RoomSurface.Of(StationTextures.WallpaperStainedMat, RoomSurface.Paper with { Streaks = 0.95f, Tide = 0.85f, Grime = 1.3f, StreakLen = 2.2f }, 132));
		stained.Color = Colors.White;
		Walls(stained, null);
		Commit(stained, Group("WallsStained", 2, 4), "Walls");

		// a beadboard wainscot of knotty pine round the lower walls, its chair rail and a skirting (kept to the blood
		// stage: the industry's plate goes up over it). The doorways' casings stand over its ends.
		var wain = new MeshKit();
		wain.Mat(RoomSurface.Of(StationTextures.PineMat, RoomSurface.Wood with { Streaks = 0.2f, Tide = 0.35f }, 133));
		wain.Color = Colors.White;
		var corners = new[] { new Vector3(-IX, 0, -IZ), new Vector3(IX, 0, -IZ), new Vector3(IX, 0, IZ), new Vector3(-IX, 0, IZ) };
		var doors = new (Vector3, float)[]
		{
			(new Vector3(StationInterior.EntryGapX, 0, -IZ), 2.32f), (new Vector3(StationInterior.BasementGapX, 0, -IZ), 2.32f),
			(new Vector3(0, 0, IZ), 2.4f), (new Vector3(IX, 0, 0), 2.32f), (new Vector3(-IX, 0, 0), 2.32f),
		};
		float face = 0.014f;
		foreach (var (run, closed) in TrimKit.Perimeter(corners, doors)) face = TrimKit.Wainscot(wain, run, 1.05f, true, closed, Vector3.Zero, 0.1f);
		wain.Mat(RoomSurface.Of(StationTextures.CasingMat, RoomSurface.Wood, 133));
		wain.Color = Colors.White * 0.85f;
		foreach (var (run, closed) in TrimKit.Perimeter(corners, doors)) TrimKit.Loop(wain, run, TrimKit.SkirtingTall.Out(face), closed, null, Vector3.Zero);
		wain.Color = Colors.White;
		Commit(wain, Group("Wainscot", 0, 2), "Wainscot", false);   // (the lining: it never stands between the lamp and anything)

		var floorK = new MeshKit { Tess = 0.6f };
		var ceilK = new MeshKit { Tess = 0.6f };
		// (2026-10-14b: the floor, the ceiling and the walls' boards were one wood; a plank floor and a plaster ceiling now)
		floorK.Mat(RoomSurface.Of(StationTextures.PlankFloorMat, RoomSurface.Wood with { FloorDirt = 0.6f }, 134));
		floorK.Color = Colors.White;
		ceilK.Mat(RoomSurface.Of(StationTextures.CeilingMat, RoomSurface.Plaster with { CeilingStains = 0.85f }, 135));
		ceilK.Color = Colors.White;
		StationKit.FloorAndCeiling(floorK, ceilK, body, W, D, H, 0);
		Commit(floorK, this, "Floor", false);
		Commit(ceilK, this, "Ceiling", false);
	}

	private static StandardMaterial3D RugMat(string key, Color c)
	{
		var m = StationTextures.Flat(key, c, 1f, 0.05f);
		if (!m.HasMeta("relief"))
		{
			m.Uv1Scale = Vector3.One * 2f;
			World.SurfaceKit.Relief(m, "carpet", 0.8f);
			m.SetMeta("relief", "carpet");
			World.DetailKit.Apply(m, World.DetailKit.Kind.Fabric);
		}
		return m;
	}

	/// <summary>Stage 0 (and some of it lingering): the kept station.</summary>
	private void BuildKept(RandomNumberGenerator rng)
	{
		// the rug (gone by the industrial stage)
		var rug = Group("Rug", 0, 2);
		var k = new MeshKit();
		// (2026-10-13g, the texture-mood pass: a flat, bright red and gold; now a trodden, faded wool with the carpet's weave)
		// (2026-10-14h: still the loudest red in the kept lobby; a worn brown-red, its border a dull olive)
		k.Mat(RugMat("st_rug2", new Color(0.25f, 0.13f, 0.1f)));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, 0.005f, 0), new Vector3(4.2f, 0.01f, 3f));
		k.Mat(RugMat("st_rug_border2", new Color(0.29f, 0.26f, 0.18f)));
		BuildKit.Box(k, new Vector3(0, 0.007f, 0), new Vector3(3.6f, 0.01f, 2.4f));
		k.Mat(RugMat("st_rug2", new Color(0.25f, 0.13f, 0.1f)));
		BuildKit.Box(k, new Vector3(0, 0.009f, 0), new Vector3(3.4f, 0.01f, 2.2f));
		Commit(k, rug, "Rug", false);

		// the rangers on the wall: four framed photographs on the back wall either side of the web
		var photos = Group("Photos", 0, 2);
		for (int i = 0; i < 4; i++)
		{
			if (i == 1) continue;   // this one comes down in the damp (see BuildDamp)
			Frame(photos, new Vector3(-4.3f + i * 0.8f + (i > 1 ? 4.5f : 0f), 1.75f, D - 0.09f), Vector3.Forward, rng);
		}
		var photoUp = Group("PhotoOnWall", 0, 0);
		Frame(photoUp, new Vector3(-3.5f, 1.75f, D - 0.09f), Vector3.Forward, rng);

		// the notice board (right wall, by Room 2's door) with its notices; (2026-10-14h) paper a step under the lantern's
		// pool, a few more sheets pinned over each other, none of them square
		var board = Group("NoticeBoard", 0, 2);
		var bk = new MeshKit();
		bk.Mat(StationTextures.Flat("st_cork2", new Color(0.36f, 0.26f, 0.17f), 0.95f, 0.05f));
		bk.Color = Colors.White;
		Vector3 bc = new(-W + 0.09f, 1.6f, -2.6f);
		BuildKit.Box(bk, bc, new Vector3(0.04f, 0.9f, 1.3f));
		bk.Mat(StationTextures.CasingMat);
		bk.Color = Colors.White * 0.8f;
		foreach (float y in new[] { 0.47f, -0.47f }) bk.BevelBox(bc + new Vector3(0.005f, y, 0), new Vector3(0.05f, 0.05f, 1.36f), 0.01f);
		foreach (float z in new[] { 0.655f, -0.655f }) bk.BevelBox(bc + new Vector3(0.005f, 0, z), new Vector3(0.05f, 0.9f, 0.05f), 0.01f);
		bk.Mat(StationTextures.NoticeMat);
		bk.Color = Colors.White;
		void Sheet(Vector3 at, float w, float h, float tilt, float tone)
		{
			bk.Color = new Color(tone, tone * 0.97f, tone * 0.9f);
			BuildKit.Box(bk, bc + at, new Vector3(0.004f, h, w), 4f, BuildKit.Face.None, new Basis(Vector3.Right, tilt));
		}
		Sheet(new Vector3(0.025f, 0.05f, -0.32f), 0.42f, 0.6f, 0.02f, 1f);
		Sheet(new Vector3(0.025f, 0.08f, 0.3f), 0.4f, 0.5f, -0.03f, 0.95f);
		Sheet(new Vector3(0.022f, 0.26f, 0.0f), 0.24f, 0.3f, 0.08f, 0.8f);
		Sheet(new Vector3(0.022f, -0.3f, 0.12f), 0.3f, 0.21f, -0.06f, 0.85f);
		Sheet(new Vector3(0.022f, -0.28f, -0.5f), 0.2f, 0.26f, 0.1f, 0.75f);
		bk.Mat(StationTextures.Flat("st_pin", new Color(0.35f, 0.08f, 0.06f), 0.5f, 0.4f));
		bk.Color = Colors.White;
		foreach (var p in new[] { new Vector3(0.03f, 0.33f, -0.32f), new Vector3(0.03f, 0.31f, 0.3f), new Vector3(0.03f, 0.39f, 0f), new Vector3(0.03f, -0.21f, 0.12f), new Vector3(0.03f, -0.17f, -0.5f) })
			bk.Cylinder(bc + p, bc + p + Vector3.Right * 0.012f, 0.008f, 0.008f, 5, true);
		Commit(bk, board, "Board", false);
		var tb = new Basis(Vector3.Up, Mathf.Pi * 0.5f);   // facing +X, into the lobby
		SignKit.Text(board, "LAKE SAFETY\n\nNEVER ROW\nALONE", bc + new Vector3(0.03f, 0.16f, -0.32f), tb, 0.055f, new Color(0.13f, 0.16f, 0.25f), shadow: false);
		_poster = SignKit.Text(board, "", bc + new Vector3(0.03f, 0.14f, 0.3f), tb, 0.05f, new Color(0.18f, 0.07f, 0.05f), shadow: false);

		// the fire danger sign: an arrow on a painted dial, beside the entry; (2026-10-14h) old enamelled board, darker
		var sign = Group("FireSign", 0, 4);
		var sk = new MeshKit();
		sk.Mat(StationTextures.Flat("st_firesign2", new Color(0.44f, 0.36f, 0.2f), 0.75f, 0.15f));
		sk.Color = Colors.White;
		Vector3 fc = new(-4.0f, 1.9f, -D + 0.1f);
		sk.BevelBox(fc, new Vector3(1.1f, 0.6f, 0.04f), 0.012f);
		Commit(sk, sign, "Sign", false);
		var fb = Basis.Identity;
		SignKit.Text(sign, "FIRE DANGER TODAY", fc + new Vector3(0, 0.16f, 0.025f), fb, 0.075f, new Color(0.17f, 0.1f, 0.05f), shadow: false);
		_fireDial = SignKit.Text(sign, "LOW", fc + new Vector3(0, -0.08f, 0.025f), fb, 0.16f, new Color(0.45f, 0.07f, 0.04f), shadow: false);

		// a bench under the photos, a chair by the desk, a coat stand with a ranger's jacket, a plant
		var bench = Group("Bench", 0, 2);
		var ck = new MeshKit();
		ck.Mat(StationTextures.CasingMat);
		ck.Color = new Color(0.95f, 0.88f, 0.8f);
		// (2026-10-14h: slatted, eased, on stout legs with a stretcher; it was three slabs)
		for (int i = 0; i < 3; i++)
			ck.BevelBox(new Vector3(-3.4f, 0.44f, D - 0.54f + i * 0.14f), new Vector3(1.6f, 0.035f, 0.12f), 0.008f, 1.2f);
		foreach (int s in new[] { -1, 1 })
			foreach (float dz in new[] { -0.15f, 0.15f })
				ck.ChamferBox(new Vector3(-3.4f + s * 0.68f, 0.21f, D - 0.4f + dz), new Vector3(0.06f, 0.42f, 0.06f), 0.008f);
		ck.BevelBox(new Vector3(-3.4f, 0.12f, D - 0.4f), new Vector3(1.36f, 0.05f, 0.04f), 0.008f);
		ck.Color = Colors.White;
		MeshKit.Solidify(Commit(ck, bench, "Bench", true), null, "wood", true);

		var chairUp = Group("ChairUp", 0, 1);
		Chair(chairUp, new Vector3(0.9f, 0, 3.7f), 0.2f, false);
		MeshKit.SolidifyAll(chairUp, new Vector3(0.85f, 0.9f, 0.85f), "wood", true);
		Chair(Group("ChairDown", 2, 4), new Vector3(1.6f, 0, 2.0f), 1.1f, true);

		var coat = Group("CoatStand", 0, 3);
		var ct = new MeshKit();
		if (World.FurnitureKit.Add(ct, "coat_stand", new Vector3(4.2f, 0, -3.8f), 2.4f, Roles))
		{
			MeshKit.Solidify(ct.CommitTo(coat, "CoatStand", true), new Vector3(0.35f, 1f, 0.35f), "wood", true);
			goto plants;
		}
		ct.Mat(PropTextures.PostMat);
		ct.Color = new Color(0.36f, 0.26f, 0.18f);
		Vector3 cs = new(4.2f, 0, -3.8f);
		ct.Cylinder(cs, cs + Vector3.Up * 1.8f, 0.025f, 0.02f, 6, true);
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3;
			ct.Cylinder(cs + Vector3.Up * 0.02f, cs + new Vector3(Mathf.Cos(a) * 0.3f, 0.02f, Mathf.Sin(a) * 0.3f), 0.02f, 0.02f, 5, true);
		}
		ct.Mat(StationTextures.Flat("st_jacket", new Color(0.24f, 0.27f, 0.18f), 0.9f, 0.1f));
		ct.Color = Colors.White;
		ct.Blob(cs + new Vector3(0.05f, 1.35f, 0.05f), new Vector3(0.22f, 0.4f, 0.12f), 5, 0.12f, false);
		MeshKit.Solidify(ct.CommitTo(coat, "CoatStand", true), new Vector3(0.35f, 1f, 0.35f), "wood", true);

		plants:
		var plant = Group("Plant", 0, 0);
		var plantDead = Group("PlantDead", 1, 2);
		PlantPot(plant, new Vector3(4.3f, 0, 3.9f), false);
		PlantPot(plantDead, new Vector3(4.3f, 0, 3.9f), true);
		MeshKit.SolidifyAll(plant, new Vector3(0.6f, 0.9f, 0.6f), "stone", true);
		MeshKit.SolidifyAll(plantDead, new Vector3(0.6f, 0.9f, 0.6f), "stone", true);

		// the wall clock over Room 1's door (ticks while all is well; stops in the damp)
		var clock = Group("WallClock", 0, 2);
		var wk = new MeshKit();
		wk.Mat(StationTextures.CasingMat);
		wk.Color = Colors.White;
		Vector3 cc = new(W - 0.08f, 2.75f, 0f);
		wk.Cylinder(cc, cc + Vector3.Left * 0.05f, 0.24f, 0.24f, 20, true);
		wk.Mat(StationTextures.Flat("st_clockface2", new Color(0.56f, 0.53f, 0.45f), 0.7f, 0.1f));
		wk.Color = Colors.White;
		wk.Cylinder(cc + Vector3.Left * 0.05f, cc + Vector3.Left * 0.055f, 0.2f, 0.2f, 20, true);
		wk.Mat(StationTextures.Flat("st_hands", new Color(0.05f, 0.05f, 0.05f), 0.6f, 0.2f));
		wk.Box(cc + new Vector3(-0.06f, 0.06f, 0), new Vector3(0.01f, 0.13f, 0.012f));
		wk.Box(cc + new Vector3(-0.06f, 0, 0.05f), new Vector3(0.01f, 0.012f, 0.1f));
		for (int i = 0; i < 12; i++)
		{
			float a = Mathf.Tau * i / 12f;
			wk.Box(cc + new Vector3(-0.057f, Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f), new Vector3(0.004f, i % 3 == 0 ? 0.03f : 0.015f, 0.008f), 1f, new Basis(Vector3.Right, -a));
		}
		Commit(wk, clock, "Clock", false);
		_tick = Loop("res://assets/audio/sfx/clock_tick_loop.wav", cc, "Events", -14f, 3f);
		_drone = Loop("res://assets/audio/ambient/industrial_drone_loop.wav", new Vector3(0, 2.4f, 0), "Unnatural", -10f, 8f);
	}

	private void Frame(Node3D parent, Vector3 at, Vector3 n, RandomNumberGenerator rng)
	{
		var k = new MeshKit();
		k.Mat(StationTextures.CasingMat);
		k.Color = new Color(0.6f, 0.5f, 0.42f);
		var b = Basis.LookingAt(-n, Vector3.Up) * new Basis(Vector3.Back, rng.RandfRange(-0.04f, 0.04f));
		k.Xf = new Transform3D(b, at);
		k.BevelBox(Vector3.Zero, new Vector3(0.46f, 0.36f, 0.03f), 0.008f);
		// (2026-10-14h: a sepia print under old glass, a step under the paper's value)
		k.Mat(StationTextures.Flat("st_photo2", new Color(0.36f, 0.31f, 0.24f), 0.35f, 0.45f));
		k.Color = new Color(rng.RandfRange(0.8f, 1f), rng.RandfRange(0.75f, 0.9f), 0.7f);
		BuildKit.Box(k, new Vector3(0, 0, 0.017f), new Vector3(0.36f, 0.26f, 0.004f));
		// a figure or two: dark smudges standing in a row, a treeline behind them
		k.Mat(StationTextures.Flat("st_photofig", new Color(0.18f, 0.15f, 0.12f), 0.5f, 0.3f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, 0.07f, 0.0195f), new Vector3(0.34f, 0.07f, 0.001f));
		int figs = rng.RandiRange(1, 3);
		for (int i = 0; i < figs; i++)
			BuildKit.Box(k, new Vector3(-0.08f * (figs - 1) + i * 0.16f, -0.02f, 0.02f), new Vector3(0.05f, 0.16f, 0.002f));
		k.Xf = Transform3D.Identity;
		Commit(k, parent, "Frame", false);
	}

	/// <summary>The station's materials for the modelled pieces' roles.</summary>
	internal static System.Collections.Generic.Dictionary<string, Material> Roles => new()
	{
		["wood"] = PropTextures.DeckMat, ["upholstery"] = StationTextures.Flat("st_seat", new Color(0.3f, 0.16f, 0.1f), 0.6f, 0.3f),
		["leather"] = StationTextures.Flat("st_leather", new Color(0.18f, 0.24f, 0.18f), 0.7f, 0.2f), ["brass"] = ItemTextures.BrassMat,
		["cloth"] = StationTextures.Flat("st_jacket", new Color(0.24f, 0.27f, 0.18f), 0.9f, 0.1f), ["felt"] = StationTextures.Flat("st_hat", new Color(0.36f, 0.3f, 0.2f), 0.9f, 0.1f),
		["paper"] = StationTextures.Flat("st_paper", new Color(0.5f, 0.47f, 0.39f), 0.95f, 0.05f), ["ceramic"] = StationTextures.Flat("st_mug", new Color(0.5f, 0.52f, 0.5f), 0.4f, 0.3f),
		["glass"] = StationTextures.Flat("st_glass", new Color(0.2f, 0.22f, 0.2f), 0.1f, 0.7f), ["steel"] = StationTextures.DrabSteelMat,
		["plastic"] = StationTextures.Flat("st_vinyl", new Color(0.16f, 0.14f, 0.12f), 0.6f, 0.3f), ["rubber"] = StationTextures.Flat("st_rubber", new Color(0.05f, 0.05f, 0.05f), 0.9f, 0.1f),
		["metal"] = ItemTextures.BrassMat, ["iron"] = StationTextures.CastIronMat, ["bark"] = StationTextures.Flat("st_bark", new Color(0.2f, 0.16f, 0.12f), 0.95f, 0.05f),
	};

	private void Chair(Node3D parent, Vector3 at, float yaw, bool toppled)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.4f, 0.28f, 0.18f);
		var b = new Basis(Vector3.Up, yaw) * (toppled ? new Basis(Vector3.Right, -1.45f) : Basis.Identity);
		// the modelled chair (turned legs, a carved crest rail, a piped seat; the fidelity pass, 2026-10-01)
		if (World.FurnitureKit.Add(k, "dining_chair", new Transform3D(b, at + (toppled ? Vector3.Up * 0.24f : Vector3.Zero)), Roles))
		{
			k.CommitTo(parent, "Chair", true);
			return;
		}
		k.Xf = new Transform3D(b, at + (toppled ? Vector3.Up * 0.24f : Vector3.Zero));
		BuildKit.Box(k, new Vector3(0, 0.46f, 0), new Vector3(0.44f, 0.04f, 0.42f), 1.2f);
		BuildKit.Box(k, new Vector3(0, 0.82f, 0.19f), new Vector3(0.44f, 0.5f, 0.04f), 1.2f);
		foreach (var (x, z) in new[] { (-0.19f, -0.18f), (0.19f, -0.18f), (-0.19f, 0.18f), (0.19f, 0.18f) })
			BuildKit.Box(k, new Vector3(x, 0.22f, z), new Vector3(0.04f, 0.44f, 0.04f), 1.2f);
		k.Xf = Transform3D.Identity;
		Commit(k, parent, "Chair", true);
	}

	private void PlantPot(Node3D parent, Vector3 at, bool dead)
	{
		var k = new MeshKit();
		k.Mat(StationTextures.Flat("st_pot2", new Color(0.4f, 0.24f, 0.17f), 0.85f, 0.15f));
		k.Color = Colors.White;
		k.Cylinder(at, at + Vector3.Up * 0.4f, 0.16f, 0.2f, 12, true);
		k.Cylinder(at + Vector3.Up * 0.37f, at + Vector3.Up * 0.43f, 0.215f, 0.215f, 12, true);
		k.Mat(StationTextures.Flat(dead ? "st_leaf_dead" : "st_leaf2", dead ? new Color(0.3f, 0.23f, 0.12f) : new Color(0.15f, 0.25f, 0.11f), 0.7f, 0.2f));
		var rng = new RandomNumberGenerator { Seed = 77 };
		for (int i = 0; i < 9; i++)
		{
			float a = Mathf.Tau * i / 9f;
			float lift = dead ? rng.RandfRange(-0.25f, 0.05f) : rng.RandfRange(0.35f, 0.7f);
			Vector3 tip = at + new Vector3(Mathf.Cos(a) * 0.35f, 0.4f + lift, Mathf.Sin(a) * 0.35f);
			k.Cylinder(at + Vector3.Up * 0.38f, tip, 0.012f, 0.006f, 4, false);
			k.Blob(tip, new Vector3(0.09f, 0.02f, 0.05f), i, 0.2f, false);
		}
		Commit(k, parent, "Plant", true);
	}

	// ------------------------------------------------------------------ the ranger station (2026-10-14h)

	/// <summary>What makes it a forester's station: the radio desk between the doors on the front wall (the set, its
	/// microphone, the log, a frequency card and a calendar pinned over it, the swivel chair), two steel filing cabinets
	/// in the corner by the way in (rifled once Room 1 is done), the district's survey map on the wall by Room 2, a cold
	/// pot-bellied stove on its zinc mat with its flue up the wall and kindling by it, cartons of pamphlets in the back
	/// corner. All against the walls, nothing in a doorway's way or on the paths between them.</summary>
	private void BuildRanger(RandomNumberGenerator rng)
	{
		var roles = Roles;
		var body = new StaticBody3D { Name = "RangerBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		var k = new MeshKit();
		var small = new MeshKit();

		// ---- the radio desk: a plain oak table, the set on it
		var oak = StationTextures.CasingMat;
		Vector3 t = RadioAt;
		k.Mat(oak);
		k.Color = new Color(0.95f, 0.88f, 0.8f);
		k.BevelBox(t + new Vector3(0, 0.755f, 0), new Vector3(1.2f, 0.035f, 0.6f), 0.01f);
		k.BevelBox(t + new Vector3(0, 0.69f, 0.0f), new Vector3(1.08f, 0.09f, 0.5f), 0.006f);
		foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
				k.ChamferBox(t + new Vector3(sx * 0.54f, 0.37f, sz * 0.24f), new Vector3(0.05f, 0.74f, 0.05f), 0.008f);
		k.BevelBox(t + new Vector3(0, 0.14f, 0.24f), new Vector3(1.04f, 0.04f, 0.025f), 0.005f);
		body.AddChild(new CollisionShape3D { Position = t + new Vector3(0, 0.39f, 0), Shape = new BoxShape3D { Size = new Vector3(1.2f, 0.78f, 0.6f) } });
		// the set: an olive steel cabinet, a black panel with its dials, a meter, a speaker grille
		var drab = StationTextures.DrabSteelMat;
		Vector3 r = t + new Vector3(-0.22f, 0.775f, -0.08f);
		k.Mat(drab);
		k.Color = Colors.White;
		k.BevelBox(r + new Vector3(0, 0.17f, 0), new Vector3(0.56f, 0.34f, 0.32f), 0.012f);
		k.BevelBox(r + new Vector3(0.38f, 0.11f, -0.02f), new Vector3(0.18f, 0.22f, 0.26f), 0.01f);   // the power supply beside it
		var panel = StationTextures.Flat("st_radiopanel", new Color(0.06f, 0.06f, 0.055f), 0.55f, 0.35f);
		k.Mat(panel);
		k.Box(r + new Vector3(0, 0.17f, 0.162f), new Vector3(0.5f, 0.28f, 0.006f));
		var knob = StationTextures.Flat("st_knob", new Color(0.14f, 0.12f, 0.1f), 0.4f, 0.4f);
		k.Mat(knob);
		foreach (var (x, y, rad) in new[] { (-0.17f, 0.09f, 0.03f), (-0.05f, 0.09f, 0.025f), (0.06f, 0.09f, 0.025f), (0.17f, 0.09f, 0.03f), (0.17f, 0.24f, 0.02f) })
			k.Cylinder(r + new Vector3(x, y, 0.164f), r + new Vector3(x, y, 0.19f), rad, rad * 0.9f, 10, true);
		k.Mat(StationTextures.Flat("st_dial", new Color(0.42f, 0.38f, 0.28f), 0.3f, 0.5f));   // the meter and the tuning scale (lit by nothing now)
		k.Box(r + new Vector3(-0.1f, 0.23f, 0.166f), new Vector3(0.22f, 0.07f, 0.004f));
		k.Box(r + new Vector3(0.07f, 0.235f, 0.166f), new Vector3(0.09f, 0.06f, 0.004f));
		k.Mat(StationTextures.Flat("st_needle", new Color(0.1f, 0.03f, 0.02f), 0.6f, 0.2f));
		k.Box(r + new Vector3(0.07f, 0.235f, 0.169f), new Vector3(0.004f, 0.05f, 0.002f), 1f, new Basis(Vector3.Back, 0.5f));
		// the microphone on its stand, its cord across the table; the headphones
		k.Mat(StationTextures.CastIronMat);
		k.Color = Colors.White;
		Vector3 mic = t + new Vector3(0.28f, 0.775f, 0.08f);
		k.Cylinder(mic, mic + Vector3.Up * 0.015f, 0.06f, 0.055f, 12, true);
		k.Cylinder(mic, mic + Vector3.Up * 0.22f, 0.008f, 0.008f, 6, false);
		k.Cylinder(mic + new Vector3(0, 0.22f, -0.03f), mic + new Vector3(0, 0.24f, 0.05f), 0.032f, 0.03f, 10, true);
		k.Mat(StationTextures.Flat("st_rubber", new Color(0.05f, 0.05f, 0.05f), 0.9f, 0.1f));
		Vector3 prev = mic + Vector3.Up * 0.01f;
		for (int i = 1; i <= 6; i++)
		{
			Vector3 p = mic.Lerp(r + new Vector3(0.2f, 0.01f, 0.17f), i / 6f) + new Vector3(0, 0.006f, 0.04f * Mathf.Sin(i * 1.3f));
			k.Cylinder(prev, p, 0.005f, 0.005f, 4, false);
			prev = p;
		}
		k.Cylinder(r + new Vector3(-0.26f, 0.32f, -0.15f), r + new Vector3(-0.26f, 2.0f - 0.775f, -0.16f), 0.005f, 0.005f, 4, false);   // the aerial lead up the wall
		World.FurnitureKit.Add(k, "papers", t + new Vector3(0.32f, 0.775f, -0.12f), 0.3f, roles);
		World.FurnitureKit.Add(k, "mug", t + new Vector3(0.48f, 0.775f, 0.15f), 1.9f, roles);
		// the frequency card, the call log and a calendar pinned over it (under the lantern's pool)
		small.Mat(StationTextures.NoticeMat);
		foreach (var (x, y, w, h, tilt, tone) in new[] { (-0.55f, 1.42f, 0.21f, 0.28f, 0.03f, 0.9f), (-0.22f, 1.5f, 0.3f, 0.4f, -0.02f, 1f), (0.12f, 1.38f, 0.16f, 0.12f, 0.06f, 0.8f) })
		{
			small.Color = new Color(tone, tone * 0.97f, tone * 0.9f);
			BuildKit.Box(small, new Vector3(t.X + x, y, -IZ + 0.004f), new Vector3(w, h, 0.004f), 4f, BuildKit.Face.None, new Basis(Vector3.Back, tilt));
		}
		small.Mat(StationTextures.Flat("st_ink", new Color(0.12f, 0.1f, 0.09f), 0.8f, 0.1f));
		small.Color = Colors.White;
		for (int i = 0; i < 6; i++)   // the calendar's grid, ruled
			BuildKit.Box(small, new Vector3(t.X - 0.22f, 1.4f + i * 0.045f, -IZ + 0.0075f), new Vector3(0.26f, 0.003f, 0.001f));
		for (int i = 0; i < 8; i++)
			BuildKit.Box(small, new Vector3(t.X - 0.34f + i * 0.034f, 1.47f, -IZ + 0.0075f), new Vector3(0.003f, 0.22f, 0.001f));
		SignKit.Text(this, "FREQ 172.275\nCALL KFS-4", new Vector3(t.X - 0.55f, 1.46f, -IZ + 0.01f), Basis.Identity, 0.025f, new Color(0.12f, 0.1f, 0.09f), shadow: false);
		// the swivel chair, pushed back from it (kept), then knocked over
		var chairKept = Group("RadioChair", 0, 2);
		var chairDown = Group("RadioChairDown", 3, 4);
		var ck = new MeshKit();
		if (World.FurnitureKit.Add(ck, "office_chair", t + new Vector3(0.1f, 0, 0.78f), 0.35f, roles)) MeshKit.Solidify(ck.CommitTo(chairKept, "Chair", true), new Vector3(0.7f, 1f, 0.7f), "wood", true);
		var cd = new MeshKit();
		if (World.FurnitureKit.Add(cd, "office_chair", new Transform3D(new Basis(Vector3.Up, 1.2f) * new Basis(Vector3.Right, 1.5f), t + new Vector3(0.5f, 0.28f, 1.05f)), roles)) cd.CommitTo(chairDown, "Chair", true);

		// ---- the filing cabinets in the corner by the way in: shut, then rifled (a drawer out, the files on the floor)
		var cabKept = Group("Cabinets", 0, 1);
		var cabRifled = Group("CabinetsRifled", 2, 4);
		var c0 = new Vector3(-4.6f, 0, -IZ + 0.37f);
		var c1 = new Vector3(-4.06f, 0, -IZ + 0.36f);
		var kk = new MeshKit();
		World.FurnitureKit.Add(kk, "filing_cabinet", c0, Mathf.Pi + 0.03f, roles);
		World.FurnitureKit.Add(kk, "filing_cabinet", c1, Mathf.Pi - 0.04f, roles);
		kk.CommitTo(cabKept, "Cabinets", true);
		var kr = new MeshKit();
		World.FurnitureKit.Add(kr, "filing_cabinet", c0, Mathf.Pi + 0.03f, roles);
		World.FurnitureKit.Add(kr, "filing_cabinet_open", c1, Mathf.Pi - 0.04f, roles);
		World.FurnitureKit.Add(kr, "papers", c1 + new Vector3(0.25f, 0.003f, 0.75f), 0.9f, roles);
		World.FurnitureKit.Add(kr, "papers", c1 + new Vector3(-0.3f, 0.003f, 0.95f), 2.3f, roles);
		kr.CommitTo(cabRifled, "Cabinets", true);
		body.AddChild(new CollisionShape3D { Position = new Vector3(-4.33f, 0.66f, -IZ + 0.37f), Shape = new BoxShape3D { Size = new Vector3(1.04f, 1.32f, 0.58f) } });
		// a bundle of rolled maps on top
		small.Mat(StationTextures.NoticeMat);
		for (int i = 0; i < 4; i++)
		{
			small.Color = new Color(0.85f, 0.82f, 0.72f) * rng.RandfRange(0.8f, 1f);
			Vector3 a = c0 + new Vector3(-0.2f, 1.36f + (i % 2) * 0.06f, -0.12f + i * 0.07f);
			small.Cylinder(a, a + new Vector3(0.42f, 0.01f * i, 0.04f * (i - 1.5f)), 0.03f, 0.03f, 8, true);
		}

		// ---- the district's survey map on the wall by Room 2, in a plain frame under glass
		var map = Group("Map", 0, 2);
		var mk = new MeshKit();
		mk.Mat(oak);
		mk.Color = new Color(0.7f, 0.6f, 0.5f);
		var mb = new Basis(Vector3.Up, Mathf.Pi * 0.5f);   // facing +X
		Vector3 mc = MapAt + Vector3.Right * 0.025f;
		mk.BevelBox(mc + new Vector3(0, 0.41f, 0), new Vector3(0.04f, 0.04f, 1.16f), 0.008f);
		mk.BevelBox(mc + new Vector3(0, -0.41f, 0), new Vector3(0.04f, 0.04f, 1.16f), 0.008f);
		mk.BevelBox(mc + new Vector3(0, 0, 0.56f), new Vector3(0.04f, 0.78f, 0.04f), 0.008f);
		mk.BevelBox(mc + new Vector3(0, 0, -0.56f), new Vector3(0.04f, 0.78f, 0.04f), 0.008f);
		mk.Mat(StationTextures.MapMat);
		mk.Color = Colors.White;
		mk.Quad(mc + new Vector3(0.005f, -0.39f, 0.54f), mc + new Vector3(0.005f, -0.39f, -0.54f), mc + new Vector3(0.005f, 0.39f, -0.54f), mc + new Vector3(0.005f, 0.39f, 0.54f), Vector3.Right,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		Commit(mk, map, "Map", false);
		SignKit.Text(map, "HOLLOW DISTRICT   SHEET 4", mc + new Vector3(0.012f, 0.33f, 0f), mb, 0.035f, new Color(0.16f, 0.13f, 0.1f), shadow: false);

		// ---- the stove: a cold pot-belly on a zinc mat, its flue up and into the wall
		var stove = Group("Stove", 0, 4);
		var sk = new MeshKit();
		var iron = StationTextures.CastIronMat;
		Vector3 s0 = StoveAt;
		sk.Mat(StationTextures.Flat("st_zinc", new Color(0.32f, 0.33f, 0.32f), 0.55f, 0.5f));
		sk.Color = Colors.White;
		sk.BevelBox(s0 + new Vector3(-0.05f, 0.006f, 0), new Vector3(0.95f, 0.012f, 0.9f), 0.004f);
		sk.Mat(iron);
		// (2026-10-15, the props' proportions pass) turned in one smooth profile, 16 sides, instead of five stacked drums:
		// a cast base on three cabriole legs, the ash pit, the belly swelling out to 52 cm, its cast band, the shoulder in
		// to the fire collar, a domed top with its finial and the flue's collar
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Pi / 3f + Mathf.Tau * i / 3f;
			Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			var knee = s0 + d * 0.17f + Vector3.Up * 0.15f;
			var ankle = s0 + d * 0.22f + Vector3.Up * 0.05f;
			var foot = s0 + d * 0.235f + Vector3.Up * 0.012f;
			sk.Cylinder(knee, ankle, 0.03f, 0.02f, 8, false);
			sk.Cylinder(ankle, foot, 0.02f, 0.028f, 8, false);
			sk.Cylinder(foot + Vector3.Down * 0.004f, foot + Vector3.Up * 0.008f, 0.032f, 0.03f, 8, true);
		}
		HollowProps.Lathe(sk, s0, new Vector2[]
		{
			new(0.19f, 0.13f), new(0.215f, 0.14f), new(0.215f, 0.17f), new(0.2f, 0.18f), new(0.205f, 0.2f),   // the base and its rim
			new(0.23f, 0.24f), new(0.252f, 0.3f), new(0.262f, 0.36f), new(0.262f, 0.42f),                     // the belly
			new(0.272f, 0.425f), new(0.272f, 0.445f), new(0.26f, 0.45f),                                        // its cast band
			new(0.252f, 0.5f), new(0.232f, 0.56f), new(0.2f, 0.62f), new(0.172f, 0.67f),                        // the shoulder
			new(0.2f, 0.68f), new(0.212f, 0.7f), new(0.212f, 0.73f), new(0.19f, 0.74f),                         // the fire collar
			new(0.17f, 0.76f), new(0.13f, 0.79f), new(0.075f, 0.81f),                                          // the dome
		}, 16, true, true);
		sk.Cylinder(s0 + Vector3.Up * 0.8f, s0 + Vector3.Up * 0.83f, 0.085f, 0.078f, 12, false);   // the flue's collar
		// the fire door on the belly, facing the room (-X): curved to it, a raised rim, hinge lugs, its latch; the ash door
		// under the band
		{
			sk.BevelBox(s0 + new Vector3(-0.264f, 0.35f, 0), new Vector3(0.022f, 0.14f, 0.2f), 0.008f);
			sk.BevelBox(s0 + new Vector3(-0.276f, 0.35f, 0), new Vector3(0.008f, 0.11f, 0.17f), 0.003f);
			foreach (float dy in new[] { -0.045f, 0.045f })
				sk.Cylinder(s0 + new Vector3(-0.272f, 0.35f + dy - 0.015f, 0.105f), s0 + new Vector3(-0.272f, 0.35f + dy + 0.015f, 0.105f), 0.01f, 0.01f, 6, true);
			sk.Cylinder(s0 + new Vector3(-0.28f, 0.35f, -0.08f), s0 + new Vector3(-0.32f, 0.36f, -0.08f), 0.008f, 0.006f, 6, true);
			sk.BevelBox(s0 + new Vector3(-0.21f, 0.2f, 0), new Vector3(0.02f, 0.05f, 0.15f), 0.005f);
			sk.Cylinder(s0 + new Vector3(-0.222f, 0.2f, 0.05f), s0 + new Vector3(-0.245f, 0.2f, 0.05f), 0.007f, 0.007f, 6, true);
		}
		// the flue: up from the top, a damper, and a bend into the wall's thimble
		float flueY = 2.25f;
		sk.Cylinder(s0 + Vector3.Up * 0.82f, s0 + Vector3.Up * flueY, 0.07f, 0.07f, 12, false);
		sk.Cylinder(s0 + Vector3.Up * 1.3f, s0 + Vector3.Up * 1.33f, 0.078f, 0.078f, 12, true);
		sk.Cylinder(s0 + Vector3.Up * flueY, s0 + new Vector3(0.06f, flueY + 0.1f, 0), 0.07f, 0.07f, 12, false);
		sk.Cylinder(s0 + new Vector3(0.06f, flueY + 0.1f, 0), new Vector3(IX + 0.02f, flueY + 0.12f, s0.Z), 0.07f, 0.07f, 12, false);
		sk.Cylinder(new Vector3(IX - 0.01f, flueY + 0.12f, s0.Z), new Vector3(IX + 0.005f, flueY + 0.12f, s0.Z), 0.11f, 0.11f, 14, true);   // the thimble's plate
		// a kettle set down on the mat (the stove is cold), a poker against it. (2026-10-15: a kettle's real shape, not a drum
		// with a cone and a stick: HollowProps.Kettle)
		HollowProps.Kettle(sk, s0 + new Vector3(-0.33f, 0.012f, 0.3f), 2.2f, StationTextures.Flat("st_enamel", new Color(0.2f, 0.24f, 0.22f), 0.45f, 0.4f), StationTextures.CasingMat);
		sk.Mat(iron);
		sk.Cylinder(s0 + new Vector3(-0.3f, 0.01f, -0.28f), s0 + new Vector3(-0.22f, 0.75f, -0.24f), 0.008f, 0.008f, 6, true);
		ItemMeshes.Torus(sk, s0 + new Vector3(-0.218f, 0.79f, -0.238f), new Vector3(0.1f, -0.05f, 0.99f), 0.035f, 0.006f, 10, 4);   // its handle's loop
		body.AddChild(new CollisionShape3D { Position = s0 + new Vector3(0, 0.45f, 0), Shape = new CylinderShape3D { Radius = 0.27f, Height = 0.9f } });
		// kindling stacked by it
		if (!World.FurnitureKit.Add(sk, "log_pile", s0 + new Vector3(0.05f, 0.012f, -0.75f), Mathf.Pi * 0.5f + 0.1f, roles, Vector3.One * 0.6f))
		{
			sk.Mat(PropTextures.PostMat);
			for (int i = 0; i < 5; i++) sk.Cylinder(s0 + new Vector3(-0.2f + i * 0.09f, 0.06f, -0.95f), s0 + new Vector3(-0.2f + i * 0.09f, 0.06f, -0.55f), 0.045f, 0.045f, 6, true);
		}
		Commit(sk, stove, "Stove", true);

		// ---- cartons of pamphlets in the back corner by the bench, one on another
		var cartons = Group("Cartons", 0, 4);
		var card = StationTextures.Flat("st_card", new Color(0.4f, 0.32f, 0.22f), 0.95f, 0.05f);
		var ok = new MeshKit();
		ClutterKit.Carton(card, new Vector3(0.44f, 0.32f, 0.36f)).Build(ok, new Transform3D(new Basis(Vector3.Up, 0.12f), new Vector3(-IX + 0.27f, 0, IZ - 0.24f)));
		ClutterKit.Carton(card, new Vector3(0.38f, 0.26f, 0.3f), new Color(0.85f, 0.8f, 0.72f)).Build(ok, new Transform3D(new Basis(Vector3.Up, -0.2f), new Vector3(-IX + 0.27f, 0.33f, IZ - 0.25f)));
		ClutterKit.Bundle(card, card, new Vector3(0.26f, 0.12f, 0.2f), new Color(0.62f, 0.6f, 0.5f)).Build(ok, new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(-IX + 0.62f, 0, IZ - 0.22f)));
		body.AddChild(new CollisionShape3D { Position = new Vector3(-IX + 0.27f, 0.3f, IZ - 0.24f), Shape = new BoxShape3D { Size = new Vector3(0.44f, 0.6f, 0.36f) } });
		Commit(ok, cartons, "Cartons", false);

		Commit(k, Group("RadioDesk", 0, 4), "RadioDesk", true);
		Commit(small, this, "RangerSmall", false);
	}

	/// <summary>Stage 1+: the damp. Stains creeping up from the floor, the fallen photo, puddles by the basement door.</summary>
	private void BuildDamp(RandomNumberGenerator rng)
	{
		var damp = Group("Damp", 1, 4);
		foreach (var (at, n) in new[]
		{
			(new Vector3(3.2f, 0.7f, -D + 0.08f), Vector3.Back), (new Vector3(-1f, 0.7f, -D + 0.08f), Vector3.Back),
			(new Vector3(W - 0.08f, 0.7f, -2.8f), Vector3.Left), (new Vector3(-W + 0.08f, 0.7f, 2.7f), Vector3.Right),
			(new Vector3(3f, 0.7f, D - 0.08f), Vector3.Forward), (new Vector3(-2.8f, 0.7f, D - 0.08f), Vector3.Forward),
		})
			StationProps.Decal(damp, StationTextures.StainMat, at, n, new Vector2(rng.RandfRange(1.6f, 2.6f), 1.5f));
		// the ceiling's first stains, over the basement door's corner and the back
		DecalDresser.Place(damp, "ceiling", new Vector3(3.4f, H - 0.01f, -3.2f), Vector3.Down, new Vector2(1.6f, 1.3f), 0.7f, 0.4f, 0.1f);
		DecalDresser.Place(damp, "mould", new Vector3(-IX + 0.01f, H - 0.45f, IZ - 0.5f), Vector3.Right, new Vector2(1.0f, 0.8f), 0.6f);
		// the photo that came down: face-down on the floor, its glass in pieces round it
		var fallen = Group("PhotoFallen", 1, 2);
		var k = new MeshKit();
		k.Mat(StationTextures.CasingMat);
		k.Color = new Color(0.6f, 0.5f, 0.42f);
		BuildKit.Box(k, new Vector3(-3.3f, 0.02f, D - 0.7f), new Vector3(0.46f, 0.03f, 0.36f), 1f, BuildKit.Face.None, new Basis(Vector3.Up, 0.4f));
		k.Mat(StationTextures.Flat("st_glass_shard", new Color(0.4f, 0.43f, 0.44f), 0.1f, 0.8f));
		k.Color = Colors.White;
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = new(-3.3f + rng.RandfRange(-0.5f, 0.5f), 0.004f, D - 0.7f + rng.RandfRange(-0.4f, 0.4f));
			k.Tri(p, p + new Vector3(rng.RandfRange(0.03f, 0.08f), 0, rng.RandfRange(-0.03f, 0.03f)), p + new Vector3(0.01f, 0, rng.RandfRange(0.03f, 0.07f)),
				Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
		}
		Commit(k, fallen, "Fallen", false);
		foreach (var at in new[] { new Vector3(2.6f, 0.012f, -3.6f), new Vector3(3.3f, 0.012f, -2.8f) })
		{
			var puddle = StationProps.Decal(damp, StationTextures.Flat("st_puddle", new Color(0.05f, 0.05f, 0.04f), 0.02f, 0.9f), at, Vector3.Up, new Vector2(rng.RandfRange(0.9f, 1.4f), rng.RandfRange(0.6f, 1f)), rng.RandfRange(0, 3f));
			puddle.Transparency = 0.25f;
		}
	}

	/// <summary>Stage 2+: the paper peels in long strips, bloody hands on the walls, webs in the corners.</summary>
	private void BuildBlood(RandomNumberGenerator rng)
	{
		var peel = Group("Peel", 2, 4);
		var k = new MeshKit();
		k.Mat(StationTextures.WallpaperStainedMat);
		k.Color = new Color(0.9f, 0.85f, 0.75f);
		var rot = new MeshKit();
		rot.Mat(StationTextures.RotBoardsMat);
		rot.Color = Colors.White;
		// bare patches of rotten board where the paper has come away, and the strips curling off them
		// (2026-10-14h: the front wall's patch moved up and right, clear of the radio desk's papers)
		foreach (var (at, n) in new[]
		{
			(new Vector3(-2.2f, 2.2f, D - 0.075f), Vector3.Forward), (new Vector3(2.5f, 1.8f, D - 0.075f), Vector3.Forward),
			(new Vector3(W - 0.075f, 2.0f, 3.6f), Vector3.Left), (new Vector3(-W + 0.075f, 2.3f, -1.8f), Vector3.Right),
			(new Vector3(0.9f, 2.45f, -D + 0.075f), Vector3.Back),
		})
		{
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			float w = rng.RandfRange(0.6f, 1.1f), h = rng.RandfRange(0.7f, 1.2f);
			if (at.Z < 0f && n == Vector3.Back) h = Mathf.Min(h, 0.9f);
			rot.Quad(at - right * w * 0.5f - Vector3.Up * h * 0.5f, at + right * w * 0.5f - Vector3.Up * h * 0.5f,
				at + right * w * 0.5f + Vector3.Up * h * 0.5f, at - right * w * 0.5f + Vector3.Up * h * 0.5f, n);
			// a strip hanging down, curling out from the wall
			Vector3 top = at + Vector3.Up * h * 0.5f + right * rng.RandfRange(-0.2f, 0.2f);
			Vector3 mid = top + Vector3.Down * 0.4f + n * 0.12f, bot = mid + Vector3.Down * 0.35f + n * 0.25f;
			float sw = 0.18f;
			k.Quad(top - right * sw, top + right * sw, mid + right * sw, mid - right * sw, n);
			k.Quad(mid - right * sw, mid + right * sw, bot + right * sw * 0.8f, bot - right * sw * 0.8f, n);
			k.Quad(mid - right * sw, mid + right * sw, top + right * sw, top - right * sw, -n);
		}
		Commit(k, peel, "Strips", false);
		Commit(rot, peel, "Bare", false);

		var blood = Group("Handprints", 2, 4);
		foreach (var (at, n, spin) in new[]
		{
			(new Vector3(-W + 0.08f, 1.4f, 1.6f), Vector3.Right, 0.3f), (new Vector3(-W + 0.08f, 1.2f, 1.9f), Vector3.Right, -0.2f),
			(new Vector3(W - 0.08f, 1.5f, 1.3f), Vector3.Left, 0.1f), (new Vector3(1.8f, 1.6f, -D + 0.08f), Vector3.Back, 0.5f),
			(new Vector3(-1.4f, 1.1f, D - 0.08f), Vector3.Forward, -0.4f), (new Vector3(-1.6f, 1.35f, D - 0.08f), Vector3.Forward, -0.2f),
		})
			StationProps.Decal(blood, StationTextures.HandprintMat, at, n, new Vector2(0.24f, 0.24f), spin);

		var webs = Group("CornerWebs", 2, 4);
		foreach (var (corner, a, b) in new[]
		{
			(new Vector3(-W, H, D), Vector3.Right, Vector3.Forward), (new Vector3(W, H, -D), Vector3.Left, Vector3.Back),
			(new Vector3(W, H, D), Vector3.Left, Vector3.Forward),
		})
			World.WebKit.Corner(webs, rng, corner, a, b, 1.3f);
	}

	/// <summary>Stage 3+: industrial. Rusted steel plate bolted over the walls, pipes, chains, grating,
	/// steam, a blood trail along the floor to the desk.</summary>
	private void BuildIndustry(RandomNumberGenerator rng)
	{
		var ind = Group("Industry", 3, 4);
		var k = new MeshKit();
		// (2026-10-14h: the plate weathered and a step darker: it was a bright orange print over every wall)
		k.Mat(RoomSurface.Of(StationTextures.RustPlateMat, RoomSurface.Concrete with { Streaks = 0.8f, Desaturate = 0.3f }, 136));
		k.Color = new Color(0.7f, 0.66f, 0.62f);
		// plates over most of each wall, leaving the doorways (and the dressing standing in front of them)
		void Plates(Vector3 a, Vector3 b, Vector3 n)
		{
			Vector3 along = (b - a);
			float len = along.Length();
			Vector3 dir = along / len;
			for (float t = 0.1f; t < len - 0.3f; t += 1.25f)
			{
				float w = Mathf.Min(1.2f, len - t - 0.1f);
				Vector3 c = a + dir * (t + w * 0.5f) + n * 0.1f;
				float h = rng.RandfRange(1.6f, 2.9f);
				Vector3 size = new(Mathf.Abs(dir.X) * w + Mathf.Abs(n.X) * 0.04f, h, Mathf.Abs(dir.Z) * w + Mathf.Abs(n.Z) * 0.04f);
				BuildKit.Box(k, c + Vector3.Up * h * 0.5f, size, 1.2f, BuildKit.Face.None, new Basis(n, rng.RandfRange(-0.03f, 0.03f)));
			}
		}
		Plates(new Vector3(-W, 0, -D), new Vector3(-3.6f, 0, -D), Vector3.Back);
		Plates(new Vector3(-1.2f, 0, -D), new Vector3(1.4f, 0, -D), Vector3.Back);
		Plates(new Vector3(3.8f, 0, -D), new Vector3(W, 0, -D), Vector3.Back);
		Plates(new Vector3(-W, 0, D), new Vector3(-1.3f, 0, D), Vector3.Forward);
		Plates(new Vector3(1.3f, 0, D), new Vector3(W, 0, D), Vector3.Forward);
		Plates(new Vector3(W, 0, -D), new Vector3(W, 0, -1.2f), Vector3.Left);
		Plates(new Vector3(W, 0, 1.2f), new Vector3(W, 0, D), Vector3.Left);
		Plates(new Vector3(-W, 0, -D), new Vector3(-W, 0, -1.2f), Vector3.Right);
		Plates(new Vector3(-W, 0, 1.2f), new Vector3(-W, 0, D), Vector3.Right);
		k.Color = Colors.White;
		// pipes along the ceiling edges and down the corners
		StationProps.Pipe(k, new Vector3(-W + 0.25f, H - 0.25f, -D + 0.25f), new Vector3(W - 0.25f, H - 0.25f, -D + 0.25f), 0.07f);
		StationProps.Pipe(k, new Vector3(-W + 0.25f, H - 0.35f, D - 0.25f), new Vector3(W - 0.25f, H - 0.35f, D - 0.25f), 0.09f);
		StationProps.Pipe(k, new Vector3(W - 0.25f, H - 0.3f, -D + 0.25f), new Vector3(W - 0.25f, H - 0.3f, D - 0.25f), 0.06f);
		StationProps.Pipe(k, new Vector3(-W + 0.25f, 0, -D + 0.25f), new Vector3(-W + 0.25f, H, -D + 0.25f), 0.08f);
		StationProps.Pipe(k, new Vector3(W - 0.25f, 0, D - 0.25f), new Vector3(W - 0.25f, H, D - 0.25f), 0.08f);
		// grating over the middle of the floor
		k.Mat(StationTextures.RustPlateMat);
		k.Color = new Color(0.4f, 0.36f, 0.32f);
		for (float x = -1.9f; x <= 1.9f; x += 0.19f)
			BuildKit.Box(k, new Vector3(x, 0.02f, -0.3f), new Vector3(0.03f, 0.03f, 3.4f), 2f);
		for (float z = -1.9f; z <= 1.3f; z += 0.6f)
			BuildKit.Box(k, new Vector3(0, 0.035f, z), new Vector3(3.9f, 0.02f, 0.04f), 2f);
		foreach (var top in new[] { new Vector3(-2.6f, H, -1.2f), new Vector3(2.2f, H, 1.4f), new Vector3(-0.8f, H, -2.9f) })
			StationProps.Chain(k, top, rng.RandfRange(0.9f, 1.8f));
		Commit(k, ind, "Industry", true);

		// the blood trail: drops, then drag marks, along the floor to behind the desk
		for (int i = 0; i < 9; i++)
		{
			float t = i / 8f;
			Vector3 at = new Vector3(-2.4f, 0.01f, -3.4f).Lerp(new Vector3(-0.6f, 0.01f, 3.6f), t) + new Vector3(rng.RandfRange(-0.15f, 0.15f), 0, 0);
			StationProps.Decal(ind, StationTextures.BloodMat, at, Vector3.Up, new Vector2(0.3f + 0.4f * t, 0.3f + 0.2f * t), rng.RandfRange(0, 3f));
		}
		// rust weeping down from the plates' bolts, water down from the pipes
		foreach (var (at, n) in new[] { (new Vector3(-2.4f, 1.6f, D - 0.03f), Vector3.Forward), (new Vector3(3.0f, 1.5f, D - 0.03f), Vector3.Forward), (new Vector3(W - 0.03f, 1.6f, -2.5f), Vector3.Left), (new Vector3(-W + 0.03f, 1.7f, 2.6f), Vector3.Right) })
			DecalDresser.Place(ind, "streak", at, n, new Vector2(0.9f, 2.2f), 0.7f, 0f, 0.12f);
		// steam hissing from a burst joint, over by the basement door
		ind.AddChild(Steam(new Vector3(W - 0.3f, H - 0.3f, -D + 0.3f)));
		ind.AddChild(Steam(new Vector3(-W + 0.3f, H - 0.35f, D - 0.3f)));
	}

	/// <summary>Stage 4: the rot. The ceiling has come down in two places (black holes over heaps of plaster and lath,
	/// the lath hanging), water drips through, black mould spreads from the corners and round the holes, the paper sags,
	/// and here and there something grows out through the walls: dull, mould-furred lumps of flesh, part buried in the
	/// plaster. (2026-10-14h, the station pass: it was ten glossy, wet, red growths the size of armchairs, spot-lit by the
	/// lantern into hot glints; the look survey: let the damp, the mould and the collapsing ceiling carry the rot.)</summary>
	private void BuildRot(RandomNumberGenerator rng)
	{
		var rot = Group("Flesh", 4, 4);
		var k = new MeshKit();
		// the growths, fewer and smaller, sunk into the walls
		foreach (var (at, n, size) in new[]
		{
			(new Vector3(-W + 0.07f, 2.5f, -3.4f), Vector3.Right, 0.6f), (new Vector3(-W + 0.07f, 0.35f, 3.5f), Vector3.Right, 0.5f),
			(new Vector3(W - 0.07f, 2.75f, 3.3f), Vector3.Left, 0.65f), (new Vector3(3.6f, H - 0.05f, -1.9f), Vector3.Down, 0.55f),
			(new Vector3(-2.3f, 2.7f, D - 0.07f), Vector3.Forward, 0.55f), (new Vector3(-1.5f, 2.85f, -D + 0.07f), Vector3.Back, 0.5f),
		})
		{
			StationProps.Growth(k, at - n * size * 0.25f, n, size, (int)(at.X * 31 + at.Z * 17));
			DecalDresser.Place(rot, "mould", at + n * 0.01f, n, new Vector2(size * 2.6f, size * 2.2f), 0.85f, rng.RandfRange(0f, 3f), 0.2f);
		}
		k.Color = Colors.White;

		// the ceiling down: a ragged black hole, plaster chunks round its lip, the lath hanging, a heap under it
		var plaster = RoomSurface.Of(StationTextures.CeilingMat, RoomSurface.Plaster, 137);
		var lath = StationTextures.RotBoardsMat;
		var dark = StationTextures.Flat("st_void", new Color(0.012f, 0.01f, 0.01f), 1f, 0f);
		void Collapse(Vector3 c, float r, int seed)
		{
			var rr = new RandomNumberGenerator { Seed = (ulong)seed };
			const int n = 11;
			var rim = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				float a = Mathf.Tau * i / n, rad = r * rr.RandfRange(0.6f, 1.1f);
				rim[i] = new Vector3(c.X + Mathf.Cos(a) * rad, H - 0.004f, c.Z + Mathf.Sin(a) * rad * 0.8f);
			}
			k.Mat(dark);
			k.Color = Colors.White;
			Vector3 mid = new(c.X, H - 0.004f, c.Z);
			for (int i = 0; i < n; i++) k.Tri(mid, rim[(i + 1) % n], rim[i], Vector3.Down, Vector2.Zero, Vector2.Right, Vector2.Down);
			// chunks of plaster hanging off the lip, broken pieces of lath dangling into the room
			k.Mat(plaster);
			for (int i = 0; i < n; i++)
			{
				if (rr.Randf() < 0.35f) continue;
				Vector3 p = rim[i] + Vector3.Down * 0.02f;
				Vector3 outward = (p - mid) with { Y = 0 };
				var b = new Basis(outward.Normalized().Cross(Vector3.Up).Normalized(), rr.RandfRange(-0.7f, -0.2f)) * new Basis(Vector3.Up, rr.Randf() * 3f);
				k.BevelBox(p + Vector3.Down * 0.03f, new Vector3(rr.RandfRange(0.12f, 0.26f), 0.025f, rr.RandfRange(0.1f, 0.2f)), 0.006f, 1f, b);
			}
			k.Mat(lath);
			for (int i = 0; i < 6; i++)
			{
				float x = c.X + rr.RandfRange(-r * 0.7f, r * 0.7f);
				Vector3 top = new(x, H - 0.01f, c.Z + rr.RandfRange(-r * 0.4f, r * 0.4f));
				Vector3 bot = top + new Vector3(rr.RandfRange(-0.15f, 0.15f), -rr.RandfRange(0.3f, 0.8f), rr.RandfRange(-0.3f, 0.3f));
				k.Beam(top, bot, 0.035f, 0.008f);
			}
			// the heap: plaster slabs and lath on the floor, dust round them
			for (int i = 0; i < 16; i++)
			{
				float a = rr.Randf() * Mathf.Tau, d = Mathf.Sqrt(rr.Randf()) * r * 1.1f;
				Vector3 p = new(c.X + Mathf.Cos(a) * d, 0.012f + rr.Randf() * 0.04f, c.Z + Mathf.Sin(a) * d);
				var b = new Basis(Vector3.Up, rr.Randf() * Mathf.Tau) * new Basis(Vector3.Right, rr.RandfRange(-0.4f, 0.4f));
				if (i % 3 == 0) { k.Mat(lath); k.Beam(p, p + b * new Vector3(rr.RandfRange(0.4f, 0.9f), 0, 0), 0.035f, 0.008f); }
				else { k.Mat(plaster); k.BevelBox(p, new Vector3(rr.RandfRange(0.1f, 0.32f), rr.RandfRange(0.02f, 0.04f), rr.RandfRange(0.08f, 0.25f)), 0.006f, 1f, b); }
			}
			DecalDresser.Place(rot, "floor", new Vector3(c.X, 0.01f, c.Z), Vector3.Up, new Vector2(r * 3f, r * 2.6f), 0.8f, rr.Randf() * 3f, 0.1f);
			DecalDresser.Place(rot, "ceiling", new Vector3(c.X, H - 0.01f, c.Z), Vector3.Down, new Vector2(r * 4f, r * 3.4f), 0.85f, rr.Randf() * 3f, 0.1f);
			DecalDresser.Place(rot, "puddle", new Vector3(c.X + r * 0.6f, 0.01f, c.Z - r * 0.5f), Vector3.Up, new Vector2(0.9f, 0.7f), 0.85f, rr.Randf() * 3f, 0.1f);
		}
		Collapse(new Vector3(-2.9f, H, 2.45f), 0.65f, 71);
		Collapse(new Vector3(2.7f, H, -1.3f), 0.5f, 72);
		Commit(k, rot, "Rot", true);

		// mould up out of the corners and along the cornice; long streaks down the walls; the paper sagging off in a sheet
		foreach (var (at, n, size) in new[]
		{
			(new Vector3(-IX + 0.01f, 0.6f, -IZ + 0.9f), Vector3.Right, new Vector2(1.6f, 1.2f)), (new Vector3(IX - 0.01f, 2.6f, -IZ + 1.0f), Vector3.Left, new Vector2(1.8f, 1.1f)),
			(new Vector3(IX - 0.01f, 0.7f, IZ - 0.9f), Vector3.Left, new Vector2(1.6f, 1.3f)), (new Vector3(-3.9f, 2.7f, IZ - 0.01f), Vector3.Forward, new Vector2(1.8f, 1.0f)),
			(new Vector3(3.9f, 2.6f, -IZ + 0.01f), Vector3.Back, new Vector2(1.5f, 1.1f)), (new Vector3(1.9f, 2.65f, IZ - 0.01f), Vector3.Forward, new Vector2(1.4f, 1.0f)),
		})
			DecalDresser.Place(rot, "mould", at, n, size, 0.85f, 0f, 0.12f);
		foreach (var (at, n) in new[] { (new Vector3(-0.8f, 1.9f, -IZ + 0.01f), Vector3.Back), (new Vector3(-IX + 0.01f, 2.0f, -0.9f), Vector3.Right), (new Vector3(IX - 0.01f, 1.8f, 2.0f), Vector3.Left), (new Vector3(3.8f, 1.8f, IZ - 0.01f), Vector3.Forward) })
			DecalDresser.Place(rot, "streak", at, n, new Vector2(0.8f, 2.4f), 0.8f, 0f, 0.12f);
		var sag = new MeshKit();
		sag.Mat(StationTextures.WallpaperStainedMat);
		sag.Color = new Color(0.7f, 0.66f, 0.58f);
		// a sheet of paper come away at the top and hanging forward off the back wall, folded once
		Vector3 s0 = new(3.4f, H - 0.25f, IZ - 0.004f);
		float sw = 0.5f;
		Vector3 s1 = s0 + new Vector3(0, -0.9f, -0.25f), s2 = s1 + new Vector3(0.05f, -0.7f, 0.12f);
		sag.Quad(s0 + Vector3.Right * sw, s0 + Vector3.Left * sw, s1 + Vector3.Left * sw, s1 + Vector3.Right * sw, Vector3.Forward);
		sag.Quad(s1 + Vector3.Right * sw, s1 + Vector3.Left * sw, s2 + Vector3.Left * sw * 0.9f, s2 + Vector3.Right * sw * 0.9f, Vector3.Forward);
		sag.Mat(StationTextures.RotBoardsMat);
		sag.Color = Colors.White;
		sag.Quad(s0 + Vector3.Left * sw + Vector3.Down * 1.6f, s0 + Vector3.Right * sw + Vector3.Down * 1.6f, s0 + Vector3.Right * sw, s0 + Vector3.Left * sw, Vector3.Forward);
		Commit(sag, rot, "Sag", false);
	}

	private void BuildLight()
	{
		var k = new MeshKit();
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.55f, 0.46f, 0.3f);
		k.Cylinder(new Vector3(0, H, 0), new Vector3(0, H - 0.5f, 0), 0.012f, 0.012f, 5, false);
		k.Cylinder(new Vector3(0, H - 0.04f, 0), new Vector3(0, H - 0.1f, 0), 0.06f, 0.03f, 10);   // the canopy where the rod hangs
		// (2026-10-14b) a pressed-brass gallery the globe hangs in: it shades the ceiling right over the lamp (the rose was blown
		// out) and hides the bare join of rod and glass
		k.Cylinder(new Vector3(0, H - 0.45f, 0), new Vector3(0, H - 0.49f, 0), 0.03f, 0.17f, 16);
		k.Cylinder(new Vector3(0, H - 0.49f, 0), new Vector3(0, H - 0.55f, 0), 0.17f, 0.175f, 16, false);
		k.CommitTo(this, "LampRod", true);
		// a plaster rose round it on the ceiling, in rings (the interiors pass, 2026-10-03); a hair under the ceiling
		var rose = new MeshKit();
		rose.Mat(new StandardMaterial3D { ResourceName = "st_rose", AlbedoColor = new Color(0.42f, 0.4f, 0.35f), Roughness = 0.95f, VertexColorUseAsAlbedo = true });   // (2026-10-14b: a step darker: right over the lamp, it blew out to a white disc)
		foreach (var (r, top, bottom) in new[] { (0.36f, 0.002f, 0.018f), (0.29f, 0.018f, 0.03f), (0.2f, 0.03f, 0.044f), (0.1f, 0.044f, 0.058f) })
		{
			rose.Color = Colors.White * (1f - top * 2f);
			rose.Cylinder(new Vector3(0, H - top, 0), new Vector3(0, H - bottom, 0), r, r - 0.012f, 28);
		}
		Commit(rose, this, "CeilingRose", false);
		_globe = new MeshInstance3D
		{
			Name = "LampGlobe",
			Mesh = new SphereMesh { Radius = 0.18f, Height = 0.3f },
			Position = new Vector3(0, H - 0.62f, 0),
			// (2026-10-14h: an old opal glass gone yellow, not a white disc)
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.8f, 0.68f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.6f), EmissionEnergyMultiplier = 0.8f,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_globe);
		// (2026-10-14h: a pool, not a floodlight: the corners of a 10 m room fall away into the dark; it had lit the whole box
		// to the ceiling)
		_ceiling = new OmniLight3D
		{
			Name = "LobbyLamp", LightColor = new Color(1f, 0.8f, 0.6f), LightEnergy = 2.3f,
			OmniRange = 8f, OmniAttenuation = 1.4f, Position = LampAt, ShadowEnabled = true,
		};
		AddChild(_ceiling);
	}

	private static GpuParticles3D Steam(Vector3 at)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = new Vector3(0, -0.3f, 1f), Spread = 20f,
			InitialVelocityMin = 0.6f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, 0.25f, 0),
			ScaleMin = 0.8f, ScaleMax = 1.6f,
			ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0f) }, Offsets = new[] { 0f, 1f } } },
		};
		var draw = new QuadMesh { Size = Vector2.One * 0.4f };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = LakeParts.LakeFx.SoftDot(), AlbedoColor = new Color(0.85f, 0.82f, 0.8f, 0.5f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
		};
		return new GpuParticles3D
		{
			Name = "Steam", Amount = 24, Lifetime = 2.2, Position = at, ProcessMaterial = pm, DrawPass1 = draw,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private AudioStreamPlayer3D Loop(string path, Vector3 at, string bus, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = 25f, Position = at };
		AddChild(p);
		return p;
	}
}
