using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 5's break-in: at the boarded cabin door, an axe chops through quickly;
/// a hammer takes a slower, hold-to-pry mini-game. Either way the door opens,
/// the tool is used up, and the player can finally step inside.
///
/// An <see cref="Interactable"/> on the door: look at the boards and press E
/// (axe) or hold E (hammer, <see cref="HammerSeconds"/>). Also owns restoring
/// the door on Continue: boarded from Act 2 until it was broken open.
///
/// Act 5 begins after the giant (Act 4): until <see cref="StoryManager.Flag.GiantEventDone"/>
/// the boards cannot be broken even with a tool in hand, and the prompt says why
/// in the plainest terms. Prompts never carry the key hint; the HUD adds it.
/// </summary>
public partial class DoorBreakEvent : Interactable
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float AxeSeconds = 2.2f;
	[Export] public float HammerSeconds = 9f;

	private Cabin _cabin;
	private PlayerInventory _inventory;
	private bool _busy;

	public override void _Ready()
	{
		_cabin = GetNode<Cabin>(CabinPath);
		// The pick volume sits over the door itself, a little proud of its collider.
		PickOffset = _cabin.ToLocal(_cabin.DoorCenter) - Position;
		PickRadius = 0.9f;
		MaxDistance = 3f;
		// Highlight the planks nailed over the door (resolved when focused, since they are rebuilt).
		HighlightRoot = new NodePath(GetPathTo(_cabin) + "/Generated/Planks");
		base._Ready();
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
		if (_inventory != null && IsInstanceValid(_inventory)) _inventory.ToolChanged -= UpdateHold;
	}

	/// <summary>Continue: put the door back the way the story left it.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (StoryBeat.CabinDoorOpen(s)) _cabin.SetOpen(true);   // no sound or animation
		else if (s is { StairsClimbed: true }) _cabin.SetBoarded(true);
		UpdateEnabled();
		// The hold time depends only on what is carried, so it follows the inventory, not the prompt.
		_inventory = StoryBeat.Player(this)?.Inventory;
		if (_inventory != null) _inventory.ToolChanged += UpdateHold;
		UpdateHold();
	}

	private void OnCheckpoint(Checkpoint _) => UpdateEnabled();

	private void UpdateEnabled() => Enabled = _cabin.DoorBoarded && !_cabin.IsOpen;

	private void UpdateHold() => HoldSeconds = ToolFor(_inventory) == ToolKind.Hammer ? HammerSeconds : 0f;

	private static bool GiantSeen => StoryManager.Instance is { GiantEventDone: true };

	public override bool CanInteract(PlayerController player)
	{
		if (!base.CanInteract(player) || _busy || !GiantSeen) return false;
		return ToolFor(player.Inventory) != ToolKind.None;
	}

	public override string GetPrompt(PlayerController player)
	{
		if (_busy) return "Chopping through the boards...";
		var tool = ToolFor(player.Inventory);
		if (tool == ToolKind.None) return "The door is boarded shut.";
		if (!GiantSeen) return "Boarded shut. Find him first.";
		return tool == ToolKind.Axe
			? "Chop the boards with the axe"
			: HoldProgress > 0f ? $"Prying the boards loose... {HoldProgress * 100f:0}%" : "Pry the boards loose";
	}

	public override void Interact(PlayerController player)
	{
		var inv = player.Inventory;
		if (inv == null || _busy || !GiantSeen) return;
		var tool = ToolFor(inv);
		if (tool == ToolKind.None) return;
		_busy = true;
		bool axe = tool == ToolKind.Axe;
		_ = Cutscene.Run(this, async ct =>
		{
			if (axe) await Cutscene.Wait(this, AxeSeconds, ct);
			inv.Consume(tool);
			_cabin.OpenDoor();
			Enabled = false;
			StoryManager.Instance?.SetFlag(StoryManager.Flag.CabinDoorOpen);
			base.Interact(player);
			await StoryBeat.Caption(this, "The door gives way.", 1.0f, 1.8f, 1.0f, ct);
		});
	}

	/// <summary>The axe if carried (quick), else the hammer (the slow pry), else nothing.</summary>
	private static ToolKind ToolFor(PlayerInventory inv)
		=> inv == null ? ToolKind.None : inv.HasTool(ToolKind.Axe) ? ToolKind.Axe : inv.HasTool(ToolKind.Hammer) ? ToolKind.Hammer : ToolKind.None;
}
