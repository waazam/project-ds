using Godot;

namespace ProjectDS.Player;

public enum ToolKind { None, Lantern, Compass, Axe, Key, Hammer }

/// <summary>
/// The player carries one tool at a time. Lantern and Compass are equipped
/// gear (picking them up turns systems on and is never consumed); Axe, Key
/// and Hammer are single-use and clear back to None once <see cref="Consume"/>
/// is called by whatever puzzle used them.
/// </summary>
public partial class PlayerInventory : Node
{
	[Signal] public delegate void ToolChangedEventHandler();

	public ToolKind CurrentTool { get; private set; } = ToolKind.None;
	public bool HasLantern { get; private set; }
	public bool HasCompass { get; private set; }

	/// <summary>False if a hands-full tool is already held (lantern/compass don't occupy the hands).</summary>
	public bool TryPickup(ToolKind kind)
	{
		if (kind == ToolKind.Lantern) { HasLantern = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (kind == ToolKind.Compass) { HasCompass = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (CurrentTool != ToolKind.None) return false;
		CurrentTool = kind;
		EmitSignal(SignalName.ToolChanged);
		return true;
	}

	public void Consume()
	{
		CurrentTool = ToolKind.None;
		EmitSignal(SignalName.ToolChanged);
	}
}
