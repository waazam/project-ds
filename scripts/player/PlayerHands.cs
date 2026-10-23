using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;

namespace ProjectDS.Player;

/// <summary>
/// The two hands (2026-10-13j, the owner: "make items and inventory scrolling actually work and switch between items,
/// allow for lantern to be in left hand and other items to be in right").
/// <list type="bullet">
/// <item><b>The left hand</b> is the lantern's (<see cref="HeldLantern"/>), always: F lights it and puts it out, B its
/// blacklight, as before.</item>
/// <item><b>The right hand</b> holds what's chosen (<see cref="PlayerInventory.InHand"/>, drawn by <see cref="HeldTool"/>):
/// the mouse wheel and the bumpers (LB back, RB on) go round what's carried that a hand can hold, an empty hand among them;
/// 1-9 pick by place (the one in hand again puts it away). The old thing is lowered out of view and the new one raised,
/// with a rustle and the grip's sound; the HUD names it (<see cref="UI.EquippedItemHud"/>).</item>
/// <item><b>Right mouse</b> is the right hand's: with the camera in it, it raises the camera to the eye; with anything
/// else (or nothing), it gathers the lantern's beam.</item>
/// <item><b>Both hands:</b> the flamethrower takes both (its wand's two grips), so the lantern hangs at the belt while it's
/// out (its light still with you); a burning flare, a tin to throw and a tool being used take the right hand for their
/// moment, the chosen thing lowered meanwhile. Rowing (Act 12) lowers the right hand's thing; the oars' hands are the
/// boat's.</item>
/// <item><b>No switching</b> while the story has the controls (a cutscene, a note, the pause menu), while the camera is at
/// the eye (the wheel zooms it), the album is open (it turns the pages), a flare or a tin is in the hand, a tool is in use,
/// in the boat, or while a beat holds the hands (<see cref="Hold"/>). A beat that puts something in the hand
/// (<see cref="PlayerInventory.Select"/>: Act 24's flamethrower) still does.</item>
/// </list>
/// </summary>
public partial class PlayerHands : Node
{
	public static PlayerHands Instance { get; private set; }

	private PlayerController _player;
	private PlayerInventory _inv;
	private HeldTool _tool;
	private World.LakeCrossingEvent _lake;
	private Node _lakeScene;
	private static readonly HashSet<object> _holds = new();

	/// <summary>Switches made (for tests).</summary>
	public int Switches { get; private set; }

	/// <summary>A beat keeps the hands as they are until <see cref="Release"/> with the same key.</summary>
	public static void Hold(object key) { if (key != null) _holds.Add(key); }
	public static void Release(object key) { if (key != null) _holds.Remove(key); }

	/// <summary>The right hand's thing as shown (null before the player's camera is up).</summary>
	public HeldTool Tool => _tool;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_inv = _player.Inventory;
		Callable.From(() => { if (_player.CameraRig?.Camera != null) _tool = HeldItem.Attach<HeldTool>(_player); }).CallDeferred();
	}

	/// <summary>In the rowboat (Act 12), its oars in both hands.</summary>
	public bool Rowing
	{
		get
		{
			var scene = GetTree().CurrentScene;
			if (scene != _lakeScene || (_lake != null && !IsInstanceValid(_lake))) { _lakeScene = scene; _lake = scene?.GetNodeOrNull<World.LakeCrossingEvent>("LakeCrossing") ?? scene?.GetNodeOrNull<World.LakeCrossingEvent>("HollowWorld/LakeCrossing"); }   // (2026-10-23: it lives in the Hollow's world; looked for at the root only, it was never found)
			return _lake != null && IsInstanceValid(_lake) && _lake.Boarded && !_lake.Landed;
		}
	}

	/// <summary>Both hands on the flamethrower's wand: the lantern hangs at the belt.</summary>
	public static bool BothHandsOnWand(PlayerController p) => p?.Inventory is { InHand: ToolKind.Flamethrower };

	private bool TinInHand => _player.CameraRig?.Camera?.GetNodeOrNull<HeldProp>(nameof(HeldProp)) is { Has: true };

	/// <summary>The right hand is free for what's chosen (no flare, tin or tool in use in it; not rowing).</summary>
	public bool RightFree => !HeldFlare.AnyLit && !TinInHand && !HeldUse.Busy && !Rowing;

	/// <summary>The camera's in the right hand and up in view (so right mouse raises it).</summary>
	public static bool CameraReady(PlayerController p) => p?.Inventory is { InHand: ToolKind.Camera }
		&& (Instance?._tool is not { } t || !IsInstanceValid(t) || t.Ready(ToolKind.Camera));

	/// <summary>Right mouse gathers the lantern's beam (the camera isn't in the right hand, the lantern's in the left).</summary>
	public static bool LanternCanGather(PlayerController p) => p?.Inventory is { } inv && inv.InHand != ToolKind.Camera && !BothHandsOnWand(p);

	/// <summary>Why switching is shut now (null if it's open).</summary>
	public string Blocked()
	{
		var input = _player.PlayerInput;
		if (input == null || !input.IsLive) return "no control";
		if (_holds.Count > 0) return "held by the story";
		if (_player.GetNodeOrNull<CameraTool>("CameraTool") is { Raising: true }) return "camera up";
		if (GetTree().GetFirstNodeInGroup("photo_log_page") is UI.PhotoLogPage { IsOpen: true }) return "album open";
		if (HeldFlare.AnyLit) return "a flare in hand";
		if (TinInHand) return "a tin in hand";
		if (HeldUse.Busy) return "a tool in use";
		if (Rowing) return "rowing";
		if (Flamethrower.Instance is { Firing: true }) return "firing";
		return null;
	}

	public override void _Process(double delta)
	{
		var input = _player.PlayerInput;
		if (_inv == null || input == null) return;
		int dir = input.ItemNextPressed ? 1 : input.ItemPrevPressed ? -1 : 0;
		int slot = input.SlotPressed;
		if (dir == 0 && slot == 0) return;
		if (Blocked() != null) return;
		var choices = _inv.HandChoices();
		if (choices.Count < 2) return;
		ToolKind want;
		if (slot > 0)
		{
			if (slot >= choices.Count) return;
			want = choices[slot] == _inv.InHand ? ToolKind.None : choices[slot];
		}
		else want = _inv.Step(dir);
		SwitchTo(want);
	}

	/// <summary>Puts <paramref name="kind"/> in the right hand, the way the player does (tests call it too).</summary>
	public bool SwitchTo(ToolKind kind)
	{
		if (_inv == null || kind == _inv.InHand) return false;
		if (!_inv.Select(kind)) return false;
		Switches++;
		GD.Print($"[hands] right hand: {kind}");
		return true;
	}

	// ── the sounds of a switch: a rustle of the sleeve as the old thing goes down, the grip as the new one comes up; never
	// the same take twice running (the owner's rule: no repetitive sounds) ──
	private static readonly Dictionary<string, int> _lastTake = new();
	private static readonly RandomNumberGenerator _rng = new();

	public static void Sound(Node from, string name, int variants, float db)
	{
		if (from?.GetTree()?.CurrentScene is not { } root) return;
		int last = _lastTake.TryGetValue(name, out var l) ? l : 0;
		int pick = _rng.RandiRange(1, variants);
		if (variants > 1 && pick == last) pick = pick % variants + 1;
		_lastTake[name] = pick;
		string path = $"res://assets/audio/sfx/{name}_{pick:00}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Player", VolumeDb = db, PitchScale = _rng.RandfRange(0.93f, 1.07f) };
		root.AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}
}
