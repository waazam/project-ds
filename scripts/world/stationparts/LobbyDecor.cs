using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// The station lobby's shell and everything in it, in five states of decay. It opens kept and homely
/// (striped paper over wainscot, a rug, the rangers' photos, a notice board, a ticking clock, a plant,
/// the fire-danger sign at LOW, warm light) and rots a stage every time the player goes off and
/// comes back: damp and a stopped clock (after the basement), peeling paper and bloody handprints
/// (after Room 1), rust plate, pipes, chains and red light (after Room 2), and finally flesh growing
/// out of the walls (once the iron door is open).
///
/// Every prop carries the stages it belongs to; <see cref="Apply"/> just shows and hides. The stage
/// follows the story's flags (<see cref="StageFor"/>), so Continue restores it; while playing it only
/// ever changes when the player isn't in the lobby to see it happen.
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

	private const float W = StationInterior.HalfWidth, D = StationInterior.HalfDepth, H = StationInterior.Height;

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
		(Color c, float e)[] light =
		{
			(new Color(1f, 0.82f, 0.62f), 2.3f),
			(new Color(1f, 0.76f, 0.56f), 1.7f),
			(new Color(1f, 0.6f, 0.44f), 1.3f),
			(new Color(1f, 0.32f, 0.24f), 1.35f),
			(new Color(0.95f, 0.16f, 0.1f), 1.5f),
		};
		var (col, energy) = light[Mathf.Clamp(stage, 0, 4)];
		if (_ceiling != null) { _ceiling.LightColor = col; _ceiling.LightEnergy = energy; }
		if (_globe?.MaterialOverride is StandardMaterial3D gm) { gm.Emission = col; gm.EmissionEnergyMultiplier = 0.6f + 0.2f * energy; }
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
		BuildShell();
		BuildKept(rng);
		BuildDamp(rng);
		BuildBlood(rng);
		BuildIndustry(rng);
		BuildFlesh(rng);
		BuildLight();
		Apply(StageFor(StoryManager.Instance));
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
			StationKit.WallAlongX(k, b, -D, -W, 0.2f, H, 0, (StationInterior.EntryGapX, 2.2f));
			StationKit.WallAlongX(k, b, -D, 0.2f, W, H, 0, (StationInterior.BasementGapX, 2.2f));
			StationKit.WallAlongX(k, b, D, -W, W, H, 0, (0f, 2.4f), 2.5f);
			StationKit.WallAlongZ(k, b, W, -D, D, H, 0, (0f, 2.2f));
			StationKit.WallAlongZ(k, b, -W, -D, D, H, 0, (0f, 2.2f));
		}
		var clean = new MeshKit();
		clean.Mat(StationTextures.WallpaperMat);
		clean.Color = Colors.White;
		Walls(clean, body);
		clean.CommitTo(Group("WallsKept", 0, 1), "Walls");
		var stained = new MeshKit();
		stained.Mat(StationTextures.WallpaperStainedMat);
		stained.Color = Colors.White;
		Walls(stained, null);
		stained.CommitTo(Group("WallsStained", 2, 4), "Walls");

		// wainscot and a chair rail round the lower walls (kept and damp stages)
		var wain = new MeshKit();
		wain.Mat(PropTextures.DeckMat);
		wain.Color = new Color(0.46f, 0.34f, 0.24f);
		void Strip(Vector3 a, Vector3 b, Vector3 inward)
		{
			Vector3 c = (a + b) * 0.5f + inward * 0.09f;
			Vector3 size = (b - a).Abs() + new Vector3(0.03f, 0f, 0.03f);
			wain.Box(c + Vector3.Up * 0.5f, new Vector3(Mathf.Max(size.X, 0.03f), 1.0f, Mathf.Max(size.Z, 0.03f)), 1.2f);
			wain.Box(c + Vector3.Up * 1.02f + inward * 0.02f, new Vector3(Mathf.Max(size.X, 0.06f), 0.06f, Mathf.Max(size.Z, 0.06f)), 1.2f);
		}
		Strip(new Vector3(-W, 0, -D), new Vector3(-3.5f, 0, -D), Vector3.Back);
		Strip(new Vector3(-1.3f, 0, -D), new Vector3(1.5f, 0, -D), Vector3.Back);
		Strip(new Vector3(3.7f, 0, -D), new Vector3(W, 0, -D), Vector3.Back);
		Strip(new Vector3(-W, 0, D), new Vector3(-1.2f, 0, D), Vector3.Forward);
		Strip(new Vector3(1.2f, 0, D), new Vector3(W, 0, D), Vector3.Forward);
		foreach (int s in new[] { -1, 1 })
		{
			Vector3 inward = s > 0 ? Vector3.Left : Vector3.Right;
			Strip(new Vector3(s * W, 0, -D), new Vector3(s * W, 0, -1.1f), inward);
			Strip(new Vector3(s * W, 0, 1.1f), new Vector3(s * W, 0, D), inward);
		}
		wain.Color = Colors.White;
		wain.CommitTo(Group("Wainscot", 0, 2), "Wainscot");

		var floorK = new MeshKit();
		var ceilK = new MeshKit();
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.62f, 0.52f, 0.4f);
		ceilK.Mat(BuildingTextures.BoardsMat);
		ceilK.Color = new Color(0.55f, 0.5f, 0.44f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, W, D, H, 0);
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);
	}

	/// <summary>Stage 0 (and some of it lingering): the kept station.</summary>
	private void BuildKept(RandomNumberGenerator rng)
	{
		// the rug (gone by the industrial stage)
		var rug = Group("Rug", 0, 2);
		var k = new MeshKit();
		k.Mat(StationTextures.Flat("st_rug", new Color(0.5f, 0.16f, 0.12f), 0.95f, 0.05f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, 0.005f, 0), new Vector3(4.2f, 0.01f, 3f));
		k.Mat(StationTextures.Flat("st_rug_border", new Color(0.78f, 0.62f, 0.3f), 0.95f, 0.05f));
		BuildKit.Box(k, new Vector3(0, 0.007f, 0), new Vector3(3.6f, 0.01f, 2.4f));
		k.Mat(StationTextures.Flat("st_rug", new Color(0.5f, 0.16f, 0.12f), 0.95f, 0.05f));
		BuildKit.Box(k, new Vector3(0, 0.009f, 0), new Vector3(3.4f, 0.01f, 2.2f));
		k.CommitTo(rug, "Rug", false);

		// the rangers on the wall: four framed photographs on the back wall either side of the web
		var photos = Group("Photos", 0, 2);
		for (int i = 0; i < 4; i++)
		{
			if (i == 1) continue;   // this one comes down in the damp (see BuildDamp)
			Frame(photos, new Vector3(-4.3f + i * 0.8f + (i > 1 ? 4.5f : 0f), 1.75f, D - 0.09f), Vector3.Forward, rng);
		}
		var photoUp = Group("PhotoOnWall", 0, 0);
		Frame(photoUp, new Vector3(-3.5f, 1.75f, D - 0.09f), Vector3.Forward, rng);

		// the notice board (right wall, by Room 2's door) with its posters
		var board = Group("NoticeBoard", 0, 2);
		var bk = new MeshKit();
		bk.Mat(StationTextures.Flat("st_cork", new Color(0.55f, 0.4f, 0.26f), 0.95f, 0.05f));
		bk.Color = Colors.White;
		Vector3 bc = new(-W + 0.09f, 1.6f, -2.6f);
		BuildKit.Box(bk, bc, new Vector3(0.04f, 0.9f, 1.3f));
		bk.Mat(PropTextures.DeckMat);
		bk.Color = new Color(0.38f, 0.28f, 0.18f);
		BuildKit.Box(bk, bc + new Vector3(0, 0.47f, 0), new Vector3(0.06f, 0.05f, 1.36f));
		BuildKit.Box(bk, bc + new Vector3(0, -0.47f, 0), new Vector3(0.06f, 0.05f, 1.36f));
		bk.Mat(StationTextures.Flat("st_paper", new Color(0.86f, 0.82f, 0.7f), 0.9f, 0.05f));
		bk.Color = Colors.White;
		BuildKit.Box(bk, bc + new Vector3(0.025f, 0.05f, -0.32f), new Vector3(0.005f, 0.6f, 0.42f));
		BuildKit.Box(bk, bc + new Vector3(0.025f, 0.08f, 0.3f), new Vector3(0.005f, 0.5f, 0.4f));
		bk.CommitTo(board, "Board", false);
		var tb = new Basis(Vector3.Up, Mathf.Pi * 0.5f);   // facing +X, into the lobby
		SignKit.Text(board, "LAKE SAFETY\n\nNEVER ROW\nALONE", bc + new Vector3(0.03f, 0.16f, -0.32f), tb, 0.055f, new Color(0.15f, 0.2f, 0.35f), shadow: false);
		_poster = SignKit.Text(board, "", bc + new Vector3(0.03f, 0.14f, 0.3f), tb, 0.05f, new Color(0.2f, 0.08f, 0.06f), shadow: false);

		// the fire danger sign: an arrow on a painted dial, beside the entry
		var sign = Group("FireSign", 0, 4);
		var sk = new MeshKit();
		sk.Mat(StationTextures.Flat("st_firesign", new Color(0.7f, 0.58f, 0.3f), 0.8f, 0.1f));
		sk.Color = Colors.White;
		Vector3 fc = new(-4.0f, 1.9f, -D + 0.1f);
		BuildKit.Box(sk, fc, new Vector3(1.1f, 0.6f, 0.04f));
		sk.CommitTo(sign, "Sign", false);
		var fb = Basis.Identity;
		SignKit.Text(sign, "FIRE DANGER TODAY", fc + new Vector3(0, 0.16f, 0.025f), fb, 0.075f, new Color(0.2f, 0.12f, 0.05f), shadow: false);
		_fireDial = SignKit.Text(sign, "LOW", fc + new Vector3(0, -0.08f, 0.025f), fb, 0.16f, new Color(0.55f, 0.08f, 0.04f), shadow: false);

		// a bench under the photos, a chair by the desk, a coat stand with a ranger's jacket, a plant
		var bench = Group("Bench", 0, 2);
		var ck = new MeshKit();
		ck.Mat(PropTextures.DeckMat);
		ck.Color = new Color(0.44f, 0.32f, 0.2f);
		BuildKit.Box(ck, new Vector3(-3.4f, 0.44f, D - 0.4f), new Vector3(1.6f, 0.05f, 0.42f), 1.2f);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(ck, new Vector3(-3.4f + s * 0.7f, 0.21f, D - 0.4f), new Vector3(0.06f, 0.42f, 0.38f), 1.2f);
		ck.Color = Colors.White;
		ck.CommitTo(bench, "Bench", true);

		Chair(Group("ChairUp", 0, 1), new Vector3(0.9f, 0, 3.7f), 0.2f, false);
		Chair(Group("ChairDown", 2, 4), new Vector3(1.6f, 0, 2.0f), 1.1f, true);

		var coat = Group("CoatStand", 0, 3);
		var ct = new MeshKit();
		ct.Mat(PropTextures.PostMat);
		ct.Color = new Color(0.36f, 0.26f, 0.18f);
		Vector3 cs = new(4.2f, 0, -3.8f);
		ct.Cylinder(cs, cs + Vector3.Up * 1.8f, 0.025f, 0.02f, 6, true);
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3;
			ct.Cylinder(cs + Vector3.Up * 0.02f, cs + new Vector3(Mathf.Cos(a) * 0.3f, 0.02f, Mathf.Sin(a) * 0.3f), 0.02f, 0.02f, 5, true);
		}
		ct.Mat(StationTextures.Flat("st_jacket", new Color(0.28f, 0.34f, 0.2f), 0.9f, 0.1f));
		ct.Color = Colors.White;
		ct.Blob(cs + new Vector3(0.05f, 1.35f, 0.05f), new Vector3(0.22f, 0.4f, 0.12f), 5, 0.12f, false);
		ct.CommitTo(coat, "CoatStand", true);

		PlantPot(Group("Plant", 0, 0), new Vector3(4.3f, 0, 3.9f), false);
		PlantPot(Group("PlantDead", 1, 2), new Vector3(4.3f, 0, 3.9f), true);

		// the wall clock over Room 1's door (ticks while all is well; stops in the damp)
		var clock = Group("WallClock", 0, 2);
		var wk = new MeshKit();
		wk.Mat(PropTextures.DeckMat);
		wk.Color = new Color(0.4f, 0.28f, 0.18f);
		Vector3 cc = new(W - 0.08f, 2.75f, 0f);
		wk.Cylinder(cc, cc + Vector3.Left * 0.05f, 0.24f, 0.24f, 16, true);
		wk.Mat(StationTextures.Flat("st_clockface", new Color(0.88f, 0.84f, 0.74f), 0.7f, 0.1f));
		wk.Color = Colors.White;
		wk.Cylinder(cc + Vector3.Left * 0.05f, cc + Vector3.Left * 0.055f, 0.2f, 0.2f, 16, true);
		wk.Mat(StationTextures.Flat("st_hands", new Color(0.05f, 0.05f, 0.05f), 0.6f, 0.2f));
		wk.Box(cc + new Vector3(-0.06f, 0.06f, 0), new Vector3(0.01f, 0.13f, 0.012f));
		wk.Box(cc + new Vector3(-0.06f, 0, 0.05f), new Vector3(0.01f, 0.012f, 0.1f));
		wk.CommitTo(clock, "Clock", false);
		_tick = Loop("res://assets/audio/sfx/clock_tick_loop.wav", cc, "Events", -14f, 3f);
		_drone = Loop("res://assets/audio/ambient/industrial_drone_loop.wav", new Vector3(0, 2.4f, 0), "Unnatural", -10f, 8f);
	}

	private void Frame(Node3D parent, Vector3 at, Vector3 n, RandomNumberGenerator rng)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.22f, 0.16f, 0.1f);
		var b = Basis.LookingAt(-n, Vector3.Up);
		k.Xf = new Transform3D(b, at);
		BuildKit.Box(k, Vector3.Zero, new Vector3(0.46f, 0.36f, 0.03f));
		k.Mat(StationTextures.Flat("st_photo", new Color(0.55f, 0.48f, 0.36f), 0.4f, 0.4f));
		k.Color = new Color(rng.RandfRange(0.8f, 1f), rng.RandfRange(0.75f, 0.9f), 0.7f);
		BuildKit.Box(k, new Vector3(0, 0, 0.017f), new Vector3(0.36f, 0.26f, 0.004f));
		// a figure or two: dark smudges standing in a row
		k.Mat(StationTextures.Flat("st_photofig", new Color(0.18f, 0.15f, 0.12f), 0.5f, 0.3f));
		k.Color = Colors.White;
		int figs = rng.RandiRange(1, 3);
		for (int i = 0; i < figs; i++)
			BuildKit.Box(k, new Vector3(-0.08f * (figs - 1) + i * 0.16f, -0.02f, 0.02f), new Vector3(0.05f, 0.16f, 0.002f));
		k.Xf = Transform3D.Identity;
		k.CommitTo(parent, "Frame", false);
	}

	private static void Chair(Node3D parent, Vector3 at, float yaw, bool toppled)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.4f, 0.28f, 0.18f);
		var b = new Basis(Vector3.Up, yaw) * (toppled ? new Basis(Vector3.Right, -1.45f) : Basis.Identity);
		k.Xf = new Transform3D(b, at + (toppled ? Vector3.Up * 0.24f : Vector3.Zero));
		BuildKit.Box(k, new Vector3(0, 0.46f, 0), new Vector3(0.44f, 0.04f, 0.42f), 1.2f);
		BuildKit.Box(k, new Vector3(0, 0.82f, 0.19f), new Vector3(0.44f, 0.5f, 0.04f), 1.2f);
		foreach (var (x, z) in new[] { (-0.19f, -0.18f), (0.19f, -0.18f), (-0.19f, 0.18f), (0.19f, 0.18f) })
			BuildKit.Box(k, new Vector3(x, 0.22f, z), new Vector3(0.04f, 0.44f, 0.04f), 1.2f);
		k.Xf = Transform3D.Identity;
		k.CommitTo(parent, "Chair", true);
	}

	private static void PlantPot(Node3D parent, Vector3 at, bool dead)
	{
		var k = new MeshKit();
		k.Mat(StationTextures.Flat("st_pot", new Color(0.55f, 0.3f, 0.2f), 0.8f, 0.2f));
		k.Color = Colors.White;
		k.Cylinder(at, at + Vector3.Up * 0.4f, 0.16f, 0.2f, 10, true);
		k.Mat(StationTextures.Flat(dead ? "st_leaf_dead" : "st_leaf", dead ? new Color(0.35f, 0.26f, 0.12f) : new Color(0.2f, 0.36f, 0.14f), 0.7f, 0.2f));
		var rng = new RandomNumberGenerator { Seed = 77 };
		for (int i = 0; i < 9; i++)
		{
			float a = Mathf.Tau * i / 9f;
			float lift = dead ? rng.RandfRange(-0.25f, 0.05f) : rng.RandfRange(0.35f, 0.7f);
			Vector3 tip = at + new Vector3(Mathf.Cos(a) * 0.35f, 0.4f + lift, Mathf.Sin(a) * 0.35f);
			k.Cylinder(at + Vector3.Up * 0.38f, tip, 0.012f, 0.006f, 4, false);
			k.Blob(tip, new Vector3(0.09f, 0.02f, 0.05f), i, 0.2f, false);
		}
		k.CommitTo(parent, "Plant", true);
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
		// the photo that came down: face-down on the floor, its glass in pieces round it
		var fallen = Group("PhotoFallen", 1, 2);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.22f, 0.16f, 0.1f);
		BuildKit.Box(k, new Vector3(-3.3f, 0.02f, D - 0.7f), new Vector3(0.46f, 0.03f, 0.36f), 1f, BuildKit.Face.None, new Basis(Vector3.Up, 0.4f));
		k.Mat(StationTextures.Flat("st_glass_shard", new Color(0.7f, 0.75f, 0.78f), 0.1f, 0.8f));
		k.Color = Colors.White;
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = new(-3.3f + rng.RandfRange(-0.5f, 0.5f), 0.004f, D - 0.7f + rng.RandfRange(-0.4f, 0.4f));
			k.Tri(p, p + new Vector3(rng.RandfRange(0.03f, 0.08f), 0, rng.RandfRange(-0.03f, 0.03f)), p + new Vector3(0.01f, 0, rng.RandfRange(0.03f, 0.07f)),
				Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
		}
		k.CommitTo(fallen, "Fallen", false);
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
		foreach (var (at, n) in new[]
		{
			(new Vector3(-2.2f, 2.2f, D - 0.075f), Vector3.Forward), (new Vector3(2.5f, 1.8f, D - 0.075f), Vector3.Forward),
			(new Vector3(W - 0.075f, 2.0f, 2.8f), Vector3.Left), (new Vector3(-W + 0.075f, 2.3f, -1.8f), Vector3.Right),
			(new Vector3(0.6f, 2.1f, -D + 0.075f), Vector3.Back),
		})
		{
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			float w = rng.RandfRange(0.6f, 1.1f), h = rng.RandfRange(0.7f, 1.2f);
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
		k.CommitTo(peel, "Strips", false);
		rot.CommitTo(peel, "Bare", false);

		var blood = Group("Handprints", 2, 4);
		foreach (var (at, n, spin) in new[]
		{
			(new Vector3(-W + 0.08f, 1.4f, 1.6f), Vector3.Right, 0.3f), (new Vector3(-W + 0.08f, 1.2f, 1.9f), Vector3.Right, -0.2f),
			(new Vector3(W - 0.08f, 1.5f, 1.3f), Vector3.Left, 0.1f), (new Vector3(1.8f, 1.6f, -D + 0.08f), Vector3.Back, 0.5f),
			(new Vector3(-1.4f, 1.1f, D - 0.08f), Vector3.Forward, -0.4f), (new Vector3(-1.6f, 1.35f, D - 0.08f), Vector3.Forward, -0.2f),
		})
			StationProps.Decal(blood, StationTextures.HandprintMat, at, n, new Vector2(0.24f, 0.24f), spin);

		var webs = Group("CornerWebs", 2, 4);
		foreach (var at in new[] { new Vector3(-W + 0.35f, H - 0.35f, D - 0.35f), new Vector3(W - 0.35f, H - 0.35f, -D + 0.35f), new Vector3(W - 0.35f, H - 0.35f, D - 0.35f) })
		{
			Vector3 n = (new Vector3(0, H * 0.5f, 0) - at).Normalized();
			StationProps.Decal(webs, StationTextures.WebMat, at, n, new Vector2(1.1f, 1.1f), rng.RandfRange(0, 3f));
		}
	}

	/// <summary>Stage 3+: industrial. Rusted steel plate bolted over the walls, pipes, chains, grating,
	/// steam, a blood trail along the floor to the desk.</summary>
	private void BuildIndustry(RandomNumberGenerator rng)
	{
		var ind = Group("Industry", 3, 4);
		var k = new MeshKit();
		k.Mat(StationTextures.RustPlateMat);
		k.Color = Colors.White;
		// plates over most of each wall, leaving the doorways
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
		// pipes along the ceiling edges and down the corners
		StationProps.Pipe(k, new Vector3(-W + 0.25f, H - 0.25f, -D + 0.25f), new Vector3(W - 0.25f, H - 0.25f, -D + 0.25f), 0.07f);
		StationProps.Pipe(k, new Vector3(-W + 0.25f, H - 0.35f, D - 0.25f), new Vector3(W - 0.25f, H - 0.35f, D - 0.25f), 0.09f);
		StationProps.Pipe(k, new Vector3(W - 0.25f, H - 0.3f, -D + 0.25f), new Vector3(W - 0.25f, H - 0.3f, D - 0.25f), 0.06f);
		StationProps.Pipe(k, new Vector3(-W + 0.25f, 0, -D + 0.25f), new Vector3(-W + 0.25f, H, -D + 0.25f), 0.08f);
		StationProps.Pipe(k, new Vector3(W - 0.25f, 0, D - 0.25f), new Vector3(W - 0.25f, H, D - 0.25f), 0.08f);
		// grating over the middle of the floor
		k.Mat(StationTextures.RustPlateMat);
		k.Color = new Color(0.5f, 0.45f, 0.4f);
		for (float x = -1.9f; x <= 1.9f; x += 0.19f)
			BuildKit.Box(k, new Vector3(x, 0.02f, -0.3f), new Vector3(0.03f, 0.03f, 3.4f), 2f);
		for (float z = -1.9f; z <= 1.3f; z += 0.6f)
			BuildKit.Box(k, new Vector3(0, 0.035f, z), new Vector3(3.9f, 0.02f, 0.04f), 2f);
		foreach (var top in new[] { new Vector3(-2.6f, H, -1.2f), new Vector3(2.2f, H, 1.4f), new Vector3(-0.8f, H, -2.9f) })
			StationProps.Chain(k, top, rng.RandfRange(0.9f, 1.8f));
		k.CommitTo(ind, "Industry", true);

		// the blood trail: drops, then drag marks, along the floor to behind the desk
		for (int i = 0; i < 9; i++)
		{
			float t = i / 8f;
			Vector3 at = new Vector3(-2.4f, 0.01f, -3.4f).Lerp(new Vector3(-0.6f, 0.01f, 3.6f), t) + new Vector3(rng.RandfRange(-0.15f, 0.15f), 0, 0);
			StationProps.Decal(ind, StationTextures.BloodMat, at, Vector3.Up, new Vector2(0.3f + 0.4f * t, 0.3f + 0.2f * t), rng.RandfRange(0, 3f));
		}
		// steam hissing from a burst joint, over by the basement door
		ind.AddChild(Steam(new Vector3(W - 0.3f, H - 0.3f, -D + 0.3f)));
		ind.AddChild(Steam(new Vector3(-W + 0.3f, H - 0.35f, D - 0.3f)));
	}

	/// <summary>Stage 4: flesh. Swollen growths out of the walls, the ceiling and the desk's corner.</summary>
	private void BuildFlesh(RandomNumberGenerator rng)
	{
		var flesh = Group("Flesh", 4, 4);
		var k = new MeshKit();
		foreach (var (at, n, size) in new[]
		{
			(new Vector3(-W + 0.1f, 2.4f, -3.2f), Vector3.Right, 0.9f), (new Vector3(-W + 0.1f, 0.6f, 3.4f), Vector3.Right, 0.7f),
			(new Vector3(W - 0.1f, 2.6f, 3.1f), Vector3.Left, 1.0f), (new Vector3(W - 0.1f, 1.0f, -2.2f), Vector3.Left, 0.6f),
			(new Vector3(-3.4f, H - 0.1f, 0.4f), Vector3.Down, 1.1f), (new Vector3(3.1f, H - 0.1f, -1.8f), Vector3.Down, 0.9f),
			(new Vector3(0.9f, H - 0.1f, 3.8f), Vector3.Down, 0.8f), (new Vector3(-2.2f, 2.6f, D - 0.1f), Vector3.Forward, 0.8f),
			(new Vector3(2.4f, 0.3f, D - 0.1f), Vector3.Forward, 0.6f), (new Vector3(-1.4f, 2.8f, -D + 0.1f), Vector3.Back, 0.9f),
		})
			StationProps.Growth(k, at, n, size, (int)(at.X * 31 + at.Z * 17));
		k.CommitTo(flesh, "Growths", true);
	}

	private void BuildLight()
	{
		var k = new MeshKit();
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.7f, 0.58f, 0.36f);
		k.Cylinder(new Vector3(0, H, 0), new Vector3(0, H - 0.5f, 0), 0.012f, 0.012f, 5, false);
		k.CommitTo(this, "LampRod", false);
		_globe = new MeshInstance3D
		{
			Name = "LampGlobe",
			Mesh = new SphereMesh { Radius = 0.18f, Height = 0.3f },
			Position = new Vector3(0, H - 0.62f, 0),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.9f, 0.75f), EmissionEnabled = true, Emission = new Color(1f, 0.82f, 0.6f), EmissionEnergyMultiplier = 1f,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_globe);
		_ceiling = new OmniLight3D
		{
			Name = "LobbyLamp", LightColor = new Color(1f, 0.82f, 0.62f), LightEnergy = 2.3f,
			OmniRange = 10.5f, OmniAttenuation = 1.1f, Position = new Vector3(0, H - 0.85f, 0), ShadowEnabled = true,
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
