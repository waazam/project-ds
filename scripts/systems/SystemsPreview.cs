using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// Dev harness for systems_preview.tscn: assembles the real level (forest world,
/// player, ambience, HUD, stalker) WITHOUT GameFlow, so no checkpoint is ever
/// reached and nothing is saved, then exercises the systems layer with logged
/// PASS/FAIL checks and screenshots in test-output/systems/. Not used by the game.
///
/// Scenarios (user arg <c>--scenario=NAME</c>, default "main"):
/// - main: cutscene lock refcount, cancellable tween, fader caption + echo,
///   lazy StoryTrigger restore, stalker bridge/indoor rules, door prompts and
///   hold time, cabin exit direction, SoundEvent trigger, ambience players,
///   and the Act 1 open-trail grade (before/after screenshots along the trail).
/// - stairs: Act6ExtendedClimb set before load: the flight loads at twice its
///   base length, built exactly once.
/// - stairs11: Act11DialogueDone set before load: the flight loads at 220, built once.
/// - act6: the clearing dressing is pre-built at load but absent from the world, then
///   attached whole on reveal (screenshot of the clearing).
/// </summary>
public partial class SystemsPreview : Node3D
{
	private string _out;
	private int _fails;
	private string _scenario = "main";
	private PlayerController _player;
	private ScreenFader _fader;

