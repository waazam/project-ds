using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// Act 24 (the owner's "Real Act 24": "the main spectacle of the entire game ... battle through a snow tunnelled
/// maze-like cavern to best the Wendigo ... a 5-15 minute encounter"):
/// <list type="number">
/// <item>Out of the lodge's splintered doorway, its tracks go off across the snow to a drift with a hole in it, black:
/// down into the snow. Into it, and they are in the maze (a save).</item>
/// <item>The maze (<see cref="SnowMazeCave"/>): quiet, the snow muffling everything; dead ends, loops that bring them back
/// round, one right way through. The wendigo hunts them in it (<see cref="WendigoHunter"/>): by what it hears and sees,
/// steered by a director that knows where they are and only ever hints.</item>
/// <item>At its heart the cavern, and the trenches cut through its floor: the old war's dead in them, helmets, rifles,
/// crates. Reaching it is a save. Somewhere in the trenches a crowbar; somewhere a long crate stencilled FLAME THROWER,
/// nailed shut: prised open, the flamethrower (a save).</item>
/// <item>Now it is the prey: burned, it hides, and comes back from behind. Four hits of the fire and it's finished
/// (Act 25: <see cref="Act25Ending"/>).</item>
/// </list>
/// The maze lies far under the lodge (its own pocket of the level); the hole at the surface hands the player down to
/// it behind a moment of black.
/// </summary>
public partial class Act24Maze : Node3D
{
	public static Act24Maze Instance { get; private set; }
	public const string FlagCrowbar = "act24_crowbar", FlagCrate = "act24_crate_open";
	/// <summary>Where the maze lies, from the lodge (lodge-local), and the hole in the drift at the surface.</summary>
	public static readonly Vector3 MazeOffset = new(-45f, -160f, 40f);
	public static readonly Vector3 HoleLocal = new(-7f, 0f, SkiLodge.Apothem + 26f);

	public SnowMazeCave Cave { get; private set; }
	public WendigoHunter Hunter { get; private set; }
	public bool InMaze { get; private set; }
	private UI.FrostEdge _frost;
	public bool InCavern { get; private set; }
	public Pickup Crowbar { get; private set; }
	public PickupInteractable CrateUse { get; private set; }
	public Vector3 CrateLocal { get; private set; }
	public Vector3 CrowbarLocal { get; private set; }
	public bool Finished { get; private set; }

	private SkiLodge _lodge;
	private Node3D _crate;
	private bool _handing;
	private Area3D _holeTrigger;
	private Vector3 _holeWorld;
	private AudioStreamPlayer _air;

	public static Act24Maze Build(SkiLodge lodge)
	{
		var a = new Act24Maze { Name = "Act24Maze" };
		a._lodge = lodge;
		lodge.AddChild(a);
		return a;
	}

	public override void _Ready()
	{
		Instance = this;
		// built during play (as the finale ends) off the main thread; loaded into, all at once behind the black
		Cave = new SnowMazeCave { Name = "SnowMaze", Position = MazeOffset, Async = S == null || S.Current < Checkpoint.Act24Maze };
		AddChild(Cave);
		Callable.From(BuildHole).CallDeferred();
		if (Cave.Built) CallDeferred(nameof(Furnish));
		else Cave.OnBuilt += () => CallDeferred(nameof(Furnish));
	}

	public override void _ExitTree() { if (Instance == this) Instance = null; }

	private static StoryManager S => StoryManager.Instance;

	// ------------------------------------------------------------------ the hole in the drift

