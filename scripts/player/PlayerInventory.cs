using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

public enum ToolKind { None, Lantern, Compass, Axe, Key, Hammer, Camera, NewelPost, Radio, Knife, Lighter, DeadEye, PaleHand, StairTread, Bookmark, FontKey, Chalice, Keycard202, PantryKey, Keycard203, DiningKey, Keycard201, Crowbar, Flamethrower }

/// <summary>
/// One inventory, with no hands-full limit. Lantern, Compass, Camera, NewelPost and Radio are gear (picking them up
/// turns systems on); Axe, Key, Hammer and Knife are puzzle tools, any number carried at once. Axe/Key/Hammer are gone
/// once <see cref="Consume"/> is called by the puzzle that used it; the Knife is never consumed (Act 13 reuses it for
/// every cut).
/// <para>(2026-10-13j, the owner: "make items and inventory scrolling actually work and switch between items, allow for
/// lantern to be in left hand and other items to be in right") Two hands. The lantern is the LEFT hand's, always, on F as
/// before. The RIGHT hand holds one thing, <see cref="InHand"/>: one of the <see cref="IsHoldable"/> things carried (the
/// camera, the axe, the hammer, the cap, the walkie, the knife, the lighter, the crowbar, the flamethrower) or nothing.
/// The wheel, the bumpers and 1-9 choose it (<see cref="PlayerHands"/>); a holdable pickup comes to hand; the rest (keys,
/// cards, the compass, the station's three things) are pocketed, and the hand shows them only as they're used
/// (<see cref="HeldUse"/>). Puzzle tools still work from the pocket: walking up to the boarded door with the axe anywhere
/// about you still cuts it free. What's in hand is saved with the rest (";hand=Axe").</para>
/// </summary>
public partial class PlayerInventory : Node
{
	[Signal] public delegate void ToolChangedEventHandler();
	/// <summary>The right hand's thing changed (<see cref="InHand"/>).</summary>
	[Signal] public delegate void HandChangedEventHandler();

	/// <summary>Puzzle tools carried (axe, key, hammer). Any number at once; each is used up when used for its job.</summary>
	private readonly List<ToolKind> _tools = new();
	public bool HasTool(ToolKind kind) => _tools.Contains(kind);
	public IReadOnlyList<ToolKind> Tools => _tools;
	public bool HasLantern { get; private set; }
	public bool HasCompass { get; private set; }
	public bool HasCamera { get; private set; }
	public bool HasNewelPost { get; private set; }
	public bool HasRadio { get; private set; }

	/// <summary>What the right hand holds (None: nothing, the hand down out of view).</summary>
	public ToolKind InHand { get; private set; } = ToolKind.None;

	/// <summary>The things that go in the right hand, in the order the story hands them over (the wheel's order, and 1-9's).</summary>
	public static readonly ToolKind[] HoldOrder = { ToolKind.Camera, ToolKind.Axe, ToolKind.Hammer, ToolKind.NewelPost, ToolKind.Radio, ToolKind.Knife, ToolKind.Lighter, ToolKind.Crowbar, ToolKind.Flamethrower };
	public static bool IsHoldable(ToolKind k) => System.Array.IndexOf(HoldOrder, k) >= 0;

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

	/// <summary>Compact save form, e.g. "lantern,compass;tools=Axe+Key;hand=Axe".</summary>
	public string Serialize()
	{
		var gear = new List<string>();
		if (HasLantern) gear.Add("lantern");
		if (HasCompass) gear.Add("compass");
		if (HasCamera) gear.Add("camera");
		if (HasNewelPost) gear.Add("newel_post");
		if (HasRadio) gear.Add("radio");
		return string.Join(",", gear) + ";tools=" + string.Join("+", _tools) + ";hand=" + InHand;
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
		// the right hand: as saved; an older save (no "hand=") has the camera in it until the lantern's taken, then nothing
		string hand = parts.Length >= 3 && parts[2].StartsWith("hand=") ? parts[2][5..] : null;
		InHand = hand != null && System.Enum.TryParse(hand, out ToolKind h) && Has(h) && (h == ToolKind.None || IsHoldable(h)) ? h
			: hand == null && HasCamera && !HasLantern ? ToolKind.Camera : ToolKind.None;
		EmitSignal(SignalName.ToolChanged);
		EmitSignal(SignalName.HandChanged);
	}

