using System.Reflection;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
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
/// Restore: <see cref="Restore"/> puts the rooms in the state the saved story
/// reached, and re-entering the bunker after a Continue lands the player where
/// that state says (the hallway, or the maze, or the maze's end).
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
	private PlayerController _player;
	private ScreenFader _fader;
	private ForestAtmosphere _atmosphere;

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
			if (_player == null || !IsInstanceValid(_player)) _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			return _player;
		}
	}

	private ScreenFader Fader
	{
		get
		{
			if (_fader == null || !IsInstanceValid(_fader)) _fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
			return _fader;
		}
	}

	private ForestAtmosphere Atmosphere
	{
		get
		{
			if (_atmosphere == null || !IsInstanceValid(_atmosphere)) _atmosphere = GetTree().Root.FindChild("Atmosphere", true, false) as ForestAtmosphere;
			return _atmosphere;
		}
	}

	private static MethodInfo _setIndoor;
	private static bool _setIndoorLooked;

	/// <summary>
	/// Dulls the forest while the player is inside. Calls ForestAmbienceManager.SetIndoor(object, bool)
	/// (the systems agent's API) if it exists in this build; a no-op otherwise.
	/// </summary>
	private void SetIndoor(bool on)
	{
		if (_indoor == on) return;
		_indoor = on;
		if (!_setIndoorLooked)
		{
			_setIndoorLooked = true;
			_setIndoor = typeof(ForestAmbienceManager).GetMethod("SetIndoor", new[] { typeof(object), typeof(bool) });
			if (_setIndoor == null) GD.PushWarning("[bunker] ForestAmbienceManager.SetIndoor(object, bool) not found: forest ambience won't dull indoors");
		}
		var mgr = ForestAmbienceManager.Instance;
		if (_setIndoor != null && mgr != null) _setIndoor.Invoke(mgr, new object[] { _interior, on });
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
		}
		if (s.HasFlag(StoryManager.Flag.BunkerMazeEntered)) HideHallway();
		if (s.HasFlag(StoryManager.Flag.BunkerMazeExited)) _maze.MarkExited();
	}

	// ------------------------------------------------------------------ per frame

	public void Tick(double delta)
	{
		_clock += delta;
		var player = Player;
		bool near = player != null && player.GlobalPosition.DistanceTo(_interior.GlobalPosition) < 500f;
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
		Cutscene.Run(this, async ct =>
		{
			try
			{
				if (Fader != null) await Fader.Fade(1f, 0.8f);
				ct.ThrowIfCancellationRequested();
				SetIndoor(true);
				if (maze)
				{
					// Restored mid-maze: straight back in, no second caption.
					EnterMaze();
					if (exited) { _maze.MarkExited(); SpawnWalkie(); }
					var at = exited ? BunkerMaze.ExitLocal + new Vector3(0, 0, 1.2f) : BunkerMaze.StartLocal;
					player.Teleport(_interior.ToGlobal(at), 0f);
					Atmosphere?.SetMood(ForestAtmosphere.Mood.Menacing, 1f);
				}
				else
				{
					// The hallway floor only spans Z 0..-90: land just inside it.
					player.Teleport(_interior.ToGlobal(HallSpawn), 0f);
					Atmosphere?.SetMood(ForestAtmosphere.Mood.Night, 1f);
				}
				await Cutscene.Frame(this, ct);
				if (Fader != null) await Fader.Fade(0f, 1.0f);
			}
			finally { IsAdmitting = false; }
		}, lockInput: true, freezeBody: true);
	}

	// ------------------------------------------------------------------ Act 9: the screens

	private void RunCrtSequence()
	{
		Cutscene.Run(this, async ct =>
		{
			_crt.TurnAllOff();
			if (_crt.Target != null)
				BunkerKit.OneShot(_crt.Target, "res://assets/audio/sfx/camera_shutter.wav", Vector3.Up * 0.3f, "Events", 0f, 0.6f, 2f, 15f);
			await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 1.5 : 5.0, ct);
			_crt.TurnOnStairs(3.5f);
			if (Fader != null)
				await Fader.ShowCaption("", "Every screen shows the same thing: the stairs, in the woods.", 1.2f, 3.0f, 1.2f);
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
		Cutscene.Run(this, async ct =>
		{
			if (Fader != null) await Fader.Fade(1f, 1.4f);
			ct.ThrowIfCancellationRequested();
			EnterMaze();
			player.Teleport(_interior.ToGlobal(BunkerMaze.StartLocal), 0f);
			StoryManager.Instance?.SetFlag(StoryManager.Flag.BunkerMazeEntered);
			Atmosphere?.SetMood(ForestAtmosphere.Mood.Menacing, 2f);
			await Cutscene.Frame(this, ct);
			if (Fader != null)
			{
				await Fader.Fade(0f, 1.4f);
				await Fader.ShowCaption("", "\"...come and see...\"", 1.0f, 2.4f, 1.0f);
			}
			GD.Print("[story] Act 10: the hallway has become a maze");
		}, lockInput: true, freezeBody: true);
	}

	/// <summary>The hallway is gone; the maze wakes (lamps, glimpses, the choir).</summary>
	private void EnterMaze()
	{
		_mazeTriggered = true;
		HideHallway();
		if (!_maze.Exited) _maze.Active = true;
		StartChoir();
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
		Cutscene.Run(this, async ct =>
		{
			if (Fader != null) await Fader.ShowCaption("", "The corridor ends where it began.", 1.0f, 2.4f, 1.0f);
		});
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