	/// <summary>The drift with the hole in it (its tracks going in), on the snow in front of the lodge.</summary>
	private void BuildHole()
	{
		var at = HoleLocal;
		// the ground there (a ray straight down onto the snow)
		var from = _lodge.ToGlobal(at + Vector3.Up * 60f);
		var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 140f, 1);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
		_holeWorld = hit.Count > 0 ? (Vector3)hit["position"] : _lodge.ToGlobal(at);
		var hole = new Node3D { Name = "Hole" };
		AddChild(hole);
		hole.GlobalPosition = _holeWorld;
		hole.GlobalRotation = new Vector3(0, _lodge.GlobalRotation.Y, 0);   // (its mouth, local -Z, toward the lodge)
		var k = new MeshKit();
		k.Mat(WinterWoods.SoftSnow);
		k.Color = Colors.White;
		// the drift: heaped round and over the mouth
		k.Blob(new Vector3(0, 0.9f, 1.4f), new Vector3(3.6f, 1.9f, 2.6f), 2401, 0.25f, true, 1f);
		k.Blob(new Vector3(-2.6f, 0.5f, 0.6f), new Vector3(1.8f, 1.1f, 1.8f), 2402, 0.3f, true, 1f);
		k.Blob(new Vector3(2.5f, 0.6f, 0.8f), new Vector3(1.6f, 1.2f, 1.9f), 2403, 0.3f, true, 1f);
		k.CommitTo(hole, "Drift", true);
		// the mouth: black, going down into the snow
		var mouth = new MeshKit();
		mouth.Mat(new StandardMaterial3D { ResourceName = "hole_dark", AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
		mouth.Color = Colors.White;
		mouth.Blob(new Vector3(0, 0.75f, -0.2f), new Vector3(1.0f, 0.85f, 0.25f), 2404, 0.15f, false, 1f);
		mouth.Cylinder(new Vector3(0, 0.75f, -0.15f), new Vector3(0, -0.6f, 3f), 1.0f, 0.8f, 10, false);
		mouth.CommitTo(hole, "Mouth", false);
		// walking into it: down to the maze
		_holeTrigger = StoryBeat.MakeTrigger(hole, new BoxShape3D { Size = new Vector3(1.8f, 2f, 1.4f) }, new Vector3(0, 1f, 0.3f), p => _ = Cutscene.Run(this, ct => GoDown(p, ct), lockInput: true), "HoleTrigger");
		// the drift solid round the mouth (collision: a ring of boxes, the way in left open)
		var body = new StaticBody3D { Name = "DriftBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		foreach (var (c, s) in new[] { (new Vector3(-2.2f, 1f, 1.2f), new Vector3(2.4f, 2f, 3f)), (new Vector3(2.2f, 1f, 1.2f), new Vector3(2.4f, 2f, 3f)), (new Vector3(0, 1.6f, 1.6f), new Vector3(2f, 0.8f, 2.4f)) })
			body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		hole.AddChild(body);
		// its tracks: off the porch, round, and in
		Callable.From(LayTracks).CallDeferred();
	}

	private void LayTracks()
	{
		var prints = WinterWoods.Instance?.Prints;
		if (prints == null) return;
		var start = _lodge.ToGlobal(new Vector3(0.3f, 0.1f, SkiLodge.Apothem + 6f));
		var end = _holeWorld;
		var dir = (end - start) with { Y = 0 };
		float len = dir.Length();
		dir /= Mathf.Max(len, 0.01f);
		var side = new Vector3(dir.Z, 0, -dir.X);
		int n = Mathf.FloorToInt(len / 1.6f);
		for (int i = 0; i < n; i++)
		{
			var p = start + dir * (i * 1.6f) + side * (i % 2 == 0 ? 0.28f : -0.28f) + side * Mathf.Sin(i * 0.4f) * 0.6f;
			var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 20f, p + Vector3.Down * 40f, 1);
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			if (hit.Count == 0) continue;
			prints.Lay((Vector3)hit["position"], (Vector3)hit["normal"], dir, i % 2 == 0, true, 0.6f);
		}
	}

	/// <summary>In through the hole: a moment's black, and they are in the maze's way in, looking down it.</summary>
	private async Task GoDown(PlayerController player, CancellationToken ct)
	{
		if (_handing || InMaze) return;
		_handing = true;
		var fader = StoryBeat.Fader(this);
		AudioDirector.OneShot(this, "step_snow", 6, player.GlobalPosition, -2f, "Player", 3f, 0.05f);
		if (fader != null) await fader.Fade(1f, 0.6f, ct);
		while (!Cave.Built || Crowbar == null) await Cutscene.Wait(this, 0.1, ct);   // (still building: wait in the black)
		EnterMaze(player);
		await Cutscene.Wait(this, 0.3, ct);
		if (fader != null) await fader.Fade(0f, 1.0f, ct);
		_handing = false;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Maze);
		_ = StoryBeat.Caption(this, "So quiet down here.", 0.6f, 2.4f, 1f);
	}

