using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Act 10's way out, replacing the maze: one bare concrete room with three doors (left, right,
/// ahead) and the closed one you came in by. Open any door and you are standing in the same
/// room again, the door behind you shut: the same lamp, the same stains, the same three doors.
/// After a random number of rooms (never the same twice) the room you step into has the stalker
/// hanging from the ceiling, facing you. Then (Dan, 2026-09-22) the lamp dies: the room is black
/// and all that can be seen of it are its two glowing eyes; the doors ahead slam and the
/// door behind you swings open onto a short hall with the bunker's own round front door at its
/// end and the night beyond. You turn and run for it, its clicking swelling at your back the
/// whole way, and burst out. (The walkie-talkie is not here any more: it lies on the CRT room's
/// console, dead until you are outside.)
///
/// Built once at level load at <see cref="MazeOffset"/> (the room is never rebuilt: going through
/// a door is a fade, a teleport back to the doorway and a count). Same surface as the old maze
/// for the flow: <see cref="Active"/>, <see cref="Exited"/>, <see cref="ExitReached"/>,
/// <see cref="StartLocal"/>, <see cref="ExitLocal"/>.
/// </summary>
public partial class BunkerRooms : Node3D
{
	public const float RoomW = 9f, RoomD = 7f, RoomH = 2.7f;
	/// <summary>The exit hall runs behind the entry door, along local +Z, to the round door.</summary>
	private const float HallZ0 = 0.3f, HallZ1 = 40f, HallHalfW = 1.5f;   // a long run (Dan, 2026-09-22: a hallway behind him, not the same room)
	private const float DoorW = 1.0f, DoorH = 2.05f;

	/// <summary>Just inside the entry door, facing into the room (interior-local).</summary>
	public static Vector3 StartLocal => MazeOffset + new Vector3(0, 0, -1.2f);
	/// <summary>The far door, ahead: where the compass points while the rooms repeat (interior-local).</summary>
	public static Vector3 LoopLocal => MazeOffset + new Vector3(0, 1f, -RoomD + 0.4f);
	/// <summary>The round door at the exit hall's end (interior-local): the way out once it has shown itself.</summary>
	public static Vector3 ExitLocal => MazeOffset + new Vector3(0, 1f, HallZ1 - 1.2f);

	/// <summary>The rooms are live: doors work, the lamp drifts.</summary>
	public bool Active { get; set; }
	public bool Exited { get; private set; }
	/// <summary>Rooms stepped into so far (the first room counts as 0).</summary>
	public int RoomsEntered { get; private set; }
	/// <summary>The stalker has shown itself: the room is black and the way back is open.</summary>
	public bool JumpscareDone { get; private set; }
	/// <summary>A door is being gone through (fade, teleport): the others ignore E meanwhile.</summary>
	public bool Busy { get; private set; }
	/// <summary>The three doors' interactables (left, right, ahead), for the autotest.</summary>
	public IReadOnlyList<Interactable> Doors => _doors;
	/// <summary>How many rooms in the stalker waits this time (random per run).</summary>
	public int ScareAt => _scareAt;
	/// <summary>For the autotest: every lamp is out; only the eyes are left.</summary>
	public bool BlackedOut { get; private set; }
	/// <summary>For the autotest: it is hanging there with its eyes lit.</summary>
	public bool EyesLit => _scareBody != null && IsInstanceValid(_scareBody);
	/// <summary>For the autotest: the door behind you stands open onto the exit hall.</summary>
	public bool EntryOpen { get; private set; }
	/// <summary>For the autotest: the shove-out beat has happened (there is no "RUN!" caption any more).</summary>
	public bool RunShown { get; private set; }
	/// <summary>0..1 how loud its clicking is right now (rises as you run for the round door).</summary>
	public float RattleGain { get; private set; }
	/// <summary>Just short of the round door: reaching it is the exit.</summary>
	public Vector3 ExitWorld => ToGlobal(new Vector3(0, 0, HallZ1 - 1.0f));

	/// <summary>Raised once, when the round door is reached.</summary>
	public event System.Action ExitReached;

	public const string ScaredFlag = "bunker_rooms_scared";

	private readonly List<Interactable> _doors = new();
	private readonly RandomNumberGenerator _rng = new();
	private int _scareAt;
	private StaticBody3D _body;
	private StandardMaterial3D _lampMat;
	private OmniLight3D _lamp, _fill, _hallFill;
	private CollisionShape3D _entryBlock;
	private Node3D _entryLeaf;
	private StalkerBody _scareBody;
	private AudioStreamPlayer3D _rattle;
	private AmbienceLoop _rattleLoop;
	private bool _burstOn = true;
	private float _burstTimer = 1.2f, _burstDb;
	private System.Collections.Generic.List<AudioStream> _rattleTakes = new();
	private ProjectDS.Audio.RecentPicker _rattlePicker;
	private double _joltUntil = -1;
	private readonly Color _lampA = new(0.72f, 0.07f, 0.05f), _lampB = new(0.42f, 0.12f, 0.48f);
	private float _lampPhase;

	public override void _Ready()
	{
		Position = MazeOffset;
		_rng.Randomize();
		_scareAt = _rng.RandiRange(2, 4);
		_lampPhase = _rng.RandfRange(0f, Mathf.Tau);
		_body = new StaticBody3D { Name = "RoomsBody", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "stone");
		AddChild(_body);
		BuildRoom();
		BuildExitHall();
		// A Continue that already met it: the room is black, the eyes wait, the way back is open.
		if (StoryManager.Instance is { } s && s.HasFlag(ScaredFlag)) ApplyScared(instant: true);
	}

