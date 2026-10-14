using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Sits on the footbridge deck. Once the newel post is in hand (the Act 5 → 6
/// handoff), crossing the creek advances the story and repoints the compass to
/// the Act 6 clearing. Crossing earlier or later does nothing — it only
/// reacts during that specific window, so the bridge stays a normal part of
/// the trail the rest of the time.
///
/// Any way over counts (Dan, 2026-10-13: events must fire "even if character
/// doesnt cross the bridge"): the deck's volume catches the planks walked, and
/// <see cref="Footbridge.IsAcross"/> catches a player who wades the creek
/// beside it, jumps it, goes round, or is already over the water when the cap
/// is taken. Checked a few times a second while the window is open.
/// </summary>
public partial class BridgeCrossEvent : StoryTrigger
{
	/// <summary>The bridge whose creek this is (set by <see cref="Footbridge"/>).</summary>
	public Footbridge Bridge;

	private double _poll;

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act6BridgeCrossed;
	protected override bool CanFire(StoryManager s, PlayerController p) => s is { NewelPostTaken: true, Current: Checkpoint.Act5CabinEntered };

	public override void _Process(double delta)
	{
		if (Fired) { SetProcess(false); return; }
		if ((_poll -= delta) > 0) return;
		_poll = 0.1;
		if (StoryManager.Instance is not { NewelPostTaken: true, Current: Checkpoint.Act5CabinEntered }) return;
		if (Bridge == null || !IsInstanceValid(Bridge) || StoryBeat.Player(this) is not { } p) return;
		if (Bridge.IsAcross(p.GlobalPosition)) TryFire(p);
	}

	protected override void Fire(PlayerController player)
	{
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act6BridgeCrossed);
		GD.Print($"[story] Act 6: the bridge is crossed{(Bridge != null && StoryBeat.PlayerInside(this) == null ? " (over the creek, not by the deck)" : "")}");
	}
}
