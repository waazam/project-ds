using System.Collections.Generic;
using Godot;

namespace ProjectDS.UI;

/// <summary>(2026-10-13) The key on an action as the player has bound it (<see cref="Systems.KeyRebinds"/>), for the hints'
/// text: "B", or whatever they chose. Falls back to <paramref name="fallback"/> if the action has no key.
/// (2026-10-15) Once a pad is in use (<see cref="HintStyle.UsingPad"/>), the pad's button instead (its bindings are
/// fixed: GameSettings' input map).</summary>
public static class KeyHint
{
	private static readonly Dictionary<string, string> Pad = new()
	{
		["interact"] = "A", ["flashlight_toggle"] = "Y", ["photo"] = "X", ["photo_log"] = "View", ["lantern_mode"] = "D-pad down",
		["lean_left"] = "D-pad left", ["lean_right"] = "D-pad right", ["crouch"] = "RS", ["throw"] = "B", ["hold_breath"] = "D-pad up",
		["item_next"] = "RB", ["item_prev"] = "LB", ["focus"] = "RT", ["focus_lens"] = "LT", ["run"] = "LT", ["wheel"] = "LB / RB",
	};

	public static string Of(string action, string fallback)
	{
		if (HintStyle.UsingPad && Pad.TryGetValue(action, out var pad)) return pad;
		var key = Systems.KeyRebinds.KeyOf(action);
		if (key == Key.None) return fallback;
		string s = OS.GetKeycodeString(key);
		return string.IsNullOrEmpty(s) ? fallback : s;
	}

	public static string Blacklight => Of("lantern_mode", "B");
	public static string Lantern => Of("flashlight_toggle", "F");
	public static string Use => Of("interact", "E");

	// a how-to line's keys as written (the defaults) and what they stand for
	private static readonly (string Written, string Action)[] Written =
	{
		("F", "flashlight_toggle"), ("B", "lantern_mode"), ("Right mouse", "focus"), ("Left mouse", "photo"), ("Wheel", "wheel"), ("Tab", "photo_log"),
		("Space", "focus_lens"),
	};

	/// <summary>A how-to line ("F: lantern on and off.   B: its blacklight.   Right mouse: ...") with each lead key as the
	/// player has it now: their rebinding, or the pad's button.</summary>
	public static string ForDevice(string line)
	{
		if (string.IsNullOrEmpty(line)) return line;
		var segs = line.Split("   ");
		for (int i = 0; i < segs.Length; i++)
			foreach (var (w, action) in Written)
				if (segs[i].StartsWith(w + ":"))
				{
					segs[i] = Of(action, w) + segs[i][w.Length..];
					break;
				}
		return string.Join("   ", segs);
	}
}