	public override void _Ready()
	{
		_out = ProjectSettings.GlobalizePath("res://test-output/systems");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--scenario=")) _scenario = a.Substring("--scenario=".Length);
		Log($"scenario {_scenario}");

		// Flags go in before the level exists, exactly as a Continue would have them.
		var story = StoryManager.Instance;
		if (_scenario == "stairs") story?.SetFlag(StoryManager.Flag.Act6ExtendedClimb);
		if (_scenario == "stairs11") story?.SetFlag(StoryManager.Flag.Act11DialogueDone);

		AddChild(GD.Load<PackedScene>("res://scenes/levels/forest_world.tscn").Instantiate());
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<PlayerController>();
		AddChild(_player);
		AddChild(GD.Load<PackedScene>("res://scenes/audio/forest_ambience.tscn").Instantiate());
		AddChild(GD.Load<PackedScene>("res://scenes/audio/score.tscn").Instantiate());
		var hud = GD.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate();
		AddChild(hud);
		AddChild(GD.Load<PackedScene>("res://scenes/entities/stalker.tscn").Instantiate());
		_fader = hud.GetNode<ScreenFader>("ScreenFader");
		_fader.AddToGroup("screen_fader");
		_fader.SetBlack(false);
		_ = Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Phys(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private async Task Seconds(double s) { await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout); }
	private void Log(string s) => GD.Print("[systems] " + s);
	private void Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) _fails++; }
	private void Shot(string name) => GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");

	private async Task Run()
	{
		try
		{
			await Frames(20);
			if (GetTree().GetFirstNodeInGroup("player_spawn") is Node3D spawn)
				_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(spawn.GlobalBasis.Z.X, spawn.GlobalBasis.Z.Z));
			await Phys(5);

			if (_scenario == "main") await Main();
			else if (_scenario == "act6") await Act6Dressing();
			else await StairsOnly();
		}
		catch (System.Exception e)
		{
			GD.PushError("[systems] harness exception: " + e);
			_fails++;
		}
		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private StaircaseBuilder OriginalStairs()
		=> (GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node)?.GetParent() as StaircaseBuilder;

	private async Task StairsOnly()
	{
		await Frames(5);
		var stairs = OriginalStairs();
		Check(stairs != null, "original staircase found");
		if (stairs == null) return;
		int want = StairsState.StepsFor(StoryManager.Instance, stairs.BaseSteps);
		Log($"stairs: base {stairs.BaseSteps}, steps {stairs.Steps}, wanted {want}, builds {stairs.BuildCount}");
		Check(stairs.Steps == want, $"flight loads at StairsState length ({stairs.Steps} == {want})");
		Check(stairs.BuildCount == 1, $"flight built exactly once on load (builds {stairs.BuildCount})");
		if (_scenario == "stairs") Check(stairs.Steps == stairs.BaseSteps * 2, "Act6ExtendedClimb: twice the base");
		if (_scenario == "stairs11") Check(stairs.Steps == StairsState.TallSteps, "Act11DialogueDone: 220");
	}

	private async Task Act6Dressing()
	{
		await Frames(5);
		var stairs = OriginalStairs();
		Act6ClearingEvent act6 = null;
		foreach (var c in stairs.GetChildren()) if (c is Act6ClearingEvent a) act6 = a;
		var dressing = stairs.GetNodeOrNull<DeepZoneDressing>("Dressing");
		Check(act6 != null && dressing != null, "Act 6 clearing event and dressing found");
		if (act6 == null || dressing == null) return;
		int giantsBefore = 0;
		foreach (var n in GetTree().CurrentScene.FindChildren("Giant*", "Node3D", true, false)) giantsBefore++;
		var minisBefore = GetTree().GetNodesInGroup("act6_mini_stairs");
		Check(!act6.Revealed && act6.MiniStairCount == 0 && minisBefore.Count == 0 && giantsBefore == 0,
			$"before the reveal nothing of the clearing is in the world (minis {minisBefore.Count}, giants {giantsBefore})");
		var sw = System.Diagnostics.Stopwatch.StartNew();
		act6.DebugReveal();
		sw.Stop();
		await Frames(2);
		int giants = 0;
		foreach (var n in GetTree().CurrentScene.FindChildren("Giant*", "Node3D", true, false)) giants++;
		var minis = GetTree().GetNodesInGroup("act6_mini_stairs");
		Check(act6.Revealed && act6.MiniStairCount == 15 && minis.Count == 15 && giants == 9,
			$"reveal attaches the pre-built dressing (minis {minis.Count}, giants {giants}) in {sw.Elapsed.TotalMilliseconds:0.0} ms");
		Check(sw.Elapsed.TotalMilliseconds < 50, $"reveal is an attach, not a build ({sw.Elapsed.TotalMilliseconds:0.0} ms)");
		int builds = 0;
		foreach (var m in minis) if (m is StaircaseBuilder sb) builds += sb.BuildCount;
		Check(builds == 15, $"each mini stair built exactly once ({builds})");
		// A look at it from the trail side, once the menacing mood has come in.
		var pos = stairs.GlobalTransform * new Vector3(6f, 0f, 24f);
		if (GetTree().GetFirstNodeInGroup("terrain") is ForestTerrain t) pos.Y = t.HeightAt(pos.X, pos.Z);
		_player.Teleport(pos + Vector3.Up * 0.15f, YawToward(pos, stairs.GlobalPosition + Vector3.Up * 2f));
		await Seconds(4.0);
		Shot("act6_clearing_revealed");
	}

	private async Task Main()
	{
		// --- stairs at a fresh load: untouched, built once
		var stairs = OriginalStairs();
		Check(stairs != null && stairs.Steps == stairs.BaseSteps && stairs.BuildCount == 1,
			$"fresh load: flight at base length, built once ({stairs?.Steps}/{stairs?.BaseSteps}, builds {stairs?.BuildCount})");

		await StalkerRules();   // first: crossing the bridge latches it awake for good
		await Atmosphere();
		await CutsceneLocks();
		await CancelTween();
		await FaderCaptions();
		LazyTrigger();
		await InteractionProbe();
		await DoorPrompts();
		await CabinExit();
		SoundEventTrigger();
		AmbiencePlayers();
	}

	// ------------------------------------------------------------------ atmosphere

	private async Task Atmosphere()
	{
		var atmo = StoryBeat.Atmosphere(this);
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var cabin = StoryBeat.Cabin(this);
		Check(atmo != null && terrain != null, "atmosphere and terrain found");
		if (atmo == null || terrain == null) return;
		atmo.OpenSmoothing = 0.15f;   // the harness teleports; let the grade settle fast
		var bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		float bridgeAlong = -1f;
		if (bridge != null) terrain.TrailDistance(bridge.GlobalPosition.X, bridge.GlobalPosition.Z, out bridgeAlong);
		Log($"trail length {terrain.TrailLength:0}, bridge at {bridgeAlong:0} m along");

		(string name, Vector3 pos, float yaw)[] views =
		{
			("trailhead", TrailPose(terrain, 2f, out float y0), y0),
			("cabin", cabin != null ? cabin.WideApproachPoint : TrailPose(terrain, 10f, out _), cabin != null ? YawToward(cabin.WideApproachPoint, cabin.GlobalPosition) : 0f),
			("100m", TrailPose(terrain, 100f, out float y1), y1),
			("past_bridge", TrailPose(terrain, bridgeAlong > 0 ? bridgeAlong + 25f : 205f, out float y2), y2),
		};
		int i = 1;
		foreach (var (name, pos, yaw) in views)
		{
			_player.Teleport(pos, yaw);
			foreach (bool on in new[] { false, true })
			{
				atmo.OpenTrailGrade = on;
				await Seconds(1.5);
				Shot($"atmo_{i:00}_{name}_{(on ? "after" : "before")}");
				Log($"{name} grade {(on ? "on" : "off")}: open {atmo.OpenAmount:0.00}");
				if (name == "trailhead" && on) Check(atmo.OpenAmount > 0.9f, $"trailhead is fully open ({atmo.OpenAmount:0.00})");
				if (name == "past_bridge" && on) Check(atmo.OpenAmount < 0.05f, $"past the bridge the grade is gone ({atmo.OpenAmount:0.00})");
				if (name == "100m" && on) Check(atmo.OpenAmount > 0.1f && atmo.OpenAmount < 0.9f, $"100 m in is a blend ({atmo.OpenAmount:0.00})");
				if (!on) Check(atmo.OpenAmount < 0.05f, $"{name}: grade off reads 0 ({atmo.OpenAmount:0.00})");
			}
			i++;
		}
		// Never during a storm (RainVfx owns Storm from its intensity) nor under a scripted mood.
		_player.Teleport(views[0].pos, views[0].yaw);
		if (RainVfx.Instance is { } rain) rain.Intensity = 1f;
		await Seconds(1.0);
		Check(atmo.OpenAmount < 0.05f, $"storm cancels the open grade ({atmo.OpenAmount:0.00}, storm {atmo.Storm:0.00})");
		if (RainVfx.Instance is { } rain2) rain2.Intensity = 0f;
		await Seconds(1.0);
		Check(atmo.OpenAmount > 0.9f, $"grade returns after the storm ({atmo.OpenAmount:0.00})");
		atmo.SetMood(ForestAtmosphere.Mood.Night, 0.2f);
		await Seconds(1.0);
		Check(atmo.OpenAmount < 0.05f, $"a scripted mood cancels the open grade ({atmo.OpenAmount:0.00})");
		atmo.SetMood(ForestAtmosphere.Mood.Auto, 0.2f);
		await Seconds(1.0);
		atmo.OpenSmoothing = 2f;
		await Seconds(0.5);
	}

	private static Vector3 TrailPose(ForestTerrain terrain, float s, out float yaw)
	{
		var p = terrain.TrailPoint(s, out var tangent);
		yaw = Mathf.Atan2(-tangent.X, -tangent.Z);
		return p + Vector3.Up * 0.15f;
	}

	private static float YawToward(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(-d.X, -d.Z); }

	// ------------------------------------------------------------------ cutscene

	private async Task CutsceneLocks()
	{
		var input = _player.PlayerInput;
		Check(input.Enabled && Cutscene.InputLocks == 0, "starts unlocked");
		Cutscene.Lock(_player);
		Cutscene.Lock(_player);
		Check(!input.Enabled && Cutscene.InputLocks == 2, "two locks: input off, depth 2");
		Cutscene.Unlock(_player);
		Check(!input.Enabled, "one of two released: still locked");
		Cutscene.Unlock(_player);
		Check(input.Enabled && Cutscene.InputLocks == 0, "last released: input back");
		Cutscene.Unlock(_player);
		Check(input.Enabled && Cutscene.InputLocks == 0, "an extra unlock never goes negative");

		// A locking Run overlapping a manual lock: control returns only when both are done.
		int active = Cutscene.ActiveCount;   // long-lived loops (Act 7 whispers) already count
		var run = Cutscene.Run(this, async ct => { await Cutscene.Wait(this, 0.3, ct); }, lockInput: true);
		Check(Cutscene.ActiveCount == active + 1 && !input.Enabled, $"Run(lockInput): ActiveCount +1 ({Cutscene.ActiveCount}), input off");
		Cutscene.Lock(_player);
		await run;
		Check(Cutscene.ActiveCount == active && !input.Enabled, $"Run finished under a manual lock: count back ({Cutscene.ActiveCount}), input still off");
		Cutscene.Unlock(_player);
		Check(input.Enabled, "manual lock released: input back");
	}

	private async Task CancelTween()
	{
		var probe = new Node3D { Name = "TweenProbe" };
		AddChild(probe);
		var tween = probe.CreateTween();
		tween.TweenProperty(probe, "position:x", 10f, 30f);
		var run = Cutscene.Run(probe, ct => Cutscene.Tween(probe, tween, ct));
		await Frames(3);
		tween.Kill();   // killed elsewhere: the awaiting sequence must return, not hang
		var done = await Task.WhenAny(run, Task.Delay(2000));
		Check(done == run, "Cutscene.Tween returns when its tween is killed elsewhere");

		var tween2 = probe.CreateTween();
		tween2.TweenProperty(probe, "position:x", 20f, 30f);
		var run2 = Cutscene.Run(probe, ct => Cutscene.Tween(probe, tween2, ct));
		await Frames(3);
		probe.QueueFree();   // owner leaves: cancellation kills the tween
		await Frames(3);
		var done2 = await Task.WhenAny(run2, Task.Delay(2000));
		Check(done2 == run2 && !(IsInstanceValid(tween2) && tween2.IsValid()), "cancelled Cutscene.Tween kills its tween and returns");
	}

	// ------------------------------------------------------------------ fader

	private async Task FaderCaptions()
	{
		_fader.SetBlack(true);
		var first = _fader.ShowCaption("", "\"Come up and see.\"", 0.3f, 1.6f, 0.3f);
		await Seconds(0.4);
		var echo = _fader.ShowEcho("\"...come up and see...\"", 0.2f, 1.0f, 0.3f);
		await Seconds(0.5);
		Check(_fader.CaptionAlpha > 0.95f && _fader.EchoAlpha > 0.95f, $"caption and echo stand together ({_fader.CaptionAlpha:0.00}, {_fader.EchoAlpha:0.00})");
		Shot("fader_caption_echo");
		await echo;
		Check(_fader.CaptionAlpha > 0.95f, $"echo fading out leaves the caption up ({_fader.CaptionAlpha:0.00})");
		await first;
		Check(_fader.CaptionAlpha < 0.02f && _fader.EchoAlpha < 0.02f, "both lines down when their own calls end");

		// A newer caption supersedes an older one without the old one fading it out.
		var a = _fader.ShowCaption("", "first", 0.1f, 0.4f, 0.3f);
		await Seconds(0.2);
		var b = _fader.ShowCaption("", "second", 0.1f, 1.0f, 0.2f);
		await a;
		await Seconds(0.3);
		Check(_fader.CaptionAlpha > 0.95f, $"superseded caption never fades the newer one ({_fader.CaptionAlpha:0.00})");
		await b;

		// Cancellation: a caption owned by a freed node comes down at once.
		var owner = new Node { Name = "CaptionOwner" };
		AddChild(owner);
		_ = Cutscene.Run(owner, ct => StoryBeat.Caption(owner, "cancelled line", 0.1f, 5f, 0.3f, ct));
		await Seconds(0.4);
		Check(_fader.CaptionAlpha > 0.9f, "caption up before its owner leaves");
		owner.QueueFree();
		await Frames(4);
		Check(_fader.CaptionAlpha < 0.02f, $"caption taken down when its cutscene is cancelled ({_fader.CaptionAlpha:0.00})");
		_fader.SetBlack(false);
	}

	// ------------------------------------------------------------------ story trigger

	private partial class ProbeTrigger : StoryTrigger
	{
		public bool Played;
		protected override bool AlreadyHappened(StoryManager s) => true;
		protected override void Fire(PlayerController player) => Played = true;
		public void Poke(PlayerController p) => TryFire(p);
	}

	private void LazyTrigger()
	{
		// Entered before its deferred restore ran (not even in the tree yet): must still not replay.
		var t = new ProbeTrigger();
		t.Poke(_player);
		Check(t.Fired && !t.Played, "StoryTrigger consults AlreadyHappened on the first TryFire, before restore");
		t.Free();
	}

	// ------------------------------------------------------------------ stalker

	private async Task StalkerRules()
	{
		var stalker = GetTree().GetFirstNodeInGroup("stalker") as Stalker;
		var bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		Check(stalker != null && bridge != null, "stalker and bridge found");
		if (stalker == null || bridge == null) return;
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, 0f);
		await Frames(5);
		Check(stalker.Current == Stalker.State.Dormant && !stalker.Awake, $"dormant at the trailhead ({stalker.Current})");
		// Right at the bridge, still on the cabin side: not yet.
		_player.Teleport(bridge.GlobalPosition + new Vector3(0, 0.3f, 3f), 0f);
		await Frames(5);
		Check(!stalker.Awake, "still dormant on the cabin side of the bridge");
		// Past it: it wakes.
		_player.Teleport(bridge.GlobalPosition + new Vector3(0, 0.3f, -10f), 0f);
		await Frames(5);
		Check(stalker.Awake && stalker.Current != Stalker.State.Dormant, $"awake once past the bridge ({stalker.Current})");
		// Indoors: nothing to hide behind.
		ForestAmbienceManager.Instance.SetIndoor(this, true);
		await Frames(3);
		Check(stalker.Current == Stalker.State.Dormant, $"indoors it withdraws ({stalker.Current})");
		ForestAmbienceManager.Instance.SetIndoor(this, false);
		await Frames(3);
		Check(stalker.Current != Stalker.State.Dormant, $"back outside it resumes ({stalker.Current})");
	}

	// ------------------------------------------------------------------ interaction probe

	private async Task InteractionProbe()
	{
		// A big disabled pick sphere in front of a small enabled one, both on the view ray at eye height.
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, 0f);
		await Phys(2);
		var cam = _player.CameraRig.Camera;
		Vector3 eye = cam.GlobalPosition, dir = -cam.GlobalBasis.Z;
		var big = new Interactable { Name = "BigDisabled", Prompt = "big", PickRadius = 0.9f, Enabled = false };
		var small = new Interactable { Name = "SmallBehind", Prompt = "small", PickRadius = 0.12f };
		AddChild(big); AddChild(small);
		big.GlobalPosition = eye + dir * 1.3f;
		small.GlobalPosition = eye + dir * 2.4f;
		var interaction = _player.Interaction;
		await Phys(4);
		Check(interaction.Focused == small, $"a disabled pick sphere does not shadow the small one behind it (focused {interaction.Focused?.Name})");
		big.Enabled = true;
		await Phys(4);
		Check(interaction.Focused == big, $"enabled again, the near one is focused (focused {interaction.Focused?.Name})");
		big.Enabled = false;
		small.Visible = false;
		await Phys(4);
		Check(interaction.Focused == null, $"disabled in front, hidden behind: nothing focused (focused {interaction.Focused?.Name})");
		// Three disabled spheres in a row are looked past; a fourth is the limit.
		small.Visible = true;
		var extra1 = new Interactable { Name = "Extra1", PickRadius = 0.3f, Enabled = false };
		var extra2 = new Interactable { Name = "Extra2", PickRadius = 0.3f, Enabled = false };
		AddChild(extra1); AddChild(extra2);
		extra1.GlobalPosition = eye + dir * 0.7f;
		extra2.GlobalPosition = eye + dir * 2.0f;
		await Phys(4);
		Check(interaction.Focused == small, $"three disabled spheres are looked past (focused {interaction.Focused?.Name})");
		var extra3 = new Interactable { Name = "Extra3", PickRadius = 0.2f, Enabled = false };
		AddChild(extra3);
		extra3.GlobalPosition = eye + dir * 2.2f;
		await Phys(4);
		Check(interaction.Focused == null, $"a fourth disabled sphere is the re-cast limit (focused {interaction.Focused?.Name})");
		foreach (var n in new Node[] { big, small, extra1, extra2, extra3 }) n.QueueFree();
		await Phys(2);
	}

	// ------------------------------------------------------------------ door

	private async Task DoorPrompts()
	{
		var cabin = StoryBeat.Cabin(this);
		var door = cabin == null ? null : FindDoor(cabin);
		Check(cabin != null && door != null, "cabin door break event found");
		if (cabin == null || door == null) return;
		var inv = _player.Inventory;
		cabin.SetBoarded(true);
		door.Enabled = true;
		Check(door.GetPrompt(_player) == "The door is boarded shut." && !door.CanInteract(_player), $"no tool: '{door.GetPrompt(_player)}'");
		inv.TryPickup(ToolKind.Axe);
		await Frames(1);
		Check(door.GetPrompt(_player) == "Boarded shut. Find him first." && !door.CanInteract(_player), $"axe before the giant: '{door.GetPrompt(_player)}'");
		StoryManager.Instance.SetFlag(StoryManager.Flag.GiantEventDone);
		await Frames(1);
		Check(door.GetPrompt(_player) == "Chop the boards with the axe" && door.CanInteract(_player) && door.HoldSeconds == 0f,
			$"axe after the giant: '{door.GetPrompt(_player)}' hold {door.HoldSeconds}");
		inv.Consume(ToolKind.Axe);
		inv.TryPickup(ToolKind.Hammer);
		await Frames(1);
		Check(door.GetPrompt(_player) == "Pry the boards loose" && Mathf.IsEqualApprox(door.HoldSeconds, door.HammerSeconds),
			$"hammer: '{door.GetPrompt(_player)}' hold {door.HoldSeconds} (from the inventory change, not the prompt)");
		inv.Consume(ToolKind.Hammer);
		await Frames(1);
		Check(door.HoldSeconds == 0f, "hold time cleared when the hammer goes");
		cabin.SetBoarded(false);
		door.Enabled = false;
	}

	private static DoorBreakEvent FindDoor(Node cabin)
	{
		foreach (var n in cabin.GetChildren()) if (n is DoorBreakEvent d) return d;
		return null;
	}

	// ------------------------------------------------------------------ cabin exit

	private async Task CabinExit()
	{
		var cabin = StoryBeat.Cabin(this);
		var line = cabin?.GetNodeOrNull<CabinExitLine>("ExitLineTrigger");
		Check(cabin != null && line != null, "cabin exit line found");
		if (cabin == null || line == null) return;
		_player.Inventory.TryPickup(ToolKind.NewelPost);
		Vector3 Local(float z) => cabin.GlobalTransform * new Vector3(0, 0.1f, z);
		float hd = cabin.Depth * 0.5f;
		// Into the doorway volume, then back to the table: no dawn.
		_player.Teleport(Local(hd - 0.8f), 0f);
		await Phys(4);
		_player.Teleport(Local(0f), 0f);
		await Phys(4);
		Check(!line.Fired, "stepping back inside from the doorway does not fire");
		// Doorway, then out onto the porch: dawn.
		_player.Teleport(Local(hd - 0.8f), 0f);
		await Phys(4);
		_player.Teleport(Local(hd + 3.2f), 0f);   // clear of the doorway box (it reaches 1.5 m past the wall)
		await Phys(4);
		Check(line.Fired, "leaving toward the porch fires");
		await Seconds(0.3);
	}

	// ------------------------------------------------------------------ sound event, ambience

	private void SoundEventTrigger()
	{
		var bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		if (bridge == null) return;
		var ev = new SoundEvent { Name = "ProbeShot", SoundPath = "res://assets/audio/sfx/rifle_distant_01.wav", TargetPath = bridge.GetPath() };
		AddChild(ev);
		// Its trigger is built deferred; check on the next call.
		Callable.From(() =>
		{
			Check(bridge.GetNodeOrNull("ProbeShotTrigger") != null && !ev.Fired, "SoundEvent before Act 2: trigger standing, not fired");
			ev.QueueFree();
		}).CallDeferred();
	}

	private void AmbiencePlayers()
	{
		int playing = 0, total = 0;
		foreach (var n in GetTree().GetNodesInGroup("silence_zones")) _ = n;   // touch the group (no-op)
		foreach (var loop in FindLoops(GetTree().CurrentScene))
		{
			total++;
			var parent = loop.GetParent();
			bool on = parent is AudioStreamPlayer p2 ? p2.Playing : parent is AudioStreamPlayer3D p3 && p3.Playing;
			if (on) playing++;
		}
		Check(total > 0 && playing >= total / 2, $"typed ambience loops running ({playing}/{total} playing)");
	}

	private static System.Collections.Generic.IEnumerable<AmbienceLoop> FindLoops(Node root)
	{
		foreach (var n in root.FindChildren("*", "Node", true, false))
			if (n is AmbienceLoop l) yield return l;
	}
}
