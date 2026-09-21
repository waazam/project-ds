using Godot;
using ProjectDS.Player;

namespace ProjectDS.Systems;

/// <summary>
/// Base for a one-shot story beat fired by the player entering an Area3D.
///
/// Subclasses answer three questions:
/// - <see cref="AlreadyHappened"/>: has the saved story already passed this beat? Checked once,
///   deferred from _Ready, so a Continue never replays it (the restore contract).
/// - <see cref="CanFire"/>: is the story in the right state for it right now?
/// - <see cref="Fire"/>: play it (usually <c>Cutscene.Run</c>).
///
/// If the player is already standing inside when the story changes (a checkpoint or flag
/// lands), the trigger re-checks, so a condition that becomes true mid-overlap still fires.
/// </summary>
public partial class StoryTrigger : Area3D
{
	/// <summary>True once the beat has fired (or the saved story is already past it).</summary>
	public bool Fired { get; protected set; }

	/// <summary>Re-check every physics frame while the player stands inside (for conditions that
	/// depend on where exactly they are, like "inside the cabin"). Off by default.</summary>
	protected virtual bool RecheckWhileInside => false;

	private PlayerController _inside;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += b => { if (ReferenceEquals(b, _inside)) { _inside = null; SetPhysicsProcess(false); } };
		SetPhysicsProcess(false);
		Callable.From(Restore).CallDeferred();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Fired || _inside == null || !IsInstanceValid(_inside)) { SetPhysicsProcess(false); return; }
		TryFire(_inside);
	}

	public override void _EnterTree()
	{
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached += OnStoryChanged;
			s.FlagSet += OnStoryChanged;
		}
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached -= OnStoryChanged;
			s.FlagSet -= OnStoryChanged;
		}
	}

	protected virtual bool AlreadyHappened(StoryManager s) => false;
	protected virtual bool CanFire(StoryManager s, PlayerController player) => true;
	protected virtual void Fire(PlayerController player) { }
	/// <summary>Called once after load with the saved story (restore any state the beat left behind).</summary>
	protected virtual void OnRestored(StoryManager s) { }

	private void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null) return;
		if (AlreadyHappened(s)) Fired = true;
		OnRestored(s);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController p) return;
		TryFire(p);
		if (!Fired && RecheckWhileInside) { _inside = p; SetPhysicsProcess(true); }
	}

	private void OnStoryChanged<T>(T _) => Recheck();

	/// <summary>Fires if the player is inside right now and the story allows it.</summary>
	protected void Recheck()
	{
		if (Fired || !IsInsideTree()) return;
		var p = StoryBeat.PlayerInside(this);
		if (p != null) TryFire(p);
	}

	protected void TryFire(PlayerController player)
	{
		if (Fired || StoryManager.Instance is not { } s || !CanFire(s, player)) return;
		Fired = true;
		Fire(player);
	}
}
