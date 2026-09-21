using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

public enum ToolKind { None, Lantern, Compass, Axe, Key, Hammer, Camera, NewelPost, Radio }

/// <summary>
/// The player carries one tool at a time. Lantern, Compass, Camera and NewelPost
/// are equipped gear (picking them up turns systems on and is never consumed by
/// the single-slot rule); Axe, Key and Hammer are single-use and clear back to
/// None once <see cref="Consume"/> is called by whatever puzzle used them.
/// </summary>
public partial class PlayerInventory : Node
{
	[Signal] public delegate void ToolChangedEventHandler();

	public ToolKind CurrentTool { get; private set; } = ToolKind.None;
	public bool HasLantern { get; private set; }
	public bool HasCompass { get; private set; }
	public bool HasCamera { get; private set; }
	public bool HasNewelPost { get; private set; }
	public bool HasRadio { get; private set; }

	/// <summary>False if a hands-full tool is already held (equipped gear doesn't occupy the hands).</summary>
	public bool TryPickup(ToolKind kind)
	{
		if (kind == ToolKind.Lantern) { HasLantern = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (kind == ToolKind.Compass) { HasCompass = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (kind == ToolKind.Camera) { HasCamera = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (kind == ToolKind.NewelPost) { HasNewelPost = true; EmitSignal(SignalName.ToolChanged); return true; }
		if (kind == ToolKind.Radio) { HasRadio = true; EmitSignal(SignalName.ToolChanged); return true; }
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

	/// <summary>The camera is put away for good once the first stairs take over (Act 2); it has no further use.</summary>
	public void TakeAwayCamera()
	{
		if (!HasCamera) return;
		HasCamera = false;
		EmitSignal(SignalName.ToolChanged);
	}

	/// <summary>The newel post flies out of the player's hands and fuses onto a staircase in the Act 6 clearing.</summary>
	public void ConsumeNewelPost()
	{
		if (!HasNewelPost) return;
		HasNewelPost = false;
		EmitSignal(SignalName.ToolChanged);
	}

	/// <summary>Everything currently carried, in pickup-independent display order, for the equipped-item HUD.</summary>
	public IReadOnlyList<(ToolKind Kind, string Label)> OwnedItems()
	{
		var list = new List<(ToolKind, string)>();
		if (HasCamera) list.Add((ToolKind.Camera, "Camera"));
		if (HasLantern) list.Add((ToolKind.Lantern, "Lantern"));
		if (HasCompass) list.Add((ToolKind.Compass, "Compass"));
		if (HasNewelPost) list.Add((ToolKind.NewelPost, "Newel Post"));
		if (HasRadio) list.Add((ToolKind.Radio, "Radio"));
		if (CurrentTool != ToolKind.None) list.Add((CurrentTool, CurrentTool.ToString()));
		return list;
	}

	/// <summary>Which of <see cref="OwnedItems"/> the scroll wheel has landed on (clamped, not wrapped-and-stored,
	/// so a lost item never leaves the index pointing past the end).</summary>
	public int SelectedIndex { get; private set; }

	public void CycleSelected(int direction)
	{
		int count = OwnedItems().Count;
		if (count == 0) { SelectedIndex = 0; return; }
		SelectedIndex = ((SelectedIndex + direction) % count + count) % count;
		EmitSignal(SignalName.ToolChanged);
	}

	public (ToolKind Kind, string Label)? SelectedItem
	{
		get
		{
			var items = OwnedItems();
			if (items.Count == 0) return null;
			return items[Mathf.Clamp(SelectedIndex, 0, items.Count - 1)];
		}
	}
}
