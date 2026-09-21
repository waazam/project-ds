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
/// </summary>
public partial class DoorBreakEvent : Interactable
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float AxeSeconds = 2.2f;
	[Export] public float HammerSeconds = 9f;

	private Cabin _cabin;
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
	}

	/// <summary>Continue: put the door back the way the story left it.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (StoryBeat.CabinDoorOpen(s)) _cabin.SetOpen(true);   // no sound or animation
		else if (s is { StairsClimbed: true }) _cabin.SetBoarded(true);
		UpdateEnabled();
	}

	private void OnCheckpoint(Checkpoint _) => UpdateEnabled();

	private void UpdateEnabled() => Enabled = _cabin.DoorBoarded && !_cabin.IsOpen;

	public override bool CanInteract(PlayerController player)
	{
		if (!base.CanInteract(player) || _busy) return false;
		return ToolFor(player) != ToolKind.None;
	}

	public override string GetPrompt(PlayerController player)
	{
		if (_busy) return "Chopping through the boards...";
		switch (ToolFor(player))
		{
			case ToolKind.Axe:
				HoldSeconds = 0f;
				return "[E] Chop the boards with the axe";
			case ToolKind.Hammer:
				HoldSeconds = HammerSeconds;
				return HoldProgress > 0f ? $"Prying the boards loose... {HoldProgress * 100f:0}%" : "[Hold E] Pry the boards loose";
			default:
				HoldSeconds = 0f;
				return "The door is boarded shut.";
		}
	}

	public override void Interact(PlayerController player)
	{
		var inv = Inventory(player);
		if (inv == null || _busy) return;
		_busy = true;
		var tool = ToolFor(player);
		if (tool == ToolKind.None) return;
		bool axe = tool == ToolKind.Axe;
		Cutscene.Run(this, async ct =>
		{
			if (axe) await Cutscene.Wait(this, AxeSeconds, ct);
			inv.Consume(tool);
			_cabin.OpenDoor();
			Enabled = false;
			StoryManager.Instance?.SetFlag(StoryManager.Flag.CabinDoorOpen);
			base.Interact(player);
			await StoryBeat.Caption(this, "The door gives way.", 1.0f, 1.8f, 1.0f);
		});
	}

	/// <summary>The axe if carried (quick), else the hammer (the slow pry), else nothing.</summary>
	private static ToolKind ToolFor(PlayerController p)
	{
		var inv = Inventory(p);
		return inv == null ? ToolKind.None : inv.HasTool(ToolKind.Axe) ? ToolKind.Axe : inv.HasTool(ToolKind.Hammer) ? ToolKind.Hammer : ToolKind.None;
	}

	private static PlayerInventory Inventory(PlayerController p) => p.GetNodeOrNull<PlayerInventory>("Inventory");
}
