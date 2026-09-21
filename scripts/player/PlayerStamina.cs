using Godot;

namespace ProjectDS.Player;

/// <summary>
/// A running gas tank. Drains while actually running (held key and moving),
/// refills faster at rest than on the move, and — once fully spent — locks
/// running out again until it has climbed back to <see cref="MinToResumeRun"/>,
/// so sprint can't be feathered on and off one frame at a time at empty.
/// Applies to scripted input too: the autotest manages stamina like a player would.
/// </summary>
public partial class PlayerStamina : Node
{
	[Export] public float DrainPerSecond = 0.11f;
	[Export] public float RegenPerSecondMoving = 0.16f;
	[Export] public float RegenPerSecondResting = 0.22f;
	[Export] public float MinToResumeRun = 0.16f;

	public float Value { get; private set; } = 1f;
	public bool CanRun { get; private set; } = true;

	private PlayerController _player;

	public override void _Ready() => _player = GetParent<PlayerController>();

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		// Debug builds (running from the editor/project): unlimited stamina for playtesting.
		// Not during the autotest, which checks that running drains it.
		if (OS.IsDebugBuild() && !(Systems.GameSettings.Instance?.AutoTest ?? false))
		{
			Value = 1f;
			CanRun = true;
			return;
		}
		if (_player.IsRunning)
		{
			Value = Mathf.Max(0f, Value - DrainPerSecond * dt);
		}
		else
		{
			float regen = _player.GroundSpeed < 0.15f ? RegenPerSecondResting : RegenPerSecondMoving;
			Value = Mathf.Min(1f, Value + regen * dt);
		}

		if (Value <= 0f) CanRun = false;
		else if (Value >= MinToResumeRun) CanRun = true;
	}
}
