using Godot;

namespace ProjectDS.UI;

/// <summary>(2026-10-13) The key on an action as the player has bound it (<see cref="Systems.KeyRebinds"/>), for the hints'
/// text: "B", or whatever they chose. Falls back to <paramref name="fallback"/> if the action has no key.</summary>
public static class KeyHint
{
	public static string Of(string action, string fallback)
	{
		var key = Systems.KeyRebinds.KeyOf(action);
		if (key == Key.None) return fallback;
		string s = OS.GetKeycodeString(key);
		return string.IsNullOrEmpty(s) ? fallback : s;
	}

	public static string Blacklight => Of("lantern_mode", "B");
	public static string Lantern => Of("flashlight_toggle", "F");
}
