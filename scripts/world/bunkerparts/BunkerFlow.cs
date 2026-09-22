using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The bunker's story, as sequences (all through <see cref="Cutscene.Run"/>):
/// - Admit: fade, teleport into the hallway (Act 8), night mood, indoor audio.
/// - The CRT sequence (Act 9): the marked screen is switched off, every tube
///   goes dark, five seconds later they all come back fuzzy and clear to the
///   stairs, the caption, then the CRT flag.
/// - The maze (Act 10): after the screens, going back out through the vine
///   door fades to the maze, the choir starts, "...come and see...".
/// - The exit: the caption and the dropped walkie-talkie.
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
	private BunkerMaze _maze;

	private double _clock;
	private bool _mazeTriggered, _mazeArmed, _indoor, _choirStarted;
	private bool _playerNear = true;   // so the first tick with a player outdoors moves the marker to the hatch
	private PlayerController _player;

	public bool IsAdmitting { get; private set; }

	public void Setup(BunkerInterior interior, BunkerHallway hallway, BunkerVineDoor vineDoor, CrtRoom crt, BunkerMaze maze)
	{
		_interior = interior; _hallway = hallway; _vineDoor = vineDoor; _crt = crt; _maze = maze;
		_crt.SwitchUsed += RunCrtSequence;
		_maze.ExitReached += OnMazeExit;
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
			_vineDoor.SetOpenInstant();
			_crt.SetStairsInstant();
			// The lights committed to red on the way in; they don't replay their sequence on a Continue.
			_hallway.SetRedInstant();
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
		if (near) _maze.Tick(_clock, local, player);
		if (near && !_mazeTriggered && StoryManager.Instance is { CrtPuzzleDone: true })
			CheckVineDoorReturn(local.Value);
		if (_indoor && !near && !IsAdmitting) SetIndoor(false);
	}

	/// <summary>Once the screens have shown the stairs, walking back toward the vine door from inside the
	/// CRT room turns the hallway you're about to re-enter into the maze. (Armed once the player has been
	/// deeper in the room than the trigger band, so walking in from the hallway after a Continue doesn't
	/// fire it on the way in.)</summary>
	private void CheckVineDoorReturn(Vector3 local)
	{
		if (local.Z < -HallLength - 6f) _mazeArmed = true;
		if (!_mazeArmed || !_vineDoor.IsOpen) return;
		if (local.Z < -HallLength - 0.5f && local.Z > -HallLength - 6f)
		{
			_mazeTriggered = true;
			TransformToMaze();
		}
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
					if (exited) { _maze.MarkExited(); SpawnWalkie(); }
					var at = exited ? BunkerMaze.ExitLocal + new Vector3(0, 0, 1.2f) : BunkerMaze.StartLocal;
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
			await StoryBeat.Caption(this, "Every screen shows the same thing: the stairs, in the woods.", 1.2f, 3.0f, 1.2f);
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
			player.Teleport(_interior.ToGlobal(BunkerMaze.StartLocal), 0f);
			StoryManager.Instance?.SetFlag(StoryManager.Flag.BunkerMazeEntered);
			StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 2f);
			await Cutscene.Frame(this, ct);
			if (fader != null)
			{
				await fader.Fade(0f, 1.4f);
				await fader.ShowCaption("", "\"...come and see...\"", 1.0f, 2.4f, 1.0f);
			}
			GD.Print("[story] Act 10: the hallway has become a maze");
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
		var choir = new AudioStreamPlayer { Name = "Choir", Bus = "Unnatural" };
		_maze.AddChild(choir);
		choir.AddChild(new AmbienceLoop { StreamPath = path, BaseVolumeDb = -8f });
	}

	private void OnMazeExit()
	{
		StoryManager.Instance?.SetFlag(StoryManager.Flag.BunkerMazeExited);
		SpawnWalkie();
		_ = Cutscene.Run(this, _ => StoryBeat.Caption(this, "The corridor ends where it began.", 1.0f, 2.4f, 1.0f));
		GD.Print("[story] Act 10: the maze ends");
	}

	/// <summary>The walkie-talkie, dropped on the floor by the old front door, hissing, its LED blinking.</summary>
	private void SpawnWalkie()
	{
		if (StoryManager.Instance is { Current: >= Checkpoint.Act10WalkieFound }) return;
		if (_maze.GetNodeOrNull("WalkiePickup") != null) return;
		var walkie = new WalkiePickup { Name = "WalkiePickup", Position = BunkerMaze.WalkieLocal - MazeOffset, Rotation = new Vector3(0, 0.5f, 0) };
		_maze.AddChild(walkie);
		walkie.AddChild(new WalkieBeacon { Name = "Beacon", Position = new Vector3(0.022f, 0.158f, 0.024f) });
	}
}
