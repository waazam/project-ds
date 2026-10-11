using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>A key in the settings: shows the action's key; pressed, it waits for the next key (Escape keeps the old one).</summary>
public partial class RebindButton : Button
{
	public string Action;
	private bool _listening;

	public override void _Ready()
	{
		Refresh();
		Pressed += () => { _listening = true; Text = "press a key"; };
		FocusExited += () => { if (_listening) { _listening = false; Refresh(); } };
	}

	private void Refresh() => Text = OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(KeyRebinds.KeyOf(Action)));

	public override void _Input(InputEvent e)
	{
		if (!_listening || e is not InputEventKey { Pressed: true, Echo: false } k) return;
		GetViewport().SetInputAsHandled();
		_listening = false;
		if (k.PhysicalKeycode != Key.Escape) { KeyRebinds.Bind(Action, k.PhysicalKeycode); GameSettings.Instance?.Save(); }
		foreach (var b in GetTree().GetNodesInGroup("rebind_buttons")) if (b is RebindButton rb) rb.Refresh();
	}

	public override void _EnterTree() => AddToGroup("rebind_buttons");
}
