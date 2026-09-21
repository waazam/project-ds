using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Lives on the cabin. Once the player has the lantern and compass (picked up
/// after the door is boarded) and steps outside this radius, the storm
/// begins. One-way: records <see cref="StoryManager.Flag.StormStarted"/>, from
/// which the storm restores itself on Continue.
///
/// A sphere trigger (no per-frame polling): leaving it, or picking up the last
/// piece of gear while already outside it, starts the storm.
/// </summary>
public partial class SafeZoneWatcher : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 16f;

	private Node3D _cabin;
	private Area3D _zone;
	private PlayerInventory _watchedInv;
	private bool _fired;

	public override void _Ready()
	{
		_cabin = GetNode<Node3D>(CabinPath);
		Callable.From(Restore).CallDeferred();
	}

	public override void _ExitTree()
	{
		if (_watchedInv != null && IsInstanceValid(_watchedInv)) _watchedInv.ToolChanged -= OnGearChanged;
	}

	private void Restore()
	{
		// Deferred: the cabin (our parent) is still readying its children during _Ready.
		// Tall cylinder: only horizontal distance matters, as before.
		_zone = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = Radius, Height = 80f }, Vector3.Zero, null, "SafeZone");
		_zone.BodyExited += b => { if (b is PlayerController p) TryStart(p, justLeft: true); };
		_fired = StoryManager.Instance?.HasFlag(StoryManager.Flag.StormStarted) ?? false;
		if (_fired) return;
		// Gear picked up while already outside the zone also counts.
		_watchedInv = StoryBeat.Player(this)?.GetNodeOrNull<PlayerInventory>("Inventory");
		if (_watchedInv != null) _watchedInv.ToolChanged += OnGearChanged;
	}

	private void OnGearChanged()
	{
		if (StoryBeat.Player(this) is { } p) TryStart(p, justLeft: false);
	}

	private void TryStart(PlayerController player, bool justLeft)
	{
		if (_fired || StoryManager.Instance is not { Current: >= Checkpoint.Act3DoorBoarded }) return;
		if (player.GetNodeOrNull<PlayerInventory>("Inventory") is not { HasLantern: true, HasCompass: true }) return;
		if (!justLeft && StoryBeat.PlayerInside(_zone) != null) return;
		_fired = true;
		StormController.Instance?.Activate();
		StoryManager.Instance.SetFlag(StoryManager.Flag.StormStarted);
	}
}