	private void EnterMaze(PlayerController player)
	{
		var at = Cave.ToGlobal(Cave.ArriveAt);
		player.GlobalPosition = at;
		player.Velocity = Vector3.Zero;
		var look = Cave.ToGlobal(Cave.ArriveAt + Vector3.Back * 4f);
		player.CameraRig?.SnapBehind(Mathf.Atan2(-(look - at).X, -(look - at).Z));
		InMaze = true;
		StartHunt();
	}

	// ------------------------------------------------------------------ the hunt

	public void StartHunt()
	{
		if (Hunter != null) return;
		Hunter = new WendigoHunter { Name = "Hunter" };
		AddChild(Hunter);
		// it starts far off from them (the cavern's far side, or the maze's far corner)
		var L = Cave.Layout;
		var p = Cave.ToLocal(StoryBeat.Player(this)?.GlobalPosition ?? Cave.GlobalPosition);
		int far = 0;
		float fd = 0f;
		for (int v = 0; v < L.Nodes.Count; v++) { float d = L.Nodes[v].DistanceTo(p); if (d > fd && v != L.Cavern) { fd = d; far = v; } }
		Hunter.Setup(Cave, L.Nodes[far]);
		Hunter.Killed += () => _ = Cutscene.Run(this, ct => Defeated(ct), lockInput: true);
		Hunter.Hit += n => GD.Print($"[story] Act 24: the fire on it - {n} of {Hunter.HitsToKill}");
		if (S != null && S.Current >= Checkpoint.Act24Flamethrower) Hunter.ArmedPlayer();
		GD.Print($"[story] Act 24: the wendigo is in the maze (node {far})");
	}