	/// <summary>Carried now (gear or tool; None, the empty hand, always).</summary>
	public bool Has(ToolKind kind) => kind switch
	{
		ToolKind.None => true,
		ToolKind.Lantern => HasLantern,
		ToolKind.Compass => HasCompass,
		ToolKind.Camera => HasCamera,
		ToolKind.NewelPost => HasNewelPost,
		ToolKind.Radio => HasRadio,
		_ => _tools.Contains(kind),
	};

	/// <summary>What can go in the right hand now, in the wheel's order: nothing first, then each holdable carried.</summary>
	public List<ToolKind> HandChoices()
	{
		var list = new List<ToolKind> { ToolKind.None };
		foreach (var k in HoldOrder) if (Has(k)) list.Add(k);
		return list;
	}

	/// <summary>Puts <paramref name="kind"/> in the right hand (None: empties it). False if it isn't carried or can't be held.
	/// <see cref="PlayerHands"/> and <see cref="HeldTool"/> play the change (the old thing lowered, the new one raised);
	/// this only says what's wanted.</summary>
	public bool Select(ToolKind kind)
	{
		if (kind != ToolKind.None && (!IsHoldable(kind) || !Has(kind))) return false;
		if (InHand == kind) return true;
		InHand = kind;
		EmitSignal(SignalName.HandChanged);
		return true;
	}

	/// <summary>The next (or previous) choice round the wheel from what's in hand.</summary>
	public ToolKind Step(int dir)
	{
		var list = HandChoices();
		int i = list.IndexOf(InHand);
		i = (((i < 0 ? 0 : i) + dir) % list.Count + list.Count) % list.Count;
		return list[i];
	}

	/// <summary>Adds the item; a thing for the right hand comes to it. There is no hands-full limit.</summary>
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
		// a new thing for the right hand comes to it; the lantern (the left hand's) puts the camera away, so the right
		// mouse gathers its beam (the wheel brings the camera back)
		if (IsHoldable(kind)) Select(kind);
		else if (kind == ToolKind.Lantern && InHand == ToolKind.Camera) Select(ToolKind.None);
		return true;
	}

	/// <summary>A tool used for its job is gone.</summary>
	public void Consume(ToolKind kind)
	{
		if (_tools.Remove(kind)) Changed();
	}

	/// <summary>Takes the camera away. It stays with the player all game; only Act 25's endings take it (EndingDirector: the camera lifted back into the trunk).</summary>
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

	/// <summary>Everything currently carried, in pickup-independent display order.</summary>
	public IReadOnlyList<(ToolKind Kind, string Label)> OwnedItems()
	{
		var list = new List<(ToolKind, string)>();
		if (HasCamera) list.Add((ToolKind.Camera, "Camera"));
		if (HasLantern) list.Add((ToolKind.Lantern, "Lantern"));
		if (HasCompass) list.Add((ToolKind.Compass, "Compass"));
		if (HasNewelPost) list.Add((ToolKind.NewelPost, Label(ToolKind.NewelPost)));
		if (HasRadio) list.Add((ToolKind.Radio, Label(ToolKind.Radio)));
		foreach (var t in _tools) list.Add((t, Label(t)));
		return list;
	}

	public static string Label(ToolKind t) => t switch
	{
		ToolKind.None => "Empty Hand",
		ToolKind.NewelPost => "The Cap",
		ToolKind.Radio => "Walkie-Talkie",
		ToolKind.DeadEye => "The Eye",
		ToolKind.PaleHand => "The Hand",
		ToolKind.StairTread => "The Step",
		ToolKind.Bookmark => "Bookmark",
		ToolKind.FontKey => "Iron Key",
		ToolKind.Chalice => "Chalice",
		ToolKind.Keycard202 => "Keycard 202",
		ToolKind.PantryKey => "Pantry Key",
		ToolKind.Keycard203 => "Keycard 203",
		ToolKind.DiningKey => "Dining Room Key",
		ToolKind.Keycard201 => "Keycard 201",
		ToolKind.Flamethrower => "Flamethrower",
		_ => t.ToString(),
	};

	/// <summary>Any change tells the HUD to redraw; a thing that's gone from the pack leaves the hand empty.</summary>
	private void Changed(ToolKind? _ = null)
	{
		EmitSignal(SignalName.ToolChanged);
		if (InHand != ToolKind.None && !Has(InHand)) { InHand = ToolKind.None; EmitSignal(SignalName.HandChanged); }
	}
}
