using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The bunker's story, as sequences (all through <see cref="Cutscene.Run"/>):
/// - Admit: fade, teleport into the hallway (Act 8), night mood, indoor audio.
/// - The CRT sequence (Act 9): the marked screen is switched off, every tube
///   goes dark, five seconds later they all come back fuzzy and clear to the
///   stairs (no caption), then the CRT flag.
/// - The maze (Act 10): after the screens, going back out through the vine
///   door fades to the maze, the choir starts, "...come and see...".
/// - The exit: the round door, reached at a run; outside, the dead walkie-talkie from the CRT
///   room comes to life (checkpoint 8, then Act 11's radio).
/// It also keeps the compass honest: after the screens the objective marker
/// (group "bunker_entrance_marker") stands at the hallway's entrance, the way
/// out, until the maze wakes and it moves to the maze's exit; while the player
/// is outdoors it stands at the real hatch.
/// Restore: <see cref="Restore"/> puts the rooms in the state the saved story
/// reached (the hallway already red once the screens have shown the stairs),
/// and re-entering the bunker after a Continue lands the player where that
/// state says (the hallway, or the maze, or the maze's end).
/// </summary>
public partial class BunkerFlow : Node
{
	private BunkerInterior _interior;
	private BunkerHallway _hallway;
	private BunkerVineDoor _vineDoor;
	private CrtRoom _crt;
	private BunkerRooms _maze;

	private double _clock;
	private bool _mazeTriggered, _mazeArmed, _indoor, _choirStarted;
	private bool _playerNear = true;   // so the first tick with a player outdoors moves the marker to the hatch
	private PlayerController _player;

	public bool IsAdmitting { get; private set; }

	public void Setup(BunkerInterior interior, BunkerHallway hallway, BunkerVineDoor vineDoor, CrtRoom crt, BunkerRooms maze)
	{
		_interior = interior; _hallway = hallway; _vineDoor = vineDoor; _crt = crt; _maze = maze;
		_crt.SwitchUsed += RunCrtSequence;
		_maze.ExitReached += OnMazeExit;
		_vineDoor.UsedFromInside += OnVineDoorUsedInside;
	}

	// ------------------------------------------------------------------ lookups

	private PlayerController Player
	{
		get
		{
			if (_player == null || !IsInstanceValid(_player)) _player = StoryBeat.Player(this);
			return _player;
		}
	}

	/// <summary>Dulls the forest while the player is inside.</summary>
	private void SetIndoor(bool on)
	{
		if (_indoor == on) return;
		_indoor = on;
		ForestAmbienceManager.Instance?.SetIndoor(_interior, on);
	}

	// ------------------------------------------------------------------ restore

