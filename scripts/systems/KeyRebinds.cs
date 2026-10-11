using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Keys the player chooses (2026-10-11, accessibility for the Steam release: "rebindable keys"). Each of the game's
/// keyboard actions can be given another key in the settings (UI.RebindButton); the pad's buttons and the mouse's stay as
/// they are. Kept in the settings file under [keys].
/// </summary>
public static class KeyRebinds
{
	public static readonly (string Action, string Label)[] Actions =
	{
		("move_forward", "Forward"), ("move_back", "Back"), ("move_left", "Left"), ("move_right", "Right"),
		("run", "Run"), ("crouch", "Crouch"), ("interact", "Use"), ("flashlight_toggle", "Lantern"),
		("lantern_mode", "Blacklight"), ("photo_log", "Album"), ("hold_breath", "Hold breath"), ("throw", "Throw"),
		("lean_left", "Lean left"), ("lean_right", "Lean right"),
	};

	/// <summary>The key on an action now (its first keyboard one), or None.</summary>
	public static Key KeyOf(string action)
	{
		if (!InputMap.HasAction(action)) return Key.None;
		foreach (var e in InputMap.ActionGetEvents(action))
			if (e is InputEventKey k) return k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
		return Key.None;
	}

	/// <summary>Puts <paramref name="key"/> on <paramref name="action"/> in place of its keyboard keys (any other action
	/// that had it gives it up).</summary>
	public static void Bind(string action, Key key)
	{
		if (!InputMap.HasAction(action) || key == Key.None) return;
		foreach (var (other, _) in Actions)
		{
			if (other == action || !InputMap.HasAction(other)) continue;
			foreach (var e in InputMap.ActionGetEvents(other))
				if (e is InputEventKey k && (k.PhysicalKeycode == key || k.Keycode == key)) InputMap.ActionEraseEvent(other, e);
		}
		foreach (var e in InputMap.ActionGetEvents(action))
			if (e is InputEventKey) InputMap.ActionEraseEvent(action, e);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}

	public static void SaveTo(ConfigFile cfg)
	{
		foreach (var (a, _) in Actions) cfg.SetValue("keys", a, (long)KeyOf(a));
	}

	public static void LoadFrom(ConfigFile cfg)
	{
		foreach (var (a, _) in Actions)
		{
			long k = (long)cfg.GetValue("keys", a, 0L);
			if (k != 0 && (Key)k != KeyOf(a)) Bind(a, (Key)k);
		}
	}
}
