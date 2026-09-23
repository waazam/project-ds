using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 5's payoff, inside the cabin: the friend is not there. What he left is
/// (his chair pushed back, the bandage, the stains, his page, the newel post on
/// the table). Fires once the player is inside and marks the checkpoint (the newel
/// post and his page show from it); no line. The moment they are in: three quick
/// knocks on the wall outside, and the door slamming shut right behind them.
///
/// Gated so it can only happen as the story intends: after the boarded-door
/// checkpoint (Act 3) and before this one, with the door actually broken open,
/// and with the player standing inside the cabin's walls.
/// </summary>
public partial class FriendReveal : StoryTrigger
{
	/// <summary>How far inside the walls the player must be (metres from each wall).</summary>
	[Export] public float InsideMargin = 0.15f;

	private Cabin _cabin;

	public override void _Ready()
	{
		base._Ready();
		for (Node n = GetParent(); n != null && _cabin == null; n = n.GetParent()) _cabin = n as Cabin;
		_cabin ??= StoryBeat.Cabin(this);
	}

	/// <summary>Unused since 2026-09-22 (no self-talk); kept for references.</summary>
	public const string Line = "He's not here.";

	protected override bool RecheckWhileInside => true;

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act5CabinEntered;

	protected override bool CanFire(StoryManager s, PlayerController p)
		=> s.Current >= Checkpoint.Act3DoorBoarded && s.Current < Checkpoint.Act5CabinEntered
			&& _cabin is { IsOpen: true } && PlayerInsideCabin(p);

	private bool PlayerInsideCabin(PlayerController p)
	{
		Vector3 local = _cabin.ToLocal(p.GlobalPosition);
		return Mathf.Abs(local.X) < _cabin.Width * 0.5f - InsideMargin && Mathf.Abs(local.Z) < _cabin.Depth * 0.5f - InsideMargin;
	}

	public const string KnockFlag = "cabin_wall_knocked";
	/// <summary>For tests: the knocking on the wall has played.</summary>
	public bool Knocked { get; private set; }
	/// <summary>Seconds after stepping in before the first knock.</summary>
	[Export] public float KnockDelay = 0.35f;
	/// <summary>Gap between the three quick knocks.</summary>
	[Export] public float KnockGap = 0.22f;
	/// <summary>Level of each pound on the wall (hot: this is the scare).</summary>
	[Export] public float PoundDb = 6f;   // non-positional now

	private void PlayFlat(string path, float db, float pitch = 1f)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, PitchScale = pitch };
		GetTree().CurrentScene.AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}
	/// <summary>Seconds after the last knock before the door slams.</summary>
	[Export] public float SlamDelay = 0.3f;

	protected override void Fire(PlayerController player)
	{
		// No line (Dan, 2026-09-22: no talking to himself); the checkpoint is the beat.
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act5CabinEntered);
		if (StoryManager.Instance is { } s && !s.HasFlag(KnockFlag)) Cutscene.Run(this, KnockAndSlam);
		// The storm breaks the moment the cap is taken (Dan, 2026-09-22: the rain must be over while still inside),
		// not on stepping out; CabinExitLine's later call is then a no-op.
		if (StoryManager.Instance is { } sm && !sm.NewelPostTaken) sm.FlagSet += OnCapTaken;
	}

	private void OnCapTaken(string flag)
	{
		if (flag != StoryManager.Flag.NewelPostTaken) return;
		if (StoryManager.Instance is { } s)
		{
			s.FlagSet -= OnCapTaken;
			StormController.Instance?.Deactivate(4f);
			if (!s.HasFlag(StoryManager.Flag.DawnBroke)) s.SetFlag(StoryManager.Flag.DawnBroke);
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (StoryManager.Instance is { } s) s.FlagSet -= OnCapTaken;
	}

	/// <summary>
	/// The moment they step into the room (Dan, 2026-09-22): three quick knocks on the outside of the
	/// wall beside the table, as if with a knuckle, from the far side of the logs, and 0.3 s after the
	/// last one the door slams shut behind them (E opens it again). Once only (saved).
	/// </summary>
	private async Task KnockAndSlam(CancellationToken ct)
	{
		if (_cabin == null || StoryManager.Instance is not { } s || s.HasFlag(KnockFlag)) return;
		s.SetFlag(KnockFlag);
		Knocked = true;
		// The wall nearest the table (or the right-hand wall if there is no table), just outside the logs, at knuckle height.
		var table = _cabin.FindChild("Table", true, false) as Node3D;
		Vector3 local = table != null ? _cabin.ToLocal(table.GlobalPosition) : new Vector3(1f, 0f, 0f);
		float side = local.X >= 0f ? 1f : -1f;
		Vector3 at = new(side * (_cabin.Width * 0.5f + 0.3f), 1.3f, local.Z);
		await Cutscene.Wait(this, KnockDelay, ct);
		var player = StoryBeat.Player(this);
		for (int i = 0; i < 3; i++)
		{
			// Three heavy pounds (Dan, 2026-09-22: dramatic and scary): a fist on the logs, hot, each with a small
			// jolt of the view, and the last one a shade harder.
			int take = (int)(GD.Randi() % 3) + 1;
			// Flat at the ear (no 3D falloff) with a low thump under each (Dan: way louder).
			PlayFlat($"res://assets/audio/sfx/wall_pound_{take:00}.wav", PoundDb + (i == 2 ? 1.5f : 0f), (float)GD.RandRange(0.9, 1.0));
			PlayFlat("res://assets/audio/sfx/steel_door_slam_02.wav", PoundDb - 14f, 0.8f);
			player?.PlayerInput?.AddCutsceneLook(new Vector2((float)GD.RandRange(-0.02, 0.02), (float)GD.RandRange(0.015, 0.03)));
			if (i < 2) await Cutscene.Wait(this, KnockGap, ct);
		}
		GD.Print("[story] Act 5: three pounds on the wall outside");
		await Cutscene.Wait(this, SlamDelay, ct);
		_cabin.SlamShut();
		GD.Print("[story] Act 5: the door slams shut");
	}
}
