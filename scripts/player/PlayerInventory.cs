using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

public enum ToolKind { None, Lantern, Compass, Axe, Key, Hammer, Camera, NewelPost, Radio, Knife, Lighter, DeadEye, PaleHand, StairTread, Bookmark }

/// <summary>
/// One inventory, all of it usable at any time, with no selecting and no hands-full
/// limit. Lantern, Compass, Camera, NewelPost and Radio are gear (picking them up
/// turns systems on); Axe, Key, Hammer and Knife are puzzle tools, any number carried
/// at once. Axe/Key/Hammer are gone once <see cref="Consume"/> is called by the
/// puzzle that used it; the Knife is never consumed (Act 13 reuses it for every cut).
/// </summary>
public partial class PlayerInventory : Node
{
	[Signal] public delegate void ToolChangedEventHandler();

	/// <summary>Puzzle tools carried (axe, key, hammer). Any number at once; each is used up when used for its job.</summary>
	private readonly List<ToolKind> _tools = new();
	public bool HasTool(ToolKind kind) => _tools.Contains(kind);
	public IReadOnlyList<ToolKind> Tools => _tools;
	public bool HasLantern { get; private set; }
	public bool HasCompass { get; private set; }
	public bool HasCamera { get; private set; }
	public bool HasNewelPost { get; private set; }
	public bool HasRadio { get; private set; }

	public override void _Ready()
	{
		// Continue: put back exactly what the player was carrying at the save.
		var story = Systems.StoryManager.Instance;
		if (story?.PendingInventory is string saved)
		{
			Restore(saved);
			story.ClearPendingInventory();
		}
	}

	/// <summary>Compact save form, e.g. "lantern,compass;tools=Axe+Key".</summary>
	public string Serialize()
	{
		var gear = new List<string>();
		if (HasLantern) gear.Add("lantern");
		if (HasCompass) gear.Add("compass");
		if (HasCamera) gear.Add("camera");
		if (HasNewelPost) gear.Add("newel_post");
		if (HasRadio) gear.Add("radio");
		return string.Join(",", gear) + ";tools=" + string.Join("+", _tools);
	}

	public void Restore(string saved)
	{
		var parts = (saved ?? "").Split(';');
		var gear = new HashSet<string>(parts[0].Split(',', System.StringSplitOptions.RemoveEmptyEntries));
		HasLantern = gear.Contains("lantern");
		HasCompass = gear.Contains("compass");
		// the camera stays with the player the whole game once they have it (older saves, from before it
		// did, had it taken away at the first stairs: give it back)
		HasCamera = gear.Contains("camera") || (Systems.StoryManager.Instance?.Current ?? Systems.Checkpoint.None) >= Systems.Checkpoint.Act2StairsClimbed;
		HasNewelPost = gear.Contains("newel_post");
		HasRadio = gear.Contains("radio");
		_tools.Clear();
		// "tools=Axe+Key", or an older save's single "tool=Axe".
		string list = parts.Length < 2 ? "" : parts[1].StartsWith("tools=") ? parts[1][6..] : parts[1].StartsWith("tool=") ? parts[1][5..] : "";
		foreach (var name in list.Split('+', System.StringSplitOptions.RemoveEmptyEntries))
			if (System.Enum.TryParse(name, out ToolKind t) && t != ToolKind.None && !_tools.Contains(t)) _tools.Add(t);
		EmitSignal(SignalName.ToolChanged);
	}

	/// <summary>Adds the item. Everything carried is usable at once; there is no hands-full limit.</summary>
	public bool TryPickup(ToolKind kind)
	{
		switch (kind)
		{
			case ToolKind.Lantern: HasLantern = true; break;
			case ToolKind.Compass: HasCompass = true; break;
			case ToolKind.Camera: HasCamera = true; break;
			case ToolKind.NewelPost: HasNewelPost = true; break;
			case ToolKind.Radio: HasRadio = true; break;
			default:
				if (kind == ToolKind.None) return false;
				if (!_tools.Contains(kind)) _tools.Add(kind);
				break;
		}
		Changed(kind);
		return true;
	}

	/// <summary>A tool used for its job is gone.</summary>
	public void Consume(ToolKind kind)
	{
		if (_tools.Remove(kind)) Changed();
	}

	/// <summary>Takes the camera away (no longer used by the story: it stays with the player all game).</summary>
	public void TakeAwayCamera()
	{
		if (!HasCamera) return;
		HasCamera = false;
		Changed();
	}

	/// <summary>The newel post flies out of the player's hands and fuses onto a staircase in the Act 6 clearing.</summary>
	public void ConsumeNewelPost()
	{
		if (!HasNewelPost) return;
		HasNewelPost = false;
		Changed();
	}

	/// <summary>Everything currently carried, in pickup-independent display order, for the inventory HUD. All of it is usable at any time.</summary>
	public IReadOnlyList<(ToolKind Kind, string Label)> OwnedItems()
	{
		var list = new List<(ToolKind, string)>();
		if (HasCamera) list.Add((ToolKind.Camera, "Camera"));
		if (HasLantern) list.Add((ToolKind.Lantern, "Lantern"));
		if (HasCompass) list.Add((ToolKind.Compass, "Compass"));
		if (HasNewelPost) list.Add((ToolKind.NewelPost, "Newel Post"));
		if (HasRadio) list.Add((ToolKind.Radio, "Radio"));
		foreach (var t in _tools) list.Add((t, Label(t)));
		return list;
	}

	private static string Label(ToolKind t) => t switch
	{
		ToolKind.DeadEye => "The Eye",
		ToolKind.PaleHand => "The Hand",
		ToolKind.StairTread => "The Step",
		ToolKind.Bookmark => "Bookmark",
		_ => t.ToString(),
	};

	/// <summary>Everything is usable at once (no selecting): any change just tells the HUD to redraw.</summary>
	private void Changed(ToolKind? _ = null) => EmitSignal(SignalName.ToolChanged);
}