	/// <summary>Called once the rooms are built: match the saved story.</summary>
	public void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null) return;
		if (s.CrtPuzzleDone)
		{
			// Inside the CRT room the door has shut behind them: it is the way to the room of doors now.
			_vineDoor.SetShutBehindInstant();
			_crt.SetStairsInstant();
			// The lights committed to red on the way in; they don't replay their sequence on a Continue.
			_hallway.SetRedInstant();
		}
		_jumpscared = s.HasFlag(JumpscareFlag) || s.CrtPuzzleDone;
		// (2026-10-29) past the scare, the lamps came back red for good
		if (s.HasFlag(JumpscareFlag)) _hallway.SetRedInstant();
		// the scare's body built now, as the bunker builds, and kept still and hidden till its moment (the lag pass,
		// 2026-10-13: built on the spot, it held the frame it appears in for 60 ms)
		if (!_jumpscared && _scareBody == null)
		{
			_scareBody = new StalkerBody { Name = "HallwayScare", Seed = 2077, Size = 1.08f, Visible = false, ProcessMode = ProcessModeEnum.Disabled };
			_interior.AddChild(_scareBody);
			if (_scareBody.IsInsideTree()) _scareBody.GlowEyes(new Color(1f, 0.16f, 0.05f), 8f);   // (its lit eyes' shader drawn by the load's warm-up too)
		}
		bool inMaze = s.HasFlag(StoryManager.Flag.BunkerMazeEntered);
		if (inMaze) HideHallway();
		if (s.HasFlag(StoryManager.Flag.BunkerMazeExited)) _maze.MarkExited();
		_interior.PointCompass(inMaze ? BunkerInterior.CompassSpot.MazeExit : BunkerInterior.CompassSpot.HallwayEntrance);
	}

	// ------------------------------------------------------------------ per frame

	public void Tick(double delta)
	{
		_clock += delta;
		var player = Player;
		bool near = player != null && player.GlobalPosition.DistanceTo(_interior.GlobalPosition) < 500f;
		if (player != null && near != _playerNear)
		{
			_playerNear = near;
			// Outdoors the way back in is the hatch itself; inside, the way out (or the maze's end).
			_interior.PointCompass(!near ? BunkerInterior.CompassSpot.Outside
				: _mazeTriggered ? BunkerInterior.CompassSpot.MazeExit : BunkerInterior.CompassSpot.HallwayEntrance);
		}
		Vector3? local = near ? _interior.ToLocal(player.GlobalPosition) : null;
		if (near && _hallway.Visible) _hallway.Animate(local, _clock);
		if (near && _hallway.Visible && local is { } l) CheckJumpscare(l, player);   // back in (Dan, 2026-09-22: "re add the 1st jumpscare")
		// Through into the CRT room: the vine door swings shut behind them (once), the way back is the room of doors.
		if (near && _vineDoor.IsOpen && !_vineDoor.ShutBehind && local is { } lc && lc.Z < -HallLength - 1.5f) _vineDoor.Close();
		if (near) _maze.Tick(_clock, local, player);
		if (near && !_mazeTriggered && !_wayOutLabelled && WayOutOpen) { _wayOutLabelled = true; _vineDoor.SetInsidePrompt("Push it open"); }
		if (near && !_mazeTriggered && StoryManager.Instance is { CrtPuzzleDone: true })
			CheckVineDoorReturn(local.Value);
		if (_indoor && !near && !IsAdmitting) SetIndoor(false);
	}

	// ------------------------------------------------------------------ Act 8: the hallway jumpscare

	/// <summary>How far down the hallway it happens: past the first flicker (0.4), before the red (0.75).</summary>
	private const float JumpscareFrac = 0.56f;
	private const string JumpscareFlag = "bunker_jumpscare_done";
	private bool _jumpscared;
	private StalkerBody _scareBody;
	/// <summary>For the autotest: it has stepped out in the hallway.</summary>
	public bool JumpscareFired => _jumpscared;

	/// <summary>
	/// Once the lamps have started to fail, at a point down the hall (and only while they are looking down it), the
	/// lights cut out, all but one red lamp far ahead; then the red comes on, every lamp; then it is there. Once only
	/// (saved).
	/// </summary>
	private void CheckJumpscare(Vector3 local, PlayerController player)
	{
		if (_jumpscared || local.Z > 0f || local.Z < -HallLength || -local.Z < HallLength * JumpscareFrac) return;
		var cam = player.CameraRig?.Camera;
		if (cam == null) return;
		Vector3 fwd = _interior.GlobalBasis.Inverse() * (-cam.GlobalBasis.Z);
		if (fwd.Z > -0.4f) return;
		_jumpscared = true;
		StoryManager.Instance?.SetFlag(JumpscareFlag);
		_ = Cutscene.Run(this, ct => Jumpscare(player, ct));
	}

	/// <summary>How long the hall stands dark with only the far red lamp (s), and how long it stands all red before it comes.</summary>
	[Export] public float DarkHold = 3.2f, RedHold = 1.4f;
	/// <summary>How far down the hall, ahead of the player, the one red lamp burns (m).</summary>
	[Export] public float SoloLampAhead = 26f;
	/// <summary>For tests: the stages the scare went through (1 dark with one red lamp, 2 all red, 3 it lunged).</summary>
	public int JumpscareStage { get; private set; }
	/// <summary>For tests: the scare has run its course (the lamps steady red, control back).</summary>
	public bool JumpscareDone { get; private set; }

	/// <summary>
	/// (2026-10-29, the owner: "the lights should cut out so it's dark, with just one red light in the distance; then all
	/// the red lights come on; then the jumpscare triggers") the breaker's clunk and every lamp dies at once, but for one,
	/// far down the hall, that sputters back on red; the dark holds; then the red comes on, lamp by lamp, from that one
	/// back toward them and on to the entrance; a breath of all-red quiet; then, in one frame, it is standing two metres in
	/// front of them with its eyes lit, the burst and the roar, the lunge at the face (input held for that second), the
	/// lamps jolting, and it is gone. The walk is theirs through the dark and the red: only the lunge holds them.
	/// </summary>
	private async System.Threading.Tasks.Task Jumpscare(PlayerController player, System.Threading.CancellationToken ct)
	{
		var body = _scareBody != null && IsInstanceValid(_scareBody) ? _scareBody : new StalkerBody { Name = "HallwayScare", Seed = 2077, Size = 1.08f };
		_scareBody = null;
		if (body.GetParent() == null) _interior.AddChild(body);
		Vector3 Local() => _interior.ToLocal(player.GlobalPosition);
		bool locked = false;
		try
		{
			// 1. the lights cut out: dark, but for one red lamp far down the hall
			float depth = -Local().Z;
			int solo = _hallway.LampAt(Mathf.Min(depth + SoloLampAhead, HallLength - LightSpacing));
			_hallway.Blackout(true);
			JumpscareStage = 1;
			GD.Print("[story] Act 8: the lamps cut out, one red one left far down the hall");
			BunkerKit.OneShot(_interior, "res://assets/audio/sfx/relay_clunk_01.wav", Local() + new Vector3(0, HallHeight - 0.3f, -4f), "Events", 2f, 0.85f, 6f, 60f);
			await Cutscene.Wait(this, 0.7, ct);
			_hallway.SoloRed(solo);
			int sp = (int)(GD.Randi() % 3) + 1;
			BunkerKit.OneShot(_interior, $"res://assets/audio/sfx/bulb_sputter_{sp:00}.wav", new Vector3(0, HallHeight - 0.3f, _hallway.SoloLampZ), "Events", 0f, 0.9f, 10f, 80f);
			await Cutscene.Wait(this, DarkHold, ct);

			// 2. all the red lights come on: from that lamp back toward them, one by one, and on to the entrance
			JumpscareStage = 2;
			GD.Print("[story] Act 8: the red comes on, every lamp");
			BunkerKit.OneShot(_interior, "res://assets/audio/sfx/power_surge_01.wav", Local() + new Vector3(0, HallHeight - 0.3f, -6f), "Events", -2f, 0.8f, 8f, 60f);
			for (int i = solo; i >= 0; i--)
			{
				_hallway.AllRed(i);
				if (Mathf.Abs(_hallway.LampAt(-Local().Z) - i) <= 3)
				{
					int lt = (int)(GD.Randi() % 2) + 1;
					BunkerKit.OneShot(_interior, $"res://assets/audio/sfx/light_switch_{lt:00}.wav", new Vector3(0, HallHeight - 0.3f, -LightSpacing * (i + 0.5f)), "Events", -6f, 0.6f, 4f, 30f);
				}
				await Cutscene.Wait(this, 0.07, ct);
			}
			_hallway.RedOn();
			await Cutscene.Wait(this, RedHold, ct);

			// 3. then it is there, two metres in front of their face (whichever way down the hall they look now)
			JumpscareStage = 3;
			Cutscene.Lock(player, true, true);
			locked = true;
			Vector3 local = Local();
			var cam = player.CameraRig?.Camera;
			float dir = cam != null && (_interior.GlobalBasis.Inverse() * (-cam.GlobalBasis.Z)).Z > 0.2f ? 1f : -1f;
			Vector3 at = new(Mathf.Clamp(local.X, -1f, 1f) * 0.3f, 0f, Mathf.Clamp(local.Z + dir * 2.0f, -HallLength + 0.5f, -0.5f));
			body.ProcessMode = ProcessModeEnum.Inherit;
			body.Visible = true;
			body.Position = at;
			body.Rotation = new Vector3(0, Mathf.Atan2(local.X - at.X, local.Z - at.Z), 0);
			body.GlowEyes(new Color(1f, 0.16f, 0.05f), 8f);
			body.Snap("loom");   // drawn up over them, the arms out, the mouth's slit open
			body.TrackTarget = cam?.GlobalPosition;   // its face straight at theirs
			body.Watched = true;
			_hallway.Jolt(_clock + 0.45);
			BunkerRooms.ScareVoice(_interior, at + Vector3.Up * 1.7f);
			GD.Print("[story] Act 8: it is in the hallway");
			// 0.0-0.15 s it stands; 0.15-0.45 s the lunge to 0.6 m, the head coming down at the face
			await Cutscene.Wait(this, 0.15, ct);
			Vector3 lunge = new(at.X, 0f, local.Z + dir * 0.6f);
			var tw = body.CreateTween().SetParallel();
			tw.TweenProperty(body, "position", lunge, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenProperty(body, "rotation:x", -0.35f, 0.3f);
			await Cutscene.Wait(this, 0.3, ct);
			// 0.45 s, one frame: it is gone (no fade), the lamps jolting red a moment more
			player.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 0.16f, 0.14f));
			body.Visible = false;
			body.QueueFree();
			_hallway.Jolt(_clock + 0.5);
			await Cutscene.Wait(this, 0.75, ct);
		}
		finally
		{
			_hallway.AllRed();
			_hallway.RedOn();
			if (IsInstanceValid(body)) body.QueueFree();
			if (locked && IsInstanceValid(player) && player.IsInsideTree()) Cutscene.Unlock(player, true, true);
			JumpscareDone = true;
		}
	}

	/// <summary>Once the screens have shown the stairs, walking back toward the vine door from inside the
	/// CRT room turns the hallway you're about to re-enter into the maze. (Armed once the player has been
	/// deeper in the room than the trigger band, so walking in from the hallway after a Continue doesn't
	/// fire it on the way in.)</summary>
	private void CheckVineDoorReturn(Vector3 local)
	{
		if (local.Z < -HallLength - 6f) _mazeArmed = true;
		if (!_mazeArmed || !WayOutOpen) return;
		// Walking into the shut door (or E on it, see OnVineDoorUsedInside) is the way to the room of doors.
		if (local.Z < -HallLength - 0.5f && local.Z > -HallLength - 1.6f && Mathf.Abs(local.X) < DoorHalfWidth + 0.6f)
		{
			_mazeTriggered = true;
			TransformToMaze();
		}
	}

	/// <summary>The CRT room's door stays locked until BOTH the screens have shown the stairs AND the walkie-talkie is
	/// in hand (Dan, 2026-09-22: "you cant leave the tv room until you grab the walkie"). Restore-safe: both are flags.</summary>
	private bool WayOutOpen => _vineDoor.ShutBehind && StoryManager.Instance is { CrtPuzzleDone: true } s
		&& (s.HasFlag(StoryManager.Flag.WalkieTaken) || Player?.Inventory is { HasRadio: true });
	/// <summary>For the autotest: the shut vine door will let them through now.</summary>
	public bool VineDoorUnlocked => WayOutOpen;
	private bool _wayOutLabelled;

	/// <summary>E on the shut vine door from the CRT room: locked (a rattle, "It won't move.") until the screens are
	/// done and the walkie is taken; then it is the transition to the room of doors.</summary>
	private void OnVineDoorUsedInside()
	{
		if (_mazeTriggered) return;
		if (WayOutOpen)
		{
			_mazeTriggered = true;
			TransformToMaze();
			return;
		}
		_vineDoor.Rattle();
	}

	// ------------------------------------------------------------------ Act 8: in through the hatch

	public void Admit(PlayerController player)
	{
		if (IsAdmitting || player == null) return;
		IsAdmitting = true;
		var s = StoryManager.Instance;
		bool maze = s != null && s.HasFlag(StoryManager.Flag.BunkerMazeEntered);
		bool exited = s != null && s.HasFlag(StoryManager.Flag.BunkerMazeExited);
		_ = Cutscene.Run(this, async ct =>
		{
			try
			{
				var fader = StoryBeat.Fader(this);
				if (fader != null) await fader.Fade(1f, 0.8f);
				ct.ThrowIfCancellationRequested();
				SetIndoor(true);
				if (maze)
				{
					// Restored mid-maze: straight back in, no second caption.
					EnterMaze();
					if (exited) _maze.MarkExited();
					// Shoved out already: the room is shut behind them, so they land in the hall, not the room.
					bool scared = s.HasFlag(BunkerRooms.ScaredFlag);
					var at = exited ? BunkerRooms.ExitLocal + new Vector3(0, -1f, -1.5f) : scared ? MazeOffset + new Vector3(0, 0, 3.2f) : BunkerRooms.StartLocal;
					player.Teleport(_interior.ToGlobal(at), 0f);
					StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 1f);
				}
				else
				{
					// The hallway floor only spans Z 0..-90: land just inside it.
					player.Teleport(_interior.ToGlobal(HallSpawn), 0f);
					StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, 1f);
				}
				await Cutscene.Frame(this, ct);
				if (fader != null) await fader.Fade(0f, 1.0f);
			}
			finally { IsAdmitting = false; }
		}, lockInput: true, freezeBody: true);
	}

	// ------------------------------------------------------------------ Act 9: the screens

	private void RunCrtSequence()
	{
		_ = Cutscene.Run(this, async ct =>
		{
			_crt.TurnAllOff();
			if (_crt.Target != null)
				BunkerKit.OneShot(_crt.Target, "res://assets/audio/sfx/camera_shutter.wav", Vector3.Up * 0.3f, "Events", 0f, 0.6f, 2f, 15f);
			await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 1.5 : 5.0, ct);
			_crt.TurnOnStairs(3.5f);
			await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 1.0 : 4.0, ct);   // the screens say it; nothing is printed (Dan, 2026-09-22)
			ct.ThrowIfCancellationRequested();
			StoryManager.Instance?.MarkCrtPuzzleDone();
			GD.Print("[story] Act 9: the screens show the stairs");
		});
	}

	// ------------------------------------------------------------------ Act 10: the maze

	private void TransformToMaze()
	{
		var player = Player;
		if (player == null) return;
		_ = Cutscene.Run(this, async ct =>
		{
			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 1.4f);
			ct.ThrowIfCancellationRequested();
			EnterMaze();
			player.Teleport(_interior.ToGlobal(BunkerRooms.StartLocal), 0f);
			StoryManager.Instance?.SetFlag(StoryManager.Flag.BunkerMazeEntered);
			StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 2f);
			await Cutscene.Frame(this, ct);
			if (fader != null) await fader.Fade(0f, 1.4f);   // the choir says it; nothing is printed
			GD.Print("[story] Act 10: the hallway is gone; a room with three doors");
		}, lockInput: true, freezeBody: true);
	}

	/// <summary>The hallway is gone; the maze wakes (lamps, glimpses, the choir); the compass points at its end.</summary>
	private void EnterMaze()
	{
		_mazeTriggered = true;
		HideHallway();
		if (!_maze.Exited) _maze.Active = true;
		StartChoir();
		_interior.PointCompass(BunkerInterior.CompassSpot.MazeExit);
	}

	private void HideHallway()
	{
		foreach (Node3D n in new Node3D[] { _hallway, _vineDoor })
		{
			n.Visible = false;
			n.ProcessMode = ProcessModeEnum.Disabled;
		}
	}

	private void StartChoir()
	{
		if (_choirStarted) return;
		_choirStarted = true;
		const string path = "res://assets/audio/ambient/choir_chant_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		var choir = new AudioStreamPlayer { Name = "Choir", Bus = "Unnatural", PitchScale = 0.66f };   // voices stay deep (Dan, 2026-09-22: deeper still)
		_maze.AddChild(choir);
		choir.AddChild(new AmbienceLoop { StreamPath = path, BaseVolumeDb = -12f });
	}

	/// <summary>
	/// Out through the round door at a run. Outside is Act 11's first beat: the walkie-talkie picked
	/// up dead in the CRT room crackles to life (checkpoint 8: <see cref="Act11Ending"/> carries the
	/// player out into the night in front of the hatch and the radio speaks).
	/// </summary>
	private void OnMazeExit()
	{
		var s = StoryManager.Instance;
		s?.SetFlag(StoryManager.Flag.BunkerMazeExited);
		var player = Player;
		if (player != null) player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);   // never without it
		if (player != null && s != null && s.Current < Checkpoint.Act10WalkieFound)
			s.ReachCheckpoint(Checkpoint.Act10WalkieFound, player.GlobalPosition, player.CameraRig.Yaw);
		GD.Print("[story] Act 10: out of the bunker; the radio wakes");
	}
}