	public override void _Process(double delta)
	{
		var player = StoryBeat.Player(this);
		if (player == null || Cave?.Layout == null) return;
		var l = Cave.ToLocal(player.GlobalPosition);
		bool inside = l.Y > -6f && l.Y < 14f && l.X > -10f && l.X < 100f && l.Z > -22f && l.Z < 100f;
		if (inside && !InMaze) { InMaze = true; StartHunt(); }
		InMaze = inside;
		// the cave's air: underground (no sky, no sun), a cold blue dark, everything muffled
		if (StoryBeat.Atmosphere(this) is { } atmo)
		{
			float dt = (float)delta;
			atmo.Underground = Mathf.MoveToward(atmo.Underground, inside ? 1f : 0f, dt);
			if (inside)
			{
				atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(new Color(0.03f, 0.05f, 0.08f), Mathf.Min(1f, dt * 2f));
				atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, 0.035f, dt * 0.05f);
			}
		}
		// frost at the screen's edges as it comes near (and as the hunt's menace rises); thawing as it goes
		if (inside && _frost == null) { _frost = new UI.FrostEdge { Name = "FrostEdge" }; AddChild(_frost); }
		if (_frost != null)
		{
			float near = 0f;
			if (inside && Hunter is { Body: not null, Dead: false } h)
				near = Mathf.Clamp(1f - h.Body.GlobalPosition.DistanceTo(player.GlobalPosition) / 16f, 0f, 1f) * 0.85f + h.Menace / 100f * 0.25f;
			_frost.Target = near;
		}
		// the cave's quiet: a low moan of air far off down the tunnels, the ice ticking (the snow muffles the rest)
		if (_air == null && ResourceLoader.Exists("res://assets/audio/ambient/snow_cave_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/snow_cave_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_air = new AudioStreamPlayer { Name = "CaveAir", Stream = wav, Bus = "Wind", VolumeDb = -80f };
			AddChild(_air);
		}
		if (_air != null)
		{
			_air.VolumeDb = Mathf.MoveToward(_air.VolumeDb, inside ? -6f : -80f, (float)delta * 20f);
			if (_air.VolumeDb > -79f && !_air.Playing) _air.Play();
			else if (_air.VolumeDb <= -79f && _air.Playing) _air.Stop();
		}
		if (!inside) return;
		bool cav = SnowMazeLayout.InCavern(l);
		if (cav && !InCavern && S != null && S.Current < Checkpoint.Act24Trenches)
		{
			GD.Print("[story] Act 24: the cavern, and the trenches");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Trenches);
			_ = StoryBeat.Caption(this, "Trenches. Down here.", 0.6f, 2.4f, 1f);
		}
		InCavern = cav;
	}

	// ------------------------------------------------------------------ the crowbar and the crate

	private void Furnish()
	{
		Vector3 crowbarAt = default, crateAt = default; float crateYaw = 0f;
		FurnitureKit.Clean(() => { Cave.DressTrenches(out crowbarAt, out crateAt, out crateYaw); return 0; });
		CrowbarLocal = crowbarAt;
		// (2026-10-10) the green flares along the way through; the hollows in the dead ends
		MazeExtras.BuildHollows(Cave);
		if (S == null || S.Current < Checkpoint.Act24Trenches) MazeExtras.PlaceFlares(Cave);
		CrateLocal = crateAt;
		bool crateOpen = S != null && (S.HasFlag(FlagCrate) || S.Current >= Checkpoint.Act24Flamethrower);
		// the crowbar
		Crowbar = new Pickup { Name = "Crowbar", Kind = ToolKind.Crowbar, Label = "Crowbar", TakenId = FlagCrowbar, UseSpot = false, SnapToSurface = false };
		Cave.AddChild(Crowbar);
		Crowbar.Position = crowbarAt + Vector3.Up * 0.03f;
		Crowbar.Rotation = new Vector3(0, 0.7f, 0);
		// the crate
		_crate = new Node3D { Name = "FlameCrate", Position = crateAt, Rotation = new Vector3(0, crateYaw, 0) };
		Cave.AddChild(_crate);
		FurnitureKit.Clean(() => FurnitureKit.Place(_crate, crateOpen ? "flame_crate_open" : "flame_crate", Transform3D.Identity, SnowMazeDressing.Roles, "Crate"));
		CrateUse = new PickupInteractable
		{
			Name = "CrateUse", PickRadius = 0.8f, MaxDistance = 2.4f, Position = new Vector3(0, 0.45f, 0),
			PromptFor = p => crateOpenNow() ? "" : p?.Inventory is { } inv && inv.HasTool(ToolKind.Crowbar) ? "Prise the crate open" : "Nailed shut. \"FLAME THROWER, PORTABLE\".",
			CanUse = p => !crateOpenNow() && p?.Inventory is { } inv && inv.HasTool(ToolKind.Crowbar),
		};
		bool crateOpenNow() => S != null && S.HasFlag(FlagCrate);
		CrateUse.Interacted += p => { if (!crateOpenNow()) _ = Cutscene.Run(this, ct => OpenCrate(p, ct), lockInput: true); };
		_crate.AddChild(CrateUse);
		// the respawns: at the way in, at the cavern's edge, by the crate
		Marker("Act24MazeMarker", Cave.ToGlobal(Cave.ArriveAt), Cave.GlobalRotation.Y, "respawn_Act24Maze");
		var door = Cave.Layout.EndAt(Cave.Layout.Solution[^2], Cave.Layout.Cavern);
		Marker("Act24TrenchMarker", Cave.ToGlobal(door + (SnowMazeLayout.CavernCentre - door).Normalized() * 1.5f + Vector3.Up * 0.1f), Cave.GlobalRotation.Y, "respawn_Act24Trenches");
		Marker("Act24FlameMarker", Cave.ToGlobal(crateAt + new Vector3(0, 0.1f, 0) + new Basis(Vector3.Up, crateYaw) * new Vector3(1.5f, 0, 0)), Cave.GlobalRotation.Y, "respawn_Act24Flamethrower");   // (along the trench from its end: across it was the wall)
		// a save in here: the level put them down before these markers were up (the maze is built after the lodge), so
		// they're put at the save's own now; the hunt starts as they're found in the maze
		if (S != null && S.Current is >= Checkpoint.Act24Maze and <= Checkpoint.Act24Flamethrower && !InMaze
			&& StoryBeat.Player(this) is { } pl && GetTree().GetFirstNodeInGroup($"respawn_{S.Current}") is Node3D m)
			pl.Teleport(m.GlobalPosition + Vector3.Up * 0.1f, m.GlobalRotation.Y);
		if (S != null && S.Current >= Checkpoint.Act24Flamethrower) EnsureFlamethrower(StoryBeat.Player(this));
	}

	private void Marker(string name, Vector3 at, float yaw, string group)
	{
		var m = new Node3D { Name = name };
		AddChild(m);
		m.GlobalPosition = at;
		m.GlobalRotation = new Vector3(0, yaw, 0);
		m.AddToGroup(group);
	}

	/// <summary>The lid prised up, nail by nail, and off; in the straw, the flamethrower: on with it (a save).</summary>
	private async Task OpenCrate(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 24: prising the crate open");
		await StoryBeat.PanTowards(this, player, _crate.GlobalPosition + Vector3.Up * 0.3f, 0.5f, ct, pitch: true);
		for (int i = 0; i < 3; i++)
		{
			AudioDirector.OneShot(this, "door_strain", 2, _crate.GlobalPosition, -6f + i * 2f, "Events", 3f, 0.08f);
			await Cutscene.Wait(this, 0.55, ct);
		}
		AudioDirector.OneShot(this, "wood_bump", 3, _crate.GlobalPosition, 0f, "Events", 3f, 0.05f);
		foreach (var c in _crate.GetChildren()) if (c is MeshInstance3D) c.QueueFree();
		FurnitureKit.Clean(() => FurnitureKit.Place(_crate, "flame_crate_open", Transform3D.Identity, SnowMazeDressing.Roles, "CrateOpen"));
		S?.SetFlag(FlagCrate);
		await Cutscene.Wait(this, 0.6, ct);
		EnsureFlamethrower(player);
		AudioDirector.OneShot(this, "flame_ready", 1, null, -4f, "Player");
		_ = StoryBeat.Caption(this, "A flamethrower. It still works.", 0.5f, 2.8f, 1f);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Flamethrower);
		Hunter?.ArmedPlayer();
		// and the scream from the tunnels: it heard
		await Cutscene.Wait(this, 1.2, ct);
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, _crate.GlobalPosition + new Vector3(0, 6f, 20f), 4f, "Unnatural", 20f, 0.02f);
	}

	public static void EnsureFlamethrower(PlayerController player)
	{
		if (player == null) return;
		player.Inventory?.TryPickup(ToolKind.Flamethrower);
		if (player.GetNodeOrNull<Flamethrower>("Flamethrower") == null) player.AddChild(new Flamethrower { Name = "Flamethrower" });
	}

	// ------------------------------------------------------------------ the end of it

	private async Task Defeated(CancellationToken ct)
	{
		if (Finished) return;
		Finished = true;
		var player = StoryBeat.Player(this);
		GD.Print("[story] Act 24: the fourth burn - it's finished");
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Finished);
		await Act25Ending.Play(this, player, Hunter, Cave, ct);
	}
}
