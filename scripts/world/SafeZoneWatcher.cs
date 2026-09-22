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
///
/// It also says the one line of Act 3 (<see cref="StepsLine"/>): the first time
/// the lantern and the compass are both in hand, a moment after the pickup's own
/// feedback, once ever (saved as <see cref="StepsLineFlag"/>). Never on a Continue
/// that already has both.
/// </summary>
public partial class SafeZoneWatcher : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 16f;
	[Export] public string StepsLine = "He's not here. He said the steps.";
	/// <summary>Seconds after the second piece of gear is taken before the line shows.</summary>
	[Export] public float StepsLineDelay = 1.6f;

	/// <summary>Set once the Act 3 line has played (or would have: both items owned at load).</summary>
	public const string StepsLineFlag = "line_he_said_the_steps";

	private Node3D _cabin;
	private Area3D _zone;
	private PlayerInventory _watchedInv;
	private bool _fired;
	private bool _lineDone;

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
		var s = StoryManager.Instance;
		_fired = s?.HasFlag(StoryManager.Flag.StormStarted) ?? false;
		// PlayerInventory restores its saved gear in its own _Ready, before this runs: a Continue
		// with both already in hand is past the moment the line belongs to.
		var inv = StoryBeat.Player(this)?.GetNodeOrNull<PlayerInventory>("Inventory");
		_lineDone = (s?.HasFlag(StepsLineFlag) ?? false) || inv is { HasLantern: true, HasCompass: true };
		if (_fired && _lineDone) return;
		// Gear picked up while already outside the zone also counts.
		_watchedInv = inv;
		if (_watchedInv != null) _watchedInv.ToolChanged += OnGearChanged;
	}

	private void OnGearChanged()
	{
		if (StoryBeat.Player(this) is not { } p) return;
		TryStart(p, justLeft: false);
		TryLine(p);
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

	/// <summary>Act 3, P13: both pieces of gear in hand for the first time. The compass points at the stairs; this is why the player follows it.</summary>
	private void TryLine(PlayerController player)
	{
		if (_lineDone || StoryManager.Instance is not { Current: >= Checkpoint.Act3DoorBoarded } s) return;
		if (player.GetNodeOrNull<PlayerInventory>("Inventory") is not { HasLantern: true, HasCompass: true }) return;
		_lineDone = true;
		s.SetFlag(StepsLineFlag);
		Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, StepsLineDelay, ct);
			await StoryBeat.Caption(this, StepsLine, 0.8f, 3.0f, 1.0f);
		});
	}
}