	/// <summary>Restore: the exit was already reached.</summary>
	public void MarkExited() { Exited = true; Active = false; }

	// ------------------------------------------------------------------ per frame

	public void Tick(double clock, Vector3? playerLocal, Node3D player)
	{
		if (_lampMat == null) return;
		float t = (float)clock;
		float u = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Tau / 34f + _lampPhase);
		Color c = _lampA.Lerp(_lampB, u);
		float e = 1.6f;
		float sag = Mathf.Sin(t * 0.41f + _lampPhase) * Mathf.Sin(t * 0.27f + _lampPhase * 1.7f);
		if (sag > 0.8f) e *= Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0.8f, 0.95f, sag));
		if (clock < _joltUntil) { e = GD.Randf() < 0.55f ? 0.05f : 2.4f; c = GD.Randf() < 0.7f ? _lampA : Colors.White; }
		if (BlackedOut) { e = 0f; c = Colors.Black; }
		_lampMat.Emission = c;
		_lampMat.AlbedoColor = c;
		_lampMat.EmissionEnergyMultiplier = 1.4f * e / 1.6f;
		_lamp.LightColor = c;
		_lamp.LightEnergy = e;

		if (!JumpscareDone || Exited || playerLocal is not { } pl) return;
		Vector3 local = pl - MazeOffset;
		// The run: from the room's doorway to the round door, its clicking behind you comes up the whole way.
		float progress = Mathf.Clamp((local.Z + 1.2f) / (HallZ1 - 1.0f + 1.2f), 0f, 1f);
		RattleGain = Mathf.MoveToward(RattleGain, Mathf.Max(0.12f, progress), (float)GetProcessDeltaTime() * 0.8f);
		if (_rattleLoop != null)
		{
			// In bursts, not a steady drone (Dan, 2026-09-22): short silences between, each burst a little louder or softer.
			_burstTimer -= (float)GetProcessDeltaTime();
			if (_burstTimer <= 0f)
			{
				_burstOn = !_burstOn;
				_burstTimer = _burstOn ? _rng.RandfRange(1.0f, 2.6f) : _rng.RandfRange(0.4f, 1.4f);
				_burstDb = _rng.RandfRange(-2.5f, 1.5f);
				// One take only (Dan, 2026-09-22: the other beats read as hooves); speed follows the run, gaps from the gating.
			}
			_rattleLoop.Gain = Mathf.MoveToward(_rattleLoop.Gain, _burstOn ? RattleGain : RattleGain * 0.15f, (float)GetProcessDeltaTime() * 2.5f);
			_rattleLoop.BaseVolumeDb = Mathf.Lerp(-6f, 8f, RattleGain) + _burstDb;
			_rattle.PitchScale = Mathf.Lerp(0.74f, 1f, RattleGain);   // quicker as it closes, never above the take
		}
		if (Active && ShovedOut && local.Z > 0.5f) TickChase(local, player, (float)GetProcessDeltaTime());
		if (Active && local.Z > HallZ1 - 1.6f && Mathf.Abs(local.X) < HallHalfW) Exit();
	}

	private void Exit()
	{
		Exited = true;
		Active = false;
		if (_rattle != null && IsInstanceValid(_rattle))
		{
			var tw = _rattle.CreateTween();
			tw.TweenProperty(_rattle, "volume_db", -40f, 0.6f);
			tw.TweenCallback(Callable.From(_rattle.QueueFree));
			_rattle = null; _rattleLoop = null;
		}
		GD.Print("[story] Act 10: out through the round door");
		ExitReached?.Invoke();
	}

	// ------------------------------------------------------------------ the doors

	private void OpenDoor(int index, PlayerController player)
	{
		if (!Active || Busy || Exited || JumpscareDone || player == null) return;
		Busy = true;
		_ = Cutscene.Run(this, ct => Through(index, player, ct), lockInput: true, freezeBody: true);
	}

	private async Task Through(int index, PlayerController player, CancellationToken ct)
	{
		try
		{
			var fader = StoryBeat.Fader(this);
			if (index >= 0 && index < _doorLeafLocal.Count)
				BunkerKit.OneShot(this, $"res://assets/audio/sfx/steel_door_open_{(int)(GD.Randi() % 2) + 1:00}.wav", _doorLeafLocal[index] + Vector3.Up * 1.2f, "Events", -2f, 1f, 3f, 25f);   // steel, never wood
			if (fader != null) await fader.Fade(1f, 0.35f);
			ct.ThrowIfCancellationRequested();
			RoomsEntered++;
			player.Teleport(ToGlobal(StartLocal - MazeOffset), 0f);
			await Cutscene.Frame(this, ct);
			if (fader != null) await fader.Fade(0f, 0.35f);
			if (RoomsEntered >= _scareAt) await Jumpscare(player, ct);
		}
		finally { Busy = false; }
	}

	/// <summary>
	/// It is hanging from the ceiling in the middle of the room, facing you. The lamp jolts and
	/// dies: black, and its two eyes. The doors ahead slam; the one behind you swings open
	/// onto the hall and the moonlight at its end. Control comes back with you facing it.
	/// </summary>
	private async Task Jumpscare(PlayerController player, CancellationToken ct)
	{
		JumpscareDone = true;
		StoryManager.Instance?.SetFlag(ScaredFlag);
		SpawnHanging(player);
		_joltUntil = _clockNow + 0.5;
		ScareVoice(this, _scareBody.Position + Vector3.Up * 1.7f);
		GD.Print($"[story] Act 10: it is in the room (room {RoomsEntered})");
		await Cutscene.Wait(this, 0.5, ct);

		// The lamp dies. Only the eyes.
		Blackout();
		_scareBody.GlowEyes(new Color(1f, 0.16f, 0.05f), 7f);
		EyesShown = true;
		await Cutscene.Wait(this, 0.6, ct);

		// No line (Dan, 2026-09-22): the slam behind the eyes, the way out opening at your back, and the shove.
		SlamDoors();
		await Cutscene.Wait(this, 0.35, ct);
		OpenEntry(instant: false);
		await Cutscene.Wait(this, 0.4, ct);
		// Forced out: turned to the open door and shoved through it into the hall, then the door slams
		// shut at their back. The only way is down the hall.
		await ShoveOut(player, ct);
		RunShown = true;   // kept for tests: the "run" beat happened (the shove), nothing was printed
		StartRattle();
		StartChase();
	}

	// ------------------------------------------------------------------ the shove, and the chase

	/// <summary>For the autotest: the eyes were lit in the black room (they go with the door).</summary>
	public bool EyesShown { get; private set; }
	/// <summary>For the autotest: they were pushed out of the room and the door shut behind them.</summary>
	public bool ShovedOut { get; private set; }
	/// <summary>For the autotest: they have turned round in the hall (and found nothing there).</summary>
	public bool LookedBack { get; private set; }
	/// <summary>For the autotest: the flash in front of them has happened (once only, saved).</summary>
	public bool FlashFired { get; private set; }
	/// <summary>For the autotest: the flash body is in the hall right now.</summary>
	public bool FlashShowing => _flashBody != null && IsInstanceValid(_flashBody);
	/// <summary>For the autotest: whether they are looking back up the hall right now.</summary>
	public bool LookingBack { get; private set; }

	public const string FlashedFlag = "bunker_rooms_flashed";
	private const float BackCos = 0.35f;    // more than ~70 degrees off the way out: turned back toward the door
	private const float GlanceCos = 0.7f;   // more than ~45 degrees off the way out: a glance (nothing there, steps hold)
	private StalkerBody _flashBody;
	private double _stepTimer = 0.3, _snarlTimer = 4.0;
	private int _stepFoot;
	private bool _flashRunning;

	/// <summary>Turns them to the doorway and pushes them through it; the door slams at their back.</summary>
	private async Task ShoveOut(PlayerController player, CancellationToken ct)
	{
		Vector3 hallDir = GlobalBasis * Vector3.Back;   // the hall runs along local +Z
		float yaw = Mathf.Atan2(-hallDir.X, -hallDir.Z);
		Vector3 start = ToGlobal(new Vector3(0, 0, -0.6f));
		player.Teleport(new Vector3(start.X, player.GlobalPosition.Y, start.Z), yaw);
		BunkerKit.OneShot(this, "res://assets/audio/sfx/breath_in_03.wav", new Vector3(0, 1.5f, -0.6f), "Events", -4f, 0.9f, 3f, 20f);
		player.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 0.1f, -0.12f));   // a stumble
		Vector3 to = ToGlobal(new Vector3(0, 0, 3.2f));
		to.Y = player.GlobalPosition.Y;
		var tw = player.CreateTween();
		tw.TweenProperty(player, "global_position", to, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		await Cutscene.Wait(this, 0.5, ct);
		CloseEntry();
		ShovedOut = true;
		await Cutscene.Wait(this, 0.25, ct);
	}

	/// <summary>The door behind them slams shut: the leaf back on its hinge, its collider back, the room and its eyes gone.</summary>
	private void CloseEntry()
	{
		EntryOpen = false;
		if (_entryLeaf != null) _entryLeaf.Rotation = Vector3.Zero;
		if (_entryBlock == null) _entryBlock = AddBox(new Vector3(0, DoorH * 0.5f, 0.05f), new Vector3(DoorW + 0.1f, DoorH, 0.12f));
		int take = (int)(GD.Randi() % 2) + 1;
		BunkerKit.OneShot(this, $"res://assets/audio/sfx/steel_door_slam_{take:00}.wav", new Vector3(0, 1.0f, 0.1f), "Events", 5f, 1f, 5f, 40f);
		if (_scareBody != null && IsInstanceValid(_scareBody)) { _scareBody.QueueFree(); _scareBody = null; }
		if (GetTree().GetFirstNodeInGroup("player") is PlayerController p)
			p.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 0.05f, 0.04f));
	}

	private void StartChase()
	{
		_stepTimer = 0.4;
		_snarlTimer = GD.RandRange(3.0, 6.0);
	}

	/// <summary>
	/// The chase, per frame: heavy running footfalls a few metres behind them, a snarl now and then.
	/// A glance to the side (past ~45 degrees off the way out) shows nothing and the footfalls hold while
	/// they look; but the moment they turn back toward the door at all (past ~70 degrees off the way out),
	/// once, it is right there in the direction they are looking, 2.5 m off, for a third of a second
	/// (Dan, 2026-09-22: "looking back at all and getting the scare").
	/// </summary>
	private void TickChase(Vector3 local, Node3D player, float dt)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		Vector3 fwd = GlobalBasis.Inverse() * (-cam.GlobalBasis.Z);
		bool back = fwd.Z < BackCos;      // turned back toward the room's door at all
		bool glance = fwd.Z < GlanceCos;  // off the way out: nothing to see, the steps hold
		LookingBack = back;
		if (back && !LookedBack) { LookedBack = true; GD.Print("[story] Act 10: they looked back"); }
		if (back && !FlashFired && !_flashRunning)
			_ = Cutscene.Run(this, ct => Flash(local, fwd, player, ct));
		if (glance || _flashRunning) return;   // it is never seen behind (after the flash), and it holds its breath while they look

		_stepTimer -= dt;
		if (_stepTimer <= 0)
		{
			_stepTimer = GD.RandRange(0.26, 0.34);
			_stepFoot = (_stepFoot + 1) % 6;
			Vector3 at = new(Mathf.Clamp(local.X, -1f, 1f) + (_stepFoot % 2 == 0 ? -0.25f : 0.25f), 0.1f, local.Z - 4f);
			BunkerKit.OneShot(this, $"res://assets/audio/sfx/step_stone_{_stepFoot + 1:00}.wav", at, "Unnatural", 2f, (float)GD.RandRange(0.78, 0.86), 3f, 30f);
		}
		_snarlTimer -= dt;
		if (_snarlTimer <= 0)
		{
			_snarlTimer = GD.RandRange(4.0, 8.0);
			int take = new[] { 1, 2, 4 }[(int)(GD.Randi() % 3)];
			BunkerKit.OneShot(this, $"res://assets/audio/sfx/creature_snarl_{take:00}.wav", new Vector3(local.X, 1.4f, local.Z - 3.5f), "Unnatural", 0f, 0.9f, 4f, 30f);
		}
	}

	/// <summary>Once: it is standing 2.5 m off in the direction they turned to look, facing them, eyes lit, for a third of a second.</summary>
	private async Task Flash(Vector3 local, Vector3 lookLocal, Node3D player, CancellationToken ct)
	{
		_flashRunning = true;
		FlashFired = true;
		StoryManager.Instance?.SetFlag(FlashedFlag);
		try
		{
			Vector3 dir = new(lookLocal.X, 0f, lookLocal.Z);
			dir = dir.LengthSquared() < 0.01f ? Vector3.Back : dir.Normalized();
			Vector3 at = local + dir * 2.5f;
			at = new Vector3(Mathf.Clamp(at.X, -HallHalfW + 0.5f, HallHalfW - 0.5f), 0f, Mathf.Max(HallZ0 + 0.4f, at.Z));
			_flashBody = new StalkerBody { Name = "HallFlash", Seed = 2077, Size = 1.08f };
			AddChild(_flashBody);
			_flashBody.Position = at;
			Vector3 to = local - at;
			_flashBody.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
			_flashBody.GlowEyes(new Color(1f, 0.16f, 0.05f), 7f);
			ScareVoice(this, at + Vector3.Up * 1.7f);
			GD.Print("[story] Act 10: it is in front of them");
			await Cutscene.Wait(this, 0.35, ct);
		}
		finally
		{
			if (_flashBody != null && IsInstanceValid(_flashBody)) _flashBody.QueueFree();
			_flashBody = null;
			_flashRunning = false;
			_stepTimer = 0.6;
		}
	}

	/// <summary>Spawns it upside down under the lamp, facing the doorway (or the player).</summary>
	private void SpawnHanging(Node3D player)
	{
		if (EyesLit) return;
		_scareBody = new StalkerBody { Name = "RoomScare", Seed = 2077, Size = 1.08f };
		AddChild(_scareBody);
		Vector3 at = new(0f, RoomH - 0.35f, -4.2f);
		Vector3 to = (player != null ? ToLocal(player.GlobalPosition) : (StartLocal - MazeOffset)) - at;
		_scareBody.Position = at;
		_scareBody.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), Mathf.Pi);
	}

	private void Blackout()
	{
		BlackedOut = true;
		_joltUntil = -1;
		if (_fill != null) _fill.LightEnergy = 0f;
		if (_hallFill != null) _hallFill.LightEnergy = 0f;
		foreach (var d in _doors) d.Enabled = false;   // the doors ahead are shut for good
	}

	private void SlamDoors()
	{
		int take = (int)(GD.Randi() % 2) + 1;
		BunkerKit.OneShot(this, $"res://assets/audio/sfx/steel_door_slam_{take:00}.wav", new Vector3(0, 1.0f, -RoomD), "Events", 4f, 1f, 5f, 40f);
		BunkerKit.OneShot(this, $"res://assets/audio/sfx/steel_door_slam_{3 - take:00}.wav", new Vector3(-RoomW * 0.5f, 1.0f, -3.5f), "Events", 0f, 0.94f, 4f, 30f);
		if (GetTree().GetFirstNodeInGroup("player") is PlayerController p)
			p.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 0.06f, -0.03f));
	}

	/// <summary>The door behind you swings open: its collider goes, the leaf turns on its hinge.</summary>
	private void OpenEntry(bool instant)
	{
		if (EntryOpen) return;
		EntryOpen = true;
		if (_entryBlock != null) { _entryBlock.QueueFree(); _entryBlock = null; }
		if (_entryLeaf == null) return;
		float open = Mathf.DegToRad(112f);   // swings away from you, into the hall
		if (instant) { _entryLeaf.Rotation = new Vector3(0, open, 0); return; }
		BunkerKit.OneShot(this, $"res://assets/audio/sfx/steel_door_open_{(int)(GD.Randi() % 2) + 1:00}.wav", new Vector3(0, 1.2f, 0), "Events", -1f, 0.95f, 3f, 25f);   // steel, never wood
		var tw = _entryLeaf.CreateTween();
		tw.TweenProperty(_entryLeaf, "rotation:y", open, 0.9f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
	}

	/// <summary>Its clicking, from where it hangs, rising as you run (Tick drives the level).</summary>
	private void StartRattle()
	{
		if (_rattle != null) return;
		// The original take only (Dan, 2026-09-22: the rhythm variants read as horse hooves).
		const string path = "res://assets/audio/sfx/creature_rattle_loop_01.wav";
		if (!ResourceLoader.Exists(path)) return;
		_rattle = new AudioStreamPlayer3D { Name = "Rattle", Bus = "Unnatural", UnitSize = 8f, MaxDistance = 60f, PitchScale = 0.74f, Position = new Vector3(0, RoomH - 0.6f, -4.2f) };
		AddChild(_rattle);
		_rattleLoop = new AmbienceLoop { Name = "Loop", StreamPath = path, BaseVolumeDb = -6f, Gain = 0.12f };
		_rattle.AddChild(_rattleLoop);
		RattleGain = 0.12f;
	}

	/// <summary>Restore (or a late Continue): the state the scare leaves, all at once.</summary>
	private void ApplyScared(bool instant)
	{
		JumpscareDone = true;
		Blackout();
		// Already shoved out in the saved story: the door is shut, the room is empty, the chase is on
		// (the flow lands them in the hall). The flash never replays.
		ShovedOut = true;
		EntryOpen = false;
		FlashFired = StoryManager.Instance?.HasFlag(FlashedFlag) ?? false;
		StartRattle();
		StartChase();
	}

	private double _clockNow;
	public override void _Process(double delta) => _clockNow += delta;

	/// <summary>
	/// The jumpscare's sound (the hallway's and the rooms'): the old walkie-talkie burst, the super loud
	/// static the radio used to make (Dan, 2026-09-22: keep that as the scare), played flat at the ear on
	/// the Radio bus, with the creature jumpscare take under it at the mouth for the roar (kept, Dan
	/// 2026-09-22). A view kick lands with the hit. No "come up and see" in here (that voice belongs only
	/// where staircases stand).
	/// </summary>
	public static void ScareVoice(Node3D parent, Vector3 localMouth)
	{
		int burst = (int)(GD.Randi() % 2) + 1;
		string burstPath = $"res://assets/audio/sfx/radio_burst_{burst:00}.wav";
		if (ResourceLoader.Exists(burstPath))
		{
			var burstPlayer = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(burstPath), Bus = "Radio", VolumeDb = 8f };
			Cutscene.SceneRoot(parent).AddChild(burstPlayer);
			burstPlayer.Finished += burstPlayer.QueueFree;
			burstPlayer.Play();
		}
		int take = (int)(GD.Randi() % 3) + 1;
		BunkerKit.OneShot(parent, $"res://assets/audio/sfx/creature_jumpscare_{take:00}.wav", localMouth, "Unnatural", 0f, GD.Randf() < 0.5f ? 0.94f : 1f, 8f, 60f);
		if (parent.GetTree().GetFirstNodeInGroup("player") is PlayerController p)
			p.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 0.12f, 0.08f + GD.Randf() * 0.05f));
	}

	// ------------------------------------------------------------------ building

	private readonly List<Vector3> _doorLeafLocal = new();

	private CollisionShape3D AddBox(Vector3 c, Vector3 s)
	{
		var shape = new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } };
		_body.AddChild(shape);
		return shape;
	}

	private void BuildRoom()
	{
		float hw = RoomW * 0.5f, hz = RoomD * 0.5f;
		var k = new MeshKit();
		k.Mat(BunkerTextures.RoomWallMat);
		k.Color = Colors.White;
		// Walls (0.3 thick, faces inward), floor, ceiling with two cast beams. The entry wall (+Z) has
		// a real doorway in it: the way out, once the door behind you opens.
		k.Box(new Vector3(-hw - 0.15f, RoomH * 0.5f, -hz), new Vector3(0.3f, RoomH, RoomD + 0.6f));
		k.Box(new Vector3(hw + 0.15f, RoomH * 0.5f, -hz), new Vector3(0.3f, RoomH, RoomD + 0.6f));
		k.Box(new Vector3(0, RoomH * 0.5f, -RoomD - 0.15f), new Vector3(RoomW, RoomH, 0.3f));
		float jamb = (RoomW - DoorW) * 0.5f;
		foreach (float sx in new[] { -1f, 1f })
			k.Box(new Vector3(sx * (DoorW * 0.5f + jamb * 0.5f), RoomH * 0.5f, 0.15f), new Vector3(jamb, RoomH, 0.3f));
		k.Box(new Vector3(0, DoorH + (RoomH - DoorH) * 0.5f, 0.15f), new Vector3(DoorW + 0.02f, RoomH - DoorH, 0.3f));
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		k.Box(new Vector3(0, RoomH + 0.05f, -hz), new Vector3(RoomW, 0.1f, RoomD));
		k.Color = new Color(0.92f, 0.92f, 0.9f);
		foreach (float z in new[] { -2.3f, -4.7f })
			k.Box(new Vector3(0, RoomH - 0.14f, z), new Vector3(RoomW, 0.28f, 0.32f));
		k.CommitTo(this, "RoomShell");
		var f = new MeshKit();
		f.Mat(BunkerTextures.RoomFloorMat);
		f.Color = Colors.White;
		f.Box(new Vector3(0, -0.05f, -hz), new Vector3(RoomW, 0.1f, RoomD));
		f.CommitTo(this, "RoomFloor");
		AddBox(new Vector3(-hw - 0.15f, RoomH * 0.5f, -hz), new Vector3(0.3f, RoomH, RoomD + 0.6f));
		AddBox(new Vector3(hw + 0.15f, RoomH * 0.5f, -hz), new Vector3(0.3f, RoomH, RoomD + 0.6f));
		AddBox(new Vector3(0, RoomH * 0.5f, -RoomD - 0.15f), new Vector3(RoomW, RoomH, 0.3f));
		foreach (float sx in new[] { -1f, 1f })
			AddBox(new Vector3(sx * (DoorW * 0.5f + jamb * 0.5f), RoomH * 0.5f, 0.15f), new Vector3(jamb, RoomH, 0.3f));
		AddBox(new Vector3(0, DoorH + (RoomH - DoorH) * 0.5f, 0.15f), new Vector3(DoorW + 0.02f, RoomH - DoorH, 0.3f));
		// The shut entry door itself, until it opens.
		_entryBlock = AddBox(new Vector3(0, DoorH * 0.5f, 0.05f), new Vector3(DoorW + 0.1f, DoorH, 0.12f));
		AddBox(new Vector3(0, -0.05f, -hz), new Vector3(RoomW, 0.1f, RoomD));
		AddBox(new Vector3(0, RoomH + 0.05f, -hz), new Vector3(RoomW, 0.1f, RoomD));

		// The doors: the one behind (shut, no handle to it; it opens on its hinge later), and three that open.
		_entryLeaf = Door(new Vector3(0, 0, 0), Vector3.Forward, -1);   // entry, in the +Z wall, faces into the room (-Z)
		Door(new Vector3(-hw, 0, -3.5f), Vector3.Right, 0);            // left wall
		Door(new Vector3(hw, 0, -3.5f), Vector3.Left, 1);              // right wall
		Door(new Vector3(0, 0, -RoomD), Vector3.Back, 2);              // far wall

		// The lamp, dead centre, its cage and cord; a dim fill so the corners read.
		var metal = new MeshKit();
		metal.Mat(ProcTextures.MetalMat);
		var xf = new Transform3D(Basis.Identity, new Vector3(0, RoomH - 0.02f, -hz));
		BunkerKit.CagedLamp(metal, xf);
		metal.CommitTo(this, "LampCage", false);
		_lampMat = BunkerTextures.NewLampGlass(_lampA, 1.4f);
		BunkerKit.LampGlass(this, xf, _lampMat, "LampGlass");
		_lamp = new OmniLight3D { Name = "Lamp", LightColor = _lampA, LightEnergy = 1.6f, OmniRange = 9f, OmniAttenuation = 1.1f, Position = new Vector3(0, RoomH - 0.5f, -hz) };
		AddChild(_lamp);
		_fill = new OmniLight3D { Name = "Fill", LightColor = new Color(0.45f, 0.5f, 0.42f), LightEnergy = 0.35f, OmniRange = 10f, Position = new Vector3(0, 1.8f, -hz) };
		AddChild(_fill);

		// Damp, stains and a drain: the same every time, which is the point.
		var rng = new RandomNumberGenerator { Seed = 7171 };
		for (int i = 0; i < 6; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			BunkerKit.AddDecal(this, rng.Randf() < 0.5f ? BunkerTextures.RustRun() : BunkerTextures.Streak(),
				new Vector3(side * hw, RoomH - 1.1f, rng.RandfRange(-RoomD + 0.8f, -0.8f)), new Vector3(-side, 0, 0), Vector3.Down,
				new Vector2(rng.RandfRange(0.4f, 0.9f), 2.0f), 0.5f, new Color(1, 1, 1, 0.85f));
		}
		for (int i = 0; i < 3; i++)
			BunkerKit.AddDecal(this, BunkerTextures.Puddle(), new Vector3(rng.RandfRange(-2.5f, 2.5f), 0f, rng.RandfRange(-5.5f, -1.5f)),
				Vector3.Up, Vector3.Forward, new Vector2(rng.RandfRange(1f, 1.8f), rng.RandfRange(1f, 2f)), 0.3f, new Color(1, 1, 1, 0.85f), BunkerTextures.PuddleOrm());
		BunkerKit.AddDecal(this, BunkerTextures.Blotch(), new Vector3(1.4f, 0f, -3.6f), Vector3.Up, Vector3.Forward, new Vector2(2.2f, 2.2f), 0.3f, new Color(1, 1, 1, 0.7f));
		var g = new MeshKit();
		g.Mat(ProcTextures.MetalMat);
		g.Color = new Color(0.3f, 0.3f, 0.28f);
		g.Cylinder(new Vector3(1.4f, 0.002f, -3.6f), new Vector3(1.4f, 0.012f, -3.6f), 0.16f, 0.16f, 10);
		g.CommitTo(this, "Drain", false);
	}

	/// <summary>A plank door in a frame set into a wall. <paramref name="inward"/> is the wall's inward normal (into the room).
	/// index -1 = the entry door (shut, no interactable; its leaf node pivots on the hinge so it can swing open).
	/// Returns the leaf node.</summary>
	private Node3D Door(Vector3 at, Vector3 inward, int index)
	{
		Vector3 side = Vector3.Up.Cross(inward).Normalized();
		var basis = new Basis(side, Vector3.Up, -inward);
		const float w = DoorW, h = DoorH, t = 0.1f;
		// Steel bunker doors (Dan, 2026-09-22: the plank ones read see-through): a riveted steel frame
		// proud of the wall, and a solid slab leaf. Every piece is a closed box or cylinder, so nothing
		// is single-sided or transparent.
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.3f, 0.31f, 0.29f);
		// Frame proud of the wall: jambs, header, a low sill.
		k.Box(at + side * (-w * 0.5f - t * 0.5f) + Vector3.Up * h * 0.5f + inward * 0.06f, new Vector3(t, h, 0.2f), 1f, basis);
		k.Box(at + side * (w * 0.5f + t * 0.5f) + Vector3.Up * h * 0.5f + inward * 0.06f, new Vector3(t, h, 0.2f), 1f, basis);
		k.Box(at + Vector3.Up * (h + t * 0.5f) + inward * 0.06f, new Vector3(w + t * 2f, t, 0.2f), 1f, basis);
		k.Box(at + Vector3.Up * 0.02f + inward * 0.06f, new Vector3(w + t * 2f, 0.04f, 0.2f), 1f, basis);
		// Frame rivets down both jambs.
		k.Color = new Color(0.22f, 0.22f, 0.2f);
		for (int i = 0; i < 7; i++)
		{
			float y = 0.2f + i * (h - 0.4f) / 6f;
			foreach (float sx in new[] { -1f, 1f })
			{
				Vector3 r = at + side * (sx * (w * 0.5f + t * 0.5f)) + Vector3.Up * y + inward * 0.16f;
				k.Cylinder(r, r + inward * 0.014f, 0.014f, 0.011f, 6);
			}
		}
		k.CommitTo(this, index < 0 ? "EntryFrame" : $"DoorFrame{index}");

		// The leaf pivots on its hinge edge (so the entry door can swing); its mesh is authored in room space and shifted back.
		Vector3 hinge = at + side * (-w * 0.5f);
		var leaf = new Node3D { Name = index < 0 ? "EntryLeaf" : $"DoorLeaf{index}", Position = hinge };
		AddChild(leaf);
		var lk = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = (ulong)(8300 + index) };
		// The leaf: one solid painted-steel slab, grey-green, a shade different per door.
		lk.Mat(BunkerTextures.PaintedMetalMat);
		float tone = rng.RandfRange(0.86f, 1f);
		lk.Color = new Color(0.36f * tone, 0.41f * tone, 0.35f * tone);
		Vector3 slabC = at + Vector3.Up * (h * 0.5f - 0.01f) + inward * 0.07f;
		lk.Box(slabC, new Vector3(w - 0.02f, h - 0.04f, 0.08f), 1f, basis);
		// Two horizontal ribs and a latch-side strap, riveted.
		lk.Color = new Color(0.3f * tone, 0.34f * tone, 0.29f * tone);
		foreach (float y in new[] { 0.5f, h - 0.5f })
			lk.Box(at + Vector3.Up * y + inward * 0.125f, new Vector3(w - 0.1f, 0.13f, 0.035f), 1f, basis);
		lk.Box(at + side * (w * 0.5f - 0.09f) + Vector3.Up * (h * 0.5f - 0.01f) + inward * 0.125f, new Vector3(0.08f, h - 0.14f, 0.035f), 1f, basis);
		lk.Mat(ProcTextures.MetalMat);
		lk.Color = new Color(0.2f, 0.2f, 0.18f);
		foreach (float y in new[] { 0.5f, h - 0.5f })
			for (int i = 0; i < 6; i++)
			{
				Vector3 r = at + side * (-(w * 0.5f - 0.12f) + i * (w - 0.24f) / 5f) + Vector3.Up * y + inward * 0.1425f;
				lk.Cylinder(r, r + inward * 0.014f, 0.014f, 0.011f, 6);
			}
		// A lever handle on a boss, latch side.
		Vector3 boss = at + side * (w * 0.5f - 0.2f) + Vector3.Up * 1.02f + inward * 0.11f;
		lk.Cylinder(boss, boss + inward * 0.05f, 0.036f, 0.036f, 8);
		lk.Cylinder(boss + inward * 0.05f, boss + inward * 0.1f, 0.016f, 0.016f, 6);
		lk.Cylinder(boss + inward * 0.1f, boss + inward * 0.1f + Vector3.Down * 0.16f, 0.013f, 0.011f, 6);
		var mesh = lk.CommitTo(leaf, "Mesh");
		mesh.Position = -hinge;
		_doorLeafLocal.Add(at + Vector3.Up * 1.0f);
		if (index < 0) return leaf;

		var use = new Interactable
		{
			Name = "Open",
			Prompt = "Open the door",
			MaxDistance = 2.6f,
			PickRadius = 0.55f,
			Position = at + Vector3.Up * 1.1f + inward * 0.35f - hinge,
		};
		leaf.AddChild(use);
		int idx = index;
		use.Interacted += p => OpenDoor(idx, p);
		_doors.Add(use);
		return leaf;
	}

	/// <summary>The way out, behind the entry door: a short hall ending in the inside of the bunker's own round door,
	/// night beyond, moonlight on the floor.</summary>
	private void BuildExitHall()
	{
		float mid = (HallZ0 + HallZ1) * 0.5f, len = HallZ1 - HallZ0;
		var k = new MeshKit();
		k.Mat(BunkerTextures.MazeWallMat);
		k.Color = Colors.White;
		k.Box(new Vector3(-HallHalfW - 0.15f, RoomH * 0.5f, mid), new Vector3(0.3f, RoomH, len + 0.6f));
		k.Box(new Vector3(HallHalfW + 0.15f, RoomH * 0.5f, mid), new Vector3(0.3f, RoomH, len + 0.6f));
		k.Box(new Vector3(0, RoomH * 0.5f, HallZ1 + 0.15f), new Vector3(HallHalfW * 2f + 0.6f, RoomH, 0.3f));
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		k.Box(new Vector3(0, RoomH + 0.05f, mid), new Vector3(HallHalfW * 2f, 0.1f, len));
		k.CommitTo(this, "ExitHallShell");
		var f = new MeshKit();
		f.Mat(BunkerTextures.MazeFloorMat);
		f.Color = Colors.White;
		f.Box(new Vector3(0, -0.05f, mid), new Vector3(HallHalfW * 2f, 0.1f, len + 0.6f));
		f.CommitTo(this, "ExitHallFloor");
		AddBox(new Vector3(-HallHalfW - 0.15f, RoomH * 0.5f, mid), new Vector3(0.3f, RoomH, len + 0.6f));
		AddBox(new Vector3(HallHalfW + 0.15f, RoomH * 0.5f, mid), new Vector3(0.3f, RoomH, len + 0.6f));
		AddBox(new Vector3(0, RoomH * 0.5f, HallZ1 + 0.15f), new Vector3(HallHalfW * 2f + 0.6f, RoomH, 0.3f));
		AddBox(new Vector3(0, -0.05f, mid), new Vector3(HallHalfW * 2f, 0.1f, len + 0.6f));
		AddBox(new Vector3(0, RoomH + 0.05f, mid), new Vector3(HallHalfW * 2f, 0.1f, len));

		// The round door's inside at the far end: frame, the night through it, the leaf swung back, moonlight.
		var ringC = new Vector3(0, 1.25f, HallZ1 - 0.02f);
		var rk = new MeshKit();
		rk.Mat(ProcTextures.MetalMat);
		rk.Color = new Color(0.5f, 0.48f, 0.44f);
		const int segs = 20;
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			Vector3 d0 = new(Mathf.Cos(a0), Mathf.Sin(a0), 0), d1 = new(Mathf.Cos(a1), Mathf.Sin(a1), 0);
			rk.Beam(ringC + d0 * 1.2f + Vector3.Forward * 0.05f, ringC + d1 * 1.2f + Vector3.Forward * 0.05f, 0.18f, 0.1f, 1f, Vector3.Forward);
		}
		rk.CommitTo(this, "ExitDoorFrame");
		var night = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.03f, 0.05f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var nk = new MeshKit();
		nk.Mat(night);
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			// Wound the other way round so the disc faces back down the hall (-Z).
			nk.Tri(ringC + Vector3.Forward * 0.01f, ringC + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * 1.12f + Vector3.Forward * 0.01f,
				ringC + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * 1.12f + Vector3.Forward * 0.01f, Vector3.Forward, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		nk.CommitTo(this, "ExitNight", false);
		var leaf = new MeshKit { Xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(95f), 0)), ringC + new Vector3(1.1f, -1.25f, -0.1f)) };
		BunkerExterior.VaultDoor(leaf, new Vector3(-BunkerExterior.DoorRadius, 1.25f, 0f), ProcTextures.MetalMat);
		leaf.CommitTo(this, "ExitDoorLeaf");
		// The moonlight through the round door is the goal, visible from the room; three near-dead cold lamps
		// down the run keep it from being a void (the fill dies with the blackout, these do not).
		AddChild(new OmniLight3D { Name = "Moonlight", LightColor = new Color(0.45f, 0.55f, 0.75f), LightEnergy = 0.9f, OmniRange = 14f, OmniAttenuation = 1.2f, Position = ringC + new Vector3(0, 0.6f, -0.8f) });
		_hallFill = new OmniLight3D { Name = "HallFill", LightColor = new Color(0.4f, 0.42f, 0.5f), LightEnergy = 0.3f, OmniRange = 12f, Position = new Vector3(0, 2f, mid) };
		AddChild(_hallFill);
		var lampMetal = new MeshKit();
		lampMetal.Mat(ProcTextures.MetalMat);
		int hl = 0;
		foreach (float z in new[] { 10f, 20f, 30f })
		{
			var lxf = new Transform3D(Basis.Identity, new Vector3(0, RoomH - 0.02f, z));
			BunkerKit.CagedLamp(lampMetal, lxf);
			BunkerKit.LampGlass(this, lxf, BunkerTextures.NewLampGlass(new Color(0.55f, 0.62f, 0.7f), 0.5f), $"HallLampGlass{hl}");
			AddChild(new OmniLight3D { Name = $"HallLamp{hl}", LightColor = new Color(0.55f, 0.62f, 0.7f), LightEnergy = 0.15f, OmniRange = 7f, OmniAttenuation = 1.3f, Position = new Vector3(0, RoomH - 0.5f, z) });
			hl++;
		}
		lampMetal.CommitTo(this, "HallLampCages", false);
	}
}
